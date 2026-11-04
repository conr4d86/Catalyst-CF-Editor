using System;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;using System.Collections.Generic;using CFEditor;
static class IdentityProbe {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
 sealed class Link:IAssetExchange {
  public Task<byte[]> Exchange(byte[] packet,int timeout,CancellationToken cancel){
   cancel.ThrowIfCancellationRequested();Console.WriteLine(Json.Serialize(new{packet=Convert.ToBase64String(packet),timeout=timeout}));Console.Out.Flush();
   var reply=(Dictionary<string,object>)Json.DeserializeObject(Console.ReadLine());if(reply.ContainsKey("error"))throw new TimeoutException("Synthetic missing identity");return Task.FromResult(Convert.FromBase64String((string)reply["packet"]));
  }
 }
 static int Main(string[] args){try{uint product=uint.Parse(Console.ReadLine());string identity=AssetDeviceIdentity.Read(new Link(),product,CancellationToken.None,args.Contains("--edition")).GetAwaiter().GetResult();Console.WriteLine(Json.Serialize(new{done=true,accepted=true,identity}));}catch(Exception error){Console.WriteLine(Json.Serialize(new{done=true,accepted=false,error=error.GetType().Name}));}return 0;}
}
