using System;using System.IO;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;using System.Windows.Forms;
namespace CFEditor {
// Narrow explicit trial controls. No preset-store or firmware operation.
static class NativeExercise {
 internal static int Run(string report,string mode){
  report=Path.GetFullPath(report);Directory.CreateDirectory(Path.GetDirectoryName(report));int result=1;
  using(var app=new ApplicationContext())using(var start=new System.Windows.Forms.Timer{Interval=10}){start.Tick+=async delegate{
   start.Stop();SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());var backend=new NativeBackend();var values=new Dictionary<string,object>();Dictionary<string,object> before=null;var events=new List<string>();Exception failure=null;bool closed=false,extended=false;
   backend.Status+=s=>{events.Add(s);File.AppendAllText(report+".log",s+Environment.NewLine);};backend.Changed+=(g,p,v)=>{values[g+"/"+p]=v;};
   var cab=new Binding("CatalystGroupDirectOut",Catalog.Int("CabinetIndex","IR",0,2));var amp=new Binding("CatalystGroupAmp",new Parameter{id="graphSlot_AssetEngineID",kind="string"},false);var cf=new Binding("CFPreamp",Catalog.Int("type","Preamp",0,3));
   var cabEnabled=new Binding("global",Catalog.Bool("cabMicEnable","IR activo"));
   var observed=new List<Binding>{cab,amp,cf,cabEnabled};foreach(var name in new[]{"currentAmpBank","currentAmpChannel","currentDevicePresetIndex"})observed.Add(new Binding("systemState",Catalog.Int(name,name,0,12),false));foreach(var name in new[]{"BoostEnable","Boost","Gain","Bass","Mid","Treble","Presence","ChannelVolume"})observed.Add(new Binding("CatalystGroupAmp",Catalog.Int(name,name,0,name=="BoostEnable"?1:255)));
   try{await backend.Connect();await backend.Read(observed);await Task.Delay(50);extended=backend.ExtendedPreampAvailable;
    if(mode!="read"){
     if(!extended)throw new IOException("El firmware/modelo no ha confirmado los controles NAM.");
     // User explicitly requested reuse of the existing verified archive.
     // These trials edit only the volatile buffer; record read values locally.
     before=new Dictionary<string,object>(values);
     if(mode=="ir-cycle"){int prior=Convert.ToInt32(values[cab.Key]);foreach(int slot in new[]{1,0,2,prior}){await backend.Set(cab,slot);await Task.Delay(2500);}await backend.Read(new[]{cab});}
     else if(mode=="nam1"||mode=="nam2"){await backend.SelectAsset("CatalystGroupAmp",mode=="nam1"?"Chime":"Chime 2");await Task.Delay(1500);await backend.Read(observed);extended=backend.ExtendedPreampAvailable;}
     else if(mode=="ir-on"){await backend.Set(cabEnabled,true);await backend.Set(cab,1);await Task.Delay(2500);await backend.Set(cab,0);await Task.Delay(2500);await backend.Read(new[]{cabEnabled,cab});}
     else if(mode.StartsWith("preamp-")&&mode.Length==8&&mode[7]>='0'&&mode[7]<='3'){await backend.Set(cf,mode[7]-'0');await Task.Delay(300);await backend.Read(new[]{cf});}
     else if(mode.StartsWith("pedal-")&&mode.Length==7&&mode[6]>='0'&&mode[6]<='3'){
      await backend.Set(cf,mode[6]-'0');await backend.Set(new Binding("CatalystGroupAmp",Catalog.Int("BoostEnable","Boost activo",0,1)),1);await Task.Delay(1500);await backend.Read(observed);
     }
     else if(mode=="factory-rearm"){
      await backend.Set(cf,0);await backend.Set(new Binding("CatalystGroupAmp",Catalog.Int("BoostEnable","Boost activo",0,1)),0);
      int prior=Convert.ToInt32(values[cab.Key]);await backend.Set(cab,(prior+1)%3);await Task.Delay(2500);await backend.Set(cab,prior);await Task.Delay(2500);await backend.Read(observed);
     }
     else throw new ArgumentException("Prueba no admitida.");
    }
   }catch(Exception e){failure=e;}
   try{await backend.Disconnect();closed=true;}catch(Exception e){failure=failure==null?e:new AggregateException(failure,e);}
   result=failure==null&&closed?0:1;File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{passed=result==0,mode,extended_preamp_available=extended,shutdown_confirmed=closed,presets_saved=false,firmware_installed=false,before,values,events,error=failure==null?null:failure.ToString()}));if(closed)app.ExitThread();
  };start.Start();Application.Run(app);}return result;
 }
}
}
