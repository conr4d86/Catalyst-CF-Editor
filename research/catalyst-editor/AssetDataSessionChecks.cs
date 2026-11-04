#if DEVELOPMENT
using System;using System.IO;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Collections.Generic;using System.Web.Script.Serialization;
namespace CFEditor {
static class AssetDataSessionChecks {
 static void Need(bool value,string message){if(!value)throw new Exception(message);}
 static async Task Refuse(Task action){bool rejected=false;try{await action.ConfigureAwait(false);}catch(IOException){rejected=true;}catch(InvalidDataException){rejected=true;}catch(InvalidOperationException){rejected=true;}catch(ArgumentException){rejected=true;}catch(OperationCanceledException){rejected=true;}Need(rejected,"Invalid data session was accepted");}
 static AssetPortBinding Binding(bool unique=true){return AssetPortBinding.Select(2424833,new[]{new AssetMidiEndpoint(4,true,"Catalyst",@"\\?\session-in",@"USB\VID_0E41&PID_0001\SESSION-FIXTURE",unique),new AssetMidiEndpoint(9,false,"Catalyst",@"\\?\session-out",@"USB\VID_0E41&PID_0001\SESSION-FIXTURE",unique)},unique);}
 static FakeAssetPortDriver Driver(ReceiptLossFixture fixture){var driver=new FakeAssetPortDriver();driver.Respond=packet=>{byte[] reply=null;try{reply=fixture.Exchange(packet,1500,CancellationToken.None).GetAwaiter().GetResult();}catch(TimeoutException){}if(reply!=null&&!driver.Deliver(reply))throw new Exception("No fake input buffer available");};return driver;}
 static async Task<List<string>> Checks(string root){
  var checks=new List<string>();int opens=0;var fixture=new ReceiptLossFixture();var driver=Driver(fixture);FakeAssetLease lease=null;
  Func<uint,Task<IAssetDataConnection>> opener=async product=>{opens++;return await AssetPortOwner.Open(Binding(),driver,()=>lease=new FakeAssetLease()).ConfigureAwait(false);};
  Func<uint,string,AssetJournal> journals=(product,id)=>new AssetJournal(root,product,id);
  var session=new AssetDataSession(opener,journals);Need(opens==0&&!session.Ready&&!session.HasOwner,"Constructing a session opened hardware");
  await Refuse(session.Connect(1,23,CancellationToken.None));Need(opens==0,"Unsupported product reached discovery");checks.Add("Construction and unsupported product validation do not call the opener");
  using(var cancel=new CancellationTokenSource()){cancel.Cancel();await Refuse(session.Connect(2424833,23,cancel.Token));Need(opens==0,"Pre-canceled connect opened a port");}
  await session.Connect(2424833,23,CancellationToken.None).ConfigureAwait(false);Need(opens==1&&session.Ready&&session.HasOwner,"Verified session not ready");
  Need(await session.Client.ReadSlot(0,24,CancellationToken.None).ConfigureAwait(false)==null,"Empty slot changed");
  await Refuse(session.Connect(2424833,25,CancellationToken.None));Need(session.Ready&&driver.CloseCalls==0&&opens==1,"Duplicate connect disturbed active ownership");
  checks.Add("Real codec HELLO and STATUS use the fake WinMM owner; duplicate connect preserves it");
  driver.BlockUnprepare=true;await Refuse(session.Close());Need(!session.Ready&&session.HasOwner&&lease.Released==0,"Failed close released a live owner");
  bool locked=false;try{using(var other=journals(2424833,Binding().PhysicalIdentity)){} }catch(IOException){locked=true;}Need(locked,"Failed close released the journal lock");
  await Refuse(session.Connect(2424833,26,CancellationToken.None));Need(opens==1,"Quarantined session reopened a port");
  driver.BlockUnprepare=false;await session.Close().ConfigureAwait(false);Need(!session.HasOwner&&lease.Released==1&&driver.Buffers.All(b=>b.Released),"Close retry leaked buffers or lease");
  checks.Add("Unconfirmed close retains journal and driver pointers; only successful explicit retry releases them");
  var metadata=new AssetInfo{Name="NAM 2 fixture",InputDb=-12};byte[] payload=new byte[7772];Buffer.BlockCopy(BitConverter.GetBytes(.125f),0,payload,0,4);uint crc=AssetWire.Crc(payload);
  using(var journal=journals(2424833,Binding().PhysicalIdentity)){var pending=new AssetPendingTransfer[5];pending[4]=new AssetPendingTransfer(4,100,7772,crc,metadata.Encode(),null);journal.Save(44,pending);}
  fixture.Seed(new AssetReceipt{Slot=4,Generation=1,Transaction=100,Length=7772,Crc=crc,Info=metadata});driver=Driver(fixture);
  session=new AssetDataSession(opener,journals);await session.Connect(2424833,99,CancellationToken.None).ConfigureAwait(false);Need(session.Client.HasPending(4),"Reconnect lost pending journal");
  var resolved=await session.Client.Reconcile(4,101,CancellationToken.None).ConfigureAwait(false);Need(resolved.Stored&&!session.Client.HasPending(4)&&fixture.Begins==0&&fixture.Commits==0,"Reconciliation resent the model");
  Need(AssetWire.Decode(driver.Sent[0]).Transaction==44,"Reconnect did not restore the saved session nonce");await session.Close().ConfigureAwait(false);
  checks.Add("Reopen uses the saved nonce and reconciles a committed NAM without BEGIN, CHUNK or COMMIT");
  driver=Driver(new ReceiptLossFixture());session=new AssetDataSession(opener,(product,id)=>{throw new IOException("Synthetic disk failure");});
  await Refuse(session.Connect(2424833,222,CancellationToken.None));Need(!session.HasOwner&&driver.CloseCalls==2&&driver.Sent.Count==0,"Journal failure sent HELLO or leaked a port");
  checks.Add("A journal IO failure closes owned ports before any protocol send");
  driver=Driver(new ReceiptLossFixture());session=new AssetDataSession(opener,journals);await session.Connect(2424833,223,CancellationToken.None).ConfigureAwait(false);driver.InvalidHeader();
  var until=DateTime.UtcNow.AddSeconds(3);while(session.Ready&&DateTime.UtcNow<until)await Task.Delay(5).ConfigureAwait(false);Need(!session.Ready&&session.HasOwner,"Driver failure did not disable the session");
  await session.Close().ConfigureAwait(false);checks.Add("Driver failure immediately invalidates readiness and requires explicit cleanup");
  driver=Driver(new ReceiptLossFixture());int journalsOpened=0;
  Func<uint,Task<IAssetDataConnection>> locationOpen=async product=>await AssetPortOwner.Open(Binding(false),driver,()=>new FakeAssetLease()).ConfigureAwait(false);
  session=new AssetDataSession(locationOpen,(product,id)=>{journalsOpened++;return journals(product,id);});
  await Refuse(session.Connect(2424833,300,CancellationToken.None));Need(!session.HasOwner&&journalsOpened==0&&driver.Sent.Count==0,"Port location acquired a persistent journal or sent HELLO");
  checks.Add("A non-unique PnP port cannot act as a persistent journal identity without the receiver query");
  foreach(string mode in new[]{"valid","changed-edition","legacy","invalid"}){
   fixture=new ReceiptLossFixture();driver=new FakeAssetPortDriver();string recorded=null;uint marker=mode=="changed-edition"?2u:1u;
   driver.Respond=packet=>{var request=AssetWire.Decode(packet);byte[] response;
    if(request.Opcode==8){var bytes=new byte[mode=="legacy"?20:52];Action<int,uint> put=(at,value)=>Buffer.BlockCopy(BitConverter.GetBytes(value),0,bytes,at,4);put(4,mode=="legacy"?1u:2u);put(8,request.Product);put(12,mode=="invalid"?0u:0x10203040u);put(16,mode=="invalid"?0u:0xa0b0c0d0u);if(bytes.Length==52)for(int i=20;i<52;i++)bytes[i]=(byte)marker;request.Opcode=72;request.Data=bytes;response=AssetWire.Encode(request);
    }else response=fixture.Exchange(packet,1500,CancellationToken.None).GetAwaiter().GetResult();Need(driver.Deliver(response),"Missing fake input buffer");};
   session=new AssetDataSession(locationOpen,(product,id)=>{recorded=id;return journals(product,id);},(channel,exchange,token)=>AssetDeviceIdentity.Read(exchange,channel.Product,token,true));
   if(mode=="legacy"||mode=="invalid"){
    await Refuse(session.Connect(2424833,301,CancellationToken.None));Need(recorded==null&&!session.HasOwner&&driver.Sent.Count==1&&AssetWire.Decode(driver.Sent[0]).Opcode==8,"Incompatible identity opened a journal, sent HELLO or leaked ownership");
   }else{
    await session.Connect(2424833,301,CancellationToken.None);Need(session.Ready&&recorded=="CFCX-UID2:10203040A0B0C0D0:"+string.Concat(Enumerable.Repeat(marker==1?"01":"02",32)),"UID/edition journal key changed");
    Need(AssetWire.Decode(driver.Sent[0]).Opcode==8&&AssetWire.Decode(driver.Sent[1]).Opcode==1,"HELLO preceded identity and journal validation");await session.Close();
   }
  }
  checks.Add("Silicon UID and edition query precede journal/HELLO; new full-CF editions have different keys; incompatible replies close without HELLO");return checks;
 }
 public static int Run(string report){var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)),"journal-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{var checks=Checks(root).GetAwaiter().GetResult();File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{passed=true,USB_opened=false,SDK_loaded=false,device_enumeration=false,native_driver_called=false,checks}));return 0;}catch(Exception error){File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{passed=false,USB_opened=false,error=error.ToString()}));return 1;}}
}
}
#endif
