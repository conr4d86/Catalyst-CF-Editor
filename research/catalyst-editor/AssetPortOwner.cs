using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Collections.Concurrent;using System.Runtime.InteropServices;using System.Threading;using System.Threading.Tasks;
namespace CFEditor {
[StructLayout(LayoutKind.Sequential)]struct AssetMidiHeader {
 public IntPtr Data;public uint Length,Recorded;public UIntPtr User;public uint Flags;public IntPtr Next;public UIntPtr Reserved;public uint Offset;
 public UIntPtr Reserved0,Reserved1,Reserved2,Reserved3,Reserved4,Reserved5,Reserved6,Reserved7;
}
sealed class AssetMidiBuffer {
 static readonly int FlagsOffset=Marshal.OffsetOf(typeof(AssetMidiHeader),"Flags").ToInt32();
 public readonly IntPtr Header,Data;public readonly int Capacity;public bool Prepared,Released;public TaskCompletionSource<bool> Completion;
 public AssetMidiBuffer(int size){if(size<1||size>4096)throw new ArgumentOutOfRangeException("size");Capacity=size;Data=Marshal.AllocHGlobal(size);try{Header=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(AssetMidiHeader)));var zero=new byte[size];Marshal.Copy(zero,0,Data,size);Marshal.StructureToPtr(new AssetMidiHeader{Data=Data,Length=(uint)size},Header,false);}catch{if(Header!=IntPtr.Zero)Marshal.FreeHGlobal(Header);Marshal.FreeHGlobal(Data);throw;}}
 public AssetMidiHeader Read(){if(Released)throw new ObjectDisposedException("MIDI buffer");return (AssetMidiHeader)Marshal.PtrToStructure(Header,typeof(AssetMidiHeader));}
 public bool DriverDone{get{if(Released)throw new ObjectDisposedException("MIDI buffer");if((Marshal.ReadInt32(Header,FlagsOffset)&1)==0)return false;Thread.MemoryBarrier();return true;}}
 public void Free(){if(Released)return;if(Prepared)throw new InvalidOperationException("El controlador conserva el buffer MIDI.");Marshal.FreeHGlobal(Header);Marshal.FreeHGlobal(Data);Released=true;}
}
interface IAssetPortDriver {
 uint OpenInput(uint index,out IntPtr handle);uint OpenOutput(uint index,out IntPtr handle);
 void Revalidate(AssetPortBinding binding,bool input,IntPtr handle);
 uint Prepare(bool input,IntPtr handle,AssetMidiBuffer buffer);uint Unprepare(bool input,IntPtr handle,AssetMidiBuffer buffer);
 uint QueueInput(IntPtr handle,AssetMidiBuffer buffer);uint StartInput(IntPtr handle);uint StopInput(IntPtr handle);uint Reset(bool input,IntPtr handle);uint Close(bool input,IntPtr handle);
 uint Send(IntPtr handle,AssetMidiBuffer buffer);
}
interface IAssetPortLease:IDisposable {}
// Mutex ownership stays on the same native worker for its complete lifetime.
// A quarantined worker deliberately retains the lease: it cannot certify that
// the SDK may reopen a port whose driver still owns pointers.
sealed class Line6USBLease:IAssetPortLease {
 static volatile bool unsafeClose;readonly Mutex gate;bool owned;
 public static void BlockAfterUnconfirmedClose(){unsafeClose=true;}
 internal static bool UnconfirmedClose{get{return unsafeClose;}}
 public Line6USBLease(){if(unsafeClose)throw new IOException("El cierre anterior de Line 6 no está confirmado. Cierra la app antes de volver a conectar.");gate=new Mutex(false,"Local\\Conr4d.Line6USB");try{try{owned=gate.WaitOne(0);}catch(AbandonedMutexException){owned=true;unsafeClose=true;throw new IOException("Una conexión anterior terminó sin confirmar su cierre. Reinicia la app antes de conectar.");}if(!owned)throw new IOException("Otra operación de Line 6 está usando la conexión.");}catch{if(owned){gate.ReleaseMutex();owned=false;}gate.Dispose();throw;}}
 public void Dispose(){if(owned){owned=false;gate.ReleaseMutex();gate.Dispose();}}
}
// Native calls and all MIDIHDR operations run on one worker. CALLBACK_NULL
// avoids managed callback/root lifetime races; completed SysEx headers are
// polled every 5 ms. It does not receive short MIDI messages or open audio.
sealed class AssetPortOwner:IAssetDataConnection {
 static readonly object retainedLock=new object();static readonly List<AssetPortOwner> retained=new List<AssetPortOwner>();
 readonly AssetPortBinding binding;readonly IAssetPortDriver driver;readonly Func<IAssetPortLease> acquire;readonly ConcurrentQueue<Action> commands=new ConcurrentQueue<Action>();readonly AutoResetEvent wake=new AutoResetEvent(false);readonly List<AssetMidiBuffer> input=new List<AssetMidiBuffer>();readonly Queue<AssetMidiBuffer> inputOrder=new Queue<AssetMidiBuffer>();
 readonly TaskCompletionSource<bool> ready=new TaskCompletionSource<bool>();readonly Thread worker;readonly object stateLock=new object();
 IAssetPortLease lease;IntPtr inHandle,outHandle;volatile AssetMidiBuffer output;volatile Exception terminal;volatile bool stopping,finished;bool quarantined;TaskCompletionSource<bool> closed;
 public uint Product{get{return binding.Product;}}public string PhysicalIdentity{get{return binding.PhysicalIdentity;}}
 public event Action<byte[]> BytesReceived;public event Action<Exception> Failed;
 public bool Closed{get{lock(stateLock)return finished;}}public bool Quarantined{get{lock(stateLock)return quarantined;}}
 AssetPortOwner(AssetPortBinding b,IAssetPortDriver d,Func<IAssetPortLease> a){binding=b;driver=d;acquire=a;worker=new Thread(Loop){IsBackground=true,Name="Catalyst data MIDI owner"};worker.SetApartmentState(ApartmentState.MTA);}
 // Explicit factory only. The UI/preview constructor must never invoke it.
 public static async Task<AssetPortOwner> Open(AssetPortBinding binding,IAssetPortDriver driver,Func<IAssetPortLease> acquire,int timeoutMs=5000){
  if(binding==null||driver==null||acquire==null)throw new ArgumentNullException();if(timeoutMs<1||timeoutMs>30000)throw new ArgumentOutOfRangeException("timeoutMs");var owner=new AssetPortOwner(binding,driver,acquire);owner.worker.Start();
  if(await Task.WhenAny(owner.ready.Task,Task.Delay(timeoutMs)).ConfigureAwait(false)!=owner.ready.Task){owner.ready.Task.ContinueWith(t=>{var ignored=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);owner.stopping=true;owner.Keep();owner.Post(owner.TryClose);throw new IOException("El controlador MIDI no ha respondido. La conexión queda reservada hasta confirmar su cierre.");}
  await owner.ready.Task.ConfigureAwait(false);return owner;
 }
 static void Done(TaskCompletionSource<bool> promise,Exception error=null,bool cancel=false){if(promise==null)return;ThreadPool.QueueUserWorkItem(_=>{if(cancel)promise.TrySetCanceled();else if(error!=null)promise.TrySetException(error);else promise.TrySetResult(true);});}
 static void Require(uint result,string operation){if(result!=0)throw new IOException(operation+" (MIDI "+result+").");}
 bool Post(Action action){lock(stateLock){if(finished)return false;commands.Enqueue(action);wake.Set();return true;}}
 void NeedOpening(){if(stopping)throw new OperationCanceledException("Conexión MIDI cancelada.");}
 void Fault(Exception error){
  lock(stateLock){if(terminal!=null)return;terminal=error;}
  Done(output==null?null:output.Completion,error);var failed=Failed;if(failed!=null)ThreadPool.QueueUserWorkItem(_=>{try{failed(error);}catch{}});
 }
 void Loop(){
  try{
   lease=acquire();NeedOpening();Require(driver.OpenInput(binding.Input.Index,out inHandle),"No se pudo abrir la entrada");NeedOpening();driver.Revalidate(binding,true,inHandle);
   NeedOpening();Require(driver.OpenOutput(binding.Output.Index,out outHandle),"No se pudo abrir la salida");NeedOpening();driver.Revalidate(binding,false,outHandle);
   for(int i=0;i<4;i++){var buffer=new AssetMidiBuffer(512);input.Add(buffer);buffer.Prepared=true;Require(driver.Prepare(true,inHandle,buffer),"No se pudo preparar la entrada");Require(driver.QueueInput(inHandle,buffer),"No se pudo recibir datos");inputOrder.Enqueue(buffer);}
   NeedOpening();Require(driver.StartInput(inHandle),"No se pudo iniciar la entrada");Done(ready);
  }catch(Exception error){Fault(error);Done(ready,error);stopping=true;TryClose();}
  while(!finished){
   Action command;while(commands.TryDequeue(out command)){try{command();}catch(Exception error){Fault(error);}}
   if(!stopping){try{Poll();}catch(Exception error){Fault(error);}}
   if(!finished)wake.WaitOne(stopping?Timeout.Infinite:5);
  }
  wake.Dispose();
 }
 void Poll(){
  if(output!=null&&output.DriverDone){Require(driver.Unprepare(false,outHandle,output),"La salida no ha devuelto su buffer");output.Prepared=false;Done(output.Completion);output.Free();output=null;}
  if(terminal!=null)return;
  // The queue rotates after requeue. Creation order would reverse fragments
  // when headers 3 and 0 complete together at the wraparound boundary.
  for(int n=0;n<4&&inputOrder.Count>0&&!stopping;n++){var buffer=inputOrder.Peek();if(!buffer.DriverDone)break;inputOrder.Dequeue();var h=buffer.Read();
   if(h.Data!=buffer.Data||h.Length!=buffer.Capacity||h.Recorded>buffer.Capacity)throw new InvalidDataException("El controlador devolvió un buffer MIDI no válido.");
   if(h.Recorded!=0){var bytes=new byte[h.Recorded];Marshal.Copy(buffer.Data,bytes,0,bytes.Length);var receive=BytesReceived;if(receive!=null)receive(bytes);}
   Require(driver.QueueInput(inHandle,buffer),"No se pudo volver a recibir datos");inputOrder.Enqueue(buffer);
  }
 }
 public Task Send(byte[] packet,CancellationToken cancel){
  if(packet==null||packet.Length<1||packet.Length>AssetWire.MaxPacket)throw new ArgumentException("Tamaño MIDI no válido");var bytes=(byte[])packet.Clone();var result=new TaskCompletionSource<bool>();
  lock(stateLock){if(stopping||finished)throw new ObjectDisposedException("AssetPortOwner");if(terminal!=null)throw new IOException("La conexión de datos se ha interrumpido.",terminal);}
  CancellationTokenRegistration registration=cancel.Register(()=>Post(()=>{if(output!=null&&output.Completion==result){Done(result,null,true);Fault(new OperationCanceledException("El envío MIDI se ha cancelado; debe cerrarse su conexión."));}else Done(result,null,true);}));
  result.Task.ContinueWith(_=>registration.Dispose(),TaskScheduler.Default);
  if(!Post(()=>{
   if(cancel.IsCancellationRequested){Done(result,null,true);return;}
   if(stopping||terminal!=null){Done(result,terminal??new ObjectDisposedException("AssetPortOwner"));return;}
   if(output!=null){Done(result,new IOException("Ya hay un envío MIDI en curso."));return;}
   try{output=new AssetMidiBuffer(bytes.Length){Completion=result};Marshal.Copy(bytes,0,output.Data,bytes.Length);output.Prepared=true;Require(driver.Prepare(false,outHandle,output),"No se pudo preparar el envío");Require(driver.Send(outHandle,output),"No se pudo enviar el mensaje");}
   catch(Exception error){Done(result,error);Fault(error);} // Driver-owned memory stays rooted until Unprepare succeeds.
  }))Done(result,new ObjectDisposedException("AssetPortOwner"));return result.Task;
 }
 public async Task CloseAsync(int timeoutMs=4000){
  Task wait;lock(stateLock){if(finished)return;if(closed==null||closed.Task.IsCompleted)closed=new TaskCompletionSource<bool>();wait=closed.Task;stopping=true;}
  Post(TryClose);var expired=Task.Delay(timeoutMs);if(await Task.WhenAny(wait,expired).ConfigureAwait(false)!=wait)throw new IOException("El controlador no ha confirmado el cierre. Los buffers se conservan y no se reabrirá la conexión.");await wait.ConfigureAwait(false);
 }
 void Keep(){lock(stateLock)quarantined=true;lock(retainedLock)if(!retained.Contains(this))retained.Add(this);}
 void TryClose(){
  stopping=true;
  try{
   if(inHandle!=IntPtr.Zero){driver.StopInput(inHandle);driver.Reset(true,inHandle);}if(outHandle!=IntPtr.Zero)driver.Reset(false,outHandle);
   DateTime until=DateTime.UtcNow.AddMilliseconds(1500);bool pending;
   do{
    pending=false;
    foreach(var buffer in input){if(!buffer.Prepared)continue;uint rc=driver.Unprepare(true,inHandle,buffer);if(rc==0){buffer.Prepared=false;buffer.Free();}else pending=true;}
    if(output!=null&&output.Prepared){uint rc=driver.Unprepare(false,outHandle,output);if(rc==0){output.Prepared=false;Done(output.Completion,new OperationCanceledException("Conexión MIDI cerrada."));output.Free();output=null;}else pending=true;}
    if(pending)Thread.Sleep(5);
   }while(pending&&DateTime.UtcNow<until);
   if(pending){Keep();Done(closed,new IOException("El controlador conserva buffers MIDI. No se han liberado sus punteros."));return;}
   foreach(var buffer in input)buffer.Free();
   if(inHandle!=IntPtr.Zero){Require(driver.Close(true,inHandle),"La entrada MIDI no se ha cerrado");inHandle=IntPtr.Zero;}
   if(outHandle!=IntPtr.Zero){Require(driver.Close(false,outHandle),"La salida MIDI no se ha cerrado");outHandle=IntPtr.Zero;}
   if(lease!=null){lease.Dispose();lease=null;}
   lock(stateLock){finished=true;quarantined=false;}lock(retainedLock)retained.Remove(this);Done(closed);
  }catch(Exception error){Keep();Done(closed,error);}
 }
}
}
