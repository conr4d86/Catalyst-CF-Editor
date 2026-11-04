using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;
#if IN_PROCESS
using EngineWrapper = InProcessSDK.UpdaterEngine;
#else
using EngineCLIWrapper;
#endif

// --prepare always declines EULA. --install requires a verified candidate,
// matching backup, exact SHA-256 and an explicit stdin ACCEPT <sha256> response.
class CentralAdapter {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool SetDllDirectory(string path);
 static object gate=new object();static ManualResetEvent eula=new ManualResetEvent(false);
 static EngineWrapper keepEngineAlive;
 static ManualResetEvent terminal=new ManualResetEvent(false);static volatile bool success=false;
 static string licenseHtml="";
 static byte[] Template(string path){return TemplateSource.Load(path);}
 static Action<string> logSink;
 static Func<string> inputSource;
 static bool callbacksInstalled;
 static void Log(string s){lock(gate){if(logSink!=null)logSink(s);else{Console.WriteLine(s);Console.Out.Flush();}}}
#if IN_PROCESS
 public static int Execute(string[] args,Action<string> output,Func<string> input){
  logSink=output;inputSource=input;eula.Reset();terminal.Reset();success=false;licenseHtml="";
  try{return Run(args);}catch(Exception e){Log(e.ToString());return 1;}finally{logSink=null;inputSource=null;}
 }
#endif
 static string Show(object v){if(v==null)return "null";var a=v as System.Collections.IEnumerable;
  return a!=null&&!(v is string)?"["+string.Join(",",a.Cast<object>().Select(Show).ToArray())+"]":v.ToString();}
 static bool Allowed(string g,string p){return g=="stepsSystemState"||g=="systemState"||g.StartsWith("engineUpdateProcess");}
#if !IN_PROCESS
 static int Main(string[] args){
  string dir=Environment.GetEnvironmentVariable("CATALYSTIR_CENTRAL_HOME")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Line6","Line 6 Central");
  try{
   if(args.Length>=2)args[1]=Path.GetFullPath(args[1]);
   if(args.Length>=3)args[2]=Path.GetFullPath(args[2]);
   if(args.Length==5)args[4]=Path.GetFullPath(args[4]);
   int? staged=VendorRuntime.Stage(dir,"central",args);if(staged.HasValue)return staged.Value;
  }catch(Exception e){Log(e.ToString());return 1;}
  SetDllDirectory(dir);AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
   string f=Path.Combine(dir,new System.Reflection.AssemblyName(e.Name).Name+".dll");
   return File.Exists(f)?System.Reflection.Assembly.LoadFrom(f):null;};
  try{return Run(args);}catch(Exception e){Log(e.ToString());return 1;}
 }
#endif
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 static int Run(string[] args){
  // The embedded recipe fixes all executable/bootstrap bytes. Candidate
  // validation below permits only IR coefficient/bank changes, including gain.
  // Failed legacy code cannot pass that comparison. Firmware writes still
  // require an exact candidate hash, verified backup and explicit acceptance.
  if(!callbacksInstalled){
  EngineWrapper.SendFloat+=(g,p,v,d)=>{if(Allowed(g,p))Log(g+"/"+p+"="+v);};
  EngineWrapper.sendInt+=(g,p,v,d)=>{if(Allowed(g,p))Log(g+"/"+p+"="+v);};
  EngineWrapper.sendBool+=(g,p,v,d)=>{if(Allowed(g,p))Log(g+"/"+p+"="+v);};
  EngineWrapper.SendString+=(g,p,v,d)=>{if(Allowed(g,p)){
   if(v!=null&&v.Contains("<html"))Log(g+"/"+p+"=<HTML omitted>");else Log(g+"/"+p+"="+v);
   if(p=="engineUpdateProcessPostUpdateDeviceArrived"){success=true;terminal.Set();}
   if(p=="engineUpdateProcessCompleted"){success=true;terminal.Set();}
   if(p=="firmwareUpdateFailed"||p=="engineUpdateProcessFailed")terminal.Set();
   if(p.IndexOf("eula",StringComparison.OrdinalIgnoreCase)>=0 || v=="displayEula")eula.Set();}};
  EngineWrapper.SendStringArray+=(g,p,v,d)=>{if(Allowed(g,p)){
   if(p=="engineUpdateProcessClientStep_displayEula"){if(v.Count>2)licenseHtml=v[2];eula.Set();}
   if(p=="engineUpdateProcessClientStep_updateFailed")terminal.Set();
   if(p=="engineUpdateProcessClientStep_displayStaticInfoViewDismissableOK"&&v.Count>2&&v[2].Contains("Firmware Update Failed"))terminal.Set();
   string value=Show(v);Log(g+"/"+p+"="+(value.Length>500?"<long value omitted>":value));}};
  EngineWrapper.SendDictionary+=(g,p,v,d)=>{if(Allowed(g,p))foreach(var kv in v){
   string value=Show(kv.Value);Log(g+"/"+p+"/"+kv.Key+"="+(value.Length>500?"<long value omitted>":value));
   if(value=="displayEula")eula.Set();}};
  callbacksInstalled=true;
  }
  var engine=new EngineWrapper();keepEngineAlive=engine;
  try{
   Log("initialize="+engine.initialize("1.05"));Log("start="+engine.start());Thread.Sleep(5000);
   foreach(string group in new[]{"systemState","stepsSystemState"}){
    var devices=engine.getDictionaryParamValue(group,"deviceListData");
    if(devices!=null)foreach(var kv in devices)Log(group+" DEVICE "+kv.Key+"="+Show(kv.Value));
   }
   if(args.Length==0)return 0;
   bool recovery=args.Length==4&&args[0]=="--recover";
   bool install=(args.Length==5&&args[0]=="--install")||recovery;
   if(!install&&(args.Length!=3 || args[0]!="--prepare"))throw new ArgumentException("Use --prepare <ctf> <official>, --install <ctf> <backup> <sha256> <official>, or --recover <official> <backup> <sha256>");
   if(!File.Exists(args[1]))throw new FileNotFoundException(args[1]);
   byte[] candidate=File.ReadAllBytes(args[1]);string sha=FirmwareCodec.Hash(candidate);
   if(recovery){if(sha!="f9fe2482e561bef8a04128a38af89cadee4f8d06071b06a9053e36c206471ae8")throw new InvalidDataException("Recovery accepts only the verified official firmware");FirmwareCodec.Parse(candidate);}
   else FirmwareCodec.ValidateCandidate(Template(args[install?4:2]),candidate);
   if(install){if(args[3]!=sha)throw new InvalidDataException("Candidate hash mismatch");FirmwareCodec.VerifyPreservedBackup(args[2]);}
   int expectedProduct=install?FamilyProfile.BackupProduct(args[2]):0;
   var listing=engine.getDictionaryParamValue("systemState","deviceListData");
   var ids=((System.Collections.IEnumerable)listing["deviceListData_IdentifiersKey"]).Cast<object>().Select(Convert.ToInt32).ToList();
   int index=FamilyProfile.TargetIndex(ids,recovery,expectedProduct);
   if(recovery&&FamilyProfile.IsBootloader(ids[index])){
    var identity=FamilyProfile.RecoveryIdentity(index,ids[index],args[2]);
    int identified=engine.setDictionaryParamValue("stepsSystemState","setDeviceIdentity",identity);Log("Identify recovery target from verified product backup="+identified);
    if(identified!=0)throw new InvalidOperationException("Bootloader identification failed");Thread.Sleep(1000);
   }
   Log("select index "+index+"="+engine.setIntParamValue("systemState","currentDeviceIndex",index));
   Thread.Sleep(5000);
   int id=engine.getIntParamValue("stepsSystemState","currentDeviceID");
   if(id<=0)id=engine.getIntParamValue("systemState","currentDeviceID");
   Log("product="+id);
   if(!FamilyProfile.IsModel(id)||install&&id!=expectedProduct)throw new InvalidOperationException("La copia no corresponde al modelo conectado");
   FamilyProfile.ConnectedProduct=id;
   string version=engine.getStringParamValue("stepsSystemState","currentDeviceFlashFirmwareVersionString");
   Log("firmware="+version);
   if(install && !recovery && version!="2.02")throw new InvalidOperationException("Expected firmware 2.02");
   var request=new Dictionary<string,object>{{"updateDeviceFirmwareOffline_imagePath",Path.GetFullPath(args[1])},{"updateDeviceFirmwareOffline_stepsPath",""}};
   Log("prepare="+engine.setDictionaryParamValue("systemState","updateDeviceFirmwareOffline",request));
   bool reached=eula.WaitOne(10000);Log("Updater ready="+reached);
   if(install&&reached){
#if IN_PROCESS
    // The app's Install IR action authorizes this exact validated image.
    // No license dialog/file or separate consent record is produced by the app.
    Log("AWAIT_INSTALL="+sha);
#else
    string license=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])),"Line6-EULA-"+sha.Substring(0,12)+"-"+Guid.NewGuid().ToString("N")+".txt");
    string html=licenseHtml;
    try{string repaired=new System.Text.UTF8Encoding(false,true).GetString(System.Text.Encoding.GetEncoding(1252).GetBytes(html));if(repaired.IndexOf('\ufffd')<0)html=repaired;}catch(System.Text.DecoderFallbackException){}
    html=System.Text.RegularExpressions.Regex.Replace(html,@"</(?:p|h[1-6]|li)>|<br\s*/?>",Environment.NewLine+Environment.NewLine,System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    string text=System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html,"<[^>]+>"," "));
    File.WriteAllText(license,text,System.Text.Encoding.UTF8);Log("AWAIT_EULA="+license);
#endif
    string response=inputSource!=null?inputSource():Console.ReadLine();
    if(response=="ACCEPT "+sha){
     if(FirmwareCodec.Hash(File.ReadAllBytes(args[1]))!=sha)throw new InvalidDataException("Candidate changed after confirmation");
     Log("INSTALL_REQUEST_CONFIRMED");
     Log("accept="+engine.setStringParamValue("engineUpdateProcessClientResponse","engineUpdateProcessClientResponse_eulaAccepted",""));
     // The native engine performs all USB update stages. Stay alive until a
     // terminal notification; never terminate a transfer on an arbitrary timer.
     while(!terminal.WaitOne(1000))Log("UPDATE_ACTIVE");
     Log(success?"UPDATE_ENGINE_COMPLETED; VERIFY_DEVICE_BOOT_SEPARATELY":"UPDATE_FAILED");return success?0:3;
    }
   }
   Log("decline="+engine.setStringParamValue("engineUpdateProcessClientResponse","engineUpdateProcessClientResponse_eulaRejected",""));
   Log("Preflight stopped before acceptance, reboot or flash transfer.");Thread.Sleep(500);
   return reached?0:2;
  }finally{
   // Match the vendor application's two-stage shutdown. Destroying the
   // engine directly can leave scanner/transport threads running.
   Log("stop workers="+engine.setStringParamValue("systemState","tearDownEngine",""));
   Log("shutdown");engine.shutDown();Log("shutdown complete");GC.KeepAlive(keepEngineAlive);
  }
 }
}
