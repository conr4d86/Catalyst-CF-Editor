#if DEVELOPMENT
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Drawing;using System.Threading.Tasks;using System.Windows.Forms;using System.Web.Script.Serialization;
namespace CFEditor {
sealed class LifecycleBackend:IBackend {
 readonly PreviewBackend inner=new PreviewBackend();public int Connections,Disconnections,Reads;public bool FailDisconnect;public TaskCompletionSource<bool> DisconnectGate,ReadGate;public readonly TaskCompletionSource<bool> DisconnectEntered=new TaskCompletionSource<bool>(),ReadEntered=new TaskCompletionSource<bool>();public readonly List<string> Presets=new List<string>();
 public bool Connected{get{return inner.Connected;}}public bool Preview{get{return false;}}public int ProductID{get{return inner.ProductID;}}public event Action<string,string,object> Changed;public event Action<string> Status;
 public LifecycleBackend(){inner.Changed+=(g,p,v)=>{if(Changed!=null)Changed(g,p,v);};}
 public async Task Connect(){Connections++;await inner.Connect();if(Changed!=null){Changed("CFAssets","FirmwareID","CATALYST-CF-112233445566");Changed("CFAssets","ReceiverKnown",true);}}
 public async Task Disconnect(){Disconnections++;DisconnectEntered.TrySetResult(true);if(FailDisconnect){FailDisconnect=false;throw new IOException("Synthetic delayed shutdown failure");}if(DisconnectGate!=null)await DisconnectGate.Task;await inner.Disconnect();if(Changed!=null)Changed("systemState","engineInterfaceConnectedToDevice",false);if(Status!=null)Status("Late shutdown status");}
 public async Task Read(IEnumerable<Binding> refs){Reads++;if(ReadGate!=null){var gate=ReadGate;ReadGate=null;ReadEntered.TrySetResult(true);await gate.Task;}await inner.Read(refs);}
 public Task Set(Binding binding,object value){return inner.Set(binding,value);}public Task SelectAsset(string group,string asset){return inner.SelectAsset(group,asset);}public Task SelectPreset(int bank,int channel){Presets.Add(bank+":"+channel);return inner.SelectPreset(bank,channel);}public Task Command(string command,object value){throw new IOException("No device commands in lifecycle checks");}
}
sealed partial class EditorForm {
 async Task WaitAccessIdle(){for(int i=0;i<400&&(initializing||busy||accessRedirectQueued);i++)await Task.Delay(5);TestNeed(!initializing&&!busy,"Access transition did not settle");}
 void NeedInstallOnly(){UpdateEnabled();TestNeed(currentPage==4&&nav[4].Enabled&&nav.Where((n,i)=>i!=4).All(n=>!n.Enabled),"Official/unknown firmware exposes editor pages");}
 async Task<EditorForm> LifecycleForm(LifecycleBackend fake){var subject=new EditorForm(true,false,false,false){StartPosition=FormStartPosition.Manual,Location=new Point(-12000,-12000),ShowInTaskbar=false};subject.Show();await subject.WaitAccessIdle();subject.writes.Stop();subject.layoutTimer.Stop();subject.backend.Changed-=subject.OnChanged;subject.backend.Status-=subject.OnBackendStatus;subject.backend=fake;subject.preview=false;subject.detectedUsb=1;fake.Changed+=subject.OnChanged;fake.Status+=subject.OnBackendStatus;await fake.Connect();await subject.RefreshDevice();return subject;}
 static async Task UntilLifecycle(Func<bool> done){var clock=System.Diagnostics.Stopwatch.StartNew();while(!done()&&clock.ElapsedMilliseconds<5000)await Task.Delay(5);TestNeed(done(),"Lifecycle fixture did not settle");}
 async Task CheckLifecycleRaces(){
  var fake=new LifecycleBackend();using(var subject=await LifecycleForm(fake)){
   subject.refreshing=true;subject.bank.SelectedIndex=5;subject.channel.SelectedIndex=2;subject.refreshing=false;await subject.ChangePreset();subject.refreshing=true;subject.channel.SelectedIndex=1;subject.refreshing=false;await subject.ChangePreset();
   TestNeed(fake.Presets.SequenceEqual(new[]{"5:2","5:1"})&&subject.bank.SelectedIndex==5&&subject.channel.SelectedIndex==1&&!subject.busy,"Sequential 6B to 6A transition lost its requested target");
   fake.DisconnectGate=new TaskCompletionSource<bool>();subject.customFirmwareConfirmed=false;subject.ScheduleRestrictedPage();TestNeed(subject.accessRedirectQueued,"Redirect race was not actually queued");int connects=fake.Connections;subject.Close();await UntilLifecycle(()=>fake.DisconnectEntered.Task.IsCompleted);
   TestNeed(subject.closePending&&!subject.closing&&!subject.IsDisposed,"Close does not retain the live form until disconnect completes");await Task.Delay(20);TestNeed(fake.Connections==connects&&!subject.busy&&!subject.accessRedirectQueued,"Queued redirect started work during asynchronous close");
   string before=subject.message.Text;subject.OnBackendStatus("Late during close");subject.Error(new IOException("Late close failure"));TestNeed(subject.message.Text==before,"Late callback rewrote a closing view");
   fake.DisconnectGate.TrySetResult(true);await UntilLifecycle(()=>subject.IsDisposed);int actions=0;await subject.ChangePage(0);await subject.Run(()=>{actions++;return Task.FromResult(0);});subject.Error(new IOException("Late after dispose"));subject.Log("Late after dispose");subject.OnChanged("systemState","currentAmpChannel",2);subject.OnBackendStatus("Late status");subject.UpdateEnabled();subject.ScheduleRestrictedPage();TestNeed(actions==0&&fake.Connections==connects,"Disposed form reopened work or a connection");
  }
  foreach(bool fail in new[]{false,true}){fake=new LifecycleBackend();using(var subject=await LifecycleForm(fake)){
   var release=new TaskCompletionSource<bool>();fake.ReadGate=release;var refresh=subject.Run(subject.RefreshDevice);await UntilLifecycle(()=>fake.ReadEntered.Task.IsCompleted);subject.Dispose();if(fail)release.TrySetException(new IOException("Synthetic late read error"));else release.TrySetResult(true);await refresh;TestNeed(!subject.busy&&!subject.reading,"Disposed refresh left operation state active");subject.languageUI.Dispose();AppLanguage.Changed-=subject.RefreshLanguageChoices;
  }}
  fake=new LifecycleBackend{FailDisconnect=true};using(var subject=await LifecycleForm(fake)){subject.Close();await UntilLifecycle(()=>!subject.closePending);TestNeed(!subject.IsDisposed&&subject.UiAlive&&(subject.firmware.DataView==null||!subject.firmware.DataView.IsDisposed),"Failed disconnect disposed the reusable firmware view");subject.Close();await UntilLifecycle(()=>subject.IsDisposed);TestNeed(fake.Disconnections==2,"Failed-close retry did not use the retained owner");}
  using(var log=new DarkLogBox()){log.Dispose();log.AppendText("Late standalone log");}
 }
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
  fake.KnownReceiver=true;fake.UpgradeRequired=true;await ToggleEditorConnection();await WaitAccessIdle();NeedInstallOnly();
  TestNeed(firmwareUpgradeRequired&&message.Text==FirmwareUpgradeRequiredException.UpdateMessage&&firmware.VerifiedUpgradeEdition==fake.FirmwareEdition&&AllChildren(firmware.DataView).Any(c=>c.Visible&&c.Text=="Reinstalar Custom Firmware"),"Unsafe CF handover lost its update hint, full edition or reinstall action");fake.UpgradeRequired=false;
  fake.KnownReceiver=true;fake.IdentityAvailable=false;await ToggleEditorConnection();await WaitAccessIdle();NeedInstallOnly();fake.IdentityAvailable=true;await ToggleEditorConnection();await WaitAccessIdle();TestNeed(customFirmwareConfirmed&&nav.All(n=>n.Enabled)&&!fake.Connected,"Compatible CF does not unlock pages after the USB check");
  TestNeed(message.Text!="Instala el Custom Firmware para desbloquear el editor."&&message.ForeColor!=UITheme.Warning,"Install warning remains after CF confirmation");
  await ChangePage(0);TestNeed(fake.Connected&&EditorAccess&&currentPage==0,"Verified CF cannot return to the sound editor");
  await ChangePage(4);RestrictUntilVerified();NeedInstallOnly();string receiverIdentity="CFCX-UID2:10203040A0B0C0D0:"+new string('1',64);
  ReceiverVerified(2424834,receiverIdentity,61);NeedInstallOnly();ReceiverVerified(2424833,"invalid",61);NeedInstallOnly();
  OnChanged("CFAssets","FirmwareID","CATALYST-CF-112233445566");NeedInstallOnly();TestNeed(!EditorAccess,"Identity alone unlocks the editor");
  ReceiverVerified(2424833,receiverIdentity,1);NeedInstallOnly();TestNeed(!EditorAccess,"Data-only capabilities unlock unsupported editor controls");
  ReceiverVerified(2424833,receiverIdentity,29);NeedInstallOnly();TestNeed(firmwareUpgradeRequired&&message.Text==FirmwareUpgradeRequiredException.UpdateMessage,"Unsafe all-model CF did not show its reinstall instruction");
  OnChanged("CFAssets","ReceiverKnown",true);NeedInstallOnly();ReceiverVerified(2424833,receiverIdentity,125);NeedInstallOnly();
  AppLanguage.Set(true,false);TestNeed(AppLanguage.Text(FirmwareUpgradeRequiredException.UpdateMessage).StartsWith("The installed Custom Firmware needs an update"),"Upgrade warning lacks an English caption");AppLanguage.Set(false,false);
  ReceiverVerified(2424833,receiverIdentity,61);TestNeed(EditorAccess&&nav.All(n=>n.Enabled)&&!firmwareUpgradeRequired,"Fresh safe receiver confirmation does not unlock the editor");
  await ChangePage(0);await fake.Disconnect();await ChangePage(4);await WaitAccessIdle();NeedInstallOnly();
  await CheckLifecycleRaces();
  TestNeed(!native.NativeInitializationAttempted&&!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="EngineCLIWrapper"),"Access checks loaded the native SDK");
  return new{passed=true,hardware_accessed=false,USB_opened=false,SDK_loaded=false,unknown_and_official_install_tab_only=true,offline_fallback_cannot_bypass=true,blocked_programmatic_navigation_and_writes=true,stale_install_hint_cleared=true,compatible_CF_unlocks=true,firmware_identifier_required=true,unsafe_all_model_CF_install_tab_only=true,unknown_caps_cannot_unlock=true,upgrade_hint_ES_EN=true,no_private_UID_in_public_identifier=true,intentional_handover_preserves_access=true,fresh_receiver_product_checked=true,disconnect_relocks=true,queued_redirect_ignored_during_close=true,late_read_and_error_after_dispose_safe=true,failed_close_retains_reusable_view=true,fake_bank6B_to_6A_targets_preserved=true};
 }
}
static class FirmwareAccessChecks {
 public static int Run(string folder){folder=Path.GetFullPath(folder);Directory.CreateDirectory(folder);int result=1;using(var form=new EditorForm(false,false,false,false)){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-12000,-12000);form.ShowInTaskbar=false;form.Shown+=async delegate{try{var report=await form.CheckFirmwareAccess(folder);File.WriteAllText(Path.Combine(folder,"result.json"),new JavaScriptSerializer().Serialize(report));result=0;}catch(Exception error){File.WriteAllText(Path.Combine(folder,"result.json"),new JavaScriptSerializer().Serialize(new{passed=false,USB_opened=false,error=error.ToString()}));}finally{form.Close();}};Application.Run(form);}return result;}
}
}
#endif
