using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;

// Standalone offline codec. No native libraries, USB, network or audio APIs.
static class CFContainerCodec {
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
 public static byte[] ReplaceImages(Container original,Dictionary<uint,byte[]> images){
  var chunks=new List<Chunk>();var pieces=new List<byte[]>();uint offset=0;
  foreach(var chunk in original.Chunks){var copy=new Chunk{Tag=chunk.Tag,Data=(byte[])chunk.Data.Clone()};if(copy.Tag=="dinf"){uint kind=BE(copy.Data,0);byte[] value;if(!images.TryGetValue(kind,out value)||value==null)throw new InvalidDataException("Falta una imagen CTF");BE(copy.Data,8,offset);BE(copy.Data,12,(uint)value.Length);offset=checked(offset+(uint)value.Length);pieces.Add(value);}chunks.Add(copy);}
  if(images.Count!=pieces.Count||offset>16000000)throw new InvalidDataException("Inventario de imágenes incorrecto");var raw=new byte[offset];int at=0;foreach(var piece in pieces){Buffer.BlockCopy(piece,0,raw,at,piece.Length);at+=piece.Length;}return Repack(new Container{Chunks=chunks,Raw=raw},raw);
 }
}
