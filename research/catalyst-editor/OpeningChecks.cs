using System;using System.Drawing;using System.Linq;using System.IO;using System.Threading.Tasks;using System.Windows.Forms;using System.Web.Script.Serialization;
namespace CFEditor {
sealed partial class EditorForm {
 internal async Task<object> CheckOpening(){
  while(initializing)await Task.Delay(1);ArrangeSound();var work=Screen.FromControl(this).WorkingArea;double expected=FitOpeningScale(work.Size,new Size(Width-ClientSize.Width,Height-ClientSize.Height),naturalHeight);
  TestNeed(EditorZoom.Factor<=.801&&Math.Abs(EditorZoom.Factor-expected)<.015,"Opening scale is not 80% or fitted to screen");
  TestNeed(Width<=work.Width&&Height<=work.Height,"Default window is outside the working area");TestNeed(soundStack.Height<=content.ClientSize.Height,"Default opening requires scroll");
  var dial=AllChildren(strips["amp"]).OfType<Knob>().First();TestNeed(Math.Abs(dial.Font.SizeInPoints-10*EditorZoom.Factor)<.11,"Opening font is not proportional");
  await ChangePage(5);TestNeed(firmware.DataView.Width==pages[4].ClientSize.Width,"First CF construction does not fit reduced window");for(int i=0;i<5;i++)firmware.PreviewClickTab(i);await ChangePage(0);ArrangeSound();
  TestNeed(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="EngineCLIWrapper"),"Opening check loaded native USB SDK");
  return new{passed=true,hardware_accessed=false,opening_scale=EditorZoom.Factor,client_width=ClientSize.Width,client_height=ClientSize.Height,working_width=work.Width,working_height=work.Height,scroll_required=false,first_cf_tabs_work=true};
 }
}
static class OpeningChecks {
 public static int Run(string output){int result=1;using(var form=new EditorForm(true)){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-12000,-12000);form.ShowInTaskbar=false;form.Shown+=async delegate{try{var report=await form.CheckOpening();File.WriteAllText(output,new JavaScriptSerializer().Serialize(report));result=0;}catch(Exception error){File.WriteAllText(output,new JavaScriptSerializer().Serialize(new{passed=false,hardware_accessed=false,error=error.ToString()}));}finally{form.Close();}};Application.Run(form);}return result;}
}
}
