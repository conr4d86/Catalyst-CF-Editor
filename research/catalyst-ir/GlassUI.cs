using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

static class UITheme {
 public static readonly Color Text=Color.FromArgb(225,234,246),Muted=Color.FromArgb(158,174,195),Success=Color.FromArgb(105,225,166),Warning=Color.FromArgb(239,195,119),Error=Color.FromArgb(249,143,157);
 public static readonly Font Body=new Font("Segoe UI",9.5f),Bold=new Font("Segoe UI",9.5f,FontStyle.Bold),Small=new Font("Segoe UI",8.5f),SmallBold=new Font("Segoe UI",8.5f,FontStyle.Bold),Heading=new Font("Segoe UI",25f,FontStyle.Bold),Status=new Font("Segoe UI",10.5f,FontStyle.Bold);
 public static readonly Font Action=new Font("Segoe UI",14f,FontStyle.Bold);
 public static readonly Font ActionPrompt=new Font("Segoe UI",16f,FontStyle.Bold);
 public static GraphicsPath Rounded(RectangleF r,float radius){
  float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));var p=new GraphicsPath();
  p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
 }
 public static void Background(Graphics g,Rectangle r){
  if(r.Width<1||r.Height<1)return;
  using(var b=new LinearGradientBrush(r,Color.FromArgb(16,25,41),Color.FromArgb(9,16,28),50))g.FillRectangle(b,r);
  using(var path=new GraphicsPath()){
   path.AddEllipse(r.Width-420,-200,620,500);
   using(var glow=new PathGradientBrush(path)){glow.CenterColor=Color.FromArgb(40,63,175,143);glow.SurroundColors=new[]{Color.Transparent};g.FillPath(glow,path);}
  }
 }
}

class GlassPanel : Panel {
 public GlassPanel(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;}
 protected override void OnPaintBackground(PaintEventArgs e){
  base.OnPaintBackground(e);if(Width<3||Height<3)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
  var r=new RectangleF(1,1,Width-3,Height-3);
  using(var p=UITheme.Rounded(r,14))using(var b=new LinearGradientBrush(ClientRectangle,Color.FromArgb(168,36,51,72),Color.FromArgb(178,18,29,45),90))using(var pen=new Pen(Color.FromArgb(75,123,153,180))){e.Graphics.FillPath(b,p);e.Graphics.DrawPath(pen,p);}
 }
}

class AmpPicture : Control {
 Bitmap bitmap;
 Rectangle source;
 public AmpPicture(){
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;
  using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("amplifier.png")){
   if(stream!=null){using(var original=Image.FromStream(stream))bitmap=new Bitmap(original);}
  }
  if(bitmap!=null){
   int x0=bitmap.Width,y0=bitmap.Height,x1=0,y1=0;
   for(int y=0;y<bitmap.Height;y+=3)for(int x=0;x<bitmap.Width;x+=3)if(bitmap.GetPixel(x,y).A>32){x0=Math.Min(x0,x);y0=Math.Min(y0,y);x1=Math.Max(x1,x);y1=Math.Max(y1,y);}
   source=Rectangle.FromLTRB(Math.Max(0,x0-4),Math.Max(0,y0-4),Math.Min(bitmap.Width,x1+5),Math.Min(bitmap.Height,y1+5));
  }
 }
 protected override void OnPaint(PaintEventArgs e){
  base.OnPaintBackground(e);if(Width<4||Height<4)return;
  e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
  using(var p=UITheme.Rounded(new RectangleF(1,1,Width-3,Height-3),16))using(var b=new LinearGradientBrush(ClientRectangle,Color.FromArgb(105,62,100,107),Color.FromArgb(60,24,40,61),70))using(var pen=new Pen(Color.FromArgb(85,123,164,166))){e.Graphics.FillPath(b,p);e.Graphics.DrawPath(pen,p);}
  if(bitmap!=null&&source.Width>0&&source.Height>0){
   float scale=Math.Min((Width-15f)/source.Width,(Height-15f)/source.Height);float w=source.Width*scale,h=source.Height*scale;
   e.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;e.Graphics.DrawImage(bitmap,new RectangleF((Width-w)/2,(Height-h)/2,w,h),source,GraphicsUnit.Pixel);
  }
 }
 protected override void Dispose(bool disposing){if(disposing&&bitmap!=null)bitmap.Dispose();base.Dispose(disposing);}
}

class GlassNumber : NumericUpDown {
 public bool Successful;
 public GlassNumber(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Font=UITheme.Bold;BackColor=Color.FromArgb(15,26,42);ForeColor=UITheme.Text;BorderStyle=BorderStyle.FixedSingle;}
 void ChildrenVisible(){foreach(Control child in Controls)child.Visible=Enabled;Invalidate();}
 protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);ChildrenVisible();}
 protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);ChildrenVisible();}
 protected override void OnValueChanged(EventArgs e){base.OnValueChanged(e);Invalidate();}
 protected override void OnPaint(PaintEventArgs e){
  base.OnPaint(e);if(Enabled)return;
  e.Graphics.Clear(BackColor);var color=Successful?UITheme.Success:UITheme.Muted;
  TextRenderer.DrawText(e.Graphics,Value.ToString("F"+DecimalPlaces),Font,new Rectangle(6,0,Math.Max(1,Width-12),Height),color,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
 }
}

class GlassProgress : ProgressBar {
 readonly Timer timer;
 float position;
 public GlassProgress(){
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
  MarqueeAnimationSpeed=0;timer=new Timer{Interval=40};timer.Tick+=delegate{position+=Math.Max(4,Width*.015f);if(position>Width*1.3f)position=0;Invalidate();};
 }
 public new ProgressBarStyle Style {get{return base.Style;}set{base.Style=value;if(timer!=null)timer.Enabled=value==ProgressBarStyle.Marquee;Invalidate();}}
 public new int Value {get{return base.Value;}set{base.Value=value;Invalidate();}}
 protected override void OnPaint(PaintEventArgs e){
  e.Graphics.Clear(Color.FromArgb(13,22,36));if(Width<4||Height<3)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
  using(var p=UITheme.Rounded(new RectangleF(0,0,Width-1,Height-1),4))using(var b=new SolidBrush(Color.FromArgb(37,54,75)))e.Graphics.FillPath(b,p);
  float width=Style==ProgressBarStyle.Marquee?Width*.26f:Width*(Value-Minimum)/(float)Math.Max(1,Maximum-Minimum);
  if(width<1)return;float x=Style==ProgressBarStyle.Marquee?position-width:0;var rect=RectangleF.Intersect(new RectangleF(x,0,width,Height-1),new RectangleF(0,0,Width-1,Height-1));
  if(rect.Width>0)using(var p=UITheme.Rounded(rect,4))using(var b=new SolidBrush(UITheme.Success))e.Graphics.FillPath(b,p);
 }
 protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
