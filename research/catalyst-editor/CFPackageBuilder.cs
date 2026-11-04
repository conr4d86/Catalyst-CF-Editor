using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using System.Security.Cryptography;
namespace CFEditor {
// Offline initial image builder. A valid container is NOT hardware approval.
// The updater may accept a result only after the separately reviewed bootstrap
// and memory/IRQ/cache checks permit this exact code/template fingerprint.
sealed class CFPackageBuilder {
 internal const string BaseHash="e80bba31906c0032b4b92f9d062c54879ea88fbabbf0a9d073dd1794d2a287a1";
 internal const string SegmentHash="4a81169b0cee9b8ed384936fad14161b8fd42169c5a5dcc317337d9dec15bf34";
 const uint App=0x60001000,Seed=0x601e3000,Limit=0x60200000;const int Manifest=1048;
 readonly CFContainerCodec.Container template;readonly byte[] neutral,header,other;readonly uint entry;
 sealed class Image {public uint Address;public byte[] Data;}
 static uint BE(byte[] b,int p){return (uint)b[p]<<24|(uint)b[p+1]<<16|(uint)b[p+2]<<8|b[p+3];}static uint U(byte[] b,int p){return BitConverter.ToUInt32(b,p);}
 static void Put(byte[] b,int p,uint v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,p,4);}
 static void Edition(byte[] edition){if(edition==null||edition.Length!=32||edition.All(b=>b==0)||edition.All(b=>b==255))throw new InvalidDataException("Edición de firmware no válida.");}
 static List<Image> Decode(byte[] hex,out uint entry){var images=new List<Image>();uint high=0,last=0;bool ended=false,started=false;entry=0;MemoryStream current=null;
  foreach(string line in Encoding.ASCII.GetString(hex).Split(new[]{"\r\n"},StringSplitOptions.RemoveEmptyEntries)){
   if(ended||line.Length<11||line[0]!=':'||(line.Length&1)==0)throw new InvalidDataException("Secuencia HEX incorrecta.");var b=new byte[(line.Length-1)/2];for(int i=0;i<b.Length;i++)b[i]=Convert.ToByte(line.Substring(1+i*2,2),16);
   if(b.Length!=b[0]+5||(b.Sum(x=>(int)x)&255)!=0)throw new InvalidDataException("Checksum HEX incorrecto.");int address=b[1]*256+b[2],kind=b[3];
   if(started&&kind!=1)throw new InvalidDataException("Datos después del inicio HEX.");
   if(kind==0){if(b[0]==0||address+b[0]>65536)throw new InvalidDataException("Registro HEX fuera de página.");uint a=checked(high+(uint)address);if(a<last)throw new InvalidDataException("Registros HEX solapados.");if(current==null||a!=last){if(current!=null){images.Last().Data=current.ToArray();current.Dispose();}current=new MemoryStream();images.Add(new Image{Address=a});if(images.Count>8)throw new InvalidDataException("Demasiadas áreas HEX.");}current.Write(b,4,b[0]);if(current.Length>0x200000)throw new InvalidDataException("Imagen HEX demasiado grande.");last=checked(a+b[0]);}
   else if(kind==4){if(b[0]!=2||address!=0)throw new InvalidDataException("Base HEX incorrecta.");high=(uint)(b[4]*256+b[5])<<16;}
   else if(kind==5){if(started||b[0]!=4||address!=0)throw new InvalidDataException("Inicio HEX incorrecto.");entry=BE(b,4);started=true;}
   else if(kind==1){if(b[0]!=0||address!=0)throw new InvalidDataException("EOF HEX incorrecto.");ended=true;}else throw new InvalidDataException("Registro HEX no admitido.");
  }if(current!=null){images.Last().Data=current.ToArray();current.Dispose();}if(!ended||!started)throw new InvalidDataException("Imagen HEX incompleta.");return images;
 }
 static void Record(StringBuilder text,int type,int address,byte[] data){int sum=data.Length+(address>>8)+(address&255)+type;text.Append(':').Append(data.Length.ToString("X2")).Append(address.ToString("X4")).Append(type.ToString("X2"));foreach(byte b in data){text.Append(b.ToString("X2"));sum+=b;}text.Append(((byte)(-sum)).ToString("X2")).Append("\r\n");}
 static byte[] Encode(IEnumerable<Image> images,uint entry){var text=new StringBuilder();uint high=uint.MaxValue;foreach(var image in images)for(int offset=0;offset<image.Data.Length;){uint a=checked(image.Address+(uint)offset);if((a>>16)!=high){high=a>>16;Record(text,4,0,new[]{(byte)(high>>8),(byte)high});}int n=Math.Min(16,Math.Min(image.Data.Length-offset,65536-(int)(a&65535)));var b=new byte[n];Buffer.BlockCopy(image.Data,offset,b,0,n);Record(text,0,(int)(a&65535),b);offset+=n;}Record(text,5,0,new[]{(byte)(entry>>24),(byte)(entry>>16),(byte)(entry>>8),(byte)entry});Record(text,1,0,new byte[0]);return Encoding.ASCII.GetBytes(text.ToString());}
 public CFPackageBuilder(byte[] baseCTF,byte[] neutralSegment){if(baseCTF==null||neutralSegment==null||FirmwareCodec.Hash(baseCTF)!=BaseHash||FirmwareCodec.Hash(neutralSegment)!=SegmentHash)throw new InvalidDataException("La base de firmware no coincide con la versión revisada.");
  template=CFContainerCodec.Parse(baseCTF);neutral=(byte[])neutralSegment.Clone();var region=template.Chunks.Single(c=>c.Tag=="dinf"&&BE(c.Data,0)==0);var images=Decode(template.Raw.Skip((int)BE(region.Data,8)).Take((int)BE(region.Data,12)).ToArray(),out entry);
  if(images.Count!=2||images[0].Address!=0x60000000||images[0].Data.Length!=512||images[1].Address!=App||entry!=0x600388f9||neutral.Length<Seed-App+Manifest||neutral.Length>Limit-App)throw new InvalidDataException("Distribución de firmware no compatible.");header=images[0].Data;
  region=template.Chunks.Single(c=>c.Tag=="dinf"&&BE(c.Data,0)==5);other=template.Raw.Skip((int)BE(region.Data,8)).Take((int)BE(region.Data,12)).ToArray();
 }
 internal static void ValidateAsset(AssetPreparation resource,int slot){if(resource==null)return;if(resource.Slot!=slot||resource.Info==null||resource.Data==null||resource.Data.Length!=(slot<3?25088:7772))throw new InvalidDataException("Ranura inicial no válida.");resource.Info.Encode();var floats=NamModel.Floats(resource.Data);
  if(slot<3){var taps=resource.Data.Take(8192).ToArray();if(!FFTBank.Build(taps).SequenceEqual(resource.Data.Skip(8192)))throw new InvalidDataException("El IR y su banco FFT no coinciden.");}
  else{byte[] state,silence;NamModel.Reset(floats.Take(1871).ToArray(),out state,out silence);if(!silence.SequenceEqual(resource.Data.Skip(7484)))throw new InvalidDataException("El estado inicial no corresponde a este NAM.");}
 }
 static byte[] SeedBytes(AssetPreparation[] resources,byte[] edition){if(resources==null||resources.Length!=5)throw new InvalidDataException("Se requieren cinco posiciones iniciales, que pueden estar vacías.");Edition(edition);var chunks=new List<byte[]>();int length=Manifest;uint mask=0;var manifest=new byte[Manifest];Put(manifest,0,0x31445343);Put(manifest,4,1);Put(manifest,12,5);Buffer.BlockCopy(edition,0,manifest,16,32);
  for(int i=0;i<5;i++){var r=resources[i];ValidateAsset(r,i);if(r==null)continue;mask|=1u<<i;int at=48+i*200;Put(manifest,at,checked(Seed+(uint)length));Put(manifest,at+4,(uint)r.Data.Length);Put(manifest,at+8,AssetWire.Crc(r.Data));Put(manifest,at+12,(uint)i+1);Buffer.BlockCopy(r.Info.Encode(),0,manifest,at+16,184);chunks.Add((byte[])r.Data.Clone());length+=r.Data.Length;}
  Put(manifest,8,mask);if(Seed+length>Limit)throw new InvalidDataException("Los recursos exceden la memoria reservada.");var result=new byte[length];Buffer.BlockCopy(manifest,0,result,0,Manifest);int offset=Manifest;foreach(var chunk in chunks){Buffer.BlockCopy(chunk,0,result,offset,chunk.Length);offset+=chunk.Length;}return result;
 }
 public CFPreparedPackage Build(AssetPreparation[] resources,byte[] edition=null){if(edition==null){edition=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(edition);}byte[] seeds=SeedBytes(resources,edition);int prefix=(int)(Seed-App);var segment=new byte[prefix+seeds.Length];Buffer.BlockCopy(neutral,0,segment,0,prefix);Buffer.BlockCopy(seeds,0,segment,prefix,seeds.Length);
  var hex=Encode(new[]{new Image{Address=0x60000000,Data=header},new Image{Address=App,Data=segment}},entry);var bytes=CFContainerCodec.ReplaceImages(template,new Dictionary<uint,byte[]>{{0,hex},{5,other}});Validate(bytes);return new CFPreparedPackage(bytes,edition,resources.Count(x=>x!=null));
 }
 // Private repair migration only: requires the exact previously reviewed CTF
 // hash. Copies its seed bytes and metadata; never reimports/rescales assets.
 internal static AssetPreparation[] ReadPinnedTrialSeeds(byte[] bytes,string expected){
  if(expected==null||expected.Length!=64||FirmwareCodec.Hash(bytes)!=expected)throw new InvalidDataException("La copia anterior no coincide con su huella.");
  var c=CFContainerCodec.Parse(bytes);var d=c.Chunks.Single(x=>x.Tag=="dinf"&&BE(x.Data,0)==0);uint address;var images=Decode(c.Raw.Skip((int)BE(d.Data,8)).Take((int)BE(d.Data,12)).ToArray(),out address);
  if(address!=0x600388f9||images.Count!=2||images[1].Address!=App||images[1].Data.Length<Seed-App+Manifest||images[1].Data.Length>Limit-App)throw new InvalidDataException("Distribución anterior no válida.");
  var seed=images[1].Data.Skip((int)(Seed-App)).ToArray();if(U(seed,0)!=0x31445343||U(seed,4)!=1||U(seed,8)>31||U(seed,12)!=5)throw new InvalidDataException("Inventario anterior no válido.");var edition=seed.Skip(16).Take(32).ToArray();Edition(edition);var resources=new AssetPreparation[5];int offset=Manifest;
  for(int i=0;i<5;i++){int p=48+i*200;if((U(seed,8)&(1u<<i))==0){if(seed.Skip(p).Take(200).Any(b=>b!=0))throw new InvalidDataException("Ranura vacía incorrecta.");continue;}int n=i<3?25088:7772;
   if(U(seed,p)!=Seed+offset||U(seed,p+4)!=n||U(seed,p+12)!=(uint)i+1||seed.Length-offset<n)throw new InvalidDataException("Recurso anterior fuera de rango.");var data=seed.Skip(offset).Take(n).ToArray();if(AssetWire.Crc(data)!=U(seed,p+8))throw new InvalidDataException("CRC anterior no válido.");resources[i]=new AssetPreparation{Slot=(byte)i,Data=data,Info=AssetInfo.Decode(seed.Skip(p+16).Take(184).ToArray())};ValidateAsset(resources[i],i);offset+=n;
  }if(offset!=seed.Length||!seed.SequenceEqual(SeedBytes(resources,edition)))throw new InvalidDataException("Inventario anterior incompleto.");return resources;
 }
 internal CFPreparedPackage ReadPrepared(byte[] bytes){Validate(bytes);var c=CFContainerCodec.Parse(bytes);var d=c.Chunks.Single(x=>x.Tag=="dinf"&&BE(x.Data,0)==0);uint address;var images=Decode(c.Raw.Skip((int)BE(d.Data,8)).Take((int)BE(d.Data,12)).ToArray(),out address);var seed=images[1].Data.Skip((int)(Seed-App)).ToArray();uint mask=U(seed,8);int count=0;for(int i=0;i<5;i++)if((mask&(1u<<i))!=0)count++;return new CFPreparedPackage(bytes,seed.Skip(16).Take(32).ToArray(),count);}
 public void Validate(byte[] candidate){var c=CFContainerCodec.Parse(candidate);if(c.Chunks.Count!=template.Chunks.Count)throw new InvalidDataException("Inventario CTF modificado.");for(int i=0;i<c.Chunks.Count;i++){var oldChunk=template.Chunks[i];var nextChunk=c.Chunks[i];if(oldChunk.Tag!=nextChunk.Tag||oldChunk.Tag=="dinf"&&!oldChunk.Data.Take(8).SequenceEqual(nextChunk.Data.Take(8)))throw new InvalidDataException("Orden o identidad de regiones CTF modificados.");}var d=c.Chunks.Single(x=>x.Tag=="dinf"&&BE(x.Data,0)==0);uint address;var images=Decode(c.Raw.Skip((int)BE(d.Data,8)).Take((int)BE(d.Data,12)).ToArray(),out address);int prefix=(int)(Seed-App);
  if(address!=entry||images.Count!=2||images[0].Address!=0x60000000||!images[0].Data.SequenceEqual(header)||images[1].Address!=App||images[1].Data.Length<prefix+Manifest||!images[1].Data.Take(prefix).SequenceEqual(neutral.Take(prefix)))throw new InvalidDataException("El firmware altera código o datos fuera de sus recursos iniciales.");
  var seed=images[1].Data.Skip(prefix).ToArray();if(U(seed,0)!=0x31445343||U(seed,4)!=1||U(seed,8)>31||U(seed,12)!=5)throw new InvalidDataException("Inventario inicial no válido.");var edition=seed.Skip(16).Take(32).ToArray();Edition(edition);var resources=new AssetPreparation[5];int offset=Manifest;
  for(int i=0;i<5;i++){int p=48+i*200;if((U(seed,8)&(1u<<i))==0){if(seed.Skip(p).Take(200).Any(b=>b!=0))throw new InvalidDataException("Ranura vacía con datos ajenos.");continue;}int n=i<3?25088:7772;
   if(U(seed,p)!=Seed+offset||U(seed,p+4)!=n||U(seed,p+12)!=(uint)i+1||seed.Length-offset<n)throw new InvalidDataException("Distribución inicial de recursos incorrecta.");var data=seed.Skip(offset).Take(n).ToArray();if(AssetWire.Crc(data)!=U(seed,p+8))throw new InvalidDataException("CRC de recurso inicial incorrecto.");resources[i]=new AssetPreparation{Slot=(byte)i,Data=data,Info=AssetInfo.Decode(seed.Skip(p+16).Take(184).ToArray())};ValidateAsset(resources[i],i);offset+=n;
  }if(offset!=seed.Length||Seed+seed.Length>Limit||!seed.SequenceEqual(SeedBytes(resources,edition)))throw new InvalidDataException("Datos iniciales adicionales o incompletos.");
  d=c.Chunks.Single(x=>x.Tag=="dinf"&&BE(x.Data,0)==5);if(!other.SequenceEqual(c.Raw.Skip((int)BE(d.Data,8)).Take((int)BE(d.Data,12))))throw new InvalidDataException("Se ha modificado una imagen nativa ajena.");
  var before=template.Chunks.Where(x=>x.Tag!="data"&&x.Tag!="dinf"&&x.Tag!="HEAD").ToArray();var after=c.Chunks.Where(x=>x.Tag!="data"&&x.Tag!="dinf"&&x.Tag!="HEAD").ToArray();if(before.Length!=after.Length||before.Where((x,i)=>x.Tag!=after[i].Tag||!x.Data.SequenceEqual(after[i].Data)).Any())throw new InvalidDataException("Metadatos CTF modificados.");
  var oldHead=template.Chunks.Single(x=>x.Tag=="HEAD").Data;var newHead=c.Chunks.Single(x=>x.Tag=="HEAD").Data;for(int i=0;i<52;i++)if((i<16||i>=36)&&oldHead[i]!=newHead[i])throw new InvalidDataException("Identidad CTF modificada.");
 }
}
}
