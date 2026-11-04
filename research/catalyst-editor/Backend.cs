using System;using System.IO;using System.Linq;using System.IO.Compression;using System.Reflection;using System.Runtime.InteropServices;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;using System.Windows.Forms;
namespace CFEditor {
interface IBackend {
 bool Connected{get;} bool Preview{get;} int ProductID{get;}
 event Action<string,string,object> Changed;event Action<string> Status;
 Task Connect();Task Disconnect();Task Read(IEnumerable<Binding> refs);Task Set(Binding b,object value);Task SelectAsset(string group,string asset);Task SelectPreset(int bank,int channel);Task Command(string command,object value);
}
// Preview contains generated demonstration values, never user/device data.
sealed class PreviewBackend:IBackend {
 readonly Dictionary<string,object> state=new Dictionary<string,object>();public readonly List<string> Writes=new List<string>();
 public bool Connected{get;private set;}public bool Preview{get{return true;}}public int ProductID{get{return 2424833;}}
 public event Action<string,string,object> Changed;public event Action<string> Status;
 public PreviewBackend(){state["CatalystGroupAmp/graphSlot_AssetEngineID"]="Chime";state["CatalystGroupEffect/graphSlot_AssetEngineID"]="SimpleDelay";state["CatalystGroupReverb/graphSlot_AssetEngineID"]="Ganymede";state["meta/currentPresetName"]="PREVISUALIZACIÓN";state["systemState/currentAmpBank"]=2;state["systemState/currentAmpChannel"]=1;state["CFPreamp/type"]=3;state["CFPreamp/level"]=50;state["CFPreamp/tone"]=50;state["CFPreamp/boost"]=false;state["CFPreamp/bright"]=false;}
 public Task Connect(){Connected=true;if(Status!=null)Status("Vista de diseño · Sin conexión USB");return Task.FromResult(0);}
 public Task Disconnect(){Connected=false;return Task.FromResult(0);}
 public Task Read(IEnumerable<Binding> refs){foreach(var b in refs){object v;if(!state.TryGetValue(b.Key,out v))v=b.Param.kind=="bool"?(object)true:(object)(int)Math.Round((b.Param.minimum+b.Param.maximum)/2);if(Changed!=null)Changed(b.Group,b.Param.id,v);}return Task.FromResult(0);}
 public Task Set(Binding b,object value){if(!b.Editable)throw new InvalidOperationException("Parámetro de solo lectura");if(!Connected)throw new InvalidOperationException("Sin conexión");b.Param.Validate(value);Writes.Add(b.Key);state[b.Key]=value;if(Changed!=null)Changed(b.Group,b.Param.id,value);return Task.FromResult(0);}
 public Task SelectAsset(string group,string asset){state[group+"/graphSlot_AssetEngineID"]=asset;Writes.Add(group+"/@asset");if(Changed!=null)Changed(group,"graphSlot_AssetEngineID",asset);return Task.FromResult(0);}
 public async Task SelectPreset(int bank,int channel){await Set(new Binding("systemState",Catalog.Int("currentAmpChannel","Canal",0,2)),channel);await Set(new Binding("systemState",Catalog.Int("currentAmpBank","Banco",0,5)),bank);}
 public Task Command(string name,object value){throw new InvalidOperationException("Las operaciones del amplificador no se ejecutan en la vista de diseño.");}
}
static class EditorRuntime {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryEx(string file,IntPtr unused,uint flags);
 static readonly List<IntPtr> modules=new List<IntPtr>();public static string Root;public static bool SaveSelectorReadbackQuirk;
 public static void Prepare(){
  if(Root!=null)return;byte[] data;using(var r=Assembly.GetExecutingAssembly().GetManifestResourceStream("editor-runtime.zip"))using(var m=new MemoryStream()){r.CopyTo(m);data=m.ToArray();}
  string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CatalystCFEditor","runtime",FirmwareCodec.Hash(data).Substring(0,24));Directory.CreateDirectory(root);
  using(var z=new ZipArchive(new MemoryStream(data),ZipArchiveMode.Read)){
   Dictionary<string,string>[] entries;using(var r=new StreamReader(z.GetEntry("manifest.json").Open()))entries=new JavaScriptSerializer().Deserialize<Dictionary<string,string>[]>(r.ReadToEnd());
   var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var e in entries){string n=e["path"],file=Path.GetFullPath(Path.Combine(root,n.Replace('/',Path.DirectorySeparatorChar)));if(!file.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!n.StartsWith("editor/",StringComparison.Ordinal)||!names.Add(file))throw new InvalidDataException("Inventario de runtime incorrecto");byte[] b;using(var r=z.GetEntry(n).Open())using(var m=new MemoryStream()){r.CopyTo(m);b=m.ToArray();}if(FirmwareCodec.Hash(b)!=e["sha256"])throw new InvalidDataException("Runtime alterado");
    if(!File.Exists(file)||FirmwareCodec.Hash(File.ReadAllBytes(file))!=e["sha256"]){Directory.CreateDirectory(Path.GetDirectoryName(file));string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllBytes(temp,b);if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);}
   }
  }
  string dir=Path.Combine(root,"editor"),host=AppDomain.CurrentDomain.BaseDirectory;
  foreach(string file in Directory.GetFiles(Path.Combine(dir,"res"),"*",SearchOption.AllDirectories))CopyData(file,Path.Combine(host,file.Substring(dir.Length+1)));
  foreach(string name in new[]{"AssetDefs.plist","L6ConfigAmplifi.xml"})if(File.Exists(Path.Combine(dir,name)))CopyData(Path.Combine(dir,name),Path.Combine(host,name));
  foreach(string n in new[]{"msvcp140.dll","vcruntime140.dll","Interposer.dll","APIV3EngineDLL.dll"}){IntPtr h=LoadLibraryEx(Path.Combine(dir,n),IntPtr.Zero,8);if(h==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"No se pudo cargar "+n);modules.Add(h);}SaveSelectorReadbackQuirk=FirmwareCodec.Hash(File.ReadAllBytes(Path.Combine(dir,"APIV3EngineDLL.dll")))=="6d5325b93e3f8b973525cbf97cba38606c5af56aab48a07e8a9e058f6b23d1aa";Root=dir;
 }
 static void CopyData(string from,string to){var b=File.ReadAllBytes(from);if(File.Exists(to)){if(FirmwareCodec.Hash(File.ReadAllBytes(to))!=FirmwareCodec.Hash(b))throw new IOException("Recursos incompatibles junto al editor. Usa su carpeta propia.");return;}Directory.CreateDirectory(Path.GetDirectoryName(to));using(var s=new FileStream(to,FileMode.CreateNew,FileAccess.Write))s.Write(b,0,b.Length);}
}
// One explicitly connected SDK, one serialized command queue, rooted delegates.
// No updater APIs, cloud services or helper executables. The optional CF
// channel borrows reviewed SDK handles through scoped imports in this process.
sealed partial class NativeBackend:IBackend {
 static readonly List<NativeBackend> unconfirmedShutdowns=new List<NativeBackend>();IAssetPortLease line6Lease;volatile bool shutdownConfirmed=true;
 readonly SynchronizationContext ui;readonly bool useCFControls;readonly object queueLock=new object();readonly Queue<Action> queue=new Queue<Action>();
 Thread thread;TaskCompletionSource<bool> shutdownSettled;ApplicationContext context;object native;Type wrapper;readonly List<Delegate> roots=new List<Delegate>();System.Windows.Forms.Timer timer;
 volatile bool connected,synced;volatile int product;bool stopped,connecting;Mutex ownership;int generation;volatile bool awaitingStore;volatile int storeReceipt;
 internal bool NativeInitializationAttempted{get;private set;}
 public bool Connected{get{return connected&&synced&&DeviceFamily.Supported(product);}}public bool Preview{get{return false;}}public int ProductID{get{return product;}}
 public event Action<string,string,object> Changed;public event Action<string> Status;
 public NativeBackend(bool enableCFControls=true){useCFControls=enableCFControls;ui=SynchronizationContext.Current??new WindowsFormsSynchronizationContext();}
 void Post(Action a){int epoch=generation;try{ui.Post(_=>{if(epoch==generation)a();},null);}catch(System.ComponentModel.InvalidAsynchronousStateException){}catch(ObjectDisposedException){}}void Report(string s){Post(()=>{if(Status!=null)Status(s);});}
 void Receive(string g,string p,object v){
  if(g=="CatalystGroupAmp"&&p=="graphSlot_AssetEngineID"){string model=Convert.ToString(v);preampNamSlot=model=="Chime"?0:model=="Chime 2"?1:-1;preampModelSupported=PreampAmpModel(model);}
  if(awaitingStore&&g=="devicePresetSaveTarget"){if(p=="saveDevicePresetToCurrentTargetSucceeded")storeReceipt=1;if(p=="saveDevicePresetToCurrentTargetFailed")storeReceipt=-1;}
  if(g=="systemState"&&p=="deviceSyncStarted"){int id;if(int.TryParse(Convert.ToString(v),out id))product=id;synced=false;}
  if(g=="systemState"&&p=="deviceSyncCompleted")synced=true;
  if(g=="systemState"&&p=="engineInterfaceConnectedToDevice"){connected=Convert.ToBoolean(v);if(!connected)synced=false;}
  if(g=="systemState"&&p=="deviceConnectionStateText")Report(Convert.ToString(v));
  Post(()=>{if(Changed!=null)Changed(g,p,v);});
 }
 public void OnFloat(string g,string p,float v,bool d){Receive(g,p,v);}public void OnInt(string g,string p,int v,bool d){Receive(g,p,v);}public void OnBool(string g,string p,bool v,bool d){Receive(g,p,v);}public void OnString(string g,string p,string v,bool d){Receive(g,p,v);}public void OnStrings(string g,string p,List<string> v,bool d){Receive(g,p,v);}public void OnDictionary(string g,string p,Dictionary<string,object> v,bool d){Receive(g,p,v);}
 object Invoke(string method,params object[] args){try{return wrapper.GetMethod(method).Invoke(native,args);}catch(TargetInvocationException e){throw e.InnerException??e;}}
 T Pump<T>(Func<T> work){var t=Task.Run(work);while(!t.IsCompleted){Application.DoEvents();Thread.Sleep(1);}return t.GetAwaiter().GetResult();}
 object Call(string method,params object[] args){return Pump(()=>Invoke(method,args));}
 void Need(){if(!Connected)throw new InvalidOperationException("Conecta y sincroniza un Catalyst 60, 100 o 200 compatible antes de enviar cambios.");}
 Task Enqueue(Action action){var done=new TaskCompletionSource<bool>();lock(queueLock){if(stopped||thread==null)throw new InvalidOperationException("Conecta primero el editor");queue.Enqueue(()=>{try{action();done.TrySetResult(true);}catch(Exception e){done.TrySetException(e);}});}return done.Task;}
 internal static void RefuseOtherUSBApps(){
  foreach(var p in System.Diagnostics.Process.GetProcesses()){using(p){string name;try{name=p.ProcessName;}catch{continue;}if(new[]{"Catalyst Edit","CatalystIR","POD Go Edit","PODGoNAMManager","POD Go NAM Manager","Line 6 Updater","CatalystUSBNormal","CatalystUSBCompat"}.Contains(name,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("Cierra "+name+" antes de conectar el editor.");}}
 }
 public async Task Connect(){
  if(connecting||Connected)return;if(thread!=null)await Disconnect();RefuseOtherUSBApps();
  connecting=true;generation++;Report("Preparando la comunicación USB…");var ready=new TaskCompletionSource<bool>();shutdownSettled=new TaskCompletionSource<bool>();
  thread=new Thread(()=>{
   try{
    line6Lease=new Line6USBLease();ownership=new Mutex(false,"Local\\Conr4d.CatalystCFEditor.SDK");bool acquired;try{acquired=ownership.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}if(!acquired)throw new InvalidOperationException("Otro editor CF está usando la conexión");
    EditorRuntime.Prepare();wrapper=Assembly.LoadFile(Path.Combine(EditorRuntime.Root,"EngineCLIWrapper.dll")).GetType("EngineCLIWrapper.EngineWrapper",true);
    string[] events={"SendFloat","sendInt","sendBool","SendString","SendStringArray","SendDictionary"},methods={"OnFloat","OnInt","OnBool","OnString","OnStrings","OnDictionary"};
    for(int i=0;i<events.Length;i++){var e=wrapper.GetEvent(events[i]);var d=Delegate.CreateDelegate(e.EventHandlerType,this,methods[i]);roots.Add(d);e.AddEventHandler(null,d);}if(useCFControls)PrepareCFControls();native=Activator.CreateInstance(wrapper);shutdownConfirmed=false;
    NativeInitializationAttempted=true;if(!(bool)Call("initialize","2.01")||!(bool)Call("start"))throw new IOException("No se pudo iniciar la comunicación");Report("Buscando el amplificador en el motor USB…");
    var until=DateTime.UtcNow.AddSeconds(8);while(DateTime.UtcNow<until&&!connected){Application.DoEvents();Thread.Sleep(10);}if(!connected)Call("setIntParamValue","systemState","currentDeviceIndex",0);
    until=DateTime.UtcNow.AddSeconds(40);while(DateTime.UtcNow<until&&!(connected&&synced)){Application.DoEvents();Thread.Sleep(10);}Need();
    string device=(string)Call("getStringParamValue","systemState","currentDeviceName");if(device==null||device.IndexOf("Catalyst",StringComparison.OrdinalIgnoreCase)<0)throw new IOException("Dispositivo inesperado");
    if(useCFControls)StartCFControls();context=new ApplicationContext();timer=new System.Windows.Forms.Timer{Interval=25};bool executing=false;timer.Tick+=delegate{if(executing)return;Action a=null;lock(queueLock){if(queue.Count>0)a=queue.Dequeue();}if(a!=null){executing=true;try{a();}finally{executing=false;}}};timer.Start();ready.SetResult(true);Report("Conectado · "+device);Application.Run(context);
   }catch(Exception e){ready.TrySetException(e);Report(e.Message);}finally{FinishWorker();}
  });thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();
  Exception failure=null;try{await ready.Task;}catch(Exception error){failure=error;}finally{connecting=false;}
  if(failure!=null){bool closed=await shutdownSettled.Task;if(!closed)throw new IOException("El motor USB no ha confirmado su cierre; no se abrirá otra conexión.",failure);await Task.Run(()=>thread.Join());thread=null;context=null;throw failure;}
 }
 void FinishWorker(){Shutdown();if(shutdownSettled!=null)shutdownSettled.TrySetResult(shutdownConfirmed);if(!shutdownConfirmed)new ManualResetEvent(false).WaitOne();}
 void Shutdown(){
  try{ShutdownCore();}catch(Exception error){
   shutdownConfirmed=false;connected=synced=false;Line6USBLease.BlockAfterUnconfirmedClose();lock(unconfirmedShutdowns)if(!unconfirmedShutdowns.Contains(this))unconfirmedShutdowns.Add(this);
   // Keep both mutexes on their owning native worker. An abandoned mutex
   // would otherwise let another process reuse the uncertain SDK handles.
   try{Report("No se ha confirmado el cierre del motor USB: "+error.Message);}catch{}
  }
 }
 void ShutdownCore(){
  if(timer!=null){timer.Stop();timer.Dispose();timer=null;}
  bool closed=native==null;
  if(native!=null){try{Call("setStringParamValue","systemState","tearDownEngine","");Call("shutDown");closed=true;}catch(Exception e){Line6USBLease.BlockAfterUnconfirmedClose();lock(unconfirmedShutdowns)if(!unconfirmedShutdowns.Contains(this))unconfirmedShutdowns.Add(this);Report("El motor no ha confirmado el cierre: "+e.Message);}GC.KeepAlive(native);if(closed)native=null;}
  if(closed)try{CloseCFControls();}catch(Exception error){closed=false;Line6USBLease.BlockAfterUnconfirmedClose();lock(unconfirmedShutdowns)if(!unconfirmedShutdowns.Contains(this))unconfirmedShutdowns.Add(this);Report("El canal CF no ha confirmado su cierre: "+error.Message);}
  shutdownConfirmed=closed;
  if(closed&&wrapper!=null){string[] names={"SendFloat","sendInt","sendBool","SendString","SendStringArray","SendDictionary"};for(int i=0;i<roots.Count;i++)wrapper.GetEvent(names[i]).RemoveEventHandler(null,roots[i]);roots.Clear();}
  connected=synced=false;if(closed){if(ownership!=null){try{ownership.ReleaseMutex();}catch(ApplicationException){}ownership.Dispose();ownership=null;}if(line6Lease!=null){line6Lease.Dispose();line6Lease=null;}}Post(()=>{if(Changed!=null)Changed("systemState","engineInterfaceConnectedToDevice",false);});
 }
 public async Task Disconnect(){if(thread==null){if(!shutdownConfirmed)throw new IOException("El cierre USB anterior no está confirmado.");return;}if(connecting)throw new InvalidOperationException("Espera a que termine la conexión");if(shutdownSettled!=null&&shutdownSettled.Task.IsCompleted&&!shutdownSettled.Task.Result)throw new IOException("El cierre USB anterior no está confirmado. La conexión sigue reservada.");if(thread.IsAlive&&context!=null)await Enqueue(()=>{stopped=true;context.ExitThread();});if(shutdownSettled!=null&&!await shutdownSettled.Task)throw new IOException("El motor USB no ha confirmado su cierre; la conexión sigue reservada.");await Task.Run(()=>thread.Join());thread=null;context=null;stopped=false;if(!shutdownConfirmed)throw new IOException("El motor USB no ha confirmado su cierre; no se abrirá otra conexión.");Report("Desconectado · USB libre");}
 public Task Read(IEnumerable<Binding> refs){var list=refs.Where(x=>x.Group!="CFPreamp").GroupBy(x=>x.Key).Select(x=>x.First()).ToArray();return Enqueue(()=>{Need();Pump(()=>{foreach(var b in list){try{object v=Invoke(b.Param.kind=="bool"?"getBoolParamValue":b.Param.kind=="string"?"getStringParamValue":b.Param.kind=="strings"?"getStringArrayParamValue":"getIntParamValue",b.Group,b.Param.id);Receive(b.Group,b.Param.id,v);}catch(Exception e){Report("No disponible: "+b.Param.name+" · "+e.Message);}}return true;});if(refs.Any(x=>x.Group=="CFPreamp")){ReadCFPreamp();ReadCFAssetNames();}});}
 internal async Task<PresetSaveContext> ReadSaveContext(){
  PresetSaveContext context=null;
  await Enqueue(()=>{Need();context=PresetSaveContext.Read((g,p)=>Convert.ToInt32(Call("getIntParamValue",g,p)));Receive("systemState","currentAmpBank",context.Bank);Receive("systemState","currentAmpChannel",context.Channel);Receive("systemState","currentDevicePresetIndex",context.Index);});
  return context;
 }
 public Task Set(Binding b,object value){if(!b.Editable)throw new InvalidOperationException("Parámetro de solo lectura");b.Param.Validate(value);return Enqueue(()=>{Need();if(b.Group=="CFPreamp"){SetCFPreamp(b,value);return;}if(b.AssetID!=null&&!string.Equals(Convert.ToString(Call("getStringParamValue",b.Group,"graphSlot_AssetEngineID")),b.AssetID,StringComparison.Ordinal))throw new IOException("Ha cambiado el modelo; vuelve a leer sus ajustes");int rc=Convert.ToInt32(Call(b.Param.kind=="bool"?"setBoolParamValue":"setIntParamValue",b.Group,b.Param.id,b.Param.kind=="bool"?(object)Convert.ToBoolean(value):Convert.ToInt32(value)));if(rc!=0)throw new IOException("Cambio rechazado ("+rc+"): "+b.Param.name);object read=Call(b.Param.kind=="bool"?"getBoolParamValue":"getIntParamValue",b.Group,b.Param.id);Receive(b.Group,b.Param.id,read);});}
 public Task SelectAsset(string group,string asset){if(!new[]{"CatalystGroupAmp","CatalystGroupEffect","CatalystGroupReverb"}.Contains(group))throw new ArgumentException("Grupo no editable");var c=Catalog.Load();if(!(group=="CatalystGroupAmp"?c.Amps:c.Effects).Any(a=>a.id==asset))throw new ArgumentException("Modelo no compatible");return Enqueue(()=>{Need();int rc=Convert.ToInt32(Call("setStringParamValue",group,"graphSlot_AssetEngineID",asset));if(rc!=0)throw new IOException("Modelo rechazado: "+rc);Receive(group,"graphSlot_AssetEngineID",Call("getStringParamValue",group,"graphSlot_AssetEngineID"));});}
 public Task SelectPreset(int bank,int channel){if(bank<0||bank>5||channel<0||channel>2)throw new ArgumentOutOfRangeException();return Enqueue(()=>{Need();foreach(var pair in new[]{new KeyValuePair<string,int>("currentAmpChannel",channel),new KeyValuePair<string,int>("currentAmpBank",bank)}){int rc=Convert.ToInt32(Call("setIntParamValue","systemState",pair.Key,pair.Value));if(rc!=0)throw new IOException("Selección de preset rechazada: "+rc);}Receive("systemState","presetDidChange","");});}
 public Task Command(string command,object value){
  string[] paths={"savePresetToPath","loadPresetFromPath","saveDeviceArchiveToPath","restoreDeviceWithArchiveFromPath"};
  if(!paths.Contains(command)&&command!="saveCurrentPresetToDeviceAtIndex"&&command!="rename")throw new ArgumentException("Operación no permitida");
  return Enqueue(()=>{Need();
   if(command=="saveCurrentPresetToDeviceAtIndex"){
    int index=Convert.ToInt32(value);var destination=PresetSaveContext.Read((g,p)=>Convert.ToInt32(Call("getIntParamValue",g,p)));
    if(index!=destination.Index)throw new IOException("El preset ha cambiado desde la confirmación; no se ha guardado");
    string backup=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CatalystCFEditor","backups",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-ffff"));Directory.CreateDirectory(backup);int br=Convert.ToInt32(Call("setStringParamValue","deviceCommand","savePresetToPath",Path.Combine(backup,"before.catl6p")));if(br!=0||!File.Exists(Path.Combine(backup,"before.catl6p")))throw new IOException("No se pudo respaldar el preset");
    var afterBackup=PresetSaveContext.Read((g,p)=>Convert.ToInt32(Call("getIntParamValue",g,p)));if(afterBackup.Bank!=destination.Bank||afterBackup.Channel!=destination.Channel)throw new IOException("Ha cambiado el preset; no se ha guardado");
    var preampEdit=CaptureCFPreamp();
    try{
     PresetStore.Commit(index,(g,p)=>Convert.ToInt32(Call("getIntParamValue",g,p)),(g,p,v)=>{int result=Convert.ToInt32(Call("setIntParamValue",g,p,v));if(result!=0)Report("Retorno SDK: "+g+"/"+p+"; código "+result+"; se comprueba el destino; copia: "+backup);return result;},()=>storeReceipt,()=>{storeReceipt=0;awaitingStore=true;},()=>{Application.DoEvents();System.Threading.Thread.Sleep(5);},12000,EditorRuntime.SaveSelectorReadbackQuirk);Need();SaveCFPreamp(preampEdit);Report("Preset guardado y confirmado por el amplificador. Copia: "+backup);
    }finally{awaitingStore=false;}
    return;
   }
   int rc=Convert.ToInt32(command=="rename"?Call("setStringParamValue","meta","currentPresetName",Convert.ToString(value)):Call("setStringParamValue","deviceCommand",command,value));if(rc!=0)throw new IOException("Operación rechazada: "+rc);if(command.StartsWith("save")&&paths.Contains(command)&&!File.Exists(Convert.ToString(value)))throw new IOException("El SDK no generó el archivo");
  });
 }
}
}
