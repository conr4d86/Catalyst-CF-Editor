using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;

// A user must supply the exact official CTF. This resource carries our empty
// extension and bounded deltas, never an official image or a user's IR.
static class FirmwarePatch {
 public class Change {public uint address{get;set;}public string expected{get;set;}public string replacement{get;set;}}
 public class Recipe {public string schema{get;set;}public string stock_sha256{get;set;}public string capsule{get;set;}public uint stock_main_end{get;set;}public uint capsule_address{get;set;}public Change[] changes{get;set;}}
 static uint BE(byte[] b,int p){return ((uint)b[p]<<24)|((uint)b[p+1]<<16)|((uint)b[p+2]<<8)|b[p+3];}
 static void BE(byte[] b,int p,uint v){b[p]=(byte)(v>>24);b[p+1]=(byte)(v>>16);b[p+2]=(byte)(v>>8);b[p+3]=(byte)v;}
 static byte[] Hex(string s){if(s==null||s.Length%2!=0)throw new InvalidDataException("HEX incorrecto");return Enumerable.Range(0,s.Length/2).Select(i=>Convert.ToByte(s.Substring(i*2,2),16)).ToArray();}
 static string Record(byte kind,int address,byte[] data){
  byte[] r=new byte[data.Length+5];r[0]=(byte)data.Length;r[1]=(byte)(address>>8);r[2]=(byte)address;r[3]=kind;
  Buffer.BlockCopy(data,0,r,4,data.Length);r[r.Length-1]=(byte)(-r.Sum(x=>(int)x)&255);
  return ":"+BitConverter.ToString(r).Replace("-","")+"\r\n";
 }
 public static byte[] Build(byte[] stock,byte[] recipeBytes){
  var recipe=new JavaScriptSerializer{MaxJsonLength=2000000}.Deserialize<Recipe>(Encoding.UTF8.GetString(recipeBytes));
  bool multi=recipe.schema=="CatalystIR.SourcePatch.v2";
  if((!multi&&recipe.schema!="CatalystIR.SourcePatch.v1")||FirmwareCodec.Hash(stock)!=recipe.stock_sha256)throw new InvalidDataException("Selecciona el firmware oficial Catalyst 60/100/200 v2.02 exacto");
  var container=FirmwareCodec.Parse(stock);var region=container.Chunks.Single(c=>c.Tag=="dinf"&&BE(c.Data,0)==0);
  int offset=(int)BE(region.Data,8),size=(int)BE(region.Data,12);
  var changes=new Dictionary<uint,Tuple<byte,byte>>();
  foreach(var change in recipe.changes){
   byte[] old=Hex(change.expected),replacement=Hex(change.replacement);if(old.Length!=replacement.Length)throw new InvalidDataException("Delta incorrecto");
   for(int i=0;i<old.Length;i++)changes.Add(change.address+(uint)i,Tuple.Create(old[i],replacement[i]));
  }
  var output=new StringBuilder();string startRecord=null;uint high=0,lastEnd=0;int changed=0;bool eof=false;
  foreach(string line in Encoding.ASCII.GetString(container.Raw,offset,size).Split(new[]{"\r\n"},StringSplitOptions.RemoveEmptyEntries)){
   if(eof||!line.StartsWith(":"))throw new InvalidDataException("Secuencia HEX incorrecta");byte[] r=Hex(line.Substring(1));
   if(r.Length<5||r.Length!=r[0]+5||(r.Sum(x=>(int)x)&255)!=0)throw new InvalidDataException("Registro HEX incorrecto");
   int kind=r[3],address=(r[1]<<8)|r[2];
   if(kind==1){eof=true;continue;}
   if(kind==5){if(startRecord!=null)throw new InvalidDataException("Inicio duplicado");startRecord=line+"\r\n";continue;}
   if(kind==4){if(r[0]!=2)throw new InvalidDataException("Base HEX incorrecta");high=(uint)((r[4]<<8)|r[5])<<16;output.Append(line+"\r\n");continue;}
   if(kind!=0)throw new InvalidDataException("Tipo HEX no admitido");
   uint first=high+(uint)address;if(first<lastEnd)throw new InvalidDataException("HEX solapado");lastEnd=first+r[0];
   byte[] payload=new byte[r[0]];Buffer.BlockCopy(r,4,payload,0,payload.Length);
   for(int i=0;i<payload.Length;i++){
    Tuple<byte,byte> delta;if(!changes.TryGetValue(first+(uint)i,out delta))continue;
    if(payload[i]!=delta.Item1)throw new InvalidDataException("El firmware original no coincide con el parche");payload[i]=delta.Item2;changed++;
   }output.Append(Record(0,address,payload));
  }
  if(!eof||changed!=changes.Count||lastEnd!=recipe.stock_main_end)throw new InvalidDataException("La imagen original está incompleta");
  byte[] capsule=Hex(recipe.capsule);uint end=recipe.capsule_address+(uint)capsule.Length;
  if(recipe.capsule_address<lastEnd||capsule.Length!=(multi?0x16000:0x8000)||end>0x60200000)throw new InvalidDataException("Extensión incorrecta");
  for(uint a=lastEnd;a<end;){
   uint nextHigh=a&0xffff0000;
   if(nextHigh!=high){high=nextHigh;output.Append(Record(4,0,new[]{(byte)(a>>24),(byte)(a>>16)}));}
   int count=(int)Math.Min(16,Math.Min(end-a,0x10000-(a&0xffff)));byte[] bytes=new byte[count];
   for(int i=0;i<count;i++)bytes[i]=a+(uint)i<recipe.capsule_address?(byte)255:capsule[a+(uint)i-recipe.capsule_address];
   output.Append(Record(0,(int)(a&0xffff),bytes));a+=(uint)count;
  }
  if(startRecord!=null)output.Append(startRecord);output.Append(Record(1,0,new byte[0]));
  byte[] hex=Encoding.ASCII.GetBytes(output.ToString());var second=container.Chunks.Single(c=>c.Tag=="dinf"&&BE(c.Data,0)==5);
  int secondOffset=(int)BE(second.Data,8),secondLength=(int)BE(second.Data,12);byte[] raw=new byte[hex.Length+secondLength];
  Buffer.BlockCopy(hex,0,raw,0,hex.Length);Buffer.BlockCopy(container.Raw,secondOffset,raw,hex.Length,secondLength);
  BE(region.Data,12,(uint)hex.Length);BE(second.Data,8,(uint)hex.Length);
  return FirmwareCodec.Repack(container,raw);
 }
}
