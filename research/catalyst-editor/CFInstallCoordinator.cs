using System;using System.IO;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;
namespace CFEditor {
interface ICFInstallTransport {
 bool Closed{get;}byte[] OfficialFirmware{get;}
 Task<string> Status(Action<string> log);Task<string> Backup(string folder,Action<string> log);
 int CreateBackup(string folder,string archive);int VerifyBackup(string archive);int BackupProduct(string archive);
 Task<string> Install(string image,string backup,string hash,Func<string,bool> accept,Action<string> log);
}
sealed class CFInstallRecord {
 public int Schema=1,Product;public string Phase,Folder,Candidate,Hash,Backup,Edition;
 public static CFInstallRecord Load(string file){file=Path.GetFullPath(file);if(new FileInfo(file).Length>16384)throw new InvalidDataException("Registro de instalación demasiado grande.");var r=new JavaScriptSerializer().Deserialize<CFInstallRecord>(File.ReadAllText(file));string folder=Path.GetDirectoryName(file);
  if(r==null||r.Schema!=1||!DeviceFamily.Supported(r.Product)||!new[]{"preparing","backed-up","installing","transfer-uncertain","awaiting-reboot","active","failed"}.Contains(r.Phase)||r.Hash==null||r.Hash.Length!=64||!r.Hash.All(Uri.IsHexDigit)||r.Edition==null||r.Edition.Length!=64||!r.Edition.All(Uri.IsHexDigit)||!string.Equals(r.Folder,folder,StringComparison.OrdinalIgnoreCase)||!string.Equals(r.Candidate,Path.Combine(folder,"Catalyst-CF.ctf"),StringComparison.OrdinalIgnoreCase)||!string.Equals(r.Backup,Path.Combine(folder,"presets","Catalyst-verified.catl6a"),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("El registro de instalación no es válido.");return r;
 }
 public void Save(string file){var json=new JavaScriptSerializer().Serialize(this);string temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";Directory.CreateDirectory(Path.GetDirectoryName(file));try{using(var f=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var b=new System.Text.UTF8Encoding(false).GetBytes(json);f.Write(b,0,b.Length);f.Flush(true);}if(File.Exists(file))File.Replace(temporary,file,null);else File.Move(temporary,file);}finally{if(File.Exists(temporary))File.Delete(temporary);}}
}
// A single worker retains the Line 6 lease through every SDK handover. It does
// not reset anything. Unconfirmed shutdown deliberately keeps that worker and
// lease alive, so another operation cannot reuse an uncertain native session.
sealed class CFInstallCoordinator {
 readonly Func<IAssetPortLease> acquire;readonly Func<ICFInstallTransport> transport;readonly Func<CFPreparedPackage,bool> candidateAllowed;readonly Action<byte[]> validate;
 public CFInstallCoordinator(Func<IAssetPortLease> lease,Func<ICFInstallTransport> services,Func<CFPreparedPackage,bool> allow,Action<byte[]> validator){if(lease==null||services==null||allow==null||validator==null)throw new ArgumentNullException();acquire=lease;transport=services;candidateAllowed=allow;validate=validator;}
 static void Need(bool b,string message){if(!b)throw new IOException(message);}static void Closed(string text){Need(text!=null&&text.Contains("shutdown complete"),"El motor no ha confirmado su cierre; la conexión permanece reservada.");}
 static void CopyVerifiedBackup(string source,string target){
  string manifest=source+".verification.json";Need(File.Exists(manifest)&&new FileInfo(manifest).Length<=2000000,"Falta la verificación de la copia existente.");
  byte[] evidence=File.ReadAllBytes(manifest);var data=new JavaScriptSerializer{MaxJsonLength=2000000}.Deserialize<System.Collections.Generic.Dictionary<string,object>>(System.Text.Encoding.UTF8.GetString(evidence));
  var reads=data["reads"] as System.Collections.IList;Need(Convert.ToString(data["schema"])=="CatalystIR.RepeatedPresetReads.v1"&&reads!=null&&reads.Count==12,"La verificación no contiene las lecturas de los 12 presets.");
  Directory.CreateDirectory(Path.GetDirectoryName(target));
  foreach(var entry in reads){var r=(System.Collections.Generic.Dictionary<string,object>)entry;foreach(string key in new[]{"first","second"}){
   string name=Convert.ToString(r[key]);Need(!string.IsNullOrWhiteSpace(name)&&name==Path.GetFileName(name)&&name!="."&&name!="..","Nombre de lectura de respaldo no válido.");
   string file=Path.Combine(Path.GetDirectoryName(source),name);Need(File.Exists(file)&&new FileInfo(file).Length<=2000000,"Falta una lectura verificada de la copia existente.");File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(target),name),File.ReadAllBytes(file));
  }}
  byte[] archive=File.ReadAllBytes(source);File.WriteAllBytes(target,archive);File.WriteAllBytes(target+".verification.json",evidence);
  Need(FirmwareCodec.Hash(archive)==FirmwareCodec.Hash(File.ReadAllBytes(target)),"No se ha conservado íntegra la copia indicada.");
 }
 static bool Pending(CFInstallRecord record){return record.Phase=="installing"||record.Phase=="transfer-uncertain"||record.Phase=="awaiting-reboot";}
 internal static CFInstallRecord FindPending(string root,int product){string backups=Path.Combine(root,"backups");if(!Directory.Exists(backups))return null;var dirs=Directory.EnumerateDirectories(backups).Take(2049).ToArray();Need(dirs.Length<=2048,"Revisa las copias anteriores antes de preparar otra instalación.");foreach(string dir in dirs.OrderByDescending(x=>x,StringComparer.Ordinal)){string file=Path.Combine(dir,"installation.json");if(!File.Exists(file))continue;var old=CFInstallRecord.Load(file);if(old.Product==product&&Pending(old))return old;}return null;}
 static void RefusePending(string root,int product){Need(FindPending(root,product)==null,"Hay una instalación anterior pendiente de comprobar. Confirma primero el arranque del amplificador; no se repetirá la transferencia.");}
 public static CFInstallRecord ConfirmBoot(string journal,int product,string verifiedIdentity){var r=CFInstallRecord.Load(journal);Need(r.Product==product&&Pending(r)&&verifiedIdentity!=null&&verifiedIdentity.StartsWith("CFCX-UID2:",StringComparison.Ordinal)&&verifiedIdentity.Length==91&&verifiedIdentity[26]==':'&&verifiedIdentity.Substring(10,16).All(Uri.IsHexDigit)&&verifiedIdentity.Substring(27).Equals(r.Edition,StringComparison.OrdinalIgnoreCase),"El receptor conectado no corresponde al firmware pendiente.");Need(File.Exists(r.Candidate)&&FirmwareCodec.Hash(File.ReadAllBytes(r.Candidate))==r.Hash,"No se conserva la copia verificada del firmware instalado.");r.Phase="active";r.Save(journal);return r;}
 public Task<CFInstallRecord> Install(CFPreparedPackage package,int product,string root,Action<string> log,string existingVerifiedBackup=null){if(package==null||!DeviceFamily.Supported(product))throw new ArgumentException("Falta el firmware y el modelo comprobados.");validate(package.Bytes);Need(package.Hash==FirmwareCodec.Hash(package.Bytes),"El firmware preparado ha cambiado.");Need(candidateAllowed(package),"Este motor inicial aún no está habilitado para instalarse. Se conserva el firmware actual del amplificador.");
  var done=new TaskCompletionSource<CFInstallRecord>();string data=Path.GetFullPath(root);var worker=new Thread(()=>{
   IAssetPortLease lease=null;ICFInstallTransport service=null;CFInstallRecord record=null;Exception failure=null;bool safe=true,updateStarted=false;Action<string> report=s=>{if(log!=null)try{log(s);}catch{}};
   try{
    lease=acquire();RefusePending(data,product);service=transport();string folder=Path.Combine(data,"backups",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(folder);string journal=Path.Combine(folder,"installation.json");
    record=new CFInstallRecord{Product=product,Phase="preparing",Folder=folder,Candidate=Path.Combine(folder,"Catalyst-CF.ctf"),Hash=package.Hash,Backup=Path.Combine(folder,"presets","Catalyst-verified.catl6a"),Edition=BitConverter.ToString(package.Edition).Replace("-","")};record.Save(journal);
    package.Export(record.Candidate);Need(FirmwareCodec.Hash(File.ReadAllBytes(record.Candidate))==record.Hash,"No se pudo comprobar el firmware guardado.");byte[] official=service.OfficialFirmware;
    Need(official!=null&&FirmwareCodec.Hash(official)=="f9fe2482e561bef8a04128a38af89cadee4f8d06071b06a9053e36c206471ae8","Falta el firmware oficial de recuperación verificado.");File.WriteAllBytes(Path.Combine(folder,"Catalyst-OFICIAL-recuperacion.ctf"),official);
    report("Comprobando la conexión del amplificador…");safe=false;string text=service.Status(report).GetAwaiter().GetResult();Closed(text);safe=service.Closed;
    Need(safe&&text.IndexOf("connected=True; name=Catalyst",StringComparison.OrdinalIgnoreCase)>=0&&text.Contains("Full device synchronization confirmed"),"No se ha confirmado la sincronización del amplificador.");
    if(existingVerifiedBackup==null){
     report("Guardando y verificando los 12 presets…");safe=false;text=service.Backup(Path.GetDirectoryName(record.Backup),report).GetAwaiter().GetResult();Closed(text);safe=service.Closed;Need(safe,"No se ha cerrado el editor antes de actualizar.");
     Need(service.CreateBackup(Path.GetDirectoryName(record.Backup),record.Backup)==12,"La copia no contiene los 12 presets.");
    }else{
     report("Reutilizando la copia verificada indicada, sin leer de nuevo los presets del amplificador.");
     string source=Path.GetFullPath(existingVerifiedBackup);Need(File.Exists(source)&&new FileInfo(source).Length<=16*1024*1024,"No se conserva la copia indicada.");
     Need(service.VerifyBackup(source)==12&&service.BackupProduct(source)==product,"La copia indicada no corresponde al modelo conectado.");
     CopyVerifiedBackup(source,record.Backup);
    }
    Need(service.VerifyBackup(record.Backup)==12&&service.BackupProduct(record.Backup)==product,"La copia no contiene los 12 presets del modelo conectado.");
    validate(File.ReadAllBytes(record.Candidate));Need(FirmwareCodec.Hash(File.ReadAllBytes(record.Candidate))==record.Hash,"El firmware cambió después de la copia.");record.Phase="backed-up";record.Save(journal);
    report("Instalando firmware. Mantén el USB y la alimentación conectados.");record.Phase="installing";record.Save(journal);updateStarted=true;safe=false;
    text=service.Install(record.Candidate,record.Backup,record.Hash,request=>request=="install:"+record.Hash&&FirmwareCodec.Hash(File.ReadAllBytes(record.Candidate))==record.Hash,report).GetAwaiter().GetResult();Closed(text);safe=service.Closed;
    Need(safe&&!text.Contains("UPDATE_FAILED")&&text.Contains("INSTALL_REQUEST_CONFIRMED")&&text.Contains("UPDATE_ENGINE_COMPLETED; VERIFY_DEVICE_BOOT_SEPARATELY"),"La transferencia no tiene confirmación completa. No se repetirá automáticamente.");
    record.Phase="awaiting-reboot";record.Save(journal);report("Apaga y enciende tu amplificador normalmente, sin pulsar botones.");
   }catch(Exception error){if(record!=null)try{record.Phase=updateStarted?(record.Phase=="awaiting-reboot"?"awaiting-reboot":"transfer-uncertain"):"failed";record.Save(Path.Combine(record.Folder,"installation.json"));}catch{}failure=error;}
   finally{safe=service==null||service.Closed;if(safe&&lease!=null)try{lease.Dispose();}catch(Exception cleanup){safe=false;failure=new IOException("No se ha confirmado la liberación de la conexión.",cleanup);}if(!safe&&lease!=null){Line6USBLease.BlockAfterUnconfirmedClose();report("El cierre de la conexión no está confirmado. Se mantiene reservada.");done.TrySetException(failure??new IOException("El motor no ha confirmado su cierre."));new ManualResetEvent(false).WaitOne();}}
   if(failure!=null)done.TrySetException(failure);else done.TrySetResult(record);
  }){IsBackground=true,Name="Catalyst initial firmware owner"};worker.SetApartmentState(ApartmentState.STA);worker.Start();return done.Task;
 }
}
}
