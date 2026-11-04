using System;using System.IO;using System.Collections.Generic;using System.Collections.Concurrent;using System.Runtime.InteropServices;using System.Threading;using System.Threading.Tasks;
namespace CFEditor {
// Borrows only the handles opened by our reviewed SDK. It never opens, closes,
// stops or resets a MIDI handle. The SDK keeps its original callbacks and data.
// The callback adapter calls OutputReturned AFTER the original callback has
// unprepared the header and signalled its dwUser event, not on MHDR_DONE alone.
sealed class SdkMidiBorrow:IAssetMidiChannel {
 readonly object sync=new object();readonly IAssetPortDriver driver;readonly ConcurrentQueue<byte[]> incoming=new ConcurrentQueue<byte[]>();
 readonly Queue<Action> work=new Queue<Action>();readonly AutoResetEvent wake=new AutoResetEvent(false);readonly Thread worker;
 static readonly List<SdkMidiBorrow> retained=new List<SdkMidiBorrow>();
 AssetPortBinding binding;IntPtr input,output;AssetMidiBuffer sending;AutoResetEvent returnedEvent;TaskCompletionSource<bool> sent;
 bool stopping,finished,quarantined;int queued;Exception terminal;
 public uint Product{get{return binding==null?0:binding.Product;}}public bool Quarantined{get{lock(sync)return quarantined;}}
 public event Action<byte[]> BytesReceived;public event Action<Exception> Failed;
 public SdkMidiBorrow(IAssetPortDriver api){if(api==null)throw new ArgumentNullException();driver=api;worker=new Thread(Loop){IsBackground=true,Name="Catalyst SDK control channel"};worker.SetApartmentState(ApartmentState.MTA);worker.Start();}
 public void Bind(AssetPortBinding identity,IntPtr inputHandle,IntPtr outputHandle){lock(sync){
  if(binding!=null||stopping||terminal!=null||identity==null||inputHandle==IntPtr.Zero||outputHandle==IntPtr.Zero)throw new IOException("No se puede asociar el canal del editor.");
  driver.Revalidate(identity,true,inputHandle);driver.Revalidate(identity,false,outputHandle);input=inputHandle;output=outputHandle;binding=identity;
 }}
 static void Complete(TaskCompletionSource<bool> task,Exception error){if(task==null)return;ThreadPool.QueueUserWorkItem(_=>{if(error==null)task.TrySetResult(true);else task.TrySetException(error);});}
 void Post(Action action){lock(sync){if(finished)return;work.Enqueue(action);wake.Set();}}
 void Fault(Exception error){Action<Exception> fail=null;lock(sync){if(terminal!=null)return;terminal=error;fail=Failed;}Complete(sent,error);if(fail!=null)ThreadPool.QueueUserWorkItem(_=>{try{fail(error);}catch{}});}
 // Called before forwarding the original input callback. Copy only; no await,
 // SDK call, disk access, subscribers or header requeueing on the callback.
 public void InputLong(IntPtr handle,IntPtr header){try{
  lock(sync){if(stopping||binding==null||handle!=input)return;}
  if(header==IntPtr.Zero)throw new InvalidDataException("Cabecera MIDI ausente.");var h=(AssetMidiHeader)Marshal.PtrToStructure(header,typeof(AssetMidiHeader));
  if(h.Length>4096||h.Recorded>h.Length||h.Data==IntPtr.Zero)throw new InvalidDataException("Fragmento de controles no válido.");if(h.Recorded==0)return;
  if(Interlocked.Increment(ref queued)>32){Interlocked.Decrement(ref queued);throw new IOException("Se ha interrumpido la recepción de controles.");}
  var b=new byte[h.Recorded];Marshal.Copy(h.Data,b,0,b.Length);incoming.Enqueue(b);wake.Set();
 }catch(Exception error){Fault(error);}}
 public void OutputReturned(IntPtr handle,IntPtr header){Post(()=>{if(sending==null||handle!=output||header!=sending.Header)return;var h=sending.Read();
  if((h.Flags&2)!=0){Fault(new IOException("El SDK no ha devuelto el buffer de controles."));return;}
  sending.Prepared=false;sending.Free();sending=null;returnedEvent.Dispose();returnedEvent=null;Complete(sent,null);sent=null;
 });}
 public void HandleClosed(bool isInput,IntPtr handle){lock(sync){if(binding==null||handle!=(isInput?input:output))return;if(isInput)input=IntPtr.Zero;else output=IntPtr.Zero;}Fault(new IOException("El motor ha cerrado su conexión de controles."));}
 public Task Send(byte[] packet,CancellationToken cancel){if(packet==null)throw new ArgumentNullException();var request=AssetWire.Decode(packet);
  if(request==null||request.Opcode!=1&&request.Opcode!=5&&request.Opcode!=8&&(request.Opcode<9||request.Opcode>12))throw new InvalidOperationException("Este canal solo admite controles y consulta de nombres.");
  var bytes=(byte[])packet.Clone();var result=new TaskCompletionSource<bool>();lock(sync){if(binding==null||stopping||terminal!=null||request.Product!=Product||input==IntPtr.Zero||output==IntPtr.Zero)throw new IOException("El canal de controles no está conectado.");}
  Post(()=>{if(cancel.IsCancellationRequested){Complete(result,new OperationCanceledException(cancel));return;}if(sending!=null||terminal!=null||stopping){Complete(result,terminal??new IOException("Hay otro envío de controles pendiente."));return;}
   try{sending=new AssetMidiBuffer(bytes.Length);sent=result;returnedEvent=new AutoResetEvent(false);var h=sending.Read();h.User=new UIntPtr(unchecked((uint)returnedEvent.SafeWaitHandle.DangerousGetHandle().ToInt32()));Marshal.StructureToPtr(h,sending.Header,false);Marshal.Copy(bytes,0,sending.Data,bytes.Length);
    uint code=driver.Prepare(false,output,sending);if(code!=0)throw new IOException("No se pudo preparar el control MIDI: "+code);sending.Prepared=true;
    code=driver.Send(output,sending);if(code!=0)throw new IOException("No se pudo enviar el control MIDI: "+code);
   }catch(Exception error){Complete(result,error);Fault(error);TryReleaseUnsent();}
  });return result.Task;
 }
 void TryReleaseUnsent(){if(sending==null)return;if(sending.Prepared){if(driver.Unprepare(false,output,sending)!=0)return;sending.Prepared=false;}sending.Free();sending=null;if(returnedEvent!=null){returnedEvent.Dispose();returnedEvent=null;}sent=null;}
 void Loop(){while(true){Action action=null;lock(sync){if(work.Count>0)action=work.Dequeue();}if(action!=null){try{action();}catch(Exception error){Fault(error);}continue;}
   byte[] bytes;if(incoming.TryDequeue(out bytes)){Interlocked.Decrement(ref queued);Action<byte[]> receive;lock(sync)receive=stopping||terminal!=null?null:BytesReceived;if(receive!=null)try{receive(bytes);}catch(Exception error){Fault(error);}continue;}
   lock(sync){if(stopping&&sending==null){finished=true;break;}}wake.WaitOne(10);
  }wake.Dispose();}
 public async Task Detach(int timeoutMs=2000){lock(sync){if(finished)return;stopping=true;wake.Set();}if(!await Task.Run(()=>worker.Join(timeoutMs)).ConfigureAwait(false)){
   lock(sync)quarantined=true;lock(retained)if(!retained.Contains(this))retained.Add(this);throw new IOException("El SDK todavía conserva un envío de controles. Se mantiene reservada la conexión.");
  }lock(retained)retained.Remove(this);
 }
}
}
