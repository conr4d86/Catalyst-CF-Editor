using System;using System.IO;using System.Linq;using System.Threading;using System.Threading.Tasks;
namespace CFEditor {
sealed class PreampSettings {
 public uint Type;public bool Enabled,Boost,Bright;public float Drive=.5f,Tone=.5f,Level=1f;
 static bool Valid(float value,float high){return !float.IsNaN(value)&&!float.IsInfinity(value)&&value>=0&&value<=high;}
 public byte[] Encode(){if(Type>3||!Valid(Drive,1)||!Valid(Tone,1)||!Valid(Level,2))throw new InvalidDataException("Ajustes de preamp no válidos.");var b=new byte[32];uint[] words={Type,Enabled?1u:0u,Boost?1u:0u,Bright?1u:0u};for(int i=0;i<4;i++)Buffer.BlockCopy(BitConverter.GetBytes(words[i]),0,b,i*4,4);foreach(var item in new[]{Tuple.Create(16,Drive),Tuple.Create(20,Tone),Tuple.Create(24,Level)})Buffer.BlockCopy(BitConverter.GetBytes(item.Item2),0,b,item.Item1,4);return b;}
 public static PreampSettings Decode(byte[] b){if(b==null||b.Length!=32||BitConverter.ToUInt32(b,4)>1||BitConverter.ToUInt32(b,8)>1||BitConverter.ToUInt32(b,12)>1||BitConverter.ToUInt32(b,28)!=0)throw new InvalidDataException("Respuesta de preamp no válida.");var s=new PreampSettings{Type=BitConverter.ToUInt32(b,0),Enabled=BitConverter.ToUInt32(b,4)!=0,Boost=BitConverter.ToUInt32(b,8)!=0,Bright=BitConverter.ToUInt32(b,12)!=0,Drive=BitConverter.ToSingle(b,16),Tone=BitConverter.ToSingle(b,20),Level=BitConverter.ToSingle(b,24)};s.Encode();return s;}
 public PreampSettings Copy(){return Decode(Encode());}
}
sealed class PreampSnapshot {
 public readonly byte Index;public readonly uint Revision;public readonly string AssetID;readonly byte[] data;
 public PreampSnapshot(byte index,uint revision,PreampSettings settings,string assetID=null){if(index>=13||revision==0||settings==null)throw new InvalidDataException("Falta el contexto del preset.");Index=index;Revision=revision;AssetID=assetID;data=settings.Encode();}
 public PreampSettings Settings{get{return PreampSettings.Decode(data);}}
}
sealed class PreampPendingSave {
 public readonly uint Session,Transaction;public readonly PreampSnapshot Expected;
 public PreampPendingSave(uint session,uint transaction,PreampSnapshot snapshot){if(session==0||transaction==0||snapshot==null)throw new InvalidDataException("Registro de preamp no válido.");Session=session;Transaction=transaction;Expected=new PreampSnapshot(snapshot.Index,snapshot.Revision,snapshot.Settings);}
}
interface IPreampJournal {PreampPendingSave Load();void Save(PreampPendingSave entry);}
sealed class PreampUncertainException:IOException {public PreampUncertainException(Exception error):base("El preamp aún no tiene confirmación de guardado. Comprueba el guardado antes de volver a enviarlo.",error){}}
// Only an explicit negative acknowledgement can be retried. A lost or malformed
// reply is not evidence that a SET was rejected, and SAVE is never repeated.
sealed class PreampSetRejectedException:IOException {
 public readonly uint Status,Index,Revision;
 public PreampSetRejectedException(uint status,uint index,uint revision):base("Ha cambiado el preamp; no se ha aplicado el ajuste. Vuelve a leerlo."){Status=status;Index=index;Revision=revision;}
}
// One serialized, capability-gated client. Its channel is supplied by the
// verified device owner; this type never discovers, opens or resets a device.
sealed class PreampClient {
 readonly IAssetExchange exchange;readonly uint product;readonly IPreampJournal journal;readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
 uint session,counter;PreampPendingSave pending;PreampSnapshot current;
 public bool Ready{get{return session!=0;}}public bool HasPending{get{return pending!=null;}}public bool AllAmpPreamp{get;private set;}
 public PreampClient(IAssetExchange channel,uint productId,IPreampJournal saveJournal){if(channel==null||saveJournal==null)throw new ArgumentNullException();if(!DeviceFamily.Supported((int)productId))throw new NotSupportedException("Modelo no compatible.");exchange=channel;product=productId;journal=saveJournal;pending=journal.Load();}
 uint Next(){if(counter==uint.MaxValue)throw new IOException("Vuelve a conectar para iniciar otra sesión de controles.");return ++counter;}
 AssetMessage Message(byte opcode,byte index,uint transaction,uint revision=0,byte[] data=null){return new AssetMessage{Product=product,Session=opcode==1?0:session,Transaction=transaction,Number=revision,Opcode=opcode,Slot=index,Data=data??new byte[0]};}
 static uint U(byte[] b,int p){return BitConverter.ToUInt32(b,p);}
 async Task<AssetMessage> Request(AssetMessage request,CancellationToken token){var reply=AssetWire.Decode(await exchange.Exchange(AssetWire.Encode(request),request.Opcode==5?30000:1500,token).ConfigureAwait(false));if(!AssetWire.RepliesTo(request,reply)||reply.Data.Length<8)throw new InvalidDataException("La respuesta del preamp no corresponde a esta petición.");return reply;}
 static PreampSnapshot Snapshot(AssetMessage reply){if(reply.Data.Length!=44||U(reply.Data,4)>=13||U(reply.Data,8)==0)throw new InvalidDataException("El amplificador no ha confirmado el contexto del preamp.");return new PreampSnapshot((byte)U(reply.Data,4),U(reply.Data,8),PreampSettings.Decode(reply.Data.Skip(12).ToArray()));}
 static uint Code(AssetMessage reply){return U(reply.Data,0);}
 void Need(){if(!Ready)throw new IOException("Conecta un firmware compatible antes de editar el preamp.");}
 async Task<PreampSnapshot> ReadCore(CancellationToken cancel){
  Need();for(int i=0;i<20;i++){
   var reply=await Request(Message(9,0,Next()),cancel).ConfigureAwait(false);if(Code(reply)==0)return current=Snapshot(reply);
   if(Code(reply)!=1)throw new IOException("No se pueden leer los controles de preamp.");await Task.Delay(25,cancel).ConfigureAwait(false);
  }throw new IOException("El preamp sigue cambiando de preset. Espera un momento y vuelve a leerlo.");
 }
 public async Task Connect(uint nonce,CancellationToken cancel){if(nonce==0)throw new ArgumentOutOfRangeException("nonce");await gate.WaitAsync(cancel).ConfigureAwait(false);try{
  session=0;AllAmpPreamp=false;current=null;counter=nonce;if(pending!=null)nonce=pending.Session;var reply=await Request(Message(1,0,nonce),cancel).ConfigureAwait(false);
  if(Code(reply)!=0)throw new IOException("El receptor está ocupado; no se ha abierto otra sesión de controles.");
  if(reply.Data.Length!=20||U(reply.Data,8)!=256||U(reply.Data,12)!=5||U(reply.Data,16)!=2||(U(reply.Data,4)&AssetWire.PreampControl)==0||(U(reply.Data,4)&~31u)!=0)throw new NotSupportedException("El firmware no anuncia controles de preamp compatibles.");
  session=nonce;try{await ReadCore(cancel).ConfigureAwait(false);AllAmpPreamp=(U(reply.Data,4)&AssetWire.AllAmpPreamp)!=0;}catch{session=0;throw;}
 }finally{gate.Release();}}
 public async Task<AssetReceipt> ReadAsset(byte slot,CancellationToken cancel){if(slot>=5)throw new ArgumentOutOfRangeException("slot");await gate.WaitAsync(cancel).ConfigureAwait(false);try{Need();return AssetTransfer.DecodeStatus(await Request(Message(5,slot,Next()),cancel).ConfigureAwait(false),slot);}finally{gate.Release();}}
 public async Task<PreampSnapshot> Read(CancellationToken cancel){await gate.WaitAsync(cancel).ConfigureAwait(false);try{return await ReadCore(cancel).ConfigureAwait(false);}finally{gate.Release();}}
 public async Task<PreampSnapshot> Set(PreampSnapshot expected,PreampSettings settings,CancellationToken cancel){if(expected==null||settings==null)throw new ArgumentNullException();byte[] data=settings.Encode();await gate.WaitAsync(cancel).ConfigureAwait(false);try{
  Need();if(pending!=null)throw new PreampUncertainException(null);if(current==null||current.Index!=expected.Index||current.Revision!=expected.Revision)throw new IOException("Ha cambiado el preset; vuelve a leer el preamp.");
  current=null;var reply=await Request(Message(10,expected.Index,Next(),expected.Revision,data),cancel).ConfigureAwait(false);
  if(Code(reply)!=0){if(reply.Data.Length==44&&(Code(reply)==1||Code(reply)==2||Code(reply)==3))throw new PreampSetRejectedException(Code(reply),U(reply.Data,4),U(reply.Data,8));throw new IOException("El amplificador ha rechazado el ajuste de preamp ("+Code(reply)+").");}var accepted=Snapshot(reply);
  if(accepted.Index!=expected.Index||accepted.Revision==expected.Revision||!accepted.Settings.Encode().SequenceEqual(data))throw new InvalidDataException("El amplificador no ha confirmado el ajuste solicitado.");return current=accepted;
 }finally{gate.Release();}}
 async Task<PreampSnapshot> Resolve(CancellationToken cancel,int attempts){
  if(pending==null)throw new InvalidOperationException("No hay un guardado pendiente.");for(int i=0;i<attempts;i++){
   var reply=await Request(Message(12,pending.Expected.Index,pending.Transaction),cancel).ConfigureAwait(false);uint status=Code(reply);
   if(status==0){var saved=Snapshot(reply);if(saved.Index!=pending.Expected.Index||!saved.Settings.Encode().SequenceEqual(pending.Expected.Settings.Encode()))throw new InvalidDataException("El recibo del preamp no coincide con el guardado pendiente.");journal.Save(null);pending=null;return current=saved;}
   if(status!=1&&status!=6)throw new IOException("No se puede confirmar este guardado. Se conserva el registro y no se volverá a enviar automáticamente.");await Task.Delay(25,cancel).ConfigureAwait(false);
  }throw new TimeoutException("El preamp todavía no ha confirmado su guardado.");
 }
 public async Task<PreampSnapshot> Save(PreampSnapshot expected,CancellationToken cancel){if(expected==null)throw new ArgumentNullException();await gate.WaitAsync(cancel).ConfigureAwait(false);try{
  Need();if(pending!=null)throw new PreampUncertainException(null);if(current==null||current.Index!=expected.Index||current.Revision!=expected.Revision)throw new IOException("Ha cambiado el preset; vuelve a leer el preamp antes de guardar.");
  var entry=new PreampPendingSave(session,Next(),expected);journal.Save(entry);pending=entry;
  try{var reply=await Request(Message(11,expected.Index,entry.Transaction,expected.Revision),cancel).ConfigureAwait(false);if(Code(reply)!=0&&Code(reply)!=6)throw new IOException("El amplificador no ha aceptado el guardado del preamp.");return await Resolve(cancel,100).ConfigureAwait(false);}catch(Exception error){throw new PreampUncertainException(error);}
 }finally{gate.Release();}}
 public async Task<PreampSnapshot> Reconcile(CancellationToken cancel){await gate.WaitAsync(cancel).ConfigureAwait(false);try{Need();try{return await Resolve(cancel,100).ConfigureAwait(false);}catch(Exception error){throw new PreampUncertainException(error);}}finally{gate.Release();}}
}
}
