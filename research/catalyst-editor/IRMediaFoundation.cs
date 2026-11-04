using System;using System.IO;using System.Collections.Generic;using System.Runtime.InteropServices;
// File-only Windows decoder fallback (e.g. FLAC). No capture devices, playback,
// helper executables or third-party native codec downloads are involved.
static class IRMediaFoundation {
 [DllImport("ole32.dll")]static extern int CoInitializeEx(IntPtr reserved,uint flags);
 [DllImport("ole32.dll")]static extern void CoUninitialize();
 [DllImport("mfplat.dll")]static extern int MFStartup(int version,int flags);
 [DllImport("mfplat.dll")]static extern int MFShutdown();
 [DllImport("mfplat.dll")]static extern int MFCreateMediaType(out IntPtr type);
 [DllImport("mfreadwrite.dll",CharSet=CharSet.Unicode)]static extern int MFCreateSourceReaderFromURL(string path,IntPtr attributes,out IntPtr reader);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int Select(IntPtr self,uint stream,[MarshalAs(UnmanagedType.Bool)]bool enabled);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int SetGuid(IntPtr self,ref Guid key,ref Guid value);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int GetGuid(IntPtr self,ref Guid key,out Guid value);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int SetUInt(IntPtr self,ref Guid key,uint value);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int GetUInt(IntPtr self,ref Guid key,out uint value);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int SetType(IntPtr self,uint stream,IntPtr reserved,IntPtr type);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int ReadMediaType(IntPtr self,uint stream,out IntPtr type);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int ReadSample(IntPtr self,uint stream,uint flags,out uint actual,out uint returnedFlags,out long time,out IntPtr sample);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int Contiguous(IntPtr self,out IntPtr buffer);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int LockBuffer(IntPtr self,out IntPtr bytes,out uint maximum,out uint current);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int UnlockBuffer(IntPtr self);
 static Guid Major=new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"),Subtype=new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5"),Audio=new Guid("73647561-0000-0010-8000-00aa00389b71"),Float=new Guid("00000003-0000-0010-8000-00aa00389b71");
 static Guid Channels=new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a"),Rate=new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba"),Bits=new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
 static T Method<T>(IntPtr obj,int index){return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj),index*IntPtr.Size),typeof(T));}
 static void Check(int result){if(result<0)Marshal.ThrowExceptionForHR(result);}
 static void Release(ref IntPtr obj){if(obj!=IntPtr.Zero){Marshal.Release(obj);obj=IntPtr.Zero;}}
 public static IRImport.Audio Decode(string path){
  if(!File.Exists(path)||!Path.IsPathRooted(path))throw new InvalidDataException("Se necesita un archivo IR local");
  IntPtr reader=IntPtr.Zero,type=IntPtr.Zero,current=IntPtr.Zero;bool mf=false,com=false;
  try{
   int init=CoInitializeEx(IntPtr.Zero,0);com=init>=0;if(init<0&&init!=unchecked((int)0x80010106))Check(init);
   Check(MFStartup(0x20070,1));mf=true;Check(MFCreateSourceReaderFromURL(path,IntPtr.Zero,out reader));
   const uint firstAudio=0xfffffffdu;Check(Method<Select>(reader,4)(reader,0xfffffffeu,false));Check(Method<Select>(reader,4)(reader,firstAudio,true));
   Check(MFCreateMediaType(out type));Check(Method<SetGuid>(type,24)(type,ref Major,ref Audio));Check(Method<SetGuid>(type,24)(type,ref Subtype,ref Float));Check(Method<SetUInt>(type,21)(type,ref Bits,32));
   Check(Method<SetType>(reader,7)(reader,firstAudio,IntPtr.Zero,type));Check(Method<ReadMediaType>(reader,6)(reader,firstAudio,out current));
   uint channels,rate,bits;Guid subtype;Check(Method<GetUInt>(current,7)(current,ref Channels,out channels));Check(Method<GetUInt>(current,7)(current,ref Rate,out rate));Check(Method<GetUInt>(current,7)(current,ref Bits,out bits));Check(Method<GetGuid>(current,10)(current,ref Subtype,out subtype));
   if(channels<1||channels>32||rate<8000||rate>768000||bits!=32||subtype!=Float)throw new InvalidDataException("El decodificador no produjo audio float32 válido");
   var values=new List<double>();bool ended=false;
   for(int packet=0;packet<100000;packet++){
    IntPtr sample=IntPtr.Zero,buffer=IntPtr.Zero;bool locked=false;
    try{
     uint stream,flags;long time;Check(Method<ReadSample>(reader,9)(reader,firstAudio,0,out stream,out flags,out time,out sample));
     if((flags&0x31)!=0)throw new InvalidDataException("El formato del audio cambió durante la lectura");
     if(sample!=IntPtr.Zero){
      Check(Method<Contiguous>(sample,41)(sample,out buffer));IntPtr bytes;uint maximum,length;Check(Method<LockBuffer>(buffer,3)(buffer,out bytes,out maximum,out length));locked=true;
      if(length>maximum||length%(channels*4)!=0||length/4>IRImport.MaxValues-values.Count)throw new InvalidDataException("El IR decodificado excede los límites o tiene muestras incompletas");
      var floats=new float[(int)(length/4)];Marshal.Copy(bytes,floats,0,floats.Length);foreach(float value in floats){if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidDataException("IR con muestras no finitas");values.Add(value);}
     }
     if((flags&2)!=0){ended=true;break;}
    }finally{try{if(locked)Check(Method<UnlockBuffer>(buffer,4)(buffer));}finally{Release(ref buffer);Release(ref sample);}}
   }
   if(!ended||values.Count==0)throw new InvalidDataException("El decodificador no completó un IR válido");
   return new IRImport.Audio{Samples=values.ToArray(),Rate=(int)rate,Channels=(int)channels,Bits=32,Format="Audio Windows"};
  }catch(COMException e){throw new InvalidDataException("Windows no puede decodificar este formato de IR (0x"+e.ErrorCode.ToString("X8")+"). Usa WAV, AIFF o FLAC compatible.",e);}
  finally{Release(ref current);Release(ref type);Release(ref reader);if(mf)MFShutdown();if(com)CoUninitialize();}
 }
}
