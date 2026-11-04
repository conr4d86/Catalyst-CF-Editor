using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;
namespace CFEditor {
// Fixed stateless receiver observations, no HELLO takeover, asset/preset
// writes or model selection. Uses exact PnP binding and qualified owner close.
static class ReceiverDiagnostic {
 internal static int Run(string report,int count=6){return Observe(report,count).GetAwaiter().GetResult();}
 static async Task<int> Observe(string report,int count){
  report=Path.GetFullPath(report);Directory.CreateDirectory(Path.GetDirectoryName(report));IAssetDataConnection channel=null;AssetMidiExchange exchange=null;Exception error=null;bool closed=false;string identity=null;var samples=new List<uint[]>();
  var timestamps=new List<double>();var clock=System.Diagnostics.Stopwatch.StartNew();
  try{NativeBackend.RefuseOtherUSBApps();var binding=AssetPortDiscovery.Discover(2424833,false);channel=await AssetPortOwner.Open(binding,new WinmmAssetPortDriver(),()=>new Line6USBLease()).ConfigureAwait(false);exchange=new AssetMidiExchange(channel);
   identity=await AssetDeviceIdentity.Read(exchange,2424833,CancellationToken.None,true).ConfigureAwait(false);
   for(uint i=1;i<=count;i++){double started=clock.Elapsed.TotalSeconds;var request=new AssetMessage{Product=2424833,Opcode=13,Transaction=0x60000000+i};var raw=await exchange.Exchange(AssetWire.Encode(request),1500,CancellationToken.None).ConfigureAwait(false);var reply=AssetWire.Decode(raw);timestamps.Add((started+clock.Elapsed.TotalSeconds)*.5);
    if(!AssetWire.RepliesTo(request,reply)||reply.Data.Length<8||BitConverter.ToUInt32(reply.Data,0)!=0)throw new InvalidDataException("Diagnóstico no compatible.");
    uint version=BitConverter.ToUInt32(reply.Data,4);if(!((version==1&&reply.Data.Length==128)||(version==2&&reply.Data.Length==160)||(version==3&&reply.Data.Length==192)||((version==4||version==5)&&reply.Data.Length==256)))throw new InvalidDataException("Versión de diagnóstico no compatible.");
    samples.Add(Enumerable.Range(0,reply.Data.Length/4).Select(n=>BitConverter.ToUInt32(reply.Data,n*4)).ToArray());if(i<count)await Task.Delay(1000).ConfigureAwait(false);
   }
  }catch(Exception e){error=e;}
  if(exchange!=null)exchange.Dispose();if(channel!=null)try{await channel.CloseAsync().ConfigureAwait(false);closed=channel.Closed;}catch(Exception e){error=error==null?e:new AggregateException(error,e);}else closed=true;
  bool pass=error==null&&closed&&samples.Count==count;
  File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{passed=pass,identity,shutdown_confirmed=closed,samples,reply_midpoints_seconds=timestamps,parameters_edited=false,assets_written=false,presets_changed=false,firmware_installed=false,error=error==null?null:error.ToString()}));if(!closed)new ManualResetEvent(false).WaitOne();return pass?0:1;
 }
}
}
