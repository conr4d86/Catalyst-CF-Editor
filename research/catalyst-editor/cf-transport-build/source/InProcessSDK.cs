using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Load each official C++/CLI wrapper explicitly. LoadFrom unifies these weakly
// named assemblies despite their different versions; LoadFile keeps their types
// distinct. No compile-time SDK reference or child executable is required.
namespace InProcessSDK {
 public abstract class NativeEngine {
  object native;
  Type type;
  static Dictionary<string,Type> types=new Dictionary<string,Type>();
  static List<Delegate> callbackRoots=new List<Delegate>();
  protected NativeEngine(string component){
   if(!types.TryGetValue(component,out type)){
    string path=Path.Combine(OneClickBundle.Root,component,"EngineCLIWrapper.dll");
    var assembly=Assembly.LoadFile(path);
    type=assembly.GetType("EngineCLIWrapper.EngineWrapper",true);
    Type receiver=component=="editor"?typeof(EditorEngine):typeof(UpdaterEngine);
    foreach(string name in new[]{"SendFloat","sendInt","sendBool","SendString","SendStringArray","SendDictionary"}){
     var signal=type.GetEvent(name);var method=receiver.GetMethod("Receive_"+name,BindingFlags.Static|BindingFlags.NonPublic);
     if(signal==null||method==null)throw new MissingMethodException("Missing SDK signal "+name);
     var callback=Delegate.CreateDelegate(signal.EventHandlerType,method);callbackRoots.Add(callback);signal.AddEventHandler(null,callback);
    }
    types.Add(component,type);
   }
   native=Activator.CreateInstance(type);
  }
  object Call(string method,params object[] args){
   try{return type.GetMethod(method).Invoke(native,args);}catch(TargetInvocationException e){throw e.InnerException??e;}
  }
  public bool initialize(string version){return (bool)Call("initialize",version);}
  public bool start(){return (bool)Call("start");}
  public void shutDown(){Call("shutDown");GC.KeepAlive(native);GC.KeepAlive(callbackRoots);}
  public int setIntParamValue(string group,string name,int value){return (int)Call("setIntParamValue",group,name,value);}
  public int setBoolParamValue(string group,string name,bool value){return (int)Call("setBoolParamValue",group,name,value);}
  public int setStringParamValue(string group,string name,string value){return (int)Call("setStringParamValue",group,name,value);}
  public int setDictionaryParamValue(string group,string name,Dictionary<string,object> value){return (int)Call("setDictionaryParamValue",group,name,value);}
  public int getIntParamValue(string group,string name){return (int)Call("getIntParamValue",group,name);}
  public bool getBoolParamValue(string group,string name){return (bool)Call("getBoolParamValue",group,name);}
  public string getStringParamValue(string group,string name){return (string)Call("getStringParamValue",group,name);}
  public Dictionary<string,object> getDictionaryParamValue(string group,string name){return (Dictionary<string,object>)Call("getDictionaryParamValue",group,name);}
  public static string Identities(){return string.Join("; ",types.Select(p=>p.Key+"="+p.Value.Assembly.FullName).ToArray());}
 }
 public class EditorEngine : NativeEngine {
  public EditorEngine():base("editor"){}
  public static event Action<string,string,float,bool> SendFloat;
  public static event Action<string,string,int,bool> sendInt;
  public static event Action<string,string,bool,bool> sendBool;
  public static event Action<string,string,string,bool> SendString;
  public static event Action<string,string,List<string>,bool> SendStringArray;
  public static event Action<string,string,Dictionary<string,object>,bool> SendDictionary;
  static void Receive_SendFloat(string g,string p,float v,bool d){if(SendFloat!=null)SendFloat(g,p,v,d);}
  static void Receive_sendInt(string g,string p,int v,bool d){if(sendInt!=null)sendInt(g,p,v,d);}
  static void Receive_sendBool(string g,string p,bool v,bool d){if(sendBool!=null)sendBool(g,p,v,d);}
  static void Receive_SendString(string g,string p,string v,bool d){if(SendString!=null)SendString(g,p,v,d);}
  static void Receive_SendStringArray(string g,string p,List<string> v,bool d){if(SendStringArray!=null)SendStringArray(g,p,v,d);}
  static void Receive_SendDictionary(string g,string p,Dictionary<string,object> v,bool d){if(SendDictionary!=null)SendDictionary(g,p,v,d);}
 }
 public class UpdaterEngine : NativeEngine {
  public UpdaterEngine():base("central"){}
  public static event Action<string,string,float,bool> SendFloat;
  public static event Action<string,string,int,bool> sendInt;
  public static event Action<string,string,bool,bool> sendBool;
  public static event Action<string,string,string,bool> SendString;
  public static event Action<string,string,List<string>,bool> SendStringArray;
  public static event Action<string,string,Dictionary<string,object>,bool> SendDictionary;
  static void Receive_SendFloat(string g,string p,float v,bool d){if(SendFloat!=null)SendFloat(g,p,v,d);}
  static void Receive_sendInt(string g,string p,int v,bool d){if(sendInt!=null)sendInt(g,p,v,d);}
  static void Receive_sendBool(string g,string p,bool v,bool d){if(sendBool!=null)sendBool(g,p,v,d);}
  static void Receive_SendString(string g,string p,string v,bool d){if(SendString!=null)SendString(g,p,v,d);}
  static void Receive_SendStringArray(string g,string p,List<string> v,bool d){if(SendStringArray!=null)SendStringArray(g,p,v,d);}
  static void Receive_SendDictionary(string g,string p,Dictionary<string,object> v,bool d){if(SendDictionary!=null)SendDictionary(g,p,v,d);}
 }
}

static class OneProcessRuntime {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryEx(string path,IntPtr reserved,uint flags);
 [DllImport("shell32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CommandLineToArgvW(string command,out int count);
 [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr memory);
 static SemaphoreSlim gate=new SemaphoreSlim(1,1);
 static bool initialized;
 static List<IntPtr> moduleRoots=new List<IntPtr>();
 static void EnsureNative(){
  if(initialized)return;
  // The official engines resolve res/ beside the host image. Extract data only,
  // preserving pre-existing files and rejecting incompatible name collisions.
  string host=AppDomain.CurrentDomain.BaseDirectory;
  foreach(string component in new[]{"editor","central"}){
   string source=Path.Combine(OneClickBundle.Root,component);
   foreach(string directory in new[]{"res"}){
    string origin=Path.Combine(source,directory);if(!Directory.Exists(origin))continue;
    foreach(string file in Directory.GetFiles(origin,"*",SearchOption.AllDirectories)){
     string relative=file.Substring(source.Length+1);CopyData(file,Path.Combine(host,relative));
    }
   }
   foreach(string name in new[]{"AssetDefs.plist","L6ConfigAmplifi.xml"})if(File.Exists(Path.Combine(source,name)))CopyData(Path.Combine(source,name),Path.Combine(host,name));
  }
  // Use the updater's libusb for its own imports. Both engines share the exact
  // same official Interposer binary; neither SDK is loaded through a child app.
  foreach(string name in new[]{"msvcp140.dll","vcruntime140.dll","Interposer.dll","libusb-1.0.dll","cyusbserial.dll","pthreadVC2.dll","UpdaterV3EngineDLL.dll"})Load(Path.Combine(OneClickBundle.Root,"central",name));
  Load(Path.Combine(OneClickBundle.Root,"editor","APIV3EngineDLL.dll"));initialized=true;
 }
 static void CopyData(string source,string destination){
  byte[] content=File.ReadAllBytes(source);
  if(File.Exists(destination)){
   if(FirmwareCodec.Hash(File.ReadAllBytes(destination))!=FirmwareCodec.Hash(content))throw new IOException("Hay recursos incompatibles junto a la app: "+destination+". Abre CatalystIR desde una carpeta propia.");
   return;
  }
  Directory.CreateDirectory(Path.GetDirectoryName(destination));
  using(var file=new FileStream(destination,FileMode.CreateNew,FileAccess.Write))file.Write(content,0,content.Length);
 }
 static void Load(string path){
  IntPtr handle=LoadLibraryEx(path,IntPtr.Zero,8);if(handle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"No se pudo cargar "+Path.GetFileName(path));moduleRoots.Add(handle);
 }
 static string[] Arguments(string text){
  int count;IntPtr pointer=CommandLineToArgvW("CatalystIR "+text,out count);if(pointer==IntPtr.Zero)throw new ArgumentException("Argumentos inválidos");
  try{var result=new string[count-1];for(int i=1;i<count;i++)result[i-1]=Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer,i*IntPtr.Size));return result;}finally{LocalFree(pointer);}
 }
 public static async Task<string> Run(string component,string arguments,Action<string> onLine,Func<string,bool> accept){
  var context=SynchronizationContext.Current;
  await gate.WaitAsync();
  try{
   var done=new TaskCompletionSource<string>();
   var thread=new Thread(()=>{
    var output=new System.Text.StringBuilder();object logGate=new object();string installRequest=null;
    Action<string> report=line=>{
     lock(logGate){output.AppendLine(line);if(line.StartsWith("AWAIT_INSTALL="))installRequest="install:"+line.Substring(14);}
     if(onLine!=null){if(context==null)onLine(line);else context.Post(_=>onLine(line),null);}
    };
    try{
     EnsureNative();report("IN_PROCESS pid="+System.Diagnostics.Process.GetCurrentProcess().Id+" component="+component);
     string[] args=Arguments(arguments);
     Func<string> input=()=>{
      bool yes=false;Action approve=()=>{try{yes=accept!=null&&installRequest!=null&&accept(installRequest);}catch(Exception e){report(e.Message);}};
      if(context==null)approve();else context.Send(_=>approve(),null);
      return yes&&args.Length>=4?"ACCEPT "+args[3]:"REJECT";
     };
     int code=component=="editor"?EngineProbe.Execute(args,report):component=="central"?CentralAdapter.Execute(args,report,input):-1;
     report("SDK_IDENTITIES "+InProcessSDK.NativeEngine.Identities());
     if(code!=0)throw new InvalidOperationException("Comunicación USB: código "+code+"."+Environment.NewLine+output.ToString());
     done.SetResult(output.ToString());
    }catch(Exception e){report(e.ToString());done.SetException(e);}
   });
   thread.SetApartmentState(ApartmentState.STA);thread.Start();return await done.Task;
  }finally{gate.Release();}
 }
}
