using System;using System.IO;using System.Drawing;using System.Drawing.Drawing2D;using System.Windows.Forms;
namespace CFEditor {
static class BlackTheme {
 static readonly Bitmap tolex;
 static readonly System.Collections.Generic.Dictionary<Size,Bitmap> backgrounds=new System.Collections.Generic.Dictionary<Size,Bitmap>(),panels=new System.Collections.Generic.Dictionary<Size,Bitmap>();
 static BlackTheme(){using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("tolex.png"))using(var original=Image.FromStream(stream))tolex=new Bitmap(original);}
 static Bitmap Cached(System.Collections.Generic.Dictionary<Size,Bitmap> cache,Size size,bool panel){Bitmap bitmap;if(cache.TryGetValue(size,out bitmap))return bitmap;if(cache.Count>=12){foreach(var old in cache.Values)old.Dispose();cache.Clear();}bitmap=new Bitmap(Math.Max(1,size.Width),Math.Max(1,size.Height),System.Drawing.Imaging.PixelFormat.Format32bppPArgb);using(var g=Graphics.FromImage(bitmap)){if(panel)DrawPanel(g,new Rectangle(Point.Empty,size));else{using(var brush=new TextureBrush(tolex,WrapMode.TileFlipXY)){brush.ScaleTransform(.48f,.48f);g.FillRectangle(brush,new Rectangle(Point.Empty,size));}using(var shade=new SolidBrush(Color.FromArgb(92,0,0,0)))g.FillRectangle(shade,new Rectangle(Point.Empty,size));}}cache[size]=bitmap;return bitmap;}
 public static void Background(Graphics g,Rectangle rect){if(rect.Width<1||rect.Height<1)return;g.DrawImageUnscaled(Cached(backgrounds,rect.Size,false),rect.Location);}
 static void DrawPanel(Graphics g,Rectangle rect){if(rect.Width<4||rect.Height<4)return;g.SmoothingMode=SmoothingMode.AntiAlias;using(var path=UITheme.Rounded(new RectangleF(1,1,rect.Width-3,rect.Height-3),14))using(var fill=new LinearGradientBrush(rect,Color.FromArgb(140,25,26,29),Color.FromArgb(140,13,14,17),90))using(var pen=new Pen(Color.FromArgb(105,77,79,83))){g.FillPath(fill,path);g.DrawPath(pen,path);}}
 public static void Surface(Graphics g,Control panel){Behind(g,panel);Panel(g,panel.ClientRectangle);}
 public static void Panel(Graphics g,Rectangle rect){if(rect.Width<4||rect.Height<4)return;g.DrawImageUnscaled(Cached(panels,rect.Size,true),rect.Location);}
 public static void Behind(Graphics g,Control control){
  var chain=new System.Collections.Generic.List<Control>();for(Control p=control.Parent;p!=null;p=p.Parent)chain.Add(p);if(chain.Count==0){using(var b=new SolidBrush(Color.FromArgb(19,20,23)))g.FillRectangle(b,control.ClientRectangle);return;}
  var root=chain[chain.Count-1];Point offset=Point.Empty;for(Control p=control;p!=root;p=p.Parent)offset.Offset(p.Left,p.Top);
  var state=g.Save();try{g.SetClip(control.ClientRectangle,CombineMode.Intersect);g.TranslateTransform(-offset.X,-offset.Y);Background(g,root.ClientRectangle);g.TranslateTransform(offset.X,offset.Y);for(int i=chain.Count-1;i>=0;i--){Control panel=chain[i];if(!(panel is GlassPanel))continue;Point relative=Point.Empty;for(Control p=control;p!=panel;p=p.Parent)relative.Offset(p.Left,p.Top);g.TranslateTransform(-relative.X,-relative.Y);Panel(g,panel.ClientRectangle);g.TranslateTransform(relative.X,relative.Y);}}finally{g.Restore(state);}
 }
}
}
