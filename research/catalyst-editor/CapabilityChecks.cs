#if DEVELOPMENT
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;
namespace CFEditor {
// Managed codec/owner doubles only: these tests never create a native port.
static class CapabilityChecks {
 sealed class ControlsJournal:IPreampJournal {public PreampPendingSave Pending;public int Writes;public PreampPendingSave Load(){return Pending;}public void Save(PreampPendingSave value){Pending=value;Writes++;}}
 sealed class DataJournal:IAssetPendingJournal {
  public uint Product{get{return 2424833;}}public uint Session{get{return 42;}}public int Writes;readonly AssetPendingTransfer[] pending=new AssetPendingTransfer[5];
  public DataJournal(){pending[4]=new AssetPendingTransfer(4,900,7772,19,new AssetInfo{Name="Pending NAM"}.Encode(),null);}
  public AssetPendingTransfer[] Load(){return (AssetPendingTransfer[])pending.Clone();}public void Save(uint session,AssetPendingTransfer[] values){Writes++;}
 }
 sealed class Receiver:IAssetExchange {
  public uint Flags;public readonly List<byte> Opcodes=new List<byte>();public Receiver(uint flags){Flags=flags;}
  static void Put(byte[] b,int p,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,p,4);}
  public Task<byte[]> Exchange(byte[] packet,int timeout,CancellationToken cancel){cancel.ThrowIfCancellationRequested();var q=AssetWire.Decode(packet);Opcodes.Add(q.Opcode);byte[] data;
   if(q.Opcode==1){data=new byte[20];Put(data,4,Flags);Put(data,8,256);Put(data,12,5);Put(data,16,2);}
   else if(q.Opcode==9){data=new byte[44];Put(data,4,11);Put(data,8,1);Buffer.BlockCopy(new PreampSettings().Encode(),0,data,12,32);}
   else if(q.Opcode==5){data=new byte[8];Put(data,0,7);}
   else throw new Exception("Capability gate allowed an unexpected request: "+q.Opcode);
   return Task.FromResult(AssetWire.Encode(new AssetMessage{Product=q.Product,Session=q.Opcode==1?q.Transaction:q.Session,Transaction=q.Transaction,Number=q.Number,Slot=q.Slot,Opcode=(byte)(q.Opcode|64),Data=data}));
  }
 }
 static void Need(bool value,string text){if(!value)throw new Exception(text);}
 static async Task<Exception> Refused(Task action){try{await action.ConfigureAwait(false);}catch(Exception error){return error;}throw new Exception("Incompatible firmware was accepted");}
 static async Task<List<string>> Check(){
  var results=new List<string>();Need(AssetWire.KnownCapabilities==63&&AssetWire.SafeControlOwner==32,"Capability bit assignment changed");
  foreach(uint flags in new[]{29u,64u,125u,49u}){
   var controlsReceiver=new Receiver(flags);var controlsJournal=new ControlsJournal();var controls=new PreampClient(controlsReceiver,2424833,controlsJournal);
   var error=await Refused(controls.Connect(100,CancellationToken.None));Need(flags!=29||error is FirmwareUpgradeRequiredException,"Unsafe owner has no clear update instruction");Need(!controls.Ready&&!controls.AllAmpPreamp&&controlsReceiver.Opcodes.SequenceEqual(new byte[]{1}),"Unsafe control HELLO reached READ or became ready");
   await Refused(controls.Read(CancellationToken.None));var expected=new PreampSnapshot(11,1,new PreampSettings());await Refused(controls.Set(expected,new PreampSettings{Type=3},CancellationToken.None));await Refused(controls.Save(expected,CancellationToken.None));
   Need(controlsReceiver.Opcodes.Count==1&&controlsJournal.Writes==0,"Rejected controls emitted a later request or changed a journal");
   var dataReceiver=new Receiver(flags);var dataJournal=new DataJournal();var data=new AssetTransfer(dataReceiver,2424833,dataJournal);error=await Refused(data.Connect(200,CancellationToken.None));Need(flags!=29||error is FirmwareUpgradeRequiredException,"Unsafe data owner has no update instruction");
   var payload=new byte[7772];Buffer.BlockCopy(BitConverter.GetBytes(.125f),0,payload,0,4);await Refused(data.ReadSlot(4,201,CancellationToken.None));await Refused(data.Upload(3,202,payload,new AssetInfo{Name="Never sent"},null,CancellationToken.None));await Refused(data.Reconcile(4,203,CancellationToken.None));
   bool adoptRefused=false;try{data.RequireCompatibleReceiver();}catch(InvalidOperationException){adoptRefused=true;}Need(adoptRefused&&!data.Ready&&data.HasPending(4)&&dataReceiver.Opcodes.SequenceEqual(new byte[]{1})&&dataJournal.Writes==0,"Rejected data owner scanned, uploaded, adopted or changed a pending journal");
  }results.Add("Unsafe flags29, unknown64/125 and incomplete49 stop at HELLO; no control READ/SET/SAVE, asset STATUS/BEGIN/COMMIT or journal mutation");
  foreach(uint flags in new[]{13u,61u,63u}){
   var receiver=new Receiver(flags);var client=new PreampClient(receiver,2424833,new ControlsJournal());await client.Connect(300,CancellationToken.None);Need(client.Ready&&client.AllAmpPreamp==((flags&16)!=0)&&receiver.Opcodes.SequenceEqual(new byte[]{1,9}),"Safe/legacy controls were rejected or read before negotiation");
   var dataReceiver=new Receiver(flags);var data=new AssetTransfer(dataReceiver,2424833);await data.Connect(400,CancellationToken.None);Need(data.Capabilities==flags&&data.Ready,"Validated data flags were not retained");Need(await data.ReadSlot(4,401,CancellationToken.None)==null&&dataReceiver.Opcodes.SequenceEqual(new byte[]{1,5}),"Safe data owner cannot read its target receipt");
  }results.Add("Corrected all-model flags61/63 accepted; stable NAM-only flags13 retain control and data support");
  var reconnectReceiver=new Receiver(61);var reconnect=new PreampClient(reconnectReceiver,2424833,new ControlsJournal());await reconnect.Connect(500,CancellationToken.None);reconnectReceiver.Flags=29;await Refused(reconnect.Connect(501,CancellationToken.None));await Refused(reconnect.Read(CancellationToken.None));Need(!reconnect.Ready&&!reconnect.AllAmpPreamp&&reconnectReceiver.Opcodes.SequenceEqual(new byte[]{1,9,1}),"Failed renegotiation retained an earlier safe session");results.Add("Unsafe renegotiation clears the previous safe control session before any later request");
  return results;
 }
 public static int Run(string path){try{var results=Check().GetAwaiter().GetResult();Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));File.WriteAllText(path,new JavaScriptSerializer().Serialize(new{passed=true,USB_opened=false,SDK_loaded=false,device_enumeration=false,native_driver_called=false,checks=results}));return 0;}catch(Exception error){File.WriteAllText(path,new JavaScriptSerializer().Serialize(new{passed=false,USB_opened=false,error=error.ToString()}));return 1;}}
}
}
#endif
