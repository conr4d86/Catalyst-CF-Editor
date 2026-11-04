using System;using System.IO;using System.Linq;using System.Text;using System.Security.Cryptography;
namespace CFEditor {
interface IAssetPendingJournal {
 uint Product{get;}uint Session{get;}
 AssetPendingTransfer[] Load();
 void Save(uint session,AssetPendingTransfer[] pending);
}
// The native owner must supply a VERIFIED physical-device identity, not a port
// name, model number or user-entered label. This class never discovers devices.
// No user audio/model data or source paths are stored, only receipts/metadata.
sealed class AssetJournal:IAssetPendingJournal,IDisposable {
 readonly string path;readonly byte[] identity;readonly FileStream lease;AssetPendingTransfer[] entries;bool disposed;
 public uint Product{get;private set;}public uint Session{get;private set;}
 static byte[] Hash(byte[] b){using(var h=SHA256.Create())return h.ComputeHash(b);}
 static AssetPendingTransfer[] Snapshot(AssetPendingTransfer[] values){
  if(values==null||values.Length!=5)throw new InvalidDataException("Registro de transferencias no válido");
  var copy=new AssetPendingTransfer[5];for(int i=0;i<5;i++){var p=values[i];if(p==null)continue;if(p.Slot!=i)throw new InvalidDataException("La ranura no coincide con su registro");copy[i]=new AssetPendingTransfer(p.Slot,p.Transaction,p.Length,p.Crc,p.Metadata,p.Previous);}
  return copy;
 }
 static byte[] Marker(byte[] key){var b=new byte[40];Buffer.BlockCopy(Encoding.ASCII.GetBytes("CFJL"),0,b,0,4);b[4]=1;Buffer.BlockCopy(key,0,b,8,32);return b;}
 public AssetJournal(string root,uint product,string verifiedDeviceIdentity){
  if(product<2424833||product>2424835)throw new NotSupportedException("Modelo no compatible con el registro de datos");
  if(string.IsNullOrWhiteSpace(verifiedDeviceIdentity)||verifiedDeviceIdentity.Length>1024||verifiedDeviceIdentity.Any(c=>char.IsControl(c)))throw new ArgumentException("Falta una identidad física comprobada del amplificador");
  Product=product;identity=Hash(new UTF8Encoding(false,true).GetBytes(product.ToString(System.Globalization.CultureInfo.InvariantCulture)+":"+verifiedDeviceIdentity));
  root=Path.GetFullPath(root);Directory.CreateDirectory(root);string stem="pending-"+BitConverter.ToString(identity).Replace("-","").ToLowerInvariant();path=Path.Combine(root,stem+".cfj");
  lease=new FileStream(Path.Combine(root,stem+".lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  try{
   byte[] marker=Marker(identity);if(lease.Length!=0){if(lease.Length!=marker.Length)throw new InvalidDataException("El registro de propiedad está dañado");var old=new byte[marker.Length];if(lease.Read(old,0,old.Length)!=old.Length||!old.SequenceEqual(marker))throw new InvalidDataException("El registro de propiedad no corresponde a este amplificador");if(!File.Exists(path))throw new InvalidDataException("Falta el registro anterior. El envío permanece bloqueado.");}
   if(File.Exists(path))Read();else{entries=new AssetPendingTransfer[5];Save(0,entries);}
   if(lease.Length==0){lease.Write(marker,0,marker.Length);lease.Flush(true);}
  }catch{lease.Dispose();throw;}
 }
 byte[] Encode(uint session,AssetPendingTransfer[] pending){
  if(session==0&&pending.Any(p=>p!=null))throw new InvalidDataException("Una transferencia pendiente necesita su sesión original");
  using(var memory=new MemoryStream())using(var w=new BinaryWriter(memory)){
   w.Write(Encoding.ASCII.GetBytes("CFJ1"));w.Write(1u);w.Write(Product);w.Write(session);w.Write(identity);w.Write((uint)pending.Count(p=>p!=null));w.Write(0u);
   foreach(var p in pending.Where(p=>p!=null)){
    w.Write((uint)p.Slot);w.Write(p.Transaction);w.Write(p.Length);w.Write(p.Crc);w.Write(p.Generation);w.Write(p.Metadata);var old=p.Previous;
    w.Write(old==null?0u:1u);if(old==null)w.Write(new byte[200]);else{w.Write(old.Generation);w.Write(old.Transaction);w.Write(old.Length);w.Write(old.Crc);w.Write(old.Info.Encode());}
   }w.Flush();byte[] body=memory.ToArray();return body.Concat(Hash(body)).ToArray();
  }
 }
 void Read(){
  byte[] bytes;using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
   if(file.Length<88||file.Length>2128)throw new InvalidDataException("Tamaño de registro no válido; no se permite enviar datos");bytes=new byte[(int)file.Length];int offset=0;while(offset<bytes.Length){int n=file.Read(bytes,offset,bytes.Length-offset);if(n==0)throw new EndOfStreamException("Registro truncado");offset+=n;}
  }
  byte[] body=bytes.Take(bytes.Length-32).ToArray();if(!Hash(body).SequenceEqual(bytes.Skip(body.Length)))throw new InvalidDataException("El registro de transferencias está dañado; el envío permanece bloqueado");
  using(var r=new BinaryReader(new MemoryStream(body))){
   if(Encoding.ASCII.GetString(r.ReadBytes(4))!="CFJ1"||r.ReadUInt32()!=1||r.ReadUInt32()!=Product)throw new InvalidDataException("Registro de otra versión o modelo");
   uint session=r.ReadUInt32();if(!r.ReadBytes(32).SequenceEqual(identity))throw new InvalidDataException("Registro de otro amplificador");uint count=r.ReadUInt32();if(count>5||r.ReadUInt32()!=0||body.Length!=56+count*408)throw new InvalidDataException("Estructura de registro no válida");
   var loaded=new AssetPendingTransfer[5];int last=-1;
   for(int i=0;i<count;i++){
    uint slot=r.ReadUInt32(),tx=r.ReadUInt32(),length=r.ReadUInt32(),crc=r.ReadUInt32(),generation=r.ReadUInt32();if(slot>=5||slot<=last)throw new InvalidDataException("Ranuras duplicadas o fuera de orden");last=(int)slot;byte[] info=r.ReadBytes(184);uint hasPrevious=r.ReadUInt32();byte[] prior=r.ReadBytes(200);AssetReceipt previous=null;
    if(hasPrevious>1)throw new InvalidDataException("Estado anterior no válido");if(hasPrevious==0){if(prior.Any(b=>b!=0))throw new InvalidDataException("Datos anteriores inesperados");}
    else using(var old=new BinaryReader(new MemoryStream(prior))){previous=new AssetReceipt{Slot=(byte)slot,Generation=old.ReadUInt32(),Transaction=old.ReadUInt32(),Length=old.ReadUInt32(),Crc=old.ReadUInt32(),Info=AssetInfo.Decode(old.ReadBytes(184))};}
    var pending=new AssetPendingTransfer((byte)slot,tx,length,crc,info,previous);if(pending.Generation!=generation)throw new InvalidDataException("Generación pendiente no válida");loaded[slot]=pending;
   }
   if(session==0&&count!=0)throw new InvalidDataException("Falta la sesión de recuperación");Session=session;entries=loaded;
  }
 }
 public AssetPendingTransfer[] Load(){if(disposed)throw new ObjectDisposedException("AssetJournal");return Snapshot(entries);}
 public void Save(uint session,AssetPendingTransfer[] pending){
  if(disposed)throw new ObjectDisposedException("AssetJournal");var next=Snapshot(pending);byte[] bytes=Encode(session,next);string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{
   using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){file.Write(bytes,0,bytes.Length);file.Flush(true);}
   // Never delete the old record as a fallback when replacement fails.
   if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
   entries=next;Session=session;
  }finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch{}}
 }
 public void Dispose(){if(disposed)return;disposed=true;lease.Dispose();}
}
}
