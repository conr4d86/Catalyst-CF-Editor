using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Web.Script.Serialization;
static class FamilyProfile {
 public static int ConnectedProduct;
 public static int SyncProduct;
 public static void RecordSync(string value){int id;SyncProduct=int.TryParse(value,out id)&&IsModel(id)?id:0;}
 public static bool IsModel(int id){return id==2424833||id==2424834||id==2424835;}
 public static bool IsBootloader(int id){return id==2428928||id==2428929;}
 public static string Name(int id){if(!IsModel(id))throw new InvalidDataException("Modelo de Catalyst desconocido");return id==2424833?"Catalyst 60":id==2424834?"Catalyst 100":"Catalyst 200";}
 public static int TargetIndex(IList<int> ids,bool recovery,int expected){if(recovery&&!IsModel(expected))throw new InvalidDataException("Recuperación sin identificación verificada");var matches=ids.Select((id,index)=>new{id,index}).Where(v=>IsModel(v.id)||recovery&&IsBootloader(v.id)).ToArray();if(matches.Length!=1)throw new InvalidDataException("Se necesita un solo Catalyst identificado");if(IsModel(matches[0].id)&&expected!=0&&matches[0].id!=expected)throw new InvalidDataException("La copia pertenece a otro modelo");return matches[0].index;}
 public static Dictionary<string,object> RecoveryIdentity(int index,int bootloader,string backup){if(index<0||!IsBootloader(bootloader))throw new InvalidDataException("Bootloader no identificado");int product=BackupProduct(backup);return new Dictionary<string,object>{{"setDeviceIdentity_deviceIndexKey",index},{"setDeviceIdentity_currentDeviceProductIDKey",bootloader},{"setDeviceIdentity_newDeviceProductIDKey",product},{"setDeviceIdentity_newDeviceFriendlyNameKey",Name(product)}};}
 public static int BackupProduct(string path){FirmwareCodec.VerifyPreservedBackup(path);var json=new JavaScriptSerializer{MaxJsonLength=2000000};var archive=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));int id=Convert.ToInt32(((Dictionary<string,object>)archive["meta"])["device"]);if(!IsModel(id))throw new InvalidDataException("La copia no identifica un Catalyst soportado");return id;}
}
