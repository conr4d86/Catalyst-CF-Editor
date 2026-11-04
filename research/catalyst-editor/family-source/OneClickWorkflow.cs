using System;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// Persistent update journal. A transfer is not a confirmed boot or an audio test.
class OneClickSession {
 public string folder,candidate,candidateHash,recipe,backup,phase,templateHash;
 public int activationSlot;
 public bool hasNAM;
 public bool bootRecallSaved;
 public bool rebootDisconnected;
 public static string Recipe(string irKey,double db,bool cuts,double low,double high){
  return irKey+"|"+db.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+cuts+"|"+low.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+high.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
 }
 public void Save(string path){
  Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";
  File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this));
  if(File.Exists(path))File.Replace(temp,path,path+".previous");else File.Move(temp,path);
 }
 public static OneClickSession Load(string path){
  if(!File.Exists(path))return null;if(new FileInfo(path).Length>100000)throw new InvalidDataException("Registro de instalación incorrecto");
  return new JavaScriptSerializer().Deserialize<OneClickSession>(File.ReadAllText(path));
 }
 public static void CheckSDK(string text){
  if(text.IndexOf("connected=True; name=Catalyst",StringComparison.OrdinalIgnoreCase)<0||!text.Contains("Full device synchronization confirmed")||!text.Contains("shutdown complete"))
   throw new InvalidOperationException("No se ha confirmado la comunicación con el Catalyst. Cierra Catalyst Edit y vuelve a intentarlo.");
 }
 public static bool MatchesInstallRequest(string request,string expectedHash){
  return expectedHash!=null&&expectedHash.Length==64&&System.Text.RegularExpressions.Regex.IsMatch(expectedHash,"^[a-f0-9]{64}$")&&request=="install:"+expectedHash;
 }
 public void ConfirmTransfer(string output){
  if(output.Contains("UPDATE_FAILED")||!output.Contains("INSTALL_REQUEST_CONFIRMED")||!output.Contains("UPDATE_ENGINE_COMPLETED; VERIFY_DEVICE_BOOT_SEPARATELY")||!output.Contains("shutdown complete"))
   throw new InvalidOperationException("La transferencia no tiene confirmación completa. Conserva el registro y no pruebes el IR todavía.");
  phase="awaiting-reboot";rebootDisconnected=false;
 }
 public bool ObserveUSB(int count){
  if(phase!="awaiting-reboot")return false;
  if(count==0)rebootDisconnected=true;
  return rebootDisconnected&&count==1;
 }
 public void ConfirmActivation(string output){
#if AUTOBOOT
  if(!output.Contains("IR_BOOT_RECALL_SAVED index=")||!output.Contains("shutdown complete"))throw new InvalidOperationException("Falta confirmar el guardado del IR para el próximo encendido");
  bootRecallSaved=true;
#else
  if(!output.Contains("IR virtual cabinet 112 active in edit buffer; no preset stored")||!output.Contains("shutdown complete"))throw new InvalidOperationException("Falta confirmar la activación del IR");
#endif
  phase="active";
 }
}
