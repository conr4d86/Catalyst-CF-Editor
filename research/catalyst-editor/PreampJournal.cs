using System;using System.IO;using System.Linq;using System.Text;using System.Security.Cryptography;
namespace CFEditor {
// A fixed-size write-ahead journal for ONE uncertain preamp SAVE. Only the
// verified UID+CF-edition key selects it. No paths to user captures are stored.
sealed class PreampJournal:IPreampJournal,IDisposable {
 readonly string path;readonly byte[] identity;readonly FileStream lease;PreampPendingSave pending;bool disposed;
 static byte[] Hash(byte[] b){using(var h=SHA256.Create())return h.ComputeHash(b);}
 static byte[] Word(uint value){return BitConverter.GetBytes(value);}
 static uint U(byte[] b,int p){return BitConverter.ToUInt32(b,p);}
 readonly uint product;
 public PreampJournal(string root,uint productId,string verifiedIdentity){
  if(!DeviceFamily.Supported((int)productId)||verifiedIdentity==null||!verifiedIdentity.StartsWith("CFCX-UID2:",StringComparison.Ordinal)||verifiedIdentity.Length!=91||verifiedIdentity[26]!=':'||verifiedIdentity.Substring(10,16).Any(c=>!Uri.IsHexDigit(c))||verifiedIdentity.Substring(27).Any(c=>!Uri.IsHexDigit(c)))throw new InvalidDataException("Falta la identidad y la edición comprobadas del amplificador.");
  product=productId;identity=Hash(Encoding.ASCII.GetBytes(productId.ToString(System.Globalization.CultureInfo.InvariantCulture)+":"+verifiedIdentity));root=Path.GetFullPath(root);Directory.CreateDirectory(root);string stem="preamp-"+BitConverter.ToString(identity).Replace("-","");path=Path.Combine(root,stem+".cfj");lease=new FileStream(Path.Combine(root,stem+".lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  try{var marker=Encoding.ASCII.GetBytes("CFPL0001").Concat(identity).ToArray();if(lease.Length!=0){var old=new byte[40];if(lease.Length!=40||lease.Read(old,0,40)!=40||!old.SequenceEqual(marker)||!File.Exists(path))throw new InvalidDataException("Falta el registro anterior de guardado del preamp.");}
   if(File.Exists(path))Read();else Save(null);if(lease.Length==0){lease.Write(marker,0,marker.Length);lease.Flush(true);}
  }catch{lease.Dispose();throw;}
 }
 void Read(){if(new FileInfo(path).Length!=128)throw new InvalidDataException("Registro de preamp dañado.");var b=File.ReadAllBytes(path);if(Encoding.ASCII.GetString(b,0,4)!="CFPJ"||U(b,4)!=1||U(b,8)!=product||!b.Skip(32).Take(32).SequenceEqual(identity)||!Hash(b.Take(96).ToArray()).SequenceEqual(b.Skip(96)))throw new InvalidDataException("Registro de preamp no válido.");
  if(U(b,28)==0){if(b.Skip(12).Take(20).Any(v=>v!=0)||b.Skip(64).Take(32).Any(v=>v!=0))throw new InvalidDataException("Registro vacío de preamp no válido.");pending=null;}
  else if(U(b,28)==1&&U(b,20)<13)pending=new PreampPendingSave(U(b,12),U(b,16),new PreampSnapshot((byte)U(b,20),U(b,24),PreampSettings.Decode(b.Skip(64).Take(32).ToArray())));
  else throw new InvalidDataException("Estado de guardado no válido.");
 }
 public PreampPendingSave Load(){if(disposed)throw new ObjectDisposedException("PreampJournal");return pending;}
 public void Save(PreampPendingSave entry){if(disposed)throw new ObjectDisposedException("PreampJournal");var body=new byte[96];Buffer.BlockCopy(Encoding.ASCII.GetBytes("CFPJ"),0,body,0,4);Action<int,uint> put=(at,v)=>Buffer.BlockCopy(Word(v),0,body,at,4);put(4,1);put(8,product);Buffer.BlockCopy(identity,0,body,32,32);
  if(entry!=null){put(12,entry.Session);put(16,entry.Transaction);put(20,entry.Expected.Index);put(24,entry.Expected.Revision);put(28,1);Buffer.BlockCopy(entry.Expected.Settings.Encode(),0,body,64,32);}var bytes=body.Concat(Hash(body)).ToArray();string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){file.Write(bytes,0,bytes.Length);file.Flush(true);}if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);pending=entry;}finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch{}}
 }
 public void Dispose(){if(disposed)return;disposed=true;lease.Dispose();}
}
}
