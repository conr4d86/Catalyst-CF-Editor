#if DEVELOPMENT
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;
namespace CFEditor {
static class MidiExchangeChecks {
 sealed class Channel:IAssetMidiChannel {
  public uint Product{get;set;}public int Sends;public Action<byte[]> Handler;public Func<byte[],Task> Sender;
  public event Action<byte[]> BytesReceived;public event Action<Exception> Failed;
  public Task Send(byte[] b,CancellationToken cancel){Sends++;if(Handler!=null)Handler(b);return Sender==null?Task.FromResult(true):Sender(b);}
  public void Receive(byte[] b){var call=BytesReceived;if(call!=null)call(b);}
  public void Fail(){var call=Failed;if(call!=null)call(new IOException("simulated disconnect"));}
  public int Subscribers{get{return (BytesReceived==null?0:BytesReceived.GetInvocationList().Length)+(Failed==null?0:Failed.GetInvocationList().Length);}}
 }
 static readonly List<string> cases=new List<string>();
 static void Need(bool ok,string message){if(!ok)throw new Exception(message);}
 static byte[] Packet(uint tx=123,byte opcode=1,uint session=0,byte slot=0,uint number=0){return AssetWire.Encode(new AssetMessage{Product=2424833,Transaction=tx,Opcode=opcode,Session=session,Slot=slot,Number=number});}
 static AssetMessage Reply(byte[] packet){var r=AssetWire.Decode(packet);r.Session=r.Opcode==1?r.Transaction:r.Session;r.Opcode|=64;r.Data=new byte[8];return r;}
 static byte[] Bytes(AssetMessage message){return AssetWire.Encode(message);}
 static async Task Expect<T>(Task task,string message)where T:Exception{try{await task;throw new Exception(message);}catch(T){}}
 static async Task WaitSends(Channel channel,int count){for(int i=0;i<200&&channel.Sends<count;i++)await Task.Delay(1);Need(channel.Sends==count,"Serialized send did not start");}
 static async Task Check(){
  var expected=Bytes(Reply(Packet()));
  for(int split=0;split<=expected.Length;split++){
   var framer=new AssetMidiFramer();var frames=framer.Feed(expected.Take(split).ToArray());frames.AddRange(framer.Feed(expected.Skip(split).ToArray()));Need(frames.Count==1&&frames[0].SequenceEqual(expected),"Fragment boundary "+split);
  }cases.Add("Every fragment boundary preserves the exact frame");
  {
   var framer=new AssetMidiFramer();var got=new List<byte[]>();foreach(byte b in expected){got.AddRange(framer.Feed(new[]{b, (byte)0xf8,(byte)0xfe}));}Need(got.Count==1&&got[0].SequenceEqual(expected),"Realtime byte handling");
   var stream=new List<byte>{0x91,60,12,0xf7,0xf0,1,0x90,40,50};stream.AddRange(expected);stream.AddRange(expected);got=framer.Feed(stream.ToArray());Need(got.Count==2,"Mixed MIDI stream");
   var tooLong=new List<byte>{0xf0};tooLong.AddRange(Enumerable.Repeat((byte)1,AssetWire.MaxPacket+20));tooLong.Add(0xf7);tooLong.AddRange(expected);got=framer.Feed(tooLong.ToArray());Need(got.Count==1&&got[0].SequenceEqual(expected),"Oversized message recovery");
   var maximum=AssetWire.Encode(new AssetMessage{Product=2424833,Session=1,Transaction=1,Opcode=67,Slot=0,Data=new byte[256]});Need(framer.Feed(maximum).Count==1,"Maximum-size frame dropped");
   framer.Feed(new byte[]{0xf0,1,2});framer.Reset();Need(framer.Feed(new byte[]{3,0xf7}).Count==0,"Reset retained partial bytes");
  }cases.Add("Realtime, foreign, interrupted, oversized, maximum and reset framing");
  {
   var c=new Channel{Product=2424833};using(var exchange=new AssetMidiExchange(c)){
    Need(c.Sends==0,"Constructor accessed the channel");
    c.Handler=b=>{var valid=Reply(b);var foreign=Bytes(valid);foreign[foreign.Length-2]^=1;c.Receive(foreign);c.Receive(new byte[]{0xf0,1,2,0xf7});c.Receive(new byte[4097]);
     foreach(var change in new Action<AssetMessage>[] {r=>r.Product++,r=>r.Session++,r=>r.Transaction++,r=>r.Number++,r=>r.Slot++,r=>r.Opcode++}){var wrong=Reply(b);change(wrong);c.Receive(Bytes(wrong));}
     foreach(byte value in Bytes(valid))c.Receive(new[]{value});c.Receive(Bytes(valid));};
    Need((await exchange.Exchange(Packet(),1000,CancellationToken.None)).SequenceEqual(expected),"Correlation or fragmented exchange failed");
    c.Handler=b=>c.Receive(Bytes(Reply(b)));var status=Packet(456,5,123,4);Need((await exchange.Exchange(status,1000,CancellationToken.None)).SequenceEqual(Bytes(Reply(status))),"Status response failed");
   }Need(c.Subscribers==0,"Disposed adapter kept channel callbacks");
  }cases.Add("No constructor IO; corrupt/foreign/wrong correlation ignored; duplicate and fragmented replies");
  {
   var c=new Channel{Product=2424833};c.Handler=b=>c.Receive(Bytes(Reply(b)));using(var exchange=new AssetMidiExchange(c)){
    var query=Packet(912,13);Need((await exchange.Exchange(query,1000,CancellationToken.None)).SequenceEqual(Bytes(Reply(query))),"Fixed stateless diagnostic rejected");
   }
  }cases.Add("Fixed stateless diagnostic reaches the correlated transport without opening a write session");
  {
   int polls=0;var c=new Channel{Product=2424833};c.Handler=b=>{var request=AssetWire.Decode(b);var r=Reply(b);if(request.Opcode==1){r.Data=new byte[20];Buffer.BlockCopy(BitConverter.GetBytes(1u),0,r.Data,4,4);Buffer.BlockCopy(BitConverter.GetBytes(256u),0,r.Data,8,4);Buffer.BlockCopy(BitConverter.GetBytes(5u),0,r.Data,12,4);Buffer.BlockCopy(BitConverter.GetBytes(2u),0,r.Data,16,4);}else{Need(request.Opcode==5,"ReadSlot attempted a write while waiting");polls++;Buffer.BlockCopy(BitConverter.GetBytes(polls<3?1u:polls==3?6u:7u),0,r.Data,0,4);}c.Receive(Bytes(r));};
   using(var exchange=new AssetMidiExchange(c)){var client=new AssetTransfer(exchange,2424833);await client.Connect(777,CancellationToken.None);Need(await client.ReadSlot(4,778,CancellationToken.None)==null&&polls==4,"ReadSlot did not wait for BUSY/PENDING to clear");
    using(var cancel=new CancellationTokenSource()){cancel.Cancel();int sent=c.Sends;await Expect<OperationCanceledException>(client.ReadSlot(4,779,cancel.Token),"Canceled status query ignored");Need(c.Sends==sent,"Canceled status query reached transport");}
   }
  }cases.Add("Post-commit BUSY/PENDING states are polled read-only, not mistaken for a failed upload or retried write; cancellation prevents IO");
  {
   var c=new Channel{Product=2424833};c.Handler=b=>{var request=AssetWire.Decode(b);var r=Reply(b);if(request.Opcode==1){r.Data=new byte[20];Buffer.BlockCopy(BitConverter.GetBytes(1u),0,r.Data,4,4);Buffer.BlockCopy(BitConverter.GetBytes(256u),0,r.Data,8,4);Buffer.BlockCopy(BitConverter.GetBytes(5u),0,r.Data,12,4);Buffer.BlockCopy(BitConverter.GetBytes(2u),0,r.Data,16,4);c.Receive(Bytes(r));}else{Need(request.Opcode==5,"Delayed metadata read attempted mutation");Buffer.BlockCopy(BitConverter.GetBytes(7u),0,r.Data,0,4);Task.Run(async delegate{await Task.Delay(1750);c.Receive(Bytes(r));});}};
   using(var exchange=new AssetMidiExchange(c)){var client=new AssetTransfer(exchange,2424833);await client.Connect(1777,CancellationToken.None);Need(await client.ReadSlot(1,1778,CancellationToken.None)==null&&c.Sends==2,"Valid delayed metadata response caused timeout/replay");}
  }cases.Add("Slow physical-style metadata read exceeds old 1.5 s deadline without false disconnect or replay; mutations remain forbidden");
  {
   var c=new Channel{Product=2424833};using(var exchange=new AssetMidiExchange(c)){
    await Expect<TimeoutException>(exchange.Exchange(Packet(),25,CancellationToken.None),"Missing reply accepted");c.Receive(expected);
    c.Handler=b=>{c.Receive(expected);c.Receive(Bytes(Reply(b)));};var next=Packet(124);Need((await exchange.Exchange(next,1000,CancellationToken.None)).SequenceEqual(Bytes(Reply(next))),"Late prior transaction accepted");
   }
  }cases.Add("Timeout permits a new request after completed send; old transaction ignored");
  {
   var c=new Channel{Product=2424833};c.Handler=b=>{var busy=Reply(b);busy.Session=999;busy.Data=new byte[20];Buffer.BlockCopy(BitConverter.GetBytes(1u),0,busy.Data,0,4);Buffer.BlockCopy(BitConverter.GetBytes(1u),0,busy.Data,4,4);Buffer.BlockCopy(BitConverter.GetBytes(256u),0,busy.Data,8,4);Buffer.BlockCopy(BitConverter.GetBytes(5u),0,busy.Data,12,4);Buffer.BlockCopy(BitConverter.GetBytes(2u),0,busy.Data,16,4);c.Receive(Bytes(busy));};
   using(var exchange=new AssetMidiExchange(c)){var client=new AssetTransfer(exchange,2424833);await Expect<IOException>(client.Connect(123,CancellationToken.None),"Busy HELLO accepted as a new session");Need(!client.Ready&&c.Sends==1,"Busy HELLO adopted foreign session or blindly retried");}
  }cases.Add("Busy HELLO with previous session is correlated as refusal, never adopted or reported as a timeout");
  {
   var c=new Channel{Product=2424833};using(var exchange=new AssetMidiExchange(c)){
    var first=exchange.Exchange(Packet(),1000,CancellationToken.None);await WaitSends(c,1);
    using(var cancel=new CancellationTokenSource()){
     var queued=exchange.Exchange(Packet(124),1000,cancel.Token);cancel.Cancel();await Expect<OperationCanceledException>(queued,"Canceled queued request sent");Need(c.Sends==1,"Queued cancellation reached channel");
    }
    var second=exchange.Exchange(Packet(125),1000,CancellationToken.None);Need(c.Sends==1,"Overlapping sends");c.Receive(expected);await first;await WaitSends(c,2);c.Receive(Bytes(Reply(Packet(125))));await second;
    using(var cancel=new CancellationTokenSource()){cancel.Cancel();await Expect<OperationCanceledException>(exchange.Exchange(Packet(126),1000,cancel.Token),"Pre-cancel ignored");Need(c.Sends==2,"Pre-canceled request reached channel");}
   }
  }cases.Add("Requests serialized; queued and pre-canceled requests never sent");
  {
   var c=new Channel{Product=2424833};using(var exchange=new AssetMidiExchange(c))using(var cancel=new CancellationTokenSource()){
    var request=exchange.Exchange(Packet(),1000,cancel.Token);await WaitSends(c,1);cancel.Cancel();await Expect<OperationCanceledException>(request,"Cancellation ignored");c.Receive(expected);
    c.Handler=b=>c.Receive(Bytes(Reply(b)));await exchange.Exchange(Packet(124),1000,CancellationToken.None);
   }
  }cases.Add("In-flight reply wait cancellation clears state without resetting the channel");
  {
   var c=new Channel{Product=2424833};var blocked=new TaskCompletionSource<bool>();c.Sender=b=>blocked.Task;using(var exchange=new AssetMidiExchange(c)){
    await Expect<TimeoutException>(exchange.Exchange(Packet(),25,CancellationToken.None),"Blocked sender hung exchange");
    await Expect<IOException>(exchange.Exchange(Packet(124),1000,CancellationToken.None),"Blocked channel reused");Need(c.Sends==1,"Blocked driver received another send");blocked.TrySetResult(true);
   }
  }cases.Add("Timed-out sender quarantines channel and never sends again");
  {
   var c=new Channel{Product=2424833};using(var exchange=new AssetMidiExchange(c)){
    var wait=exchange.Exchange(Packet(),1000,CancellationToken.None);c.Fail();await Expect<IOException>(wait,"Disconnect ignored");await Expect<IOException>(exchange.Exchange(Packet(124),1000,CancellationToken.None),"Disconnected channel reused");Need(c.Sends==1,"Disconnected channel sent again");
   }
   c=new Channel{Product=2424833};c.Sender=b=>{var fault=new TaskCompletionSource<bool>();fault.SetException(new IOException("send failed"));return fault.Task;};using(var exchange=new AssetMidiExchange(c)){
    await Expect<IOException>(exchange.Exchange(Packet(),1000,CancellationToken.None),"Failed send accepted");await Expect<IOException>(exchange.Exchange(Packet(124),1000,CancellationToken.None),"Failed sender reused");Need(c.Sends==1,"Failed sender sent again");
   }
  }cases.Add("Disconnect and asynchronous send failure quarantine channel");
  {
   var c=new Channel{Product=2424833};var exchange=new AssetMidiExchange(c);var waiting=exchange.Exchange(Packet(),1000,CancellationToken.None);exchange.Dispose();exchange.Dispose();await Expect<ObjectDisposedException>(waiting,"Dispose left reply pending");await Expect<ObjectDisposedException>(exchange.Exchange(Packet(),1000,CancellationToken.None),"Disposed exchange reused");Need(c.Subscribers==0&&c.Sends==1,"Dispose changed owner IO or retained callbacks");
  }cases.Add("Dispose wakes pending request, detaches callbacks and does not close/reset owner device");
  {
   var c=new Channel{Product=2424833};using(var exchange=new AssetMidiExchange(c)){
    await Expect<InvalidDataException>(exchange.Exchange(new byte[]{0xf0,0xf7},1000,CancellationToken.None),"Foreign outgoing message accepted");
    await Expect<InvalidDataException>(exchange.Exchange(Bytes(Reply(Packet())),1000,CancellationToken.None),"Reply sent as request");
    await Expect<InvalidDataException>(exchange.Exchange(Packet(1,5,0),1000,CancellationToken.None),"Zero session accepted");
    foreach(byte opcode in new byte[]{8,13})foreach(var query in new[]{Packet(1,opcode,1),Packet(1,opcode,0,1),Packet(1,opcode,0,0,1)})await Expect<InvalidDataException>(exchange.Exchange(query,1000,CancellationToken.None),"Malformed stateless query accepted");
    await Expect<ArgumentOutOfRangeException>(exchange.Exchange(Packet(),0,CancellationToken.None),"Invalid timeout accepted");Need(c.Sends==0,"Invalid outgoing request reached channel");
    c.Product=2424834;var changed=AssetWire.Decode(Packet());changed.Product=2424834;await Expect<InvalidDataException>(exchange.Exchange(Bytes(changed),1000,CancellationToken.None),"Channel rebound to a different product");Need(c.Sends==0,"Rebound channel sent a request");
   }
   bool rejected=false;try{new AssetMidiExchange(new Channel{Product=2162695});}catch(NotSupportedException){rejected=true;}Need(rejected,"POD identity accepted as Catalyst");
  }cases.Add("Invalid requests and non-Catalyst channel identities rejected before IO");
  {
   var old=new AssetReceipt{Slot=3,Generation=7,Transaction=6,Length=7772,Crc=17,Info=new AssetInfo{Name="Previous",InputDb=-12,OutputDb=0}};
   var info=new AssetInfo{Name="New model",InputDb=-18,OutputDb=-3};var pending=new AssetPendingTransfer(3,8,7772,19,info.Encode(),old);
   var stored=new AssetReceipt{Slot=3,Generation=8,Transaction=8,Length=7772,Crc=19,Info=AssetInfo.Decode(info.Encode())};Need(pending.Resolve(stored).Stored,"Exact committed receipt not recognized");
   Need(!pending.Resolve(old).Stored,"Unchanged previous resource counted as new success");
   old.Info.Name="mutated";bool failed=false;try{pending.Resolve(old);}catch(InvalidDataException){failed=true;}Need(failed,"Mutable caller receipt changed the pending snapshot");
   foreach(var change in new Action<AssetReceipt>[] {r=>r.Slot++,r=>r.Generation++,r=>r.Transaction++,r=>r.Length++,r=>r.Crc++,r=>r.Info.OutputDb++,r=>r.Info.SourceHash[0]++}){
    var changed=new AssetReceipt{Slot=3,Generation=8,Transaction=8,Length=7772,Crc=19,Info=AssetInfo.Decode(info.Encode())};change(changed);failed=false;try{pending.Resolve(changed);}catch(InvalidDataException){failed=true;}Need(failed,"Different resource accepted during reconciliation");
   }
   failed=false;try{pending.Resolve(null);}catch(InvalidDataException){failed=true;}Need(failed,"Lost previous resource treated as harmless rollback");
   var empty=new AssetPendingTransfer(3,8,7772,19,info.Encode(),null);Need(!empty.Resolve(null).Stored&&empty.Resolve(stored=new AssetReceipt{Slot=3,Generation=1,Transaction=8,Length=7772,Crc=19,Info=info}).Stored,"Empty original slot resolution");
  }cases.Add("Uncertain receipt resolves only to the exact new generation or unchanged previous resource; metadata and snapshots verified");
 }
 public static int Run(string output){
  try{cases.Clear();Check().GetAwaiter().GetResult();File.WriteAllText(output,new JavaScriptSerializer().Serialize(new{passed=true,hardware_accessed=false,SDK_loaded=false,USB_opened=false,native_transport_attached=false,cases=cases.ToArray()}));return 0;}
  catch(Exception error){File.WriteAllText(output,new JavaScriptSerializer().Serialize(new{passed=false,hardware_accessed=false,error=error.ToString(),cases=cases.ToArray()}));return 1;}
 }
}
}
#endif
