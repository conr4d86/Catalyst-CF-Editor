using System;using System.Drawing;using System.Runtime.InteropServices;using System.Windows.Forms;
namespace CFEditor {
// Read-only activity log with an owner-drawn scrollbar. No system theme or
// global Windows setting is changed; native text selection and copying remain.
sealed class DarkLogBox:UserControl {
 readonly LogText text;readonly LogScroll scroll;bool layingOut;
 [DllImport("user32.dll",CharSet=CharSet.Auto)]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
 public DarkLogBox(){DoubleBuffered=true;BackColor=Color.FromArgb(13,15,18);text=new LogText(this){Multiline=true,ReadOnly=true,WordWrap=true,ScrollBars=ScrollBars.None,BorderStyle=BorderStyle.None};scroll=new LogScroll(this);Controls.Add(text);Controls.Add(scroll);ApplyStyle();}
 void ApplyStyle(){if(text==null)return;text.BackColor=BackColor;text.ForeColor=ForeColor;text.Font=Font;scroll.Invalidate();}
 protected override void OnBackColorChanged(EventArgs e){base.OnBackColorChanged(e);ApplyStyle();}
 protected override void OnForeColorChanged(EventArgs e){base.OnForeColorChanged(e);ApplyStyle();}
 protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);ApplyStyle();LayoutLog();}
 protected override void OnResize(EventArgs e){base.OnResize(e);LayoutLog();}
 void LayoutLog(){if(text==null||layingOut)return;layingOut=true;try{int width=Math.Max(12,(int)Math.Round(14*DeviceDpiScale));text.SetBounds(0,0,Math.Max(1,Width-width-3),Height);scroll.SetBounds(Math.Max(0,Width-width),0,width,Height);Sync();}finally{layingOut=false;}}
 float DeviceDpiScale{get{return Font==null?1:Math.Max(.65f,Font.SizeInPoints/11.5f);}}
 internal int FirstLine{get{return text.IsHandleCreated?(int)SendMessage(text.Handle,0xCE,IntPtr.Zero,IntPtr.Zero):0;}}
 internal int VisibleLines{get{return Math.Max(1,text.ClientSize.Height/Math.Max(1,text.Font.Height));}}
 internal int LastScrollLine{get{return text.IsHandleCreated?Math.Max(0,(int)SendMessage(text.Handle,0xBA,IntPtr.Zero,IntPtr.Zero)-VisibleLines):0;}}
 internal void ScrollToLine(int line){if(!text.IsHandleCreated)return;line=Math.Max(0,Math.Min(LastScrollLine,line));SendMessage(text.Handle,0xB6,IntPtr.Zero,(IntPtr)(line-FirstLine));Sync();}
 internal void Wheel(int delta){int lines=SystemInformation.MouseWheelScrollLines;if(lines<0)lines=VisibleLines;if(lines==0)return;ScrollToLine(FirstLine-(int)Math.Round(delta/120.0*lines));}
 internal void Sync(){if(scroll!=null)scroll.Invalidate();}
 public void AppendText(string value){int previous=FirstLine;bool follow=previous>=LastScrollLine;text.AppendText(value);if(follow){text.SelectionStart=text.TextLength;text.SelectionLength=0;text.ScrollToCaret();}else ScrollToLine(previous);Sync();}
 sealed class LogText:TextBox {readonly DarkLogBox owner;public LogText(DarkLogBox value){owner=value;}
  protected override void OnMouseWheel(MouseEventArgs e){owner.Wheel(e.Delta);}
  protected override void WndProc(ref Message message){base.WndProc(ref message);if(owner!=null&&(message.Msg==0x115||message.Msg==0x101||message.Msg==0x200||message.Msg==0x202||message.Msg==0xC||message.Msg==0xC2))owner.Sync();}
 }
 sealed class LogScroll:Control {readonly DarkLogBox owner;bool dragging,hover;int grab;
  public LogScroll(DarkLogBox value){owner=value;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);TabStop=false;AccessibleName="Desplazar actividad";AccessibleRole=AccessibleRole.ScrollBar;}
  Rectangle Thumb(){int rail=Math.Max(1,Height-4),range=owner.LastScrollLine;int height=range==0?rail:Math.Min(rail,Math.Max(24,(int)Math.Round(rail*owner.VisibleLines/(double)(range+owner.VisibleLines))));int travel=rail-height;return new Rectangle(3,2+(range==0?0:(int)Math.Round(travel*owner.FirstLine/(double)range)),Math.Max(2,Width-6),height);}
  protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Color.FromArgb(18,22,27));var thumb=Thumb();using(var brush=new SolidBrush(owner.LastScrollLine==0?Color.FromArgb(38,45,53):dragging?Color.FromArgb(119,145,157):hover?Color.FromArgb(91,112,126):Color.FromArgb(65,80,93)))e.Graphics.FillRectangle(brush,thumb);}
  protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left||owner.LastScrollLine==0)return;var thumb=Thumb();if(thumb.Contains(e.Location)){dragging=true;grab=e.Y-thumb.Top;Capture=true;Invalidate();}else owner.ScrollToLine(owner.FirstLine+(e.Y<thumb.Top?-owner.VisibleLines:owner.VisibleLines));}
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(!dragging)return;var thumb=Thumb();int travel=Height-4-thumb.Height;if(travel>0)owner.ScrollToLine((int)Math.Round((e.Y-grab-2)*owner.LastScrollLine/(double)travel));}
  protected override void OnMouseUp(MouseEventArgs e){dragging=false;Capture=false;Invalidate();base.OnMouseUp(e);}
  protected override void OnMouseCaptureChanged(EventArgs e){if(!Capture){dragging=false;Invalidate();}base.OnMouseCaptureChanged(e);}
  protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
  protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
  protected override void OnMouseWheel(MouseEventArgs e){owner.Wheel(e.Delta);}
 }
}
}
