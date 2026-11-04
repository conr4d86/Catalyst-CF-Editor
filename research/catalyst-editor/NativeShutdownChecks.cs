using System;using System.IO;using System.Reflection;using System.Threading;using System.Threading.Tasks;using System.Collections.Generic;using System.Web.Script.Serialization;
namespace CFEditor {
sealed class FakeShutdownEngine {
 public bool Fail;public int Teardown,Shutdown;
 public int setStringParamValue(string group,string key,string value){if(group!="systemState"||key!="tearDownEngine"||value!="")throw new Exception("Wrong teardown protocol");Teardown++;return 0;}
 public void shutDown(){Shutdown++;if(Fail)throw new IOException("Synthetic SDK refused shutdown");}
}
sealed class ShutdownContext:SynchronizationContext {public override void Post(SendOrPostCallback callback,object state){callback(state);}}
static class NativeShutdownChecks {
 public static int Run(string file){var previous=SynchronizationContext.Current;var checks=new List<string>();try{
  SynchronizationContext.SetSynchronizationContext(new ShutdownContext());var type=typeof(NativeBackend);const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
  foreach(bool fail in new[]{false,true}){
   var backend=new NativeBackend();var engine=new FakeShutdownEngine{Fail=fail};var lease=new FakeAssetLease();var status=new List<string>();backend.Status+=s=>status.Add(s);
   type.GetField("wrapper",flags).SetValue(backend,typeof(FakeShutdownEngine));type.GetField("native",flags).SetValue(backend,engine);type.GetField("line6Lease",flags).SetValue(backend,lease);type.GetField("shutdownConfirmed",flags).SetValue(backend,false);
   type.GetMethod("Shutdown",flags).Invoke(backend,null);
   if(engine.Teardown!=1||engine.Shutdown!=1||lease.Released!=(fail?0:1))throw new Exception("Shutdown calls/lease mismatch");
   bool confirmed=(bool)type.GetField("shutdownConfirmed",flags).GetValue(backend);object kept=type.GetField("native",flags).GetValue(backend);
   if(confirmed==fail||(!fail&&kept!=null)||(fail&&!object.ReferenceEquals(kept,engine)))throw new Exception("Unconfirmed engine was discarded");
   bool refused=false;try{backend.Disconnect().GetAwaiter().GetResult();}catch(IOException){refused=true;}if(refused!=fail)throw new Exception("Disconnect falsely reported a free USB");
   checks.Add(fail?"Failed SDK shutdown retains its engine AND lease and rejects a false USB-free receipt":"Confirmed SDK teardown releases its lease and native object");
  }
  // The actual worker completion path must return a finite failure to the
  // caller while its background owner stays alive holding the uncertain lease.
  var retainedBackend=new NativeBackend();var retainedLease=new FakeAssetLease();var settled=new TaskCompletionSource<bool>();
  type.GetField("wrapper",flags).SetValue(retainedBackend,typeof(FakeShutdownEngine));type.GetField("native",flags).SetValue(retainedBackend,new FakeShutdownEngine{Fail=true});type.GetField("line6Lease",flags).SetValue(retainedBackend,retainedLease);type.GetField("shutdownConfirmed",flags).SetValue(retainedBackend,false);type.GetField("shutdownSettled",flags).SetValue(retainedBackend,settled);
  var worker=new Thread(()=>type.GetMethod("FinishWorker",flags).Invoke(retainedBackend,null)){IsBackground=true};worker.SetApartmentState(ApartmentState.STA);type.GetField("thread",flags).SetValue(retainedBackend,worker);worker.Start();
  if(!settled.Task.Wait(3000)||settled.Task.Result||!worker.IsAlive||retainedLease.Released!=0)throw new Exception("Uncertain SDK worker exited or released its owner");
  var failedDisconnect=retainedBackend.Disconnect();try{failedDisconnect.GetAwaiter().GetResult();throw new Exception("Quarantine falsely disconnected");}catch(IOException){}if(!failedDisconnect.IsCompleted||!worker.IsAlive||retainedLease.Released!=0)throw new Exception("Quarantine waited forever or lost its owner");
  checks.Add("Unconfirmed worker settles the caller with failure without joining/releasing the quarantined owner");
  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));File.WriteAllText(file,new JavaScriptSerializer().Serialize(new{passed=true,USB_opened=false,SDK_loaded=false,device_enumeration=false,scope="Real NativeBackend shutdown with an injected managed engine and fake lease",checks}));return 0;
 }catch(Exception e){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));File.WriteAllText(file,new JavaScriptSerializer().Serialize(new{passed=false,USB_opened=false,error=e.ToString(),checks}));return 1;}finally{SynchronizationContext.SetSynchronizationContext(previous);}}
}
}
