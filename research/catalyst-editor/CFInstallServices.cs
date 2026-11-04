using System;using System.IO;using System.Reflection;using System.Threading.Tasks;
namespace CFEditor {
// Native libraries remain inert until an explicitly authorized installation
// calls Run. Construction verifies the managed component and extracts only its
// pinned data; no child EXE is written/launched, no SDK/USB enumeration occurs.
sealed class CFInstallServices:ICFInstallTransport {
 readonly Assembly component;readonly Type bundle,codec,runtime,gate,profile;readonly Action<byte[]> validate;volatile bool closed=true;
 public bool Closed{get{return closed;}}
 static object Invoke(Type type,string name,params object[] args){try{return type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}catch(TargetInvocationException error){throw error.InnerException??error;}}
 public CFInstallServices(byte[] managedComponent,string expectedHash,string dataRoot,Action<byte[]> validator){if(managedComponent==null||expectedHash==null||FirmwareCodec.Hash(managedComponent)!=expectedHash||validator==null)throw new InvalidDataException("El componente integrado no coincide con la versión comprobada.");validate=validator;component=Assembly.Load(managedComponent);bundle=component.GetType("OneClickBundle",true);codec=component.GetType("FirmwareCodec",true);runtime=component.GetType("OneProcessRuntime",true);gate=component.GetType("CFCandidateGate",true);profile=component.GetType("FamilyProfile",true);Invoke(bundle,"Prepare",Path.GetFullPath(dataRoot));}
 public byte[] OfficialFirmware{get{string path=(string)bundle.GetProperty("Official",BindingFlags.Static|BindingFlags.Public).GetValue(null,null);return File.ReadAllBytes(path);}}
 static string Quote(string path){if(path==null||path.IndexOf('"')>=0||path.IndexOf('\0')>=0)throw new ArgumentException("Ruta incorrecta.");return "\""+Path.GetFullPath(path)+"\"";}
 async Task<string> Run(string name,string command,Action<string> log,Func<string,bool> accept=null){if(!closed)throw new IOException("El motor anterior no ha confirmado su cierre.");NativeBackend.RefuseOtherUSBApps();closed=false;bool receipt=false;
  Action<string> observe=line=>{if(line=="shutdown complete")receipt=true;if(log!=null)log(line);};
  try{var task=(Task<string>)Invoke(runtime,"Run",name,command,observe,accept);return await task.ConfigureAwait(false);}finally{closed=receipt;}
 }
 public Task<string> Status(Action<string> log){return Run("editor","--status",log);}
 public Task<string> Backup(string folder,Action<string> log){return Run("editor","--verified-presets-preserve-live "+Quote(folder),log);}
 public int CreateBackup(string folder,string archive){Invoke(codec,"CreateVerifiedBackup",folder,archive);return VerifyBackup(archive);}
 public int VerifyBackup(string archive){return Convert.ToInt32(Invoke(codec,"VerifyPreservedBackup",archive));}
 public int BackupProduct(string archive){return Convert.ToInt32(Invoke(profile,"BackupProduct",archive));}
 internal Task<string> Recover(string backup,Func<string,bool> accept,Action<string> log){if(VerifyBackup(backup)!=12||BackupProduct(backup)!=2424833)throw new InvalidDataException("La recuperación necesita los 12 presets del Catalyst conectado.");string official=(string)bundle.GetProperty("Official",BindingFlags.Static|BindingFlags.Public).GetValue(null,null);string hash=FirmwareCodec.Hash(File.ReadAllBytes(official));if(hash!="f9fe2482e561bef8a04128a38af89cadee4f8d06071b06a9053e36c206471ae8")throw new InvalidDataException("Firmware oficial no comprobado.");return Run("central","--recover "+Quote(official)+" "+Quote(backup)+" "+hash,log,accept);}
 public async Task<string> Install(string image,string backup,string hash,Func<string,bool> accept,Action<string> log){if(!closed)throw new IOException("No se ha cerrado la operación anterior.");byte[] bytes=File.ReadAllBytes(image);if(FirmwareCodec.Hash(bytes)!=hash)throw new InvalidDataException("El firmware inicial ha cambiado.");validate(bytes);
  var field=gate.GetField("Validator",BindingFlags.Static|BindingFlags.Public);if(field.GetValue(null)!=null)throw new IOException("Ya hay otro firmware inicial preparado.");Func<byte[],bool> check=b=>{if(FirmwareCodec.Hash(b)!=hash)return false;validate(b);return true;};field.SetValue(null,check);
  try{string official=(string)bundle.GetProperty("Official",BindingFlags.Static|BindingFlags.Public).GetValue(null,null);return await Run("central","--install "+Quote(image)+" "+Quote(backup)+" "+hash+" "+Quote(official),log,accept).ConfigureAwait(false);}
  finally{if(closed)field.SetValue(null,null);GC.KeepAlive(check);}
 }
}
}
