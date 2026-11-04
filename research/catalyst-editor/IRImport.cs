using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using System.Security.Cryptography;
// General audio-IR importer. Output is always the motor's 2048 reversed f32 taps.
// The input file is never changed. Level/EQ are deliberately left to IRLevels/IRFilters.
static class IRImport {
 public const int MaxFileBytes=64*1024*1024,MaxValues=8000000,TargetRate=48000,TapCount=2048;
 public const string FileFilter="Respuestas a impulsos|*.wav;*.wave;*.aif;*.aiff;*.aifc;*.flac|Todos los archivos|*.*";
 static readonly Dictionary<string,Result> cache=new Dictionary<string,Result>();static readonly object gate=new object();
 public sealed class Audio {public int Rate,Channels,Bits;public string Format;public double[] Samples;public int Frames{get{return Samples.Length/Channels;}}}
 public sealed class Result {
  public byte[] Coefficients;public string Summary,SourceHash;public int SourceRate,SourceChannels,SourceFrames,UsedFrames,TrimmedFrames;public bool Converted,TailWindow,PhaseSafeChannel;
  public Result Copy(){var r=(Result)MemberwiseClone();r.Coefficients=(byte[])Coefficients.Clone();return r;}
 }
 public static bool SupportedPath(string file){return new[]{".wav",".wave",".aif",".aiff",".aifc",".flac"}.Contains(Path.GetExtension(file).ToLowerInvariant());}
 public static byte[] WaveBytes(byte[] coefficients){
  Need(coefficients!=null&&coefficients.Length==TapCount*4,"Banco IR incorrecto");using(var m=new MemoryStream())using(var w=new BinaryWriter(m)){w.Write(Encoding.ASCII.GetBytes("RIFF"));w.Write(36+coefficients.Length);w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((ushort)3);w.Write((ushort)1);w.Write(TargetRate);w.Write(TargetRate*4);w.Write((ushort)4);w.Write((ushort)32);w.Write(Encoding.ASCII.GetBytes("data"));w.Write(coefficients.Length);for(int n=TapCount-1;n>=0;n--)w.Write(coefficients,n*4,4);w.Flush();return m.ToArray();}
 }
 static string Tag(byte[] b,int p){Need(p>=0&&p<=b.Length-4,"Cabecera de audio truncada");return Encoding.ASCII.GetString(b,p,4);}
 static void Need(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
 static uint U32(byte[] b,int p,bool big=false){Need(p>=0&&p<=b.Length-4,"Audio truncado");return big?((uint)b[p]<<24)|((uint)b[p+1]<<16)|((uint)b[p+2]<<8)|b[p+3]:BitConverter.ToUInt32(b,p);}
 static ushort U16(byte[] b,int p,bool big=false){Need(p>=0&&p<=b.Length-2,"Audio truncado");return big?(ushort)((b[p]<<8)|b[p+1]):BitConverter.ToUInt16(b,p);}
 static bool Finite(double x){return !double.IsNaN(x)&&!double.IsInfinity(x)&&Math.Abs(x)<=float.MaxValue;}
 static string Hash(byte[] b){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
 public static Result Read(string path){
  var info=new FileInfo(path);Need(info.Exists,"El archivo IR no existe");Need(info.Length>=12&&info.Length<=MaxFileBytes,"El IR debe tener una cabecera válida y ocupar como máximo 64 MiB");
  byte[] bytes=File.ReadAllBytes(path);string key=Hash(bytes);lock(gate){Result saved;if(cache.TryGetValue(key,out saved))return saved.Copy();}
  Audio audio;
  if((Tag(bytes,0)=="RIFF"||Tag(bytes,0)=="RF64"||Tag(bytes,0)=="RIFX")&&Tag(bytes,8)=="WAVE")audio=Wave(bytes);
  else if(Tag(bytes,0)=="FORM"&&(Tag(bytes,8)=="AIFF"||Tag(bytes,8)=="AIFC"))audio=Aiff(bytes);
  else {audio=IRMediaFoundation.Decode(Path.GetFullPath(path));Need(Hash(File.ReadAllBytes(path))==key,"El archivo cambió durante la importación");}
  Result result=ConvertAudio(audio);result.SourceHash=key;lock(gate){if(cache.Count>=16)cache.Clear();cache[key]=result.Copy();}return result;
 }
 static Audio Decode(byte[] b,int data,int size,int rate,int channels,int bits,int kind,bool big,bool unsigned8,string name){
  Need(rate>=8000&&rate<=768000&&channels>=1&&channels<=32,"Frecuencia o número de canales de IR fuera de los límites admitidos");
  Need((kind==1&&(bits==8||bits==16||bits==24||bits==32))||(kind==3&&(bits==32||bits==64)),"Codificación de muestras no admitida: se necesita PCM o float");
  int width=bits/8,align=checked(channels*width);Need(size>0&&size%align==0&&data>=0&&data<=b.Length-size,"Bloque de muestras truncado o desalineado");
  Need(size/width<=MaxValues,"El IR decodificado supera ocho millones de muestras");var samples=new double[size/width];
  for(int i=0,p=data;i<samples.Length;i++,p+=width){double x;
   if(kind==3){if(!big)x=bits==32?BitConverter.ToSingle(b,p):BitConverter.ToDouble(b,p);else{var v=new byte[width];for(int k=0;k<width;k++)v[k]=b[p+width-1-k];x=bits==32?BitConverter.ToSingle(v,0):BitConverter.ToDouble(v,0);}}
   else if(bits==8)x=unsigned8?(b[p]-128)/128.0:(sbyte)b[p]/128.0;
   else{long value=0;if(big){for(int k=0;k<width;k++)value=(value<<8)|b[p+k];}else{for(int k=width-1;k>=0;k--)value=(value<<8)|b[p+k];}long sign=1L<<(bits-1);if((value&sign)!=0)value-=1L<<bits;x=value/(double)sign;}
   Need(Finite(x),"El IR contiene muestras no finitas o fuera del rango float32");samples[i]=x;
  }
  return new Audio{Samples=samples,Rate=rate,Channels=channels,Bits=bits,Format=name};
 }
 static Audio Wave(byte[] b){
  bool big=Tag(b,0)=="RIFX",rf=Tag(b,0)=="RF64";long declared=U32(b,4,big);Need(rf||declared+8==b.Length,"Tamaño RIFF incorrecto");
  int fmt=-1,fmtBytes=0,data=-1,dataBytes=0;ulong rfData=0;int p=12;
  while(p<b.Length){Need(p<=b.Length-8,"Chunk WAV truncado");string tag=Tag(b,p);ulong n=U32(b,p+4,big);if(tag=="data"&&rf&&n==uint.MaxValue){Need(rfData>0,"RF64 sin tamaño de datos ds64");n=rfData;}Need(n<=int.MaxValue&&p+8L+(long)n<=b.Length,"Chunk WAV fuera del archivo");int count=(int)n;
   if(tag=="ds64"){Need(rf&&count>=28,"ds64 incorrecto");ulong length=BitConverter.ToUInt64(b,p+8);Need(length+8==(ulong)b.Length,"Tamaño RF64 incorrecto");rfData=BitConverter.ToUInt64(b,p+16);}
   if(tag=="fmt "){Need(fmt<0&&count>=16,"Cabecera fmt duplicada o truncada");fmt=p+8;fmtBytes=count;}
   if(tag=="data"){Need(data<0,"Bloque de muestras duplicado");data=p+8;dataBytes=count;}
   // Some exporters omit the terminal alignment byte (often after LIST).
   // Accept only an entirely present final chunk. Never repair sample bytes,
   // internal chunk boundaries, a truncated payload or the RIFF length.
   long end=p+8L+count,next=end+(count&1);if((count&1)!=0&&end==b.Length)next=end;
   Need(next<=b.Length,"Falta el padding de un chunk WAV");p=(int)next;
  }
  Need(fmt>=0&&data>=0,"Falta fmt o data en el WAV");int kind=U16(b,fmt,big),channels=U16(b,fmt+2,big),rate=checked((int)U32(b,fmt+4,big)),bits=U16(b,fmt+14,big),align=U16(b,fmt+12,big);
  if(kind==0xfffe){Need(!big&&fmtBytes>=40&&U16(b,fmt+16)>=22,"WAVEFORMATEXTENSIBLE truncado");var guid=new byte[16];Buffer.BlockCopy(b,fmt+24,guid,0,16);Guid g=new Guid(guid);if(g==new Guid("00000001-0000-0010-8000-00AA00389B71"))kind=1;else if(g==new Guid("00000003-0000-0010-8000-00AA00389B71"))kind=3;int valid=U16(b,fmt+18);Need(valid==0||valid<=bits,"Bits válidos incorrectos en el WAV");}
  Need(align==channels*(bits/8)&&U32(b,fmt+8,big)==(long)rate*align,"Alineación o tasa de bytes WAV incorrecta");return Decode(b,data,dataBytes,rate,channels,bits,kind,big,true,"WAV");
 }
 static Audio Aiff(byte[] b){
  Need(U32(b,4,true)+8L==b.Length,"Tamaño AIFF incorrecto");int comm=-1,commBytes=0,data=-1,size=0,p=12;bool aifc=Tag(b,8)=="AIFC";
  while(p<b.Length){Need(p<=b.Length-8,"Chunk AIFF truncado");string tag=Tag(b,p);uint n=U32(b,p+4,true);Need(n<=int.MaxValue&&p+8L+n<=b.Length,"Chunk AIFF fuera del archivo");int count=(int)n;
   if(tag=="COMM"){Need(comm<0&&count>=(aifc?22:18),"COMM inválido");comm=p+8;commBytes=count;}
   if(tag=="SSND"){Need(data<0&&count>=8,"SSND inválido");uint offset=U32(b,p+8,true);Need(offset<=count-8,"Offset SSND fuera de rango");data=checked(p+16+(int)offset);size=count-8-(int)offset;}
   long next=p+8L+count+(count&1);Need(next<=b.Length,"Falta padding AIFF");p=(int)next;
  }
  Need(comm>=0&&data>=0,"Falta COMM o SSND en AIFF");int channels=U16(b,comm,true),bits=U16(b,comm+6,true);uint frames=U32(b,comm+2,true);int exponent=U16(b,comm+8,true);Need((exponent&0x8000)==0&&(exponent&0x7fff)!=0x7fff,"Frecuencia AIFF inválida");
  ulong mantissa=((ulong)U32(b,comm+10,true)<<32)|U32(b,comm+14,true);double hz=mantissa*Math.Pow(2,(exponent&0x7fff)-16383-63);Need(Finite(hz)&&hz>=8000&&hz<=768000&&Math.Abs(hz-Math.Round(hz))<0.01,"Frecuencia AIFF no admitida");
  string code=aifc?Tag(b,comm+18):"NONE";int kind=(code=="fl32"||code=="FL32"||code=="fl64"||code=="FL64")?3:1;bool big=code!="sowt";Need(new[]{"NONE","twos","sowt","fl32","FL32","fl64","FL64"}.Contains(code),"Compresión AIFF no admitida");
  Need((long)frames*channels*(bits/8)==size,"Longitud SSND incorrecta");return Decode(b,data,size,(int)Math.Round(hz),channels,bits,kind,big,false,"AIFF");
 }
 public static Result ConvertAudio(Audio a){
  Need(a!=null&&a.Samples!=null&&a.Rate>=8000&&a.Rate<=768000&&a.Channels>0&&a.Channels<=32&&a.Samples.Length>0&&a.Samples.Length<=MaxValues&&a.Samples.Length%a.Channels==0,"Geometría de IR incorrecta");
  int frames=a.Frames,best=0;var energies=new double[a.Channels];for(int n=0;n<frames;n++)for(int c=0;c<a.Channels;c++){double x=a.Samples[n*a.Channels+c];Need(Finite(x),"IR con muestras no finitas");energies[c]+=x*x;}
  for(int c=1;c<a.Channels;c++)if(energies[c]>energies[best])best=c;Need(energies[best]>0,"El archivo IR está en silencio");var active=Enumerable.Range(0,a.Channels).Where(c=>energies[c]>energies[best]*1e-12).ToArray();
  var mono=new double[frames];double mixEnergy=0;for(int n=0;n<frames;n++){double x;if(active.Length==1)x=a.Samples[n*a.Channels+active[0]];else{x=0;foreach(int c in active)x+=a.Samples[n*a.Channels+c]/active.Length;}mono[n]=x;mixEnergy+=x*x;}
  bool phase=active.Length>1&&mixEnergy<energies[best]*0.25;if(phase)for(int n=0;n<frames;n++)mono[n]=a.Samples[n*a.Channels+best];
  bool longIR=Math.Ceiling(frames*(double)TargetRate/a.Rate)>TapCount;int start=0;
  if(longIR){double peak=mono.Max(x=>Math.Abs(x));int first=Array.FindIndex(mono,x=>Math.Abs(x)>=peak*0.0001);start=Math.Max(0,first-(int)Math.Ceiling(a.Rate*.001));}
  int available=checked((int)Math.Ceiling((frames-start)*(double)TargetRate/a.Rate)),used=Math.Min(TapCount,available);bool cropped=available>TapCount;var taps=new double[TapCount];
  if(a.Rate==TargetRate)Array.Copy(mono,start,taps,0,used);
  else{
   double ratio=a.Rate/(double)TargetRate,cutoff=Math.Min(1,1/ratio)*.96;int radius=(int)Math.Ceiling(32/cutoff);
   for(int n=0;n<used;n++){double x=start+n*ratio,total=0;int left=(int)Math.Ceiling(x-radius),right=(int)Math.Floor(x+radius);
    for(int k=left;k<=right;k++){if(k<0||k>=frames)continue;double d=x-k,t=d*cutoff,w=.5+.5*Math.Cos(Math.PI*d/radius);double sinc=Math.Abs(t)<1e-12?1:Math.Sin(Math.PI*t)/(Math.PI*t);total+=mono[k]*cutoff*sinc*w;}
    // IR resampling preserves discrete convolution gain, not waveform height.
    taps[n]=total*ratio;
   }
  }
  if(cropped){int fade=Math.Min(64,used);for(int i=0;i<fade;i++)taps[used-fade+i]*=.5+.5*Math.Cos(Math.PI*i/(fade-1));}
  var bytes=new byte[TapCount*4];for(int n=0;n<TapCount;n++){Need(Finite(taps[n]),"La conversión produjo una muestra fuera de rango");Buffer.BlockCopy(BitConverter.GetBytes((float)taps[n]),0,bytes,(TapCount-1-n)*4,4);}
  var changes=new List<string>();if(a.Channels>1)changes.Add(phase?"canal "+(best+1)+" para evitar cancelación":"mezcla mono");if(a.Rate!=TargetRate)changes.Add(a.Rate+" → 48000 Hz");if(start>0)changes.Add("silencio inicial ajustado");if(cropped)changes.Add("ventana de 2048 muestras (42,7 ms)");if(a.Bits!=32||a.Format!="WAV")changes.Add("float32");
  return new Result{Coefficients=bytes,Summary=(changes.Count==0?"IR compatible":string.Join(" · ",changes.ToArray()))+" · Mono · 48 kHz",SourceRate=a.Rate,SourceChannels=a.Channels,SourceFrames=frames,UsedFrames=used,TrimmedFrames=start,Converted=changes.Count>0,TailWindow=cropped,PhaseSafeChannel=phase};
 }
}
