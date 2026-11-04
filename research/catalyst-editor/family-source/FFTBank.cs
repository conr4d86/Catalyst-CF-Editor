using System;
using System.IO;
using System.Runtime.CompilerServices;

// Deterministic offline FFT-partition coefficient builder. No audio/USB APIs.
static class FFTBank {
 static readonly uint[] Words = {0x3f800000u,0x80000000u,0x3f7ec46du,0xbdc8bd36u,0x3f7b14beu,0xbe47c5c2u,0x3f74fa0bu,0xbe94a031u,0x3f6c835eu,0xbec3ef15u,0x3f61c598u,0xbef15aeau,0x3f54db31u,0xbf0e39dau,0x3f45e403u,0xbf226799u,0x3f3504f3u,0xbf3504f3u,0x3f226799u,0xbf45e403u,0x3f0e39dau,0xbf54db31u,0x3ef15aeau,0xbf61c598u,0x3ec3ef15u,0xbf6c835eu,0x3e94a031u,0xbf74fa0bu,0x3e47c5c2u,0xbf7b14beu,0x3dc8bd36u,0xbf7ec46du,0x248d3132u,0xbf800000u,0xbdc8bd36u,0xbf7ec46du,0xbe47c5c2u,0xbf7b14beu,0xbe94a031u,0xbf74fa0bu,0xbec3ef15u,0xbf6c835eu,0xbef15aeau,0xbf61c598u,0xbf0e39dau,0xbf54db31u,0xbf226799u,0xbf45e403u,0xbf3504f3u,0xbf3504f3u,0xbf45e403u,0xbf226799u,0xbf54db31u,0xbf0e39dau,0xbf61c598u,0xbef15aeau,0xbf6c835eu,0xbec3ef15u,0xbf74fa0bu,0xbe94a031u,0xbf7b14beu,0xbe47c5c2u,0xbf7ec46du,0xbdc8bd36u};
 [MethodImpl(MethodImplOptions.NoInlining)]static double Round64(double x){return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x));}
 public static byte[] Build(byte[] reversed){
  if(reversed.Length!=8192)throw new InvalidDataException("IR length");
  var weights=new float[64];for(int i=0;i<64;i++)weights[i]=BitConverter.ToSingle(BitConverter.GetBytes(Words[i]),0);
  var output=new byte[0x4200];
  for(int part=0;part<64;part++)for(int k=0;k<33;k++){
   double re=0,im=0;
   for(int n=0;n<32;n++){
    int sample=part*32+n,index=(k*n)&63;double sign=index>=32?-1:1;index&=31;
    double x=BitConverter.ToSingle(reversed,(2047-sample)*4);
    re=Round64(re+x*(weights[index*2]*sign));im=Round64(im+x*(weights[index*2+1]*sign));
   }
   float real=(float)re,imaginary=(float)im;
   if(float.IsNaN(real)||float.IsInfinity(real)||float.IsNaN(imaginary)||float.IsInfinity(imaginary))throw new InvalidDataException("El nivel del IR desborda el banco FFT");
   int offset=part*264+k*8;Buffer.BlockCopy(BitConverter.GetBytes(real),0,output,offset,4);Buffer.BlockCopy(BitConverter.GetBytes(imaginary),0,output,offset+4,4);
  }return output;
 }
}
