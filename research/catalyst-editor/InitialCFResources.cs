using System;using System.IO;using System.Reflection;
namespace CFEditor {
// Embedded immutable resources, all in the one application image. Reading them
// never prepares the vendor runtime, enumerates a port or starts another EXE.
static class InitialCFResources {
 public static bool UnifiedWorkspace{get{return true;}}
 static byte[] Resource(string name,string digest){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))using(var memory=new MemoryStream()){if(stream==null)throw new InvalidDataException("Falta un recurso del firmware inicial.");stream.CopyTo(memory);var bytes=memory.ToArray();if(FirmwareCodec.Hash(bytes)!=digest)throw new InvalidDataException("El recurso del firmware inicial no coincide con la versión comprobada.");return bytes;}}
 public static CFPackageBuilder Builder(){return new CFPackageBuilder(Resource("initial-cf.base",CFPackageBuilder.BaseHash),Resource("initial-cf.segment",CFPackageBuilder.SegmentHash));}
 public static bool InstallAllowed(int product){return Array.IndexOf(InitialCFInfo.ApprovedProducts,product)>=0;}
 public static CFInstallCoordinator Installer(int product,CFPackageBuilder builder){if(builder==null||!DeviceFamily.Supported(product))throw new ArgumentException("Falta el modelo y el motor comprobados.");
  string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CatalystCFEditor","initial-runtime");
  return new CFInstallCoordinator(()=>new Line6USBLease(),()=>new CFInstallServices(Resource("initial-cf.transport",InitialCFInfo.TransportHash),InitialCFInfo.TransportHash,root,builder.Validate),package=>InstallAllowed(product),builder.Validate);
 }
}
}
