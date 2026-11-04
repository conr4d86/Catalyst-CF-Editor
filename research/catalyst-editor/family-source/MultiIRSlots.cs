using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;

// V2 selection state is separate from the v1 journal. Profiles belong to a
// slot, so even identical WAVs can have different gain/cuts in IR1/IR2/IR3.
class MultiIRSlots {
 public class Slot {public string path,name,key,sourcePath,conversion;public IRLevels.Profile profile;}
 public Slot[] slots=new Slot[3];
 static readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=2000000};
 public static IRLevels.Profile Clone(IRLevels.Profile p){return json.Deserialize<IRLevels.Profile>(json.Serialize(p));}
 public int Count {get{return slots.Count(s=>s!=null);}}
 public int First {get{return Array.FindIndex(slots,s=>s!=null);}}
 public string Recipe(){
  if(Count==0)return null;
  return "v2|"+string.Join(";",slots.Select((s,i)=>s==null?i+":factory":i+":"+OneClickSession.Recipe(s.key,IRLevels.EffectiveDb(Analyze(s),s.profile),s.profile.filtersEnabled,s.profile.lowCutHz,s.profile.highCutHz)).ToArray());
 }
 public static IRLevels.Analysis Analyze(Slot s){return IRLevels.Analyze(IRFilters.Apply(Read(s),s.profile.filtersEnabled,s.profile.lowCutHz,s.profile.highCutHz));}
 public static byte[] Read(Slot s){
  if(s==null||s.profile==null||string.IsNullOrEmpty(s.path))throw new InvalidDataException("Ranura IR incompleta");
  byte[] b=FirmwareCodec.ReadIR(s.path);if(FirmwareCodec.Hash(b)!=s.key)throw new InvalidDataException("El WAV guardado ha cambiado: "+s.name);return b;
 }
 public byte[][] Process(){
  var result=new byte[3][];
  for(int i=0;i<3;i++)if(slots[i]!=null){var s=slots[i];byte[] filtered=IRFilters.Apply(Read(s),s.profile.filtersEnabled,s.profile.lowCutHz,s.profile.highCutHz);result[i]=IRLevels.Apply(filtered,IRLevels.EffectiveDb(IRLevels.Analyze(filtered),s.profile));}
  return result;
 }
 public Slot Import(int index,string file,IRLevels.Profile defaults,string root){
  if(index<0||index>2)throw new ArgumentOutOfRangeException("index");
  byte[] b=FirmwareCodec.ReadIR(file);string key=FirmwareCodec.Hash(b);var p=Clone(defaults??new IRLevels.Profile());p.name=Path.GetFileName(file);
  IRLevels.Analyze(IRFilters.Apply(b,p.filtersEnabled,p.lowCutHz,p.highCutHz));
  string folder=Path.Combine(root,"irs");Directory.CreateDirectory(folder);string target=Path.Combine(folder,key+".wav");
  if(!File.Exists(target))File.WriteAllBytes(target,IRImport.WaveBytes(b));
  string ext=Path.GetExtension(file).ToLowerInvariant();if(!IRImport.SupportedPath(file))ext=".audio";string source=Path.Combine(folder,IRImport.Read(file).SourceHash+"-source"+ext);if(!File.Exists(source))File.Copy(file,source);
  var next=new Slot{path=target,name=Path.GetFileName(file),key=key,profile=p,sourcePath=source,conversion=IRImport.Read(file).Summary};Read(next);
  slots[index]=next;return next;
 }
 public void Save(string path){
  Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  File.WriteAllText(temp,json.Serialize(this),new UTF8Encoding(false));
  if(File.Exists(path))File.Replace(temp,path,path+".previous");else File.Move(temp,path);
 }
 public static MultiIRSlots Load(string path){
  if(!File.Exists(path))return new MultiIRSlots();if(new FileInfo(path).Length>100000)throw new InvalidDataException("Selección IR demasiado grande");
  var result=json.Deserialize<MultiIRSlots>(File.ReadAllText(path));
  if(result==null||result.slots==null||result.slots.Length!=3)throw new InvalidDataException("Selección IR incorrecta");
  foreach(var s in result.slots)if(s!=null){
   if(s.profile==null||string.IsNullOrEmpty(s.path))throw new InvalidDataException("Perfil IR incompleto");
   if(!Path.IsPathRooted(s.path))s.path=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path),s.path));
   if(!string.IsNullOrEmpty(s.sourcePath)&&!Path.IsPathRooted(s.sourcePath))s.sourcePath=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path),s.sourcePath));
   IRFilters.Validate(s.profile.lowCutHz,s.profile.highCutHz);Analyze(s);
  }
  return result;
 }
 public static MultiIRSlots StartFromV1(string root){
  var result=new MultiIRSlots();string legacy=Path.GetDirectoryName(root);
  var previous=OneClickSession.Load(Path.Combine(legacy,"installation.json"));
  if(previous==null||previous.phase!="active"||previous.templateHash!="508602ac8bf2fc95ca64198e825d4ed8ed93219c8295eca3f7df09d166ee6261")return result;
  string wav=Path.Combine(previous.folder,"IR-original.wav"),settings=Path.Combine(previous.folder,"IR-settings.json");
  if(!File.Exists(wav)||!File.Exists(settings))return result;
  var saved=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(settings));
  var profile=json.Deserialize<IRLevels.Profile>(json.Serialize(saved["profile"]));
  if(FirmwareCodec.Hash(FirmwareCodec.ReadIR(wav))!=(string)saved["original_ir_sha256"])throw new InvalidDataException("La copia IR v1 no coincide con su perfil");
  var s=result.Import(0,wav,profile,root);s.name=profile.name??"IR anterior";return result;
 }
 public void Backup(string folder){
  var saved=new MultiIRSlots();for(int i=0;i<3;i++)if(slots[i]!=null){
   var s=slots[i];Read(s);string name="IR"+(i+1)+"-original.wav";File.Copy(s.path,Path.Combine(folder,name));
   string original=null;if(!string.IsNullOrEmpty(s.sourcePath)&&File.Exists(s.sourcePath)){original="IR"+(i+1)+"-source"+Path.GetExtension(s.sourcePath);File.Copy(s.sourcePath,Path.Combine(folder,original));}
   saved.slots[i]=new Slot{path=name,name=s.name,key=s.key,profile=Clone(s.profile),sourcePath=original,conversion=s.conversion};
  }
  File.WriteAllText(Path.Combine(folder,"IR-slots.json"),json.Serialize(saved),new UTF8Encoding(false));
 }
}
