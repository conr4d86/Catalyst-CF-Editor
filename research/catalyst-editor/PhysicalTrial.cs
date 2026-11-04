using System;using System.IO;using System.Reflection;using System.Threading.Tasks;using System.Web.Script.Serialization;
namespace CFEditor {
// Explicit private trial entry, never invoked by the normal UI. An exact-image
// trial does not grant a product release approval or alter ApprovedProducts.
static class PhysicalTrial {
 static void Finish(CFInstallServices service,Line6USBLease lease,Action<string> log){if(lease==null)return;if(service==null||service.Closed){lease.Dispose();return;}Line6USBLease.BlockAfterUnconfirmedClose();log("Cierre sin confirmar. Proceso retenido; no abrir otra conexión.");new System.Threading.ManualResetEvent(false).WaitOne();}
 static byte[] Resource(string name,string hash){using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))using(var m=new MemoryStream()){if(s==null)throw new InvalidDataException("Falta el componente de prueba.");s.CopyTo(m);var b=m.ToArray();if(FirmwareCodec.Hash(b)!=hash)throw new InvalidDataException("Componente de prueba alterado.");return b;}}
 internal static CFInstallServices Services(string root,Action<byte[]> validate){return new CFInstallServices(Resource("initial-cf.transport",InitialCFInfo.TransportHash),InitialCFInfo.TransportHash,Path.Combine(root,"runtime"),validate);}
 internal static int Backup(string root){
  root=Path.GetFullPath(root);Directory.CreateDirectory(root);string log=Path.Combine(root,"backup.log");Action<string> report=s=>File.AppendAllText(log,s+Environment.NewLine);CFInstallServices service=null;Line6USBLease lease=null;
  try{lease=new Line6USBLease();service=Services(root,b=>{});string status=service.Status(report).GetAwaiter().GetResult();if(!service.Closed||!status.Contains("Full device synchronization confirmed"))throw new IOException("Falta sincronización confirmada.");string folder=Path.Combine(root,"presets");service.Backup(folder,report).GetAwaiter().GetResult();string archive=Path.Combine(folder,"Catalyst-verified.catl6a");if(!service.Closed||service.CreateBackup(folder,archive)!=12||service.BackupProduct(archive)!=2424833)throw new IOException("Copia incompleta o modelo diferente.");File.WriteAllBytes(Path.Combine(root,"Catalyst-OFICIAL-recuperacion.ctf"),service.OfficialFirmware);File.WriteAllText(Path.Combine(root,"BACKUP.json"),new JavaScriptSerializer().Serialize(new{verified_presets=12,product=2424833,archive,firmware_installed=false}));return 0;}catch(Exception e){report(e.ToString());return 1;}finally{Finish(service,lease,report);}
 }
 internal static int Recover(string backup,string root){root=Path.GetFullPath(root);Directory.CreateDirectory(root);string log=Path.Combine(root,"recovery.log");Action<string> report=s=>File.AppendAllText(log,s+Environment.NewLine);CFInstallServices service=null;Line6USBLease lease=null;
  try{lease=new Line6USBLease();service=Services(root,b=>{});string hash=FirmwareCodec.Hash(service.OfficialFirmware);string result=service.Recover(backup,r=>r=="install:"+hash,report).GetAwaiter().GetResult();return service.Closed&&result.Contains("UPDATE_ENGINE_COMPLETED; VERIFY_DEVICE_BOOT_SEPARATELY")?0:1;}catch(Exception e){report(e.ToString());return 1;}finally{Finish(service,lease,report);}
 }
 internal static int Install(string candidate,string expected,string root,string existingVerifiedBackup=null){
  root=Path.GetFullPath(root);Directory.CreateDirectory(root);string log=Path.Combine(root,"activity.log");Action<string> report=line=>File.AppendAllText(log,DateTime.Now.ToString("HH:mm:ss ")+line+Environment.NewLine);
  try{var builder=InitialCFResources.Builder();var package=builder.ReadPrepared(File.ReadAllBytes(candidate));if(expected!=package.Hash)throw new InvalidDataException("El firmware no coincide con la prueba solicitada.");
   var installer=new CFInstallCoordinator(()=>new Line6USBLease(),()=>Services(root,builder.Validate),p=>p.Hash==expected,builder.Validate);
   var record=installer.Install(package,2424833,root,report,existingVerifiedBackup).GetAwaiter().GetResult();File.WriteAllText(Path.Combine(root,"RESULT.json"),new JavaScriptSerializer().Serialize(new{transfer_confirmed=true,boot_verified=false,journal=Path.Combine(record.Folder,"installation.json"),candidate_sha256=record.Hash,reused_existing_backup=existingVerifiedBackup!=null}));return 0;
  }catch(Exception e){report(e.ToString());File.WriteAllText(Path.Combine(root,"ERROR.txt"),e.ToString());if(Line6USBLease.UnconfirmedClose)new System.Threading.ManualResetEvent(false).WaitOne();return 1;}
 }
}
}
