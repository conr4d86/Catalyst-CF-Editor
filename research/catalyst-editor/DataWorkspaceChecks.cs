#if DEVELOPMENT
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Drawing;using System.Threading;using System.Threading.Tasks;using System.Windows.Forms;using System.Web.Script.Serialization;
namespace CFEditor {
sealed partial class AssetDataPanel {
 internal async Task TestSettings(int slot,bool auto,double db,double trim,double lowHz=80,double highHz=8000){SelectSlot(slot);automatic.Checked=auto;output.Value=(decimal)db;input.Value=(decimal)trim;low.Value=(decimal)lowHz;high.Value=(decimal)highHz;recalc.Stop();await Calculate();}
 internal async Task TestDeferredPreparation(){
  SelectSlot(3);prepared[3]=null;revisions[3]++;recalc.Stop();connecting=true;
  try{await Calculate();if(!recalc.Enabled||prepared[3]!=null)throw new Exception("A connection dropped its pending preparation");}finally{connecting=false;}
  var clock=System.Diagnostics.Stopwatch.StartNew();while(prepared[3]==null&&clock.ElapsedMilliseconds<12000)await Task.Delay(10);
  if(prepared[3]==null||prepared[3].Info.InputDb!=workspace.Slots[3].Settings.InputDb)throw new Exception("Pending preparation did not resume without editing the controls");
 }
 internal string TestMessage{get{return receiverState.Text;}}
 internal async Task TestReceiptRecovery(){
  SetPreview(false);var link=new ReceiptLossFixture();await UseReceiver(link,2424833,CancellationToken.None);SelectSlot(4);if(!SendEnabled)throw new Exception("Verified fixture did not enable prepared NAM2");
  var expected=prepared[4];await Send();if(link.Uploaded==null||!link.Uploaded.SequenceEqual(expected.Data)||link.UploadedInfo.InputDb!=workspace.Slots[4].Settings.InputDb||link.UploadedInfo.OutputDb!=expected.Info.OutputDb)throw new Exception("UI input/output did not reach actual transfer payload/metadata");if(!client.HasPending(4)||!send.Text.Contains("Comprobar guardado")||link.Commits!=1||link.Begins!=1)throw new Exception("Uncertain send did not change to receipt-only action");
  await Send();if(!client.HasPending(4)||link.Commits!=1||link.Begins!=1)throw new Exception("Missing receipt caused a duplicate send");
  link.AllowQuery=true;await Send();if(client.HasPending(4)||link.Commits!=1||link.Begins!=1||receipts[4]==null||receipts[4].Info==null||!receiverText.Contains("guardado en el amplificador"))throw new Exception("UI failed to reconcile receipt without resending");
  SetPreview(true);
 }
 internal static async Task TestReopenedReceipt(string folder){
  using(var journal=new AssetJournal(Path.Combine(folder,"reopened-receipt"),2424833,"verified:UI-recovery-fixture"))using(var panel=new AssetDataPanel(false)){
   var info=new AssetInfo{Name="Recovered NAM 2",InputDb=-12,OutputDb=-3};var entries=new AssetPendingTransfer[5];entries[4]=new AssetPendingTransfer(4,123,7772,19,info.Encode(),null);journal.Save(42,entries);
   var link=new ReceiptLossFixture();link.Seed(new AssetReceipt{Slot=4,Generation=1,Transaction=123,Length=7772,Crc=19,Info=info});panel.SetActive(true);
   await panel.UseReceiver(link,2424833,CancellationToken.None,journal);
   if(panel.SelectedSlot!=4||panel.Selection(4)!=null||!panel.SendEnabled||!panel.send.Text.Contains("Comprobar guardado"))throw new Exception("Restored pending receipt requires a local file or lost its action");
   await panel.Send();if(panel.client.HasPending(4)||panel.SendEnabled||journal.Load()[4]!=null||link.Begins!=0||link.Commits!=0)throw new Exception("Restored pending receipt sent data instead of querying");
  }
 }
 internal async Task TestSelectedReceipt(){
  SetPreview(false);SelectSlot(4);var fixture=new SelectedReceiptFixture();await UseReceiver(fixture,2424833,CancellationToken.None);
  if(!Ready||!SendEnabled||!fixture.Slots.SequenceEqual(new byte[]{4}))throw new Exception("NAM2 connection scanned unrelated IR/NAM slots");
  fixture.CorruptStatus=true;await Send();if(fixture.Inner.Begins!=0||SendEnabled||client!=null||fixture.Slots.Count!=2)throw new Exception("Upload used a cached/advisory receipt instead of validating its fresh target receipt");
  SetPreview(true);
 }
 internal async Task TestOwnedReceiver(string folder){
  SetPreview(false);int opened=0;var fixture=new ReceiptLossFixture();var driver=new FakeAssetPortDriver();
  driver.Respond=packet=>{var reply=fixture.Exchange(packet,1500,CancellationToken.None).GetAwaiter().GetResult();if(!driver.Deliver(reply))throw new Exception("No fixture receive buffer");};
  ConfigureConnection(2424833,()=>new AssetDataSession(async product=>{opened++;var binding=AssetPortBinding.Select(product,new[]{new AssetMidiEndpoint(4,true,"Catalyst",@"\\?\ui-in",@"USB\VID_0E41&PID_0001\UI-OWNER-FIXTURE",true),new AssetMidiEndpoint(9,false,"Catalyst",@"\\?\ui-out",@"USB\VID_0E41&PID_0001\UI-OWNER-FIXTURE",true)});return await AssetPortOwner.Open(binding,driver,()=>new FakeAssetLease());},(product,id)=>new AssetJournal(Path.Combine(folder,"owned-ui"),product,id)));
  SelectSlot(1);SelectSlot(4);if(opened!=0||SendEnabled)throw new Exception("Selecting a data slot opens a driver");
  await ConnectionAction();if(opened!=1||!HasDataOwner||!SendEnabled)throw new Exception("Explicit fixture connect did not attach the UI");
  driver.BlockUnprepare=true;bool refused=false;try{await CloseConnection();}catch(IOException){refused=true;}
  if(!refused||!HasDataOwner||SendEnabled||Busy)throw new Exception("Failed close lost ownership, allowed sends or trapped navigation");
  driver.BlockUnprepare=false;await CloseConnection();if(HasDataOwner||SendEnabled||driver.CloseCalls!=2)throw new Exception("Explicit UI close retry did not release its owner");SetPreview(true);
 }
}
 sealed class SelectedReceiptFixture:IAssetExchange {
  public readonly ReceiptLossFixture Inner=new ReceiptLossFixture();public readonly List<byte> Slots=new List<byte>();public bool CorruptStatus;
  public Task<byte[]> Exchange(byte[] packet,int timeout,CancellationToken cancel){var request=AssetWire.Decode(packet);if(request.Opcode==5){if(timeout!=30000||request.Slot!=4)throw new Exception("Connection queried an unrelated bank or used the short metadata deadline");Slots.Add(request.Slot);if(CorruptStatus)return Task.FromResult(AssetWire.Encode(new AssetMessage{Product=request.Product,Session=request.Session,Transaction=request.Transaction,Slot=request.Slot,Number=request.Number,Opcode=69,Data=new byte[8]}));}return Inner.Exchange(packet,timeout,cancel);}
 }
 sealed class ReceiptLossFixture:IAssetExchange {
 uint session;AssetReceipt saved;byte[] info;int length,offset;uint crc;public bool AllowQuery;public int Commits,Begins;public byte[] Uploaded;public AssetInfo UploadedInfo;
 static void Put(byte[] b,int p,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,p,4);}
 public void Seed(AssetReceipt value){saved=value;AllowQuery=true;}
 public Task<byte[]> Exchange(byte[] packet,int timeout,CancellationToken cancel){
  var request=AssetWire.Decode(packet);byte[] data=new byte[8];
  if(request.Opcode==1){session=request.Transaction;data=new byte[20];Put(data,4,AssetWire.DataWrite);Put(data,8,256);Put(data,12,5);Put(data,16,2);}
  else if(request.Opcode==5){
   if(saved!=null&&request.Slot==saved.Slot){if(!AllowQuery)throw new TimeoutException("simulated receipt loss");data=new byte[204];Put(data,4,saved.Generation);Put(data,8,saved.Transaction);Put(data,12,saved.Length);Put(data,16,saved.Crc);Buffer.BlockCopy(saved.Info.Encode(),0,data,20,184);}
   else Put(data,0,7);
  }
  else if(request.Opcode==2){Begins++;length=(int)BitConverter.ToUInt32(request.Data,0);crc=BitConverter.ToUInt32(request.Data,4);info=request.Data.Skip(8).ToArray();UploadedInfo=AssetInfo.Decode(info);Uploaded=new byte[length];offset=0;}
  else if(request.Opcode==3){if(request.Number!=offset)throw new Exception("fixture chunk order");Buffer.BlockCopy(request.Data,0,Uploaded,offset,request.Data.Length);offset+=request.Data.Length;Put(data,4,(uint)offset);}
  else if(request.Opcode==4){if(offset!=length)throw new Exception("fixture incomplete payload");Commits++;saved=new AssetReceipt{Slot=request.Slot,Generation=1,Transaction=request.Transaction,Length=(uint)length,Crc=crc,Info=AssetInfo.Decode(info)};throw new TimeoutException("simulated lost commit receipt");}
  else throw new Exception("Unexpected fixture command");
  return Task.FromResult(AssetWire.Encode(new AssetMessage{Product=request.Product,Opcode=(byte)(request.Opcode|64),Session=session,Transaction=request.Transaction,Number=request.Number,Slot=request.Slot,Data=data}));
 }
}
sealed partial class EditorForm {
 internal async Task<object> CheckDataWorkflow(string model,string folder){
  while(initializing)await Task.Delay(1);await ChangePage(5);var view=firmware.DataView;var previewBackend=(PreviewBackend)backend;int writesBefore=previewBackend.Writes.Count;
  string file=Path.Combine(folder,"stereo-long-44100.wav");using(var stream=new FileStream(file,FileMode.CreateNew))using(var writer=new BinaryWriter(stream)){
   const int frames=4410;writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+frames*4);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((ushort)1);writer.Write((ushort)2);writer.Write(44100);writer.Write(44100*4);writer.Write((ushort)4);writer.Write((ushort)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(frames*4);
   for(int i=0;i<frames;i++){short value=(short)(i==0?10000:Math.Sin(i*.15)*3000*Math.Exp(-i/900.0));writer.Write(value);writer.Write((short)-value);}
  }
  string originalHash=FirmwareCodec.Hash(File.ReadAllBytes(file));
  TestNeed(await view.ImportDrop(view.Tab(1),new DataObject(DataFormats.FileDrop,new[]{file})),"IR2 drag/drop failed");
  TestNeed(view.SelectedSlot==1&&view.Selection(0)==null&&view.Selection(2)==null&&view.Preparation(1).Data.Length==25088,"Partial IR selection changed other slots");
  TestNeed(!view.SendEnabled&&view.Preparation(1).Info.InputDb==0&&view.Preparation(1).Info.Filters,"Unverified send or invalid IR metadata");
  var initialIR=view.Selection(1);TestNeed(await view.ImportFile(1,file)&&!ReferenceEquals(initialIR,view.Selection(1)),"Same file cannot be loaded again");
  var retainedIR=view.Selection(1);
  TestNeed(await view.ImportDrop(view.Tab(4),new DataObject(DataFormats.FileDrop,new[]{model})),"NAM2 drag/drop failed");
  TestNeed(view.Selection(3)==null&&view.Preparation(4).Data.Length==7772&&view.Preparation(4).Info.InputDb==-5&&!view.Preparation(4).Info.Filters,"NAM2 geometry, default trim or slot isolation");
  TestNeed(ReferenceEquals(retainedIR,view.Selection(1)),"Loading NAM overwrote the IR");
  TestNeed(await view.ImportFile(3,model)&&!ReferenceEquals(view.Selection(3).Settings,view.Selection(4).Settings),"NAM settings shared across slots");
  TestNeed(view.Preparation(3).Info.InputDb==-5&&view.Preparation(4).Info.InputDb==-5,"Fresh NAM slots do not start at -5 dB");
  await view.TestSettings(4,false,-9,-18);TestNeed(view.Preparation(4)!=null&&view.Preparation(4).Info.OutputDb==-9&&view.Preparation(4).Info.InputDb==-18,"NAM2 manual controls ignored");
  await view.TestSettings(3,false,-6,-12);TestNeed(view.Preparation(3).Info.OutputDb==-6&&view.Preparation(4).Info.OutputDb==-9,"NAM level settings leaked across slots");
  await view.TestSettings(1,false,-12,-12,2000,1000);TestNeed(view.Preparation(1)==null&&!view.SendEnabled,"Invalid cut range can be sent");
  await view.TestSettings(1,false,-12,-12,80,8000);TestNeed(view.Preparation(1)!=null&&view.Preparation(1).Info.OutputDb==-12&&view.Preparation(1).Info.LowHz==80&&view.Preparation(1).Info.HighHz==8000,"IR controls did not recover after invalid range");
  TestNeed(view.Preparation(3).Info.OutputDb==-6&&view.Preparation(4).Info.InputDb==-18,"IR controls changed NAM settings");
  string invalid=Path.Combine(folder,"invalid.nam");File.WriteAllText(invalid,"{}");var kept=view.Selection(4);var keptPayload=view.Preparation(4);
  TestNeed(!await view.ImportFile(4,invalid)&&ReferenceEquals(view.Selection(4),kept)&&ReferenceEquals(view.Preparation(4),keptPayload),"Invalid NAM replaced the valid selection");
  TestNeed(!await view.ImportDrop(view.Tab(0),new DataObject(DataFormats.FileDrop,new[]{model})),"NAM accepted into an explicit IR target");
  TestNeed(!await view.ImportDrop(view.Tab(4),new DataObject(DataFormats.FileDrop,new[]{file})),"IR accepted into an explicit NAM target");
  TestNeed(!await view.ImportDrop(view.Tab(0),new DataObject(DataFormats.FileDrop,new[]{file,model}))&&!await view.ImportDrop(view.Tab(0),new DataObject(DataFormats.Text,"https://example.com/model.nam")),"Unsupported multi-file or URL drop accepted");
  foreach(double zoom in new[]{1.0,.8,.6}){
   ApplyZoom(zoom);firmware.Relayout();Application.DoEvents();
   for(int i=0;i<5;i++){((IButtonControl)view.Tab(i)).PerformClick();TestNeed(view.SelectedSlot==i,"Five-slot tab click failed");foreach(Control c in AllChildren(view).Where(c=>c.Visible))TestNeed(c.Left>=0&&c.Top>=0&&c.Right<=c.Parent.ClientSize.Width&&c.Bottom<=c.Parent.ClientSize.Height,"Data workspace clipped at "+zoom+": "+c.Text);}
   TestNeed(!((Panel)pages[4]).HorizontalScroll.Visible&&!((Panel)pages[4]).VerticalScroll.Visible,"Data workspace requires scrolling");
  }
  ApplyZoom(1);firmware.Relayout();await ChangePage(4);TestNeed(InitialCFResources.UnifiedWorkspace?view.Visible&&view.InitialMode&&firmware.Child==null:firmware.Child.Visible&&!view.Visible,"Initial installer mode not restored");
  await ChangePage(5);TestNeed(view.Selection(1)!=null&&view.Selection(3)!=null&&view.Selection(4)!=null,"Mode switch discarded data selections");
  TestNeed(FirmwareCodec.Hash(File.ReadAllBytes(file))==originalHash,"Converter modified the source WAV");
  TestNeed(previewBackend.Writes.Count==writesBefore&&!view.SendEnabled,"Local data workflow wrote to the backend");
  await view.TestDeferredPreparation();
  await view.TestSelectedReceipt();
  await view.TestReceiptRecovery();TestNeed(!view.SendEnabled,"Preview remained send-enabled after simulated receiver test");
  await view.TestOwnedReceiver(folder);TestNeed(!view.HasDataOwner&&!view.SendEnabled,"Native-data fixture retained an owner");
  await AssetDataPanel.TestReopenedReceipt(folder);
  bool privateWavRegression=false;string regression=Environment.GetEnvironmentVariable("CATALYST_IR_REGRESSION_FIXTURE");
  if(File.Exists(regression)){string before=FirmwareCodec.Hash(File.ReadAllBytes(regression));var keptNAM1=view.Selection(3);var keptNAM2=view.Selection(4);TestNeed(await view.ImportFile(0,regression),"Private unpadded WAV failed normal file loading");TestNeed(await view.ImportDrop(view.Tab(2),new DataObject(DataFormats.FileDrop,new[]{regression})),"Private unpadded WAV failed drag/drop");TestNeed(view.Preparation(0).Data.SequenceEqual(view.Preparation(2).Data)&&view.Preparation(0).Info.Filters&&view.Preparation(0).Info.LowHz==80&&view.Preparation(0).Info.HighHz==8000,"Load/drop conversion or default cuts differ");TestNeed(FirmwareCodec.Hash(File.ReadAllBytes(regression))==before&&ReferenceEquals(keptNAM1,view.Selection(3))&&ReferenceEquals(keptNAM2,view.Selection(4))&&!view.SendEnabled,"IR regression changed the source, NAM or permitted preview send");privateWavRegression=true;}
  TestNeed(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="EngineCLIWrapper"),"Data workflow loaded USB SDK");await ChangePage(0);
  return new{passed=true,hardware_accessed=false,SDK_loaded=false,USB_opened=false,data_slots=5,private_unpadded_wav_load_and_drop=privateWavRegression,partial_IR_selection=true,IR_conversion="stereo antiphase 44.1kHz 4410 frames to 48kHz mono 2048 taps",source_unchanged=true,NAM1_NAM2_independent=true,trim_and_output_separate=true,per_slot_manual_levels=true,invalid_cuts_block_send=true,invalid_import_preserves_selection=true,duplicate_import_allowed=true,drag_targets_type_checked=true,mode_switch_preserves_selections=true,unverified_send_disabled=true,simulated_lost_receipt_action_reconciles_without_resend=true,restored_pending_receipt_without_local_file=true,zoom_layouts=new[]{1.0,.8,.6}};
 }
}
static class DataWorkspaceChecks {
 public static int Run(string model,string folder){
  folder=Path.GetFullPath(folder);Directory.CreateDirectory(folder);string output=Path.Combine(folder,"result.json");int result=1;
  using(var form=new EditorForm(true,false,false,false)){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-12000,-12000);form.ShowInTaskbar=false;form.Shown+=async delegate{try{File.WriteAllText(output,new JavaScriptSerializer().Serialize(await form.CheckDataWorkflow(model,folder)));result=0;}catch(Exception error){File.WriteAllText(output,new JavaScriptSerializer().Serialize(new{passed=false,hardware_accessed=false,error=error.ToString()}));}finally{form.Close();}};Application.Run(form);}return result;
 }
}
}
#endif
