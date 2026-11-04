using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Web.Script.Serialization;

// Windows' disabled-text renderer uses an etched black caption. On a dark
// background, draw a plain, high-contrast caption while retaining Enabled=false
// and the normal WinForms input/accessibility behaviour.
class DarkButton : Button {
 public bool Successful;
 public bool RequiresRestart;
 public Color FillColor=Color.FromArgb(37,53,75);
 bool hover;
 public DarkButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;FlatAppearance.BorderSize=0;}
 protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
 protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
 protected override void OnPaint(PaintEventArgs e){
  base.OnPaintBackground(e);if(Width<4||Height<4)return;e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
  Color fill=Enabled?FillColor:Successful?Color.FromArgb(26,58,51):Color.FromArgb(28,39,56);
  if(hover&&Enabled)fill=Color.FromArgb(Math.Min(255,fill.R+12),Math.Min(255,fill.G+12),Math.Min(255,fill.B+12));
  using(var path=UITheme.Rounded(new RectangleF(1,1,Width-3,Height-3),10))
  using(var brush=new System.Drawing.Drawing2D.LinearGradientBrush(ClientRectangle,fill,Color.FromArgb(Math.Max(0,fill.R-6),Math.Max(0,fill.G-6),Math.Max(0,fill.B-6)),90))
  using(var border=new Pen(Successful?Color.FromArgb(120,105,225,166):Color.FromArgb(75,126,157,187))){e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(border,path);}
  var color=RequiresRestart?Color.FromArgb(255,64,64):Successful?UITheme.Success:Enabled?UITheme.Text:UITheme.Muted;
  TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
  if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-5,-5),color,fill);
 }
}
class DarkCheckBox : CheckBox {
 public bool Successful;
 protected override void OnPaint(PaintEventArgs e){
  if(Enabled){base.OnPaint(e);return;}
  base.OnPaintBackground(e);
  int size=Math.Max(13,(int)Math.Round(Font.Height*.78)),left=0,top=(Height-size)/2;
  using(var fill=new SolidBrush(Color.FromArgb(43,49,59)))e.Graphics.FillRectangle(fill,left,top,size,size);
  using(var border=new Pen(Color.FromArgb(143,155,172)))e.Graphics.DrawRectangle(border,left,top,size-1,size-1);
  if(Checked)using(var pen=new Pen(Color.FromArgb(190,198,209),Math.Max(2,size/8f))){
   e.Graphics.DrawLines(pen,new[]{new PointF(left+size*.2f,top+size*.5f),new PointF(left+size*.43f,top+size*.72f),new PointF(left+size*.82f,top+size*.25f)});
  }
  TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(size+6,0,Math.Max(0,Width-size-6),Height),Successful?UITheme.Success:UITheme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
 }
}
class DarkLabel : Label {
 public bool Successful;
 protected override void OnPaint(PaintEventArgs e){
  if(Enabled){base.OnPaint(e);return;}
  base.OnPaintBackground(e);
  var flags=TextFormatFlags.Left|TextFormatFlags.NoPadding;
  flags|=AutoSize||AutoEllipsis?TextFormatFlags.SingleLine:TextFormatFlags.WordBreak;
  if(AutoEllipsis)flags|=TextFormatFlags.EndEllipsis;
  TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Successful?UITheme.Success:UITheme.Muted,flags);
 }
}
class DarkLinkLabel : LinkLabel {
 protected override void OnPaint(PaintEventArgs e){
  if(Enabled){base.OnPaint(e);return;}
  base.OnPaintBackground(e);
  using(var underline=new Font(Font,Font.Style|FontStyle.Underline))
   TextRenderer.DrawText(e.Graphics,Text,underline,ClientRectangle,Color.FromArgb(190,198,209),TextFormatFlags.Left|TextFormatFlags.NoPadding|TextFormatFlags.SingleLine);
 }
}

partial class OneClickApp : Form {
 static string data;
 static bool preview;
 static bool previewKeepSlots;
 static readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=2000000};
 Label connection,info,irName,levelInfo,lowCaption,highCaption;
 ToolTip help=new ToolTip();
 Panel workspace;
 Button choose,run;
 NumericUpDown gain,low,high;
 CheckBox auto,cuts;
 GlassProgress progress;
 TextBox details;
 System.Windows.Forms.Timer timer;
 bool busy,refreshing,closing,polling,profileValid=true;
 int usbCount;
 string wav,key;
 byte[] original;
 IRLevels.Profile profile;
 IRLevels.Analysis analysis;
 Dictionary<string,IRLevels.Profile> profiles;
 OneClickSession session;
#if MULTI_IR
 MultiIRSlots slots;
 int selectedSlot;
 Button[] slotButtons=new Button[3];
 Button clearSlot;
 bool completedThisWindow;
 string SlotsPath {get{return Path.Combine(data,"slots.json");}}
#endif
 string ProfilesPath {get{return Path.Combine(data,"settings","ir-levels.json");}}
 string Journal {get{return Path.Combine(data,"installation.json");}}
 string Recipe {get{
#if MULTI_IR
  try{return FullRecipe();}catch{return null;}
#else
  return profile==null?null:OneClickSession.Recipe(key,IRLevels.EffectiveDb(analysis,profile),profile.filtersEnabled,profile.lowCutHz,profile.highCutHz);
#endif
 }}
 bool CurrentTemplate {get{return session!=null&&session.templateHash==NamFirmwareCodec.AppTemplateHash;}}
 bool InstalledSelection {get{return CurrentTemplate&&session.phase=="active"&&
#if MULTI_IR
  completedThisWindow&&Recipe!=null&&session.recipe==Recipe;
#else
  (profile==null||session.recipe==Recipe);
#endif
 }}

 [STAThread]static int Main(string[] args){
  try{
   int namResult;if(NamCommands(args,out namResult))return namResult;
   // Test switches never install firmware or change device parameters.
   if(args.Length==2&&args[0]=="--usb-status"){
    File.WriteAllText(args[1],json.Serialize(new{catalystMidiInputs=AmplifierUSB.Count(),scope="capability enumeration only; no port opened"}));return 0;
   }
   if(args.Length>=2&&args[0]=="--bundle-check"){
    OneClickBundle.Prepare(args[1]);
    byte[] template=OneClickBundle.Template();
    File.WriteAllText(Path.Combine(args[1],"bundle-check.json"),json.Serialize(new{passed=true,official_sha256=FirmwareCodec.Hash(File.ReadAllBytes(OneClickBundle.Official)),template_bytes=template.Length,runtime=OneClickBundle.Root}));return 0;
   }
#if MULTI_IR
   if(args.Length==4&&args[0]=="--ui-slot-check"){
    preview=true;data=Path.GetFullPath(args[1]);Directory.CreateDirectory(data);Application.EnableVisualStyles();
    using(var form=new OneClickApp()){
     form.usbCount=1;form.RefreshUI();form.LoadIR(Path.Combine(args[2],"IR1.wav"));
     form.auto.Checked=false;form.gain.Value=-15;form.low.Value=82;
     string first=OneClickSession.Recipe(form.key,IRLevels.EffectiveDb(form.analysis,form.profile),form.profile.filtersEnabled,form.profile.lowCutHz,form.profile.highCutHz);
     form.SelectSlot(1);form.LoadIR(Path.Combine(args[2],"IR1.wav"));form.auto.Checked=false;form.gain.Value=-9;form.high.Value=6500;
     if(form.slots.slots[0].profile==form.slots.slots[1].profile||form.slots.slots[0].profile.manualDb!=-15||form.slots.slots[0].profile.lowCutHz!=82)throw new Exception("Los perfiles de ranura se mezclan");
     string combined=form.Recipe;form.SelectSlot(2);
     if(!form.run.Enabled||form.profile!=null||form.Recipe!=combined)throw new Exception("Una ranura vacía impide instalar las demás");
     form.LoadIR(Path.Combine(args[2],"IR3.wav"));form.SelectSlot(0);
     if(first!=OneClickSession.Recipe(form.key,IRLevels.EffectiveDb(form.analysis,form.profile),form.profile.filtersEnabled,form.profile.lowCutHz,form.profile.highCutHz))throw new Exception("Los ajustes cambiaron al regresar a IR1");
     string selected=form.Recipe;var saved=MultiIRSlots.Load(form.SlotsPath);if(saved.Recipe()!=selected)throw new Exception("No se conserva la selección al reabrir");
     form.session=new OneClickSession{phase="active",templateHash=NamFirmwareCodec.AppTemplateHash,recipe=selected};form.completedThisWindow=true;form.RefreshUI();
     if(form.run.Enabled||form.run.Text!="IR guardados ✓")throw new Exception("Estado instalado incorrecto");
     form.LoadIR(form.wav);if(!form.run.Enabled||form.InstalledSelection||form.Recipe!=selected)throw new Exception("Volver a cargar el mismo WAV no permite reinstalar");
     form.completedThisWindow=true;form.gain.Value=-14.5m;if(!form.run.Enabled||form.InstalledSelection)throw new Exception("Cambiar un ajuste no invalida la instalación anterior");
     form.usbCount=0;form.RefreshUI();if(form.workspace.Enabled)throw new Exception("Edición activa sin USB");
     form.session.recipe=form.Recipe;form.session.Save(form.Journal);string journalHash=FirmwareCodec.Hash(File.ReadAllBytes(form.Journal));
     for(int slot=0;slot<3;slot++)using(var reopened=new OneClickApp()){
      reopened.usbCount=1;reopened.RefreshUI();
      if(reopened.slots.Count!=0||reopened.profile!=null||reopened.InstalledSelection||reopened.run.Enabled)throw new Exception("La nueva apertura no empieza vacía");
      if(FirmwareCodec.Hash(File.ReadAllBytes(form.Journal))!=journalHash)throw new Exception("Abrir la app altera el historial de instalación");
      reopened.SelectSlot(slot);reopened.LoadIR(Path.Combine(args[2],"IR"+(slot+1)+".wav"));
      if(reopened.slots.Count!=1||reopened.slots.slots[slot]==null||!reopened.run.Enabled)throw new Exception("Una sola ranura no permite instalar");
     }
     form.slots.Save(form.SlotsPath);form.session.phase="awaiting-reboot";form.session.Save(form.Journal);
     using(var resumed=new OneClickApp()){
      resumed.usbCount=1;resumed.RefreshUI();
      if(resumed.slots.Count!=3||resumed.Recipe!=form.Recipe||resumed.run.Enabled||resumed.run.Text!="Apaga y enciende tu ampli")throw new Exception("Se pierden las ranuras de una instalación pendiente");
     }
     form.session.phase="active";form.session.Save(form.Journal);
     File.WriteAllText(args[3],json.Serialize(new{passed=true,independent_same_wav_profiles=true,all_slot_recipe=true,empty_slot_allowed=true,reload_preserves_profiles=true,opening_starts_empty=true,same_wav_can_be_reinstalled=true,each_single_slot_install_allowed=true,pending_installation_resumes_slots=true,journal_preserved_on_open=true,changed_gain_requires_reinstall=true,usb_disables_workspace=true,scope="offline UI state only; no USB opened"}));
    }return 0;
   }
   if(args.Length==4&&args[0]=="--multi-candidate"){
    OneClickBundle.Prepare(args[1]);var selected=MultiIRSlots.Load(args[2]);byte[] template=OneClickBundle.Template();
    byte[] candidate=MultiIRCodec.WithIRs(template,selected.Process());FirmwareCodec.TestGuards(template,candidate);File.WriteAllBytes(args[3],candidate);
    File.WriteAllText(args[3]+".json",json.Serialize(new{passed=true,sha256=FirmwareCodec.Hash(candidate),recipe=selected.Recipe(),slots=selected.Count,scope="offline; no USB"}));return 0;
   }
#endif
   if(args.Length==5&&args[0]=="--profile-candidate"){
    OneClickBundle.Prepare(args[1]);byte[] original=FirmwareCodec.ReadIR(args[2]);
    var saved=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[3]));
    if((string)saved["original_ir_sha256"]!=FirmwareCodec.Hash(original))throw new InvalidDataException("IR y perfil no coinciden");
    var profile=json.Deserialize<IRLevels.Profile>(json.Serialize(saved["profile"]));
    byte[] filtered=IRFilters.Apply(original,profile.filtersEnabled,profile.lowCutHz,profile.highCutHz);double db=IRLevels.EffectiveDb(IRLevels.Analyze(filtered),profile);
    byte[] ir=IRLevels.Apply(filtered,db),template=OneClickBundle.Template(),candidate=FirmwareCodec.WithIR(template,ir);FirmwareCodec.TestGuards(template,candidate);File.WriteAllBytes(args[4],candidate);
    File.WriteAllText(args[4]+".json",json.Serialize(new{passed=true,sha256=FirmwareCodec.Hash(candidate),profile=profile,patch_sha256=NamFirmwareCodec.AppTemplateHash,level_db=db,scope="offline; no USB"}));return 0;
   }
   if(args.Length==4&&(args[0]=="--offline-candidate"||args[0]=="--offline-auto-candidate")){
    OneClickBundle.Prepare(args[1]);byte[] source=FirmwareCodec.ReadIR(args[2]);
    byte[] filtered=IRFilters.Apply(source,true,80,8000);var analysis=IRLevels.Analyze(filtered);
    bool automatic=args[0]=="--offline-auto-candidate";double db=automatic?analysis.automaticDb:-24.1;
    byte[] ir=IRLevels.Apply(filtered,db);
    byte[] template=OneClickBundle.Template(),candidate=FirmwareCodec.WithIR(template,ir);
    FirmwareCodec.TestGuards(template,candidate);File.WriteAllBytes(args[3],candidate);
    File.WriteAllText(args[3]+".json",json.Serialize(new{passed=true,sha256=FirmwareCodec.Hash(candidate),automatic=automatic,low_cut_hz=80,high_cut_hz=8000,level_db=db,peak_db=analysis.frequencyPeakDb+db,scope="offline; no USB"}));return 0;
   }
   if(args.Length==3&&args[0]=="--sdk-status"){
    data=Path.GetFullPath(args[1]);OneClickBundle.Prepare(data);
    string output=Helper("editor","--status",null,null).GetAwaiter().GetResult();OneClickSession.CheckSDK(output);File.WriteAllText(args[2],output);return 0;
   }
   if(args.Length==4&&args[0]=="--device-action"){
    data=Path.GetFullPath(args[1]);OneClickBundle.Prepare(data);string command=args[2],destination=Path.GetFullPath(args[3]);
    if(command!="--current-snapshot"&&command!="--verified-presets-preserve-live"&&command!="--activate-store-ir"
#if MULTI_IR
     &&command!="--activate-store-ir-slot0"&&command!="--activate-store-ir-slot1"&&command!="--activate-store-ir-slot2"
#endif
     )throw new ArgumentException("Operación no permitida");
    string log=destination+".operation.log";Directory.CreateDirectory(Path.GetDirectoryName(log));object writeGate=new object();
    string output=Helper("editor",command+" "+Q(destination),line=>{lock(writeGate)File.AppendAllText(log,line+Environment.NewLine);},null).GetAwaiter().GetResult();OneClickSession.CheckSDK(output);
    if(command=="--verified-presets-preserve-live"){
     string archive=Path.Combine(destination,"Catalyst-verified.catl6a");FirmwareCodec.CreateVerifiedBackup(destination,archive);FirmwareCodec.VerifyPreservedBackup(archive);
    }
    if(command=="--activate-store-ir")new OneClickSession().ConfirmActivation(output);
    return 0;
   }
   if(args.Length==6&&args[0]=="--install-prepared"){
    data=Path.GetFullPath(args[1]);OneClickBundle.Prepare(data);string candidate=Path.GetFullPath(args[2]),backup=Path.GetFullPath(args[3]);
    string expectedHash=FirmwareCodec.Hash(File.ReadAllBytes(candidate));
    if(args[4]!=expectedHash)throw new InvalidDataException("No coincide la huella de instalación");
    string hash=FirmwareCodec.Hash(File.ReadAllBytes(candidate)),log=Path.GetFullPath(args[5]);object writeGate=new object();
    if(File.Exists(log))throw new IOException("El registro de instalación ya existe");
    Directory.CreateDirectory(Path.GetDirectoryName(log));
    string output=Helper("central","--install "+Q(candidate)+" "+Q(backup)+" "+hash+" "+Q(OneClickBundle.Official),line=>{lock(writeGate)File.AppendAllText(log,line+Environment.NewLine);},request=>OneClickSession.MatchesInstallRequest(request,expectedHash)).GetAwaiter().GetResult();
    new OneClickSession().ConfirmTransfer(output);File.WriteAllText(log+".json",json.Serialize(new{transfer_completed=true,boot_confirmed=false,sha256=hash,patch_sha256=NamFirmwareCodec.AppTemplateHash}));return 0;
   }
   if(args.Length==5&&(args[0]=="--native-preflight"||args[0]=="--integration-preflight")){
    data=Path.GetFullPath(args[1]);OneClickBundle.Prepare(data);string candidate=Path.GetFullPath(args[2]),backup=Path.GetFullPath(args[3]);
    string hash=FirmwareCodec.Hash(File.ReadAllBytes(candidate));bool sawRequest=false,matchingHash=false;
    string before="",after="";
    if(args[0]=="--integration-preflight"){before=Helper("editor","--status",null,null).GetAwaiter().GetResult();OneClickSession.CheckSDK(before);}
    // Exercise the native update boundary, then reject before any write.
    string output=Helper("central","--install "+Q(candidate)+" "+Q(backup)+" "+hash+" "+Q(OneClickBundle.Official),null,request=>{
     sawRequest=true;matchingHash=OneClickSession.MatchesInstallRequest(request,hash);return false;
    }).GetAwaiter().GetResult();
    File.WriteAllText(args[4],output);
    if(!sawRequest||!matchingHash||output.Contains("INSTALL_REQUEST_CONFIRMED")||!output.Contains("Preflight stopped before acceptance, reboot or flash transfer.")||!output.Contains("shutdown complete"))throw new InvalidOperationException("Preflight nativo no confirmado o huella distinta");
    if(args[0]=="--integration-preflight"){
     after=Helper("editor","--status",null,null).GetAwaiter().GetResult();OneClickSession.CheckSDK(after);File.WriteAllText(args[4]+".sequence.log",before+output+after);
    }
    File.WriteAllText(args[4]+".json",json.Serialize(new{passed=true,candidate_hash_matched=matchingHash,install_rejected=true,firmware_written=false}));return 0;
   }
   if(args.Length==4&&args[0]=="--render-preview"){
    preview=true;previewKeepSlots=args[2]!="startup";data=Path.GetFullPath(args[1]);Directory.CreateDirectory(data);
    Application.EnableVisualStyles();using(var form=new OneClickApp()){
     form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-12000,-12000);form.ShowInTaskbar=false;
     if(args[2]=="startup"||args[2]=="connected"||args[2]=="loaded"||args[2]=="ready"||args[2]=="saved")form.usbCount=1;
#if MULTI_IR
     if(args[2]=="slot1"||args[2]=="slot2"||args[2]=="slot3"||args[2]=="saved-set"){
      form.usbCount=1;form.SelectSlot(args[2]=="saved-set"?0:args[2][4]-'1');
      if(args[2]=="saved-set"){form.session=new OneClickSession{phase="active",recipe=form.Recipe,templateHash=NamFirmwareCodec.AppTemplateHash};form.completedThisWindow=true;}
     }
     if(args[2]=="restart"){
      form.usbCount=1;form.session=new OneClickSession{phase="awaiting-reboot",recipe=form.Recipe,templateHash=NamFirmwareCodec.AppTemplateHash};
     }
#endif
     if(args[2]=="loaded"||args[2]=="ready"||args[2]=="saved")form.LoadIR(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-ir.wav"));
     if(args[2]=="saved"&&form.profile!=null)form.irName.Text=form.profile.name+"  ✓";
     form.RefreshUI();if(args[2]=="saved"||args[2]=="saved-offline")form.SetInfo("IR guardado. Puedes encender el ampli sin la app.",true);form.Show();Application.DoEvents();
     using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));image.Save(args[3]);}form.Hide();
    }return 0;
   }
   if(args.Length!=0)throw new ArgumentException("Argumentos incorrectos");
   data=OneClickBundle.DefaultData;
#if MULTI_IR
   data=Path.Combine(data,"v3-nam");
#endif
   Directory.CreateDirectory(data);
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   bool owns;using(var single=new System.Threading.Mutex(true,"Local\\CatalystIR-OneClick",out owns)){
    if(!owns){MessageBox.Show("CatalystIR ya está abierto.","CatalystIR");return 0;}
    Application.Run(new OneClickApp());
   }return 0;
  }catch(Exception e){
   string message=e.ToString();try{string root=data??AppDomain.CurrentDomain.BaseDirectory;Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"error-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".txt"),message);}catch{}
   if(args.Length==0)MessageBox.Show(e.Message,"CatalystIR");return 1;
  }
 }
 protected override void OnPaintBackground(PaintEventArgs e){UITheme.Background(e.Graphics,ClientRectangle);}
 static Button MakeButton(string text){return new DarkButton{Text=text,AutoSize=false,Height=40,FlatStyle=FlatStyle.Flat,ForeColor=UITheme.Text,Font=UITheme.Bold,UseVisualStyleBackColor=false};}
 public OneClickApp(){
  Text="CatalystIR";ClientSize=new Size(800,620);MinimumSize=new Size(816,659);StartPosition=FormStartPosition.CenterScreen;
  using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("application.ico"))if(stream!=null)using(var icon=new Icon(stream))Icon=(Icon)icon.Clone();
  Font=UITheme.Body;BackColor=Color.FromArgb(12,20,33);ForeColor=UITheme.Text;DoubleBuffered=true;
  var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=8,BackColor=Color.Transparent};Controls.Add(layout);
  foreach(int height in new[]{104,12,236,62,8,10})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));
  var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,BackColor=Color.Transparent,Margin=Padding.Empty};header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,124));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
  var amp=new AmpPicture{Size=new Size(104,104),Margin=Padding.Empty,AccessibleName="Catalyst CX60, imagen frontal"};header.Controls.Add(amp,0,0);
  var titlePanel=new Panel{Dock=DockStyle.Fill,BackColor=Color.Transparent,Margin=Padding.Empty};
  var title=new Label{Text="CatalystIR",Font=UITheme.Heading,ForeColor=UITheme.Text,BackColor=Color.Transparent};title.SetBounds(0,1,560,44);titlePanel.Controls.Add(title);
  var subtitle=new Label{Text="IR de pantalla · Catalyst 60 / CX60",Font=UITheme.Small,ForeColor=UITheme.Muted,BackColor=Color.Transparent};subtitle.SetBounds(2,49,560,22);titlePanel.Controls.Add(subtitle);
  connection=new DarkLabel{Text="Esperando conexión de Ampli",Font=UITheme.Status,ForeColor=UITheme.Muted,BackColor=Color.Transparent};connection.SetBounds(0,77,600,24);titlePanel.Controls.Add(connection);header.Controls.Add(titlePanel,1,0);layout.Controls.Add(header,0,0);
  workspace=new GlassPanel{Dock=DockStyle.Fill,Enabled=false,Margin=Padding.Empty};layout.Controls.Add(workspace,0,2);
  choose=MakeButton("Cargar IR");choose.SetBounds(18,17,130,36);workspace.Controls.Add(choose);
  irName=new DarkLabel{Text="Selecciona tu archivo WAV",AutoEllipsis=true,UseMnemonic=false,Font=UITheme.Bold,ForeColor=UITheme.Muted,BackColor=Color.Transparent};irName.SetBounds(164,17,566,24);workspace.Controls.Add(irName);
  var format=new DarkLabel{Text="Mono · 48 kHz · hasta 2048 muestras",Font=UITheme.Small,ForeColor=UITheme.Muted,BackColor=Color.Transparent};format.SetBounds(165,43,565,18);workspace.Controls.Add(format);
  auto=new DarkCheckBox{Text="Volumen automático",Checked=true,Font=UITheme.Bold,ForeColor=UITheme.Text,BackColor=Color.Transparent};auto.SetBounds(18,81,205,27);workspace.Controls.Add(auto);
  gain=new GlassNumber{Minimum=-1000,Maximum=1000,DecimalPlaces=1,Increment=.5m};gain.SetBounds(236,82,85,25);workspace.Controls.Add(gain);
  var gainLabel=new DarkLabel{Text="dB",AutoSize=true,Font=UITheme.Bold,BackColor=Color.Transparent};gainLabel.SetBounds(330,88,30,22);workspace.Controls.Add(gainLabel);
  levelInfo=new DarkLabel{Text="Se ajusta al cargar tu IR",Font=UITheme.Small,ForeColor=UITheme.Muted,BackColor=Color.Transparent};levelInfo.SetBounds(369,79,365,37);workspace.Controls.Add(levelInfo);
  cuts=new DarkCheckBox{Text="Cortes automáticos",Checked=true,Font=UITheme.Bold,ForeColor=UITheme.Text,BackColor=Color.Transparent};cuts.SetBounds(18,126,205,27);workspace.Controls.Add(cuts);
  lowCaption=new DarkLabel{Text="Low Cut",AutoSize=true,Font=UITheme.Bold,BackColor=Color.Transparent};lowCaption.SetBounds(236,133,55,22);workspace.Controls.Add(lowCaption);
  low=new GlassNumber{Minimum=20,Maximum=2000,Value=80,Increment=10};low.SetBounds(295,127,75,25);workspace.Controls.Add(low);
  highCaption=new DarkLabel{Text="High Cut",AutoSize=true,Font=UITheme.Bold,BackColor=Color.Transparent};highCaption.SetBounds(386,133,60,22);workspace.Controls.Add(highCaption);
  high=new GlassNumber{Minimum=1000,Maximum=22000,Value=8000,Increment=100};high.SetBounds(453,127,84,25);workspace.Controls.Add(high);
  var hz=new DarkLabel{Text="Hz · 12 dB/oct",AutoSize=true,Font=UITheme.Small,ForeColor=UITheme.Muted,BackColor=Color.Transparent};hz.SetBounds(550,135,170,20);workspace.Controls.Add(hz);
  run=MakeButton("Instalar IR");run.SetBounds(396,177,342,41);((DarkButton)run).FillColor=Color.FromArgb(31,111,87);workspace.Controls.Add(run);
#if MULTI_IR
  ClientSize=new Size(800,656);MinimumSize=new Size(816,695);layout.RowStyles[2].Height=272;
  foreach(Control control in workspace.Controls)control.Top+=30;
  run.SetBounds(188,207,384,41);run.Font=UITheme.Action;
  irName.Width=445;
  for(int i=0;i<3;i++){
   int index=i;var tab=MakeButton("IR"+(i+1));tab.SetBounds(360+i*126,0,126,31);tab.Click+=delegate{SelectSlot(index);};
   tab.AccessibleName="IR"+(i+1);help.SetToolTip(tab,"Seleccionar "+tab.AccessibleName);slotButtons[i]=tab;workspace.Controls.Add(tab);
  }
  clearSlot=MakeButton("Vaciar");clearSlot.SetBounds(637,47,100,32);help.SetToolTip(clearSlot,"Restaurar la pantalla de fábrica en esta ranura al instalar el conjunto.");
  clearSlot.Click+=delegate{if(!busy){completedThisWindow=false;slots.slots[selectedSlot]=null;slots.Save(SlotsPath);SelectSlot(selectedSlot);SetInfo("Ranura vacía: se conservará su pantalla de fábrica al instalar.");}};workspace.Controls.Add(clearSlot);
#endif
  info=new DarkLabel{Text="Conecta el Catalyst por USB y enciéndelo. Cierra Catalyst Edit.",Dock=DockStyle.Fill,Font=UITheme.Body,ForeColor=UITheme.Muted,BackColor=Color.Transparent,Padding=new Padding(3,11,3,4),Margin=Padding.Empty};layout.Controls.Add(info,0,3);
  progress=new GlassProgress{Dock=DockStyle.Fill,Style=ProgressBarStyle.Continuous,Margin=Padding.Empty};layout.Controls.Add(progress,0,4);
  var logPanel=new GlassPanel{Dock=DockStyle.Fill,Padding=new Padding(12,27,12,10),Margin=Padding.Empty};
  var activity=new Label{Text="ACTIVIDAD",Font=UITheme.SmallBold,ForeColor=UITheme.Muted,BackColor=Color.Transparent};activity.SetBounds(12,7,180,17);logPanel.Controls.Add(activity);
  details=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,BorderStyle=BorderStyle.None,ScrollBars=ScrollBars.Vertical,BackColor=Color.FromArgb(18,29,45),ForeColor=UITheme.Muted,Font=new Font("Consolas",8.5f)};logPanel.Controls.Add(details);layout.Controls.Add(logPanel,0,6);
  var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,BackColor=Color.Transparent,Margin=Padding.Empty};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));
  var credit=new LinkLabel{Text="Desarrollado por conr4d",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=UITheme.Bold,ForeColor=UITheme.Muted,LinkColor=UITheme.Success,VisitedLinkColor=UITheme.Success,BackColor=Color.Transparent,Margin=Padding.Empty};
  string release=ProjectLinks.ValidatedLatestRelease();credit.LinkArea=release==null?new LinkArea(0,0):new LinkArea(0,credit.Text.Length);help.SetToolTip(credit,release==null?"El enlace a la última versión se activará cuando publiquemos el GitHub.":"Ver versiones y descargas en GitHub");
  credit.LinkClicked+=delegate{if(release!=null)try{Process.Start(new ProcessStartInfo(release){UseShellExecute=true});}catch(Exception e){MessageBox.Show(this,e.Message,"GitHub");}};footer.Controls.Add(credit,1,0);
  footer.Controls.Add(new Label{Text=
#if MULTI_IR
   "v3.0 · NAM experimental",
#else
   "v1.0 · Experimental",
#endif
   Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleRight,Font=UITheme.Small,ForeColor=UITheme.Muted,BackColor=Color.Transparent,Margin=Padding.Empty},2,0);layout.Controls.Add(footer,0,7);
  FormClosed+=delegate{help.Dispose();};
  profiles=IRLevels.Load(ProfilesPath);session=OneClickSession.Load(Journal);
#if MULTI_IR
  if(!File.Exists(ProfilesPath)&&!preview)profiles=IRLevels.Load(Path.Combine(Path.GetDirectoryName(data),"settings","ir-levels.json"));
  bool resume=CurrentTemplate&&(session.phase=="awaiting-reboot"||session.phase=="activation-failed");
  slots=previewKeepSlots||resume?MultiIRSlots.Load(SlotsPath):new MultiIRSlots();
  SelectSlot(slots.First<0?0:slots.First);
#endif
  // A previous process cannot prove a new physical power cycle.
  if(session!=null&&session.phase=="awaiting-reboot")session.rebootDisconnected=false;
  InitializeNAM();
  choose.Click+=delegate{using(var dialog=new OpenFileDialog{Filter="IR WAV (*.wav)|*.wav",Title="Cargar tu respuesta de impulso"})if(dialog.ShowDialog(this)==DialogResult.OK)LoadIR(dialog.FileName);};
  auto.CheckedChanged+=delegate{EditProfile();};gain.ValueChanged+=delegate{if(!refreshing&&profile!=null){refreshing=true;auto.Checked=false;refreshing=false;EditProfile();}};
  cuts.CheckedChanged+=delegate{EditProfile();};low.ValueChanged+=delegate{EditProfile();};high.ValueChanged+=delegate{EditProfile();};
  run.Click+=async delegate{await Action();};
  FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy){e.Cancel=true;MessageBox.Show(this,"Hay una operación en curso. Espera a que termine.","CatalystIR");}else{closing=true;if(timer!=null)timer.Stop();}};
  timer=new System.Windows.Forms.Timer{Interval=1000};timer.Tick+=async delegate{await Poll();};
  if(!preview)Shown+=async delegate{
   try{SetInfo("Preparando componentes integrados…");await Task.Run(()=>OneClickBundle.Prepare(data));
    SetInfo("Carga tu IR. Baja el volumen y mantén alimentación y USB conectados durante la instalación.");
    timer.Start();await Poll();
   }catch(Exception e){SetInfo("No se pudo preparar la app: "+e.Message,false,true);Log(e.ToString());}
  };
  RefreshUI();
 }
 static void MarkSuccess(Control control,bool positive){
  var label=control as DarkLabel;if(label!=null)label.Successful=positive;
  var button=control as DarkButton;if(button!=null)button.Successful=positive;
  var check=control as DarkCheckBox;if(check!=null)check.Successful=positive;
  control.ForeColor=positive?UITheme.Success:UITheme.Muted;control.Font=UITheme.Bold;control.Invalidate();
 }
 void SetInfo(string text,bool positive=false,bool error=false){
  info.Text=positive&&!text.StartsWith("✓")?"✓ "+text:text;info.ForeColor=error?UITheme.Error:positive?UITheme.Success:UITheme.Muted;info.Font=positive||error?UITheme.Bold:UITheme.Body;((DarkLabel)info).Successful=positive;
 }
 void Log(string text){if(!closing){details.AppendText(text+Environment.NewLine);if(session!=null&&Directory.Exists(session.folder))File.AppendAllText(Path.Combine(session.folder,"activity.log"),text+Environment.NewLine);
  int at=text.IndexOf("/engineProgress=",StringComparison.Ordinal);double percent;
  if(busy&&at>=0&&double.TryParse(text.Substring(at+16).Replace(',','.'),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out percent)){
   progress.Style=ProgressBarStyle.Continuous;progress.Value=(int)Math.Max(0,Math.Min(100,percent));
  }
 }}
 void LoadIR(string file){
  try{
   byte[] b=FirmwareCodec.ReadIR(file);IRLevels.Analyze(b);string next=FirmwareCodec.Hash(b);
   IRLevels.Profile p;if(!profiles.TryGetValue(next,out p)){p=new IRLevels.Profile{name=Path.GetFileName(file),automatic=true};profiles.Add(next,p);}
#if MULTI_IR
   if(slots.slots[selectedSlot]!=null&&slots.slots[selectedSlot].key==next)p=slots.slots[selectedSlot].profile;
   var imported=slots.Import(selectedSlot,file,p,data);p=imported.profile;file=imported.path;
#endif
   // Compute before changing the active selection so failed loads are atomic.
   var a=IRLevels.Analyze(IRFilters.Apply(b,p.filtersEnabled,p.lowCutHz,p.highCutHz));
#if MULTI_IR
   completedThisWindow=false;
#endif
   original=b;key=next;profile=p;analysis=a;wav=file;profileValid=true;
   refreshing=true;auto.Checked=p.automatic;gain.Value=(decimal)Math.Round(IRLevels.EffectiveDb(a,p),1);cuts.Checked=p.filtersEnabled;low.Value=(decimal)p.lowCutHz;high.Value=(decimal)p.highCutHz;refreshing=false;
   irName.Text=
#if MULTI_IR
    slots.slots[selectedSlot].name+"  ✓";
#else
    Path.GetFileName(file)+"  ✓";
#endif
   help.SetToolTip(irName,file);SaveProfiles();SetInfo("IR válido. El botón guarda la copia de presets y prepara la instalación.",true);RefreshUI();
  }catch(Exception e){refreshing=false;if(preview)throw;MessageBox.Show(this,e.Message,"IR no admitido");}
 }
 void SaveProfiles(){
#if MULTI_IR
  if(profile!=null&&key!=null){profiles[key]=MultiIRSlots.Clone(profile);IRLevels.Save(ProfilesPath,profiles);}
  slots.Save(SlotsPath);
#else
  IRLevels.Save(ProfilesPath,profiles);
#endif
 }
#if MULTI_IR
 void SelectSlot(int index){
  if(busy)return;namSelected=false;selectedSlot=index;var s=slots.slots[index];refreshing=true;
  profile=s==null?null:s.profile;wav=s==null?null:s.path;key=s==null?null:s.key;original=s==null?null:MultiIRSlots.Read(s);analysis=s==null?null:MultiIRSlots.Analyze(s);profileValid=true;
  auto.Checked=profile==null||profile.automatic;cuts.Checked=profile==null||profile.filtersEnabled;
  gain.Value=profile==null?0:(decimal)Math.Round(IRLevels.EffectiveDb(analysis,profile),1);low.Value=profile==null?80:(decimal)profile.lowCutHz;high.Value=profile==null?8000:(decimal)profile.highCutHz;
  irName.Text=s==null?"Sin WAV seleccionado":s.name+"  ✓";help.SetToolTip(irName,s==null?"Selecciona un WAV para esta ranura.":s.path);refreshing=false;
  RefreshUI();
  if(usbCount==1&&!busy&&!(CurrentTemplate&&(session.phase=="awaiting-reboot"||session.phase=="activation-failed")))SetInfo(InstalledSelection?"IR guardados. Cada pantalla recupera su IR al encender con el preset guardado.":slots.Count>0?slots.Count+" IR preparados. Las ranuras vacías conservarán sus pantallas de fábrica.":"Carga tus WAV en IR1, IR2 e IR3.",slots.Count>0);
 }
#endif
 void EditProfile(){
  if(refreshing||profile==null||busy)return;
  try{
   IRFilters.Validate((double)low.Value,(double)high.Value);
   profile.automatic=auto.Checked;profile.manualDb=(double)gain.Value;profile.filtersEnabled=cuts.Checked;profile.lowCutHz=(double)low.Value;profile.highCutHz=(double)high.Value;
   analysis=IRLevels.Analyze(IRFilters.Apply(original,profile.filtersEnabled,profile.lowCutHz,profile.highCutHz));
   refreshing=true;if(profile.automatic)gain.Value=(decimal)Math.Round(analysis.automaticDb,1);refreshing=false;
#if MULTI_IR
   completedThisWindow=false;
#endif
   SaveProfiles();profileValid=true;RefreshUI();
  }catch(Exception e){refreshing=false;profileValid=false;RefreshUI();SetInfo(e.Message,false,true);}
 }
 void RefreshUI(){
  bool connected=usbCount==1;
  connection.Text=connected?"✓ Ampli conectado por USB":usbCount>1?"Conecta un solo Catalyst":"Esperando conexión de Ampli";
  connection.ForeColor=connected?UITheme.Success:UITheme.Muted;
  workspace.Enabled=connected&&!busy;
  bool pending=CurrentTemplate&&(session.phase=="awaiting-reboot"||session.phase=="activation-failed");
  choose.Enabled=!pending;auto.Enabled=profile!=null&&!pending;gain.Enabled=profile!=null&&!auto.Checked&&!pending;
  cuts.Enabled=profile!=null&&!pending;low.Enabled=high.Enabled=profile!=null&&cuts.Checked&&!pending;
  bool installed=InstalledSelection;
  ((DarkButton)run).RequiresRestart=pending&&session.phase=="awaiting-reboot"&&!busy;
  run.Text=pending?(session.phase=="activation-failed"?"Reintentar activación":"Apaga y enciende tu ampli"):installed?"Activar IR instalado":"Instalar IR";
  run.Enabled=connected&&!busy&&(pending?session.phase=="activation-failed":installed&&profileValid||(profile!=null&&profileValid));
#if MULTI_IR
  run.Text=pending?(session.phase=="activation-failed"?"Reintentar activación":"Apaga y enciende tu ampli"):"Instalar los IR";
  run.Enabled=connected&&!busy&&(pending?session.phase=="activation-failed":Recipe!=null&&profileValid);
  clearSlot.Enabled=profile!=null&&!pending;
  for(int i=0;i<3;i++){
   slotButtons[i].Text="IR"+(i+1)+(slots.slots[i]!=null?" ✓":"");
   slotButtons[i].Enabled=!busy&&!pending;((DarkButton)slotButtons[i]).FillColor=i==selectedSlot?Color.FromArgb(31,111,87):Color.FromArgb(30,45,63);
   MarkSuccess(slotButtons[i],slots.slots[i]!=null&&connected);
  }
#endif
#if AUTOBOOT
  if(installed){run.Text=
#if MULTI_IR
   "IR guardados ✓";
#else
   "IR guardado ✓";
#endif
   run.Enabled=false;}
#endif
  ((DarkButton)run).FillColor=Color.FromArgb(31,111,87);
  bool showSuccess=true;
#if MULTI_IR
  showSuccess=connected;
#endif
  MarkSuccess(run,installed&&showSuccess);MarkSuccess(irName,wav!=null&&showSuccess);MarkSuccess(auto,profile!=null&&auto.Checked&&profileValid&&showSuccess);MarkSuccess(cuts,profile!=null&&cuts.Checked&&profileValid&&showSuccess);MarkSuccess(lowCaption,profile!=null&&cuts.Checked&&profileValid&&showSuccess);MarkSuccess(highCaption,profile!=null&&cuts.Checked&&profileValid&&showSuccess);
#if MULTI_IR
  run.Font=((DarkButton)run).RequiresRestart?UITheme.ActionPrompt:UITheme.Action;
#endif
  foreach(var number in new[]{gain,low,high}){((GlassNumber)number).Successful=profile!=null&&profileValid&&showSuccess;number.ForeColor=profile!=null&&profileValid&&showSuccess?UITheme.Success:UITheme.Text;number.Invalidate();}
  MarkSuccess(levelInfo,profile!=null&&profileValid&&showSuccess);levelInfo.Font=profile!=null?UITheme.SmallBold:UITheme.Small;
  if(profile!=null)levelInfo.Text=(profile.automatic?"Automático: margen de 3 dB":"Manual: "+IRLevels.EffectiveDb(analysis,profile).ToString("F1")+" dB")+" · Ajuste guardado para este IR";
  if(pending&&!busy)SetInfo(session.phase=="awaiting-reboot"?"Transferencia terminada. Apaga y enciende el ampli normalmente, sin botones. La app activará el IR al reconectar.":"No se confirmó la activación. Comprueba que el ampli haya arrancado, cierra Catalyst Edit y reintenta.");
#if MULTI_IR
  if(!busy&&!pending){
   if(!connected)SetInfo(usbCount>1?"Conecta un solo Catalyst.":"Esperando conexión de Ampli.");
   else if(info.Text=="Esperando conexión de Ampli."||info.Text=="Conecta un solo Catalyst.")SetInfo(slots.Count>0?slots.Count+" IR preparados. Las ranuras vacías conservarán sus pantallas de fábrica.":"Carga tus WAV en IR1, IR2 e IR3.",slots.Count>0);
  }
#endif
  ShowNAM();
 }
 async Task Poll(){
  if(polling||busy||closing)return;polling=true;
  try{
   usbCount=AmplifierUSB.Count();RefreshUI();
   if(CurrentTemplate&&session.ObserveUSB(usbCount))await ActivateIR();
  }catch(Exception e){Log(e.Message);}finally{polling=false;}
 }
 async Task Action(){
  if(busy||usbCount!=1)return;
  if(CurrentTemplate&&(session.phase=="activation-failed"
#if !MULTI_IR
   ||InstalledSelection
#endif
   )){await ActivateIR();return;}
#if MULTI_IR
  if(InstalledSelection)return;
#endif
  if(!profileValid||
#if MULTI_IR
   Recipe==null
#else
   profile==null
#endif
   )return;
  try{RequireFreeEditors();}catch(Exception e){SetInfo(e.Message,false,true);return;}
  busy=true;RefreshUI();progress.Style=ProgressBarStyle.Marquee;
  try{
   // Preparation is offline; transport checks and a fresh backup precede flash.
   string folder=Path.Combine(data,"backups",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));Directory.CreateDirectory(folder);
   session=new OneClickSession{folder=folder,candidate=Path.Combine(folder,"Catalyst-UserIR.ctf"),backup=Path.Combine(folder,"presets","Catalyst-verified.catl6a"),recipe=Recipe,phase="preparing",templateHash=NamFirmwareCodec.AppTemplateHash};session.Save(Journal);
#if MULTI_IR
   session.activationSlot=slots.slots[selectedSlot]!=null?selectedSlot:slots.First;session.hasNAM=nam!=null;session.Save(Journal);
#endif
   SetInfo(nam==null?"Preparando y comprobando el IR…":"Preparando NAM + IR y comprobando el firmware…");
#if MULTI_IR
   slots.Backup(folder);File.Copy(OneClickBundle.Official,Path.Combine(folder,"Catalyst-OFICIAL-recuperacion.ctf"));
   if(nam!=null){File.WriteAllBytes(Path.Combine(folder,"NAM-original.nam"),nam.Source);File.WriteAllText(Path.Combine(folder,"NAM-settings.json"),json.Serialize(NAMSettings()));}
   byte[] candidate=await Task.Run(()=>BuildSelection());
#else
   if(FirmwareCodec.Hash(FirmwareCodec.ReadIR(wav))!=key)throw new InvalidDataException("El WAV ha cambiado. Vuelve a cargarlo.");
   File.Copy(wav,Path.Combine(folder,"IR-original.wav"));File.Copy(OneClickBundle.Official,Path.Combine(folder,"Catalyst-OFICIAL-recuperacion.ctf"));
   byte[] filtered=IRFilters.Apply(original,profile.filtersEnabled,profile.lowCutHz,profile.highCutHz),ir=IRLevels.Apply(filtered,IRLevels.EffectiveDb(analysis,profile));
   byte[] candidate=await Task.Run(()=>{byte[] template=OneClickBundle.Template();byte[] built=FirmwareCodec.WithIR(template,ir);FirmwareCodec.TestGuards(template,built);return built;});
#endif
   File.WriteAllBytes(session.candidate,candidate);session.candidateHash=FirmwareCodec.Hash(candidate);
   File.WriteAllText(Path.Combine(folder,"IR-settings.json"),json.Serialize(new{profile=profile,original_ir_sha256=key,recipe=session.recipe,firmware_sha256=session.candidateHash}));
   SetInfo("Comprobando comunicación USB… Cierra Catalyst Edit.");
   OneClickSession.CheckSDK(await Helper("editor","--status",Log,null));
   SetInfo("Guardando y verificando tus 12 presets. Puede tardar unos minutos…");
   await Helper("editor","--verified-presets-preserve-live "+Q(Path.GetDirectoryName(session.backup)),Log,null);
   FirmwareCodec.CreateVerifiedBackup(Path.GetDirectoryName(session.backup),session.backup);FirmwareCodec.VerifyPreservedBackup(session.backup);
   session.phase="backed-up";session.Save(Journal);
   SetInfo("Instalando por USB… No apagues ni desconectes el ampli.");
   session.phase="installing";session.Save(Journal);
   string output=await Helper("central","--install "+Q(session.candidate)+" "+Q(session.backup)+" "+session.candidateHash+" "+Q(OneClickBundle.Official),Log,ConfirmInstallRequest);
   session.ConfirmTransfer(output);session.Save(Journal);progress.Style=ProgressBarStyle.Continuous;progress.Value=100;
   Log("Copia y firmware guardados en: "+folder);
  }catch(Exception e){
   if(session!=null){session.phase="failed";session.Save(Journal);}SetInfo("Operación detenida: "+e.Message,false,true);Log(e.ToString());
  }finally{busy=false;progress.Style=ProgressBarStyle.Continuous;RefreshUI();}
 }
 async Task ActivateIR(){
  if(session!=null&&session.hasNAM){session.phase="active";session.bootRecallSaved=false;session.Save(Journal);completedThisWindow=true;SetInfo("Transferencia terminada. NAM está en CHIME GROUP 1. Ajusta el volumen y guarda el preset desde el ampli.",true);RefreshUI();return;}
  if(busy||session==null)return;busy=true;RefreshUI();progress.Style=ProgressBarStyle.Marquee;
  try{
   SetInfo("Comprobando arranque y activando tu IR…");
   string folder=Path.Combine(session.folder,"activation-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
#if AUTOBOOT
#if MULTI_IR
   int target=session.activationSlot;if(target<0||target>2||slots.slots[target]==null)throw new InvalidOperationException("No hay IR seleccionado para activar");
   string output=await Helper("editor","--activate-store-ir-slot"+target+" "+Q(folder),Log,null);
   session.ConfirmActivation(output);session.Save(Journal);completedThisWindow=true;SetInfo("IR guardados. Se recupera el IR del preset al encender sin la app.",true);
#else
   string output=await Helper("editor","--activate-store-ir "+Q(folder),Log,null);
   session.ConfirmActivation(output);session.Save(Journal);SetInfo("IR guardado. El próximo encendido con este preset cargará tu IR sin la app.",true);
#endif
#else
   string output=await Helper("editor","--activate-ir "+Q(folder),Log,null);
   session.ConfirmActivation(output);session.Save(Journal);SetInfo("IR activado. Prueba auriculares a volumen bajo. Puedes cerrar la app.",true);
#endif
  }catch(Exception e){session.phase="activation-failed";session.Save(Journal);SetInfo("No se pudo activar: "+e.Message,false,true);Log(e.ToString());}
  finally{busy=false;progress.Style=ProgressBarStyle.Continuous;RefreshUI();}
 }
 bool ConfirmInstallRequest(string request){
  return busy&&session!=null&&session.phase=="installing"&&OneClickSession.MatchesInstallRequest(request,session.candidateHash)&&FirmwareCodec.Hash(File.ReadAllBytes(session.candidate))==session.candidateHash;
 }
 static string Q(string value){if(value.Contains('"'))throw new ArgumentException("Ruta incorrecta");return "\""+value+"\"";}
 static async Task<string> Helper(string component,string arguments,Action<string> onLine,Func<string,bool> accept){
#if IN_PROCESS
  return await OneProcessRuntime.Run(component,arguments,onLine,accept);
#else
  string dir=Path.Combine(OneClickBundle.Root,component),name=component=="editor"?"EngineProbe.exe":"CentralAdapter.exe";
  var start=new ProcessStartInfo(Path.Combine(dir,name),arguments){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=dir,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true};
  start.EnvironmentVariables[component=="editor"?"CATALYSTIR_EDITOR_HOME":"CATALYSTIR_CENTRAL_HOME"]=dir;
  using(var process=new Process{StartInfo=start}){
   process.Start();Task<string> error=process.StandardError.ReadToEndAsync();var output=new System.Text.StringBuilder();string line;
   while((line=await process.StandardOutput.ReadLineAsync())!=null){
    output.AppendLine(line);if(onLine!=null)onLine(line);
    if(line.StartsWith("AWAIT_EULA=")){
     bool yes=false;try{yes=accept!=null&&accept(line.Substring(11));}catch(Exception e){if(onLine!=null)onLine(e.Message);}
     string hash=arguments.Split(' ').Length>0?ExtractHash(arguments):"";
     process.StandardInput.WriteLine(yes?"ACCEPT "+hash:"REJECT");process.StandardInput.Flush();
    }
   }
   await Task.Run(()=>process.WaitForExit());string errors=await error;output.Append(errors);
   if(process.ExitCode!=0)throw new InvalidOperationException("Auxiliar USB: código "+process.ExitCode+". "+errors+" Consulta el registro.");
   return output.ToString();
  }
#endif
 }
 static string ExtractHash(string command){var m=System.Text.RegularExpressions.Regex.Match(command,@"(?:^| )([a-f0-9]{64})(?: |$)");if(!m.Success)throw new InvalidDataException("Falta la huella de instalación");return m.Groups[1].Value;}
}
