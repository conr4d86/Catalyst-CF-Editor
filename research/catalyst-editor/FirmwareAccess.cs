using System;using System.IO;using System.Threading.Tasks;
namespace CFEditor {
sealed partial class EditorForm {
 bool customFirmwareConfirmed,accessRedirectQueued,firmwareAccessHint,firmwareUpgradeRequired;
 const string InstallHint="Instala el Custom Firmware para desbloquear el editor.";
 bool EditorAccess{get{return preview||customFirmwareConfirmed;}}
 bool PageAllowed(int index){return index==4||EditorAccess;}
 void ObserveFirmwareAccess(string group,string parameter,object value){
  if(group=="CFAssets"&&parameter=="FirmwareID"&&!FirmwareIdentifier.ValidLabel(Convert.ToString(value)))customFirmwareConfirmed=false;
  if(group=="CFAssets"&&parameter=="UpgradeRequired"){firmwareUpgradeRequired=Convert.ToBoolean(value);if(firmwareUpgradeRequired)customFirmwareConfirmed=false;}
  if(group=="CFAssets"&&parameter=="ReceiverKnown"){object id;customFirmwareConfirmed=!firmwareUpgradeRequired&&backend.Connected&&Convert.ToBoolean(value)&&values.TryGetValue("CFAssets/FirmwareID",out id)&&FirmwareIdentifier.ValidLabel(Convert.ToString(id));}
  if(group=="systemState"&&parameter=="engineInterfaceConnectedToDevice"&&!Convert.ToBoolean(value)&&!editorUSBReleased)customFirmwareConfirmed=false;
  if(!EditorAccess)pending.Clear();
 }
 void ReceiverVerified(uint product,string identity,uint capabilities){string id;if(!DeviceFamily.Supported((int)product)||product!=(uint)backend.ProductID||!FirmwareIdentifier.PublicLabel(identity,out id))return;
  try{AssetWire.ValidateCapabilities(capabilities);}catch(FirmwareUpgradeRequiredException){firmwareUpgradeRequired=true;customFirmwareConfirmed=false;UpdateEnabled();return;}catch(InvalidDataException){customFirmwareConfirmed=false;UpdateEnabled();return;}
  if((capabilities&(AssetWire.DataWrite|AssetWire.PreampControl))!=(AssetWire.DataWrite|AssetWire.PreampControl)){customFirmwareConfirmed=false;UpdateEnabled();return;}values["CFAssets/FirmwareID"]=id;firmwareUpgradeRequired=false;customFirmwareConfirmed=true;UpdateEnabled();}
 void UpdateFirmwareAccessHint(){
  if(!preview&&!EditorAccess&&!busy&&!initializing&&message.ForeColor!=UITheme.Error){message.Text=firmwareUpgradeRequired?FirmwareUpgradeRequiredException.UpdateMessage:InstallHint;message.ForeColor=UITheme.Warning;firmwareAccessHint=true;}
  else if(EditorAccess&&firmwareAccessHint){firmwareAccessHint=false;if(message.Text==InstallHint||message.Text==AppLanguage.Text(InstallHint)||message.Text==FirmwareUpgradeRequiredException.UpdateMessage||message.Text==AppLanguage.Text(FirmwareUpgradeRequiredException.UpdateMessage)){message.Text="Custom Firmware reconocido. Editor desbloqueado.";message.ForeColor=UITheme.Success;}}
 }
 void RestrictUntilVerified(){customFirmwareConfirmed=false;pending.Clear();UpdateEnabled();}
 void ScheduleRestrictedPage(){
  if(!UiAlive||preview||EditorAccess||currentPage==4||accessRedirectQueued||!IsHandleCreated||busy||initializing||firmware==null||firmware.Busy)return;
  accessRedirectQueued=true;try{BeginInvoke((Action)(async()=>{accessRedirectQueued=false;if(!UiAlive||EditorAccess||busy||initializing||firmware==null||firmware.Busy)return;try{await ChangePage(4);}catch(Exception error){Error(error);}}));}catch(InvalidOperationException){accessRedirectQueued=false;}
 }
 void ShowPageSurface(int index){if(!UiAlive)return;currentPage=index;for(int n=0;n<pages.Count;n++)pages[n].Visible=n==PageSurface(index);for(int n=0;n<nav.Count;n++){nav[n].Active=n==index;nav[n].Invalidate();}}
 async Task HandoverToFirmware(){
  if(!UiAlive||preview||!backend.Connected)return;resumeEditorAfterFirmware=true;firmware.SetDetectedReceiver(customFirmwareConfirmed||firmwareUpgradeRequired);object edition;firmware.SetUpgradeContext(firmwareUpgradeRequired,values.TryGetValue("CFAssets/FirmwareEdition",out edition)?Convert.ToString(edition):null);firmware.SetDataProduct((uint)backend.ProductID);
  await Flush(true);if(!UiAlive)return;editorUSBReleased=true;await backend.Disconnect();
 }
 async Task ToggleEditorConnection(){
  await Run(async()=>{ShowLoading("Loading & Syncing","Sincronizando los ajustes del amplificador…");try{
   if(IsFirmwarePage(currentPage)){
    if(firmware.Busy||firmware.DataView.HasDataOwner)throw new IOException("Cierra la conexión de datos antes de comprobar el USB.");
    firmware.Deactivate();editorUSBReleased=false;await backend.Connect();await RefreshDevice();await HandoverToFirmware();await firmware.Activate(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-data","firmware"));
   }else if(backend.Connected)await backend.Disconnect();else{await backend.Connect();await RefreshDevice();}
  }finally{FinishLoading();}});if(UiAlive&&!EditorAccess)await ChangePage(4);
 }
}
}
