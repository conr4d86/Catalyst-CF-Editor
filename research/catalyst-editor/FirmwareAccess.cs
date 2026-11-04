using System;using System.IO;using System.Threading.Tasks;
namespace CFEditor {
sealed partial class EditorForm {
 bool customFirmwareConfirmed,accessRedirectQueued,firmwareAccessHint;
 const string InstallHint="Instala el Custom Firmware para desbloquear el editor.";
 bool EditorAccess{get{return preview||customFirmwareConfirmed;}}
 bool PageAllowed(int index){return index==4||EditorAccess;}
 void ObserveFirmwareAccess(string group,string parameter,object value){
  if(group=="CFAssets"&&parameter=="FirmwareID"&&!FirmwareIdentifier.ValidLabel(Convert.ToString(value)))customFirmwareConfirmed=false;
  if(group=="CFAssets"&&parameter=="ReceiverKnown"){object id;customFirmwareConfirmed=backend.Connected&&Convert.ToBoolean(value)&&values.TryGetValue("CFAssets/FirmwareID",out id)&&FirmwareIdentifier.ValidLabel(Convert.ToString(id));}
  if(group=="systemState"&&parameter=="engineInterfaceConnectedToDevice"&&!Convert.ToBoolean(value)&&!editorUSBReleased)customFirmwareConfirmed=false;
  if(!EditorAccess)pending.Clear();
 }
 void ReceiverVerified(uint product,string identity){string id;if(!DeviceFamily.Supported((int)product)||product!=(uint)backend.ProductID||!FirmwareIdentifier.PublicLabel(identity,out id))return;values["CFAssets/FirmwareID"]=id;customFirmwareConfirmed=true;UpdateEnabled();}
 void UpdateFirmwareAccessHint(){
  if(!preview&&!EditorAccess&&!busy&&!initializing&&message.ForeColor!=UITheme.Error){message.Text=InstallHint;message.ForeColor=UITheme.Warning;firmwareAccessHint=true;}
  else if(EditorAccess&&firmwareAccessHint){firmwareAccessHint=false;if(message.Text==InstallHint||message.Text==AppLanguage.Text(InstallHint)){message.Text="Custom Firmware reconocido. Editor desbloqueado.";message.ForeColor=UITheme.Success;}}
 }
 void RestrictUntilVerified(){customFirmwareConfirmed=false;pending.Clear();UpdateEnabled();}
 void ScheduleRestrictedPage(){
  if(preview||EditorAccess||currentPage==4||accessRedirectQueued||closing||!IsHandleCreated||busy||initializing||firmware==null||firmware.Busy)return;
  accessRedirectQueued=true;BeginInvoke((Action)(async()=>{accessRedirectQueued=false;if(!closing&&!EditorAccess&&!busy&&!initializing&&!firmware.Busy)await ChangePage(4);}));
 }
 void ShowPageSurface(int index){currentPage=index;for(int n=0;n<pages.Count;n++)pages[n].Visible=n==PageSurface(index);for(int n=0;n<nav.Count;n++){nav[n].Active=n==index;nav[n].Invalidate();}}
 async Task HandoverToFirmware(){
  if(preview||!backend.Connected)return;resumeEditorAfterFirmware=true;firmware.SetDetectedReceiver(customFirmwareConfirmed);firmware.SetDataProduct((uint)backend.ProductID);
  await Flush(true);editorUSBReleased=true;await backend.Disconnect();
 }
 async Task ToggleEditorConnection(){
  await Run(async()=>{ShowLoading("Loading & Syncing","Sincronizando los ajustes del amplificador…");try{
   if(IsFirmwarePage(currentPage)){
    if(firmware.Busy||firmware.DataView.HasDataOwner)throw new IOException("Cierra la conexión de datos antes de comprobar el USB.");
    firmware.Deactivate();editorUSBReleased=false;await backend.Connect();await RefreshDevice();await HandoverToFirmware();await firmware.Activate(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-data","firmware"));
   }else if(backend.Connected)await backend.Disconnect();else{await backend.Connect();await RefreshDevice();}
  }finally{FinishLoading();}});if(!EditorAccess)await ChangePage(4);
 }
}
}
