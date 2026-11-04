using System;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;

// Level calculation and per-content profiles only. No audio/USB/device access.
static class IRLevels {
 public const double ExistingFirmwareGain = 1.0/32.0;
 public const double TargetPeakDb = -3.0;
 public class Analysis {
  public string key;
  public double frequencyPeak,frequencyPeakDb,automaticDb,energy;
 }
 public class Profile {
  public string name;
  public bool automatic=true;
  public double manualDb;
  public bool filtersEnabled=true;
  public double lowCutHz=80,highCutHz=8000;
 }
 public static Analysis Analyze(byte[] reversed){
  if(reversed==null||reversed.Length!=8192)throw new InvalidDataException("IR incorrecto");
  const int n=32768;var re=new double[n];var im=new double[n];double energy=0;
  for(int i=0;i<2048;i++){
   double x=BitConverter.ToSingle(reversed,(2047-i)*4);
   if(double.IsNaN(x)||double.IsInfinity(x))throw new InvalidDataException("IR no finito");
   re[i]=x;energy+=x*x;
  }
  if(energy==0)throw new InvalidDataException("El IR contiene solo silencio");
  for(int i=1,j=0;i<n;i++){
   int bit=n>>1;for(;(j&bit)!=0;bit>>=1)j^=bit;j^=bit;
   if(i<j){double t=re[i];re[i]=re[j];re[j]=t;}
  }
  for(int size=2;size<=n;size<<=1){
   double angle=-2*Math.PI/size,wrStep=Math.Cos(angle),wiStep=Math.Sin(angle);
   for(int start=0;start<n;start+=size){double wr=1,wi=0;
    for(int j=0;j<size/2;j++){
     int a=start+j,b=a+size/2;double tr=wr*re[b]-wi*im[b],ti=wr*im[b]+wi*re[b];
     double ar=re[a],ai=im[a];re[a]=ar+tr;im[a]=ai+ti;re[b]=ar-tr;im[b]=ai-ti;
     double next=wr*wrStep-wi*wiStep;wi=wr*wiStep+wi*wrStep;wr=next;
    }
   }
  }
  double peak=0;for(int i=0;i<=n/2;i++)peak=Math.Max(peak,Math.Sqrt(re[i]*re[i]+im[i]*im[i]));
  if(!(peak>0)||double.IsInfinity(peak))throw new InvalidDataException("Ganancia del IR no válida");
  double db=20*Math.Log10(peak);
  return new Analysis{key=FirmwareCodec.Hash(reversed),frequencyPeak=peak,frequencyPeakDb=db,automaticDb=TargetPeakDb-db,energy=energy};
 }
 public static double EffectiveDb(Analysis analysis,Profile profile){
  double db=profile.automatic?analysis.automaticDb:profile.manualDb;
  if(double.IsNaN(db)||double.IsInfinity(db)||Math.Abs(db)>1000)throw new InvalidDataException("Nivel del IR fuera de rango");return db;
 }
 public static byte[] Apply(byte[] originalReversed,double effectiveDb){
  if(originalReversed.Length!=8192||double.IsNaN(effectiveDb)||double.IsInfinity(effectiveDb)||Math.Abs(effectiveDb)>1000)throw new InvalidDataException("Nivel del IR no válido");
  // The tested DSP has a fixed 1/32 output factor. Compensate that factor so
  // this field always means the actual gain applied to the original IR.
  double multiplier=Math.Pow(10,effectiveDb/20)/ExistingFirmwareGain;
  var output=new byte[8192];
  for(int i=0;i<2048;i++){
   double x=BitConverter.ToSingle(originalReversed,i*4);float scaled=(float)(x*multiplier);
   if(double.IsNaN(x)||double.IsInfinity(x)||float.IsNaN(scaled)||float.IsInfinity(scaled))throw new InvalidDataException("El nivel solicitado desborda los coeficientes");
   Buffer.BlockCopy(BitConverter.GetBytes(scaled),0,output,i*4,4);
  }return output;
 }
 public static Dictionary<string,Profile> Load(string path){
  if(!File.Exists(path))return new Dictionary<string,Profile>();
  if(new FileInfo(path).Length>2000000)throw new InvalidDataException("Archivo de niveles demasiado grande");
  var result=new JavaScriptSerializer().Deserialize<Dictionary<string,Profile>>(File.ReadAllText(path));
  if(result==null)throw new InvalidDataException("Archivo de niveles incorrecto");
  foreach(var entry in result){
   if(entry.Key.Length!=64||entry.Value==null||double.IsNaN(entry.Value.manualDb)||double.IsInfinity(entry.Value.manualDb)||Math.Abs(entry.Value.manualDb)>1000)throw new InvalidDataException("Perfil de nivel incorrecto");
   IRFilters.Validate(entry.Value.lowCutHz,entry.Value.highCutHz);
  }return result;
 }
 public static void Save(string path,Dictionary<string,Profile> profiles){
  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
  string temporary=path+".tmp";File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(profiles),new System.Text.UTF8Encoding(false));
  if(File.Exists(path))File.Replace(temporary,path,path+".previous");else File.Move(temporary,path);
 }
}
