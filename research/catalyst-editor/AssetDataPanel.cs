using System;using System.IO;using System.Linq;using System.Drawing;using System.Security.Cryptography;using System.Threading;using System.Threading.Tasks;using System.Windows.Forms;
namespace CFEditor {
sealed class AssetLabel:Label {
 public bool Wrap;
 public AssetLabel(){AutoSize=false;BackColor=Color.Transparent;ForeColor=UITheme.Text;Font=FirmwareText.Body;UseMnemonic=false;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);}
 protected override void OnPaintBackground(PaintEventArgs e){BlackTheme.Behind(e.Graphics,this);}
 protected override void OnPaint(PaintEventArgs e){ClippedText.Draw(e.Graphics,Text,Font,ClientRectangle,ForeColor,(Wrap?TextFormatFlags.WordBreak:TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter)|TextFormatFlags.EndEllipsis);}
}
// Local workspace until a transport is explicitly attached by the device owner.
// Constructing/selecting/importing never probes USB, loads the SDK or flashes.
sealed partial class AssetDataPanel:Panel {
 readonly AssetWorkspace workspace=new AssetWorkspace();readonly AssetPreparation[] prepared=new AssetPreparation[5];readonly int[] revisions=new int[5];readonly AssetReceipt[] receipts=new AssetReceipt[5];
 readonly GlassPanel card=new GlassPanel(),logCard=new GlassPanel();readonly ActionButton[] tabs=new ActionButton[5];
 readonly ActionButton choose=new ActionButton(),clear=new ActionButton(),send=new ActionButton();
 readonly AssetLabel filename=new AssetLabel(),summary=new AssetLabel(),level=new AssetLabel(),inputCaption=new AssetLabel(),inputUnit=new AssetLabel(),outputUnit=new AssetLabel(),lowCaption=new AssetLabel(),highCaption=new AssetLabel(),filterUnit=new AssetLabel(),levelHint=new AssetLabel(),receiverState=new AssetLabel(),stored=new AssetLabel(),scope=new AssetLabel(),activityTitle=new AssetLabel();
 readonly CheckBox automatic=new CheckBox(),filters=new CheckBox();readonly GlassNumber input=new GlassNumber(),output=new GlassNumber(),low=new GlassNumber(),high=new GlassNumber();
 readonly GlassProgress progress=new GlassProgress();readonly DarkLogBox log=new DarkLogBox();readonly ToolTip help=new ToolTip();readonly System.Windows.Forms.Timer recalc=new System.Windows.Forms.Timer{Interval=250};
 AssetTransfer client;byte selected=3;bool active,preview,updating,importing,sending,calculating,connecting,disposed,knownReceiver;string receiverText="Receptor de datos sin comprobar",operationError;
 static readonly int[] VisualSlots={3,4,0,1,2};
 internal void SetKnownReceiver(bool known){knownReceiver=known;Render();}
 public event Action StateChanged;internal event Action<uint,string,uint> ReceiverVerified;internal event Action InstallationStarted,ReceiverLost;
 public bool Busy{get{return importing||sending||calculating||connecting;}}public int SelectedSlot{get{return selected;}}
 public bool Ready{get{return active&&!Busy&&client!=null&&client.Ready&&(dataSession==null||dataSession.Ready);}}
 public string ConnectionText{get{return client!=null&&client.Ready?"Receptor de datos conectado":"Conexión de datos sin comprobar";}}
 public AssetDataPanel(bool design){
  preview=design;BackColor=Color.Transparent;DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);Controls.Add(card);Controls.Add(logCard);Controls.Add(receiverState);Controls.Add(scope);Controls.Add(progress);
  card.Padding=Padding.Empty;logCard.Padding=Padding.Empty;
  for(int i=0;i<5;i++){int index=i;var tab=new ActionButton{Text=AssetWorkspace.SlotName(i),Tag=i,Height=32,TabIndex=Array.IndexOf(VisualSlots,i),Font=FirmwareText.Bold,AccessibleName=AssetWorkspace.SlotName(i)};tabs[i]=tab;tab.Click+=delegate{SelectSlot(index);};card.Controls.Add(tab);}
  choose.Font=clear.Font=FirmwareText.Bold;send.Font=FirmwareText.Action;choose.Text="Cargar IR…";choose.Click+=async delegate{await Choose();};clear.Text="Vaciar";clear.Click+=delegate{if(Busy)return;workspace.Slots[selected]=null;prepared[selected]=null;revisions[selected]++;Render();Note("Selección local vaciada. No se ha borrado nada del amplificador.");};send.Text="Enviar al ampli";send.Accent=UITheme.Success;send.Click+=async delegate{await Send();};
  filename.Font=FirmwareText.Bold;summary.Font=FirmwareText.Small;summary.ForeColor=UITheme.Muted;level.Font=FirmwareText.Bold;level.ForeColor=UITheme.Success;levelHint.ForeColor=UITheme.Muted;levelHint.Font=FirmwareText.Small;levelHint.Wrap=true;
  automatic.Text="Volumen de salida automático";filters.Text="Cortes automáticos";foreach(var box in new[]{automatic,filters}){box.AutoSize=false;box.BackColor=Color.Transparent;box.ForeColor=UITheme.Success;box.Font=FirmwareText.Bold;box.Checked=true;box.CheckedChanged+=delegate{SettingsChanged();};}
  output.Minimum=-60;output.Maximum=12;output.DecimalPlaces=1;output.Increment=.1m;input.Minimum=-36;input.Maximum=0;input.Value=(decimal)LibrarySlot.DefaultInputDb;input.DecimalPlaces=1;input.Increment=.5m;
  low.Minimum=20;low.Maximum=2000;low.Value=80;high.Minimum=1000;high.Maximum=22000;high.Value=8000;
  foreach(var n in new[]{input,output,low,high}){n.Font=FirmwareText.Bold;n.ValueChanged+=delegate{SettingsChanged();};}
  help.SetToolTip(input,"Nivel antes del NAM. Modifica la saturación y la respuesta; se aplica al volver a enviar este NAM.");inputCaption.Text="Entrada NAM";inputCaption.Font=FirmwareText.Bold;inputUnit.Text=outputUnit.Text="dB";lowCaption.Text="Low Cut";highCaption.Text="High Cut";lowCaption.Font=highCaption.Font=FirmwareText.Bold;filterUnit.Text="Hz · 12 dB/oct";filterUnit.Font=FirmwareText.Small;filterUnit.ForeColor=UITheme.Muted;
  stored.Font=FirmwareText.Small;stored.ForeColor=UITheme.Muted;scope.Wrap=true;scope.Font=FirmwareText.Small;scope.ForeColor=UITheme.Muted;receiverState.Font=FirmwareText.Bold;
  foreach(Control c in new Control[]{choose,clear,filename,summary,automatic,output,outputUnit,level,levelHint,inputCaption,input,inputUnit,filters,lowCaption,low,highCaption,high,filterUnit,send,stored})card.Controls.Add(c);
  activityTitle.Text="ACTIVIDAD";activityTitle.Font=FirmwareText.Bold;activityTitle.ForeColor=UITheme.Muted;logCard.Controls.Add(activityTitle);log.BackColor=Color.FromArgb(13,15,18);log.ForeColor=UITheme.Muted;log.Font=FirmwareText.Log;logCard.Controls.Add(log);
  recalc.Tick+=async delegate{recalc.Stop();await Calculate();};BuildConnectionAction();BuildInitialAction();WireDrop(this);Render();
 }
 protected override void OnPaintBackground(PaintEventArgs e){BlackTheme.Behind(e.Graphics,this);}
 protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Relayout();}
 void At(Control c,int x,int y,int w,int h){c.SetBounds(EditorZoom.Px(x),EditorZoom.Px(y),EditorZoom.Px(Math.Max(1,w)),EditorZoom.Px(Math.Max(1,h)));}
 public void Relayout(){
  if(card==null||ClientSize.Width==0)return;int w=(int)Math.Round(ClientSize.Width/EditorZoom.Factor),h=(int)Math.Round(ClientSize.Height/EditorZoom.Factor);if(w<600)return;
  At(card,0,0,w,346);int gap=6,tabWidth=(w-40-4*gap)/5;for(int i=0;i<5;i++)At(tabs[VisualSlots[i]],20+i*(tabWidth+gap),10,tabWidth,32);
  At(choose,20,62,165,40);At(clear,w-126,62,106,36);At(filename,205,59,w-350,28);At(summary,205,91,w-350,30);
  At(automatic,20,137,310,30);At(output,348,138,110,29);At(outputUnit,471,137,34,30);At(level,540,133,w-560,32);
  At(levelHint,540,169,w-560,40);
  At(inputCaption,20,211,135,28);At(input,168,210,110,29);At(inputUnit,292,210,38,30);
  At(filters,20,211,238,30);At(lowCaption,278,212,95,28);At(low,377,210,100,29);At(highCaption,501,212,100,28);At(high,605,210,110,29);At(filterUnit,735,211,w-755,30);
  int sendWidth=Math.Min(600,w-350),rowX=(w-sendWidth-224-12)/2;At(receiverConnect,rowX,268,224,44);At(send,rowX+236,268,sendWidth,44);At(stored,20,315,w-40,24);
  At(receiverState,8,354,w-16,28);At(scope,8,385,w-16,38);At(progress,0,432,w,8);
  int logHeight=Math.Max(80,h-454);At(logCard,0,454,w,logHeight);At(activityTitle,16,8,w-32,28);At(log,16,44,w-32,logHeight-58);LayoutInitial(w);
 }
 public void SetActive(bool value){active=value;if(!value)recalc.Stop();Render();if(value&&workspace.Slots[selected]!=null&&prepared[selected]==null)recalc.Start();}
 public void SetPreview(bool value){if(HasDataOwner)throw new InvalidOperationException("Cierra primero la conexión de datos.");preview=value;DropReceiver();Render();}
 void Notify(){if(!disposed&&StateChanged!=null)StateChanged();}
 void Note(string text,bool error=false){if(disposed)return;operationError=error?text:null;log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);}
 void Render(){
  if(disposed)return;updating=true;try{
   foreach(Control item in card.Controls)item.Visible=true;
   var asset=workspace.Slots[selected];bool nam=selected>=3,editable=active&&!importing&&!sending&&!connecting;var settings=asset==null?null:asset.Settings;var ready=prepared[selected];
   for(int i=0;i<5;i++){tabs[i].Text=AssetWorkspace.SlotName(i)+(workspace.Slots[i]!=null?" ✓":"");tabs[i].Active=i==selected;tabs[i].Enabled=editable;tabs[i].Invalidate();}
   choose.Text=nam?"1. Cargar NAM…":"1. Cargar IR…";choose.AccessibleName=choose.Text;choose.Enabled=editable&&!calculating;clear.Enabled=editable&&asset!=null&&!calculating;
   filename.Text=asset==null?"Arrastra un archivo o pulsa «"+(nam?"Cargar NAM":"Cargar IR")+"»":asset.Name+"  ✓ Preparado";filename.ForeColor=asset==null?UITheme.Muted:UITheme.Success;help.SetToolTip(filename,asset==null?"":asset.Name+" · Preparado en el PC; pendiente de enviar");
   summary.Text=asset==null?(nam?"A2 Lite · Mono · 48 kHz":"Conversión automática a mono · 48 kHz · 2.048 muestras"):asset.Summary;
   automatic.Enabled=editable&&asset!=null;automatic.Checked=settings==null?!nam:settings.Automatic;output.Enabled=editable&&settings!=null&&!settings.Automatic;output.Value=(decimal)Math.Max(-60,Math.Min(12,settings==null?(nam?LibrarySlot.DefaultNamOutputDb:0):settings.Automatic&&ready!=null?ready.Info.OutputDb:settings.OutputDb));
   foreach(Control c in new Control[]{inputCaption,input,inputUnit})c.Visible=nam;input.Enabled=nam&&editable&&settings!=null;input.Value=(decimal)(settings==null?LibrarySlot.DefaultInputDb:settings.InputDb);
   foreach(Control c in new Control[]{filters,lowCaption,low,highCaption,high,filterUnit})c.Visible=!nam;filters.Enabled=editable&&asset!=null;filters.Checked=settings==null||settings.Filters;
   low.Enabled=high.Enabled=editable&&settings!=null&&settings.Filters&&!nam;low.Value=(decimal)(settings==null?80:settings.LowHz);high.Value=(decimal)(settings==null?8000:settings.HighHz);
   level.Visible=asset!=null&&ready==null;level.Text=level.Visible?"Calculando volumen…":"";level.ForeColor=ready==null?UITheme.Warning:UITheme.Success;
   levelHint.Visible=true;levelHint.Text=nam?"Entrada: saturación y respuesta. Salida: volumen final.":"Normalización y cortes se aplican antes de enviar el IR.";
   bool pending=client!=null&&client.HasPending(selected);send.Text=pending?"3. Comprobar guardado de "+AssetWorkspace.SlotName(selected):"3. Enviar "+AssetWorkspace.SlotName(selected)+" al ampli";send.Enabled=Ready&&(pending||ready!=null);send.Active=send.Enabled;send.Invalidate();
   var receipt=receipts[selected];stored.Text=receipt==null?"Contenido del ampli pendiente de consultar":receipt.Info==null?"En el ampli: sin recurso registrado":"En el ampli: "+receipt.Info.Name+" · Generación "+receipt.Generation;stored.ForeColor=receipt!=null&&receipt.Info!=null?UITheme.Success:UITheme.Muted;
   receiverState.Text=operationError??(preview?"Vista previa · Preparación local":knownReceiver&&client==null?"CF compatible · Conexión de datos pendiente":receiverText);receiverState.ForeColor=operationError!=null?UITheme.Error:knownReceiver||client!=null&&client.Ready?UITheme.Success:preview?UITheme.Muted:UITheme.Warning;
   scope.Text=pending?"Consulta el guardado anterior antes de reenviar.":"Solo se modifica "+AssetWorkspace.SlotName(selected)+". Las demás posiciones se conservan.";
   if(sending)scope.Text="Envío en curso. Espera a que termine para tocar y mantén el USB y la alimentación conectados.";
   progress.Style=importing||calculating||connecting?ProgressBarStyle.Marquee:ProgressBarStyle.Continuous;
  }finally{updating=false;}RenderConnectionAction();RenderInitial();Notify();
 }
 public void SelectSlot(int slot){if(slot<0||slot>=5)throw new ArgumentOutOfRangeException("slot");if(importing||sending||connecting)return;selected=(byte)slot;operationError=null;Render();if(workspace.Slots[selected]!=null&&prepared[selected]==null)recalc.Start();}
 async Task Choose(){using(var dialog=new OpenFileDialog{Filter=selected>=3?"Neural Amp Modeler (*.nam)|*.nam":IRImport.FileFilter,CheckFileExists=true}){if(dialog.ShowDialog(FindForm())!=DialogResult.OK)return;await ImportFile(selected,dialog.FileName);}}
 internal async Task<bool> ImportFile(int slot,string file){
  if(initialMode||slot<0||slot>=5||!active||importing||sending||connecting||calculating)return false;importing=true;recalc.Stop();Render();
  try{var asset=await Task.Run(()=>AssetWorkspace.Import((byte)slot,file));workspace.Slots[slot]=asset;prepared[slot]=asset.InitialPreparation;asset.InitialPreparation=null;revisions[slot]++;selected=(byte)slot;Note("✓ "+AssetWorkspace.SlotName(slot)+": "+asset.Name+" preparado. Pulsa «3. Enviar» para guardarlo en el ampli.");return true;}
  catch(Exception error){Note(error.Message,true);return false;}finally{importing=false;Render();}
 }
 void SettingsChanged(){if(updating||importing||sending||connecting)return;var asset=workspace.Slots[selected];if(asset==null)return;var s=asset.Settings;s.Automatic=automatic.Checked;s.OutputDb=(double)output.Value;s.InputDb=selected>=3?(double)input.Value:0;s.Filters=selected<3&&filters.Checked;s.LowHz=(double)low.Value;s.HighHz=(double)high.Value;prepared[selected]=null;revisions[selected]++;Render();recalc.Stop();recalc.Start();}
 async Task Calculate(){
  if(disposed||!active||importing||sending)return;if(connecting||calculating){recalc.Start();return;}byte slot=selected;var asset=workspace.Slots[slot];if(asset==null||prepared[slot]!=null)return;
  int revision=revisions[slot];var snapshot=AssetWorkspace.Snapshot(asset.Settings);calculating=true;Render();
  try{var result=await Task.Run(()=>AssetWorkspace.Prepare(slot,snapshot));if(!disposed&&revisions[slot]==revision&&ReferenceEquals(workspace.Slots[slot],asset))prepared[slot]=result;}
  catch(Exception error){if(!disposed&&revisions[slot]==revision)Note("Revisa los ajustes de "+AssetWorkspace.SlotName(slot)+": "+error.Message,true);}
  finally{calculating=false;if(!disposed){Render();if(revisions[slot]!=revision&&selected==slot)recalc.Start();}}
 }
 static uint Nonce(){using(var random=RandomNumberGenerator.Create()){var b=new byte[4];uint result;do{random.GetBytes(b);result=BitConverter.ToUInt32(b,0);}while(result==0);return result;}}
 // Supplied by an explicit connection owner; no discovery is done here.
 internal async Task UseReceiver(IAssetExchange exchange,uint product,CancellationToken cancel,IAssetPendingJournal journal=null){
  if(preview)throw new InvalidOperationException("La vista previa no puede conectar un dispositivo.");if(Busy)throw new InvalidOperationException("Espera a que termine la operación.");
  DropReceiver();connecting=true;receiverText="Comprobando el receptor de datos…";Render();
  try{var candidate=new AssetTransfer(exchange,product,journal);await candidate.Connect(Nonce(),cancel);await AdoptReceiver(candidate,cancel);}
  catch{DropReceiver();throw;}finally{connecting=false;Render();}
 }
 async Task AdoptReceiver(AssetTransfer candidate,CancellationToken cancel){
   if(candidate==null)throw new InvalidOperationException("Comprueba primero el receptor del amplificador");candidate.RequireCompatibleReceiver();
   int pending=-1;for(byte slot=0;slot<5;slot++)if(candidate.HasPending(slot)){pending=slot;break;}
   // An abandoned transfer may still hold the receiver's serialized store.
   // Do not let advisory reads of other slots hide its reconciliation action.
   // Connection must not wait for a full filesystem scan of four unrelated
   // banks. Upload still reads a fresh complete receipt for its own target.
   if(pending<0){byte slot=selected;receiverText="Consultando "+AssetWorkspace.SlotName(slot)+"…";Render();try{var receipt=await candidate.ReadSlot(slot,Nonce(),cancel);receipts[slot]=receipt??new AssetReceipt{Slot=slot};}catch(OperationCanceledException){throw;}catch(Exception error){throw new IOException("No se ha podido consultar "+AssetWorkspace.SlotName(slot)+": "+error.Message,error);}}
   else selected=(byte)pending;
   client=candidate;receiverText=pending<0?"Receptor compatible · Carga de datos disponible":"Transferencia anterior pendiente de comprobar";Note(pending<0?"Receptor de datos comprobado. No se ha instalado firmware.":"Se ha recuperado un registro pendiente. Comprueba su resultado antes de volver a enviar.");
 }
 internal void DropReceiver(){client=null;Array.Clear(receipts,0,receipts.Length);receiverText="Receptor de datos sin comprobar";}
 async Task Send(){
  if(initialMode){if(WaitingInitial)await ConnectionAction();else await InstallInitial();return;}
  if(!Ready)return;if(client.HasPending(selected)){await ResolvePending();return;}if(prepared[selected]==null)return;byte slot=selected;var asset=prepared[slot];uint tx;do{tx=Nonce();}while(receipts[slot]!=null&&receipts[slot].Transaction==tx);
  sending=true;progress.Value=0;Render();try{
   var report=new Progress<AssetProgress>(p=>{if(disposed||!sending)return;progress.Value=Math.Max(0,Math.Min(100,(int)(100L*p.Completed/Math.Max(1,p.Total))));receiverState.Text=p.Stage+" "+AssetWorkspace.SlotName(slot)+"…";});
   var receipt=await client.Upload(slot,tx,asset.Data,asset.Info,report,CancellationToken.None);receipts[slot]=receipt;receiverText="✓ "+AssetWorkspace.SlotName(slot)+" guardado en el amplificador";Note("✓ "+AssetWorkspace.SlotName(slot)+" guardado en el amplificador.");
  }catch(AssetUncertainException error){receiverText="Guardado pendiente de comprobar";Note(error.Message,true);}
  catch(Exception error){DropReceiver();Note(error.Message,true);}finally{sending=false;Render();}
 }
 async Task ResolvePending(){
  byte slot=selected;sending=true;Render();try{
   var resolved=await client.Reconcile(slot,Nonce(),CancellationToken.None);receipts[slot]=resolved.Receipt??new AssetReceipt{Slot=slot};
   receiverText=resolved.Stored?"✓ "+AssetWorkspace.SlotName(slot)+" guardado en el amplificador":"Comprobado: el envío no se guardó. Puedes volver a enviarlo.";
   Note(resolved.Stored?"✓ El recibo confirma el envío anterior de "+AssetWorkspace.SlotName(slot)+". No se ha reenviado el recurso.":"El amplificador conserva el estado anterior de "+AssetWorkspace.SlotName(slot)+". No se ha enviado nada al comprobar.");
  }catch(Exception error){Note(error.Message,true);}finally{sending=false;Render();}
 }
 static bool FileDrop(IDataObject data,out string file){file=null;try{if(data==null||!data.GetDataPresent(DataFormats.FileDrop))return false;var files=data.GetData(DataFormats.FileDrop) as string[];if(files==null||files.Length!=1)return false;file=files[0];return !string.IsNullOrWhiteSpace(file)&&(Path.GetExtension(file).Equals(".nam",StringComparison.OrdinalIgnoreCase)||IRImport.SupportedPath(file));}catch{return false;}}
 int TargetSlot(Control target,string file){for(Control c=target;c!=null&&c!=this;c=c.Parent)for(int i=0;i<tabs.Length;i++)if(c==tabs[i])return AssetWorkspace.Accepts(i,file)?i:-1;bool nam=Path.GetExtension(file).Equals(".nam",StringComparison.OrdinalIgnoreCase);return nam?(selected>=3?selected:3):(selected<3?selected:0);}
 void WireDrop(Control root){root.AllowDrop=true;root.DragEnter+=DragFeedback;root.DragOver+=DragFeedback;root.DragDrop+=async delegate(object sender,DragEventArgs e){await ImportDrop((Control)sender,e.Data);};foreach(Control c in root.Controls)WireDrop(c);}
 void DragFeedback(object sender,DragEventArgs e){string file;e.Effect=!initialMode&&active&&!Busy&&(e.AllowedEffect&DragDropEffects.Copy)!=0&&FileDrop(e.Data,out file)&&TargetSlot((Control)sender,file)>=0?DragDropEffects.Copy:DragDropEffects.None;}
 internal Task<bool> ImportDrop(Control target,IDataObject data){string file;if(initialMode||!active||Busy||!FileDrop(data,out file))return Task.FromResult(false);int slot=TargetSlot(target,file);return slot<0?Task.FromResult(false):ImportFile(slot,file);}
 internal WorkspaceAsset Selection(int slot){return workspace.Slots[slot];}internal AssetPreparation Preparation(int slot){return prepared[slot];}internal Control Tab(int slot){return tabs[slot];}internal bool SendEnabled{get{return send.Enabled;}}
 protected override void Dispose(bool disposing){if(disposing){if(HasDataOwner)throw new InvalidOperationException("Cierra primero la conexión de datos del amplificador.");disposed=true;recalc.Stop();recalc.Dispose();help.Dispose();}base.Dispose(disposing);}
}
}
