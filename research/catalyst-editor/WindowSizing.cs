using System;using System.Drawing;using System.Runtime.InteropServices;using System.Windows.Forms;
namespace CFEditor {
sealed partial class EditorForm {
 bool sizingWindow,fittingWindow;double sizingAspect;int naturalHeight=840;
 static double FitScreenScale(Size work,Size frame,int logicalHeight){return Math.Max(.4,Math.Min(1.6,Math.Min((work.Width-frame.Width-32)/1440.0,(work.Height-frame.Height-32)/(double)logicalHeight)));}
 static double FitOpeningScale(Size work,Size frame,int logicalHeight){return Math.Min(.8,FitScreenScale(work,frame,logicalHeight));}
 protected override void OnLoad(EventArgs e){
  base.OnLoad(e);if(!defaultWindowFit)return;
  PerformLayout();foreach(Control c in Controls)c.PerformLayout();ArrangeSound(true);
  var work=Screen.FromControl(this).WorkingArea;double factor=FitOpeningScale(work.Size,new Size(Width-ClientSize.Width,Height-ClientSize.Height),naturalHeight);
  ApplyZoom(factor);ClientSize=new Size(S(1440),S(naturalHeight));ArrangeSound(true);
  if(StartPosition!=FormStartPosition.Manual)Location=new Point(work.Left+(work.Width-Width)/2,work.Top+(work.Height-Height)/2);
 }
 [StructLayout(LayoutKind.Sequential)]struct SizeRect{public int Left,Top,Right,Bottom;}
 static bool Corner(int side){return side==4||side==5||side==7||side==8;}
 static int ResizeHit(int hit){return hit==10||hit==11||hit==12||hit==15?18:hit;}
 // Project the corner onto the aspect line so a diagonal also shrinks smoothly.
 static Rectangle DiagonalBounds(Rectangle proposed,int side,Size frame,Size minimum,double aspect){
  double cw=Math.Max(1,proposed.Width-frame.Width),ch=Math.Max(1,proposed.Height-frame.Height);
  int width=(int)Math.Round((cw+ch/aspect)/(1+1/(aspect*aspect)));
  width=Math.Max(width,Math.Max(minimum.Width-frame.Width,(int)Math.Ceiling((minimum.Height-frame.Height)*aspect)));
  int height=(int)Math.Round(width/aspect)+frame.Height;width+=frame.Width;
  int x=side==4||side==7?proposed.Right-width:proposed.Left;
  int y=side==4||side==5?proposed.Bottom-height:proposed.Top;
  return new Rectangle(x,y,width,height);
 }
 void FitSoundWindow(int need){
  if(sizingWindow||fittingWindow||EditorZoom.Applying)return;
  int next=Math.Max(760,(int)Math.Round(need/EditorZoom.Factor));
  int frameHeight=Height-ClientSize.Height,frameWidth=Width-ClientSize.Width;
  MinimumSize=new Size((int)Math.Ceiling(1440*.4)+frameWidth,(int)Math.Ceiling(next*.4)+frameHeight);
  MaximumSize=new Size((int)Math.Round(1440*1.6)+frameWidth,(int)Math.Round(next*1.6)+frameHeight);
  if(Math.Abs(next-naturalHeight)<=2)return;naturalHeight=next;fittingWindow=true;
  try{if(defaultWindowFit){var work=Screen.FromControl(this).WorkingArea;double fit=FitScreenScale(work.Size,new Size(frameWidth,frameHeight),naturalHeight);if(EditorZoom.Factor>fit)ApplyZoom(fit);}ClientSize=new Size(S(1440),S(naturalHeight));}finally{fittingWindow=false;}
 }
 void ZoomForWindow(){if(sizingWindow&&!EditorZoom.Applying)ApplyZoom(ClientSize.Width/1440.0);}
 protected override void WndProc(ref Message message){
  if(message.Msg==0x112){long command=message.WParam.ToInt64();if((command&0xfff0)==0xf030)return;if((command&0xfff0)==0xf000&&!Corner((int)(command&15)))return;}
  if(message.Msg==0x231){sizingWindow=true;sizingAspect=1440.0/naturalHeight;}
  if(message.Msg==0x214&&Corner(message.WParam.ToInt32())){
   var rect=(SizeRect)Marshal.PtrToStructure(message.LParam,typeof(SizeRect));
   double aspect=sizingAspect>0?sizingAspect:1440.0/naturalHeight;
   var next=DiagonalBounds(Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom),message.WParam.ToInt32(),new Size(Width-ClientSize.Width,Height-ClientSize.Height),MinimumSize,aspect);
   rect.Left=next.Left;rect.Top=next.Top;rect.Right=next.Right;rect.Bottom=next.Bottom;Marshal.StructureToPtr(rect,message.LParam,false);message.Result=new IntPtr(1);return;
  }
  base.WndProc(ref message);
  if(message.Msg==0x84)message.Result=new IntPtr(ResizeHit(message.Result.ToInt32()));
  if(message.Msg==0x232){sizingWindow=false;ArrangeSound();}
 }
}
}
