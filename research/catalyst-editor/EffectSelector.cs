using System;using System.Drawing;using System.Linq;using System.Windows.Forms;
namespace CFEditor {
sealed class EffectHeading {public readonly string Text;public EffectHeading(string text){Text=text;}public override string ToString(){return "— "+Text+" —";}}
// Headers are presentation only; an asset ID can never be replaced by one.
sealed class AssetComboBox:ComboBox {
 int accepted=-1;bool restoring,rejectedHeader;
 public void AddEffects(Asset[] assets){foreach(string family in Catalog.EffectFamilies){var members=assets.Where(a=>Catalog.EffectFamily(a)==family).ToArray();if(members.Length==0)continue;Items.Add(new EffectHeading(family));Items.AddRange(members);}}
 protected override void OnSelectedIndexChanged(EventArgs e){
  if(restoring)return;
  if(SelectedItem is EffectHeading){rejectedHeader=true;restoring=true;try{SelectedIndex=accepted;}finally{restoring=false;}return;}
  accepted=SelectedIndex;rejectedHeader=false;base.OnSelectedIndexChanged(e);
 }
 protected override void OnSelectionChangeCommitted(EventArgs e){if(rejectedHeader){rejectedHeader=false;if(IsHandleCreated)BeginInvoke((Action)(()=>{if(!IsDisposed)DroppedDown=true;}));return;}base.OnSelectionChangeCommitted(e);}
 protected override void OnKeyDown(KeyEventArgs e){
  if(!e.Alt&&(e.KeyCode==Keys.Down||e.KeyCode==Keys.Up||e.KeyCode==Keys.Home||e.KeyCode==Keys.End||e.KeyCode==Keys.PageDown||e.KeyCode==Keys.PageUp)){
   int direction=(e.KeyCode==Keys.Up||e.KeyCode==Keys.PageUp||e.KeyCode==Keys.End)?-1:1;
   int target=e.KeyCode==Keys.Home?0:e.KeyCode==Keys.End?Items.Count-1:SelectedIndex+(e.KeyCode==Keys.PageDown?8:e.KeyCode==Keys.PageUp?-8:direction);
   target=Math.Max(0,Math.Min(Items.Count-1,target));while(target>=0&&target<Items.Count&&Items[target] is EffectHeading)target+=direction;
   if(target<0){target=0;while(target<Items.Count&&Items[target] is EffectHeading)target++;}if(target>=0&&target<Items.Count)SelectedIndex=target;e.Handled=e.SuppressKeyPress=true;return;
  }base.OnKeyDown(e);
 }
 protected override void OnDrawItem(DrawItemEventArgs e){
  base.OnDrawItem(e);if(e.Index<0||!(Items[e.Index] is EffectHeading))return;
  using(var fill=new SolidBrush(Color.FromArgb(15,24,32)))e.Graphics.FillRectangle(fill,e.Bounds);
  using(var pen=new Pen(Color.FromArgb(57,75,85)))e.Graphics.DrawLine(pen,e.Bounds.Left+4,e.Bounds.Top,e.Bounds.Right-4,e.Bounds.Top);
  ClippedText.Draw(e.Graphics,AppLanguage.Text(((EffectHeading)Items[e.Index]).Text.ToUpperInvariant()),Font,Rectangle.Inflate(e.Bounds,-7,0),Color.FromArgb(126,166,156),TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
 }
}
}
