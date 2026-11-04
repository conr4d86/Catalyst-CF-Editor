#if DEVELOPMENT
using System;using System.IO;using System.Linq;using System.Drawing;using System.Threading.Tasks;using System.Windows.Forms;using System.Web.Script.Serialization;
namespace CFEditor {
sealed partial class EditorForm {
 async Task WaitAccessIdle(){for(int i=0;i<400&&(initializing||busy||accessRedirectQueued);i++)await Task.Delay(5);TestNeed(!initializing&&!busy,"Access transition did not settle");}
 void NeedInstallOnly(){UpdateEnabled();TestNeed(currentPage==4&&nav[4].Enabled&&nav.Where((n,i)=>i!=4).All(n=>!n.Enabled),"Official/unknown firmware exposes editor pages");}
 internal async Task<object> CheckFirmwareAccess(string folder){
  await WaitAccessIdle();writes.Stop();layoutTimer.Stop();AppLanguage.Set(false,false);var native=backend as NativeBackend;
  TestNeed(native!=null&&!native.NativeInitializationAttempted&&!preview,"Normal startup loaded SDK without a detected device");
  NeedInstallOnly();TestNeed(openPreview.Text=="Ver Firmware CF","Public offline fallback still offers an unrestricted editor");openPreview.PerformClick();await WaitAccessIdle();TestNeed(!preview&&!loading.Visible,"Offline fallback bypasses CF verification");NeedInstallOnly();
  native.Changed-=OnChanged;native.Status-=OnBackendStatus;var fake=new HandoverBackend{KnownReceiver=false};backend=fake;backend.Changed+=OnChanged;backend.Status+=OnBackendStatus;detectedUsb=1;
  await InitializeView(false);await WaitAccessIdle();NeedInstallOnly();TestNeed(!fake.Connected&&editorUSBReleased&&resumeEditorAfterFirmware,"Official firmware installer did not acquire an exclusive handover");
  firmware.DataView.RestoreInitialContext(Path.Combine(folder,"empty-install-context"),2424833);TestNeed(firmware.DataView.InitialMode&&firmware.DataView.SendEnabled==(InitialCFResources.InstallAllowed(2424833)&&DemoAssets.Available),"Initial install action does not match this build's product permission");
  int connections=fake.Connections;foreach(int page in new[]{0,1,2,3,5})await ChangePage(page);TestNeed(fake.Connections==connections,"Blocked tab reopened a backend");NeedInstallOnly();
  var gain=bindings.First(b=>b.Group=="CatalystGroupAmp"&&b.Param.id=="Gain");Queue(gain,180);TestNeed(pending.Count==0,"Locked editor queued a write");
  Snapshot(Path.Combine(folder,"official-firmware-only.png"),4);
  fake.KnownReceiver=true;fake.IdentityAvailable=false;await ToggleEditorConnection();await WaitAccessIdle();NeedInstallOnly();fake.IdentityAvailable=true;await ToggleEditorConnection();await WaitAccessIdle();TestNeed(customFirmwareConfirmed&&nav.All(n=>n.Enabled)&&!fake.Connected,"Compatible CF does not unlock pages after the USB check");
  TestNeed(message.Text!="Instala el Custom Firmware para desbloquear el editor."&&message.ForeColor!=UITheme.Warning,"Install warning remains after CF confirmation");
  await ChangePage(0);TestNeed(fake.Connected&&EditorAccess&&currentPage==0,"Verified CF cannot return to the sound editor");
  await ChangePage(4);RestrictUntilVerified();NeedInstallOnly();ReceiverVerified(2424834,"CFCX-UID2:10203040A0B0C0D0:"+new string('1',64));NeedInstallOnly();ReceiverVerified(2424833,"invalid");NeedInstallOnly();ReceiverVerified(2424833,"CFCX-UID2:10203040A0B0C0D0:"+new string('1',64));TestNeed(EditorAccess&&nav.All(n=>n.Enabled),"Fresh same-product receiver confirmation does not unlock the editor");
  await ChangePage(0);await fake.Disconnect();await ChangePage(4);await WaitAccessIdle();NeedInstallOnly();
  TestNeed(!native.NativeInitializationAttempted&&!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="EngineCLIWrapper"),"Access checks loaded the native SDK");
  return new{passed=true,hardware_accessed=false,USB_opened=false,SDK_loaded=false,unknown_and_official_install_tab_only=true,offline_fallback_cannot_bypass=true,blocked_programmatic_navigation_and_writes=true,stale_install_hint_cleared=true,compatible_CF_unlocks=true,firmware_identifier_required=true,no_private_UID_in_public_identifier=true,intentional_handover_preserves_access=true,fresh_receiver_product_checked=true,disconnect_relocks=true};
 }
}
static class FirmwareAccessChecks {
 public static int Run(string folder){folder=Path.GetFullPath(folder);Directory.CreateDirectory(folder);int result=1;using(var form=new EditorForm(false,false,false,false)){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-12000,-12000);form.ShowInTaskbar=false;form.Shown+=async delegate{try{var report=await form.CheckFirmwareAccess(folder);File.WriteAllText(Path.Combine(folder,"result.json"),new JavaScriptSerializer().Serialize(report));result=0;}catch(Exception error){File.WriteAllText(Path.Combine(folder,"result.json"),new JavaScriptSerializer().Serialize(new{passed=false,USB_opened=false,error=error.ToString()}));}finally{form.Close();}};Application.Run(form);}return result;}
}
}
#endif
