using System;using System.Drawing;using System.Drawing.Drawing2D;using System.Drawing.Imaging;using System.Windows.Forms;using System.IO;using System.IO.Compression;
namespace CFEditor {
static class EditorColors {
 public static readonly Color Amp=Color.FromArgb(218,211,194),FX1=Color.FromArgb(64,239,38),FX2=Color.FromArgb(255,105,36),Boost=Color.FromArgb(255,181,36);
 public static Color Group(string group){return group=="CatalystGroupEffect"?FX1:group=="CatalystGroupReverb"?FX2:Amp;}
}
static class DialFrames {
 static readonly Bitmap[] frames=new Bitmap[127],disabled=new Bitmap[127];
 static DialFrames(){using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("knobs.zip"))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))using(var attributes=new ImageAttributes()){attributes.SetColorMatrix(new ColorMatrix(new[]{new[]{.299f,.299f,.299f,0f,0f},new[]{.587f,.587f,.587f,0f,0f},new[]{.114f,.114f,.114f,0f,0f},new[]{0f,0f,0f,.45f,0f},new[]{.12f,.12f,.12f,0f,1f}}));for(int i=0;i<frames.Length;i++)using(var entry=zip.GetEntry(i+".png").Open())using(var source=Image.FromStream(entry)){frames[i]=new Bitmap(90,90,PixelFormat.Format32bppPArgb);disabled[i]=new Bitmap(90,90,PixelFormat.Format32bppPArgb);using(var g=Graphics.FromImage(frames[i])){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(source,new Rectangle(0,0,90,90),0,0,180,180,GraphicsUnit.Pixel);}using(var g=Graphics.FromImage(disabled[i]))g.DrawImage(frames[i],new Rectangle(0,0,90,90),0,0,90,90,GraphicsUnit.Pixel,attributes);}}}
 public static Bitmap At(double amount,bool enabled){int i=Math.Max(0,Math.Min(126,(int)Math.Round(amount*126)));return enabled?frames[i]:disabled[i];}
}
class ActionButton:StableButton {
 public bool Active;public Color Accent=UITheme.Success;
 protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();}
 public ActionButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Font=UITheme.Bold;ForeColor=UITheme.Text;Height=36;Cursor=Cursors.Hand;Margin=new Padding(4);SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor|ControlStyles.ResizeRedraw,true);BackColor=Color.Transparent;}
 protected override void OnPaint(PaintEventArgs e){if(Width<4||Height<4)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;Color fill=Active?Color.FromArgb(14+Accent.R/7,24+Accent.G/6,29+Accent.B/7):Color.FromArgb(30,46,65);using(var p=UITheme.Rounded(new RectangleF(1,1,Width-3,Height-3),8))using(var b=new SolidBrush(fill))using(var pen=new Pen(Active?Color.FromArgb(170,Accent):Color.FromArgb(56,80,100),Active?1.5f:1)){e.Graphics.FillPath(b,p);e.Graphics.DrawPath(pen,p);}ClippedText.Draw(e.Graphics,Text,Font,ClientRectangle,Enabled?(Active?Accent:ForeColor):UITheme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.PreserveGraphicsTranslateTransform);if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-5,-5));}
}
sealed class Knob:Control {
 public readonly Binding Binding;int value;bool known,drag;int startY,startValue;
 public Color Accent=EditorColors.Amp;public event Action<Binding,object> Edited;
 public int DialPixels=90;public bool SmallLayout,ShowCaption=true;public string DisplayName;
 public bool Known{get{return known;}}public int Value{get{return value;}}public bool Dragging{get{return drag;}}
 public Knob(Binding b){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);Binding=b;Size=new Size(104,128);Margin=new Padding(3,3,3,1);BackColor=Color.Transparent;Font=UITheme.Bold;TabStop=true;AccessibleName=b.Param.name;AccessibleRole=AccessibleRole.Slider;}
 protected override void OnPaintBackground(PaintEventArgs e){BlackTheme.Behind(e.Graphics,this);}
 public void Read(object raw){if(drag)return;int v=raw is bool?((bool)raw?1:0):Convert.ToInt32(raw);if(v<Binding.Param.minimum||v>Binding.Param.maximum){if(known){known=false;Invalidate();}return;}if(known&&value==v)return;value=v;known=true;AccessibleDescription=Binding.Param.Display(v);Invalidate();}
 void Edit(int v){if(!Enabled||!known)return;v=Math.Max((int)Binding.Param.minimum,Math.Min((int)Binding.Param.maximum,v));if(v==value)return;value=v;Invalidate();if(Edited!=null)Edited(Binding,Binding.Param.kind=="bool"?(object)(value!=0):(object)value);}
 protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left||!known)return;Focus();if(Binding.Param.maximum==1){Edit(1-value);return;}drag=true;Capture=true;startY=e.Y;startValue=value;}
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(drag)Edit(startValue+(int)Math.Round((startY-e.Y)*(Binding.Param.maximum-Binding.Param.minimum)/150.0*(ModifierKeys==Keys.Shift?.1:1)));}
 protected override void OnMouseUp(MouseEventArgs e){drag=false;Capture=false;base.OnMouseUp(e);}
 protected override void OnMouseCaptureChanged(EventArgs e){if(!Capture)drag=false;base.OnMouseCaptureChanged(e);}
 protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);if(Focused)Edit(value+Math.Sign(e.Delta)*(ModifierKeys==Keys.Shift?1:Math.Max(1,(int)(Binding.Param.maximum-Binding.Param.minimum)/100)));}
 protected override bool IsInputKey(Keys k){return k==Keys.Up||k==Keys.Down||k==Keys.Left||k==Keys.Right||base.IsInputKey(k);}
 protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Up||e.KeyCode==Keys.Right)Edit(value+1);if(e.KeyCode==Keys.Down||e.KeyCode==Keys.Left)Edit(value-1);if(e.KeyCode==Keys.Home)Edit((int)Binding.Param.minimum);if(e.KeyCode==Keys.End)Edit((int)Binding.Param.maximum);}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;Color accent=Enabled?Accent:UITheme.Muted;
  double zoom=EditorZoom.Factor;string caption=DisplayName??Binding.Param.name;int valueY=SmallLayout?DialPixels+25:0,dialY=SmallLayout?21:24;
  ClippedText.Draw(g,known?Binding.Param.Display(value):"—",Font,new Rectangle(0,EditorZoom.Px(valueY),Width,EditorZoom.Px(22)),known?accent:UITheme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
  float diameter=Math.Min(EditorZoom.Px(DialPixels-2),Width-EditorZoom.Px(6));var r=new RectangleF((Width-diameter)/2,EditorZoom.Px(dialY),diameter,diameter);double t=(value-Binding.Param.minimum)/Math.Max(1,Binding.Param.maximum-Binding.Param.minimum);
  using(var p=new Pen(Color.FromArgb(61,72,84),(float)(5*zoom)))g.DrawArc(p,r,135,270);if(known&&t>0)using(var p=new Pen(accent,(float)(5*zoom))){p.StartCap=p.EndCap=LineCap.Round;g.DrawArc(p,r,135,(float)(270*t));}
  int pixels=Math.Min(EditorZoom.Px(DialPixels),Width);var frame=DialFrames.At(known?t:0,Enabled);if(pixels==90)g.DrawImageUnscaled(frame,(Width-pixels)/2+EditorZoom.Px(3),EditorZoom.Px(dialY+2));else g.DrawImage(frame,new Rectangle((Width-pixels)/2+EditorZoom.Px(3),EditorZoom.Px(dialY+2),pixels,pixels));
  if(ShowCaption)ClippedText.Draw(g,AppLanguage.Text(caption),Font,new Rectangle(0,EditorZoom.Px(SmallLayout?0:110),Width,EditorZoom.Px(18)),Enabled?Accent:UITheme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);if(Focused)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-2,-2));
 }
}
sealed class ParamStrip:FlowLayoutPanel {
 public ParamStrip(){DoubleBuffered=true;SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;WrapContents=true;Dock=DockStyle.Top;BackColor=Color.Transparent;Padding=new Padding(2);}
}
}
