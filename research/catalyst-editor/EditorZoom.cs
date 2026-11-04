using System;using System.Collections.Generic;using System.ComponentModel;using System.Drawing;using System.Linq;using System.Windows.Forms;
namespace CFEditor {
static class EditorZoom {
 public static double Factor=1;public static bool Applying;
 static readonly Dictionary<string,Font> fonts=new Dictionary<string,Font>();
 public static int Px(double value){return (int)Math.Round(value*Factor);}
 public static Font Font(Font original){if(Math.Abs(Factor-1)<.001)return original;float points=(float)Math.Round(original.SizeInPoints*Factor,1);string key=original.Name+"|"+points+"|"+original.Style;Font result;if(!fonts.TryGetValue(key,out result)){result=new Font(original.FontFamily,points,original.Style,GraphicsUnit.Point);fonts.Add(key,result);}return result;}
}
sealed partial class EditorForm {
 readonly Dictionary<Control,Font> zoomFonts=new Dictionary<Control,Font>();
 sealed class ZoomMetric {public RectangleF Bounds;public float[] Padding,Margin,Rows;}
 readonly Dictionary<Control,ZoomMetric> zoomMetrics=new Dictionary<Control,ZoomMetric>();
 void PruneZoom(){foreach(var control in zoomFonts.Keys.Where(c=>c.IsDisposed).ToArray())zoomFonts.Remove(control);foreach(var control in zoomMetrics.Keys.Where(c=>c.IsDisposed).ToArray())zoomMetrics.Remove(control);}
 float[] Units(Padding p,double factor){return new[]{(float)(p.Left/factor),(float)(p.Top/factor),(float)(p.Right/factor),(float)(p.Bottom/factor)};}
 Padding Pixels(float[] units){return new Padding(S(units[0]),S(units[1]),S(units[2]),S(units[3]));}
 void CaptureMetrics(Control root,double factor){foreach(Control c in root.Controls){if(!zoomMetrics.ContainsKey(c)){var metric=new ZoomMetric{Bounds=new RectangleF((float)(c.Left/factor),(float)(c.Top/factor),(float)(c.Width/factor),(float)(c.Height/factor)),Padding=Units(c.Padding,factor),Margin=Units(c.Margin,factor)};var table=c as TableLayoutPanel;if(table!=null){metric.Rows=new float[table.RowStyles.Count];for(int i=0;i<metric.Rows.Length;i++)metric.Rows[i]=table.RowStyles[i].SizeType==SizeType.Absolute?(float)(table.RowStyles[i].Height/factor):-1;}zoomMetrics[c]=metric;}CaptureMetrics(c,factor);}}
 void RestoreMetrics(Control c){ZoomMetric metric;if(zoomMetrics.TryGetValue(c,out metric)){c.Padding=Pixels(metric.Padding);c.Margin=Pixels(metric.Margin);var table=c as TableLayoutPanel;if(table!=null&&metric.Rows!=null)for(int i=0;i<Math.Min(metric.Rows.Length,table.RowStyles.Count);i++)if(metric.Rows[i]>=0)table.RowStyles[i].Height=S(metric.Rows[i]);if(!c.AutoSize){if(c.Dock==DockStyle.None)c.Size=new Size(S(metric.Bounds.Width),S(metric.Bounds.Height));else if(c.Dock==DockStyle.Top||c.Dock==DockStyle.Bottom)c.Height=S(metric.Bounds.Height);else if(c.Dock==DockStyle.Left||c.Dock==DockStyle.Right)c.Width=S(metric.Bounds.Width);}if(c.Dock==DockStyle.None&&!(c.Parent is FlowLayoutPanel)&&!(c.Parent is TableLayoutPanel))c.Location=new Point(S(metric.Bounds.X),S(metric.Bounds.Y));}foreach(Control child in c.Controls)RestoreMetrics(child);}
 int S(double value){return EditorZoom.Px(value);}
 void RememberFonts(Control root){foreach(Control c in root.Controls){if(!zoomFonts.ContainsKey(c)){bool own=TypeDescriptor.GetProperties(c)["Font"].ShouldSerializeValue(c);zoomFonts[c]=!own&&c.Parent!=null&&zoomFonts.ContainsKey(c.Parent)?zoomFonts[c.Parent]:c.Font;}RememberFonts(c);}}
 internal void ScaleFreshControls(Control root){foreach(Control c in root.Controls){if(!zoomFonts.ContainsKey(c)){RememberBranch(c);EditorZoom.Applying=true;try{if(Math.Abs(EditorZoom.Factor-1)>.001){CaptureMetrics(root,1);c.Scale(new SizeF((float)EditorZoom.Factor,(float)EditorZoom.Factor));RestoreMetrics(c);}ApplyFonts(c);}finally{EditorZoom.Applying=false;}}else ScaleFreshControls(c);}}
 void RememberBranch(Control c){bool own=TypeDescriptor.GetProperties(c)["Font"].ShouldSerializeValue(c);zoomFonts[c]=!own&&c.Parent!=null&&zoomFonts.ContainsKey(c.Parent)?zoomFonts[c.Parent]:c.Font;RememberFonts(c);}
 void ApplyFonts(Control c){Font original;if(zoomFonts.TryGetValue(c,out original))c.Font=EditorZoom.Font(original);var combo=c as ComboBox;if(combo!=null&&combo.DrawMode==DrawMode.OwnerDrawFixed)combo.ItemHeight=S(22);foreach(Control child in c.Controls)ApplyFonts(child);}
 void ApplyZoom(double factor){
  factor=Math.Max(.4,Math.Min(1.6,factor));if(Math.Abs(factor-EditorZoom.Factor)<.001)return;
  RememberFonts(this);CaptureMetrics(this,EditorZoom.Factor);double ratio=factor/EditorZoom.Factor;EditorZoom.Applying=true;var tree=AllChildren(this).ToArray();SuspendLayout();foreach(var c in tree)c.SuspendLayout();
  try{EditorZoom.Factor=factor;foreach(Control c in Controls){c.Scale(new SizeF((float)ratio,(float)ratio));ApplyFonts(c);RestoreMetrics(c);if(c.Dock==DockStyle.Fill)c.Bounds=ClientRectangle;}}
  finally{foreach(var c in tree.Reverse())c.ResumeLayout(false);ResumeLayout(true);foreach(var c in tree)c.PerformLayout();EditorZoom.Applying=false;}
  if(firmware!=null)firmware.Relayout();Invalidate(true);
 }
}
}
