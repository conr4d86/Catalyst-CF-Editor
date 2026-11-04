using System;using System.IO;using System.Threading;using System.Threading.Tasks;using System.Collections.Generic;using System.Web.Script.Serialization;using CFEditor;
// Console transport fixture only. Never loads the SDK or calls WinMM.
static class PreampProbe {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
 // A metadata CRC executes thousands of emulated ARM/FS instructions in the
 // Python peer. Its host wall time is not the MCU's response time. Enlarge
 // this laboratory metadata deadline; short production commands keep 1.5 s.
 // The host also permits up to 30 s for serialized metadata validation.
 sealed class EmulatedMetadataClock:IAssetExchange {
  readonly IAssetExchange wire;public EmulatedMetadataClock(IAssetExchange channel){wire=channel;}
  public Task<byte[]> Exchange(byte[] packet,int timeout,CancellationToken cancel){var message=AssetWire.Decode(packet);return wire.Exchange(packet,message.Opcode==5?30000:timeout,cancel);}
 }
 sealed class Link:IAssetMidiChannel {
  public uint Product{get{return 2424833;}}public event Action<byte[]> BytesReceived;public event Action<Exception> Failed;
  public Task Send(byte[] packet,CancellationToken cancel){cancel.ThrowIfCancellationRequested();Console.WriteLine(Json.Serialize(new{packet=Convert.ToBase64String(packet)}));Console.Out.Flush();var response=(Dictionary<string,object>)Json.DeserializeObject(Console.ReadLine());if(!response.ContainsKey("drop")){var notify=BytesReceived;if(notify!=null)notify(Convert.FromBase64String((string)response["packet"]));}GC.KeepAlive(Failed);return Task.FromResult(0);}
 }
 static int Main(){try{
  var command=(Dictionary<string,object>)Json.DeserializeObject(Console.ReadLine());string root=(string)command["root"],mode=(string)command["mode"],identity=(string)command["identity"];
  using(var journal=new PreampJournal(root,2424833,identity))using(var channel=new AssetMidiExchange(new Link())){
   var client=new PreampClient(new EmulatedMetadataClock(channel),2424833,journal);client.Connect(Convert.ToUInt32(command["nonce"]),CancellationToken.None).GetAwaiter().GetResult();var names=new List<string>();for(byte slot=3;slot<5;slot++){var stored=client.ReadAsset(slot,CancellationToken.None).GetAwaiter().GetResult();if(stored==null||stored.Info==null||stored.Generation==0)throw new Exception("Missing NAM metadata");names.Add(stored.Info.Name);}string result;
   if(mode=="recover"){client.Reconcile(CancellationToken.None).GetAwaiter().GetResult();result="reconciled";}
   else{var current=client.Read(CancellationToken.None).GetAwaiter().GetResult();var settings=new PreampSettings{Type=3,Enabled=true,Drive=.5f,Tone=.7f,Level=1.2f};current=client.Set(current,settings,CancellationToken.None).GetAwaiter().GetResult();
    try{client.Save(current,CancellationToken.None).GetAwaiter().GetResult();result="saved";}catch(PreampUncertainException){if(mode!="leave-pending")throw;result="pending";}
   }
   Console.WriteLine(Json.Serialize(new{done=true,passed=true,result,pending=client.HasPending,names}));return 0;
  }
 }catch(Exception error){Console.WriteLine(Json.Serialize(new{done=true,passed=false,error=error.ToString()}));return 1;}}
}
