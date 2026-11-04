using System;using System.IO;using System.Linq;using System.Text;using System.Text.RegularExpressions;using System.Runtime.InteropServices;using System.Collections.Generic;
namespace CFEditor {
// Immutable discovery evidence. These are private runtime identities, never
// display text, filenames, GitHub metadata or a name-based port selection.
sealed class AssetMidiEndpoint {
 public readonly uint Index;public readonly bool Input,UniqueHardwareID;public readonly string Name,Interface,PhysicalParent;
 public AssetMidiEndpoint(uint index,bool input,string name,string path,string parent,bool uniqueHardwareID){Index=index;Input=input;Name=name;Interface=path;PhysicalParent=parent;UniqueHardwareID=uniqueHardwareID;}
}
sealed class AssetPortBinding {
 public readonly uint Product;public readonly AssetMidiEndpoint Input,Output;public readonly string PhysicalIdentity;public readonly bool RequireUnique;
 AssetPortBinding(uint product,AssetMidiEndpoint input,AssetMidiEndpoint output,bool unique){Product=product;Input=input;Output=output;RequireUnique=unique;PhysicalIdentity=(unique?"CFCX-PNP-UNIQUE-1:":"CFCX-PNP-CONNECTION-1:")+input.PhysicalParent.ToUpperInvariant();}
 static bool Valid(AssetMidiEndpoint e,bool unique){return e!=null&&(!unique||e.UniqueHardwareID)&&e.Name!=null&&e.Name.Length<=128&&e.Name.IndexOf("Catalyst",StringComparison.OrdinalIgnoreCase)>=0&&e.Interface!=null&&e.Interface.Length>=8&&e.Interface.Length<=2048&&e.Interface.IndexOf('\0')<0&&e.PhysicalParent!=null&&e.PhysicalParent.Length<=240&&Regex.IsMatch(e.PhysicalParent,@"\AUSB\\VID_0E41&PID_[0-9A-F]{4}\\[^\\\x00-\x1f]{1,200}\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);}
 // Discovery is explicit and its native implementation is not called by this
 // selector or its tests. Ambiguous devices/ports are refused, never port 0.
 public static AssetPortBinding Select(uint product,IEnumerable<AssetMidiEndpoint> endpoints,bool requireUnique=true){
  if(!DeviceFamily.Supported((int)product))throw new NotSupportedException("El producto no es un Catalyst compatible.");
  if(endpoints==null)throw new ArgumentNullException("endpoints");var all=endpoints.Take(513).ToArray();
  if(all.Length>512)throw new InvalidDataException("Inventario MIDI demasiado grande.");
  var candidates=all.Where(e=>Valid(e,requireUnique)).GroupBy(e=>e.PhysicalParent,StringComparer.OrdinalIgnoreCase).Where(g=>g.Any(e=>e.Input)&&g.Any(e=>!e.Input)).ToArray();
  if(candidates.Length==0)throw new IOException("No se ha podido verificar la identidad física de los puertos del Catalyst.");
  if(candidates.Length!=1)throw new IOException("Hay varios Catalyst conectados. Deja conectado solo el que quieras editar.");
  var inputs=candidates[0].Where(e=>e.Input).ToArray();var outputs=candidates[0].Where(e=>!e.Input).ToArray();
  if(inputs.Length!=1||outputs.Length!=1)throw new IOException("El controlador expone varios puertos del mismo Catalyst; no se elegirá uno automáticamente.");
  return new AssetPortBinding(product,inputs[0],outputs[0],requireUnique);
 }
 public bool Matches(bool input,string currentInterface,string currentParent){var expected=input?Input:Output;return string.Equals(expected.Interface,currentInterface,StringComparison.OrdinalIgnoreCase)&&string.Equals(expected.PhysicalParent,currentParent,StringComparison.OrdinalIgnoreCase);}
 // System-intercepted WinMM identity messages take a device INDEX cast to a
 // handle, not an open HMIDI handle. Resolve and check the actual handle first.
 internal void RevalidateHandle(bool input,IntPtr handle,Func<bool,IntPtr,uint> getIndex,Func<bool,uint,string> getInterface,Func<string,bool,string> getParent){
  if(handle==IntPtr.Zero)throw new IOException("El puerto MIDI ya no está abierto.");uint index=getIndex(input,handle);var expected=input?Input:Output;
  if(index!=expected.Index)throw new IOException("Ha cambiado el índice del puerto MIDI; no se enviarán datos.");
  string path=getInterface(input,index);if(!Matches(input,path,getParent(path,RequireUnique)))throw new IOException("La identidad del puerto ha cambiado; no se enviarán datos.");
 }
}
// P/Invokes remain inert until an explicit Discover/Revalidate call. No static
// initializer, timer, constructor, preview or import probes MIDI/USB.
static class AssetPortDiscovery {
 [StructLayout(LayoutKind.Sequential)]struct PropertyKey{public Guid Format;public uint ID;public PropertyKey(Guid f,uint id){Format=f;ID=id;}}
 [DllImport("winmm.dll")]static extern uint midiInGetNumDevs();
 [DllImport("winmm.dll")]static extern uint midiOutGetNumDevs();
 [DllImport("winmm.dll",CharSet=CharSet.Unicode,EntryPoint="midiInGetDevCapsW")]static extern uint InCaps(UIntPtr id,[Out]byte[] data,uint size);
 [DllImport("winmm.dll",CharSet=CharSet.Unicode,EntryPoint="midiOutGetDevCapsW")]static extern uint OutCaps(UIntPtr id,[Out]byte[] data,uint size);
 [DllImport("winmm.dll",EntryPoint="midiInMessage")]static extern uint InMessage(IntPtr id,uint message,IntPtr data,UIntPtr size);
 [DllImport("winmm.dll",EntryPoint="midiOutMessage")]static extern uint OutMessage(IntPtr id,uint message,IntPtr data,UIntPtr size);
 [DllImport("winmm.dll",EntryPoint="midiInGetID")]static extern uint InID(IntPtr handle,out uint index);
 [DllImport("winmm.dll",EntryPoint="midiOutGetID")]static extern uint OutID(IntPtr handle,out uint index);
 [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)]static extern uint CM_Get_Device_Interface_PropertyW(string path,ref PropertyKey key,out uint type,[Out]byte[] buffer,ref uint size,uint flags);
 [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)]static extern uint CM_Locate_DevNodeW(out uint node,string instance,uint flags);
 [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)]static extern uint CM_Get_Device_IDW(uint node,StringBuilder buffer,uint characters,uint flags);
 [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)]static extern uint CM_Get_DevNode_Registry_PropertyW(uint node,uint property,out uint type,[Out]byte[] data,ref uint size,uint flags);
 [DllImport("cfgmgr32.dll")]static extern uint CM_Get_Parent(out uint parent,uint node,uint flags);
 static string Unicode(byte[] b,int size){if(size<2||size>b.Length||(size&1)!=0)throw new InvalidDataException("Identidad MIDI no válida.");int end=-1;for(int i=0;i<size;i+=2)if(b[i]==0&&b[i+1]==0){end=i;break;}if(end<0)throw new InvalidDataException("Identidad MIDI truncada.");return new UnicodeEncoding(false,false,true).GetString(b,0,end);}
 static uint Message(bool input,IntPtr id,uint op,IntPtr data,UIntPtr size){return input?InMessage(id,op,data,size):OutMessage(id,op,data,size);}
 internal static string Interface(bool input,IntPtr id){
  IntPtr sizePointer=Marshal.AllocHGlobal(4),buffer=IntPtr.Zero;
  try{Marshal.WriteInt32(sizePointer,0);if(Message(input,id,0x80d,sizePointer,UIntPtr.Zero)!=0)throw new IOException("Windows no proporciona la identidad del puerto MIDI.");int size=Marshal.ReadInt32(sizePointer);if(size<2||size>4096||(size&1)!=0)throw new IOException("Identidad del puerto MIDI no disponible.");
   buffer=Marshal.AllocHGlobal(size);var bytes=new byte[size];Marshal.Copy(bytes,0,buffer,size);
   if(Message(input,id,0x80c,buffer,new UIntPtr((uint)size))!=0)throw new IOException("Ha cambiado el puerto MIDI.");Marshal.Copy(buffer,bytes,0,size);string result=Unicode(bytes,size);if(result.Length==0)throw new IOException("El controlador no identifica el puerto MIDI.");return result;
  }finally{if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);Marshal.FreeHGlobal(sizePointer);}
 }
 internal static string Parent(string path,bool requireUnique=true){
  var key=new PropertyKey(new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"),256);var data=new byte[4096];uint bytes=(uint)data.Length,type;
  if(CM_Get_Device_Interface_PropertyW(path,ref key,out type,data,ref bytes,0)!=0||type!=0x12)throw new IOException("No se ha podido asociar el puerto a su dispositivo físico.");
  string instance=Unicode(data,checked((int)bytes));uint node;if(CM_Locate_DevNodeW(out node,instance,0)!=0)throw new IOException("El dispositivo ha cambiado durante la conexión.");
  for(int depth=0;depth<16;depth++){
   var id=new StringBuilder(512);if(CM_Get_Device_IDW(node,id,(uint)id.Capacity,0)!=0)break;
   string value=id.ToString();if(Regex.IsMatch(value,@"\AUSB\\VID_0E41&PID_[0-9A-F]{4}\\[^\\\x00-\x1f]{1,200}\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)){
    // Without CM_DEVCAP_UNIQUEID this may identify a USB LOCATION, reused by
    // another unit. It must not key a durable transfer-recovery journal.
    var capability=new byte[4];uint size=4,kind;
    if(requireUnique&&(CM_Get_DevNode_Registry_PropertyW(node,0x10,out kind,capability,ref size,0)!=0||kind!=4||size!=4||(BitConverter.ToUInt32(capability,0)&0x10)==0))throw new IOException("Windows no proporciona una identidad única de hardware del Catalyst; la carga persistente permanece desactivada.");
    return value.ToUpperInvariant();
   }
   uint next;if(CM_Get_Parent(out next,node,0)!=0||next==node)break;node=next;
  }throw new IOException("El puerto no pertenece a un dispositivo USB Line 6 identificado.");
 }
 public static AssetPortBinding Discover(uint product,bool requireUnique=true){
  var endpoints=new List<AssetMidiEndpoint>();
  foreach(bool input in new[]{true,false}){uint count=input?midiInGetNumDevs():midiOutGetNumDevs();if(count>256)throw new IOException("Inventario MIDI no válido.");
   for(uint index=0;index<count;index++){
    var caps=new byte[input?76:84];uint rc=input?InCaps(new UIntPtr(index),caps,(uint)caps.Length):OutCaps(new UIntPtr(index),caps,(uint)caps.Length);if(rc!=0)continue;
    string name;try{name=Unicode(caps.Skip(8).Take(64).ToArray(),64);}catch(InvalidDataException){continue;}if(name.IndexOf("Catalyst",StringComparison.OrdinalIgnoreCase)<0)continue;
    // A missing/virtual/ambiguous identity fails closed. It is never replaced
    // by a friendly-name match or by the first port that happens to open.
    string path=Interface(input,new IntPtr((long)index));endpoints.Add(new AssetMidiEndpoint(index,input,name,path,Parent(path,requireUnique),requireUnique));
   }
  }return AssetPortBinding.Select(product,endpoints,requireUnique);
 }
 static uint IndexForHandle(bool input,IntPtr handle){uint index;uint rc=input?InID(handle,out index):OutID(handle,out index);if(rc!=0)throw new IOException("Windows no ha podido verificar el puerto MIDI abierto ("+rc+").");return index;}
 public static void Revalidate(AssetPortBinding binding,bool input,IntPtr handle){binding.RevalidateHandle(input,handle,IndexForHandle,(direction,index)=>Interface(direction,new IntPtr((long)index)),Parent);}
}
}
