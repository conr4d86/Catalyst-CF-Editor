#if DEVELOPMENT
using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using System.Security.Cryptography;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;
namespace CFEditor {
static class AssetJournalChecks {
 const uint Product=2424833;const string Identity="verified:test-amplifier-A";
 static readonly List<string> cases=new List<string>();
 static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
 static byte[] Hash(byte[] b){using(var h=SHA256.Create())return h.ComputeHash(b);}
 static string DataPath(string folder){return Directory.GetFiles(folder,"*.cfj").Single();}
 static byte[] Data(){var b=new byte[7772];for(int i=0;i<b.Length;i+=4)Buffer.BlockCopy(BitConverter.GetBytes(.125f),0,b,i,4);return b;}
 static AssetInfo Info(string name="NAM test"){return new AssetInfo{Name=name,InputDb=-12,OutputDb=-3};}
 static AssetPendingTransfer Pending(byte slot=3,uint tx=100,AssetReceipt previous=null){return new AssetPendingTransfer(slot,tx,slot<3?25088u:7772u,123,Info().Encode(),previous);}
 static void Put(byte[] b,int p,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,p,4);}
 static async Task Expect<T>(Task action,string why)where T:Exception{try{await action;throw new Exception(why);}catch(T){}}
 sealed class FaultJournal:IAssetPendingJournal {
  public readonly AssetJournal Inner;public bool RejectSave;public uint Product{get{return Inner.Product;}}public uint Session{get{return Inner.Session;}}
  public FaultJournal(AssetJournal inner){Inner=inner;}public AssetPendingTransfer[] Load(){return Inner.Load();}public void Save(uint session,AssetPendingTransfer[] entries){if(RejectSave)throw new IOException("simulated disk full");Inner.Save(session,entries);}
 }
 sealed class Link:IAssetExchange {
  public uint Session;public int Begins,Commits,Cancels,Requests;public bool DropReceipts,DropBegin,DropCancel,Receiving;public Action<byte> Before;public Action<AssetMessage,int> Inspect;public Action AfterReceipt;
  readonly AssetReceipt[] records=new AssetReceipt[5];uint tx,crc;byte slot;int length,offset;byte[] metadata;
  public Task<byte[]> Exchange(byte[] packet,int timeout,CancellationToken cancel){
   cancel.ThrowIfCancellationRequested();var q=AssetWire.Decode(packet);Requests++;if(Before!=null)Before(q.Opcode);if(Inspect!=null)Inspect(q,timeout);var data=new byte[8];
   if(q.Opcode==1){data=new byte[20];if(Receiving&&Session!=q.Transaction)Put(data,0,1);else Session=q.Transaction;Put(data,4,1);Put(data,8,256);Put(data,12,5);Put(data,16,2);}
   else if(q.Session!=Session)throw new Exception("Journal did not resume the original session");
   else if(q.Opcode==5){
    if(Receiving)Put(data,0,6);else{var r=records[q.Slot];if(r==null)Put(data,0,7);else{if(DropReceipts)throw new TimeoutException("simulated receipt loss");data=new byte[204];Put(data,4,r.Generation);Put(data,8,r.Transaction);Put(data,12,r.Length);Put(data,16,r.Crc);Buffer.BlockCopy(r.Info.Encode(),0,data,20,184);if(AfterReceipt!=null)AfterReceipt();}}
   }
   else if(q.Opcode==2){Begins++;Receiving=true;tx=q.Transaction;slot=q.Slot;length=(int)BitConverter.ToUInt32(q.Data,0);crc=BitConverter.ToUInt32(q.Data,4);metadata=q.Data.Skip(8).ToArray();offset=0;if(DropBegin)throw new TimeoutException("simulated lost BEGIN");}
   else if(q.Opcode==3){Need(Receiving&&q.Slot==slot&&q.Transaction==tx&&q.Number==offset,"Invalid fixture chunk");offset+=q.Data.Length;Put(data,4,(uint)offset);}
   else if(q.Opcode==4){Need(Receiving&&offset==length,"Fixture incomplete commit");Commits++;Receiving=false;var old=records[slot];records[slot]=new AssetReceipt{Slot=slot,Transaction=tx,Generation=old==null?1:old.Generation+1,Length=(uint)length,Crc=crc,Info=AssetInfo.Decode(metadata)};if(DropReceipts)throw new TimeoutException("simulated lost COMMIT reply");Put(data,0,6);}
   else if(q.Opcode==6){Cancels++;if(DropCancel)throw new TimeoutException("simulated lost CANCEL");Receiving=false;}
   else throw new Exception("Unexpected fixture command");
   return Task.FromResult(AssetWire.Encode(new AssetMessage{Product=q.Product,Session=Session,Transaction=q.Transaction,Slot=q.Slot,Number=q.Number,Opcode=(byte)(q.Opcode|64),Data=data}));
  }
 }
 sealed class CancelProgress:IProgress<AssetProgress>{public CancellationTokenSource Cancel;public void Report(AssetProgress p){if(p.Stage=="Enviando"&&p.Completed==p.Total)Cancel.Cancel();}}
 static async Task Check(string root){
  string first=Path.Combine(root,"roundtrip");byte[] saved;
  using(var journal=new AssetJournal(first,Product,Identity)){
   Need(journal.Session==0&&journal.Load().All(p=>p==null),"Fresh journal not empty");
   bool locked=false;try{using(var other=new AssetJournal(first,Product,Identity)){}}catch(IOException){locked=true;}Need(locked,"Two owners opened the same journal");
   var entries=new AssetPendingTransfer[5];entries[1]=Pending(1);entries[3]=Pending(3,101,new AssetReceipt{Slot=3,Generation=7,Transaction=90,Length=7772,Crc=19,Info=Info("Previous model")});journal.Save(42,entries);saved=File.ReadAllBytes(DataPath(first));
   Need(!Encoding.UTF8.GetString(saved).Contains(Identity),"Physical identity stored in plaintext");entries[1]=null;var copy=journal.Load();Need(copy[1]!=null&&copy[3].Generation==8,"Caller changed saved entries");copy[3].Metadata[24]=65;Need(journal.Load()[3].Metadata.SequenceEqual(Info().Encode()),"Metadata snapshot mutated");
  }
  using(var journal=new AssetJournal(first,Product,Identity)){Need(journal.Session==42&&journal.Load()[1]!=null&&journal.Load()[3].Previous.Generation==7,"Closed journal lost pending receipts/session");journal.Save(42,new AssetPendingTransfer[5]);}
  using(var journal=new AssetJournal(first,Product,Identity))Need(journal.Session==42&&journal.Load().All(p=>p==null),"Resolved journal did not persist its cleared entries");
  cases.Add("Durable roundtrip, previous receipt, defensive snapshots, opaque identity and exclusive file owner");
  {
   var link=new Link();var client=new AssetTransfer(link,Product);await client.Connect(42,CancellationToken.None);int scans=0;link.Inspect=(request,deadline)=>{if(request.Opcode==5){Need(deadline==30000,"Metadata scan has an unbounded or short deadline");scans++;throw new TimeoutException("slow serialized metadata scan");}};
   await Expect<TimeoutException>(client.ReadSlot(4,101,CancellationToken.None),"Expired scan reported success");Need(scans==1&&link.Begins==0&&link.Commits==0&&link.Cancels==0,"Timeout queued duplicate scans or writes");
  }cases.Add("STATUS has one bounded 30-second scan and no duplicate request after timeout");
  {
   var link=new Link{DropBegin=true,DropCancel=true};using(var journal=new AssetJournal(Path.Combine(root,"slow-begin"),Product,Identity)){
    var client=new AssetTransfer(link,Product,journal);await client.Connect(42,CancellationToken.None);link.Inspect=(request,deadline)=>{if(request.Opcode==5||request.Opcode==2)Need(deadline==30000,"STATUS/BEGIN uses a short scan deadline");};
    await Expect<TimeoutException>(client.Upload(3,100,Data(),Info(),null,CancellationToken.None),"Lost BEGIN unexpectedly completed");Need(link.Begins==1&&link.Commits==0&&journal.Load()[3]!=null&&client.HasPending(3)&&link.Receiving,"Lost BEGIN repeated a scan or discarded unresolved write-ahead state");
   }
  }cases.Add("BEGIN scan timeout is not retried; unresolved cancellation retains its pending journal and cannot silently resend");
  using(var other=new AssetJournal(first,Product,"verified:test-amplifier-B"))Need(other.Load().All(p=>p==null),"Another amplifier inherited pending state");
  using(var other=new AssetJournal(first,2424834,Identity))Need(other.Load().All(p=>p==null),"Another model inherited pending state");cases.Add("Device/model identities use separate records");
  {
   int index=0;var corruptions=new Action<byte[]>[]{b=>b[0]^=1,b=>Put(b,4,2),b=>Put(b,8,2424834),b=>b[16]^=1,b=>Put(b,48,6),b=>Put(b,52,1),b=>Put(b,56,5),b=>Put(b,56+408,1),b=>Put(b,60,0),b=>Put(b,64,4),b=>Put(b,72,0),b=>Put(b,12,0),b=>Put(b,56+408+208,uint.MaxValue)};
   foreach(var corrupt in corruptions){string folder=Path.Combine(root,"invalid-"+(index++));using(var journal=new AssetJournal(folder,Product,Identity)){}var body=saved.Take(saved.Length-32).ToArray();corrupt(body);File.WriteAllBytes(DataPath(folder),body.Concat(Hash(body)).ToArray());bool rejected=false;try{using(var journal=new AssetJournal(folder,Product,Identity)){}}catch(InvalidDataException){rejected=true;}Need(rejected,"Structurally invalid journal accepted "+(index-1));}
   foreach(int n in new[]{0,20,saved.Length-1,2200}){string folder=Path.Combine(root,"size-"+n);using(var journal=new AssetJournal(folder,Product,Identity)){}File.WriteAllBytes(DataPath(folder),new byte[n]);bool rejected=false;try{using(var journal=new AssetJournal(folder,Product,Identity)){}}catch(InvalidDataException){rejected=true;}Need(rejected,"Truncated/oversized journal accepted");}
   string checksum=Path.Combine(root,"checksum");using(var journal=new AssetJournal(checksum,Product,Identity)){}var wrong=(byte[])saved.Clone();wrong[100]^=1;File.WriteAllBytes(DataPath(checksum),wrong);bool bad=false;try{using(var journal=new AssetJournal(checksum,Product,Identity)){}}catch(InvalidDataException){bad=true;}Need(bad,"Invalid checksum accepted");
   string missing=Path.Combine(root,"missing");using(var journal=new AssetJournal(missing,Product,Identity)){}File.Move(DataPath(missing),Path.Combine(missing,"removed.cfj.backup"));bad=false;try{using(var journal=new AssetJournal(missing,Product,Identity)){}}catch(InvalidDataException){bad=true;}Need(bad,"Missing existing journal recreated as empty");
  }cases.Add("Corrupt, truncated, oversized, duplicate, foreign and missing records fail closed, including recomputed checksum attacks");
  {
   string folder=Path.Combine(root,"success");var link=new Link();using(var journal=new AssetJournal(folder,Product,Identity)){
    var client=new AssetTransfer(link,Product,journal);await client.Connect(42,CancellationToken.None);link.Before=op=>{if(op==2||op==3||op==4)Need(journal.Load()[3]!=null&&journal.Session==42,"Write sent before durable pending record");};
    var receipt=await client.Upload(3,100,Data(),Info(),null,CancellationToken.None);Need(receipt.Generation==1&&!client.HasPending(3)&&journal.Load()[3]==null&&link.Commits==1,"Successful upload did not clear journal");
   }using(var journal=new AssetJournal(folder,Product,Identity))Need(journal.Load()[3]==null,"Success not persisted");
  }cases.Add("Pending receipt and session persisted before BEGIN/CHUNK/COMMIT; success clears disk journal");
  {
   string folder=Path.Combine(root,"lost-receipt");var link=new Link{DropReceipts=true};using(var journal=new AssetJournal(folder,Product,Identity)){
    var client=new AssetTransfer(link,Product,journal);await client.Connect(42,CancellationToken.None);await Expect<AssetUncertainException>(client.Upload(3,100,Data(),Info(),null,CancellationToken.None),"Unknown commit reported success");Need(client.HasPending(3)&&journal.Load()[3]!=null,"Unknown commit forgotten");
   }
   using(var journal=new AssetJournal(folder,Product,Identity)){
    var client=new AssetTransfer(link,Product,journal);Need(client.HasPending(3),"New client forgot disk receipt");await client.Connect(99,CancellationToken.None);Need(link.Session==42,"New client replaced recovery session");int before=link.Requests;
    await Expect<InvalidOperationException>(client.Upload(3,101,Data(),Info(),null,CancellationToken.None),"Restart allowed overwrite of unknown resource");Need(link.Requests==before,"Blocked overwrite issued traffic");
    link.DropReceipts=false;var resolved=await client.Reconcile(3,500,CancellationToken.None);Need(resolved.Stored&&link.Commits==1&&link.Begins==1&&!client.HasPending(3),"Restart did not reconcile without resending");
   }using(var journal=new AssetJournal(folder,Product,Identity))Need(journal.Load()[3]==null,"Resolved receipt resurrected on next open");
  }cases.Add("App/client reconstruction restores original session, blocks overwrite and reconciles stored data without resending");
  {
   string folder=Path.Combine(root,"disk-failure-before-begin");var link=new Link();using(var disk=new AssetJournal(folder,Product,Identity)){
    var journal=new FaultJournal(disk);var client=new AssetTransfer(link,Product,journal);await client.Connect(42,CancellationToken.None);byte[] before=File.ReadAllBytes(DataPath(folder));journal.RejectSave=true;
    await Expect<IOException>(client.Upload(3,100,Data(),Info(),null,CancellationToken.None),"Disk failure still sent resource");Need(link.Begins==0&&link.Commits==0&&before.SequenceEqual(File.ReadAllBytes(DataPath(folder))),"Disk failure wrote device or lost previous log");
   }
  }cases.Add("Journal write failure prevents BEGIN and preserves existing record");
  {
   string folder=Path.Combine(root,"disk-failure-after-commit");var link=new Link();using(var disk=new AssetJournal(folder,Product,Identity)){
    var journal=new FaultJournal(disk);var client=new AssetTransfer(link,Product,journal);await client.Connect(42,CancellationToken.None);link.AfterReceipt=()=>journal.RejectSave=true;
    await Expect<AssetUncertainException>(client.Upload(3,100,Data(),Info(),null,CancellationToken.None),"Failed local receipt clear reported success");Need(client.HasPending(3)&&journal.Load()[3]!=null,"Disk clear failure forgot pending state");
    journal.RejectSave=false;link.AfterReceipt=null;Need((await client.Reconcile(3,500,CancellationToken.None)).Stored&&link.Commits==1,"Local clear recovery resent COMMIT");
   }
  }cases.Add("Failed disk clear after confirmed commit retains pending receipt until successful reconciliation");
  {
   string folder=Path.Combine(root,"cancel");var link=new Link();using(var journal=new AssetJournal(folder,Product,Identity))using(var cancel=new CancellationTokenSource()){
    var client=new AssetTransfer(link,Product,journal);await client.Connect(42,CancellationToken.None);await Expect<OperationCanceledException>(client.Upload(3,100,Data(),Info(),new CancelProgress{Cancel=cancel},cancel.Token),"Cancellation ignored");Need(link.Commits==0&&link.Cancels==1&&journal.Load()[3]==null,"Precommit cancellation left journal despite verified old state");
   }
  }cases.Add("Precommit cancellation clears journal only after querying unchanged receiver state");
  {
   string folder=Path.Combine(root,"abandoned-reception");using(var journal=new AssetJournal(folder,Product,Identity)){var entries=new AssetPendingTransfer[5];entries[3]=Pending();journal.Save(42,entries);}
   var link=new Link{Session=42,Receiving=true};using(var journal=new AssetJournal(folder,Product,Identity)){
    var client=new AssetTransfer(link,Product,journal);await client.Connect(99,CancellationToken.None);await Expect<IOException>(client.Reconcile(3,100,CancellationToken.None),"Abandoned receive treated as committed");Need(client.HasPending(3)&&link.Cancels==0&&link.Commits==0&&link.Session==42,"Abandoned receive was unsafely canceled or forgotten");
   }
  }cases.Add("Abandoned reception remains blocked with original session; no guessed CANCEL or COMMIT");
 }
 public static int Run(string folder){
  folder=Path.GetFullPath(folder);Directory.CreateDirectory(folder);string report=Path.Combine(folder,"result.json");
  try{cases.Clear();Check(folder).GetAwaiter().GetResult();File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{passed=true,hardware_accessed=false,SDK_loaded=false,USB_opened=false,physical_identity_binding_validated=false,device_files_modified=false,cases=cases.ToArray()}));return 0;}
  catch(Exception error){File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{passed=false,hardware_accessed=false,error=error.ToString(),cases=cases.ToArray()}));return 1;}
 }
}
}
#endif
