using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Collections.Generic;

partial class OneClickApp {
 static void RequireFreeEditors(){foreach(var p in System.Diagnostics.Process.GetProcesses())using(p){string n;try{n=p.ProcessName;}catch{continue;}if(n=="Catalyst Edit"||n=="POD Go Edit"||n=="PODGoCustomFirmware"||n=="CatalystNAMControl"||n=="CatalystNAMTrial")throw new InvalidOperationException("Cierra "+n+" antes de instalar para liberar la conexión Line 6.");}}
 Panel namPanel;StableButton namTab,namChoose,namClear,namExport;Label namName,namLevelInfo;
 CheckBox namAuto,namKeepIR;NumericUpDown namGain,namInput;
 NamModel nam;string namPath;bool namSelected,namRefreshing;byte[][] previousIRs;
 public class NamProfile {public bool automatic=true;public double manualDb;public double inputDb=-12;}
 Dictionary<string,NamProfile> namProfiles=new Dictionary<string,NamProfile>();
 NamProfile namProfile;
 double NamDb {get{return nam==null?0:namProfile.automatic?nam.AutomaticForInput(namProfile.inputDb):namProfile.manualDb;}}
 string FullRecipe(){
  if(nam==null)return slots==null?null:slots.Recipe();
  string ir=slots.Count>0?slots.Recipe():namKeepIR!=null&&namKeepIR.Checked&&previousIRs!=null?"preserved:"+string.Join(",",previousIRs.Select(x=>x==null?"factory":FirmwareCodec.Hash(x)).ToArray()):"factory";
  return "v3|nam:"+nam.SourceHash+"|input:"+namProfile.inputDb.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|output:"+NamDb.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+ir;
 }
 byte[][] SelectedIRs(){return slots.Count>0?slots.Process():namKeepIR.Checked&&previousIRs!=null?previousIRs.Select(x=>x==null?null:(byte[])x.Clone()).ToArray():new byte[3][];}
 void FindPreviousIRs(){
  foreach(string journal in new[]{Journal,Path.Combine(OneClickBundle.DefaultData,"v2","installation.json")})try{
   var old=OneClickSession.Load(journal);if(old==null||(old.phase!="active"&&old.phase!="awaiting-reboot"&&old.phase!="activation-failed")||!File.Exists(old.candidate))continue;
   byte[] b=File.ReadAllBytes(old.candidate);if(FirmwareCodec.Hash(b)!=old.candidateHash)continue;var irs=MultiIRCodec.ReadIRs(b,true);
   if(irs.Any(x=>x!=null)){previousIRs=irs;return;}
  }catch{}
 }
 void InitializeNAM(){
  FindPreviousIRs();string settings=Path.Combine(data,"settings","nam-levels.json");if(File.Exists(settings)&&new FileInfo(settings).Length<100000)namProfiles=json.Deserialize<Dictionary<string,NamProfile>>(File.ReadAllText(settings))??namProfiles;
  for(int i=0;i<3;i++)slotButtons[i].Left=234+i*126;
  namTab=MakeButton("NAM");namTab.SetBounds(612,0,126,31);namTab.Click+=delegate{SelectNAM();};namTab.AccessibleName="NAM";workspace.Controls.Add(namTab);
  namPanel=new Panel{BackColor=Color.Transparent,Visible=false};namPanel.SetBounds(18,42,720,155);workspace.Controls.Add(namPanel);
  namChoose=MakeButton("Cargar NAM…");namChoose.SetBounds(0,5,130,36);namChoose.Click+=async delegate{using(var d=new OpenFileDialog{Filter="Neural Amp Model (*.nam)|*.nam",Title="Cargar NAM A2 Lite"})if(d.ShowDialog(this)==DialogResult.OK)await LoadNAM(d.FileName);};namPanel.Controls.Add(namChoose);
  namName=new DarkLabel{Text="Selecciona un archivo NAM",AutoEllipsis=true,UseMnemonic=false,Font=UITheme.Bold,ForeColor=UITheme.Muted,BackColor=Color.Transparent};namName.SetBounds(146,5,450,24);namPanel.Controls.Add(namName);
  var format=new DarkLabel{Text="A2 Lite · Mono · 48 kHz · Chime, grupo 1",Font=UITheme.Small,ForeColor=UITheme.Muted,BackColor=Color.Transparent};format.SetBounds(147,30,450,19);namPanel.Controls.Add(format);
  namClear=MakeButton("Vaciar");namClear.SetBounds(619,5,100,32);namClear.Click+=delegate{nam=null;namPath=null;namProfile=null;completedThisWindow=false;RefreshUI();};namPanel.Controls.Add(namClear);
  namAuto=new DarkCheckBox{Text="Volumen automático",Checked=true,Font=UITheme.Bold,ForeColor=UITheme.Text,BackColor=Color.Transparent};namAuto.SetBounds(0,61,205,27);namPanel.Controls.Add(namAuto);
  namGain=new GlassNumber{Minimum=-60,Maximum=12,DecimalPlaces=1,Increment=.5m};namGain.SetBounds(218,62,85,25);namPanel.Controls.Add(namGain);
  var db=new DarkLabel{Text="dB",AutoSize=true,Font=UITheme.Bold,BackColor=Color.Transparent};db.SetBounds(313,68,30,22);namPanel.Controls.Add(db);
  namLevelInfo=new DarkLabel{Text="Entrada: −12 dB · Volumen de salida ajustable",Font=UITheme.Small,ForeColor=UITheme.Muted,BackColor=Color.Transparent};namLevelInfo.SetBounds(351,59,365,36);namPanel.Controls.Add(namLevelInfo);
  namKeepIR=new DarkCheckBox{Text="Conservar los IR anteriores",Checked=previousIRs!=null,Font=UITheme.Bold,ForeColor=UITheme.Text,BackColor=Color.Transparent};namKeepIR.SetBounds(205,104,290,27);help.SetToolTip(namKeepIR,"Se utiliza si IR1, IR2 e IR3 están vacíos. Si cargas WAV, se instala el nuevo conjunto completo.");namPanel.Controls.Add(namKeepIR);
  var inputCaption=new DarkLabel{Text="Entrada",Font=UITheme.Bold,BackColor=Color.Transparent};inputCaption.SetBounds(0,109,65,25);namPanel.Controls.Add(inputCaption); namInput=new GlassNumber{Minimum=-36,Maximum=0,Value=-12,Increment=1};namInput.SetBounds(70,104,75,25);namPanel.Controls.Add(namInput); var inputDbCaption=new DarkLabel{Text="dB",Font=UITheme.Bold,BackColor=Color.Transparent};inputDbCaption.SetBounds(153,109,30,25);namPanel.Controls.Add(inputDbCaption); help.SetToolTip(namInput,"Calibración de entrada, independiente de Gain y del volumen de salida. Valor inicial −12 dB; reduce si aparecen artefactos en ataques fuertes."); namInput.ValueChanged+=async delegate{if(namRefreshing||nam==null||busy)return;namProfile.inputDb=(double)namInput.Value;completedThisWindow=false;busy=true;RefreshUI();SetInfo("Recalculando nivel de salida con la nueva entrada…");try{await Task.Run(()=>nam.AutomaticForInput(namProfile.inputDb));SaveNAMProfile();}catch(Exception e){SetInfo(e.Message,false,true);}finally{busy=false;RefreshUI();}}; namExport=MakeButton("Guardar firmware…");namExport.SetBounds(505,105,214,34);namExport.Click+=async delegate{await ExportNAM();};namPanel.Controls.Add(namExport);
  namAuto.CheckedChanged+=delegate{EditNAM(false);};namGain.ValueChanged+=delegate{EditNAM(true);};namKeepIR.CheckedChanged+=delegate{if(!namRefreshing){completedThisWindow=false;RefreshUI();}};
  if(CurrentTemplate&&session.hasNAM&&(session.phase=="awaiting-reboot"||session.phase=="activation-failed")){
   var saved=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(session.folder,"NAM-settings.json")));byte[] bytes=File.ReadAllBytes(Path.Combine(session.folder,"NAM-original.nam"));
   if(FirmwareCodec.Hash(bytes)!=(string)saved["source_sha256"])throw new InvalidDataException("El NAM pendiente ha cambiado");
   var selected=NamModel.Parse(bytes,(string)saved["name"]);namProfiles[selected.SourceHash]=new NamProfile{automatic=Convert.ToBoolean(saved["automatic"]),manualDb=Convert.ToDouble(saved["output_db"]),inputDb=Convert.ToDouble(saved["input_trim_db"])};AssignNAM(selected,Path.Combine(session.folder,"NAM-original.nam"));namSelected=true;
   if(Recipe!=session.recipe)throw new InvalidDataException("Los ajustes pendientes no coinciden con el firmware preparado.");
   byte[] candidate=File.ReadAllBytes(session.candidate);if(FirmwareCodec.Hash(candidate)!=session.candidateHash||!NamFirmwareCodec.ReadRange(candidate,NamLayout.Weights,7484).SequenceEqual(nam.Packed(namProfile.inputDb,NamDb)))throw new InvalidDataException("El firmware pendiente no coincide con los ajustes NAM");
  }
 }
 void SelectNAM(){if(busy)return;namSelected=true;RefreshUI();SetInfo(nam==null?"Carga una captura A2 Lite. Sin NAM se restaura el modelo Chime original.":"NAM preparado. Entrada −12 dB; Gain y Boost ajustables en el ampli.",nam!=null);}
 void ShowNAM(){
  if(namPanel==null)return;foreach(Control c in workspace.Controls)if(c!=namPanel&&c!=namTab&&c!=run&&!slotButtons.Contains(c as StableButton))c.Visible=!namSelected;
  if(namSelected)for(int i=0;i<3;i++)((DarkButton)slotButtons[i]).FillColor=Color.FromArgb(30,45,63);namPanel.Visible=namSelected;namTab.Visible=true;namTab.Enabled=!busy;((DarkButton)namTab).FillColor=namSelected?Color.FromArgb(31,111,87):Color.FromArgb(30,45,63);MarkSuccess(namTab,nam!=null&&usbCount==1);namTab.Text="NAM"+(nam!=null?" ✓":"");
  bool pending=CurrentTemplate&&(session.phase=="awaiting-reboot"||session.phase=="activation-failed");namTab.Enabled=!busy&&!pending;namChoose.Enabled=!pending;namClear.Enabled=nam!=null&&!pending;namExport.Enabled=nam!=null&&!busy&&!pending;
  namAuto.Enabled=nam!=null&&!pending;namGain.Enabled=nam!=null&&!namProfile.automatic&&!pending;namKeepIR.Enabled=previousIRs!=null&&slots.Count==0&&!pending;namInput.Enabled=nam!=null&&!pending;
  namRefreshing=true;namName.Text=nam==null?"Selecciona un archivo NAM":nam.Name+"  ✓";namAuto.Checked=nam==null||namProfile.automatic;namGain.Value=nam==null?0:(decimal)Math.Round(NamDb,1);namInput.Value=nam==null?-12:(decimal)namProfile.inputDb;namRefreshing=false;
  bool good=nam!=null&&usbCount==1;MarkSuccess(namName,good);MarkSuccess(namAuto,good&&namAuto.Checked);MarkSuccess(namKeepIR,good&&namKeepIR.Checked);((GlassNumber)namGain).Successful=good;
  namLevelInfo.Text=nam==null?"Entrada: −12 dB · Volumen de salida ajustable":"Entrada: "+namProfile.inputDb.ToString("F0")+" dB · Salida "+(namProfile.automatic?"automática: ":"manual: ")+NamDb.ToString("F1")+" dB";MarkSuccess(namLevelInfo,good);namLevelInfo.Font=good?UITheme.SmallBold:UITheme.Small;
  if(nam!=null){run.Text=pending?"Apaga y enciende el amplificador":InstalledSelection?"NAM + IR guardados ✓":"Instalar NAM + IR";run.Enabled=usbCount==1&&!busy&&!pending&&!InstalledSelection;}
  if(session!=null&&session.hasNAM&&pending)SetInfo("Transferencia terminada. Apaga y enciende normalmente, sin botones. NAM se utiliza en CHIME GROUP 1.");
 }
 void EditNAM(bool manual){if(namRefreshing||nam==null||busy)return;namRefreshing=true;if(manual)namAuto.Checked=false;namProfile.automatic=namAuto.Checked;namProfile.manualDb=(double)namGain.Value;namRefreshing=false;completedThisWindow=false;SaveNAMProfile();RefreshUI();}
 void SaveNAMProfile(){if(nam==null)return;namProfiles[nam.SourceHash]=namProfile;Directory.CreateDirectory(Path.Combine(data,"settings"));File.WriteAllText(Path.Combine(data,"settings","nam-levels.json"),json.Serialize(namProfiles));}
 void AssignNAM(NamModel model,string file){
  var p=new NamProfile();if(namProfiles.ContainsKey(model.SourceHash))p=namProfiles[model.SourceHash];if(p==null||double.IsNaN(p.manualDb)||double.IsInfinity(p.manualDb)||p.manualDb<-60||p.manualDb>12||double.IsNaN(p.inputDb)||double.IsInfinity(p.inputDb)||p.inputDb<-36||p.inputDb>0)throw new InvalidDataException("Perfil NAM incorrecto");
  string folder=Path.Combine(data,"nams");Directory.CreateDirectory(folder);string cached=Path.Combine(folder,model.SourceHash+".nam");if(!File.Exists(cached))File.WriteAllBytes(cached,model.Source);if(FirmwareCodec.Hash(File.ReadAllBytes(cached))!=model.SourceHash)throw new InvalidDataException("La copia NAM no coincide");
  nam=model;namPath=cached;namProfile=p;completedThisWindow=false;SaveNAMProfile();help.SetToolTip(namName,file);RefreshUI();SetInfo("NAM válido. La entrada y el volumen de salida se ajustan por separado.",true);
 }
 async Task LoadNAM(string file){if(busy)return;busy=true;RefreshUI();SetInfo("Comprobando el NAM y calculando su nivel de salida…");
  try{var selected=await Task.Run(()=>NamModel.Load(file));AssignNAM(selected,file);}catch(Exception e){if(preview)throw;MessageBox.Show(this,e.Message,"NAM no admitido");}finally{busy=false;RefreshUI();}
 }
 byte[] BuildSelection(){
  byte[] template=OneClickBundle.Template();if(nam==null){var b=MultiIRCodec.WithIRs(template,slots.Process());FirmwareCodec.TestGuards(template,b);return b;}
  if(FirmwareCodec.Hash(File.ReadAllBytes(namPath))!=nam.SourceHash)throw new InvalidDataException("El NAM guardado ha cambiado. Vuelve a cargarlo.");var built=NamFirmwareCodec.Build(nam,namProfile.inputDb,NamDb,SelectedIRs());NamFirmwareCodec.TestGuards(template,built);return built;
 }
 object NAMSettings(){return nam==null?null:new{source_sha256=nam.SourceHash,name=nam.Name,input_trim_db=namProfile.inputDb,automatic=namProfile.automatic,output_db=NamDb,weights_fnv1a="0x"+NamFirmwareCodec.Fingerprint(nam.Packed(namProfile.inputDb,NamDb)).ToString("x8"),target="CHIME GROUP 1",automatic_method="fixed multilevel synthetic excitation; RMS matched to accepted 5150; peak margin6dB; maximum boost12dB"};}
 async Task ExportNAM(){if(nam==null||busy)return;using(var d=new SaveFileDialog{Filter="Firmware Catalyst (*.ctf)|*.ctf",FileName="Catalyst-NAM-IR.ctf",Title="Guardar firmware NAM + IR"}){
  if(d.ShowDialog(this)!=DialogResult.OK)return;busy=true;RefreshUI();try{string recipe=Recipe;var settings=NAMSettings();byte[] b=await Task.Run(()=>BuildSelection());File.WriteAllBytes(d.FileName,b);File.WriteAllText(d.FileName+".json",json.Serialize(new{sha256=FirmwareCodec.Hash(b),recipe=recipe,nam=settings,scope="Preparación local; no se ha instalado"}));SetInfo("Firmware guardado. No se ha instalado en el ampli.",true);}catch(Exception e){MessageBox.Show(this,e.Message,"Firmware");}finally{busy=false;RefreshUI();}
 }}
 static bool NamCommands(string[] args,out int result){result=0;if(args.Length==0||!args[0].StartsWith("--nam-",StringComparison.Ordinal))return false;
  if(args[0]=="--nam-ui-state-check"&&args.Length==5){preview=true;data=Path.GetFullPath(args[1]);Directory.CreateDirectory(data);var m=NamModel.Load(args[2]);string baseline=Path.GetFullPath(args[3]);Application.EnableVisualStyles();
   using(var f=new OneClickApp()){
    f.usbCount=1;f.previousIRs=MultiIRCodec.ReadIRs(File.ReadAllBytes(baseline),true);f.namKeepIR.Checked=true;f.AssignNAM(m,args[2]);f.SelectNAM();if(!f.run.Enabled||f.slots.Count!=0)throw new Exception("NAM solo no permite instalar");
    string before=f.Recipe;f.namInput.Value=-18;var deadline=DateTime.UtcNow.AddSeconds(20);while(f.busy&&DateTime.UtcNow<deadline){Application.DoEvents();System.Threading.Thread.Sleep(10);}if(f.busy||f.namProfile.inputDb!=-18||f.Recipe==before||!f.namProfile.automatic)throw new Exception("Entrada y salida no son independientes");
    f.namGain.Value=-2;if(f.namProfile.automatic||f.namProfile.manualDb!=-2||f.namProfile.inputDb!=-18)throw new Exception("Volumen manual altera entrada");
    f.namInput.Value=-12;deadline=DateTime.UtcNow.AddSeconds(20);while(f.busy&&DateTime.UtcNow<deadline){Application.DoEvents();System.Threading.Thread.Sleep(10);}f.namGain.Value=0;f.slots.Save(f.SlotsPath);
    string folder=Path.Combine(data,"pending");Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"NAM-original.nam"),m.Source);File.WriteAllText(Path.Combine(folder,"NAM-settings.json"),json.Serialize(f.NAMSettings()));
    f.session=new OneClickSession{folder=folder,candidate=baseline,candidateHash=FirmwareCodec.Hash(File.ReadAllBytes(baseline)),recipe=f.Recipe,phase="awaiting-reboot",templateHash=NamFirmwareCodec.AppTemplateHash,hasNAM=true};f.session.Save(f.Journal);
    using(var reopened=new OneClickApp()){reopened.usbCount=1;reopened.RefreshUI();if(reopened.nam==null||!reopened.namSelected||reopened.Recipe!=f.Recipe||reopened.run.Enabled||reopened.run.Text!="Apaga y enciende el amplificador")throw new Exception("No se recupera el NAM pendiente");}
    f.session.phase="active";f.session.Save(f.Journal);using(var fresh=new OneClickApp()){fresh.usbCount=1;fresh.RefreshUI();if(fresh.nam!=null||fresh.slots.Count!=0||fresh.InstalledSelection||fresh.run.Enabled)throw new Exception("Una apertura nueva no se reinicia vacía");}
    File.WriteAllText(args[4],json.Serialize(new{passed=true,input_output_independent=true,manual_volume_independent=true,NAM_only_allowed=true,previous_IRs_preserved=true,pending_NAM_resumed=true,opening_starts_empty=true,hardware_accessed=false}));
   }return true;
  }
  if(args[0]=="--nam-inspect"&&(args.Length==3||args.Length==4)){data=Path.GetFullPath(args[2]);Directory.CreateDirectory(data);var m=NamModel.Load(args[1]);byte[] state,silence;double inputDb=args.Length==4?double.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture):-12;var packed=m.Packed(inputDb,0);NamModel.Reset(NamModel.Floats(packed),out state,out silence);
   File.WriteAllBytes(Path.Combine(data,"packed.bin"),packed);File.WriteAllBytes(Path.Combine(data,"state.bin"),state);File.WriteAllBytes(Path.Combine(data,"silence.bin"),silence);File.WriteAllBytes(Path.Combine(data,"input.f32"),NamModel.Bytes(NamModel.CalibrationSignal()));File.WriteAllBytes(Path.Combine(data,"output.f32"),NamModel.Bytes(NamModel.RenderPacked(NamModel.Floats(packed),NamModel.CalibrationSignal())));
   File.WriteAllText(Path.Combine(data,"report.json"),json.Serialize(new{source_hash=m.SourceHash,automatic_db=m.AutomaticForInput(inputDb),rms=m.MeasuredRms,peak=m.MeasuredPeak,input_trim_db=inputDb,hardware_accessed=false}));return true;
  }
  if(args[0]=="--nam-candidate"&&(args.Length>=5&&args.Length<=7)){string output=Path.GetFullPath(args[3]);if(File.Exists(output))throw new IOException("No se sobrescribe un firmware existente");data=Path.Combine(Path.GetDirectoryName(output),"offline-runtime");OneClickBundle.Prepare(data);var m=NamModel.Load(args[1]);byte[][] irs=args[2]=="-"?new byte[3][]:MultiIRSlots.Load(args[2]).Process();if(args.Length>=6&&args[5]!="-")irs=MultiIRCodec.ReadIRs(File.ReadAllBytes(args[5]),true);
   double inputDb=args.Length==7?double.Parse(args[6],System.Globalization.CultureInfo.InvariantCulture):-12;double db=args[4]=="auto"?m.AutomaticForInput(inputDb):double.Parse(args[4],System.Globalization.CultureInfo.InvariantCulture);byte[] b=NamFirmwareCodec.Build(m,inputDb,db,irs);NamFirmwareCodec.TestGuards(OneClickBundle.Template(),b);File.WriteAllBytes(output,b);File.WriteAllText(output+".json",json.Serialize(new{sha256=FirmwareCodec.Hash(b),source_hash=m.SourceHash,input_trim_db=inputDb,output_db=db,nam_weights_fnv1a="0x"+NamFirmwareCodec.Fingerprint(m.Packed(inputDb,db)).ToString("x8"),hardware_accessed=false}));return true;
  }
  if(args[0]=="--nam-ui-preview"&&args.Length==6){preview=true;data=Path.GetFullPath(args[1]);Directory.CreateDirectory(data);var model=args[2]=="-"?null:NamModel.Load(args[2]);Application.EnableVisualStyles();using(var f=new OneClickApp()){
   f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-12000,-12000);f.ShowInTaskbar=false;f.usbCount=args[5]=="disconnected"?0:1;if(model!=null)f.AssignNAM(model,args[2]);if(args[5]!="ir")f.SelectNAM();else f.SelectSlot(0);f.RefreshUI();f.Show();Application.DoEvents();using(var b=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(b,new Rectangle(Point.Empty,f.Size));b.Save(args[3]);}
   if(f.ClientSize!=new Size(800,656)||f.run.Bounds!=new Rectangle(188,207,384,41)||f.choose.Bounds!=new Rectangle(18,47,130,36))throw new Exception("Se alteró el diseño IR");if(args[5]=="ir"&&(f.namPanel.Visible||!f.choose.Visible))throw new Exception("Pestaña IR no recuperada");
   File.WriteAllText(args[4],json.Serialize(new{passed=true,layout_preserved=true,nam_tab_selected=f.namSelected,nam_loaded=f.nam!=null,install_enabled=f.run.Enabled,workspace_enabled=f.workspace.Enabled,hardware_accessed=false}));f.Hide();}return true;
  }
  throw new ArgumentException("Argumentos NAM incorrectos");
 }
}
