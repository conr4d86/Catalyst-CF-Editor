using System;using System.IO;using System.Linq;using System.Threading.Tasks;using System.Windows.Forms;
namespace CFEditor {
sealed partial class AssetDataPanel {
 readonly AssetLabel initialTitle=new AssetLabel(),initialDescription=new AssetLabel();bool initialMode,initialContextUnreadable;CFInstallRecord initialRecord;string initialRoot;int initialProduct;
 Func<AssetPreparation[],Action<string>,Task<CFInstallRecord>> installInitial;Func<AssetPreparation[],Task<CFPreparedPackage>> buildInitial;Func<bool> initialAllowed;
 internal bool InitialMode{get{return initialMode;}}
 void BuildInitialAction(){initialTitle.Font=FirmwareText.Title;initialDescription.Font=FirmwareText.Bold;initialDescription.ForeColor=UITheme.Success;initialDescription.Wrap=true;foreach(Control item in new Control[]{initialTitle,initialDescription}){item.Visible=false;card.Controls.Add(item);}}
 internal void ConfigureInitial(Func<AssetPreparation[],Task<CFPreparedPackage>> builder,Func<AssetPreparation[],Action<string>,Task<CFInstallRecord>> installer,Func<bool> allowed){buildInitial=builder;installInitial=installer;initialAllowed=allowed;Render();}
 internal void SetInitialMode(bool initial){if(Busy||HasDataOwner)throw new InvalidOperationException("Cierra la conexión de datos antes de cambiar de modo.");initialMode=initial;Render();Relayout();}
 internal void RestoreInitialContext(string root,int product){initialRoot=root;initialProduct=product;initialContextUnreadable=false;if(!DeviceFamily.Supported(product)){initialRecord=null;Render();return;}try{var pending=CFInstallCoordinator.FindPending(root,product);if(pending!=null)initialRecord=pending;else if(initialRecord!=null&&initialRecord.Product!=product)initialRecord=null;}catch(Exception error){initialContextUnreadable=true;Note("No se ha podido leer el estado de la instalación anterior: "+error.Message,true);}Render();}
 internal string PreparedNAM(int slot){var resource=workspace.Slots[slot];return resource==null?null:Path.GetFileNameWithoutExtension(resource.Name);}
 void LayoutInitial(int width){if(!initialMode)return;int h=(int)Math.Round(ClientSize.Height/EditorZoom.Factor);At(card,0,0,width,252);At(initialTitle,20,18,width-40,36);At(initialDescription,20,64,width-40,70);int buttonWidth=Math.Min(620,width-80);At(send,(width-buttonWidth)/2,156,buttonWidth,48);At(stored,20,216,width-40,24);At(receiverState,8,262,width-16,28);At(scope,8,296,width-16,38);At(progress,0,340,width,8);int logHeight=Math.Max(80,h-362);At(logCard,0,362,width,logHeight);At(activityTitle,16,8,width-32,28);At(log,16,44,width-32,logHeight-58);}
 bool WaitingInitial{get{return initialRecord!=null&&(initialRecord.Phase=="awaiting-reboot"||initialRecord.Phase=="transfer-uncertain"||initialRecord.Phase=="installing");}}
 void RenderInitial(){
  initialTitle.Visible=initialDescription.Visible=initialMode;if(!initialMode)return;
  foreach(Control item in card.Controls)item.Visible=item==send||item==stored||item==initialTitle||item==initialDescription;
  bool allowed=initialAllowed!=null&&initialAllowed()&&!initialContextUnreadable,editable=active&&!Busy&&!preview;
  bool transferred=WaitingInitial&&initialRecord.Phase=="awaiting-reboot";
  initialTitle.Text=knownReceiver?"Reinstalar Custom Firmware":"Instalar Custom Firmware";
  initialDescription.Text="Incluye: NAM 1 y NAM 2 · 3 IR de prueba, después pueden cambiarse desde «Cambiar NAM / IR».";
  send.Text=WaitingInitial?"Comprobar arranque":initialTitle.Text;send.Enabled=editable&&(WaitingInitial||allowed&&DemoAssets.Available&&installInitial!=null);send.Active=send.Enabled;send.Invalidate();receiverConnect.Visible=false;
  scope.Text=transferred?"Apaga y enciende el ampli normalmente, sin botones. Después pulsa «Comprobar arranque».":WaitingInitial?"Comprueba el resultado anterior antes de repetir una instalación.":"La instalación incluye copia de presets y requiere reiniciar el ampli al terminar.";
  stored.Visible=WaitingInitial||initialRecord!=null&&initialRecord.Phase=="active";stored.Text=initialRecord!=null&&initialRecord.Phase=="active"?"✓ Firmware instalado y arranque comprobado":WaitingInitial?"Pendiente de comprobar el arranque":"Los ejemplos vienen incluidos; no necesitas cargar archivos.";stored.ForeColor=initialRecord!=null&&initialRecord.Phase=="active"?UITheme.Success:UITheme.Muted;
  if(operationError==null){receiverState.Text=preview?"Vista previa · Instalación desactivada":WaitingInitial?(transferred?"Transferencia terminada · Reinicia el amplificador":"Instalación anterior pendiente de comprobar"):initialContextUnreadable?"Revisa el registro de la instalación anterior":!DemoAssets.Available?"Faltan los ejemplos integrados":!DeviceFamily.Supported(initialProduct)?"Conecta el amplificador para instalar el Custom Firmware":!allowed?"La instalación de este modelo aún no está habilitada":knownReceiver?"Preparado para reinstalar el Custom Firmware":"Preparado para instalar el Custom Firmware";receiverState.ForeColor=WaitingInitial||!allowed?UITheme.Warning:knownReceiver?UITheme.Success:UITheme.Muted;}
 }
 async Task<AssetPreparation[]> PrepareInitial(){return await Task.Run(()=>DemoAssets.Load());}
 async Task InstallInitial(){if(!initialMode||preview||!active||Busy||WaitingInitial||initialContextUnreadable||installInitial==null||initialAllowed==null||!initialAllowed()||!DemoAssets.Available)return;
  if(HasDataOwner)await CloseConnection();if(InstallationStarted!=null)InstallationStarted();sending=true;recalc.Stop();Render();var reporter=new Progress<string>(line=>{if(!disposed){Note(line);receiverState.Text=line;}});try{var resources=await PrepareInitial();initialRecord=await installInitial(resources,((IProgress<string>)reporter).Report);Note("Transferencia terminada. Apaga y enciende tu amplificador normalmente, sin botones.");}
  catch(Exception error){Note(error.Message,true);}finally{sending=false;if(!disposed){if(initialRoot!=null)RestoreInitialContext(initialRoot,initialProduct);else Render();}}
 }
 void ConfirmInitialReceiver(){if(initialRecord==null||!WaitingInitial||dataSession==null||!dataSession.Ready)return;initialRecord=CFInstallCoordinator.ConfirmBoot(Path.Combine(initialRecord.Folder,"installation.json"),(int)verifiedProduct,dataSession.VerifiedIdentity);Note("✓ Firmware inicial conectado y comprobado. Ya puedes cambiar NAM e IR sin instalar de nuevo el firmware.");}
}
}
