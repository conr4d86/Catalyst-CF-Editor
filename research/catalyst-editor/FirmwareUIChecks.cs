#if DEVELOPMENT
using System;using System.IO;using System.Linq;using System.Drawing;using System.Collections.Generic;using System.Threading.Tasks;using System.Windows.Forms;using System.Web.Script.Serialization;
namespace CFEditor {
sealed partial class EditorForm {
 internal async Task<object> CheckFirmwareUI(string model,string folder){
  WaitPreviewIdle();await RefreshDevice();writes.Stop();layoutTimer.Stop();
  TestNeed(AllChildren(this).Any(c=>c.Text=="v1.0")&&!AllChildren(this).Any(c=>c.Text.Contains("Desarrollo local")),"Version footer not updated");
  firmware.SetDetectedReceiver(true);await ChangePage(5);var view=firmware.DataView;
  TestNeed(firmware.DataMode&&!view.InitialMode,"Main data navigation did not open the data view");
  TestNeed(nav.Count==6&&nav[5].Text=="CAMBIAR NAM / IR"&&nav.All(n=>n.Height==nav[0].Height&&n.Font.Equals(nav[0].Font)),"Navigation style differs");
  TestNeed(!AllChildren(this).Any(c=>c.Visible&&(c.Text=="Instalar / reinstalar CF"||c.Text=="Instalación completa con ejemplos incluidos")),"Duplicate subnavigation remains");
  using(var button=new ActionButton()){int paints=0;button.Invalidated+=delegate{paints++;};button.CreateControl();button.Text="1. Cargar NAM…";paints=0;button.Text="1. Cargar IR…";TestNeed(paints>0,"Load label change does not request repaint");}
  TestNeed(AllChildren(this).OfType<LinkLabel>().Single(c=>c.Text=="Desarrollado por conr4d").Font.SizeInPoints>=12,"Footer credit not enlarged");
  var sourceHash=FirmwareCodec.Hash(File.ReadAllBytes(model));var demos=DemoAssets.Load();
  TestNeed(demos.Length==5&&demos.All(d=>d!=null),"Missing embedded demo");
  TestNeed(demos[3].Data.SequenceEqual(demos[4].Data)&&demos[3].Info.Encode().SequenceEqual(demos[4].Info.Encode()),"Demo NAMs differ");
  TestNeed(FirmwareCodec.Hash(demos[3].Info.SourceHash)==FirmwareCodec.Hash(demos[4].Info.SourceHash)&&BitConverter.ToString(demos[3].Info.SourceHash).Replace("-","").ToLowerInvariant()==sourceHash,"Wrong demo NAM source");
  TestNeed(demos[3].Info.InputDb==-5&&demos[4].Info.InputDb==-5&&!demos[3].Info.Automatic&&demos[3].Info.OutputDb==-3,"Demo NAM calibration");
  for(int i=0;i<3;i++)TestNeed(demos[i].Slot==i&&demos[i].Info.Automatic&&demos[i].Info.Filters&&demos[i].Info.LowHz==80&&demos[i].Info.HighHz==8000,"IR demo normalization or cuts");
  var builder=InitialCFResources.Builder();var image=await Task.Run(()=>builder.Build(demos));
  string ctf=Path.Combine(folder,"demos-offline-NOT-INSTALLED.ctf");File.WriteAllBytes(ctf,image.Bytes);
  var restored=CFPackageBuilder.ReadPinnedTrialSeeds(image.Bytes,FirmwareCodec.Hash(image.Bytes));
  for(int i=0;i<5;i++)TestNeed(restored[i].Data.SequenceEqual(demos[i].Data)&&restored[i].Info.Encode().SequenceEqual(demos[i].Info.Encode()),"Initial image changed a demo");
  var cases=new List<object>();
  foreach(double zoom in new[]{1.0,.8,.62}){
   ApplyZoom(zoom);firmware.Relayout();Application.DoEvents();
   var order=Enumerable.Range(0,5).OrderBy(i=>view.Tab(i).Left).ToArray();TestNeed(order.SequenceEqual(new[]{3,4,0,1,2}),"Visual order changed protocol slots");
   for(int slot=0;slot<5;slot++){
    ((IButtonControl)view.Tab(slot)).PerformClick();TestNeed(view.SelectedSlot==slot&&!view.SendEnabled,"Tab selected another protocol slot");
    var controls=AllChildren(view).Where(c=>c.Visible).ToArray();
    var choose=controls.OfType<ActionButton>().Single(c=>c.Text.StartsWith("1. Cargar"));
    TestNeed(choose.Text==(slot>=3?"1. Cargar NAM…":"1. Cargar IR…"),"Load label does not match selected slot");
    TestNeed(controls.Any(c=>c.Text=="Entrada NAM")== (slot>=3),"Manual input visibility differs from selected resource");
    var connect=controls.OfType<ActionButton>().Single(c=>c.Text=="2. Conectar al ampli");
    var send=controls.OfType<ActionButton>().Single(c=>c.Text.StartsWith("3. Enviar"));
    TestNeed(connect.Parent==send.Parent&&connect.Right<send.Left&&connect.Top==send.Top&&connect.Height==send.Height,"Connect not left of send in same row");
    TestNeed(!connect.Enabled&&!send.Enabled,"Preview permits hardware action");
    foreach(Control c in controls)TestNeed(c.Left>=0&&c.Top>=0&&c.Right<=c.Parent.ClientSize.Width&&c.Bottom<=c.Parent.ClientSize.Height,"Clipped firmware control: "+c.Text+" "+c.Bounds+" in "+c.Parent.ClientSize);
    cases.Add(new{zoom,slot,load=choose.Text,connect=connect.Text,send=send.Text});
   }
   await ChangePage(4);Application.DoEvents();
   var actions=AllChildren(view).OfType<ActionButton>().Where(c=>c.Visible).ToArray();
   TestNeed(AllChildren(view).Count(c=>c.Visible&&c.Text=="Incluye: NAM 1 y NAM 2 · 3 IR de prueba, después pueden cambiarse desde «Cambiar NAM / IR».")==1,"Initial copy differs from approved text");
   TestNeed(view.InitialMode&&actions.Length==1&&actions[0].Text=="Reinstalar Custom Firmware"&&!actions[0].Enabled,"Initial view not one preview-safe contextual button");
   TestNeed(!await view.ImportDrop(view.Tab(3),new DataObject(DataFormats.FileDrop,new[]{model})),"Initial screen accepted file drop");
   foreach(Control c in AllChildren(view).Where(c=>c.Visible))TestNeed(c.Left>=0&&c.Top>=0&&c.Right<=c.Parent.ClientSize.Width&&c.Bottom<=c.Parent.ClientSize.Height,"Clipped initial control: "+c.Text);
   if(zoom==.8)Snapshot(Path.Combine(folder,"firmware-install.png"),4);
   await ChangePage(5);Application.DoEvents();
   TestNeed(!view.InitialMode&&AllChildren(view).Any(c=>c.Visible&&c.Text=="2. Conectar al ampli"),"Connect button not restored after initial mode");
  }
  ApplyZoom(.8);firmware.Relayout();TestNeed(await view.ImportFile(3,model),"Data view did not import NAM1");
  TestNeed(view.Selection(4)==null&&view.Selection(0)==null,"Import changed another slot");
  var preparedNAM=view.Preparation(3);var sourceNAM=view.Selection(3).Settings.Model;var packed=NamModel.Floats(preparedNAM.Data.Take(7484).ToArray());
  TestNeed(!preparedNAM.Info.Automatic&&preparedNAM.Info.OutputDb==-3&&preparedNAM.Info.InputDb==-5,"NAM defaults do not match input -5/manual output -3");
  double ratio=Math.Pow(10,(-5+12)/20.0);
  for(int i=0;i<3;i++)TestNeed(Math.Abs(packed[i]-sourceNAM.Weights[i]*ratio)<=Math.Max(1e-8,Math.Abs(packed[i])*1e-5),"Input attenuation missing from NAM data");
  int offset=3;foreach(int kernel in NamModel.Kernels){for(int i=0;i<3;i++)TestNeed(Math.Abs(packed[offset+3+i]-sourceNAM.Weights[offset+3+i]*ratio)<=Math.Max(1e-8,Math.Abs(packed[offset+3+i])*1e-5),"NAM input mixin not attenuated");offset+=18+9*kernel;}
  TestNeed(Math.Abs(packed[1870]-sourceNAM.Weights[1870]*Math.Pow(10,-3/20.0))<=Math.Max(1e-8,Math.Abs(packed[1870])*1e-5),"Manual output attenuation missing");

  foreach(double trim in new[]{-36.0,-20.0,-5.0,0.0}){
   await view.TestSettings(3,false,-3,trim);var manual=view.Preparation(3);TestNeed(manual.Info.InputDb==trim&&manual.Info.OutputDb==-3&&!manual.Info.Automatic,"Manual NAM input/output not independent");
   TestNeed(manual.Data.Take(7484).SequenceEqual(sourceNAM.Packed(trim,-3)),"Manual input not applied to all packed model weights");
  }
  await view.TestSettings(3,true,-3,-12);var automatic=view.Preparation(3);double autoDb=sourceNAM.AutomaticForInput(-12);TestNeed(automatic.Info.Automatic&&automatic.Info.InputDb==-12&&Math.Abs(automatic.Info.OutputDb-autoDb)<1e-5&&automatic.Data.Take(7484).SequenceEqual(sourceNAM.Packed(-12,autoDb)),"Automatic output ignored the chosen input");
  await view.TestSettings(3,false,-3,-5);
  Snapshot(Path.Combine(folder,"firmware-change.png"),5);
  await ChangePage(4);await ChangePage(5);TestNeed(view.Selection(3)!=null,"Mode change lost local file");
  TestNeed(FirmwareCodec.Hash(File.ReadAllBytes(model))==sourceHash,"Demo source changed");
  TestNeed(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="EngineCLIWrapper"),"UI checks loaded USB SDK");
  return new{passed=true,hardware_accessed=false,USB_opened=false,SDK_loaded=false,initial_one_button=true,top_level_navigation=true,numbered_actions_adjacent=true,visual_order=new[]{"NAM 1","NAM 2","IR1","IR2","IR3"},protocol_ids_preserved=true,embedded_demo_sha256=DemoAssets.ResourceHash,demo_NAMs_identical=true,demo_cuts_Hz=new[]{80,8000},demo_input_db=-5,demo_output_db=-3,demo_output_automatic=false,calibration_applied_to_weights=true,manual_input_visible=true,input_boundaries_and_auto_output_verified=true,initial_package_built_and_roundtripped=true,source_unchanged=true,version_footer="v1.0",cases};
 }
}
static class FirmwareUIChecks {
 public static int Run(string model,string folder){folder=Path.GetFullPath(folder);Directory.CreateDirectory(folder);int result=1;
  using(var form=new EditorForm(true,false,false,false)){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-12000,-12000);form.ShowInTaskbar=false;form.Shown+=async delegate{try{File.WriteAllText(Path.Combine(folder,"result.json"),new JavaScriptSerializer().Serialize(await form.CheckFirmwareUI(model,folder)));result=0;}catch(Exception error){File.WriteAllText(Path.Combine(folder,"result.json"),new JavaScriptSerializer().Serialize(new{passed=false,USB_opened=false,error=error.ToString()}));}finally{form.Close();}};Application.Run(form);}return result;
 }
}
}
#endif
