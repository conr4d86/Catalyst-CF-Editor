using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Web.Script.Serialization;
namespace CFEditor {
static class EditorTests {
 static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
 public static int Run(string output){var results=new List<string>();try{
  var c=Catalog.Load();Need(c.Amps.Length==12,"12 amps");Need(c.Effects.Length==24,"24 effects");Need(c.Find("systemUITest")==null,"exclude UI test asset");results.Add("Catalog: 12 amps, 24 FX; UI diagnostics excluded");
  foreach(var a in c.assets)foreach(var p in a.parameters){p.Validate(p.kind=="bool"?(object)false:(object)p.minimum);p.Validate(p.kind=="bool"?(object)true:(object)p.maximum);if(p.choices!=null)Need(p.choices.Length==p.maximum-p.minimum+1,"enum geometry");}results.Add("All catalog parameter bounds and enum geometry");
  var backend=new PreviewBackend();Need(backend.Writes.Count==0,"startup writes");backend.Connect().GetAwaiter().GetResult();backend.Read(c.Find("Chime").parameters.Select(p=>new Binding("CatalystGroupAmp",p))).GetAwaiter().GetResult();Need(backend.Writes.Count==0,"read writes");results.Add("Preview construction, connection and reads do not issue writes");
  bool rejected=false;try{backend.Command("saveCurrentPresetToDeviceAtIndex",5);}catch(InvalidOperationException){rejected=true;}Need(rejected,"preview blocks device commands");results.Add("Preview rejects persistent device operations");
  var lib=new AssetLibrary();var taps=new byte[8192];Buffer.BlockCopy(BitConverter.GetBytes(1f),0,taps,8192-4,4);lib.Slots[1]=new LibrarySlot{Name="Synthetic impulse",Hash=FirmwareCodec.Hash(taps),Taps=taps};var pack=lib.Build();AssetLibrary.Validate(pack);Need(lib.Slots[0]==null&&lib.Slots[2]==null,"partial slots");results.Add("IR2-only package: processing, FFT, manifest and checksums");
  Need(EngineUnits.TempoMs(140)==429&&EngineUnits.TempoBpm(429)==140,"tempo conversion");Need(EngineUnits.TunerNote(0)=="E"&&EngineUnits.TunerNote(8)=="C"&&EngineUnits.TunerNote(12)=="—","tuner mapping");results.Add("Original engine tempo units and E-based tuner note mapping");
  var rhythm=c.Find("SimpleDelay").parameters.Single(p=>p.id=="graphSlotAsset_TempoSyncSubDivision");Need(rhythm.Display(0)=="1/1"&&rhythm.Display(9)=="1/8 T"&&rhythm.displayLabels.Length==19&&!rhythm.displayLabels.Any(s=>s.Contains("dB")),"rhythmic display units");results.Add("Rhythmic divisions use the native note table and never display decibels");
  Need(new[]{2424833,2424834,2424835}.All(DeviceFamily.Supported)&&!DeviceFamily.Supported(2428928)&&!DeviceFamily.Supported(0),"product identity policy");results.Add("CX60/100/200 normal-mode identities accepted; bootloaders never inferred as a model");
  rejected=false;try{Catalog.Bool("flag","Flag").Validate(2);}catch(ArgumentOutOfRangeException){rejected=true;}Need(rejected,"invalid bool accepted");results.Add("Boolean fields reject values outside 0/1");
  string folder=Path.GetDirectoryName(Path.GetFullPath(output));Exception uiFailure=null;using(var form=new EditorForm(true,false,false,false)){form.Shown+=async delegate{try{results.AddRange(await form.VerifyPreview(folder));}catch(Exception e){uiFailure=e;}finally{form.WaitPreviewIdle();form.Close();}};System.Windows.Forms.Application.Run(form);}if(uiFailure!=null)throw uiFailure;
  File.WriteAllText(output,new JavaScriptSerializer().Serialize(new{pass=true,hardware_accessed=false,checks=results}));return 0;
 }catch(Exception e){File.WriteAllText(output,new JavaScriptSerializer().Serialize(new{pass=false,hardware_accessed=false,checks=results,error=e.ToString()}));return 1;}}
}
}
