using System;using System.IO;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;
namespace CFEditor {
// The device owner supplies a channel already bound to a verified Catalyst.
// Events and Send must run on its worker, never inside a WinMM callback.
// This contract does not enumerate/open/close ports, reset devices or load SDKs.
interface IAssetMidiChannel {
 uint Product{get;}
 event Action<byte[]> BytesReceived;
 event Action<Exception> Failed;
 Task Send(byte[] packet,CancellationToken cancel);
}
// Bounded SysEx framing. Realtime bytes may legally occur inside a message.
// Oversized/aborted messages are discarded; the next F0 starts a fresh frame.
sealed class AssetMidiFramer {
 readonly byte[] data=new byte[AssetWire.MaxPacket];int count;bool receiving;
 public void Reset(){count=0;receiving=false;}
 public List<byte[]> Feed(byte[] bytes){
  if(bytes==null||bytes.Length>4096)throw new InvalidDataException("Fragmento MIDI no válido");
  var complete=new List<byte[]>();
  foreach(byte b in bytes){
   if(b>=0xf8)continue;
   if(b==0xf0){receiving=true;count=0;data[count++]=b;continue;}
   if(!receiving)continue;
   if((b>=0x80&&b!=0xf7)||count==data.Length){Reset();continue;}
   data[count++]=b;
   if(b==0xf7){var frame=new byte[count];Buffer.BlockCopy(data,0,frame,0,count);complete.Add(frame);Reset();}
  }return complete;
 }
}
// Stream -> request/reply adapter. The caller retains the channel/port lease.
// Dispose only unsubscribes this adapter; it never closes another owner's port.
// Native binding belongs to AssetPortOwner; this layer never opens a port.
sealed class AssetMidiExchange:IAssetExchange,IDisposable {
 readonly IAssetMidiChannel channel;readonly uint product;readonly object sync=new object();readonly SemaphoreSlim owner=new SemaphoreSlim(1,1);readonly AssetMidiFramer framer=new AssetMidiFramer();
 AssetMessage pending;TaskCompletionSource<byte[]> completion;Exception terminal;bool disposed;
 public AssetMidiExchange(IAssetMidiChannel channel){
  if(channel==null)throw new ArgumentNullException("channel");if(channel.Product<2424833||channel.Product>2424835)throw new NotSupportedException("El canal no identifica un Catalyst compatible");
  this.channel=channel;product=channel.Product;channel.BytesReceived+=Receive;channel.Failed+=Fail;
 }
 static void Complete(TaskCompletionSource<byte[]> target,byte[] bytes,Exception error){
  // No await continuation is allowed to execute inline on the receive worker.
  if(target==null)return;ThreadPool.QueueUserWorkItem(delegate{if(error==null)target.TrySetResult(bytes);else target.TrySetException(error);});
 }
 bool Matches(AssetMessage reply){return pending!=null&&AssetWire.RepliesTo(pending,reply);}
 void Receive(byte[] bytes){
  TaskCompletionSource<byte[]> target=null;byte[] result=null;
  lock(sync){
   if(disposed||terminal!=null)return;
   // Reject an invalid driver fragment rather than copying an unbounded input.
   if(bytes==null||bytes.Length>4096){framer.Reset();return;}
   foreach(var frame in framer.Feed(bytes)){
    AssetMessage reply;try{reply=AssetWire.Decode(frame);}catch(InvalidDataException){continue;}
    if(!Matches(reply)||completion==null)continue;
    target=completion;completion=null;pending=null;result=frame;break;
   }
  }Complete(target,result,null);
 }
 void Fail(Exception error){
  TaskCompletionSource<byte[]> target;
  lock(sync){if(disposed||terminal!=null)return;terminal=new IOException("Se ha interrumpido el canal de datos del amplificador.",error);target=completion;completion=null;pending=null;framer.Reset();}
  Complete(target,null,terminal);
 }
 void Check(){if(disposed)throw new ObjectDisposedException("AssetMidiExchange");if(terminal!=null)throw new IOException("La conexión de datos debe abrirse de nuevo.",terminal);}
 static async Task<byte[]> Finish(Task sent,Task<byte[]> received){await sent.ConfigureAwait(false);return await received.ConfigureAwait(false);}
 static void Observe(Task task){task.ContinueWith(t=>{var ignored=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);}
 public async Task<byte[]> Exchange(byte[] packet,int timeoutMs,CancellationToken cancel){
  if(timeoutMs<1||timeoutMs>30000)throw new ArgumentOutOfRangeException("timeoutMs");
  if(packet==null)throw new ArgumentNullException("packet");var copy=(byte[])packet.Clone();var request=AssetWire.Decode(copy);
  if(request==null||channel.Product!=product||request.Product!=product||request.Opcode<1||request.Opcode>14||request.Slot>=(request.Opcode>=9?13:5)||request.Transaction==0||((request.Opcode==1||request.Opcode==8||request.Opcode==13||request.Opcode==14)?(request.Session!=0||request.Slot!=0||request.Number!=0||request.Data.Length!=0):request.Session==0))throw new InvalidDataException("La petición no corresponde a este canal de datos");
  await owner.WaitAsync(cancel).ConfigureAwait(false);Task sent=null;Task<byte[]> result=null;TaskCompletionSource<byte[]> waiting=null;
  using(var deadline=new CancellationTokenSource()){
   try{
    cancel.ThrowIfCancellationRequested();
    lock(sync){Check();framer.Reset();pending=request;waiting=completion=new TaskCompletionSource<byte[]>();}
    var delay=Task.Delay(timeoutMs,deadline.Token);var canceled=new TaskCompletionSource<bool>();
    using(cancel.Register(()=>canceled.TrySetResult(true))){
     try{sent=channel.Send(copy,cancel);if(sent==null)throw new IOException("El canal no ha confirmado el envío");}catch(Exception error){Fail(error);throw;}
     result=Finish(sent,waiting.Task);
     // Observe faults even when a broken driver outlives the response deadline.
     Observe(result);
     var first=await Task.WhenAny(result,delay,canceled.Task).ConfigureAwait(false);
     if(first==result){try{return await result.ConfigureAwait(false);}catch(OperationCanceledException){throw;}catch(Exception error){Fail(error);throw;}}
     if(!sent.IsCompleted)Fail(new TimeoutException("El canal no ha liberado el envío pendiente."));
     cancel.ThrowIfCancellationRequested();throw new TimeoutException("El amplificador no ha respondido dentro del plazo.");
    }
   }finally{
    deadline.Cancel();lock(sync){if(ReferenceEquals(completion,waiting)){completion=null;pending=null;framer.Reset();}}
    // A timed-out reply task must not remain pending forever.
    if(waiting!=null)waiting.TrySetCanceled();owner.Release();
   }
  }
 }
 public void Dispose(){
  TaskCompletionSource<byte[]> target;
  lock(sync){if(disposed)return;disposed=true;target=completion;completion=null;pending=null;framer.Reset();}
  channel.BytesReceived-=Receive;channel.Failed-=Fail;Complete(target,null,new ObjectDisposedException("AssetMidiExchange"));
 }
}
}
