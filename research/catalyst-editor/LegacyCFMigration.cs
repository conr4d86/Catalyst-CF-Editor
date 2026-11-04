using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using System.Web.Script.Serialization;
namespace CFEditor {
// Offline migration of the previously validated single-NAM image. Native
// libraries are not prepared. Already filtered/levelled IR bytes and packed
// NAM weights are copied exactly; this path never processes them twice.
static class LegacyCFMigration {
 internal static void Rebase(string source,string expected,string folder){
  var slots=CFPackageBuilder.ReadPinnedTrialSeeds(File.ReadAllBytes(source),expected);var builder=InitialCFResources.Builder();var result=builder.Build(slots);builder.Validate(result.Bytes);Directory.CreateDirectory(folder);result.Export(Path.Combine(folder,"Catalyst-CF-TRIAL.ctf"));
  var restored=CFPackageBuilder.ReadPinnedTrialSeeds(result.Bytes,result.Hash);for(int i=0;i<5;i++)if((slots[i]==null)!=(restored[i]==null)||slots[i]!=null&&(!slots[i].Data.SequenceEqual(restored[i].Data)||!slots[i].Info.Encode().SequenceEqual(restored[i].Info.Encode())))throw new InvalidDataException("Se ha modificado un recurso conservado.");
  File.WriteAllText(Path.Combine(folder,"PREPARED.json"),new JavaScriptSerializer().Serialize(new{scope="private repair trial; not release approval",candidate_sha256=result.Hash,edition=BitConverter.ToString(result.Edition).Replace("-",""),segment_sha256=CFPackageBuilder.SegmentHash,previous_sha256=expected,all_five_payloads_and_metadata_preserved=true,resources=slots.Select((s,i)=>new{slot=i,present=s!=null,name=s==null?null:s.Info.Name,payload_sha256=s==null?null:FirmwareCodec.Hash(s.Data),input_db=s==null?0:s.Info.InputDb,output_db=s==null?0:s.Info.OutputDb}).ToArray(),USB_opened=false,hardware_verified=false}));
 }
 static object Invoke(Type t,string name,params object[] values){try{return t.GetMethod(name,BindingFlags.Static|BindingFlags.Public).Invoke(null,values);}catch(TargetInvocationException e){throw e.InnerException??e;}}
 internal static CFPreparedPackage Prepare(string legacy,string expected,string settings,string nam2,string folder){
  byte[] source=File.ReadAllBytes(legacy);if(expected==null||expected.Length!=64||FirmwareCodec.Hash(source)!=expected)throw new InvalidDataException("La copia anterior no coincide con la huella indicada.");
  var assembly=Assembly.Load(FirmwareTab.Payload());var codec=assembly.GetType("NamFirmwareCodec",true);var irCodec=assembly.GetType("MultiIRCodec",true);
  if(!(bool)Invoke(codec,"HasNAM",source))throw new InvalidDataException("La copia no contiene el motor NAM anterior.");
  Invoke(codec,"ValidateAny",null,source);
  var taps=(byte[][])Invoke(irCodec,"ReadIRs",source,true);var slots=new AssetPreparation[5];
  uint[] addresses={0x601ad000,0x601b4000,0x601bb000};
  for(byte i=0;i<3;i++)if(taps[i]!=null){
   byte[] data=(byte[])Invoke(codec,"ReadRange",source,addresses[i],25088);
   if(!data.Take(8192).SequenceEqual(taps[i])||!data.Skip(8192).SequenceEqual(FFTBank.Build(taps[i])))throw new InvalidDataException("El IR anterior no coincide con su FFT.");
   slots[i]=new AssetPreparation{Slot=i,Data=data,Info=new AssetInfo{Name="IR"+(i+1)+" conservado",SourceHash=new byte[32]}};
  }
  var meta=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(settings));string hash=Convert.ToString(meta["source_sha256"]);
  if(hash==null||hash.Length!=64||!hash.All(Uri.IsHexDigit))throw new InvalidDataException("Falta la huella del NAM original.");
  byte[] packed=(byte[])Invoke(codec,"ReadRange",source,1612544768u,7484),silence=(byte[])Invoke(codec,"ReadRange",source,1612555604u,288);
  uint fnv=2166136261;foreach(byte value in packed)fnv=unchecked((fnv^value)*16777619);
  if(!("0x"+fnv.ToString("x8")).Equals(Convert.ToString(meta["weights_fnv1a"]),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("El NAM de la copia no coincide con sus ajustes.");
  slots[3]=new AssetPreparation{Slot=3,Data=packed.Concat(silence).ToArray(),Info=new AssetInfo{Name=AssetInfo.DisplayName(Convert.ToString(meta["name"])),Automatic=Convert.ToBoolean(meta["automatic"]),InputDb=Convert.ToSingle(meta["input_trim_db"]),OutputDb=Convert.ToSingle(meta["output_db"]),SourceHash=Enumerable.Range(0,32).Select(i=>Convert.ToByte(hash.Substring(i*2,2),16)).ToArray()}};
  if(!string.IsNullOrWhiteSpace(nam2))slots[4]=AssetWorkspace.Import(4,nam2).InitialPreparation;
  var builder=InitialCFResources.Builder();var package=builder.Build(slots);builder.Validate(package.Bytes);Directory.CreateDirectory(folder);package.Export(Path.Combine(folder,"Catalyst-CF-TRIAL.ctf"));
  File.WriteAllText(Path.Combine(folder,"PREPARED.json"),new JavaScriptSerializer().Serialize(new{schema=1,scope="private controlled hardware trial; not release approval",candidate_sha256=package.Hash,edition=BitConverter.ToString(package.Edition).Replace("-",""),segment_sha256=CFPackageBuilder.SegmentHash,legacy_sha256=expected,initial_slots=package.InitialSlots,resources=slots.Select((s,i)=>new{slot=i,present=s!=null,name=s==null?null:s.Info.Name,payload_sha256=s==null?null:FirmwareCodec.Hash(s.Data),already_processed_preserved=i<4&&s!=null}).ToArray(),IR_processing_metadata_unknown=true,USB_opened=false,hardware_verified=false}));return package;
 }
}
}
