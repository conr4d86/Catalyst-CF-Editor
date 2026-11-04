using System;using System.IO;using System.Linq;using System.Text;
namespace CFEditor {
// Display/calibration metadata, not another DSP processing stage.
// Taps/weights are already processed by the importer exactly once.
sealed class AssetInfo {
 public bool Automatic,Filters;public float InputDb,OutputDb,LowHz,HighHz;public string Name="";public byte[] SourceHash=new byte[32];
 static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
 static uint U32(byte[] b,int p){return (uint)b[p]|((uint)b[p+1]<<8)|((uint)b[p+2]<<16)|((uint)b[p+3]<<24);}
 static void Put(byte[] b,int p,uint v){for(int i=0;i<4;i++)b[p+i]=(byte)(v>>(8*i));}
 static float F32(byte[] b,int p){var raw=new byte[4];Buffer.BlockCopy(b,p,raw,0,4);if(!BitConverter.IsLittleEndian)Array.Reverse(raw);return BitConverter.ToSingle(raw,0);}
 static void PutFloat(byte[] b,int p,float v){byte[] raw=BitConverter.GetBytes(v);if(!BitConverter.IsLittleEndian)Array.Reverse(raw);Buffer.BlockCopy(raw,0,b,p,4);}
 static bool Range(float v,float low,float high){return !float.IsNaN(v)&&!float.IsInfinity(v)&&v>=low&&v<=high;}
 void Validate(){
  if(Name==null||SourceHash==null||SourceHash.Length!=32||!Range(InputDb,-60,12)||!Range(OutputDb,-120,36)||!Range(LowHz,0,24000)||!Range(HighHz,0,24000)||LowHz>HighHz||BitConverter.ToInt32(BitConverter.GetBytes(LowHz),0)<0||BitConverter.ToInt32(BitConverter.GetBytes(HighHz),0)<0)throw new InvalidDataException("Metadatos del recurso no válidos");
  if(Name.Any(c=>c<32||c==127||c=='/'||c=='\\'||c=='\u2028'||c=='\u2029'))throw new InvalidDataException("El nombre contiene una ruta o caracteres de control");
  if(Utf8.GetByteCount(Name)>127)throw new InvalidDataException("El nombre supera el espacio del amplificador");
 }
 public byte[] Encode(){
  Validate();var b=new byte[184];Put(b,0,(uint)((Automatic?1:0)|(Filters?2:0)));PutFloat(b,4,InputDb);PutFloat(b,8,OutputDb);PutFloat(b,12,LowHz);PutFloat(b,16,HighHz);
  byte[] name=Utf8.GetBytes(Name);Put(b,20,(uint)name.Length);Buffer.BlockCopy(name,0,b,24,name.Length);Buffer.BlockCopy(SourceHash,0,b,152,32);return b;
 }
 public static AssetInfo Decode(byte[] b){
  if(b==null||b.Length!=184||U32(b,0)>3||U32(b,20)>127)throw new InvalidDataException("Metadatos del recurso truncados o incompatibles");int n=(int)U32(b,20);
  for(int i=n;i<128;i++)if(b[24+i]!=0)throw new InvalidDataException("Nombre del recurso no válido");
  var d=new AssetInfo{Automatic=(U32(b,0)&1)!=0,Filters=(U32(b,0)&2)!=0,InputDb=F32(b,4),OutputDb=F32(b,8),LowHz=F32(b,12),HighHz=F32(b,16),Name=Utf8.GetString(b,24,n),SourceHash=b.Skip(152).Take(32).ToArray()};d.Validate();return d;
 }
 public static string DisplayName(string file){
  string name=Path.GetFileNameWithoutExtension(file);if(name==null)name="";
  if(Utf8.GetByteCount(name)<=127)return name;
  int end=name.Length;while(end>0){end--;if(end>0&&char.IsHighSurrogate(name[end-1])&&char.IsLowSurrogate(name[end]))end--;if(Utf8.GetByteCount(name.Substring(0,end))<=124)return name.Substring(0,end)+"…";}return "";
 }
}
}
