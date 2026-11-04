using System;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

static class TemplateSource {
 public class Settings {public string officialFirmware{get;set;}public string lastIR{get;set;}public string lastBackup{get;set;}}
 public static string Home {get{return AppDomain.CurrentDomain.BaseDirectory;}}
 public static string SettingsPath {get{return Path.Combine(Home,"settings","app.json");}}
 public static Settings LoadSettings(){
  if(!File.Exists(SettingsPath))return new Settings();
  if(new FileInfo(SettingsPath).Length>100000)throw new InvalidDataException("Configuración demasiado grande");
  return new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(SettingsPath))??new Settings();
 }
 public static void SaveSettings(Settings settings){
  Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
  string temporary=SettingsPath+".tmp";File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(settings),new System.Text.UTF8Encoding(false));
  if(File.Exists(SettingsPath))File.Replace(temporary,SettingsPath,SettingsPath+".previous");else File.Move(temporary,SettingsPath);
 }
 public static string FindOfficial(){
  var settings=LoadSettings();if(!string.IsNullOrEmpty(settings.officialFirmware)&&File.Exists(settings.officialFirmware))return settings.officialFirmware;
  foreach(string path in new[]{
   Path.GetFullPath(Path.Combine(Home,"..","Backup-2026-10-31","Catalyst-2.02-OFICIAL-recuperacion.ctf")),
   Path.GetFullPath(Path.Combine(Home,"..","originals","Catalyst_60_100_200_v2.02.0-rc9_0x77f2d40.ctf"))})if(File.Exists(path))return path;
  return null;
 }
 public static byte[] Load(string official=null){
  official=official??FindOfficial();if(string.IsNullOrEmpty(official))throw new InvalidDataException("Selecciona el firmware oficial 2.02 con el botón Firmware oficial");
  using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("patch.json"))using(var memory=new MemoryStream()){
   if(stream==null)throw new InvalidDataException("Falta el parche de la aplicación");stream.CopyTo(memory);byte[] recipe=memory.ToArray();
#if MULTI_IR
   if(FirmwareCodec.Hash(recipe)!="82161d5b805f3c89da5ae0172075e0cc3b96f71fb07e40053240063fda0788fa")throw new InvalidDataException("Parche de la aplicación alterado");
#elif AUTOBOOT
   if(FirmwareCodec.Hash(recipe)!="508602ac8bf2fc95ca64198e825d4ed8ed93219c8295eca3f7df09d166ee6261")throw new InvalidDataException("Parche de la aplicación alterado");
#else
   if(FirmwareCodec.Hash(recipe)!="3032fed1c4e4b05f5a3cfc4ebc4914da688ca6ec29bc075d4c1fc5e0767dfc2b")throw new InvalidDataException("Parche de la aplicación alterado");
#endif
   return FirmwarePatch.Build(File.ReadAllBytes(official),recipe);
  }
 }
}
