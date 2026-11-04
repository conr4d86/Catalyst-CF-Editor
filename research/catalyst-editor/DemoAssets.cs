using System;using System.IO;using System.IO.Compression;using System.Linq;using System.Reflection;using System.Web.Script.Serialization;
namespace CFEditor {
// Prepared with the normal importer once at build time. No paths or files are
// required on the user's PC; reads never initialize a driver or open a port.
static class DemoAssets {
 public const string ResourceHash="5d1c62a5bf38f1989527e83a6d9830d486042a22278d4332e1cc9c44d3e22e56";
 public static bool Available{get{return Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains("initial-cf.demos");}}
 static byte[] Read(ZipArchive zip,string name,int size){var entry=zip.GetEntry(name);if(entry==null||entry.Length!=size)throw new InvalidDataException("Ejemplo integrado incompleto: "+name);using(var source=entry.Open())using(var result=new MemoryStream()){source.CopyTo(result);if(result.Length!=size)throw new InvalidDataException("Ejemplo truncado.");return result.ToArray();}}
 public static AssetPreparation[] Load(){
  byte[] bytes;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("initial-cf.demos"))using(var result=new MemoryStream()){if(stream==null)throw new InvalidDataException("Faltan los ejemplos integrados.");stream.CopyTo(result);bytes=result.ToArray();}
  if(FirmwareCodec.Hash(bytes)!=ResourceHash)throw new InvalidDataException("Los ejemplos no coinciden con la compilación comprobada.");
  using(var zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read)){
   if(zip.Entries.Count!=11)throw new InvalidDataException("Inventario de ejemplos incorrecto.");var slots=new AssetPreparation[5];
   for(byte i=0;i<5;i++){slots[i]=new AssetPreparation{Slot=i,Data=Read(zip,i+".bin",i<3?25088:7772),Info=AssetInfo.Decode(Read(zip,i+".info",184))};CFPackageBuilder.ValidateAsset(slots[i],i);}
   if(!slots[3].Data.SequenceEqual(slots[4].Data)||!slots[3].Info.Encode().SequenceEqual(slots[4].Info.Encode()))throw new InvalidDataException("El NAM de prueba debe estar repetido sin modificaciones.");
   return slots;
  }
 }
 public static int Prepare(string nam,string ir1,string ir2,string ir3,string output){
  output=Path.GetFullPath(output);if(File.Exists(output))throw new IOException("Ya existe el paquete de ejemplos.");Directory.CreateDirectory(Path.GetDirectoryName(output));
  string[] files={ir1,ir2,ir3,nam,nam};var slots=new AssetPreparation[5];
  for(byte i=0;i<5;i++)slots[i]=AssetWorkspace.Import(i,files[i]).InitialPreparation;
  if(!slots[3].Data.SequenceEqual(slots[4].Data))throw new InvalidDataException("Los NAM de prueba no coinciden.");
  using(var file=new FileStream(output,FileMode.CreateNew,FileAccess.Write))using(var zip=new ZipArchive(file,ZipArchiveMode.Create)){
   for(int i=0;i<5;i++){Write(zip,i+".bin",slots[i].Data);Write(zip,i+".info",slots[i].Info.Encode());}
   var manifest=slots.Select(s=>new{slot=s.Slot,name=s.Info.Name,length=s.Data.Length,payload_sha256=FirmwareCodec.Hash(s.Data),metadata_sha256=FirmwareCodec.Hash(s.Info.Encode()),input_db=s.Info.InputDb,output_db=s.Info.OutputDb,low_hz=s.Info.LowHz,high_hz=s.Info.HighHz}).ToArray();
   Write(zip,"manifest.json",System.Text.Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new{schema=1,slots=manifest,source_paths_included=false})));
  }
  File.WriteAllText(output+".sha256",FirmwareCodec.Hash(File.ReadAllBytes(output)));return 0;
 }
 static void Write(ZipArchive zip,string name,byte[] data){var entry=zip.CreateEntry(name,CompressionLevel.Optimal);entry.LastWriteTime=new DateTimeOffset(2000,1,1,0,0,0,TimeSpan.Zero);using(var stream=entry.Open())stream.Write(data,0,data.Length);}
}
}
