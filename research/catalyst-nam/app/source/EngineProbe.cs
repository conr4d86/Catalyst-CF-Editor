using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
#if IN_PROCESS
using EngineWrapper = InProcessSDK.EditorEngine;
#else
using EngineCLIWrapper;
#endif

// Vendor engine read/backup and volatile cabinet-control adapter. Firmware
// updates are not exposed. --activate-store-ir saves only the identified current
// preset after snapshots; all backup/status operations remain non-persistent.
class EngineProbe {
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool SetDllDirectory(string path);
 static object gate=new object();
 // The C++/CLI wrapper stores native callback delegates on this instance.
 // Keep a static root through native destruction, as the vendor singleton does.
 static EngineWrapper keepEngineAlive;
 static ManualResetEvent synchronized=new ManualResetEvent(false);
 static Action<string> logSink;
 static bool callbacksInstalled;
 static void Log(string message){lock(gate){if(logSink!=null)logSink(message);else{Console.WriteLine(message);Console.Out.Flush();}}}
#if IN_PROCESS
 public static int Execute(string[] args,Action<string> output){
  logSink=output;synchronized.Reset();
  try{return Run(args);}catch(Exception e){Log(e.ToString());return 1;}finally{logSink=null;}
 }
#endif
 static void PumpWait(int ms){var end=DateTime.UtcNow.AddMilliseconds(ms);while(DateTime.UtcNow<end){System.Windows.Forms.Application.DoEvents();Thread.Sleep(10);}}
 static T Background<T>(Func<T> operation){var task=Task.Run(operation);while(!task.IsCompleted)PumpWait(20);return task.GetAwaiter().GetResult();}
 static string Canonical(object value){
  var dict=value as Dictionary<string,object>;
  var json=new System.Web.Script.Serialization.JavaScriptSerializer();
  if(dict!=null)return "{"+string.Join(",",dict.Keys.OrderBy(k=>k,StringComparer.Ordinal).Select(k=>json.Serialize(k)+":"+Canonical(dict[k])).ToArray())+"}";
  var array=value as object[];if(array!=null)return "["+string.Join(",",array.Select(Canonical).ToArray())+"]";
  return json.Serialize(value);
 }
 static object NormalizeVendorFlags(object value,string key=""){
  var dict=value as Dictionary<string,object>;
  if(dict!=null)return dict.ToDictionary(k=>k.Key,k=>NormalizeVendorFlags(k.Value,k.Key));
  if(key=="HumEnable"||key=="graphSlotAsset_TempoSyncActive"){
   int v=Convert.ToInt32(value);if(v!=0&&v!=1)throw new InvalidDataException("Non-boolean vendor flag: "+key);return v!=0;
  }return value;
 }
 static bool SameLiveData(string first,string second){
  var json=new System.Web.Script.Serialization.JavaScriptSerializer();return Canonical(NormalizeVendorFlags(json.DeserializeObject(first)))==Canonical(NormalizeVendorFlags(json.DeserializeObject(second)));
 }
 static void ControlSession(EngineWrapper engine,string folder){
  Directory.CreateDirectory(folder);string commands=Path.Combine(folder,"requests.txt");
  if(!File.Exists(commands))File.WriteAllText(commands,"");
  SaveCurrent(engine,Path.Combine(folder,"session-start.catl6p"));int handled=0;Log("CONTROL_READY");
  while(true){
   string[] lines;try{lines=File.ReadAllLines(commands);}catch(IOException){PumpWait(100);continue;}
   while(handled<lines.Length){string line=lines[handled++].Trim();if(line.Length==0)continue;Log("CONTROL_REQUEST "+handled+" "+line);
    try{
     string[] fields=line.Split(' ');if(line=="QUIT"){Log("CONTROL_QUIT");return;}
     int value;
     if(fields.Length!=2||!int.TryParse(fields[1],out value))throw new ArgumentException("Expected CAB 0..2 or MIC 0..15");
     string parameter;
     if(fields[0]=="CAB"&&value>=0&&value<=2)parameter="CabinetIndex";
     else if(fields[0]=="MIC"&&value>=0&&value<=15)parameter="MicIndex";
     else throw new ArgumentException("Unsupported control request");
     int result=Background(()=>engine.setIntParamValue("CatalystGroupDirectOut",parameter,value));Log("CONTROL_RESULT="+result);if(result!=0)continue;
     PumpWait(1000);SaveCurrent(engine,Path.Combine(folder,"request-"+handled.ToString("0000")+".catl6p"));Log("CONTROL_APPLIED "+handled);
    }catch(Exception e){Log("CONTROL_ERROR "+e.Message);}
   }PumpWait(50);
  }
 }
 static void SelectPreset(EngineWrapper engine,int index,bool backedLiveBuffer=false){
  if(engine.getBoolParamValue("systemState","currentPresetDirtyState")&&!backedLiveBuffer)throw new InvalidOperationException("Unsaved preset changes; refusing selection");
  int channel=(index-1)%2+1,bank=(index-1)/2;
  int rc=Background(()=>engine.setIntParamValue("systemState","currentAmpChannel",channel));
  Log("select channel="+channel+" result="+rc);if(rc!=0)throw new InvalidOperationException("Channel selection failed");PumpWait(1000);
  rc=Background(()=>engine.setIntParamValue("systemState","currentAmpBank",bank));
  Log("select bank="+bank+" result="+rc);if(rc!=0)throw new InvalidOperationException("Bank selection failed");
  PumpWait(3000);
  int actualBank=engine.getIntParamValue("systemState","currentAmpBank"),actualChannel=engine.getIntParamValue("systemState","currentAmpChannel");
  Log("bank/channel readback="+actualBank+"/"+actualChannel);
  if(actualBank!=bank||actualChannel!=channel)throw new InvalidOperationException("Bank/channel selection readback mismatch");
 }
 static string SaveCurrent(EngineWrapper engine,string file){
  if(File.Exists(file))throw new IOException("Refusing to overwrite preset snapshot");
  int rc=Background(()=>engine.setStringParamValue("deviceCommand","savePresetToPath",file));
  if(rc!=0||!File.Exists(file))throw new IOException("Current preset export failed: "+rc);
  var json=new System.Web.Script.Serialization.JavaScriptSerializer();return Canonical(json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file))["data"]);
 }
 static void VerifiedPresets(EngineWrapper engine,string output,bool preserveLive=false){
  if(Directory.Exists(output))throw new IOException("Refusing to reuse verified export folder");
  bool dirty=engine.getBoolParamValue("systemState","currentPresetDirtyState");
  if(dirty&&!preserveLive)throw new InvalidOperationException("Unsaved changes; no presets selected");
  int originalBank=engine.getIntParamValue("systemState","currentAmpBank"),originalChannel=engine.getIntParamValue("systemState","currentAmpChannel");
  int original=originalBank*2+originalChannel;
  Log("Original bank/channel="+originalBank+"/"+originalChannel);
  if(originalChannel<1||originalChannel>2)throw new InvalidOperationException("Manual mode; refusing preset cycling");
  if(original<1||original>12)throw new InvalidOperationException("Expected a stored active preset 1..12");
  Directory.CreateDirectory(output);
  var json=new System.Web.Script.Serialization.JavaScriptSerializer();
  string live=null,liveData=null;
  if(dirty){live=Path.Combine(output,"original-live-buffer.catl6p");liveData=SaveCurrent(engine,live);PumpWait(1000);
   if(liveData!=SaveCurrent(engine,Path.Combine(output,"original-live-buffer-read-2.catl6p")))throw new InvalidOperationException("Live buffer changed during backup; no presets selected");
   Log("Unsaved live buffer preserved in two identical files; will reload only into the edit buffer, without saving device memory");}
  try{for(int index=1;index<=12;index++){
   SelectPreset(engine,index,index==1&&live!=null);string previous=null;
   for(int round=1;round<=2;round++){
    string file=Path.Combine(output,string.Format("preset-{0:00}-read-{1}.catl6p",index,round));
    int rc=Background(()=>engine.setStringParamValue("deviceCommand","savePresetToPath",file));
    if(rc!=0||!File.Exists(file))throw new IOException("Current preset export failed: "+rc);
    var document=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file));
    var data=(Dictionary<string,object>)document["data"];
    string current=Canonical(data);var meta=(Dictionary<string,object>)data["meta"];
    Log("snapshot preset="+index+" round="+round+" name="+meta["name"]+" size="+new FileInfo(file).Length);
    if(previous!=null&&previous!=current)throw new InvalidOperationException("Repeated current-preset reads differ at "+index);
    previous=current;PumpWait(1000);
   }
  }File.WriteAllText(Path.Combine(output,"READS-COMPLETE.txt"),"12 current-preset exports, two identical data reads each. Original active preset="+original);
  }finally{SelectPreset(engine,original);Log("restored original bank/channel="+originalBank+"/"+originalChannel);
   if(live!=null){int rc=Background(()=>engine.setStringParamValue("deviceCommand","loadPresetFromPath",live));Log("Restore volatile live buffer="+rc);if(rc!=0)throw new InvalidOperationException("Live-buffer reload failed; saved file retained");PumpWait(2000);
    if(!SameLiveData(SaveCurrent(engine,Path.Combine(output,"restored-live-buffer.catl6p")),liveData))throw new InvalidOperationException("Restored live-buffer data mismatch; original files retained");Log("Restored live-buffer values match its saved snapshot (known vendor 0/1 flags normalized to bool)");}
  }
 }
 static string Show(object value){
  if(value==null)return "null";
  var list=value as System.Collections.IEnumerable;
  if(list!=null && !(value is string))return "["+string.Join(",",list.Cast<object>().Select(Show).ToArray())+"]";
  return value.ToString();
 }
 static bool Allowed(string g,string p){return (g=="systemState" || g=="stepsSystemState") &&
  (p.StartsWith("currentDevice")||p.StartsWith("currentAmp")||p.StartsWith("deviceList")||p.StartsWith("deviceSync")||p=="engineInterfaceConnectedToDevice"||p=="deviceConnectionStateText");}
#if !IN_PROCESS
 [STAThread]static int Main(string[] args){
  string installed=Environment.GetEnvironmentVariable("CATALYSTIR_EDITOR_HOME")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Line6","Catalyst Edit");
  try{
   if(args.Length==2)args[1]=Path.GetFullPath(args[1]);
   int? staged=VendorRuntime.Stage(installed,"editor",args);if(staged.HasValue)return staged.Value;
  }catch(Exception e){Log(e.ToString());return 1;}
  SetDllDirectory(installed);
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
   string f=Path.Combine(installed,new System.Reflection.AssemblyName(e.Name).Name+".dll");
   return File.Exists(f)?System.Reflection.Assembly.LoadFrom(f):null;};
  try{return Run(args);}catch(Exception e){Log(e.ToString());return 1;}
 }
#endif
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 static int Run(string[] args){
  if(!callbacksInstalled){
  EngineWrapper.SendFloat+=(g,p,v,d)=>{if(Allowed(g,p))Log(g+"/"+p+"="+v);};
  EngineWrapper.sendInt+=(g,p,v,d)=>{if(Allowed(g,p))Log(g+"/"+p+"="+v);};
  EngineWrapper.sendBool+=(g,p,v,d)=>{if(Allowed(g,p))Log(g+"/"+p+"="+v);};
  EngineWrapper.SendString+=(g,p,v,d)=>{if(g=="systemState"&&p=="deviceSyncCompleted")synchronized.Set();if(Allowed(g,p))Log(g+"/"+p+"="+v);};
  EngineWrapper.SendStringArray+=(g,p,v,d)=>{if(Allowed(g,p))Log(g+"/"+p+"="+Show(v));};
  EngineWrapper.SendDictionary+=(g,p,v,d)=>{if(Allowed(g,p))foreach(var kv in v)Log(g+"/"+p+"/"+kv.Key+"="+Show(kv.Value));};
  callbacksInstalled=true;
  }
  var engine=new EngineWrapper();keepEngineAlive=engine;
  try{
   Log("initialize="+engine.initialize("2.01"));Log("start="+engine.start());
   PumpWait(5000);
   var devices=engine.getDictionaryParamValue("systemState","deviceListData");
   if(devices!=null)foreach(var kv in devices)Log("DEVICE "+kv.Key+"="+Show(kv.Value));
   bool slotActivation=false;int targetCabinet=0;
#if MULTI_IR
   slotActivation=args.Length>0&&new[]{"--activate-store-ir-slot0","--activate-store-ir-slot1","--activate-store-ir-slot2"}.Contains(args[0]);
   if(slotActivation)targetCabinet=args[0][args[0].Length-1]-'0';
#endif
   if(args.Length>0 && (slotActivation||args[0]=="--backup" || args[0]=="--status" || args[0]=="--individual" || args[0]=="--binary" || args[0]=="--current-snapshot" || args[0]=="--verified-presets" || args[0]=="--verified-presets-preserve-live" || args[0]=="--activate-ir" || args[0]=="--activate-store-ir" || args[0]=="--control-session")){
    bool doBackup=args[0]=="--backup";
    bool individual=args[0]=="--individual";
    bool binary=args[0]=="--binary";
    bool snapshot=args[0]=="--current-snapshot";
    bool verified=args[0]=="--verified-presets"||args[0]=="--verified-presets-preserve-live";
    bool persist=args[0]=="--activate-store-ir"||slotActivation;
    bool activate=args[0]=="--activate-ir"||persist;
    bool control=args[0]=="--control-session";
    if(args.Length!=(doBackup||individual||binary||snapshot||verified||activate||control?2:1))throw new ArgumentException("Invalid arguments");
    string output=doBackup||individual||binary||snapshot||verified||activate||control?Path.GetFullPath(args[1]):null;
    if(doBackup&&File.Exists(output))throw new IOException("Refusing to overwrite backup");
    Log("connect index 0="+engine.setIntParamValue("systemState","currentDeviceIndex",0));
    for(int i=0;i<150 && !engine.getBoolParamValue("systemState","engineInterfaceConnectedToDevice");i++)PumpWait(100);
    bool connected=engine.getBoolParamValue("systemState","engineInterfaceConnectedToDevice");
    string name=engine.getStringParamValue("systemState","currentDeviceName");
    Log("connected="+connected+"; name="+name+"; firmware="+engine.getStringParamValue("systemState","currentDeviceFlashFirmwareVersionString"));
    if(!connected || name.IndexOf("Catalyst",StringComparison.OrdinalIgnoreCase)<0)throw new InvalidOperationException("Expected a connected Catalyst");
    for(int i=0;i<300&&!synchronized.WaitOne(0);i++)PumpWait(100);
    if(!synchronized.WaitOne(0))throw new InvalidOperationException("Device synchronization did not complete; no archive exported");
    Log("Full device synchronization confirmed");
    Log("current preset before reads="+engine.getIntParamValue("systemState","currentDevicePresetIndex"));
    Log("current preset dirty="+engine.getBoolParamValue("systemState","currentPresetDirtyState"));
    if(control){ControlSession(engine,output);return 0;}
    if(activate){
     int saveIndex=engine.getIntParamValue("systemState","currentDevicePresetIndex"),saveBank=engine.getIntParamValue("systemState","currentAmpBank"),saveChannel=engine.getIntParamValue("systemState","currentAmpChannel");
     if(persist&&(saveIndex<1||saveIndex>12||saveChannel<1||saveChannel>2||saveIndex!=saveBank*2+saveChannel))throw new InvalidOperationException("Cannot safely identify the current stored preset; nothing saved");
     if(Directory.Exists(output))throw new IOException("Refusing to reuse IR activation snapshot folder");Directory.CreateDirectory(output);
     string before=SaveCurrent(engine,Path.Combine(output,"before-activation.catl6p"));
     var activationJson=new System.Web.Script.Serialization.JavaScriptSerializer();
     var beforeData=activationJson.Deserialize<Dictionary<string,object>>(before);var beforeTone=(Dictionary<string,object>)beforeData["tone"];
     var beforeGlobal=(Dictionary<string,object>)beforeTone["global"];
#if AUTOBOOT
     var beforeCab=(Dictionary<string,object>)beforeTone["CatalystGroupDirectOut"];
     if(persist&&Convert.ToBoolean(beforeGlobal["cabMicEnable"])&&Convert.ToInt32(beforeCab["CabinetIndex"])==targetCabinet&&!engine.getBoolParamValue("systemState","currentPresetDirtyState")){
      Log("IR_BOOT_RECALL_SAVED index="+saveIndex+" bank="+saveBank+" channel="+saveChannel+" cabinet="+targetCabinet+"; already stored, no write required; cold boot still requires physical verification");return 0;
     }
#endif
     if(!Convert.ToBoolean(beforeGlobal["cabMicEnable"])){
      int enabled=Background(()=>engine.setBoolParamValue("global","cabMicEnable",true));Log("Enable virtual cabinet for Direct Out/phones/USB="+enabled);if(enabled!=0)return 3;PumpWait(750);
     }
     int otherCabinet=(targetCabinet+1)%3;
     int stockResult=Background(()=>engine.setIntParamValue("CatalystGroupDirectOut","CabinetIndex",otherCabinet));Log("Select alternative cabinet before IR activation="+stockResult);if(stockResult!=0)return 3;PumpWait(1000);
     int activationResult=Background(()=>engine.setIntParamValue("CatalystGroupDirectOut","CabinetIndex",targetCabinet));Log("Set virtual cabinet index="+targetCabinet+" result="+activationResult);if(activationResult!=0)return 3;PumpWait(3000);
     string after=SaveCurrent(engine,Path.Combine(output,"after-activation.catl6p"));var js=new System.Web.Script.Serialization.JavaScriptSerializer();var data=js.Deserialize<Dictionary<string,object>>(after);var tone=(Dictionary<string,object>)data["tone"];var cab=(Dictionary<string,object>)tone["CatalystGroupDirectOut"];
     if(Convert.ToInt32(cab["CabinetIndex"])!=targetCabinet)throw new InvalidOperationException("Cabinet selection not reflected by device snapshot");
     var global=(Dictionary<string,object>)tone["global"];if(!Convert.ToBoolean(global["cabMicEnable"]))throw new InvalidOperationException("Cabinet processing is still bypassed");
     if(persist){
      if(engine.getIntParamValue("systemState","currentDevicePresetIndex")!=saveIndex||engine.getIntParamValue("systemState","currentAmpBank")!=saveBank||engine.getIntParamValue("systemState","currentAmpChannel")!=saveChannel)throw new InvalidOperationException("Active preset changed during activation; nothing saved");
      int stored=Background(()=>engine.setIntParamValue("deviceCommand","saveCurrentPresetToDeviceAtIndex",saveIndex));Log("Store current IR cabinet selection at index="+saveIndex+" result="+stored);if(stored!=0)return 3;PumpWait(2000);
      string storedSnapshot=SaveCurrent(engine,Path.Combine(output,"after-store-live.catl6p"));var storedData=js.Deserialize<Dictionary<string,object>>(storedSnapshot);
      if(Canonical(NormalizeVendorFlags(storedData["tone"]))!=Canonical(NormalizeVendorFlags(data["tone"])))throw new InvalidOperationException("Live tone changed while saving; inspect snapshots");
      if(engine.getBoolParamValue("systemState","currentPresetDirtyState"))throw new InvalidOperationException("The editor still reports unsaved preset changes");
      Log("IR_BOOT_RECALL_SAVED index="+saveIndex+" bank="+saveBank+" channel="+saveChannel+" cabinet="+targetCabinet+"; cold boot still requires physical verification");return 0;
     }
     Log("IR virtual cabinet 112 active in edit buffer; no preset stored");return 0;
    }
    if(verified){VerifiedPresets(engine,output,args[0]=="--verified-presets-preserve-live");return 0;}
    if(snapshot){
     if(File.Exists(output))throw new IOException("Refusing to overwrite current snapshot");PumpWait(1500);
     int result=Background(()=>engine.setStringParamValue("deviceCommand","savePresetToPath",output));
     Log("current snapshot result="+result+" exists="+File.Exists(output));return result==0?0:3;
    }
    if(binary){
     if(Directory.Exists(output))throw new IOException("Refusing to reuse binary export folder");Directory.CreateDirectory(output);
     int result=Background(()=>engine.setStringParamValue("deviceCommand","exportBinaryDevicePresetsToPath",output+Path.DirectorySeparatorChar));
     Log("binary export result="+result+" files="+Directory.GetFiles(output).Length);
     Log("current preset after reads="+engine.getIntParamValue("systemState","currentDevicePresetIndex"));return result==0?0:3;
    }
    if(individual){
     if(Directory.Exists(output))throw new IOException("Refusing to reuse individual export folder");Directory.CreateDirectory(output);
     foreach(int index in new[]{3,8})for(int round=0;round<3;round++){
      string file=Path.Combine(output,"preset-"+index+"-read-"+round+".catl6p");
      var request=new Dictionary<string,object>{{"exportPresetFromDeviceAtIndex_IndexKey",index},{"exportPresetFromDeviceAtIndex_FilePathKey",file}};
      int result=Background(()=>engine.setDictionaryParamValue("deviceCommand","exportPresetFromDeviceAtIndex",request));
      Log("individual index="+index+" round="+round+" result="+result+" exists="+File.Exists(file));if(result!=0)return 3;PumpWait(750);
     }
     Log("current preset after reads="+engine.getIntParamValue("systemState","currentDevicePresetIndex"));return 0;
    }
    if(!doBackup)return 0;
    Log("Export on background worker; owner Windows message queue active");
    int rc=Background(()=>engine.setStringParamValue("deviceCommand","saveDeviceArchiveToPath",output));
    Log("backup result="+rc+"; exists="+File.Exists(output));
    if(rc!=0 || !File.Exists(output) || new FileInfo(output).Length==0)return 2;
   }
   return 0;
  }finally{Background(()=>{Log("stop workers="+engine.setStringParamValue("systemState","tearDownEngine",""));Log("shutdown");engine.shutDown();Log("shutdown complete");GC.KeepAlive(keepEngineAlive);return 0;});}
 }
}
