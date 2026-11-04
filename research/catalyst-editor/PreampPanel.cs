using System;using System.Drawing;using System.Linq;using System.Windows.Forms;
namespace CFEditor {
sealed class PreampPanel:GlassPanel {
 internal Action ArrangeControls;
 public PreampPanel(){Width=220;Height=224;Margin=new Padding(3,3,3,1);Padding=Padding.Empty;}
}
static class PreampCapability {
 // The native adapter remains disabled until a receiver has positively
 // announced the corresponding protocol. Preview is an explicit simulator.
 public static bool Available(IBackend backend){var native=backend as NativeBackend;return backend.Preview||native!=null&&native.ExtendedPreampAvailable;}
}
sealed partial class EditorForm {
 PreampPanel BuildPreamp(string group,Asset asset){
  var box=new PreampPanel();var title=LabelText("Gain / Drive",true);title.AutoSize=false;title.TextAlign=ContentAlignment.MiddleCenter;box.Controls.Add(title);
  var amount=new Binding(group,asset.parameters.First(p=>p.id=="Boost"),true,asset.id);
  var drive=new Knob(amount){ShowCaption=false,Accent=EditorColors.Amp,AccessibleName="Gain / Drive"};drive.Edited+=Queue;Bind(amount,drive.Read);Track(drive,amount);box.Controls.Add(drive);
  Func<string,string,Binding> setting=(id,name)=>{var p=Catalog.Int(id,name,0,100);p.format=2;p.displayMinimum=0;p.displayMaximum=100;return new Binding("CFPreamp",p,true,asset.id);};
  var levelBinding=setting("level","Level");var toneBinding=setting("tone","Tone");
  var level=new Knob(levelBinding){DialPixels=38,SmallLayout=true,DisplayName="Level",Font=UITheme.SmallBold,Accent=EditorColors.Amp};var tone=new Knob(toneBinding){DialPixels=38,SmallLayout=true,DisplayName="Tone",Font=UITheme.SmallBold,Accent=EditorColors.Amp};
  foreach(var dial in new[]{level,tone}){dial.Edited+=Queue;Bind(dial.Binding,dial.Read);Track(dial,dial.Binding);box.Controls.Add(dial);}
  var typeBinding=new Binding("CFPreamp",Catalog.Int("type","Preamp",0,3,new[]{"Boost","EP Boost","808","SD-1"}),true,asset.id);var select=PrepareCombo(new ComboBox(),130,typeBinding.Param.choices);select.ForeColor=EditorColors.Boost;select.AccessibleName="Tipo de preamp";select.SelectedIndex=0;bool selecting=false;int model=0;
  var epBoost=new ActionButton{Text="Boost",Accent=EditorColors.Boost,Font=UITheme.SmallBold};var bright=new ActionButton{Text="Bright",Accent=EditorColors.Boost,Font=UITheme.SmallBold};
  Bind(typeBinding,v=>{model=Convert.ToInt32(v);selecting=true;select.SelectedIndex=model;selecting=false;epBoost.Visible=bright.Visible=model==1;});select.SelectedIndexChanged+=delegate{if(!selecting&&select.SelectedIndex>=0)Queue(typeBinding,select.SelectedIndex);};Track(select,typeBinding);box.Controls.Add(select);
  var enabled=new Binding(group,asset.parameters.First(p=>p.id=="BoostEnable"),true,asset.id);var toggle=new ActionButton{Text="OFF",Accent=EditorColors.Boost,Font=UITheme.SmallBold};bool on=false;Bind(enabled,v=>{on=Convert.ToBoolean(v);toggle.Text=on?"ON":"OFF";toggle.Active=on;toggle.Invalidate();});toggle.Click+=delegate{Queue(enabled,on?0:1);};Track(toggle,enabled);box.Controls.Add(toggle);
  Action<ActionButton,string> ep=(button,id)=>{var binding=new Binding("CFPreamp",Catalog.Bool(id,id),true,asset.id);bool value=false;Bind(binding,v=>{value=Convert.ToBoolean(v);button.Active=value;button.Invalidate();});button.Click+=delegate{Queue(binding,!value);};Track(button,binding);box.Controls.Add(button);};ep(epBoost,"boost");ep(bright,"bright");epBoost.Visible=bright.Visible=false;
  namHelp.SetToolTip(select,"EP Boost, 808 y SD-1 están disponibles en todos los modelos cuando el Custom Firmware confirma esa función. Boost conserva el procesamiento original de cada ampli.");
  namHelp.SetToolTip(tone,"Tone está disponible en EP Boost, 808 y SD-1. El Boost transparente conserva el tono de entrada.");
  Action<double> arrange=zoom=>{
   Func<int,int> px=v=>(int)Math.Round(v*zoom);int center=box.ClientSize.Width/2;
   title.SetBounds(0,0,box.ClientSize.Width,px(22));
   drive.SetBounds(center-px(48),px(24),px(96),px(118));
   level.SetBounds(px(3),px(54),px(49),px(88));
   tone.SetBounds(box.ClientSize.Width-px(52),px(54),px(49),px(88));
   int dialBottom=Math.Max(drive.Bottom,Math.Max(level.Bottom,tone.Bottom));
   select.SetBounds(center-px(66),dialBottom+px(10),px(132),px(26));
   // A native combo chooses its real height from the font/DPI. Position the
   // following row from its actual bottom, rather than a fixed assumed height.
   int row=select.Bottom+px(8);
   toggle.SetBounds(center-px(35),row,px(70),px(26));
   epBoost.SetBounds(px(3),row,px(57),px(26));
   bright.SetBounds(box.ClientSize.Width-px(60),row,px(57),px(26));
   box.Height=Math.Max(px(224),toggle.Bottom+px(8));
  };
  // Fresh controls must start in logical units. The form scales them once,
  // then explicitly lays out this panel even when its width did not change.
  arrange(1);box.ArrangeControls=()=>arrange(EditorZoom.Factor);
  return box;
 }
}
}
