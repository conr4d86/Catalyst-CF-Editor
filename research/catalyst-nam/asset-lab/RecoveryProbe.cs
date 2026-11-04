using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;using CFEditor;
static class RecoveryProbe {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=4000000};
 sealed class Link:IAssetExchange {
  public Task<byte[]> Exchange(byte[] packet,int timeout,CancellationToken cancel){
   cancel.ThrowIfCancellationRequested();Console.WriteLine(Json.Serialize(new{packet=packet.Select(x=>(int)x).ToArray(),timeout=timeout}));Console.Out.Flush();
   var row=(Dictionary<string,object>)Json.DeserializeObject(Console.ReadLine());if(row.ContainsKey("error"))throw new TimeoutException("Simulated packet loss");return Task.FromResult(((object[])row["packet"]).Select(Convert.ToByte).ToArray());
  }
 }
 sealed class Channel:IAssetMidiChannel {
  readonly Link link;public uint Product{get;private set;}public event Action<byte[]> BytesReceived;public event Action<Exception> Failed;
  public Channel(Link link,uint product){this.link=link;Product=product;}
  public async Task Send(byte[] packet,CancellationToken cancel){
   byte[] bytes;try{bytes=await link.Exchange(packet,1500,cancel);}catch(TimeoutException){return;}
   var receive=BytesReceived;if(receive==null)throw new Exception("No stream subscriber");for(int p=0;p<bytes.Length;p+=3)receive(bytes.Skip(p).Take(3).ToArray());
  }
 }
 sealed class Journal:IAssetPendingJournal {
  public uint Product{get;private set;}public uint Session{get;private set;}AssetPendingTransfer[] entries=new AssetPendingTransfer[5];
  public Journal(uint product,AssetPendingTransfer value){Product=product;Session=9;entries[value.Slot]=value;}
  public AssetPendingTransfer[] Load(){return (AssetPendingTransfer[])entries.Clone();}public void Save(uint session,AssetPendingTransfer[] next){Session=session;entries=(AssetPendingTransfer[])next.Clone();}
 }
 static uint U(Dictionary<string,object> row,string key){return Convert.ToUInt32(row[key]);}
 static int Main(){
  Console.InputEncoding=new System.Text.UTF8Encoding(false);Console.OutputEncoding=new System.Text.UTF8Encoding(false);AssetTransfer client=null;byte slot=0;
  try{
   var row=(Dictionary<string,object>)Json.DeserializeObject(Console.ReadLine());uint product=U(row,"product");slot=(byte)U(row,"slot");var metadata=Convert.FromBase64String((string)row["info"]);AssetReceipt previous=null;
   if(row.ContainsKey("previous")){var old=(Dictionary<string,object>)row["previous"];previous=new AssetReceipt{Slot=slot,Generation=U(old,"generation"),Transaction=U(old,"transaction"),Length=U(old,"length"),Crc=U(old,"crc"),Info=AssetInfo.Decode(Convert.FromBase64String((string)old["info"]))};}
   var journal=new Journal(product,new AssetPendingTransfer(slot,U(row,"transaction"),U(row,"length"),U(row,"crc"),metadata,previous));var link=new Link();
   using(var stream=new AssetMidiExchange(new Channel(link,product))){client=new AssetTransfer(stream,product,journal);client.Connect(999,CancellationToken.None).GetAwaiter().GetResult();var result=client.Reconcile(slot,300,CancellationToken.None).GetAwaiter().GetResult();Console.WriteLine(Json.Serialize(new{done=true,resolved=true,stored=result.Stored,pending=client.HasPending(slot),activated=false,generation=result.Receipt==null?0:result.Receipt.Generation}));}
  }catch(Exception error){Console.WriteLine(Json.Serialize(new{done=true,resolved=false,pending=client!=null&&client.HasPending(slot),activated=false,error=error.GetType().Name,message=error.Message}));}return 0;
 }
}
