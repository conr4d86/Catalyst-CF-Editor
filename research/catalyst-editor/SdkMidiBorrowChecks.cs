using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;
namespace CFEditor {
static class SdkMidiBorrowChecks {
 sealed class Driver:IAssetPortDriver {
  public SdkMidiBorrow Channel;public AssetMidiBuffer Buffer;public int Prepared,Sent,Released,Validated,Forbidden;public uint Reject;public bool Return=true;
  public uint OpenInput(uint i,out IntPtr h){h=IntPtr.Zero;Forbidden++;throw new Exception("borrow opened input");}public uint OpenOutput(uint i,out IntPtr h){h=IntPtr.Zero;Forbidden++;throw new Exception("borrow opened output");}
  public void Revalidate(AssetPortBinding b,bool input,IntPtr h){if(h!=new IntPtr(input?31:32))throw new Exception("wrong handle");Validated++;}
  public uint Prepare(bool input,IntPtr h,AssetMidiBuffer b){if(input)throw new Exception();Prepared++;var x=b.Read();if(x.User==UIntPtr.Zero)throw new Exception("SDK callback requires dwUser event");x.Flags=2;Marshal.StructureToPtr(x,b.Header,false);return 0;}
  public uint Unprepare(bool input,IntPtr h,AssetMidiBuffer b){if(input)throw new Exception();if(!Return)return 65;var x=b.Read();x.Flags&=~2u;Marshal.StructureToPtr(x,b.Header,false);Released++;return 0;}
  public uint Send(IntPtr h,AssetMidiBuffer b){Sent++;Buffer=b;if(Reject!=0)return Reject;if(Return)Finish();return 0;}
  public void Finish(){var b=Buffer;Unprepare(false,new IntPtr(32),b); // reviewed OEM callback does this first
   // Simulate original SetEvent using the valid dwUser, then wrapper return.
   using(var e=new EventWaitHandle(false,EventResetMode.AutoReset)){e.SafeWaitHandle=new Microsoft.Win32.SafeHandles.SafeWaitHandle(new IntPtr(unchecked((int)b.Read().User.ToUInt32())),false);e.Set();}Channel.OutputReturned(new IntPtr(32),b.Header);
  }
  uint No(){Forbidden++;throw new Exception("borrow reset/closed/started handle");}public uint QueueInput(IntPtr h,AssetMidiBuffer b){return No();}public uint StartInput(IntPtr h){return No();}public uint StopInput(IntPtr h){return No();}public uint Reset(bool i,IntPtr h){return No();}public uint Close(bool i,IntPtr h){return No();}
 }
 static AssetPortBinding Binding(){string parent=@"USB\VID_0E41&PID_424E\TEST";return AssetPortBinding.Select(2424833,new[]{new AssetMidiEndpoint(7,true,"Catalyst",@"\\?\input-test",parent,false),new AssetMidiEndpoint(9,false,"Catalyst",@"\\?\output-test",parent,false)},false);}
 static byte[] Packet(byte op=9){return AssetWire.Encode(new AssetMessage{Product=2424833,Session=1,Transaction=2,Opcode=op});}
 static void Must(bool v,string name){if(!v)throw new Exception(name);}static void Wait(Task t){if(!t.Wait(3000))throw new Exception("test timeout");t.GetAwaiter().GetResult();}
 static SdkMidiBorrow Make(Driver d){var c=new SdkMidiBorrow(d);d.Channel=c;c.Bind(Binding(),new IntPtr(31),new IntPtr(32));return c;}
 static void Input(SdkMidiBorrow c,byte[] data){var b=new AssetMidiBuffer(Math.Max(1,data.Length));try{Marshal.Copy(data,0,b.Data,data.Length);var h=b.Read();h.Recorded=(uint)data.Length;Marshal.StructureToPtr(h,b.Header,false);c.InputLong(new IntPtr(31),b.Header);}finally{b.Free();}}
 public static void Run(string path){var checks=new List<string>();
  var d=new Driver();var c=Make(d);try{Wait(c.Send(Packet(),CancellationToken.None));Must(d.Buffer.Released&&d.Prepared==1&&d.Released==1&&d.Validated==2&&d.Forbidden==0,"OEM buffer returned exactly once");checks.Add("borrow existing handles; original callback event lifetime; no open/reset/close");Wait(c.Send(Packet(5),CancellationToken.None));Must(d.Sent==2,"metadata read not permitted");checks.Add("metadata query uses the same borrowed session without enabling asset writes");
   int callbackThread=Thread.CurrentThread.ManagedThreadId;var got=new List<byte>();var received=new TaskCompletionSource<bool>();c.BytesReceived+=b=>{Must(Thread.CurrentThread.ManagedThreadId!=callbackThread,"subscriber in callback");lock(got){got.AddRange(b);if(got.Count==6)received.TrySetResult(true);}};
   Input(c,new byte[]{1,2});Input(c,new byte[]{3,4});Input(c,new byte[]{5,6});Wait(received.Task);Must(got.SequenceEqual(new byte[]{1,2,3,4,5,6}),"input FIFO");checks.Add("copied callback input; bounded FIFO; subscribers off callback");
   bool refused=false;try{c.Send(Packet(2),CancellationToken.None);}catch(InvalidOperationException){refused=true;}Must(refused&&d.Sent==2,"data update on SDK bridge");checks.Add("asset writes rejected while SDK owns channel");
  }finally{Wait(c.Detach());}
  d=new Driver{Return=false};c=Make(d);var send=c.Send(Packet(),CancellationToken.None);while(d.Buffer==null)Thread.Sleep(1);bool held=false;try{c.Detach(20).GetAwaiter().GetResult();}catch(IOException){held=true;}Must(held&&c.Quarantined&&!d.Buffer.Released&&!send.IsCompleted,"pending header retained");d.Return=true;d.Finish();Wait(send);Wait(c.Detach());Must(d.Buffer.Released&&d.Forbidden==0,"late callback release");checks.Add("unconfirmed detach retains header and dwUser; late callback releases without reset");
  d=new Driver{Reject=11};c=Make(d);bool failed=false;try{c.Send(Packet(),CancellationToken.None).GetAwaiter().GetResult();}catch(IOException){failed=true;}Wait(c.Detach());Must(failed&&d.Buffer.Released&&d.Forbidden==0,"rejected send cleanup");checks.Add("send rejection fails closed; own unsent header unprepared");
  d=new Driver();c=Make(d);c.HandleClosed(true,new IntPtr(31));failed=false;try{c.Send(Packet(),CancellationToken.None);}catch(IOException){failed=true;}Wait(c.Detach());Must(failed&&d.Sent==0,"closed SDK handle reused");checks.Add("SDK close invalidates bridge immediately");
  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));File.WriteAllText(path,new JavaScriptSerializer().Serialize(new{passed=true,USB_opened=false,SDK_loaded=false,device_enumeration=false,native_driver_called=false,checks=checks.ToArray()}));
 }
}
}
