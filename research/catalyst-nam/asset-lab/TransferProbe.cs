using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;using CFEditor;
static class TransferProbe {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=4000000};
 sealed class Link:IAssetExchange {
  public readonly CancellationTokenSource Cancel=new CancellationTokenSource();
  public Task<byte[]> Exchange(byte[] packet,int timeoutMs,CancellationToken token){
   token.ThrowIfCancellationRequested();Console.WriteLine(Json.Serialize(new{packet=packet.Select(x=>(int)x).ToArray(),timeout=timeoutMs}));Console.Out.Flush();
   var response=(Dictionary<string,object>)Json.DeserializeObject(Console.ReadLine());
   if(response.ContainsKey("error")){string error=(string)response["error"];if(error=="cancel"){Cancel.Cancel();token.ThrowIfCancellationRequested();}throw new TimeoutException("Simulated packet loss");}
   return Task.FromResult(((object[])response["packet"]).Select(x=>Convert.ToByte(x)).ToArray());
  }
 }
 sealed class StreamChannel:IAssetMidiChannel {
  readonly Link link;public uint Product{get;private set;}public event Action<byte[]> BytesReceived;public event Action<Exception> Failed;
  public StreamChannel(Link link,uint product){this.link=link;Product=product;}
  public async Task Send(byte[] packet,CancellationToken cancel){
   byte[] reply=await link.Exchange(packet,1500,cancel);var receive=BytesReceived;if(receive==null)throw new Exception("Missing stream subscriber");
   receive(new byte[]{0xf0,0x7e,0x7f,6,2,0,1,12,0xf7});
   var corrupt=(byte[])reply.Clone();corrupt[corrupt.Length-2]^=1;receive(corrupt);
   var stale=AssetWire.Decode(reply);stale.Transaction++;receive(AssetWire.Encode(stale));
   var stream=new List<byte>();foreach(byte b in reply){stream.Add(b);stream.Add(0xf8);}for(int i=0;i<stream.Count;i+=5)receive(stream.Skip(i).Take(5).ToArray());
  }
 }
 sealed class Progress:IProgress<AssetProgress>{public readonly List<string> Stages=new List<string>();public void Report(AssetProgress p){if(Stages.Count==0||Stages.Last()!=p.Stage)Stages.Add(p.Stage);}}
 static int Main(){
  Console.InputEncoding=new System.Text.UTF8Encoding(false);Console.OutputEncoding=new System.Text.UTF8Encoding(false);
  var input=(Dictionary<string,object>)Json.DeserializeObject(Console.ReadLine());var link=new Link();var stages=new Progress();AssetJournal journal=null;
  try{
   uint product=Convert.ToUInt32(input["product"]);IAssetExchange exchange=input.ContainsKey("stream")?(IAssetExchange)new AssetMidiExchange(new StreamChannel(link,product)):link;
   if(input.ContainsKey("journal"))journal=new AssetJournal((string)input["journal"],product,"verified:C-receiver-fixture");
   var client=new AssetTransfer(exchange,product,journal);client.Connect(9,link.Cancel.Token).GetAwaiter().GetResult();
   byte slot=Convert.ToByte(input["slot"]);uint tx=Convert.ToUInt32(input["transaction"]);byte[] data=Convert.FromBase64String((string)input["data"]);var info=AssetInfo.Decode(Convert.FromBase64String((string)input["info"]));
   AssetReceipt receipt;
   try{receipt=client.Upload(slot,tx,data,info,stages,link.Cancel.Token).GetAwaiter().GetResult();}
   catch(AssetUncertainException){
    if(!input.ContainsKey("reconcile"))throw;
    if(!client.HasPending(slot))throw new Exception("Uncertain receipt was forgotten");bool blocked=false;
    try{client.Upload(slot,tx+1000,data,info,null,CancellationToken.None).GetAwaiter().GetResult();}catch(InvalidOperationException){blocked=true;}
    if(!blocked)throw new Exception("Uncertain resource was resent");
    Console.WriteLine(Json.Serialize(new{phase="reconcile",blocked=true}));Console.Out.Flush();
    if(journal!=null){journal.Dispose();journal=new AssetJournal((string)input["journal"],product,"verified:C-receiver-fixture");client=new AssetTransfer(exchange,product,journal);client.Connect(999,CancellationToken.None).GetAwaiter().GetResult();}
    try{receipt=client.Reconcile(slot,900,CancellationToken.None).GetAwaiter().GetResult().Receipt;}
    catch(IOException){if(!client.HasPending(slot))throw new Exception("Busy receipt cleared pending state");receipt=client.Reconcile(slot,901,CancellationToken.None).GetAwaiter().GetResult().Receipt;}
    if(client.HasPending(slot)||receipt==null||receipt.Transaction!=tx)throw new Exception("Reconciliation failed");
   }
   Console.WriteLine(Json.Serialize(new{done=true,stored=true,activated=receipt.Activated,slot=receipt.Slot,generation=receipt.Generation,transaction=receipt.Transaction,crc=receipt.Crc,length=receipt.Length,name=receipt.Info.Name,stages=stages.Stages}));
  }catch(Exception e){Console.WriteLine(Json.Serialize(new{done=true,stored=false,error=e.GetType().Name,message=e.Message,stages=stages.Stages}));}finally{if(journal!=null)journal.Dispose();}
  return 0;
 }
}
