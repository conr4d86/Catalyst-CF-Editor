using System;using System.Drawing;using System.Diagnostics;using System.Runtime.InteropServices;using System.Threading.Tasks;using System.Windows.Forms;
namespace CFEditor {
static class DevicePresence {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct MidiCaps{public ushort manufacturer,product;public uint version;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string name;public uint support;}
 [DllImport("winmm.dll")]static extern uint midiInGetNumDevs();
 [DllImport("winmm.dll",CharSet=CharSet.Unicode)]static extern uint midiInGetDevCapsW(UIntPtr device,out MidiCaps caps,uint size);
 // Capability enumeration only: never opens MIDI or loads the Line 6 SDK.
 public static int Count(){int found=0;for(uint i=0;i<midiInGetNumDevs();i++){MidiCaps caps;if(midiInGetDevCapsW(new UIntPtr(i),out caps,(uint)Marshal.SizeOf(typeof(MidiCaps)))==0&&caps.name!=null&&caps.name.IndexOf("Catalyst",StringComparison.OrdinalIgnoreCase)>=0)found++;}return found;}
}
sealed class LoadingSurface:Panel {
 public LoadingSurface(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=Color.FromArgb(15,16,19);}
 protected override void OnPaintBackground(PaintEventArgs e){BlackTheme.Background(e.Graphics,ClientRectangle);}
}
sealed partial class EditorForm {
 LoadingSurface loading;Label loadingTitle,loadingDetail,loadingElapsed;ActionButton openPreview,retryUsb;GlassProgress loadingProgress;int detectedUsb;bool initializing,offerPreview,presenceReading;
 readonly Stopwatch loadingClock=new Stopwatch();readonly System.Windows.Forms.Timer loadingTicker=new System.Windows.Forms.Timer{Interval=250},presenceTimer=new System.Windows.Forms.Timer{Interval=1200};
 void OnBackendStatus(string status){if(!UiAlive)return;Log(status);if(loading!=null&&loading.Visible&&loadingClock.IsRunning)LoadingPhase(status);RefreshConnectionLabel();}
 void LoadingPhase(string detail){if(UiAlive&&loadingDetail!=null)loadingDetail.Text=detail;}
 void RefreshConnectionLabel(){if(!UiAlive)return;bool found=detectedUsb==1||!backend.Preview&&backend.Connected;object id;string firmwareId=!preview&&customFirmwareConfirmed&&values.TryGetValue("CFAssets/FirmwareID",out id)?Convert.ToString(id):null;connection.Text=found?"Amplificador conectado"+(string.IsNullOrEmpty(firmwareId)?"":" · "+firmwareId):"Amplificador NO detectado";connection.ForeColor=found?UITheme.Success:UITheme.Error;}
 void BuildLoading(){
  loading=new LoadingSurface{Dock=DockStyle.Fill};var center=new GlassPanel{Size=new Size(620,340),BackColor=Color.Transparent};loading.Controls.Add(center);loading.Resize+=delegate{center.Location=new Point(Math.Max(0,(loading.Width-center.Width)/2),Math.Max(0,(loading.Height-center.Height)/2));};
  center.Controls.Add(new AmpPicture{Bounds=new Rectangle(275,20,70,70)});
  var title=new Label{Text="Catalyst CF Editor",Font=new Font("Segoe UI",23,FontStyle.Bold),ForeColor=UITheme.Text,BackColor=Color.Transparent,TextAlign=ContentAlignment.MiddleCenter,Bounds=new Rectangle(10,98,600,48)};center.Controls.Add(title);
  loadingTitle=new Label{Text="Loading & Syncing",Font=new Font("Segoe UI",14,FontStyle.Bold),ForeColor=UITheme.Success,BackColor=Color.Transparent,TextAlign=ContentAlignment.MiddleCenter,Bounds=new Rectangle(10,153,600,32)};center.Controls.Add(loadingTitle);
  loadingDetail=new Label{Text="Preparando la interfaz…",Font=UITheme.Body,ForeColor=UITheme.Text,BackColor=Color.Transparent,TextAlign=ContentAlignment.MiddleCenter,Bounds=new Rectangle(30,194,560,44)};center.Controls.Add(loadingDetail);
  loadingProgress=new GlassProgress{Bounds=new Rectangle(90,252,440,6),Style=ProgressBarStyle.Marquee};center.Controls.Add(loadingProgress);
  loadingElapsed=new Label{Font=UITheme.Small,ForeColor=UITheme.Muted,BackColor=Color.Transparent,TextAlign=ContentAlignment.MiddleCenter,Bounds=new Rectangle(30,272,560,24)};center.Controls.Add(loadingElapsed);
  retryUsb=Button("Reintentar USB",async()=>await InitializeView(true),170);retryUsb.SetBounds(112,277,170,40);retryUsb.Visible=false;center.Controls.Add(retryUsb);
  openPreview=Button(preview?"Abrir vista previa":"Ver Firmware CF",async()=>{if(preview||offerPreview){offerPreview=false;preview=true;await backend.Disconnect();backend.Changed-=OnChanged;backend.Status-=OnBackendStatus;backend=new PreviewBackend();backend.Changed+=OnChanged;backend.Status+=OnBackendStatus;firmware.SwitchPreview(true);await InitializeView(false);}else{await ChangePage(4);FinishLoading();}},195);openPreview.SetBounds(294,277,195,40);openPreview.Visible=false;center.Controls.Add(openPreview);
  loadingTicker.Tick+=delegate{if(UiAlive)loadingElapsed.Text=loadingClock.Elapsed.TotalSeconds<1?"":"Tiempo transcurrido: "+(int)loadingClock.Elapsed.TotalSeconds+" s";};
  presenceTimer.Tick+=async delegate{if(presenceReading||!UiAlive)return;presenceReading=true;try{int count=await Task.Run(()=>DevicePresence.Count());if(UiAlive){detectedUsb=count;if(count!=1&&!preview)RestrictUntilVerified();RefreshConnectionLabel();}}catch(Exception error){Log("Detección USB: "+error.Message);}finally{presenceReading=false;}};
  Disposed+=delegate{loadingTicker.Dispose();presenceTimer.Dispose();};
  Controls.Add(loading);loading.BringToFront();
 }
 void ShowLoading(string title,string detail){if(!UiAlive||loading==null)return;loadingTitle.Text=title;loadingTitle.ForeColor=UITheme.Success;loadingDetail.Text=detail;loadingElapsed.Text="";loadingElapsed.Visible=true;loadingClock.Restart();loadingTicker.Start();openPreview.Visible=retryUsb.Visible=false;loadingProgress.Visible=true;loadingProgress.Style=ProgressBarStyle.Marquee;loading.Visible=true;loading.BringToFront();loading.Refresh();}
 void PauseLoading(){if(!UiAlive)return;loadingClock.Stop();loadingTicker.Stop();loadingElapsed.Visible=false;loadingProgress.Style=ProgressBarStyle.Continuous;loadingProgress.Visible=false;retryUsb.Visible=openPreview.Visible=true;}
 void FinishLoading(){if(!UiAlive||loading==null)return;ArrangeSound();Log("Interfaz lista · "+loadingClock.Elapsed.TotalSeconds.ToString("0.00")+" s");loadingClock.Stop();loadingTicker.Stop();loadingProgress.Style=ProgressBarStyle.Continuous;loading.Visible=false;RefreshConnectionLabel();}
 async Task InitializeView(bool detectUsb){if(!UiAlive||initializing)return;bool synchronized=false;initializing=true;ShowLoading("Loading & Syncing","Preparando la interfaz…");await Task.Yield();if(!UiAlive){initializing=false;return;}var dialPreparation=Task.Run(()=>DialFrames.At(0,true));try{
   if(detectUsb){detectedUsb=await Task.Run(()=>DevicePresence.Count());if(!UiAlive)return;presenceTimer.Start();}
   RefreshConnectionLabel();UpdateNamCaption();
   if((!preview||offerPreview)&&detectedUsb!=1){loadingTitle.Text=detectedUsb>1?"Varios amplificadores detectados":"Amplificador NO detectado";loadingTitle.ForeColor=UITheme.Error;loadingDetail.Text=detectedUsb>1?"Conecta un solo Catalyst para sincronizar sus ajustes.":"Conecta un Catalyst por USB o abre la vista previa para explorar el editor.";PauseLoading();return;}
   loadingDetail.Text=preview?"Preparando la vista previa…":"Sincronizando los ajustes del amplificador…";
   if(!backend.Connected)await backend.Connect();if(!UiAlive)return;editorUSBReleased=resumeEditorAfterFirmware=false;LoadingPhase("Leyendo los ajustes del amplificador…");await RefreshDevice();if(!UiAlive)return;LoadingPhase("Preparando los controles…");await dialPreparation;if(!UiAlive)return;ArrangeSound();synchronized=true;
  }catch(Exception error){if(UiAlive){Log(error.ToString());loadingTitle.Text="No se pudo sincronizar";loadingTitle.ForeColor=UITheme.Error;loadingDetail.Text=error.Message;PauseLoading();}}finally{initializing=false;UpdateEnabled();}if(synchronized&&UiAlive){if(!preview)await ChangePage(EditorAccess?0:4);FinishLoading();UpdateEnabled();}}
}
}
