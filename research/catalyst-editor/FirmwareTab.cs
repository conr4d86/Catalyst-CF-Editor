using System;using System.IO;using System.Linq;using System.Reflection;using System.Drawing;using System.Threading;using System.Threading.Tasks;using System.Windows.Forms;
namespace CFEditor {
// Hosts CatalystIR in this process, without writing or launching another EXE.
// The family-derived component keeps the tested firmware engine and adds model
// identity preservation to backup and update selection. Byte parity is checked.
sealed partial class FirmwareTab:IDisposable {
 public const string InstallerHash=EmbeddedFirmwareInfo.ComponentHash;
 static readonly Font BodyFont=UITheme.Body,ControlFont=UITheme.Bold,FileFont=new Font("Segoe UI",11f,FontStyle.Bold),PreviewActionFont=UITheme.Action,LogFont=new Font("Consolas",9.5f);
 readonly Panel parent;bool preview;Assembly assembly;Type type;Form child;Mutex ownership;System.Windows.Forms.Timer observer;bool active,staging,prepared;string lastState;
 const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;const BindingFlags Static=BindingFlags.NonPublic|BindingFlags.Static;
 public event Action StateChanged;
 public FirmwareTab(Panel p,bool design){parent=p;preview=design;parent.Resize+=delegate{Position();};WireDropTargets(parent);EnsureModes();}
 bool ChildImporting{get{if(child==null)return false;var flag=type.GetProperty("ImportingIR",BindingFlags.Public|BindingFlags.Instance);return flag!=null&&(bool)flag.GetValue(child,null);}}
 public bool Created{get{return child!=null||(InitialCFResources.UnifiedWorkspace||dataMode)&&dataView!=null;}}public bool Busy{get{return staging||importing||dataView!=null&&dataView.Busy||!dataMode&&child!=null&&(Get<bool>("busy")||Get<bool>("polling")||ChildImporting);}}
 public bool Active{get{return active;}}public string ConnectionText{get{return InitialCFResources.UnifiedWorkspace||dataMode?dataView.ConnectionText:child==null?"CatalystIR integrado":Get<Label>("connection").Text;}}
 public void SwitchPreview(bool design){if(preview==design)return;if(Busy)throw new InvalidOperationException("Espera a que termine la operación.");Dispose();preview=design;prepared=false;}
 public Assembly Assembly{get{return assembly;}}public Form Child{get{return child;}}
 public string SelectedNAM{get{if(InitialCFResources.UnifiedWorkspace&&dataView!=null)return dataView.PreparedNAM(3);if(child==null)return null;object model=type.GetField("nam",Private).GetValue(child);return model==null?null:Path.GetFileNameWithoutExtension(Convert.ToString(model.GetType().GetField("Name").GetValue(model)));}}
 T Get<T>(string field){return (T)type.GetField(field,Private).GetValue(child);}
 void SetStatic(string field,object value){type.GetField(field,Static).SetValue(null,value);}
 object Call(string method,params object[] args){try{return type.GetMethod(method,Private).Invoke(child,args);}catch(TargetInvocationException e){throw e.InnerException??e;}}
 public static byte[] Payload(){using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("catalystir.embedded"))using(var memory=new MemoryStream()){if(stream==null)throw new InvalidDataException("Falta CatalystIR integrado");stream.CopyTo(memory);var bytes=memory.ToArray();if(FirmwareCodec.Hash(bytes)!=InstallerHash)throw new InvalidDataException("CatalystIR integrado no coincide con la versión verificada");return bytes;}}
 public async Task Activate(string previewData){
  previewDataRoot=previewData;EnsureModes();active=true;if(InitialCFResources.UnifiedWorkspace){dataView.SetInitialMode(!dataMode);dataView.SetActive(true);Position();Notify();return;}if(dataMode){dataView.SetActive(true);Position();Notify();return;}dataView.SetActive(false);staging=true;Notify();try{
   if(!preview&&ownership==null){ownership=new Mutex(false,"Local\\CatalystIR-OneClick");if(!ownership.WaitOne(0)){ownership.Dispose();ownership=null;throw new InvalidOperationException("Cierra CatalystIR antes de usar la pestaña Firmware CF.");}}
   if(child==null){
    assembly=await Task.Run(()=>System.Reflection.Assembly.Load(Payload()));
    double previousZoom=EditorZoom.Factor;EditorZoom.Factor=1;try{
    type=assembly.GetType("OneClickApp",true);assembly.GetType("StableSurface",true).GetField("Painter").SetValue(null,(Action<Graphics,Control>)BlackTheme.Behind);
    string data=preview?Path.GetFullPath(previewData):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CatalystIR","v3-nam");Directory.CreateDirectory(data);SetStatic("data",data);
    // Suppress the standalone Shown handler; the host explicitly owns prepare,
    // polling and connection handover. Other installer code remains identical.
    SetStatic("preview",true);child=(Form)Activator.CreateInstance(type,true);child.TopLevel=false;child.ShowInTaskbar=false;child.FormBorderStyle=FormBorderStyle.None;child.MinimumSize=Size.Empty;child.AutoScaleMode=AutoScaleMode.None;child.ClientSize=new Size(800,510);
    var layout=child.Controls.OfType<TableLayoutPanel>().Single();layout.SuspendLayout();foreach(int row in new[]{0,1,7}){foreach(Control c in layout.Controls)if(layout.GetRow(c)==row)c.Visible=false;layout.RowStyles[row].SizeType=SizeType.Absolute;layout.RowStyles[row].Height=0;}layout.Padding=new Padding(8);layout.RowStyles[2].Height=310;layout.RowStyles[3].Height=40;layout.RowStyles[6].SizeType=SizeType.Absolute;layout.RowStyles[6].Height=150;layout.ResumeLayout();child.Paint+=delegate(object sender,PaintEventArgs e){BlackTheme.Background(e.Graphics,child.ClientRectangle);};StylePanels(child);parent.Controls.Add(child);Position();child.Show();if(preview&&EmbeddedFirmwareInfo.FamilyEnabled)type.GetMethod("ConfigureEmbeddedPreview",BindingFlags.Public|BindingFlags.Instance).Invoke(child,null);
    observer=new System.Windows.Forms.Timer{Interval=150};observer.Tick+=delegate{string state=Busy+"|"+ConnectionText+"|"+SelectedNAM+"|"+Get<bool>("namSelected");if(state!=lastState){lastState=state;Position();Notify();}};
    }finally{EditorZoom.Factor=previousZoom;}
    ((EditorForm)parent.FindForm()).ScaleFreshControls(parent);Position();
    if(!preview){var bundle=assembly.GetType("OneClickBundle",true);string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CatalystIR","v3-nam");await Task.Run(()=>{try{bundle.GetMethod("Prepare",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{data});}catch(TargetInvocationException e){throw e.InnerException??e;}});prepared=true;SetStatic("preview",false);}
   }
   WireDropTargets(child);UpdateDropHints();if(!preview&&prepared){Get<System.Windows.Forms.Timer>("timer").Start();await (Task)Call("Poll");}observer.Start();Position();
  }catch{active=false;Dispose();throw;}finally{staging=false;Notify();}
 }
 void KeepFont(Control c,Font font){c.Font=EditorZoom.Font(font);c.FontChanged+=delegate{var expected=EditorZoom.Font(font);if(!EditorZoom.Applying&&!c.IsDisposed&&!c.Font.Equals(expected))c.Font=expected;};}
 void StylePanels(Control root){foreach(Control c in root.Controls){if(c.GetType().Name=="GlassPanel"){var panel=c;c.Paint+=delegate(object sender,PaintEventArgs e){BlackTheme.Surface(e.Graphics,panel);};if(c.Controls.OfType<Label>().Any(l=>l.Text=="ACTIVIDAD"))c.Padding=new Padding(c.Padding.Left,34,c.Padding.Right,c.Padding.Bottom);}if(c is Label||c is CheckBox||c is IButtonControl||c is NumericUpDown){bool caption=c==Get<Label>("lowCaption")||c==Get<Label>("highCaption")||c.Text=="ACTIVIDAD";bool metadata=c is Label&&(c.Text.StartsWith("Mono")||c.Text.StartsWith("A2 Lite"));Font font=c==Get<Control>("run")?PreviewActionFont:c==Get<Label>("irName")||c==Get<Label>("namName")?FileFont:metadata?UITheme.Small:caption?ControlFont:c is Label?BodyFont:ControlFont;KeepFont(c,font);if(c.Text=="ACTIVIDAD")c.Height=22;}var text=c as TextBox;if(text!=null){text.BackColor=Color.FromArgb(15,16,18);text.ForeColor=UITheme.Text;KeepFont(text,text.Multiline?LogFont:ControlFont);}StylePanels(c);}}
 void Box(Control c,int x,int y,int w,int h){c.SetBounds(EditorZoom.Px(x),EditorZoom.Px(y),EditorZoom.Px(w),EditorZoom.Px(h));}
 void Place(string name,int x,int y,int w,int h){Box(Get<Control>(name),x,y,w,h);}
 public void Relayout(){Position();}
 void Position(){PositionModes();if(child==null||parent.ClientSize.Width<EditorZoom.Px(800))return;int top=EditorZoom.Px(42);child.SetBounds(0,top,parent.ClientSize.Width,Math.Min(Math.Max(1,parent.ClientSize.Height-top),EditorZoom.Px(552)));var workspace=Get<Panel>("workspace");int w=(int)Math.Round(workspace.ClientSize.Width/EditorZoom.Factor);if(w<700)return;workspace.SuspendLayout();try{
  var tabs=Get<Array>("slotButtons").Cast<Control>().ToArray();int tabWidth=(w-40)/4;for(int i=0;i<3;i++)Box(tabs[i],20+i*tabWidth,0,tabWidth,32);Place("namTab",20+3*tabWidth,0,tabWidth,32);
  Place("choose",20,53,160,40);Place("irName",200,55,w-400,25);Place("clearSlot",w-170,53,150,36);foreach(Control c in workspace.Controls)if(c is Label&&c.Text.StartsWith("Mono"))Box(c,200,84,w-300,22);
  Place("auto",20,126,220,30);Place("gain",270,128,105,28);Place("levelInfo",430,124,w-450,40);Place("cuts",20,184,220,30);Place("lowCaption",270,188,112,28);Place("low",390,185,100,28);Place("highCaption",520,188,116,28);Place("high",644,185,110,28);
  foreach(Control c in workspace.Controls)if(c is Label&&c.Text=="dB")Box(c,389,132,30,22);else if(c is Label&&c.Text.StartsWith("Hz ·"))Box(c,778,188,170,28);
  Place("namPanel",20,44,w-40,182);int nw=w-40;Place("namChoose",0,2,160,40);Place("namName",180,4,nw-370,25);Place("namClear",nw-150,2,150,36);Place("namAuto",0,74,225,30);Place("namGain",250,76,105,28);Place("namLevelInfo",420,72,nw-430,40);Place("namInput",100,128,105,28);Place("namKeepIR",270,126,355,30);Place("namExport",nw-280,122,280,38);
  var np=Get<Panel>("namPanel");foreach(Control c in np.Controls)if(c is Label&&c.Text.StartsWith("A2 Lite"))Box(c,180,34,nw-380,24);else if(c is Label&&c.Text=="Entrada")Box(c,0,133,90,24);else if(c is Label&&c.Text=="dB")Box(c,c.Top<EditorZoom.Px(100)?370:215,c.Top<EditorZoom.Px(100)?80:134,30,22);
  var run=Get<Control>("run");int runWidth=Math.Min(600,w-80);Box(run,(w-runWidth)/2,241,runWidth,48);if(Get<bool>("namSelected")&&SelectedNAM==null)run.Text="Instalar NAM + IR";
  foreach(string field in new[]{"irName","namName"})Get<Label>(field).Font=EditorZoom.Font(FileFont);foreach(Control tab in tabs)tab.Font=EditorZoom.Font(ControlFont);Get<Control>("namTab").Font=EditorZoom.Font(ControlFont);
  foreach(string field in new[]{"levelInfo","namLevelInfo"})Get<Label>(field).Font=EditorZoom.Font(BodyFont);
  if(preview){run.Font=EditorZoom.Font(PreviewActionFont);run.Text="Vista de diseño · Instalación desactivada";}
 }finally{workspace.ResumeLayout();}}
 public bool Deactivate(){if(Busy||dataView!=null&&dataView.HasDataOwner)return false;active=false;if(dataView!=null)dataView.SetActive(false);if(child!=null)Get<System.Windows.Forms.Timer>("timer").Stop();if(observer!=null)observer.Stop();if(ownership!=null){ownership.ReleaseMutex();ownership.Dispose();ownership=null;}Notify();return true;}
 void Notify(){if(StateChanged!=null)StateChanged();}
 public void Dispose(){if(Busy&&!staging)throw new InvalidOperationException("Espera a que termine la operación del firmware");active=false;if(observer!=null){observer.Stop();observer.Dispose();observer=null;}if(child!=null){Get<System.Windows.Forms.Timer>("timer").Stop();child.Close();Get<System.Windows.Forms.Timer>("timer").Dispose();child.Dispose();child=null;}DisposeModes();if(ownership!=null){try{ownership.ReleaseMutex();}catch(ApplicationException){}ownership.Dispose();ownership=null;}}
 // Deliberately restricted to preview tests; never exposes an update method.
 public object PreviewCall(string method,params object[] args){if(!preview)throw new InvalidOperationException("Solo vista previa");if(!new[]{"SelectSlot","SelectNAM","RefreshUI"}.Contains(method))throw new ArgumentException("Método de prueba no permitido");object result=Call(method,args);Position();return result;}
 public void PreviewClickTab(int index){if(InitialCFResources.UnifiedWorkspace){if(!preview)throw new InvalidOperationException("Solo vista previa");if(index<0||index>=5)throw new ArgumentOutOfRangeException("index");((IButtonControl)dataView.Tab(index)).PerformClick();if(dataView.SelectedSlot!=index||dataView.SendEnabled)throw new InvalidOperationException("La pestaña de firmware no cambió o permite enviar desde la vista previa.");return;}if(!preview)throw new InvalidOperationException("Solo vista previa");Control tab=index==3?Get<Control>("namTab"):Get<Array>("slotButtons").Cast<Control>().ToArray()[index];if(!tab.Enabled||!Get<Panel>("workspace").Enabled)throw new InvalidOperationException("Pestaña deshabilitada");((IButtonControl)tab).PerformClick();Position();if(Get<bool>("namSelected")!=(index==3)||index<3&&Get<int>("selectedSlot")!=index)throw new InvalidOperationException("La pestaña no cambió");if(Get<Control>("run").Enabled)throw new InvalidOperationException("La vista de diseño permite instalar");}
}
sealed partial class EditorForm {
 FirmwareTab firmware;int currentPage;
 void BuildFirmware(){var page=Page();firmware=new FirmwareTab(page,preview);firmware.DataView.ReceiverVerified+=ReceiverVerified;firmware.DataView.InstallationStarted+=RestrictUntilVerified;firmware.DataView.ReceiverLost+=RestrictUntilVerified;firmware.StateChanged+=delegate{if(IsFirmwarePage(currentPage)){connection.Text=preview?"Vista de diseño · Sin conexión USB":firmware.ConnectionText;}UpdateNamCaption();UpdateEnabled();};}
}
}
