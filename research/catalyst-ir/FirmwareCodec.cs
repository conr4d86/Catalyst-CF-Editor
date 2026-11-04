using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;

// Standalone offline codec. No native libraries, USB, network or audio APIs.
static class FirmwareCodec {
 internal class Chunk { public string Tag;public byte[] Data; }
 internal class Container { public List<Chunk> Chunks;public byte[] Raw; }
 public static string Hash(byte[] b){using(var h=SHA256.Create())return Hex(h.ComputeHash(b));}
 static string Hex(byte[] b){return BitConverter.ToString(b).Replace("-","").ToLowerInvariant();}
 static byte[] Slice(byte[] b,int p,int n){if(p<0||n<0||p>b.Length-n)throw new InvalidDataException("Datos truncados");var v=new byte[n];Buffer.BlockCopy(b,p,v,0,n);return v;}
 static uint BE(byte[] b,int p){return ((uint)b[p]<<24)|((uint)b[p+1]<<16)|((uint)b[p+2]<<8)|b[p+3];}
 static void BE(byte[] b,int p,uint v){b[p]=(byte)(v>>24);b[p+1]=(byte)(v>>16);b[p+2]=(byte)(v>>8);b[p+3]=(byte)v;}
 static string Text(byte[] b,int p,int n){return Encoding.ASCII.GetString(b,p,n);}
 static uint Adler(byte[] b){uint a=1,c=0;foreach(byte x in b){a=(a+x)%65521;c=(c+a)%65521;}return(c<<16)|a;}
 public static byte[] Inflate(byte[] z){
  if(z.Length<6 || ((z[0]<<8)+z[1])%31!=0 || (z[0]&15)!=8 || (z[1]&32)!=0)throw new InvalidDataException("Cabecera zlib incorrecta");
  using(var raw=new MemoryStream())using(var input=new MemoryStream(z,2,z.Length-6))using(var ds=new DeflateStream(input,CompressionMode.Decompress)){
   byte[] buffer=new byte[8192];int n;while((n=ds.Read(buffer,0,buffer.Length))>0){if(raw.Length+n>16000000)throw new InvalidDataException("Imagen demasiado grande");raw.Write(buffer,0,n);}
   byte[] b=raw.ToArray();if(Adler(b)!=BE(z,z.Length-4))throw new InvalidDataException("Adler32 incorrecto");return b;
  }
 }
 static byte[] Deflate(byte[] raw){using(var m=new MemoryStream()){
  m.WriteByte(0x78);m.WriteByte(0x9c);using(var ds=new DeflateStream(m,CompressionLevel.Optimal,true))ds.Write(raw,0,raw.Length);
  var a=new byte[4];BE(a,0,Adler(raw));m.Write(a,0,4);return m.ToArray();}}
 public static Container Parse(byte[] b){
  if(b.Length<12||Text(b,0,4)!="FORM"||Text(b,8,4)!="L6FF"||BE(b,4)!=b.Length-8)throw new InvalidDataException("Contenedor CTF incorrecto");
  var chunks=new List<Chunk>();int p=12;
  while(p<b.Length){if(p>b.Length-8)throw new InvalidDataException("Chunk truncado");int n=checked((int)BE(b,p+4));chunks.Add(new Chunk{Tag=Text(b,p,4),Data=Slice(b,p+8,n)});p=checked(p+8+n);}
  if(chunks.Count(c=>c.Tag=="HEAD")!=1||chunks.Count(c=>c.Tag=="data")!=1)throw new InvalidDataException("Estructura CTF no admitida");
  byte[] h=chunks.Single(c=>c.Tag=="HEAD").Data;byte[] raw=Inflate(chunks.Single(c=>c.Tag=="data").Data);
  if(h.Length!=52||BE(h,16)!=raw.Length)throw new InvalidDataException("Longitud incorrecta");
  using(var md5=MD5.Create())if(!md5.ComputeHash(raw).SequenceEqual(Slice(h,20,16)))throw new InvalidDataException("MD5 incorrecto");
  int offset=0;var kinds=new HashSet<uint>();
  foreach(var c in chunks.Where(c=>c.Tag=="dinf")){
   if(c.Data.Length!=16||!kinds.Add(BE(c.Data,0))||BE(c.Data,8)!=offset)throw new InvalidDataException("Regiones CTF incorrectas");
   offset=checked(offset+(int)BE(c.Data,12));if(offset>raw.Length)throw new InvalidDataException("Región fuera de imagen");
  }
  if(offset!=raw.Length||!kinds.SetEquals(new uint[]{0,5}))throw new InvalidDataException("Imágenes CTF incorrectas");
  return new Container{Chunks=chunks,Raw=raw};
 }
 public static byte[] Repack(Container c,byte[] raw){
  using(var body=new MemoryStream())using(var writer=new BinaryWriter(body)){
   writer.Write(Encoding.ASCII.GetBytes("L6FF"));
   foreach(var ch in c.Chunks){byte[] d=ch.Data;
    if(ch.Tag=="HEAD"){d=(byte[])d.Clone();BE(d,16,(uint)raw.Length);using(var md5=MD5.Create())Buffer.BlockCopy(md5.ComputeHash(raw),0,d,20,16);}
    if(ch.Tag=="data")d=Deflate(raw);
    writer.Write(Encoding.ASCII.GetBytes(ch.Tag));byte[] size=new byte[4];BE(size,0,(uint)d.Length);writer.Write(size);writer.Write(d);
   }
   byte[] value=body.ToArray(),output=new byte[value.Length+8];Buffer.BlockCopy(Encoding.ASCII.GetBytes("FORM"),0,output,0,4);BE(output,4,(uint)value.Length);Buffer.BlockCopy(value,0,output,8,value.Length);
   if(!Parse(output).Raw.SequenceEqual(raw))throw new InvalidDataException("Falló la validación tras empaquetar");return output;
  }
 }
 public static byte[] ReadIR(string path){
  byte[] b=File.ReadAllBytes(path);if(b.Length<12||Text(b,0,4)!="RIFF"||Text(b,8,4)!="WAVE"||BitConverter.ToUInt32(b,4)+8!=b.Length)throw new InvalidDataException("WAV RIFF incorrecto");
  byte[] fmt=null,data=null;int p=12;
  while(p<=b.Length-8){int n=checked((int)BitConverter.ToUInt32(b,p+4));byte[] v=Slice(b,p+8,n);string tag=Text(b,p,4);if(tag=="fmt ")fmt=v;if(tag=="data"){if(data!=null)throw new InvalidDataException("Bloque WAV duplicado");data=v;}p=checked(p+8+n+(n&1));}
  if(fmt==null||fmt.Length<16||data==null||BitConverter.ToUInt16(fmt,2)!=1||BitConverter.ToUInt32(fmt,4)!=48000)throw new InvalidDataException("Se admite WAV mono a 48 kHz, PCM 16/24/32 bits o float32, hasta 2048 muestras.");
  int kind=BitConverter.ToUInt16(fmt,0),bits=BitConverter.ToUInt16(fmt,14),align=BitConverter.ToUInt16(fmt,12);
  if(kind==0xfffe){
   if(fmt.Length<40)throw new InvalidDataException("WAVEFORMATEXTENSIBLE truncado");byte[] guid=new byte[16];Buffer.BlockCopy(fmt,24,guid,0,16);Guid subtype=new Guid(guid);
   if(subtype==new Guid("00000001-0000-0010-8000-00AA00389B71"))kind=1;else if(subtype==new Guid("00000003-0000-0010-8000-00AA00389B71"))kind=3;
  }
  if(!((kind==3&&bits==32)||(kind==1&&(bits==16||bits==24||bits==32)))||align!=bits/8||data.Length%align!=0||data.Length/align>2048||data.Length==0)throw new InvalidDataException("Formato no admitido: WAV mono 48 kHz, PCM 16/24/32 o float32, de 1 a 2048 muestras.");
  var reversed=new byte[8192];
  for(int i=0;i<data.Length/align;i++){
   int offset=i*align;float f;
   if(kind==3)f=BitConverter.ToSingle(data,offset);
   else if(bits==16)f=BitConverter.ToInt16(data,offset)/32768f;
   else if(bits==24){int value=data[offset]|(data[offset+1]<<8)|(data[offset+2]<<16);if((value&0x800000)!=0)value|=unchecked((int)0xff000000);f=value/8388608f;}
   else f=(float)(BitConverter.ToInt32(data,offset)/2147483648.0);
   if(float.IsNaN(f)||float.IsInfinity(f))throw new InvalidDataException("IR con muestras no finitas");Buffer.BlockCopy(BitConverter.GetBytes(f),0,reversed,(2047-i)*4,4);
  }return reversed;
 }
 static byte[] HexBytes(string s){if(s.Length%2!=0)throw new InvalidDataException("Registro HEX incorrecto");return Enumerable.Range(0,s.Length/2).Select(i=>Convert.ToByte(s.Substring(i*2,2),16)).ToArray();}
 public static byte[] WithIR(byte[] template,byte[] coefficients){
#if MULTI_IR
  return MultiIRCodec.WithIRs(template,new[]{coefficients,null,null});
#else
  if(coefficients.Length!=8192)throw new InvalidDataException("IR incorrecto");
  byte[] frequencyBank=FFTBank.Build(coefficients);
  Container c=Parse(template);byte[] raw=(byte[])c.Raw.Clone();var image=c.Chunks.Single(x=>x.Tag=="dinf"&&BE(x.Data,0)==0);
  int off=(int)BE(image.Data,8),size=(int)BE(image.Data,12);var hex=Encoding.ASCII.GetString(raw,off,size);var output=new StringBuilder(size);
  uint baseAddress=0;int copied=0,bankCopied=0;bool eof=false,startAddressSeen=false;uint lastEnd=0;
  foreach(string line in hex.Split(new[]{"\r\n"},StringSplitOptions.RemoveEmptyEntries)){
   if(eof||!line.StartsWith(":"))throw new InvalidDataException("Secuencia HEX incorrecta");byte[] r=HexBytes(line.Substring(1));
   if(r.Length<5||r.Length!=r[0]+5||(r.Sum(x=>(int)x)&255)!=0)throw new InvalidDataException("Checksum HEX incorrecto");
   int kind=r[3],address=(r[1]<<8)|r[2];bool changed=false;
   if(startAddressSeen&&kind!=1)throw new InvalidDataException("Line 6 termina la lectura en el registro de inicio: hay datos posteriores que no se programarían");
   if(kind==5)startAddressSeen=true;
   if(kind==4){if(r[0]!=2||address!=0)throw new InvalidDataException("Base HEX incorrecta");baseAddress=(uint)((r[4]<<8)|r[5])<<16;}
   else if(kind==0){uint start=baseAddress+(uint)address;if(start<lastEnd)throw new InvalidDataException("Registros HEX solapados");lastEnd=start+r[0];
    for(int i=0;i<r[0];i++){uint a=start+(uint)i;if(a>=0x601ad000&&a<0x601af000){r[4+i]=coefficients[a-0x601ad000];changed=true;copied++;}}
    for(int i=0;i<r[0];i++){uint a=start+(uint)i;if(a>=0x601af000&&a<0x601b3200){r[4+i]=frequencyBank[a-0x601af000];changed=true;bankCopied++;}}
   }else if(kind==1){if(r[0]!=0||address!=0)throw new InvalidDataException("EOF incorrecto");eof=true;}
   else if(kind!=5)throw new InvalidDataException("Tipo HEX no admitido");
   if(changed){r[r.Length-1]=0;r[r.Length-1]=(byte)(-r.Sum(x=>(int)x)&255);output.Append(":"+Hex(r).ToUpperInvariant()+"\r\n");}else output.Append(line+"\r\n");
  }
  if(!eof||copied!=8192||bankCopied!=0x4200||output.Length!=size)throw new InvalidDataException("No se encontró el área IR/FFT exacta");
  byte[] replacement=Encoding.ASCII.GetBytes(output.ToString());Buffer.BlockCopy(replacement,0,raw,off,size);
  return Repack(c,raw);
#endif
 }
 public static int VerifyBackup(string path){
  var js=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=2000000};
  var d=js.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
  byte[] data=Inflate(Convert.FromBase64String((string)d["encoded_data"]));var compression=(Dictionary<string,object>)d["compression"];
  uint crc=0xffffffff;foreach(byte b in data){crc^=b;for(int i=0;i<8;i++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}crc^=0xffffffff;
  if(crc!=Convert.ToUInt32(compression["crc32"])||data.Length!=Convert.ToInt32(compression["decompressed_size"]))throw new InvalidDataException("Copia con CRC o tamaño incorrectos");
  var decoded=js.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(data));var presets=(System.Collections.ArrayList)decoded["presets"];
  if(presets.Count!=12)throw new InvalidDataException("Se esperaban 12 presets");
  var meta=(Dictionary<string,object>)d["meta"];
  if(Convert.ToInt32(meta["device"])!=2424833||Convert.ToInt32(meta["device_version"])!=0x02020000)throw new InvalidDataException("Copia de otro producto o firmware");
  return presets.Count;
 }
 public static string Canonical(object value){
  var dict=value as Dictionary<string,object>;var js=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=2000000};
  if(dict!=null)return "{"+string.Join(",",dict.Keys.OrderBy(k=>k,StringComparer.Ordinal).Select(k=>js.Serialize(k)+":"+Canonical(dict[k])).ToArray())+"}";
  var list=value as System.Collections.IList;if(list!=null)return "["+string.Join(",",list.Cast<object>().Select(Canonical).ToArray())+"]";
  return js.Serialize(value);
 }
 static uint CRC(byte[] data){uint crc=0xffffffff;foreach(byte b in data){crc^=b;for(int i=0;i<8;i++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}return crc^0xffffffff;}
 public static void CreateVerifiedBackup(string folder,string output){
  if(File.Exists(output)||File.Exists(output+".verification.json"))throw new IOException("No se sobrescriben copias existentes");
  if(!File.Exists(Path.Combine(folder,"READS-COMPLETE.txt")))throw new InvalidDataException("Lectura de los 12 presets incompleta");
  var js=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=2000000};var presets=new List<object>();var reads=new List<object>();
  for(int i=1;i<=12;i++){
   string a=Path.Combine(folder,string.Format("preset-{0:00}-read-1.catl6p",i)),b=Path.Combine(folder,string.Format("preset-{0:00}-read-2.catl6p",i));
   var first=js.Deserialize<Dictionary<string,object>>(File.ReadAllText(a));var second=js.Deserialize<Dictionary<string,object>>(File.ReadAllText(b));
   var data=(Dictionary<string,object>)first["data"];
   if((string)first["schema"]!="L6Preset"||(string)second["schema"]!="L6Preset"||Canonical(data)!=Canonical(second["data"]))throw new InvalidDataException("Lecturas distintas en preset "+i);
   if(Convert.ToInt32(data["device"])!=2424833||Convert.ToInt32(data["device_version"])!=0x02020000)throw new InvalidDataException("Preset de otra unidad o firmware");
   presets.Add(data);reads.Add(new{index=i,first=Path.GetFileName(a),second=Path.GetFileName(b),first_sha256=Hash(File.ReadAllBytes(a)),second_sha256=Hash(File.ReadAllBytes(b)),data_sha256=Hash(Encoding.UTF8.GetBytes(Canonical(data)))});
  }
  byte[] raw=Encoding.UTF8.GetBytes(js.Serialize(new{meta=new{name="Catalyst verified backup"},presets=presets}));
  var archive=new{compression=new{crc32=CRC(raw),decompressed_size=raw.Length,type="zlib"},encoded_data=Convert.ToBase64String(Deflate(raw)),encoding="Base64",meta=new{application="CatalystIR",appversion=1,device=2424833,device_version=0x02020000,name="Catalyst verified backup"},schema="L6Setlist",version=2};
  File.WriteAllText(output,js.Serialize(archive),new UTF8Encoding(false));VerifyBackup(output);
  File.WriteAllText(output+".verification.json",js.Serialize(new{schema="CatalystIR.RepeatedPresetReads.v1",archive_sha256=Hash(File.ReadAllBytes(output)),reads=reads}),new UTF8Encoding(false));
  VerifyPreservedBackup(output);
 }
 public static int VerifyPreservedBackup(string path){
  int count=VerifyBackup(path);var js=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=2000000};
  var report=js.Deserialize<Dictionary<string,object>>(File.ReadAllText(path+".verification.json"));
  if((string)report["schema"]!="CatalystIR.RepeatedPresetReads.v1"||(string)report["archive_sha256"]!=Hash(File.ReadAllBytes(path)))throw new InvalidDataException("La copia no coincide con su verificación");
  var archive=js.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));var decoded=js.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(Inflate(Convert.FromBase64String((string)archive["encoded_data"]))));
  var presets=(System.Collections.IList)decoded["presets"];var reads=(System.Collections.IList)report["reads"];
  if(reads.Count!=12)throw new InvalidDataException("Faltan lecturas repetidas");string folder=Path.GetDirectoryName(Path.GetFullPath(path));
  for(int i=0;i<12;i++){
   var r=(Dictionary<string,object>)reads[i];if(Convert.ToInt32(r["index"])!=i+1)throw new InvalidDataException("Índices de copia incorrectos");
   foreach(string key in new[]{"first","second"}){
    string name=(string)r[key];if(name!=Path.GetFileName(name))throw new InvalidDataException("Ruta de lectura inválida");
    byte[] file=File.ReadAllBytes(Path.Combine(folder,name));if(Hash(file)!=(string)r[key+"_sha256"])throw new InvalidDataException("Lectura de respaldo alterada");
    var d=js.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(file));string canonical=Canonical(d["data"]);
    if(canonical!=Canonical(presets[i])||Hash(Encoding.UTF8.GetBytes(canonical))!=(string)r["data_sha256"])throw new InvalidDataException("Contenido de copia distinto de sus lecturas");
   }
  }return count;
 }
 public static void ValidateCandidate(byte[] template,byte[] candidate){
#if MULTI_IR
  MultiIRCodec.ValidateCandidate(template,candidate);return;
#else
  Container c=Parse(candidate);var image=c.Chunks.Single(x=>x.Tag=="dinf"&&BE(x.Data,0)==0);int off=(int)BE(image.Data,8),size=(int)BE(image.Data,12);
  byte[] ir=new byte[8192];int count=0;uint baseAddress=0;
  foreach(string line in Encoding.ASCII.GetString(c.Raw,off,size).Split(new[]{"\r\n"},StringSplitOptions.RemoveEmptyEntries)){
   byte[] r=HexBytes(line.Substring(1));if(r.Length<5||r.Length!=r[0]+5)throw new InvalidDataException("HEX incorrecto");
   if(r[3]==4)baseAddress=(uint)((r[4]<<8)|r[5])<<16;
   if(r[3]==0)for(int i=0;i<r[0];i++){uint a=baseAddress+(uint)((r[1]<<8)|r[2])+(uint)i;if(a>=0x601ad000&&a<0x601af000){ir[a-0x601ad000]=r[4+i];count++;}}
  }
  if(count!=8192)throw new InvalidDataException("Área IR incorrecta");
  for(int i=0;i<2048;i++){float x=BitConverter.ToSingle(ir,i*4);if(float.IsNaN(x)||float.IsInfinity(x))throw new InvalidDataException("IR no finito");}
  if(!Parse(WithIR(template,ir)).Raw.SequenceEqual(c.Raw))throw new InvalidDataException("El candidato modifica datos fuera del IR o no corresponde a la extensión verificada");
#endif
 }
 public static void TestGuards(byte[] template,byte[] candidate){
#if MULTI_IR
  MultiIRCodec.TestGuards(template,candidate);return;
#else
  ValidateCandidate(template,candidate);int rejected=0;
  byte[] broken=(byte[])candidate.Clone();broken[broken.Length/2]^=1;
  try{Parse(broken);}catch(Exception){rejected++;}
  Container c=Parse(candidate);byte[] raw=(byte[])c.Raw.Clone();raw[0]^=1;
  try{ValidateCandidate(template,Repack(c,raw));}catch(InvalidDataException){rejected++;}
  byte[] nan=new byte[8192];Buffer.BlockCopy(BitConverter.GetBytes(float.NaN),0,nan,0,4);
  try{ValidateCandidate(template,WithIR(template,nan));}catch(InvalidDataException){rejected++;}
  if(rejected!=3)throw new InvalidDataException("Falló una barrera de validación");
#endif
 }
}
