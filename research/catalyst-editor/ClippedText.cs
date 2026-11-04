using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// GDI text must keep the clip and parent translation during transparent painting.
static class ClippedText {
 public static void Draw(Graphics g,string text,Font font,Rectangle bounds,Color color,TextFormatFlags flags){
  if(bounds.Width<=0||bounds.Height<=0)return;
  var state=g.Save();
  try{
   g.SetClip(bounds,CombineMode.Intersect);
   TextRenderer.DrawText(g,text,font,bounds,color,flags|TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.PreserveGraphicsTranslateTransform);
  }finally{g.Restore(state);}
 }
}
