using System;using System.IO;using System.Linq;using System.Drawing;using System.Collections.Generic;using System.Web.Script.Serialization;using System.Windows.Forms;
namespace CFEditor {
sealed partial class EditorForm {
 void AuditTypography(string folder){
  var report=new List<object>();
  for(int page=0;page<nav.Count;page++){
   ShowPage(page);Application.DoEvents();if(page==0)ArrangeSound();
   foreach(Control c in AllChildren(pages[PageSurface(page)]).Where(c=>c.Visible&&(c is Label||c is IButtonControl||c is CheckBox||c is ComboBox||c is NumericUpDown))){
    TestNeed(c.Text.IndexOf('\ufffd')<0,"Invalid character in visible copy: "+c.Text);
    TestNeed(c.Font.SizeInPoints>=9,"Text below the minimum size: "+c.Text);
    var label=c as Label;
    if(label!=null&&!string.IsNullOrWhiteSpace(label.Text)){
     int line=TextRenderer.MeasureText("Ag",label.Font,Size.Empty,TextFormatFlags.SingleLine|TextFormatFlags.NoPadding).Height;
     TestNeed(label.Height>=line,"Label too short for its font: "+label.Text+" "+label.Height+" < "+line);
    }
    report.Add(new{page=page,text=c.Text,type=c.GetType().Name,size=c.Font.SizeInPoints,bold=c.Font.Bold,bounds=c.Bounds.ToString()});
   }
  }
  TestNeed(nav.All(b=>b.Font.SizeInPoints==10&&b.Font.Bold),"Inconsistent navigation typography");
  File.WriteAllText(Path.Combine(folder,"typography.json"),new JavaScriptSerializer().Serialize(report));
 }
}
}
