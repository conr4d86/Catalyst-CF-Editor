using System;using System.IO;using System.Linq;using System.Collections;using System.Collections.Generic;using System.Web.Script.Serialization;
namespace CFEditor {
// Unsafe all-model firmware must never be asked to switch through presets.
// Reuse is restricted to the locally confirmed installation of the exact live
// edition. The archive, evidence and all repeated reads are pinned as a unit.
sealed class CFUpgradeBackup {
 public readonly string Archive,Edition,ArchiveHash;readonly int product;readonly string recordFile,recordHash,candidate,candidateHash;readonly Dictionary<string,string> files;
 CFUpgradeBackup(string record,CFInstallRecord installation){product=installation.Product;recordFile=record;recordHash=Hash(record,16384);candidate=installation.Candidate;candidateHash=installation.Hash;Archive=installation.Backup;Edition=installation.Edition;files=ReadEvidence(Archive);ArchiveHash=files[Path.GetFileName(Archive)];Validate();}
 static void Need(bool value,string message){if(!value)throw new IOException(message);}
 internal static bool ValidEdition(string edition){return edition!=null&&edition.Length==64&&edition.All(Uri.IsHexDigit)&&!edition.All(c=>c=='0')&&!edition.All(c=>c=='f'||c=='F');}
 static string Hash(string path,long limit){Need(File.Exists(path)&&new FileInfo(path).Length<=limit,"No se conserva íntegra la copia verificada de esta edición.");return FirmwareCodec.Hash(File.ReadAllBytes(path));}
 static string Field(Dictionary<string,object> row,string key){object value;Need(row!=null&&row.TryGetValue(key,out value),"La verificación de la copia está incompleta.");return Convert.ToString(row[key]);}
 static string Digest(Dictionary<string,object> row,string key){string digest=Field(row,key);Need(digest.Length==64&&digest.All(Uri.IsHexDigit),"La verificación contiene una huella incorrecta.");return digest;}
 static Dictionary<string,string> ReadEvidence(string archive){
  string evidence=archive+".verification.json",folder=Path.GetDirectoryName(archive);Hash(evidence,2000000);var data=new JavaScriptSerializer{MaxJsonLength=2000000}.Deserialize<Dictionary<string,object>>(File.ReadAllText(evidence));
  object entries;Need(data!=null&&Field(data,"schema")=="CatalystIR.RepeatedPresetReads.v1"&&data.TryGetValue("reads",out entries),"Falta la verificación repetida de los presets.");var reads=data["reads"] as IList;Need(reads!=null&&reads.Count==12,"La copia no contiene la verificación de los 12 presets.");
  var files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);files.Add(Path.GetFileName(archive),Digest(data,"archive_sha256"));files.Add(Path.GetFileName(evidence),Hash(evidence,2000000));var indices=new HashSet<int>();
  foreach(object value in reads){var row=value as Dictionary<string,object>;int index;Need(int.TryParse(Field(row,"index"),out index)&&index>=1&&index<=12&&indices.Add(index),"Los índices de la copia no corresponden a los 12 presets.");Digest(row,"data_sha256");
   foreach(string key in new[]{"first","second"}){string name=Field(row,key);Need(!string.IsNullOrWhiteSpace(name)&&name==Path.GetFileName(name)&&name!="."&&name!=".."&&!files.ContainsKey(name),"Nombre de lectura de respaldo no válido.");files.Add(name,Digest(row,key+"_sha256"));}
  }
  foreach(var file in files)Need(Hash(Path.Combine(folder,file.Key),file.Key==Path.GetFileName(archive)?16*1024*1024:2000000).Equals(file.Value,StringComparison.OrdinalIgnoreCase),"La copia verificada ha cambiado; no se leerán los presets del amplificador.");return files;
 }
 public static CFUpgradeBackup Resolve(string root,int product,string liveEdition){
  Need(DeviceFamily.Supported(product)&&ValidEdition(liveEdition),"Falta la edición completa comprobada del firmware instalado. No se leerán los presets del amplificador.");string backups=Path.Combine(Path.GetFullPath(root),"backups");
  Need(Directory.Exists(backups),"No se conserva una copia verificada para actualizar esta edición. No se cambiará ningún preset del amplificador.");var dirs=Directory.EnumerateDirectories(backups).Take(2049).ToArray();Need(dirs.Length<=2048,"Revisa las copias anteriores antes de actualizar.");
  var matches=new List<Tuple<string,CFInstallRecord>>();foreach(string dir in dirs){string file=Path.Combine(dir,"installation.json");if(!File.Exists(file))continue;var record=CFInstallRecord.Load(file);if(record.Product==product&&record.Phase=="active"&&record.Edition.Equals(liveEdition,StringComparison.OrdinalIgnoreCase))matches.Add(Tuple.Create(file,record));}
  Need(matches.Count==1,"No hay una única copia verificada de los 12 presets para esta edición. No se leerán ni cambiarán los presets del amplificador.");return new CFUpgradeBackup(matches[0].Item1,matches[0].Item2);
 }
 public void Validate(){var current=CFInstallRecord.Load(recordFile);Need(current.Phase=="active"&&current.Product==product&&current.Edition.Equals(Edition,StringComparison.OrdinalIgnoreCase)&&current.Hash.Equals(candidateHash,StringComparison.OrdinalIgnoreCase)&&current.Backup.Equals(Archive,StringComparison.OrdinalIgnoreCase)&&Hash(recordFile,16384)==recordHash&&Hash(candidate,16*1024*1024).Equals(candidateHash,StringComparison.OrdinalIgnoreCase),"El registro o firmware de la copia verificada ha cambiado.");ValidateCopy(Archive);}
 public void ValidateCopy(string archive){Need(string.Equals(Path.GetFileName(archive),Path.GetFileName(Archive),StringComparison.OrdinalIgnoreCase),"Destino de respaldo incorrecto.");foreach(var file in files)Need(Hash(Path.Combine(Path.GetDirectoryName(archive),file.Key),file.Key==Path.GetFileName(Archive)?16*1024*1024:2000000).Equals(file.Value,StringComparison.OrdinalIgnoreCase),"La copia verificada ha cambiado; no se leerán los presets del amplificador.");}
}
}
