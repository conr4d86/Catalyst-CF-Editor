using System;using System.IO;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;
namespace CFEditor {
interface IAssetDataConnection:IAssetMidiChannel {
 string PhysicalIdentity{get;}bool Closed{get;}
 Task CloseAsync(int timeoutMs=4000);
}
// Owns the complete data connection, stream adapter and persistent journal.
// Creating a session performs no discovery, file IO, SDK call or native open.
// A failed close retains every owner and prevents reuse until an explicit retry.
sealed class AssetDataSession {
 static readonly List<AssetDataSession> retained=new List<AssetDataSession>();
 readonly Func<uint,Task<IAssetDataConnection>> open;readonly Func<uint,string,AssetJournal> journalFactory;readonly Func<IAssetDataConnection,IAssetExchange,CancellationToken,Task<string>> identify;
 readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);IAssetDataConnection channel;AssetMidiExchange exchange;AssetJournal journal;
 AssetTransfer client;string verifiedIdentity;volatile bool failed,closing;
 public event Action<Exception> Failed;
 public bool Ready{get{return !failed&&!closing&&client!=null&&client.Ready;}}
 public bool HasOwner{get{return channel!=null;}}
 public string VerifiedIdentity{get{if(!Ready||verifiedIdentity==null)throw new IOException("Falta la identidad comprobada de la conexión.");return verifiedIdentity;}}
 public AssetTransfer Client{get{if(!Ready)throw new IOException("Comprueba de nuevo la conexión de datos del amplificador.");return client;}}
 internal AssetDataSession(Func<uint,Task<IAssetDataConnection>> opener,Func<uint,string,AssetJournal> journals,Func<IAssetDataConnection,IAssetExchange,CancellationToken,Task<string>> identity=null){if(opener==null||journals==null)throw new ArgumentNullException();open=opener;journalFactory=journals;identify=identity;}
 internal static AssetDataSession Native(string journalRoot){
  return new AssetDataSession(async product=>{
   NativeBackend.RefuseOtherUSBApps();
   var binding=await Task.Run(()=>AssetPortDiscovery.Discover(product,false)).ConfigureAwait(false);
   return await AssetPortOwner.Open(binding,new WinmmAssetPortDriver(),()=>new Line6USBLease()).ConfigureAwait(false);
  },(product,identity)=>new AssetJournal(journalRoot,product,identity),(channel,exchange,cancel)=>AssetDeviceIdentity.Read(exchange,channel.Product,cancel,true));
 }
 void Broken(Exception error){failed=true;var notify=Failed;if(notify!=null)try{notify(error);}catch{}}
 void Retain(){lock(retained)if(!retained.Contains(this))retained.Add(this);}
 async Task CloseCore(){
  closing=true;client=null;
  try{
   if(exchange!=null){exchange.Dispose();exchange=null;}
   if(channel!=null){await channel.CloseAsync().ConfigureAwait(false);if(!channel.Closed)throw new IOException("No se ha confirmado el cierre del canal de datos.");channel.Failed-=Broken;channel=null;}
   if(journal!=null){journal.Dispose();journal=null;}
   lock(retained)retained.Remove(this);failed=false;verifiedIdentity=null;
  }catch{failed=true;Retain();throw;}finally{closing=false;}
 }
 public async Task Connect(uint product,uint nonce,CancellationToken cancel){
  if(!DeviceFamily.Supported((int)product)||nonce==0)throw new ArgumentException("Falta el modelo comprobado del amplificador.");
  await gate.WaitAsync(cancel).ConfigureAwait(false);if(channel!=null||journal!=null){gate.Release();throw new InvalidOperationException("Cierra la conexión de datos anterior antes de abrir otra.");}Exception failure=null;
  try{
   failed=false;cancel.ThrowIfCancellationRequested();channel=await open(product).ConfigureAwait(false);
   if(channel==null)throw new IOException("No se ha recibido un canal de datos.");channel.Failed+=Broken;
   if(channel.Product!=product||channel.Closed)throw new IOException("El canal no pertenece al modelo comprobado.");
   cancel.ThrowIfCancellationRequested();exchange=new AssetMidiExchange(channel);
   if(identify==null&&channel.PhysicalIdentity.StartsWith("CFCX-PNP-CONNECTION-1:",StringComparison.Ordinal))throw new IOException("Una ubicación de puerto no sirve como identidad persistente del amplificador.");
   string identity=identify==null?channel.PhysicalIdentity:await identify(channel,exchange,cancel).ConfigureAwait(false);
   cancel.ThrowIfCancellationRequested();journal=journalFactory(product,identity);
   var candidate=new AssetTransfer(exchange,product,journal);await candidate.Connect(nonce,cancel).ConfigureAwait(false);
   if(failed)throw new IOException("El canal se interrumpió al comprobar el receptor.");verifiedIdentity=identity;client=candidate;
  }catch(Exception error){failure=error;}
  // C# 5: asynchronous compensation is outside catch/finally.
  if(failure!=null){try{await CloseCore().ConfigureAwait(false);}catch(Exception cleanup){gate.Release();throw new IOException("La conexión falló y su cierre no está confirmado. No se abrirá el editor hasta cerrarla.",new AggregateException(failure,cleanup));}gate.Release();System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();}
  gate.Release();
 }
 public async Task Close(){await gate.WaitAsync().ConfigureAwait(false);try{await CloseCore().ConfigureAwait(false);}finally{gate.Release();}}
}
}
