using System;using System.IO;using System.Linq;using System.Diagnostics;using System.Reflection;using System.Drawing;using System.Threading.Tasks;using System.Windows.Forms;using System.Web.Script.Serialization;
namespace CFEditor {
sealed partial class EditorForm {
 public async Task<object> CheckInteraction(){
  StartPosition=FormStartPosition.Manual;Location=new Point(-12000,-12000);ShowInTaskbar=false;Show();Application.DoEvents();while(initializing)await Task.Delay(1);await RefreshDevice();ArrangeSound();writes.Stop();layoutTimer.Stop();
  var mock=(PreviewBackend)backend;var gain=AllChildren(strips["amp"]).OfType<Knob>().Single(k=>k.Binding.Param.id=="Gain");var binding=gain.Binding;int unrelated=0;
  foreach(var knob in AllChildren(pages[0]).OfType<Knob>().Where(k=>k!=gain))knob.Invalidated+=delegate{unrelated++;};
  var timer=Stopwatch.StartNew();for(int i=0;i<1000;i++)await mock.Set(binding,i%256);timer.Stop();TestNeed(unrelated==0,"A numeric read repainted unrelated dials");int numericUnrelated=unrelated;
  int repeats=0;gain.Invalidated+=delegate{repeats++;};for(int i=0;i<100;i++)gain.Read(999%256);TestNeed(repeats==0,"Identical reads invalidate the dial");
  var edit=typeof(Knob).GetMethod("Edit",BindingFlags.Instance|BindingFlags.NonPublic);for(int i=0;i<500;i++)edit.Invoke(gain,new object[]{i%256});TestNeed(pending.Count==1,"Drag does not coalesce into one pending parameter");await Flush();TestNeed(pending.Count==0&&gain.Value==499%256,"Latest drag value was lost");
  using(var image=new Bitmap(gain.Width,gain.Height))using(var g=Graphics.FromImage(image)){var paint=typeof(Knob).GetMethod("OnPaint",BindingFlags.Instance|BindingFlags.NonPublic);var background=typeof(Knob).GetMethod("OnPaintBackground",BindingFlags.Instance|BindingFlags.NonPublic);timer.Restart();for(int i=0;i<120;i++){var state=g.Save();var e=new PaintEventArgs(g,gain.ClientRectangle);background.Invoke(gain,new object[]{e});paint.Invoke(gain,new object[]{e});g.Restore(state);}timer.Stop();}
  double renderMs=timer.Elapsed.TotalMilliseconds;
  ShowPage(5);while(busy)await Task.Delay(1);for(int i=0;i<5;i++)firmware.PreviewClickTab(i);
  string folder=Path.Combine(Path.GetTempPath(),"CatalystCFEditor-paint-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);PaintContractTests.Verify(firmware.Assembly,folder);if(firmware.Assembly==null){using(var frame=new Bitmap(firmware.DataView.Width,firmware.DataView.Height)){firmware.DataView.DrawToBitmap(frame,firmware.DataView.ClientRectangle);frame.Save(Path.Combine(folder,"unified-firmware.png"));}TestNeed(AllChildren(firmware.DataView).OfType<ActionButton>().Any(),"Unified firmware has no interactive controls");}
  TestNeed(nav.All(b=>!(b is Button)),"Navigation still uses the native flat button renderer");
  TestNeed(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="EngineCLIWrapper"),"Interaction checks loaded USB SDK");
  await CheckConnectionFlow();VerifyDiagonalLayout();await VerifyZoomedPages();
  TestNeed(connection.Text=="Amplificador NO detectado"&&connection.ForeColor==UITheme.Error,"Preview claims a connected amplifier without USB");
  offerPreview=true;await backend.Disconnect();await InitializeView(false);TestNeed(loading.Visible&&openPreview.Visible&&!backend.Connected,"No-device startup did not offer preview");openPreview.PerformClick();while(initializing)await Task.Delay(1);TestNeed(preview&&backend.Connected&&!loading.Visible,"Preview offer did not open the simulated editor");
  return new{passed=true,hardware_accessed=false,numeric_updates=1000,unrelated_dial_invalidations=numericUnrelated,identical_read_invalidations=0,drag_events=500,pending_parameters_after_drag=1,final_value=499%256,cached_dial_paints=120,cached_dial_paint_total_ms=renderMs,firmware_tabs_clickable=true,firmware_connection_handover=true,data_mode_preserves_editor_release=true,CF_mode_switch_does_not_reopen_backend=true,reconnect_failure_retry=true,refresh_reads_each_parameter_once=true,proportional_controls_and_fonts=true,loading_overlay_hidden=!loading.Visible,disconnected_header_correct=true,no_device_preview_offer=true,native_flat_buttons=false,scope="In-process input/render and transport-double checks only; user validates real USB and desktop appearance"};
 }
}
static class InteractionChecks {
 public static int Run(string output){int result=1;using(var form=new EditorForm(true,false,false,false)){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-12000,-12000);form.ShowInTaskbar=false;form.Shown+=async delegate{try{var report=await form.CheckInteraction();File.WriteAllText(output,new JavaScriptSerializer().Serialize(report));result=0;}catch(Exception e){File.WriteAllText(output,new JavaScriptSerializer().Serialize(new{passed=false,hardware_accessed=false,error=e.ToString()}));}finally{form.Close();}};Application.Run(form);}return result;}
}
}
