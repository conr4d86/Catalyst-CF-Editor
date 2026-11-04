#if DEVELOPMENT
using System;using System.IO;using System.Linq;using System.Text;using System.Security.Cryptography;using System.Web.Script.Serialization;
namespace CFEditor {
// Reuse of an observed engine is reviewed separately from a new engine layout.
// This proof permits DATA changes only; it is not a universal memory/IRQ proof.
static class InitialInstallReview {
 internal const string ReferenceHash="5ee8770e3d5afe84d424b8b65a8d94433ba541ec5b1727b727a5e41055bff610";
 public static int Run(string reference,string folder){Directory.CreateDirectory(folder);try{
  var original=File.ReadAllBytes(reference);if(FirmwareCodec.Hash(original)!=ReferenceHash)throw new InvalidDataException("Wrong physical reference firmware");
  var builder=InitialCFResources.Builder();builder.Validate(original);var known=builder.ReadPrepared(original);
  var assets=DemoAssets.Load();byte[] edition;using(var hash=SHA256.Create())edition=hash.ComputeHash(Encoding.UTF8.GetBytes("CatalystCFEditor initial-demo-review:"+DemoAssets.ResourceHash));
  var candidate=builder.Build(assets,edition);builder.Validate(candidate.Bytes);var read=CFPackageBuilder.ReadPinnedTrialSeeds(candidate.Bytes,candidate.Hash);
  for(int i=0;i<5;i++)if(!assets[i].Data.SequenceEqual(read[i].Data)||!assets[i].Info.Encode().SequenceEqual(read[i].Info.Encode()))throw new InvalidDataException("Seed mismatch");
  candidate.Export(Path.Combine(folder,"initial-demos-reviewed.ctf"));
  File.WriteAllText(Path.Combine(folder,"result.json"),new JavaScriptSerializer().Serialize(new{schema=1,passed=true,USB_opened=false,SDK_loaded=false,hardware_accessed=false,reference_sha256=known.Hash,reference_edition=BitConverter.ToString(known.Edition).Replace("-",""),segment_sha256=CFPackageBuilder.SegmentHash,demo_bundle_sha256=DemoAssets.ResourceHash,demo_candidate_sha256=candidate.Hash,protected_boot_code_native_images_and_metadata_match_reference=true,only_seed_manifest_edition_and_resources_can_vary=true,initial_slots=candidate.InitialSlots,NAM_input_db=LibrarySlot.DefaultInputDb,NAM_output_db=LibrarySlot.DefaultNamOutputDb,NAM_automatic=false,NAM_demos_identical=read[3].Data.SequenceEqual(read[4].Data)&&read[3].Info.Encode().SequenceEqual(read[4].Info.Encode()),IR_cuts_Hz=new[]{80,8000},scope="Offline data-only comparison to the r95 image observed on one Catalyst 60. No new physical demo installation or general memory/IRQ certification."}));return 0;
 }catch(Exception error){File.WriteAllText(Path.Combine(folder,"result.json"),new JavaScriptSerializer().Serialize(new{passed=false,USB_opened=false,error=error.ToString()}));return 1;}}
}
}
#endif
