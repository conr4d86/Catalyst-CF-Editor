using System;using System.Drawing;using System.Drawing.Drawing2D;using System.Windows.Forms;
namespace CFEditor {
sealed class TunerDisplay:Control {
 bool active;int note=-1,cents;bool centsKnown;
 static readonly Font NoteFont=new Font("Segoe UI",32,FontStyle.Bold),ValueFont=new Font("Segoe UI",14,FontStyle.Bold),HintFont=new Font("Segoe UI",11);
 public TunerDisplay(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor|ControlStyles.ResizeRedraw,true);BackColor=Color.Transparent;Height=116;AccessibleName="Afinador";AccessibleRole=AccessibleRole.StaticText;}
 internal Color IndicatorColor{get{return !Reading?UITheme.Muted:cents<0?UITheme.Error:UITheme.Success;}}
 internal Color NoteColor{get{return Reading?Color.White:UITheme.Muted;}}
 internal bool Reading{get{return active&&note>=0&&note<12&&centsKnown;}}
 internal string NoteText{get{return active&&note>=0&&note<12?EngineUnits.TunerNote(note):"—";}}
 internal string ReadingText{get{return !active?"Afinador desactivado":!Reading?"Toca una nota":(cents>0?"+":"")+cents+" "+(Math.Abs(cents)==1?"cent":"cents");}}
 void Changed(){AccessibleDescription=NoteText+" · "+ReadingText;Invalidate();}
 public void SetActive(object value){bool next=Convert.ToBoolean(value);if(active==next)return;active=next;centsKnown=false;note=-1;Changed();}
 public void SetNote(object value){int next=Convert.ToInt32(value);if(next<0||next>=12)next=-1;if(note==next)return;note=next;Changed();}
 public void SetCents(object value){int next=Convert.ToInt32(value);bool valid=next>=-50&&next<=50;if(cents==next&&centsKnown==valid)return;cents=next;centsKnown=valid;Changed();}
 protected override void OnPaintBackground(PaintEventArgs e){BlackTheme.Behind(e.Graphics,this);}
 protected override void OnPaint(PaintEventArgs e){
  if(Width<80||Height<30)return;var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
  float z=(float)EditorZoom.Factor,mid=Width/2f,half=Math.Min(60*z,Width*.15f),left=14*z,right=Width-14*z;
  Color accent=IndicatorColor;
  float y=43*z;
  for(int side=-1;side<=1;side+=2)for(int tick=1;tick<=10;tick++){
   float limit=side<0?mid-half-14*z-left:right-mid-half-14*z;
   float x=mid+side*(half+14*z+limit*(tick-1)/9f);float height=(tick%2==0?19:12)*z;
   bool lit=Reading&&Math.Sign(cents)==side&&tick<=Math.Max(1,(int)Math.Ceiling(Math.Abs(cents)/5.0));
   using(var pen=new Pen(lit?accent:Color.FromArgb(65,77,84),Math.Max(1,(lit?4:2)*z)))g.DrawLine(pen,x,y-height/2,x,y+height/2);
  }
  var box=new RectangleF(mid-half,8*z,half*2,68*z);
  using(var shape=UITheme.Rounded(box,10*z))using(var fill=new SolidBrush(Color.FromArgb(22,25,27)))using(var pen=new Pen(accent,Math.Max(1,1.6f*z))){g.FillPath(fill,shape);g.DrawPath(pen,shape);}
  ClippedText.Draw(g,NoteText,EditorZoom.Font(NoteFont),Rectangle.Round(box),NoteColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
  var label=new Rectangle(0,EditorZoom.Px(79),Width,Math.Max(EditorZoom.Px(26),Height-EditorZoom.Px(79)));
  ClippedText.Draw(g,AppLanguage.Text(ReadingText),EditorZoom.Font(ValueFont),label,accent,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
  ClippedText.Draw(g,AppLanguage.Text("Baja"),EditorZoom.Font(HintFont),new Rectangle(0,EditorZoom.Px(79),Math.Max(1,(int)(mid-half-18*z)),EditorZoom.Px(26)),UITheme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
  ClippedText.Draw(g,AppLanguage.Text("Alta"),EditorZoom.Font(HintFont),new Rectangle((int)(mid+half+18*z),EditorZoom.Px(79),Math.Max(1,Width-(int)(mid+half+18*z)),EditorZoom.Px(26)),UITheme.Muted,TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
 }
}
}
