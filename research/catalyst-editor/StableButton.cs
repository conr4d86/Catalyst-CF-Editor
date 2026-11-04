using System;using System.Drawing;using System.Windows.Forms;

// Button input semantics without the native flat-button renderer.
public class StableButton:Control,IButtonControl {
 public sealed class FlatSettings {public int BorderSize{get;set;}}
 public FlatSettings FlatAppearance{get;private set;}
 public FlatStyle FlatStyle{get;set;}
 public bool UseVisualStyleBackColor{get;set;}
 public DialogResult DialogResult{get;set;}
 bool pressed;
 public StableButton(){FlatAppearance=new FlatSettings();SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor|ControlStyles.Selectable,true);SetStyle(ControlStyles.StandardClick|ControlStyles.StandardDoubleClick,false);AccessibleRole=AccessibleRole.PushButton;TabStop=true;}
 public void NotifyDefault(bool value){Invalidate();}
 public void PerformClick(){if(Enabled&&Visible)OnClick(EventArgs.Empty);}
 protected override bool IsInputKey(Keys key){return key==Keys.Space||key==Keys.Enter||base.IsInputKey(key);}
 protected override void OnPaintBackground(PaintEventArgs e){StableSurface.Draw(e.Graphics,this);}
 protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(Enabled&&e.Button==MouseButtons.Left){Focus();pressed=true;Capture=true;Invalidate();}}
 protected override void OnMouseUp(MouseEventArgs e){bool click=pressed&&Enabled&&ClientRectangle.Contains(e.Location);pressed=false;Capture=false;base.OnMouseUp(e);Invalidate();if(click)OnClick(EventArgs.Empty);}
 protected override void OnMouseCaptureChanged(EventArgs e){if(!Capture){pressed=false;Invalidate();}base.OnMouseCaptureChanged(e);}
 protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Space){pressed=true;e.Handled=true;Invalidate();}else if(e.KeyCode==Keys.Enter){PerformClick();e.Handled=true;}}
 protected override void OnKeyUp(KeyEventArgs e){base.OnKeyUp(e);if(e.KeyCode==Keys.Space&&pressed){pressed=false;PerformClick();e.Handled=true;Invalidate();}}
 protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
 protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);pressed=false;Invalidate();}
 protected override AccessibleObject CreateAccessibilityInstance(){return new ButtonAccessibility(this);}
 sealed class ButtonAccessibility:ControlAccessibleObject {readonly StableButton owner;public ButtonAccessibility(StableButton button):base(button){owner=button;}public override string DefaultAction{get{return "Pulsar";}}public override void DoDefaultAction(){owner.PerformClick();}}
}
public static class StableSurface {
 public static Action<Graphics,Control> Painter;
 public static void Draw(Graphics g,Control control){if(Painter!=null)Painter(g,control);else using(var brush=new SolidBrush(Color.FromArgb(19,20,23)))g.FillRectangle(brush,control.ClientRectangle);}
}
