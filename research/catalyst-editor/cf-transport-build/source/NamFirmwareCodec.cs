using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.Generic;

// A fixed, hash-pinned engine is reused; only model data/derived reset and IRs vary.
static class NamFirmwareCodec {
 public const string AppTemplateHash="CatalystIR-NAM-v3:"+NamLayout.TemplateHash;
 static byte[] cached;
 public static byte[] Template(){
  if(cached==null)using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("nam-engine.ctf"))using(var output=new MemoryStream()){
   if(stream==null)throw new InvalidDataException("Falta el motor NAM integrado");stream.CopyTo(output);var b=output.ToArray();if(FirmwareCodec.Hash(b)!=NamLayout.TemplateHash)throw new InvalidDataException("Motor NAM alterado");FirmwareCodec.Parse(b);cached=b;
  }return (byte[])cached.Clone();
 }
 static uint BE(byte[] b,int p){return ((uint)b[p]<<24)|((uint)b[p+1]<<16)|((uint)b[p+2]<<8)|b[p+3];}
 public static bool HasNAM(byte[] candidate){return FirmwareCodec.Parse(candidate).Raw.Length==FirmwareCodec.Parse(Template()).Raw.Length;}
 public static byte[] ReadRange(byte[] ctf,uint address,int count){var wanted=new Dictionary<uint,byte>();for(int i=0;i<count;i++)wanted.Add(address+(uint)i,0);Walk(FirmwareCodec.Parse(ctf),null,wanted);return Enumerable.Range(0,count).Select(i=>wanted[address+(uint)i]).ToArray();}
 static byte[] Patch(byte[] ctf,Dictionary<uint,byte> values){var c=FirmwareCodec.Parse(ctf);byte[] raw=Walk(c,values,null);return FirmwareCodec.Repack(c,raw);}
 static byte[] Walk(FirmwareCodec.Container c,Dictionary<uint,byte> values,Dictionary<uint,byte> read){
  var image=c.Chunks.Single(x=>x.Tag=="dinf"&&BE(x.Data,0)==0);int off=checked((int)BE(image.Data,8)),size=checked((int)BE(image.Data,12));
  var output=new StringBuilder(size);uint high=0,lastEnd=0;bool eof=false,start=false;int copied=0,reads=0;
  foreach(string line in Encoding.ASCII.GetString(c.Raw,off,size).Split(new[]{"\r\n"},StringSplitOptions.RemoveEmptyEntries)){
   if(eof||!line.StartsWith(":"))throw new InvalidDataException("Secuencia HEX incorrecta");
   string hex=line.Substring(1);if(hex.Length%2!=0)throw new InvalidDataException("HEX truncado");var r=Enumerable.Range(0,hex.Length/2).Select(i=>Convert.ToByte(hex.Substring(i*2,2),16)).ToArray();
   if(r.Length<5||r.Length!=r[0]+5||(r.Sum(x=>(int)x)&255)!=0)throw new InvalidDataException("Checksum HEX incorrecto");int kind=r[3],a=(r[1]<<8)|r[2];bool changed=false;
   if(start&&kind!=1)throw new InvalidDataException("Datos tras registro de inicio");
   if(kind==4){if(r[0]!=2||a!=0)throw new InvalidDataException("Base HEX incorrecta");high=(uint)((r[4]<<8)|r[5])<<16;}
   else if(kind==0){uint first=high+(uint)a;if(first<lastEnd)throw new InvalidDataException("HEX solapado");lastEnd=checked(first+r[0]);for(int i=0;i<r[0];i++){uint at=first+(uint)i;byte b;
    if(read!=null&&read.ContainsKey(at)){read[at]=r[4+i];reads++;}if(values!=null&&values.TryGetValue(at,out b)){r[4+i]=b;changed=true;copied++;}
   }}else if(kind==5){if(start||r[0]!=4||a!=0)throw new InvalidDataException("Inicio HEX incorrecto");start=true;}
   else if(kind==1){if(r[0]!=0||a!=0)throw new InvalidDataException("EOF incorrecto");eof=true;}else throw new InvalidDataException("Tipo HEX incorrecto");
   if(changed){r[r.Length-1]=0;r[r.Length-1]=(byte)(-r.Sum(x=>(int)x)&255);output.Append(":"+BitConverter.ToString(r).Replace("-","")+"\r\n");}else output.Append(line+"\r\n");
  }
  if(!eof||!start||output.Length!=size||values!=null&&copied!=values.Count||read!=null&&reads!=read.Count)throw new InvalidDataException("Áreas NAM incompletas");
  var raw=(byte[])c.Raw.Clone();Buffer.BlockCopy(Encoding.ASCII.GetBytes(output.ToString()),0,raw,off,size);return raw;
 }
 static void Add(Dictionary<uint,byte> values,uint address,byte[] bytes){for(int i=0;i<bytes.Length;i++)values.Add(address+(uint)i,bytes[i]);}
 static byte[] BuildPacked(byte[] packed,byte[][] irs){
  var weights=NamModel.Floats(packed);if(weights.Length!=NamModel.WeightCount)throw new InvalidDataException("Tamaño del NAM incorrecto");byte[] state,silence;NamModel.Reset(weights,out state,out silence);
  var values=new Dictionary<uint,byte>();Add(values,NamLayout.State,state);Add(values,NamLayout.Weights,packed);Add(values,NamLayout.Silence,silence);
  byte[] c=Patch(Template(),values);return MultiIRCodec.WithIRs(c,irs,true);
 }
 public static byte[] Build(NamModel model,double outputDb,byte[][] irs){return BuildPacked(model.Packed(outputDb),irs);}
 public static byte[] Build(NamModel model,double inputDb,double outputDb,byte[][] irs){return BuildPacked(model.Packed(inputDb,outputDb),irs);}
 static void Compare(byte[] expected,byte[] candidate){var a=FirmwareCodec.Parse(expected);var b=FirmwareCodec.Parse(candidate);if(!a.Raw.SequenceEqual(b.Raw)||a.Chunks.Count!=b.Chunks.Count)throw new InvalidDataException("Cambios fuera del modelo NAM, sus datos derivados o los IR");
  for(int i=0;i<a.Chunks.Count;i++)if(a.Chunks[i].Tag!=b.Chunks[i].Tag||a.Chunks[i].Tag!="data"&&!a.Chunks[i].Data.SequenceEqual(b.Chunks[i].Data))throw new InvalidDataException("Metadatos CTF alterados");
 }
 public static void ValidateAny(byte[] irTemplate,byte[] candidate){
  if(!HasNAM(candidate)){MultiIRCodec.ValidateCandidate(irTemplate,candidate);return;}
  var packed=ReadRange(candidate,NamLayout.Weights,7484);var irs=MultiIRCodec.ReadIRs(candidate,true);Compare(BuildPacked(packed,irs),candidate);
 }
 public static uint Fingerprint(byte[] packed){uint v=2166136261;foreach(byte b in packed)v=unchecked((v^b)*16777619);return v;}
 public static void TestGuards(byte[] irTemplate,byte[] candidate){
  if(!HasNAM(candidate)){MultiIRCodec.TestGuards(irTemplate,candidate);return;}ValidateAny(irTemplate,candidate);
  int rejected=0;Action<Action> reject=a=>{try{a();}catch{rejected++;return;}throw new InvalidDataException("No se rechazó una alteración del firmware");};
  var broken=(byte[])candidate.Clone();broken[broken.Length/2]^=1;reject(()=>ValidateAny(irTemplate,broken));
  reject(()=>ValidateAny(irTemplate,Patch(candidate,new Dictionary<uint,byte>{{0x601c3000,0}}))); // first executable byte in NAM extension
  reject(()=>ValidateAny(irTemplate,Patch(candidate,new Dictionary<uint,byte>{{NamLayout.State+17,1}})));
  var nan=new Dictionary<uint,byte>();Add(nan,NamLayout.Weights,BitConverter.GetBytes(float.NaN));reject(()=>ValidateAny(irTemplate,Patch(candidate,nan)));
  if(rejected!=4)throw new InvalidDataException("Fallaron las barreras NAM");
 }
}
