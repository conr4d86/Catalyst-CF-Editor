using System;using System.Linq;using System.Drawing;using System.Threading.Tasks;using System.Windows.Forms;
namespace CFEditor {sealed partial class EditorForm {
 void VerifyDiagonalLayout(){
  TestNeed(FitOpeningScale(new Size(1920,1080),new Size(16,39),840)==.8,"Default opening is not 80 percent");
  foreach(var work in new[]{new Size(1366,768),new Size(1024,600),new Size(800,600)}){double fit=FitOpeningScale(work,new Size(16,39),930);TestNeed(1440*fit+16+32<=work.Width+1&&930*fit+39+32<=work.Height+1,"Opening does not fit small screen "+work);}
  foreach(int hit in new[]{10,11,12,15})TestNeed(ResizeHit(hit)==18,"Straight edge starts resize");
  foreach(int side in new[]{4,5,7,8})foreach(var proposed in new[]{new Rectangle(20,30,1800,900),new Rectangle(20,30,1500,1100),new Rectangle(20,30,800,500)}){
   var next=DiagonalBounds(proposed,side,new Size(16,39),new Size(1456,839),1.8);
   TestNeed(next.Width>=1456&&next.Height>=839,"Resize bypassed minimum");TestNeed(Math.Abs((next.Width-16)/1.8-(next.Height-39))<=1,"Diagonal ratio drift");
   TestNeed((side==4||side==7?next.Right==proposed.Right:next.Left==proposed.Left)&&(side==4||side==5?next.Bottom==proposed.Bottom:next.Top==proposed.Top),"Opposite corner moved");
  }
  var smaller=DiagonalBounds(new Rectangle(0,0,1080,670),8,new Size(16,39),new Size(900,510),1.8);TestNeed(smaller.Width<1456,"Corner cannot shrink");
  ShowPage(0);Application.DoEvents();ArrangeSound();Application.DoEvents();int writesBefore=((PreviewBackend)backend).Writes.Count;
  foreach(double factor in new[]{.75,.62,1.25,1.0}){
   sizingWindow=true;try{ClientSize=new Size((int)Math.Round(1440*factor),(int)Math.Round(naturalHeight*factor));}finally{sizingWindow=false;}
   Application.DoEvents();ArrangeSound();Application.DoEvents();
   var dial=AllChildren(strips["amp"]).OfType<Knob>().First(k=>k.Binding.Param.id=="Gain");TestNeed(Math.Abs(dial.Height-128*factor)<=3,"Dial height did not scale");TestNeed(Math.Abs(dial.Font.SizeInPoints-10*factor)<.1,"Dial font did not scale");TestNeed(Math.Abs(nav[0].Width-130*factor)<=3,"Navigation did not scale");
   foreach(string group in new[]{"CatalystGroupEffect","CatalystGroupReverb"})TestNeed(strips[group].Controls.Cast<Control>().Select(c=>c.Top).Distinct().Count()==1,"Six effect dials wrap during diagonal scaling "+factor+" "+group+" "+strips[group].ClientSize+" padding="+strips[group].Padding+" "+string.Join(";",strips[group].Controls.Cast<Control>().Select(c=>c.Bounds+" "+c.Margin)));
   TestNeed(soundStack.Height<=content.ClientSize.Height,"Sound page requires scrolling after proportional resize "+factor+" stack="+soundStack.Size+" content="+content.Size+" natural="+naturalHeight+" client="+ClientSize+" rows="+string.Join(",",((TableLayoutPanel)content.Parent).GetRowHeights())+" styles="+string.Join(",",((TableLayoutPanel)content.Parent).RowStyles.Cast<RowStyle>().Select(r=>r.Height)));
  }
  TestNeed(((PreviewBackend)backend).Writes.Count==writesBefore,"Resize wrote audio parameters");TestNeed(!connect.Visible,"Preview header button visible");
 }
 async Task VerifyZoomedPages(){
  var mock=(PreviewBackend)backend;
  foreach(double factor in new[]{.62,1.25,1.0}){
   await ChangePage(0);sizingWindow=true;try{ClientSize=new Size((int)Math.Round(1440*factor),(int)Math.Round(naturalHeight*factor));}finally{sizingWindow=false;}
   foreach(var asset in catalog.Effects.OrderByDescending(a=>a.parameters.Count(EffectParameter)).Take(2)){
    await mock.SelectAsset("CatalystGroupReverb",asset.id);await RefreshDevice();ArrangeSound();Application.DoEvents();
    TestNeed(AllChildren(strips["CatalystGroupReverb"]).OfType<Knob>().All(k=>Math.Abs(k.Height-128*factor)<=2&&Math.Abs(k.Font.SizeInPoints-10*factor)<.11),"New effect controls were scaled twice");
    TestNeed(soundStack.Height<=content.ClientSize.Height,"Largest effect needs scroll at "+factor);
   }
   for(int page=0;page<nav.Count;page++){
    await ChangePage(page);Application.DoEvents();if(page==0)ArrangeSound();
    foreach(Control c in AllChildren(pages[PageSurface(page)]).Where(c=>c.Visible&&(c is IButtonControl||c is ComboBox||c is CheckBox)))TestNeed(c.Left>=-1&&c.Top>=-1&&c.Right<=c.Parent.ClientSize.Width+2&&c.Bottom<=c.Parent.ClientSize.Height+2,"Control clipped at scale "+factor+" page="+page+" text="+c.Text+" bounds="+c.Bounds+" parent="+c.Parent.ClientSize);
    if(page==5){Control host=InitialCFResources.UnifiedWorkspace?(Control)firmware.DataView:firmware.Child;TestNeed(host.Width==pages[4].ClientSize.Width,"CF does not fill proportional width");for(int tab=0;tab<(InitialCFResources.UnifiedWorkspace?5:4);tab++)firmware.PreviewClickTab(tab);}
   }
  }
  await ChangePage(0);
 }
}}
