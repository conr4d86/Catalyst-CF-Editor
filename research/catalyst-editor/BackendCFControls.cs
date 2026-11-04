using System;using System.IO;using System.Linq;using System.Security.Cryptography;using System.Threading;
namespace CFEditor {
sealed partial class NativeBackend {
 SdkMidiTap midiTap;AssetMidiExchange preampExchange;PreampJournal preampJournal;PreampClient preampClient;PreampSnapshot preampSnapshot;volatile bool preampReady,preampModelSupported;volatile int preampNamSlot=-1;readonly bool[] cfNamesRead=new bool[2],cfNamesHaveAsset=new bool[2];
 bool SelectedNAMConfirmed{get{int slot=preampNamSlot;return preampModelSupported&&slot>=0&&slot<2&&cfNamesRead[slot]&&cfNamesHaveAsset[slot];}}
 bool SelectedPreampImplemented{get{return SelectedNAMConfirmed||preampModelSupported&&preampClient!=null&&preampClient.AllAmpPreamp;}}
 internal bool ExtendedPreampAvailable{get{return Connected&&preampReady&&SelectedPreampImplemented&&preampSnapshot!=null;}}
 static bool PreampAmpModel(string model){return new[]{"Clean","Chime","Boutique","Crunch","Dynamic","Hi Gain","Clean 2","Chime 2","Boutique 2","Crunch 2","Dynamic 2","Hi Gain 2"}.Contains(model);}
 static uint ControlNonce(){using(var rng=RandomNumberGenerator.Create()){var b=new byte[4];uint n;do{rng.GetBytes(b);n=BitConverter.ToUInt32(b,0);}while(n==0||n>uint.MaxValue-4096);return n;}}
 void PrepareCFControls(){try{midiTap=SdkMidiTap.Install(Path.Combine(EditorRuntime.Root,"APIV3EngineDLL.dll"));}catch(Exception error){if(SdkMidiTap.HasOwner)throw;Report("Controles estándar disponibles. Preamp adicional: "+error.Message);}}
 void StartCFControls(){Array.Clear(cfNamesRead,0,cfNamesRead.Length);Array.Clear(cfNamesHaveAsset,0,cfNamesHaveAsset.Length);Receive("CFAssets","FirmwareID","");Receive("CFAssets","FirmwareEdition","");Receive("CFAssets","ReceiverKnown",false);Receive("CFAssets","UpgradeRequired",false);Receive("CFAssets","NAM1Known",false);Receive("CFAssets","NAM2Known",false);if(midiTap==null)return;try{
  preampExchange=new AssetMidiExchange(midiTap.Bind((uint)product));string identity=Pump(()=>AssetDeviceIdentity.Read(preampExchange,(uint)product,CancellationToken.None,true).GetAwaiter().GetResult());
  string firmwareID,edition;if(!FirmwareIdentifier.PublicLabel(identity,out firmwareID)||!FirmwareIdentifier.FullEdition(identity,out edition))throw new IOException("El Custom Firmware no devuelve un identificador compatible.");Receive("CFAssets","FirmwareID",firmwareID);Receive("CFAssets","FirmwareEdition",edition);
  preampJournal=new PreampJournal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CatalystCFEditor","control-journals"),(uint)product,identity);
  preampClient=new PreampClient(preampExchange,(uint)product,preampJournal);Pump(()=>{preampClient.Connect(ControlNonce(),CancellationToken.None).GetAwaiter().GetResult();return true;});preampReady=true;Receive("CFAssets","ReceiverKnown",true);ReadCFPreamp();ReadCFAssetNames();
 }catch(Exception error){preampReady=preampClient!=null&&preampClient.Ready;preampSnapshot=null;if(error is FirmwareUpgradeRequiredException){Receive("CFAssets","UpgradeRequired",true);Report(error.Message);}else Report("Edición estándar conectada. Controles CF adicionales no disponibles: "+error.Message);}}
 void PublishSelectedPreamp(){if(preampSnapshot!=null)PublishCFPreamp(SelectedPreampImplemented?preampSnapshot:new PreampSnapshot(preampSnapshot.Index,preampSnapshot.Revision,new PreampSettings()));}
 void ReadCFPreamp(){if(!preampReady||preampClient==null)return;try{preampSnapshot=Pump(()=>preampClient.Read(CancellationToken.None).GetAwaiter().GetResult());PublishSelectedPreamp();}catch{preampSnapshot=null;throw;}}
 void ReadCFAssetNames(){if(!preampReady||preampClient==null)return;for(byte slot=3;slot<5;slot++){if(cfNamesRead[slot-3])continue;try{var receipt=Pump(()=>preampClient.ReadAsset(slot,CancellationToken.None).GetAwaiter().GetResult());string name="NAM"+(slot-2);bool known=receipt==null||receipt.Generation!=0;cfNamesHaveAsset[slot-3]=receipt!=null&&receipt.Generation!=0&&receipt.Info!=null;cfNamesRead[slot-3]=known;Receive("CFAssets",name+"Known",known);Receive("CFAssets",name,receipt==null||receipt.Info==null?"":receipt.Info.Name);}catch(Exception error){cfNamesHaveAsset[slot-3]=false;Receive("CFAssets","NAM"+(slot-2)+"Known",false);Report("No se pudo consultar el nombre de NAM "+(slot-2)+": "+error.Message);}}PublishSelectedPreamp();}
 void PublishCFPreamp(PreampSnapshot snapshot){var s=snapshot.Settings;Receive("CFPreamp","type",(int)s.Type);Receive("CFPreamp","tone",(int)Math.Round(s.Tone*100));Receive("CFPreamp","level",(int)Math.Round(s.Level*50));Receive("CFPreamp","boost",s.Boost);Receive("CFPreamp","bright",s.Bright);}
 void RequireCFContext(PreampSnapshot snapshot){int bank=Convert.ToInt32(Call("getIntParamValue","systemState","currentAmpBank")),channel=Convert.ToInt32(Call("getIntParamValue","systemState","currentAmpChannel"));int index=channel==0?12:bank*2+channel-1;
  if(bank<0||bank>=6||channel<0||channel>2||snapshot==null||snapshot.Index!=index)throw new IOException("Ha cambiado el preset. Vuelve a leer sus ajustes antes de editar el preamp.");}
 void SetCFPreamp(Binding binding,object value){if(!ExtendedPreampAvailable||preampClient==null)throw new IOException("Este firmware no ha confirmado los controles adicionales de preamp.");
  if(binding.AssetID!=null&&!string.Equals(Convert.ToString(Call("getStringParamValue",binding.Group=="CFPreamp"?"CatalystGroupAmp":binding.Group,"graphSlot_AssetEngineID")),binding.AssetID,StringComparison.Ordinal))throw new IOException("Ha cambiado el modelo; vuelve a leer sus ajustes.");
  // Native Boost amount/enable remain authoritative. They are not sent twice.
  var expected=preampSnapshot;var current=Pump(()=>preampClient.Read(CancellationToken.None).GetAwaiter().GetResult());RequireCFContext(current);if(expected==null||expected.Index!=current.Index||expected.Revision!=current.Revision)throw new IOException("Los ajustes han cambiado; vuelve a leer el preset antes de enviar el control.");var s=current.Settings;
  switch(binding.Param.id){case "type":s.Type=Convert.ToUInt32(value);break;case "tone":s.Tone=Convert.ToSingle(value)/100;break;case "level":s.Level=Convert.ToSingle(value)/50;break;case "boost":s.Boost=Convert.ToBoolean(value);break;case "bright":s.Bright=Convert.ToBoolean(value);break;default:throw new ArgumentException("Control de preamp no reconocido.");}
  preampSnapshot=Pump(()=>preampClient.Set(current,s,CancellationToken.None).GetAwaiter().GetResult());PublishCFPreamp(preampSnapshot);
 }
 PreampSnapshot CaptureCFPreamp(){if(!ExtendedPreampAvailable)return null;if(preampClient.HasPending){preampSnapshot=Pump(()=>preampClient.Reconcile(CancellationToken.None).GetAwaiter().GetResult());PublishCFPreamp(preampSnapshot);throw new IOException("Guardado anterior del preamp confirmado. Revisa el preset antes de guardar otros cambios.");}
  var expected=preampSnapshot;var current=Pump(()=>preampClient.Read(CancellationToken.None).GetAwaiter().GetResult());RequireCFContext(current);if(expected==null||expected.Index!=current.Index||expected.Revision!=current.Revision)throw new IOException("El preset ha cambiado; revisa los ajustes antes de guardarlos.");string model=Convert.ToString(Call("getStringParamValue","CatalystGroupAmp","graphSlot_AssetEngineID"));var s=current.Settings;s.Drive=Convert.ToSingle(Call("getIntParamValue","CatalystGroupAmp","Boost"))/255f;s.Enabled=Convert.ToInt32(Call("getIntParamValue","CatalystGroupAmp","BoostEnable"))!=0;s.Encode();return new PreampSnapshot(current.Index,current.Revision,s,model);
 }
 void RequireSaveCFContext(PreampSnapshot captured,PreampSnapshot current){RequireCFContext(current);if(current.Index!=captured.Index)throw new IOException("Ha cambiado el preset durante el guardado. No se han enviado los ajustes de preamp a otro destino.");if(string.IsNullOrEmpty(captured.AssetID)||!string.Equals(captured.AssetID,Convert.ToString(Call("getStringParamValue","CatalystGroupAmp","graphSlot_AssetEngineID")),StringComparison.Ordinal))throw new IOException("Ha cambiado el modelo durante el guardado. No se han enviado los ajustes de otro preamp.");}
 void SaveCFPreamp(PreampSnapshot captured){if(captured==null)return;var settings=captured.Settings;PreampSnapshot current=null;
  // Native save may reload the old sidecar. Reapply the captured edit AFTER
  // the native receipt, then bind the sidecar to the resulting native file CRC.
  // A reload can finish between READ and SET. Refresh only after an explicit
  // BUSY/revision rejection, only in this post-save path, with a bounded retry.
  // Never repeat the native preset commit or an uncertain persistent SAVE.
  for(int attempt=0;attempt<4;attempt++){
   current=Pump(()=>preampClient.Read(CancellationToken.None).GetAwaiter().GetResult());RequireSaveCFContext(captured,current);
   try{current=Pump(()=>preampClient.Set(current,settings,CancellationToken.None).GetAwaiter().GetResult());break;}
   catch(PreampSetRejectedException error){if(attempt==3||error.Index!=captured.Index||error.Status!=1&&error.Revision==current.Revision)throw;Thread.Sleep(25);}
  }
  RequireSaveCFContext(captured,current);preampSnapshot=Pump(()=>preampClient.Save(current,CancellationToken.None).GetAwaiter().GetResult());PublishCFPreamp(preampSnapshot);
 }
 void CloseCFControls(){preampReady=false;preampClient=null;preampSnapshot=null;if(preampExchange!=null){preampExchange.Dispose();preampExchange=null;}if(midiTap!=null){Pump(()=>{midiTap.Remove().GetAwaiter().GetResult();return true;});midiTap=null;}if(preampJournal!=null){preampJournal.Dispose();preampJournal=null;}}
}
}
