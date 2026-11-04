using System;using System.IO;using System.Linq;using System.Threading;using System.Threading.Tasks;
namespace CFEditor {
// Data-only client. No SDK, WinMM, device enumeration or firmware installer.
// A future transport must hold the exclusive device lease, enforce deadlines,
// correlate late replies and release its handle before the editor reconnects.
interface IAssetExchange {
 Task<byte[]> Exchange(byte[] packet,int timeoutMs,CancellationToken cancel);
}
sealed class AssetReceipt {
 public byte Slot;public uint Generation,Transaction,Length,Crc;public AssetInfo Info;
 // A verified storage receipt is not an acknowledgement from the audio owner.
 public bool Activated{get{return false;}}
}
sealed class AssetProgress {
 public string Stage;public int Completed,Total;
 public AssetProgress(string stage,int completed,int total){Stage=stage;Completed=completed;Total=total;}
}
sealed class AssetUncertainException:IOException {
 public readonly byte Slot;public readonly uint Transaction;
 public AssetUncertainException(byte slot,uint transaction,Exception cause):base("No se ha podido confirmar el guardado. Consulta de nuevo esta ranura antes de reenviar.",cause){Slot=slot;Transaction=transaction;}
}
sealed class AssetResolution {
 public readonly bool Stored;public readonly AssetReceipt Receipt;
 public AssetResolution(bool stored,AssetReceipt receipt){Stored=stored;Receipt=receipt;}
}
sealed class AssetPendingTransfer {
 public readonly byte Slot;public readonly uint Transaction,Length,Crc,Generation;readonly byte[] info;readonly AssetReceipt previous;
 public AssetPendingTransfer(byte slot,uint tx,uint length,uint crc,byte[] metadata,AssetReceipt prior){
  if(slot>=5||tx==0||length!=(slot<3?25088u:7772u))throw new InvalidDataException("Transacción pendiente no válida");var next=AssetInfo.Decode(metadata);if(slot>=3&&next.Filters)throw new InvalidDataException("Metadatos NAM no válidos");
  if(prior!=null&&(prior.Slot!=slot||prior.Generation==0||prior.Generation==uint.MaxValue||prior.Transaction==0||prior.Transaction==tx||prior.Length!=length||prior.Info==null||slot>=3&&prior.Info.Filters))throw new InvalidDataException("Recibo anterior no válido");
  Slot=slot;Transaction=tx;Length=length;Crc=crc;info=(byte[])metadata.Clone();previous=Copy(prior);Generation=prior==null?1:prior.Generation+1;
 }
 internal byte[] Metadata{get{return (byte[])info.Clone();}}internal AssetReceipt Previous{get{return Copy(previous);}}
 static AssetReceipt Copy(AssetReceipt r){return r==null?null:new AssetReceipt{Slot=r.Slot,Generation=r.Generation,Transaction=r.Transaction,Length=r.Length,Crc=r.Crc,Info=r.Info==null?null:AssetInfo.Decode(r.Info.Encode())};}
 static bool Same(AssetReceipt a,AssetReceipt b){return a==null?b==null:b!=null&&a.Slot==b.Slot&&a.Generation==b.Generation&&a.Transaction==b.Transaction&&a.Length==b.Length&&a.Crc==b.Crc&&a.Info!=null&&b.Info!=null&&a.Info.Encode().SequenceEqual(b.Info.Encode());}
 public AssetResolution Resolve(AssetReceipt receipt){
  if(receipt!=null&&receipt.Slot==Slot&&receipt.Generation==Generation&&receipt.Transaction==Transaction&&receipt.Length==Length&&receipt.Crc==Crc&&receipt.Info!=null&&receipt.Info.Encode().SequenceEqual(info))return new AssetResolution(true,Copy(receipt));
  if(Same(previous,receipt))return new AssetResolution(false,Copy(receipt));
  throw new InvalidDataException("La ranura ha cambiado y no coincide con el envío pendiente. Se mantiene bloqueada para evitar sobrescribir un recurso sin confirmar.");
 }
}
sealed class AssetTransfer {
 readonly IAssetExchange transport;readonly uint product;readonly SemaphoreSlim owner=new SemaphoreSlim(1,1);
 readonly AssetPendingTransfer[] uncertain;readonly IAssetPendingJournal journal;
 uint session,capabilities;
 public bool Ready{get{return session!=0&&(capabilities&AssetWire.DataWrite)!=0;}}
 public AssetTransfer(IAssetExchange exchange,uint productId,IAssetPendingJournal pendingJournal=null){if(exchange==null)throw new ArgumentNullException("exchange");if(productId<2424833||productId>2424835)throw new NotSupportedException("Familia de amplificador no compatible");if(pendingJournal!=null&&pendingJournal.Product!=productId)throw new InvalidDataException("Registro de otro modelo");transport=exchange;product=productId;journal=pendingJournal;uncertain=journal==null?new AssetPendingTransfer[5]:journal.Load();}
 void Remember(byte slot,AssetPendingTransfer pending){var next=(AssetPendingTransfer[])uncertain.Clone();next[slot]=pending;if(journal!=null)journal.Save(session,next);uncertain[slot]=pending;}
 public bool HasPending(byte slot){if(slot>=5)throw new ArgumentOutOfRangeException("slot");return uncertain[slot]!=null;}
 static uint Read(byte[] b,int p){return (uint)b[p]|((uint)b[p+1]<<8)|((uint)b[p+2]<<16)|((uint)b[p+3]<<24);}
 static void Write(byte[] b,int p,uint value){for(int i=0;i<4;i++)b[p+i]=(byte)(value>>(8*i));}
 static uint Code(AssetMessage m){if(m.Data.Length<8)throw new InvalidDataException("Respuesta de transferencia truncada");return Read(m.Data,0);}
 static void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
 AssetMessage Message(byte op,byte slot,uint transaction,uint number,byte[] data){return new AssetMessage{Product=product,Session=session,Transaction=transaction,Number=number,Opcode=op,Slot=slot,Data=data??new byte[0]};}
 async Task<AssetMessage> Request(AssetMessage request,CancellationToken cancel,bool repeat){
  byte[] packet=AssetWire.Encode(request);
  for(int attempt=0;;attempt++){
   cancel.ThrowIfCancellationRequested();byte[] raw;
   // STATUS and BEGIN scan complete banks on the serialized FS owner. Give
   // one bounded scan time to finish; a lost reply is not a reason to queue
   // two more expensive scans behind it. Short idempotent chunks may retry.
   bool scan=request.Opcode==5||request.Opcode==2;
   try{raw=await transport.Exchange(packet,scan?30000:1500,cancel).ConfigureAwait(false);}
   catch(IOException){if(scan||!repeat||attempt>=2)throw;continue;}
   catch(TimeoutException){if(scan||!repeat||attempt>=2)throw;continue;}
   var reply=AssetWire.Decode(raw);
   Require(AssetWire.RepliesTo(request,reply),"El recibo no corresponde a esta petición o la sesión ha cambiado");Code(reply);return reply;
  }
 }
 public async Task Connect(uint nonce,CancellationToken cancel){
  if(nonce==0)throw new ArgumentOutOfRangeException("nonce");await owner.WaitAsync(cancel).ConfigureAwait(false);
  try{session=capabilities=0;if(journal!=null&&uncertain.Any(p=>p!=null)){nonce=journal.Session;if(nonce==0)throw new InvalidDataException("Falta la sesión pendiente del amplificador");}var reply=await Request(Message(1,0,nonce,0,null),cancel,true).ConfigureAwait(false);
   Require(reply.Data.Length==20&&Read(reply.Data,8)==256&&Read(reply.Data,12)==5&&Read(reply.Data,16)==2,"El firmware no ofrece un receptor de datos compatible");
   if(Code(reply)==1)throw new IOException("El receptor de datos está ocupado con otra operación. No se ha abierto una sesión nueva; espera a que termine antes de conectar.");
   Require(Code(reply)==0,"El firmware no ha aceptado la sesión de datos");
   uint flags=Read(reply.Data,4);Require((flags&~(AssetWire.DataWrite|AssetWire.AudioActivate|AssetWire.RecoverReceiving|AssetWire.PreampControl|AssetWire.AllAmpPreamp))==0,"Capacidades del firmware desconocidas");Require((flags&AssetWire.AllAmpPreamp)==0||(flags&AssetWire.PreampControl)!=0,"Capacidad de preamp incompleta");
   if((flags&AssetWire.DataWrite)==0)throw new NotSupportedException("Este firmware no permite enviar NAM/IR como datos. Requiere la instalación inicial del CF compatible.");
   if(journal!=null)journal.Save(nonce,uncertain);capabilities=flags;session=nonce;
  }finally{owner.Release();}
 }
 async Task<AssetReceipt> Status(byte slot,uint tx,CancellationToken cancel){
  var reply=await Request(Message(5,slot,tx,0,null),cancel,true).ConfigureAwait(false);return DecodeStatus(reply,slot);
 }
 internal static AssetReceipt DecodeStatus(AssetMessage reply,byte slot){if(reply==null||slot>=5)throw new InvalidDataException("Estado de recurso no válido.");uint code=Code(reply);
  if(code==7){Require(reply.Data.Length==8,"Estado vacío no válido");return null;}
  if(code==6||code==1){Require(reply.Data.Length==8,"Estado pendiente no válido");return new AssetReceipt{Slot=slot,Generation=0};}
  Require(code==0&&reply.Data.Length==204,"No se puede leer el estado de la ranura");
  var info=AssetInfo.Decode(reply.Data.Skip(20).ToArray());Require(slot<3||!info.Filters,"Metadatos NAM incompatibles");
  var receipt=new AssetReceipt{Slot=slot,Generation=Read(reply.Data,4),Transaction=Read(reply.Data,8),Length=Read(reply.Data,12),Crc=Read(reply.Data,16),Info=info};
  Require(receipt.Generation>0&&receipt.Transaction!=0&&receipt.Length==(slot<3?25088u:7772u),"Recibo de almacenamiento no válido");return receipt;
 }
 public async Task<AssetReceipt> ReadSlot(byte slot,uint transaction,CancellationToken cancel){
  if(slot>=5||transaction==0)throw new ArgumentOutOfRangeException();await owner.WaitAsync(cancel).ConfigureAwait(false);
  try{if(!Ready)throw new InvalidOperationException("Comprueba primero el receptor del amplificador");for(int poll=0;poll<100;poll++){var result=await Status(slot,transaction,cancel).ConfigureAwait(false);if(result==null||result.Generation!=0)return result;await Task.Delay(40,cancel).ConfigureAwait(false);}throw new IOException("El amplificador sigue preparando esta ranura. Vuelve a consultar su estado.");}finally{owner.Release();}
 }
 public async Task<AssetResolution> Reconcile(byte slot,uint query,CancellationToken cancel){
  if(slot>=5||query==0)throw new ArgumentOutOfRangeException();await owner.WaitAsync(cancel).ConfigureAwait(false);
  try{
   if(!Ready)throw new InvalidOperationException("Comprueba primero el receptor del amplificador");var pending=uncertain[slot];if(pending==null)throw new InvalidOperationException("Esta ranura no tiene un envío pendiente de comprobar");
   var receipt=await Status(slot,query,cancel).ConfigureAwait(false);
   if(receipt!=null&&receipt.Generation==0&&(capabilities&AssetWire.RecoverReceiving)!=0){
    var expected=new byte[196];Write(expected,0,pending.Generation);Write(expected,4,pending.Length);Write(expected,8,pending.Crc);Buffer.BlockCopy(pending.Metadata,0,expected,12,184);
    // The receiver atomically abandons only a matching phase-1 reception.
    // No unconditional CANCEL and no assumption based on STATUS timing.
    try{var recovered=await Request(Message(7,slot,pending.Transaction,0,expected),cancel,false).ConfigureAwait(false);Require(Code(recovered)==0||Code(recovered)==1||Code(recovered)==3,"La recuperación condicional no ha sido aceptada");}
    catch(IOException){}catch(TimeoutException){}
    // Even a lost recovery response is resolved by a fresh receipt query.
    receipt=await Status(slot,query,cancel).ConfigureAwait(false);
   }
   if(receipt!=null&&receipt.Generation==0)throw new IOException("El amplificador sigue verificando esta ranura. Vuelve a comprobarla en unos segundos.");
   var resolved=pending.Resolve(receipt);Remember(slot,null);return resolved;
  }finally{owner.Release();}
 }
 static bool Matches(AssetReceipt r,uint tx,uint length,uint crc,byte[] info){return r!=null&&r.Generation>0&&r.Transaction==tx&&r.Length==length&&r.Crc==crc&&r.Info!=null&&r.Info.Encode().SequenceEqual(info);}
 static byte[] Payload(byte slot,byte[] bytes){
  if(bytes==null||bytes.Length!=(slot<3?25088:7772))throw new InvalidDataException("Tamaño de recurso no compatible con esta ranura");
  byte[] copy=(byte[])bytes.Clone();bool nonzero=false;
  for(int i=0;i<copy.Length;i+=4){uint v=Read(copy,i)&0x7fffffff;if(v>0x4b800000)throw new InvalidDataException("El recurso contiene valores no válidos");nonzero|=v!=0;}
  if(!nonzero)throw new InvalidDataException("El recurso está vacío");return copy;
 }
 public async Task<AssetReceipt> Upload(byte slot,uint transaction,byte[] bytes,AssetInfo metadata,IProgress<AssetProgress> progress,CancellationToken cancel){
  if(slot>=5||transaction==0||metadata==null)throw new ArgumentOutOfRangeException();
  byte[] data=Payload(slot,bytes),info=metadata.Encode();if(slot>=3&&metadata.Filters)throw new InvalidDataException("Los cortes del IR no se aplican al NAM");
  uint digest=AssetWire.Crc(data);await owner.WaitAsync(cancel).ConfigureAwait(false);bool began=false,commitAttempted=false;Exception failure=null;AssetReceipt previous=null;
  try{
   if(!Ready)throw new InvalidOperationException("Comprueba primero el receptor del amplificador");
   if(uncertain[slot]!=null)throw new InvalidOperationException("Comprueba el guardado pendiente de esta ranura antes de volver a enviar.");
   if(progress!=null)progress.Report(new AssetProgress("Preparando",0,data.Length));
   previous=await Status(slot,transaction,cancel).ConfigureAwait(false);
   if(Matches(previous,transaction,(uint)data.Length,digest,info))return previous;
   Require(previous==null||previous.Generation>0,"Hay una transferencia pendiente; consulta antes de reemplazarla");
   Require(previous==null||previous.Generation<uint.MaxValue,"La generación de esta ranura ha alcanzado su límite");
   Require(previous==null||previous.Transaction!=transaction,"La transacción ya identifica otro recurso");uint generation=previous==null?0:previous.Generation;
   var begin=new byte[8+info.Length];Write(begin,0,(uint)data.Length);Write(begin,4,digest);Buffer.BlockCopy(info,0,begin,8,info.Length);
   // Write-ahead on disk BEFORE BEGIN, so a PC crash during chunks cannot lose
   // the session/transaction either. Native transport remains opt-in/unwired.
   if(journal!=null)Remember(slot,new AssetPendingTransfer(slot,transaction,(uint)data.Length,digest,info,previous));
   // BEGIN/CHUNK are idempotent with exactly the same transaction and bytes.
   began=true;var reply=await Request(Message(2,slot,transaction,generation,begin),cancel,true).ConfigureAwait(false);
   Require(Code(reply)==0||Code(reply)==8,"El amplificador no ha aceptado el recurso");
   for(int offset=0;offset<data.Length;offset+=256){
    int count=Math.Min(256,data.Length-offset);var chunk=new byte[count];Buffer.BlockCopy(data,offset,chunk,0,count);
    reply=await Request(Message(3,slot,transaction,(uint)offset,chunk),cancel,true).ConfigureAwait(false);
    Require((Code(reply)==0||Code(reply)==8)&&Read(reply.Data,4)>=(uint)(offset+count)&&Read(reply.Data,4)<=(uint)data.Length,"Bloque de datos no confirmado");
    if(progress!=null)progress.Report(new AssetProgress("Enviando",offset+count,data.Length));
   }
   cancel.ThrowIfCancellationRequested();commitAttempted=true;
   // Never repeat COMMIT after a lost response: resolve with a receipt query.
   try{reply=await Request(Message(4,slot,transaction,0,null),cancel,false).ConfigureAwait(false);Require(Code(reply)==6||Code(reply)==0||Code(reply)==9,"No se ha aceptado la verificación final");}
   catch(IOException){}catch(TimeoutException){}
   if(progress!=null)progress.Report(new AssetProgress("Verificando",data.Length,data.Length));
   for(int poll=0;poll<100;poll++){
    var stored=await Status(slot,transaction,cancel).ConfigureAwait(false);
    if(stored!=null&&stored.Generation>0){Require(stored.Generation==generation+1&&Matches(stored,transaction,(uint)data.Length,digest,info),"El recibo no confirma el recurso enviado");Remember(slot,null);return stored;}
    if(stored==null)throw new InvalidDataException("El recurso no consta como guardado");
    await Task.Delay(40,cancel).ConfigureAwait(false);
   }throw new TimeoutException("El amplificador no ha confirmado el guardado");
  }catch(Exception error){
   if(commitAttempted){if(uncertain[slot]==null)uncertain[slot]=new AssetPendingTransfer(slot,transaction,(uint)data.Length,digest,info,previous);throw new AssetUncertainException(slot,transaction,error);}
   failure=error;
  }finally{if(failure==null)owner.Release();}
  // C# 5 does not support await inside catch/finally. Keep the lease until
  // the best-effort cancellation finishes, then preserve the original stack.
  try{
   if(began){try{await Request(Message(6,slot,transaction,0,null),CancellationToken.None,false).ConfigureAwait(false);}catch{}
    if(journal!=null&&uncertain[slot]!=null){try{var old=await Status(slot,transaction,CancellationToken.None).ConfigureAwait(false);if(old==null||old.Generation>0){uncertain[slot].Resolve(old);Remember(slot,null);}}catch{}}
   }
   System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();throw new InvalidOperationException();
  }finally{owner.Release();}
 }
}
}
