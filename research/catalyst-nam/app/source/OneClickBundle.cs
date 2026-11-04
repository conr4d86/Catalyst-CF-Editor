using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;

// Private single-file edition. The public source package carries no vendor data.
static class OneClickBundle {
 public const string OfficialHash="f9fe2482e561bef8a04128a38af89cadee4f8d06071b06a9053e36c206471ae8";
#if MULTI_IR
 public const string PatchHash="82161d5b805f3c89da5ae0172075e0cc3b96f71fb07e40053240063fda0788fa";
#elif AUTOBOOT
 public const string PatchHash="508602ac8bf2fc95ca64198e825d4ed8ed93219c8295eca3f7df09d166ee6261";
#else
 public const string PatchHash="3032fed1c4e4b05f5a3cfc4ebc4914da688ca6ec29bc075d4c1fc5e0767dfc2b";
#endif
 public class Entry {public string path{get;set;}public string sha256{get;set;}}
 public static string Root;
 public static string Official {get{return Path.Combine(Root,"official.ctf");}}
 public static string DefaultData {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CatalystIR");}}
 public static void Prepare(string dataRoot){
  byte[] packed;
  using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("private-bundle.zip"))using(var m=new MemoryStream()){
   if(input==null)throw new InvalidDataException("Falta el contenido integrado");input.CopyTo(m);packed=m.ToArray();
  }
  // Content-addressed directories allow updates without replacing active helpers.
  Root=Path.Combine(Path.GetFullPath(dataRoot),"runtime",FirmwareCodec.Hash(packed).Substring(0,24));
  Directory.CreateDirectory(Root);
  using(var archive=new ZipArchive(new MemoryStream(packed),ZipArchiveMode.Read)){
   var manifest=archive.GetEntry("manifest.json");if(manifest==null)throw new InvalidDataException("Falta el inventario integrado");
   Entry[] entries;using(var reader=new StreamReader(manifest.Open()))entries=new JavaScriptSerializer().Deserialize<Entry[]>(reader.ReadToEnd());
   var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var e in entries){
    string target=Path.GetFullPath(Path.Combine(Root,e.path.Replace('/',Path.DirectorySeparatorChar)));
    if(!target.StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!names.Add(target))throw new InvalidDataException("Ruta integrada incorrecta");
    var entry=archive.GetEntry(e.path);if(entry==null)throw new InvalidDataException("Falta un componente: "+e.path);
    if(File.Exists(target)&&FirmwareCodec.Hash(File.ReadAllBytes(target))==e.sha256)continue;
    byte[] bytes;using(var source=entry.Open())using(var m=new MemoryStream()){source.CopyTo(m);bytes=m.ToArray();}
    if(FirmwareCodec.Hash(bytes)!=e.sha256)throw new InvalidDataException("Componente integrado alterado");
    Directory.CreateDirectory(Path.GetDirectoryName(target));string temp=Path.Combine(Path.GetDirectoryName(target),"~"+Guid.NewGuid().ToString("N").Substring(0,8)+".tmp");
    File.WriteAllBytes(temp,bytes);if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);
   }
  }
  if(FirmwareCodec.Hash(File.ReadAllBytes(Official))!=OfficialHash)throw new InvalidDataException("Firmware oficial integrado incorrecto");
 }
 public static byte[] Template(){
  using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("patch.json"))using(var m=new MemoryStream()){
   if(input==null)throw new InvalidDataException("Falta el parche");input.CopyTo(m);byte[] patch=m.ToArray();
   if(FirmwareCodec.Hash(patch)!=PatchHash)throw new InvalidDataException("Parche integrado incorrecto");
   return FirmwarePatch.Build(File.ReadAllBytes(Official),patch);
  }
 }
}

// Enumerating MIDI capabilities does not open a port or send a device command.
// The native engine subsequently verifies the exact product before any update.
static class AmplifierUSB {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Caps {
  public ushort manufacturer,product;public uint version;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string name;public uint support;
 }
 [DllImport("winmm.dll")]static extern uint midiInGetNumDevs();
 [DllImport("winmm.dll",CharSet=CharSet.Unicode)]static extern uint midiInGetDevCaps(UIntPtr id,out Caps caps,uint size);
 public static int Count(){int result=0;for(uint i=0;i<midiInGetNumDevs();i++){
  Caps c;if(midiInGetDevCaps(new UIntPtr(i),out c,(uint)Marshal.SizeOf(typeof(Caps)))==0&&c.name!=null&&c.name.IndexOf("Catalyst",StringComparison.OrdinalIgnoreCase)>=0)result++;
 }return result;}
}
