using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Threading;using System.Threading.Tasks;
namespace CFEditor {
// Scoped imports in one pinned module of THIS process, installed before SDK
// initialize. No export patches, remote processes, device reset or extra open.
// Original callback arguments, messages, buffers and return codes are preserved.
sealed class SdkMidiTap {
 [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate uint Open(out IntPtr handle,uint id,UIntPtr callback,UIntPtr instance,uint flags);
 [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate uint Close(IntPtr handle);
 [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate void Callback(IntPtr handle,uint message,UIntPtr instance,UIntPtr p1,UIntPtr p2);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
 [DllImport("kernel32.dll",CharSet=CharSet.Ansi)]static extern IntPtr GetProcAddress(IntPtr module,string name);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern uint GetModuleFileName(IntPtr module,System.Text.StringBuilder name,int length);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool VirtualProtect(IntPtr address,UIntPtr length,uint protection,out uint prior);
 sealed class Import {public string Name;public IntPtr Slot,Original,Replacement;public Delegate Root;public bool Patched;}
 sealed class Port {public bool Input,Closed,Opened,CloseCallback;public uint Index;public IntPtr Handle;public Callback Original,Wrapper;}
 static readonly object global=new object();static SdkMidiTap active;
 readonly object sync=new object();readonly List<Import> imports=new List<Import>();readonly List<Port> ports=new List<Port>();readonly IntPtr module;
 readonly Open inOpen,outOpen;readonly Close inClose,outClose;readonly SdkMidiBorrow channel;
 int callbacks;bool bound;Exception callbackError;
 SdkMidiTap(IntPtr sdk){module=sdk;FindImports();inOpen=(Open)Marshal.GetDelegateForFunctionPointer(Find("midiInOpen").Original,typeof(Open));outOpen=(Open)Marshal.GetDelegateForFunctionPointer(Find("midiOutOpen").Original,typeof(Open));inClose=(Close)Marshal.GetDelegateForFunctionPointer(Find("midiInClose").Original,typeof(Close));outClose=(Close)Marshal.GetDelegateForFunctionPointer(Find("midiOutClose").Original,typeof(Close));
  channel=new SdkMidiBorrow(new WinmmAssetPortDriver());Set("midiInOpen",new Open(OpenInput));Set("midiOutOpen",new Open(OpenOutput));Set("midiInClose",new Close(CloseInput));Set("midiOutClose",new Close(CloseOutput));
 }
 Import Find(string name){return imports.Single(x=>x.Name==name);}
 void Set(string name,Delegate root){var i=Find(name);i.Root=root;i.Replacement=Marshal.GetFunctionPointerForDelegate(root);}
 static int U16(IntPtr p,int n){return (ushort)Marshal.ReadInt16(p,n);}static int I32(IntPtr p,int n){return Marshal.ReadInt32(p,n);}
 static string Name(IntPtr m,int offset,int image){if(offset<1||offset>image-128)throw new InvalidDataException("Import SDK fuera de límites.");for(int i=0;i<128;i++)if(Marshal.ReadByte(m,offset+i)==0)return Marshal.PtrToStringAnsi(IntPtr.Add(m,offset),i);throw new InvalidDataException("Nombre de import SDK truncado.");}
 void FindImports(){if(U16(module,0)!=0x5a4d)throw new InvalidDataException("SDK sin cabecera MZ.");int pe=I32(module,0x3c);if(pe<64||pe>4096||I32(module,pe)!=0x4550||U16(module,pe+24)!=0x10b)throw new InvalidDataException("SDK PE32 no compatible.");int size=I32(module,pe+24+56),directory=I32(module,pe+24+104);if(size<4096||size>0x4000000||directory<1||directory>size-20)throw new InvalidDataException("Imports SDK no válidos.");
  var wanted=new[]{"midiInOpen","midiOutOpen","midiInClose","midiOutClose"};IntPtr winmm=GetModuleHandle("winmm.dll");if(winmm==IntPtr.Zero)throw new IOException("El SDK no ha cargado WinMM.");
  for(int off=directory;off<=size-20;off+=20){int names=I32(module,off),dll=I32(module,off+12),iat=I32(module,off+16);if(dll==0)break;if(!Name(module,dll,size).Equals("winmm.dll",StringComparison.OrdinalIgnoreCase))continue;if(names<1||iat<1)throw new InvalidDataException("Imports SDK sin nombres.");
   for(int n=0;n<4096;n++){long a=(long)names+n*4,b=(long)iat+n*4;if(a>size-4||b>size-4)throw new InvalidDataException("Tabla SDK fuera de límites.");int named=I32(module,(int)a);if(named==0)break;if(named<0)continue;string name=Name(module,named+2,size);if(!wanted.Contains(name))continue;IntPtr slot=IntPtr.Add(module,(int)b),original=Marshal.ReadIntPtr(slot);if(original!=GetProcAddress(winmm,name)||imports.Any(x=>x.Name==name))throw new IOException("El import de comunicación ya ha sido modificado.");imports.Add(new Import{Name=name,Slot=slot,Original=original});}
  }if(imports.Count!=4)throw new IOException("SDK sin los cuatro imports revisados.");
 }
 static void Write(Import i,bool install){IntPtr expected=install?i.Original:i.Replacement,next=install?i.Replacement:i.Original;if(Marshal.ReadIntPtr(i.Slot)!=expected)throw new IOException("Ha cambiado el propietario del import SDK.");uint old;if(!VirtualProtect(i.Slot,new UIntPtr(4),4,out old))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());try{Marshal.WriteIntPtr(i.Slot,next);i.Patched=install;}finally{uint ignored;if(!VirtualProtect(i.Slot,new UIntPtr(4),old,out ignored))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}}
 public static bool HasOwner{get{lock(global)return active!=null;}}
 public static SdkMidiTap Install(string sdkPath){lock(global){if(active!=null)throw new IOException("El SDK ya tiene un propietario de controles.");const string hash="6d5325b93e3f8b973525cbf97cba38606c5af56aab48a07e8a9e058f6b23d1aa";if(IntPtr.Size!=4||FirmwareCodec.Hash(File.ReadAllBytes(sdkPath))!=hash)throw new NotSupportedException("Versión SDK no revisada para controles CF.");IntPtr sdk=GetModuleHandle("APIV3EngineDLL.dll");if(sdk==IntPtr.Zero)throw new IOException("SDK no cargado.");var loaded=new System.Text.StringBuilder(32768);uint count=GetModuleFileName(sdk,loaded,loaded.Capacity);if(count==0||count>=loaded.Capacity||FirmwareCodec.Hash(File.ReadAllBytes(loaded.ToString()))!=hash)throw new IOException("El SDK cargado no coincide con la versión revisada.");var tap=new SdkMidiTap(sdk);active=tap;
   try{foreach(var i in tap.imports)Write(i,true);return tap;}catch{foreach(var i in tap.imports.AsEnumerable().Reverse())if(i.Patched)try{Write(i,false);}catch{}if(tap.imports.All(x=>!x.Patched)){tap.channel.Detach().GetAwaiter().GetResult();active=null;}throw;}
 }}
 uint OpenPort(bool input,out IntPtr handle,uint index,UIntPtr callback,UIntPtr instance,uint flags){var original=input?inOpen:outOpen;IntPtr expected=IntPtr.Add(module,input?0x9f220:0x9fdb0);
  if(flags!=0x30000||callback.ToUInt64()!=unchecked((uint)expected.ToInt32()))return original(out handle,index,callback,instance,flags);
  var p=new Port{Input=input,Index=index,Original=(Callback)Marshal.GetDelegateForFunctionPointer(expected,typeof(Callback))};p.Wrapper=(h,m,u,a,b)=>{
   Interlocked.Increment(ref callbacks);try{
    if(input&&m==0x3c4)channel.InputLong(h,new IntPtr(unchecked((int)a.ToUInt32())));
    p.Original(h,m,u,a,b);
    if(!input&&m==0x3c9)channel.OutputReturned(h,new IntPtr(unchecked((int)a.ToUInt32())));
    if(m==(input?0x3c2u:0x3c8u))lock(sync)p.CloseCallback=true;
   }catch(Exception error){lock(sync)callbackError=error;}finally{Interlocked.Decrement(ref callbacks);}
  };lock(sync)ports.Add(p);uint code=original(out handle,index,new UIntPtr(unchecked((uint)Marshal.GetFunctionPointerForDelegate(p.Wrapper).ToInt32())),instance,flags);lock(sync){p.Handle=handle;p.Closed=code!=0;p.Opened=code==0;}return code;
 }
 uint OpenInput(out IntPtr h,uint i,UIntPtr c,UIntPtr u,uint f){return OpenPort(true,out h,i,c,u,f);}uint OpenOutput(out IntPtr h,uint i,UIntPtr c,UIntPtr u,uint f){return OpenPort(false,out h,i,c,u,f);}
 uint ClosePort(bool input,IntPtr h){uint code=(input?inClose:outClose)(h);if(code==0){lock(sync)foreach(var p in ports)if(p.Input==input&&p.Handle==h)p.Closed=true;channel.HandleClosed(input,h);}return code;}
 uint CloseInput(IntPtr h){return ClosePort(true,h);}uint CloseOutput(IntPtr h){return ClosePort(false,h);}
 public IAssetMidiChannel Bind(uint product){lock(sync){if(bound||callbackError!=null)throw new IOException("El canal del SDK no está disponible.",callbackError);var binding=AssetPortDiscovery.Discover(product,false);var live=ports.Where(p=>!p.Closed).ToArray();var ins=live.Where(p=>p.Input&&p.Index==binding.Input.Index).ToArray();var outs=live.Where(p=>!p.Input&&p.Index==binding.Output.Index).ToArray();if(ins.Length!=1||outs.Length!=1)throw new IOException("El SDK no ha abierto los puertos Catalyst esperados.");channel.Bind(binding,ins[0].Handle,outs[0].Handle);bound=true;return channel;}}
 public async Task Remove(){await channel.Detach().ConfigureAwait(false);for(int n=0;n<100;n++){bool done;lock(sync)done=ports.All(p=>!p.Opened||p.Closed&&p.CloseCallback)&&callbacks==0;if(done)break;await Task.Delay(10).ConfigureAwait(false);}lock(global){lock(sync){if(ports.Any(p=>p.Opened&&(!p.Closed||!p.CloseCallback))||callbacks!=0)throw new IOException("El SDK aún conserva sus callbacks de comunicación.");foreach(var i in imports.AsEnumerable().Reverse())if(i.Patched)Write(i,false);if(active==this)active=null;}}}
}
}
