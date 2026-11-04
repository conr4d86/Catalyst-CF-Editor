using System;using System.IO;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;
namespace CFEditor {
// Read-only development measurement: extended deadline only for STATUS, not
// uploads or the production client. No updater, model/preset or asset writes.
static class StatusTimingTrial {
 sealed class Measured:IAssetExchange {public IAssetExchange Inner;public Action<string> Log;public async Task<byte[]> Exchange(byte[] packet,int timeout,CancellationToken cancel){var m=AssetWire.Decode(packet);var clock=System.Diagnostics.Stopwatch.StartNew();try{var r=await Inner.Exchange(packet,m.Opcode==5?30000:timeout,cancel).ConfigureAwait(false);Log("op="+m.Opcode+" slot="+m.Slot+" ms="+clock.ElapsedMilliseconds+" bytes="+r.Length);return r;}catch(Exception e){Log("op="+m.Opcode+" slot="+m.Slot+" ms="+clock.ElapsedMilliseconds+" error="+e.GetType().Name);throw;}}}
 internal static int Run(string output){return Execute(Path.GetFullPath(output)).GetAwaiter().GetResult();}
 static async Task<int> Execute(string output){IAssetDataConnection channel=null;AssetMidiExchange exchange=null;var results=new List<AssetReceipt>();Exception error=null;bool closed=false;
  Action<string> log=s=>File.AppendAllText(output+".log",s+Environment.NewLine);
  try{NativeBackend.RefuseOtherUSBApps();var binding=AssetPortDiscovery.Discover(2424833,false);channel=await AssetPortOwner.Open(binding,new WinmmAssetPortDriver(),()=>new Line6USBLease()).ConfigureAwait(false);exchange=new AssetMidiExchange(channel);await AssetDeviceIdentity.Read(exchange,2424833,CancellationToken.None,true).ConfigureAwait(false);var client=new AssetTransfer(new Measured{Inner=exchange,Log=log},2424833);await client.Connect(0x70107801,CancellationToken.None).ConfigureAwait(false);for(byte slot=0;slot<5;slot++)results.Add(await client.ReadSlot(slot,0x70107900u+slot,CancellationToken.None).ConfigureAwait(false));}catch(Exception e){error=e;}
  if(exchange!=null)exchange.Dispose();if(channel!=null)try{await channel.CloseAsync().ConfigureAwait(false);closed=channel.Closed;}catch(Exception e){error=error==null?e:new AggregateException(error,e);}else closed=true;
  bool passed=error==null&&closed;File.WriteAllText(output,new JavaScriptSerializer().Serialize(new{passed,results,shutdown_confirmed=closed,assets_written=false,firmware_installed=false,extended_status_deadline_ms=30000,error=error==null?null:error.ToString()}));if(!closed)new ManualResetEvent(false).WaitOne();return passed?0:1;
 }
}
}
