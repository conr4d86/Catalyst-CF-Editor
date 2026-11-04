using System;using System.IO;using System.Linq;using System.Drawing;using System.Collections.Generic;using System.Threading.Tasks;using System.Windows.Forms;using System.Web.Script.Serialization;
namespace CFEditor {
sealed partial class EditorForm {
 internal async Task<object> CheckPreampLayout(string folder){
  WaitPreviewIdle();await RefreshDevice();writes.Stop();layoutTimer.Stop();StoreChecks.Run();
  var mock=(PreviewBackend)backend;var cases=new List<object>();
  foreach(double factor in new[]{.62,.8,1.0,1.25,1.6}){
   File.AppendAllText(Path.Combine(folder,"progress.log"),"scale "+factor+"; form="+Size+" client="+ClientSize+" natural="+naturalHeight+Environment.NewLine);
   var frame=new Size(Math.Max(0,Width-ClientSize.Width),Math.Max(0,Height-ClientSize.Height));
   bool fitsScreen=factor<=FitScreenScale(Screen.FromControl(this).WorkingArea.Size,frame,naturalHeight);
   ApplyZoom(factor);if(fitsScreen)ClientSize=new Size(S(1440),S(naturalHeight));
   Action layout=()=>{layoutTimer.Stop();if(fitsScreen)ArrangeSound();else{var panel=strips["amp"].Controls.OfType<PreampPanel>().Single();panel.Width=S(214);panel.ArrangeControls();}Application.DoEvents();};
   foreach(string model in new[]{"Chime","Chime 2"}){
    File.AppendAllText(Path.Combine(folder,"progress.log"),model+"; form="+Size+" client="+ClientSize+" natural="+naturalHeight+Environment.NewLine);
    await mock.SelectAsset("CatalystGroupAmp",model);await RefreshDevice();layout();
    for(int type=0;type<4;type++){
     await mock.Set(new Binding("CFPreamp",Catalog.Int("type","Tipo",0,3)),type);layout();
     var box=strips["amp"].Controls.OfType<PreampPanel>().Single();
     var children=box.Controls.Cast<Control>().Where(c=>c.Visible).ToArray();
     foreach(var child in children){TestNeed(child.Left>=0&&child.Top>=0&&child.Right<=box.ClientSize.Width&&child.Bottom<=box.ClientSize.Height,"Preamp child clipped at "+factor+": "+child.Text+" "+child.Bounds+" in "+box.ClientSize);}
     for(int a=0;a<children.Length;a++)for(int b=a+1;b<children.Length;b++)TestNeed(!children[a].Bounds.IntersectsWith(children[b].Bounds),"Preamp controls overlap at "+factor+": "+children[a].Text+" / "+children[b].Text+" "+children[a].Bounds+" / "+children[b].Bounds);
     var drive=children.OfType<Knob>().Single(k=>k.Binding.Param.id=="Boost");
     TestNeed(Math.Abs(drive.Width-S(96))<=1&&Math.Abs(drive.Height-S(118))<=1,"Preamp dial scaled twice after model rebuild");
     if(fitsScreen)TestNeed(soundStack.Height<=content.ClientSize.Height,"Preamp spacing adds main-page scrolling");
     cases.Add(new{factor,model,type,visible_controls=children.Length,panel_width=box.Width,panel_height=box.Height,full_window_fit_checked=fitsScreen});
     if(model=="Chime 2"&&(type==1||type==3)&&(factor==.8||factor==1.6))using(var image=new Bitmap(box.Width,box.Height)){box.DrawToBitmap(image,box.ClientRectangle);image.Save(Path.Combine(folder,"preamp-"+type+"-"+factor.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+".png"));}
    }
   }
  }
  TestNeed(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="EngineCLIWrapper"),"Layout check loaded SDK");
  return new{passed=true,USB_opened=false,SDK_loaded=false,hardware_accessed=false,cases=cases.ToArray(),save_context_checks=true};
 }
}
static class PreampLayoutChecks {
 public static int Run(string folder){
  folder=Path.GetFullPath(folder);Directory.CreateDirectory(folder);int result=1;
  using(var form=new EditorForm(true,false,false,false)){
   form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-12000,-12000);form.ShowInTaskbar=false;
   form.Shown+=async delegate{try{var report=await form.CheckPreampLayout(folder);File.WriteAllText(Path.Combine(folder,"result.json"),new JavaScriptSerializer().Serialize(report));result=0;}catch(Exception error){File.WriteAllText(Path.Combine(folder,"result.json"),new JavaScriptSerializer().Serialize(new{passed=false,USB_opened=false,error=error.ToString()}));}finally{form.Close();}};
   Application.Run(form);
  }return result;
 }
}
}
