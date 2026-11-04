using System;using System.Collections.Generic;using System.Linq;using System.Threading.Tasks;
namespace CFEditor {
// Transport double: exercises ownership and UI transitions without loading an SDK.
sealed class HandoverBackend:IBackend {
 readonly PreviewBackend inner=new PreviewBackend();public int Connections,Disconnections;public bool FailNext;public bool KnownReceiver=true,IdentityAvailable=true,UpgradeRequired;public string FirmwareEdition=new string('1',64);public readonly List<string> Reads=new List<string>();
 public bool Connected{get{return inner.Connected;}}public bool Preview{get{return false;}}public int ProductID{get{return inner.ProductID;}}
 public event Action<string,string,object> Changed;public event Action<string> Status;
 public HandoverBackend(){inner.Changed+=(g,p,v)=>{if(Changed!=null)Changed(g,p,v);};}
 public async Task Connect(){Connections++;await Task.Yield();if(FailNext){FailNext=false;throw new InvalidOperationException("Fallo de conexión simulado");}await inner.Connect();if(Changed!=null)Changed("systemState","engineInterfaceConnectedToDevice",true);if(Changed!=null)Changed("CFAssets","FirmwareID",KnownReceiver&&IdentityAvailable?"CATALYST-CF-112233445566":"");if(Changed!=null)Changed("CFAssets","FirmwareEdition",KnownReceiver&&IdentityAvailable?FirmwareEdition:"");if(Changed!=null)Changed("CFAssets","UpgradeRequired",UpgradeRequired);if(Changed!=null)Changed("CFAssets","ReceiverKnown",KnownReceiver&&!UpgradeRequired);if(Status!=null)Status("Conexión simulada");}
 public async Task Disconnect(){Disconnections++;await inner.Disconnect();await Task.Yield();if(Changed!=null)Changed("systemState","engineInterfaceConnectedToDevice",false);}
 public Task Read(IEnumerable<Binding> refs){var unique=refs.GroupBy(b=>b.Key).Select(g=>g.First()).ToArray();Reads.AddRange(unique.Select(b=>b.Key));return inner.Read(unique);}
 public Task Set(Binding b,object v){return inner.Set(b,v);}public Task SelectAsset(string g,string a){return inner.SelectAsset(g,a);}public Task SelectPreset(int b,int c){return inner.SelectPreset(b,c);}public Task Command(string c,object v){throw new InvalidOperationException("No device commands in transport checks");}
}
sealed partial class EditorForm {
 async Task CheckConnectionFlow(){
  await ChangePage(0);var original=backend;original.Changed-=OnChanged;original.Status-=OnBackendStatus;var fake=new HandoverBackend();backend=fake;preview=false;backend.Changed+=OnChanged;backend.Status+=OnBackendStatus;detectedUsb=1;
  try{
   await fake.Connect();await RefreshDevice();int count=values.Count;var gain=bindings.First(b=>b.Group=="CatalystGroupAmp"&&b.Param.id=="Gain");Queue(gain,120);
   await ChangePage(5);TestNeed(!fake.Connected&&editorUSBReleased&&resumeEditorAfterFirmware,"Firmware did not release ownership intentionally");TestNeed(pending.Count==0&&values.Count>=count,"Handover dropped queued values or cleared the edit view");TestNeed(connection.Text.StartsWith("Amplificador conectado"),"Internal handover claims physical USB loss");
   TestNeed(firmware.DataMode&&!firmware.DataView.InitialMode,"Data navigation did not select data mode");int connections=fake.Connections,disconnections=fake.Disconnections;
   await ChangePage(5);TestNeed(firmware.DataMode&&firmware.DataView.Visible&&!firmware.DataView.InitialMode&&(InitialCFResources.UnifiedWorkspace?firmware.Child==null:!firmware.Child.Visible)&&!fake.Connected,"Data mode did not retain editor release");
   TestNeed(connection.Text.StartsWith("Amplificador conectado")&&!firmware.DataView.SendEnabled,"Data mode reports physical disconnect or permits unverified transfer");
   await ChangePage(4);TestNeed(!firmware.DataMode&&(InitialCFResources.UnifiedWorkspace?firmware.DataView.Visible&&firmware.DataView.InitialMode&&firmware.Child==null:firmware.Child.Visible&&!firmware.DataView.Visible),"Initial installer mode did not recover after data switch");
   await ChangePage(5);TestNeed(fake.Connections==connections&&fake.Disconnections==disconnections,"Changing CF modes reopened the editor backend");
   fake.Reads.Clear();await ChangePage(0);TestNeed(fake.Connected&&!editorUSBReleased&&!resumeEditorAfterFirmware&&!firmware.Active,"Editor did not regain ownership on return");TestNeed(fake.Reads.Count==fake.Reads.Distinct().Count(),"A full refresh reads a parameter more than once");TestNeed(!loading.Visible,"Handover left a blocking loading screen");
   await ChangePage(4);fake.FailNext=true;await ChangePage(0);TestNeed(!fake.Connected&&!loading.Visible&&resumeEditorAfterFirmware,"Reconnect failure blocked recovery");await InitializeView(false);TestNeed(fake.Connected&&!loading.Visible,"Reconnect cannot be retried");
  }finally{backend.Changed-=OnChanged;backend.Status-=OnBackendStatus;backend=original;backend.Changed+=OnChanged;backend.Status+=OnBackendStatus;preview=true;editorUSBReleased=resumeEditorAfterFirmware=false;detectedUsb=0;UpdateEnabled();}
 }
}
}
