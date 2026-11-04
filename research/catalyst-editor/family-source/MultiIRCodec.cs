using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

// Exact v2 layout. Only three coefficient/derived FFT banks and a three-bit
// presence mask may differ from the hash-pinned executable template.
static class MultiIRCodec {
 public static readonly uint[] Coefficients={0x601ad000,0x601b4000,0x601bb000};
 public const uint MaskAddress=0x601b3500;
 static uint BE(byte[] b,int p){return ((uint)b[p]<<24)|((uint)b[p+1]<<16)|((uint)b[p+2]<<8)|b[p+3];}
 static byte[] Hex(string s){if(s.Length%2!=0)throw new InvalidDataException("HEX incorrecto");return Enumerable.Range(0,s.Length/2).Select(i=>Convert.ToByte(s.Substring(i*2,2),16)).ToArray();}
 static Dictionary<uint,byte> Mutable(byte[][] irs,bool allowEmpty){
  if(irs==null||irs.Length!=3)throw new InvalidDataException("Se esperan tres ranuras IR");
  var result=new Dictionary<uint,byte>();uint mask=0;
  for(int slot=0;slot<3;slot++){
   byte[] ir=irs[slot];if(ir!=null){
    if(ir.Length!=8192)throw new InvalidDataException("IR incorrecto en ranura "+(slot+1));
    for(int i=0;i<2048;i++){float x=BitConverter.ToSingle(ir,i*4);if(float.IsNaN(x)||float.IsInfinity(x))throw new InvalidDataException("IR no finito en ranura "+(slot+1));}
    if(!ir.Any(b=>b!=0))throw new InvalidDataException("IR silencioso en ranura "+(slot+1));
    mask|=1u<<slot;
   }
   byte[] coeff=ir??new byte[8192],bank=ir==null?new byte[0x4200]:FFTBank.Build(ir);
   for(int i=0;i<coeff.Length;i++)result.Add(Coefficients[slot]+(uint)i,coeff[i]);
   for(int i=0;i<bank.Length;i++)result.Add(Coefficients[slot]+0x2000+(uint)i,bank[i]);
  }
  if(mask==0&&!allowEmpty)throw new InvalidDataException("Carga al menos un IR");
  byte[] bits=BitConverter.GetBytes(mask);for(int i=0;i<4;i++)result.Add(MaskAddress+(uint)i,bits[i]);
  return result;
 }
 static string Transform(FirmwareCodec.Container container,Dictionary<uint,byte> values,Dictionary<uint,byte> read){
  var image=container.Chunks.Single(c=>c.Tag=="dinf"&&BE(c.Data,0)==0);
  int off=checked((int)BE(image.Data,8)),size=checked((int)BE(image.Data,12));
  var output=new StringBuilder(size);uint high=0,lastEnd=0;bool eof=false,start=false;int copied=0;
  foreach(string line in Encoding.ASCII.GetString(container.Raw,off,size).Split(new[]{"\r\n"},StringSplitOptions.RemoveEmptyEntries)){
   if(eof||!line.StartsWith(":"))throw new InvalidDataException("Secuencia HEX incorrecta");
   byte[] r=Hex(line.Substring(1));if(r.Length<5||r.Length!=r[0]+5||(r.Sum(x=>(int)x)&255)!=0)throw new InvalidDataException("Checksum HEX incorrecto");
   int kind=r[3],address=(r[1]<<8)|r[2];bool changed=false;
   if(start&&kind!=1)throw new InvalidDataException("Datos posteriores al registro de inicio");
   if(kind==4){if(r[0]!=2||address!=0)throw new InvalidDataException("Base HEX incorrecta");high=(uint)((r[4]<<8)|r[5])<<16;}
   else if(kind==0){
    uint first=high+(uint)address;if(first<lastEnd)throw new InvalidDataException("HEX solapado");lastEnd=checked(first+r[0]);
    for(int i=0;i<r[0];i++){
     uint a=first+(uint)i;byte replacement;
     if(read!=null&&IsMutable(a))read.Add(a,r[4+i]);
     if(values!=null&&values.TryGetValue(a,out replacement)){r[4+i]=replacement;copied++;changed=true;}
    }
   }else if(kind==5){if(r[0]!=4||address!=0||start)throw new InvalidDataException("Inicio HEX incorrecto");start=true;}
   else if(kind==1){if(r[0]!=0||address!=0)throw new InvalidDataException("EOF incorrecto");eof=true;}
   else throw new InvalidDataException("Tipo HEX no admitido");
   if(changed){r[r.Length-1]=0;r[r.Length-1]=(byte)(-r.Sum(x=>(int)x)&255);output.Append(":"+BitConverter.ToString(r).Replace("-","")+"\r\n");}else output.Append(line+"\r\n");
  }
  if(!eof||output.Length!=size||(values!=null&&copied!=values.Count))throw new InvalidDataException("Áreas IR/FFT incompletas");
  return output.ToString();
 }
 static bool IsMutable(uint a){return a>=MaskAddress&&a<MaskAddress+4||Coefficients.Any(p=>a>=p&&a<p+0x6200);}
 public static byte[] WithIRs(byte[] template,byte[][] irs,bool allowEmpty=false){
  var c=FirmwareCodec.Parse(template);string result=Transform(c,Mutable(irs,allowEmpty),null);
  var image=c.Chunks.Single(x=>x.Tag=="dinf"&&BE(x.Data,0)==0);byte[] raw=(byte[])c.Raw.Clone();
  byte[] replacement=Encoding.ASCII.GetBytes(result);Buffer.BlockCopy(replacement,0,raw,(int)BE(image.Data,8),replacement.Length);
  return FirmwareCodec.Repack(c,raw);
 }
 public static byte[][] ReadIRs(byte[] candidate,bool allowEmpty=false){
  var read=new Dictionary<uint,byte>();Transform(FirmwareCodec.Parse(candidate),null,read);
  if(read.Count!=3*0x6200+4)throw new InvalidDataException("Faltan bancos IR");
  uint mask=BitConverter.ToUInt32(Enumerable.Range(0,4).Select(i=>read[MaskAddress+(uint)i]).ToArray(),0);
  if(mask>7||mask==0&&!allowEmpty)throw new InvalidDataException("Máscara de IR incorrecta");
  var irs=new byte[3][];
  for(int slot=0;slot<3;slot++)if((mask&(1u<<slot))!=0)irs[slot]=Enumerable.Range(0,8192).Select(i=>read[Coefficients[slot]+(uint)i]).ToArray();
  return irs;
 }
 public static void ValidateCandidate(byte[] template,byte[] candidate){
  var c=FirmwareCodec.Parse(candidate);byte[][] irs=ReadIRs(candidate);
  var expected=FirmwareCodec.Parse(WithIRs(template,irs));
  if(!expected.Raw.SequenceEqual(c.Raw)||c.Chunks.Count!=expected.Chunks.Count)throw new InvalidDataException("El candidato modifica datos fuera de los tres IR");
  for(int i=0;i<c.Chunks.Count;i++){
   var a=c.Chunks[i];var b=expected.Chunks[i];
   if(a.Tag!=b.Tag||a.Tag!="data"&&!a.Data.SequenceEqual(b.Data))throw new InvalidDataException("Metadatos CTF alterados");
  }
 }
 public static void TestGuards(byte[] template,byte[] candidate){
  ValidateCandidate(template,candidate);int rejected=0;
  byte[] broken=(byte[])candidate.Clone();broken[broken.Length/2]^=1;
  try{ValidateCandidate(template,broken);}catch(Exception){rejected++;}
  var c=FirmwareCodec.Parse(candidate);byte[] raw=(byte[])c.Raw.Clone();raw[0]^=1;
  try{ValidateCandidate(template,FirmwareCodec.Repack(c,raw));}catch(InvalidDataException){rejected++;}
  byte[][] irs=ReadIRs(candidate);int slot=Array.FindIndex(irs,x=>x!=null);
  Buffer.BlockCopy(BitConverter.GetBytes(float.NaN),0,irs[slot],0,4);
  try{WithIRs(template,irs);}catch(InvalidDataException){rejected++;}
  try{WithIRs(template,new byte[3][]);}catch(InvalidDataException){rejected++;}
  if(rejected!=4)throw new InvalidDataException("Falló una barrera de validación v2");
 }
}
