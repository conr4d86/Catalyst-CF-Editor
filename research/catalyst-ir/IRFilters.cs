using System;
using System.IO;

// Offline preparation of the IR; the amplifier convolves the resulting taps.
// 12 dB/oct Butterworth high-pass and low-pass. No additional DSP firmware code.
static class IRFilters {
 sealed class Biquad {
  double b0,b1,b2,a1,a2,z1,z2;
  public Biquad(double frequency,bool highpass){
   double omega=2*Math.PI*frequency/48000,cos=Math.Cos(omega),alpha=Math.Sin(omega)/Math.Sqrt(2),a0=1+alpha;
   a1=-2*cos/a0;a2=(1-alpha)/a0;
   if(highpass){b0=(1+cos)/2/a0;b1=-(1+cos)/a0;b2=b0;}
   else{b0=(1-cos)/2/a0;b1=(1-cos)/a0;b2=b0;}
  }
  public double Next(double x){double y=b0*x+z1;z1=b1*x-a1*y+z2;z2=b2*x-a2*y;return y;}
 }
 public static void Validate(double low,double high){
  if(double.IsNaN(low)||double.IsInfinity(low)||double.IsNaN(high)||double.IsInfinity(high)||low<20||low>2000||high<1000||high>22000||low>=high)throw new InvalidDataException("Los cortes deben cumplir: Low Cut 20–2000 Hz, High Cut 1000–22000 Hz y Low < High.");
 }
 public static byte[] Apply(byte[] original,bool enabled,double low=80,double high=8000){
  if(original==null||original.Length!=8192)throw new InvalidDataException("IR incorrecto");
  if(!enabled)return (byte[])original.Clone();Validate(low,high);
  var hp=new Biquad(low,true);var lp=new Biquad(high,false);var output=new byte[8192];
  for(int i=0;i<2048;i++){
   float source=BitConverter.ToSingle(original,(2047-i)*4);
   if(float.IsNaN(source)||float.IsInfinity(source))throw new InvalidDataException("IR no finito");
   float filtered=(float)lp.Next(hp.Next(source));
   if(float.IsNaN(filtered)||float.IsInfinity(filtered))throw new InvalidDataException("Filtro no finito");
   Buffer.BlockCopy(BitConverter.GetBytes(filtered),0,output,(2047-i)*4,4);
  }return output;
 }
}
