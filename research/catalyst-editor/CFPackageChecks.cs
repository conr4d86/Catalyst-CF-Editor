using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Web.Script.Serialization;
namespace CFEditor {
static class CFPackageChecks {
 static AssetPreparation IR(byte slot){var taps=new byte[8192];Buffer.BlockCopy(BitConverter.GetBytes(.125f*(slot+1)),0,taps,(2047-slot*7)*4,4);var fft=FFTBank.Build(taps);return new AssetPreparation{Slot=slot,Data=taps.Concat(fft).ToArray(),Info=new AssetInfo{Name="IR fixture "+slot,OutputDb=-18,Filters=true,LowHz=80,HighHz=8000}};}
 static void Need(bool v,string text){if(!v)throw new Exception(text);}static bool Reject(Action action){try{action();return false;}catch(InvalidDataException){return true;}}
 public static void Run(string basePath,string segmentPath,string namPath,string folder){Directory.CreateDirectory(folder);var builder=new CFPackageBuilder(File.ReadAllBytes(basePath),File.ReadAllBytes(segmentPath));var nam=AssetWorkspace.Import(3,namPath);var all=new AssetPreparation[5];for(byte i=0;i<3;i++)all[i]=IR(i);all[3]=nam.InitialPreparation;all[4]=AssetWorkspace.Prepare(4,AssetWorkspace.Snapshot(nam.Settings));var edition=Enumerable.Range(0,32).Select(i=>(byte)(i+1)).ToArray();var results=new List<object>();
  foreach(int mask in new[]{0,1,2,4,8,16,31,17,10,21}){var slots=new AssetPreparation[5];for(int i=0;i<5;i++)if((mask&(1<<i))!=0)slots[i]=all[i];var built=builder.Build(slots,edition);builder.Validate(built.Bytes);Need(built.InitialSlots==slots.Count(x=>x!=null),"seed mask/count");results.Add(new{mask,slots=built.InitialSlots,sha256=built.Hash,bytes=built.Bytes.Length});if(mask==31)File.WriteAllBytes(Path.Combine(folder,"LAB_ONLY-NOT-FOR-INSTALL.ctf"),built.Bytes);}
  var wrong=all.ToArray();wrong[4]=all[3];Need(Reject(()=>builder.Build(wrong,edition)),"slot alias accepted");
  var broken=(byte[])all[0].Data.Clone();broken[8192]^=1;wrong=all.ToArray();wrong[0]=new AssetPreparation{Slot=0,Data=broken,Info=all[0].Info};Need(Reject(()=>builder.Build(wrong,edition)),"FFT mismatch accepted");
  broken=(byte[])all[3].Data.Clone();broken[7484]^=1;wrong=all.ToArray();wrong[3]=new AssetPreparation{Slot=3,Data=broken,Info=all[3].Info};Need(Reject(()=>builder.Build(wrong,edition)),"NAM reset mismatch accepted");Need(Reject(()=>builder.Build(all,new byte[32])),"zero edition accepted");
  var candidate=builder.Build(all,edition).Bytes;var parsed=CFContainerCodec.Parse(candidate);var raw=(byte[])parsed.Raw.Clone();raw[0]^=1;Need(Reject(()=>builder.Validate(CFContainerCodec.Repack(parsed,raw))),"HEX checksum accepted");
  var head=parsed.Chunks.Single(c=>c.Tag=="HEAD");head.Data[0]^=1;Need(Reject(()=>builder.Validate(CFContainerCodec.Repack(parsed,parsed.Raw))),"native identity changed");
  var fresh=builder.Build(all).Edition;Need(!fresh.SequenceEqual(edition),"edition not unique");
  File.WriteAllText(Path.Combine(folder,"result.json"),new JavaScriptSerializer().Serialize(new{passed=true,hardware_accessed=false,USB_opened=false,SDK_loaded=false,firmware_created=true,hardware_candidate_allowed=false,scope="offline laboratory CTF builder; not an installable approval",cases=results.ToArray(),guards=new[]{"wrong slot","FFT mismatch","NAM silence mismatch","zero edition","HEX corruption","native identity","fresh edition"}}));
 }
}
}
