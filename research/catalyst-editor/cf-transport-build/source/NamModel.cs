using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

// Offline, fixed-geometry A2 Lite importer. No vendor SDK or device APIs.
sealed class NamModel {
 public const int WeightCount=1871,StateBytes=76536;
 public const double InputTrimDb=-12;
 public const double ReferenceRms=0.15205092918678589; // Replaced by the measured build calibration.
 public static readonly int[] Kernels=Enumerable.Repeat(6,14).Concat(new[]{15,15}).Concat(Enumerable.Repeat(6,7)).ToArray();
 public static readonly int[] Dilations=new[]{1,3,7,17,41,101,239,1,3,7,17,41,101,239,1,13,1,3,7,17,41,101,239};
 public readonly string Name,SourceHash;
 public readonly byte[] Source;
 public readonly float[] Weights;
 public readonly double AutomaticDb,MeasuredRms,MeasuredPeak;
 readonly Dictionary<double,double> inputLevels=new Dictionary<double,double>();
 public static float F(double x){float f=(float)x;if(float.IsNaN(f)||float.IsInfinity(f))throw new InvalidDataException("El NAM produce valores fuera de float32");return f;}
 static Dictionary<string,object> Obj(object v){var d=v as Dictionary<string,object>;if(d==null)throw new InvalidDataException("Objeto NAM incorrecto");return d;}
 static object Get(Dictionary<string,object> d,string k){object v;return d.TryGetValue(k,out v)?v:null;}
 static object[] Arr(object v){var a=v as object[];if(a==null)throw new InvalidDataException("Lista NAM incorrecta");return a;}
 static double Num(object v){if(!(v is double))throw new InvalidDataException("Número NAM incorrecto");return (double)v;}
 static void Need(bool ok,string field){if(!ok)throw new InvalidDataException("NAM no compatible: "+field+". Se admite A2 Lite mono de 3 canales a 48 kHz.");}
 static void Equal(object a,object b,string field){Need(FirmwareCodec.Canonical(a)==FirmwareCodec.Canonical(b),field);}
 static bool Disabled(object v){if(v==null||v is bool&&!(bool)v)return true;var d=v as Dictionary<string,object>;return d!=null&&Get(d,"active") is bool&&!(bool)d["active"];}
 NamModel(string name,byte[] source,float[] weights){Name=name;Source=source;SourceHash=FirmwareCodec.Hash(source);Weights=weights;
  var values=Render(CalibrationSignal());MeasuredPeak=values.Max(x=>Math.Abs((double)x));MeasuredRms=Math.Sqrt(values.Skip(1024).Average(x=>(double)x*x));
  Need(MeasuredPeak>1e-9&&MeasuredRms>1e-9,"captura silenciosa");
  AutomaticDb=Math.Min(12,Math.Min(20*Math.Log10(ReferenceRms/MeasuredRms),20*Math.Log10(Math.Pow(10,-6.0/20)/MeasuredPeak)));
  Need(AutomaticDb>=-60,"nivel de salida extremo");
  inputLevels[-12]=AutomaticDb;
 }
 public static NamModel Load(string path){var f=new FileInfo(path);if(f.Length>16*1024*1024)throw new InvalidDataException("El NAM supera 16 MiB");return Parse(File.ReadAllBytes(path),Path.GetFileName(path));}
 public static NamModel Parse(byte[] bytes,string name){
  if(bytes.Length==0||bytes.Length>16*1024*1024)throw new InvalidDataException("Tamaño NAM incorrecto");
  string text=new UTF8Encoding(false,true).GetString(bytes);if(text.Length>0&&text[0]=='\ufeff')text=text.Substring(1);
  var d=Obj(StrictNamJson.Parse(text));var models=new List<Dictionary<string,object>>();
  if((string)Get(d,"architecture")=="SlimmableContainer")foreach(var sub in Arr(Get(Obj(Get(d,"config")),"submodels")))models.Add(Obj(Get(Obj(sub),"model")));else models.Add(d);
  var selected=models.Where(candidate=>(string)Get(candidate,"architecture")=="WaveNet"&&Candidate(candidate)).ToArray();Need(selected.Length==1,"selección única Lite");
  var m=selected[0];var c=Obj(Get(m,"config"));var layers=Arr(Get(c,"layers"));Need(layers.Length==1,"una pila");var l=Obj(layers[0]);
  foreach(string k in c.Keys)Need(new[]{"layers","head","head_scale","in_channels","condition_dsp"}.Contains(k),"config."+k);
  foreach(string k in l.Keys)Need(new[]{"input_size","condition_size","head","channels","kernel_sizes","dilations","activation","bottleneck","head1x1","layer1x1","groups_input","groups_input_mixin","conv_pre_film","conv_post_film","input_mixin_pre_film","input_mixin_post_film","activation_pre_film","activation_post_film","layer1x1_post_film","head1x1_post_film","gating_mode","gated","secondary_activation","slimmable"}.Contains(k),"layers."+k);
  foreach(string k in new[]{"input_size","condition_size","groups_input","groups_input_mixin"})Need(Num(Get(l,k))==1,k);
  foreach(string k in new[]{"channels","bottleneck"})Need(Num(Get(l,k))==3,k);
  Need(Get(c,"in_channels")==null||Num(c["in_channels"])==1,"entrada mono");Need(Get(c,"head")==null&&Get(c,"condition_dsp")==null,"head/condition_dsp");
  double scale=Num(Get(c,"head_scale"));Need(!double.IsInfinity(scale)&&!double.IsNaN(scale),"head_scale");
  Equal(Get(l,"kernel_sizes"),Kernels.Cast<object>().ToArray(),"kernel_sizes");Equal(Get(l,"dilations"),Dilations.Cast<object>().ToArray(),"dilations");
  var head=Obj(Get(l,"head"));Need(head.Count==3||head.Count==4&&Get(head,"head_dilation")!=null,"head");
  Need(Num(Get(head,"out_channels"))==1&&Num(Get(head,"kernel_size"))==16&&Get(head,"bias") is bool&&(bool)head["bias"]&&(Get(head,"head_dilation")==null||Num(head["head_dilation"])==1),"head mono 16");
  var residual=Obj(Get(l,"layer1x1"));Need(residual.Count==2&&Get(residual,"active") is bool&&(bool)residual["active"]&&Num(Get(residual,"groups"))==1,"layer1x1");
  Need(Get(l,"slimmable")==null&&Disabled(Get(l,"gated")),"gating/slimmable");
  if(Get(l,"gating_mode")!=null){var a=Arr(l["gating_mode"]);Need(a.Length==23&&a.All(x=>x is string&&(string)x=="none"),"gating_mode");}
  if(Get(l,"secondary_activation")!=null){var a=Arr(l["secondary_activation"]);Need(a.Length==23&&a.All(x=>x==null),"secondary_activation");}
  var activations=Arr(Get(l,"activation"));Need(activations.Length==23,"activation");foreach(var a in activations){var ac=Obj(a);Need(ac.Count==2&&(string)Get(ac,"type")=="LeakyReLU"&&Num(Get(ac,"negative_slope"))==.01,"LeakyReLU 0.01");}
  foreach(string k in new[]{"head1x1","conv_pre_film","conv_post_film","input_mixin_pre_film","input_mixin_post_film","activation_pre_film","activation_post_film","layer1x1_post_film","head1x1_post_film"})Need(Disabled(Get(l,k)),k);
  bool rate=false;foreach(var source in new[]{d,m})if(Get(source,"sample_rate")!=null){Need(Num(source["sample_rate"])==48000,"sample_rate");rate=true;}Need(rate,"sample_rate explícita");
  var raw=Arr(Get(m,"weights"));Need(raw.Length==WeightCount,"1871 pesos");var original=raw.Select(x=>F(Num(x))).ToArray();Need(Math.Abs(original[WeightCount-1]-scale)<=Math.Max(1e-8,Math.Abs(scale)*1e-5),"head_scale y pesos incoherentes");
  var packed=new List<float>();packed.AddRange(original.Take(3));int offset=3;
  foreach(int k in Kernels){int conv=offset;offset+=9*k;packed.AddRange(original.Skip(offset).Take(3));offset+=3;packed.AddRange(original.Skip(offset).Take(3));offset+=3;int res=offset;offset+=9;int rb=offset;offset+=3;
   for(int t=0;t<k;t++)for(int j=0;j<3;j++)for(int i=0;i<3;i++)packed.Add(original[conv+(i*3+j)*k+t]);
   packed.AddRange(original.Skip(rb).Take(3));for(int j=0;j<3;j++)for(int i=0;i<3;i++)packed.Add(original[res+i*3+j]);
  }
  int hw=offset;offset+=48;packed.Add(original[offset]);for(int t=0;t<16;t++)for(int j=0;j<3;j++)packed.Add(original[hw+j*16+t]);packed.Add(original[offset+1]);Need(packed.Count==WeightCount,"orden de pesos");
  return new NamModel(name,bytes,packed.ToArray());
 }
 static bool Candidate(Dictionary<string,object> m){var c=Get(m,"config") as Dictionary<string,object>;if(c==null)return false;var a=Get(c,"layers") as object[];if(a==null||a.Length==0)return false;var l=a[0] as Dictionary<string,object>;return l!=null&&Get(l,"channels") is double&&(double)l["channels"]==3;}
 public double AutomaticForInput(double db){CheckInput(db);lock(inputLevels){double gain;if(inputLevels.TryGetValue(db,out gain))return gain;double factor=Math.Pow(10,(db+12)/20);var rendered=Render(CalibrationSignal().Select(x=>F(x*factor)).ToArray());double rms=Math.Sqrt(rendered.Skip(1024).Average(x=>(double)x*x)),peak=rendered.Max(x=>Math.Abs((double)x));Need(rms>1e-9&&peak>1e-9,"nivel calibrado silencioso");gain=Math.Min(12,Math.Min(20*Math.Log10(ReferenceRms/rms),20*Math.Log10(Math.Pow(10,-6.0/20)/peak)));Need(gain>=-60,"nivel extremo");inputLevels.Add(db,gain);return gain;}}
 static void CheckInput(double db){if(double.IsNaN(db)||double.IsInfinity(db)||db<-36||db>0)throw new InvalidDataException("Entrada NAM fuera de −36…0 dB");}
 public byte[] Packed(double db){return Packed(-12,db);}
 public byte[] Packed(double inputDb,double db){CheckInput(inputDb);if(double.IsNaN(db)||double.IsInfinity(db)||db<-60||db>12)throw new InvalidDataException("Volumen NAM fuera de −60…+12 dB");var w=(float[])Weights.Clone();double factor=Math.Pow(10,(inputDb+12)/20);for(int i=0;i<3;i++)w[i]=F(w[i]*factor);int off=3;foreach(int k in Kernels){for(int i=0;i<3;i++)w[off+3+i]=F(w[off+3+i]*factor);off+=18+9*k;}w[w.Length-1]=F(w[w.Length-1]*Math.Pow(10,db/20));return Bytes(w);}
 public static byte[] Bytes(float[] f){var b=new byte[f.Length*4];Buffer.BlockCopy(f,0,b,0,b.Length);return b;}
 public static float[] Floats(byte[] b){if(b.Length%4!=0)throw new InvalidDataException("Float32 truncado");var f=new float[b.Length/4];Buffer.BlockCopy(b,0,f,0,b.Length);if(f.Any(x=>float.IsNaN(x)||float.IsInfinity(x)))throw new InvalidDataException("Pesos no finitos");return f;}
 public static void Reset(float[] w,out byte[] state,out byte[] silence){
  if(w.Length!=WeightCount)throw new InvalidDataException("Pesos NAM incorrectos");state=new byte[StateBytes];var constants=new List<float>();float[] x={0,0,0},hs={0,0,0};int wo=3,hist=24*4;
  for(int l=0;l<23;l++){int k=Kernels[l],len=(k-1)*Dilations[l]+1;constants.AddRange(x);for(int p=0;p<len;p++)for(int j=0;j<3;j++){Buffer.BlockCopy(BitConverter.GetBytes(x[j]),0,state,hist,4);hist+=4;}
   var a=new float[3];for(int i=0;i<3;i++){float v=F(w[wo+i]+F(w[wo+3+i]*0.0));for(int t=0;t<k;t++)for(int j=0;j<3;j++)v=F(v+F(w[wo+6+t*9+j*3+i]*x[j]));a[i]=v<0?F(v*.01f):v;}
   int rw=wo+6+9*k;for(int i=0;i<3;i++){hs[i]=F(hs[i]+a[i]);float v=w[rw+i];for(int j=0;j<3;j++)v=F(v+F(w[rw+3+j*3+i]*a[j]));x[i]=F(x[i]+v);}wo+=18+9*k;
  }
  constants.AddRange(hs);for(int t=0;t<16;t++)for(int j=0;j<3;j++){Buffer.BlockCopy(BitConverter.GetBytes(hs[j]),0,state,hist,4);hist+=4;}if(hist!=StateBytes)throw new InvalidDataException("Estado NAM incorrecto");silence=Bytes(constants.ToArray());
 }
 public static float[] CalibrationSignal(){var f=new float[9216];uint seed=0x5150;for(int i=1024;i<f.Length;i++){seed=unchecked(seed*1664525+1013904223);double noise=((seed>>8)/16777216.0-.5)*.08;double level=i<4096?.06:i<6656?.15:.30;f[i]=F(level*(.50*Math.Sin(i*.028797932658)+.25*Math.Sin(i*.08325220532)+.15*Math.Sin(i*.3110176727)+noise)*Math.Pow(10,InputTrimDb/20));}return f;}
 public float[] Render(float[] input){return RenderPacked(Weights,input);}
 public static float[] RenderPacked(float[] weights,float[] input){
  byte[] initial,silence;Reset(weights,out initial,out silence);var h=Floats(initial.Skip(96).ToArray());var cursor=new int[24];var result=new float[input.Length];int[] ro=new int[23],wo=new int[23],lens=new int[23];int r=0,w=3;for(int l=0;l<23;l++){ro[l]=r;wo[l]=w;lens[l]=(Kernels[l]-1)*Dilations[l]+1;r+=3*lens[l];w+=18+9*Kernels[l];}
  for(int n=0;n<input.Length;n++){float vinput=input[n];float[] x={F(weights[0]*vinput),F(weights[1]*vinput),F(weights[2]*vinput)},hs={0,0,0};
   for(int l=0;l<23;l++){int p=cursor[l],len=lens[l],k=Kernels[l],off=wo[l],origin=ro[l];for(int j=0;j<3;j++)h[origin+p*3+j]=x[j];if(++p==len)p=0;cursor[l]=p;
    var a=new float[3];for(int i=0;i<3;i++){float v=F(weights[off+i]+F(weights[off+3+i]*vinput));int tap=p;for(int t=0;t<k;t++){for(int j=0;j<3;j++)v=F(v+F(weights[off+6+t*9+j*3+i]*h[origin+tap*3+j]));tap+=Dilations[l];if(tap>=len)tap-=len;}a[i]=v<0?F(v*.01f):v;}
    int rw=off+6+9*k;for(int i=0;i<3;i++){hs[i]=F(hs[i]+a[i]);float v=weights[rw+i];for(int j=0;j<3;j++)v=F(v+F(weights[rw+3+j*3+i]*a[j]));x[i]=F(x[i]+v);}
   }
   int hp=cursor[23];for(int j=0;j<3;j++)h[r+hp*3+j]=hs[j];if(++hp==16)hp=0;cursor[23]=hp;float y=weights[w];int hw=w+1;for(int t=0;t<16;t++){for(int j=0;j<3;j++)y=F(y+F(weights[hw++]*h[r+hp*3+j]));if(++hp==16)hp=0;}result[n]=F(y*weights[WeightCount-1]);
  }return result;
 }
}

// Duplicate keys and non-JSON numbers are rejected before model selection.
sealed class StrictNamJson {
 readonly string s;int p;StrictNamJson(string v){s=v;}
 public static object Parse(string s){var parser=new StrictNamJson(s);object v=parser.Value(0);parser.White();if(parser.p!=s.Length)throw new InvalidDataException("Datos después del JSON");return v;}
 void White(){while(p<s.Length&&(s[p]==' '||s[p]=='\r'||s[p]=='\n'||s[p]=='\t'))p++;}
 void Expect(char c){White();if(p>=s.Length||s[p++]!=c)throw new InvalidDataException("JSON NAM incorrecto");}
 string Str(){White();int start=p;Expect('"');bool escape=false;while(p<s.Length){char c=s[p++];if(!escape&&c=='"')return new JavaScriptSerializer().Deserialize<string>(s.Substring(start,p-start));if(c<32)throw new InvalidDataException("Control en texto JSON");if(!escape&&c=='\\')escape=true;else escape=false;}throw new InvalidDataException("Texto JSON truncado");}
 object Value(int depth){if(depth>80)throw new InvalidDataException("JSON demasiado profundo");White();if(p>=s.Length)throw new InvalidDataException("JSON truncado");char c=s[p];
  if(c=='"')return Str();if(c=='{'){p++;var d=new Dictionary<string,object>(StringComparer.Ordinal);White();if(p<s.Length&&s[p]=='}'){p++;return d;}while(true){string k=Str();Expect(':');if(d.ContainsKey(k))throw new InvalidDataException("Clave JSON duplicada: "+k);d.Add(k,Value(depth+1));White();if(p<s.Length&&s[p]=='}'){p++;return d;}Expect(',');}}
  if(c=='['){p++;var a=new List<object>();White();if(p<s.Length&&s[p]==']'){p++;return a.ToArray();}while(true){a.Add(Value(depth+1));White();if(p<s.Length&&s[p]==']'){p++;return a.ToArray();}Expect(',');}}
  foreach(string t in new[]{"true","false","null"})if(p+t.Length<=s.Length&&s.Substring(p,t.Length)==t){p+=t.Length;return t=="null"?null:(object)(t=="true");}
  int start=p;while(p<s.Length&&"0123456789eE+-.".IndexOf(s[p])>=0)p++;string number=s.Substring(start,p-start);double v;
  if(!System.Text.RegularExpressions.Regex.IsMatch(number,@"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?$")||!double.TryParse(number,NumberStyles.Float,CultureInfo.InvariantCulture,out v)||double.IsNaN(v)||double.IsInfinity(v))throw new InvalidDataException("Número JSON no finito o incorrecto");return v;
 }
}
