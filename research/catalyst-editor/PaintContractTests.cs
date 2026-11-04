using System;using System.Drawing;using System.Reflection;using System.Windows.Forms;using System.IO;
namespace CFEditor {
static class PaintContractTests {
 // A translated, narrow invalidation must not paint on neighbouring rows/tabs.
 public static int Check(Control control,string file){
  control.Size=new Size(200,48);control.Text="FIRMWARE CF 123";
  using(var image=new Bitmap(420,180)){
   var allowed=new Rectangle(118,74,24,5);
   using(var g=Graphics.FromImage(image)){
    g.Clear(Color.Magenta);g.TranslateTransform(110,60);g.SetClip(new Rectangle(8,14,24,5));
    var method=control.GetType().GetMethod("OnPaint",BindingFlags.NonPublic|BindingFlags.Instance);
    method.Invoke(control,new object[]{new PaintEventArgs(g,new Rectangle(8,14,24,5))});
   }
   int leaked=0;for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)if(!allowed.Contains(x,y)&&image.GetPixel(x,y).ToArgb()!=Color.Magenta.ToArgb())leaked++;
   image.Save(file);return leaked;
  }
 }
 public static void Verify(Assembly component,string folder){
  using(var button=new ActionButton())if(Check(button,Path.Combine(folder,"paint-action.png"))!=0)throw new InvalidOperationException("ActionButton paints outside its invalidated rectangle");
  using(var knob=new Knob(new Binding("test",Catalog.Int("gain","GAIN",0,255))))if(Check(knob,Path.Combine(folder,"paint-knob.png"))!=0)throw new InvalidOperationException("Knob text paints outside its invalidated rectangle");
  using(var number=new GlassNumber()){number.Enabled=false;if(Check(number,Path.Combine(folder,"paint-number.png"))!=0)throw new InvalidOperationException("Numeric text paints outside its invalidated rectangle");}
  using(var label=new AssetLabel())if(Check(label,Path.Combine(folder,"paint-asset-label.png"))!=0)throw new InvalidOperationException("Asset label paints outside its invalidated rectangle");
  // The unified workspace uses the editor's own controls above. The legacy
  // Dark* controls exist only in the optional embedded component.
  if(component==null)return;
  foreach(string name in new[]{"DarkButton","DarkLabel","DarkCheckBox","GlassNumber"})using(var c=(Control)Activator.CreateInstance(component.GetType(name,true),true)){
   c.Enabled=false;if(Check(c,Path.Combine(folder,"paint-"+name+".png"))!=0)throw new InvalidOperationException(name+" paints outside its invalidated rectangle");
  }
 }
}
}
