using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Reflection;using System.Drawing;using System.Threading.Tasks;using System.Windows.Forms;using System.Web.Script.Serialization;
namespace CFEditor {
static class StoreChecks {
 static void Need(bool value,string error){if(!value)throw new InvalidOperationException(error);}
 public static void Run(){
  for(int bank=0;bank<6;bank++)for(int channel=1;channel<=2;channel++){
   var source=new Dictionary<string,int>{{"currentAmpBank",bank},{"currentAmpChannel",channel},{"currentDevicePresetIndex",bank*2+channel}};
   var context=PresetSaveContext.Read((g,p)=>source[p]);Need(context.Bank==bank&&context.Channel==channel&&context.Index==bank*2+channel,"Fresh save context has wrong target");
  }
  // Reproduce the user's 6B/index=0 case and all other bank/channel pairs.
  for(int bank=0;bank<6;bank++)for(int channel=1;channel<=2;channel++){
   var source=new Dictionary<string,int>{{"currentAmpBank",bank},{"currentAmpChannel",channel}};
   var fresh=PresetSaveContext.Read((g,p)=>{if(p=="currentDevicePresetIndex")throw new Exception("Save depends on the stale generic index");return source[p];});
   Need(fresh.Bank==bank&&fresh.Channel==channel&&fresh.Index==bank*2+channel,"Wrong bank/channel save destination");
  }
  foreach(string mode in new[]{"manual","channel-outside","bank-outside","changing"}){
   var source=new Dictionary<string,int>{{"currentAmpBank",5},{"currentAmpChannel",2},{"currentDevicePresetIndex",12}};if(mode=="manual")source["currentAmpChannel"]=0;if(mode=="channel-outside")source["currentAmpChannel"]=3;if(mode=="bank-outside")source["currentAmpBank"]=6;int indexReads=0;bool rejected=false;
   try{PresetSaveContext.Read((g,p)=>mode=="changing"&&p=="currentAmpBank"&&++indexReads==2?4:source[p]);}catch(IOException){rejected=true;}Need(rejected,"Invalid fresh save context accepted: "+mode);
  }
  for(int bank=0;bank<6;bank++)for(int channel=1;channel<=2;channel++){
   var state=new Dictionary<string,int>{{"systemState/currentAmpBank",bank},{"systemState/currentAmpChannel",channel},{"systemState/currentDevicePresetIndex",0}};var calls=new List<string>();int receipt=0,commits=0,pumps=0;
   PresetStore.Commit(bank*2+channel,(g,p)=>state[g+"/"+p],(g,p,v)=>{Need(g=="devicePresetSaveTarget","Uses generic device command for save");calls.Add(p);state[g+"/"+p]=v;if(p=="saveDevicePresetToCurrentTarget"){Need(v==1,"Wrong save trigger");commits++;}return 0;},()=>receipt,()=>receipt=0,()=>{if(++pumps==2)receipt=1;});
   Need(commits==1&&state["devicePresetSaveTarget/targetDevicePresetIndex"]==channel-1&&state["devicePresetSaveTarget/targetDevicePresetBank"]==bank,"Wrong bank/channel destination");Need(calls.SequenceEqual(new[]{"targetDevicePresetBank","targetDevicePresetIndex","saveDevicePresetToCurrentTarget"}),"Wrong save sequence");
  }
  foreach(string mode in new[]{"wrong-target","preset-changed","failed-receipt","missing-receipt","rejected-target"}){
   int commits=0,receipt=0;var state=new Dictionary<string,int>{{"systemState/currentAmpBank",5},{"systemState/currentAmpChannel",2},{"systemState/currentDevicePresetIndex",12}};bool rejected=false;
   try{PresetStore.Commit(12,(g,p)=>mode=="wrong-target"&&p=="targetDevicePresetIndex"?0:state[g+"/"+p],(g,p,v)=>{if(mode=="rejected-target")throw new IOException("simulated rejection");state[g+"/"+p]=v;if(mode=="preset-changed"&&p=="targetDevicePresetIndex")state["systemState/currentAmpBank"]=0;if(p=="saveDevicePresetToCurrentTarget")commits++;return 0;},()=>receipt,()=>{},()=>{if(mode=="failed-receipt")receipt=-1;else System.Threading.Thread.Sleep(2);},1);}catch(IOException){rejected=true;}catch(TimeoutException){rejected=true;}
   Need(rejected,"Invalid save reported success: "+mode);Need(commits==(mode=="failed-receipt"||mode=="missing-receipt"?1:0),"Unsafe save/retry: "+mode);
  }
  // Reproduce the native 2.01 selector fall-through: state changes, rc=4.
  foreach(string mode in new[]{"verified-fallthrough","selector-not-applied","unknown-sdk","commit-rejected","different-error"}){
   var state=new Dictionary<string,int>{{"systemState/currentAmpBank",5},{"systemState/currentAmpChannel",2},{"systemState/currentDevicePresetIndex",12},{"devicePresetSaveTarget/targetDevicePresetBank",0},{"devicePresetSaveTarget/targetDevicePresetIndex",0}};int commits=0,receipt=0;bool rejected=false;
   try{PresetStore.Commit(12,(g,p)=>state[g+"/"+p],(g,p,v)=>{if(p=="saveDevicePresetToCurrentTarget"){commits++;receipt=1;return mode=="commit-rejected"?4:0;}if(mode!="selector-not-applied")state[g+"/"+p]=v;return mode=="different-error"?3:4;},()=>receipt,()=>receipt=0,()=>{},1,mode!="unknown-sdk");}catch(IOException){rejected=true;}
   Need(rejected==(mode!="verified-fallthrough"),"Selector quirk result incorrect: "+mode);Need(commits==(mode=="verified-fallthrough"||mode=="commit-rejected"?1:0),"Selector quirk allowed unsafe commit: "+mode);
  }
 }
}
sealed partial class FirmwareTab {
 internal async Task CheckDrops(string model,string wav,string folder){
  if(InitialCFResources.UnifiedWorkspace){await CheckUnifiedDrops(model,wav,folder);return;}
  var drop=new DataObject(DataFormats.FileDrop,new[]{model});var name=Get<Label>("namName");var enter=new DragEventArgs(drop,0,0,0,DragDropEffects.Copy,DragDropEffects.None);typeof(Control).GetMethod("OnDragEnter",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(name,new object[]{enter});if(enter.Effect!=DragDropEffects.Copy)throw new Exception("NAM drag enter rejected");
  object sessionBefore=type.GetField("session",Private).GetValue(child);if(!await ImportDrop(name,drop)||SelectedNAM!=Path.GetFileNameWithoutExtension(model)||!Get<bool>("namSelected"))throw new Exception("NAM drop did not load/select NAM");
  var tabs=Get<Array>("slotButtons").Cast<Control>().ToArray();var wavDrop=new DataObject(DataFormats.FileDrop,new[]{wav});
  for(int i=0;i<3;i++){if(!await ImportDrop(tabs[i],wavDrop)||Get<int>("selectedSlot")!=i||Get<bool>("namSelected"))throw new Exception("WAV did not go to target IR tab");}
  Call("SelectSlot",1);if(!await ImportDrop(Get<Label>("irName"),wavDrop)||Get<int>("selectedSlot")!=1)throw new Exception("WAV ignored selected IR2");
  if(AcceptDrop(new DataObject(DataFormats.FileDrop,new[]{wav,model}))||AcceptDrop(new DataObject(DataFormats.FileDrop,new[]{"untrusted.exe"}))||AcceptDrop(new DataObject(DataFormats.Text,"https://example.com/model.nam")))throw new Exception("Unsupported drag accepted");
  type.GetField("busy",Private).SetValue(child,true);try{if(AcceptDrop(drop))throw new Exception("Drop allowed during operation");}finally{type.GetField("busy",Private).SetValue(child,false);}
  string invalid=Path.Combine(folder,"invalid.nam");File.WriteAllText(invalid,"{}");object previous=type.GetField("nam",Private).GetValue(child);if(await ImportDrop(name,new DataObject(DataFormats.FileDrop,new[]{invalid}))||!ReferenceEquals(previous,type.GetField("nam",Private).GetValue(child)))throw new Exception("Invalid NAM replaced selected NAM");
  if(!ReferenceEquals(sessionBefore,type.GetField("session",Private).GetValue(child))||Get<Control>("run").Enabled)throw new Exception("Dropping starts or enables a preview installation");
  foreach(var target in new[]{child,Get<Panel>("workspace"),Get<Panel>("namPanel"),name,Get<Control>("namChoose"),Get<Label>("irName")})if(!target.AllowDrop)throw new Exception("Missing drop target "+target.Name);
  if(AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="EngineCLIWrapper"))throw new Exception("Drop check loaded USB SDK");
 }
 async Task CheckUnifiedDrops(string model,string wav,string folder){
  await SelectMode(true);if(dataView==null||dataView.InitialMode||dataView.HasDataOwner)throw new Exception("Data workspace not isolated");
  var drop=new DataObject(DataFormats.FileDrop,new[]{model});if(!await dataView.ImportDrop(dataView.Tab(3),drop)||SelectedNAM!=Path.GetFileNameWithoutExtension(model)||dataView.SelectedSlot!=3)throw new Exception("Unified NAM drop failed");
  var wavDrop=new DataObject(DataFormats.FileDrop,new[]{wav});for(int i=0;i<3;i++)if(!await dataView.ImportDrop(dataView.Tab(i),wavDrop)||dataView.SelectedSlot!=i)throw new Exception("Unified IR target ignored");
  var previous=dataView.Selection(3);string invalid=Path.Combine(folder,"unified-invalid.nam");File.WriteAllText(invalid,"{}");if(await dataView.ImportFile(3,invalid)||!ReferenceEquals(previous,dataView.Selection(3)))throw new Exception("Invalid NAM replaced selected model");
  if(await dataView.ImportDrop(dataView.Tab(0),drop)||await dataView.ImportDrop(dataView.Tab(4),wavDrop)||await dataView.ImportDrop(dataView.Tab(0),new DataObject(DataFormats.FileDrop,new[]{wav,model}))||await dataView.ImportDrop(dataView.Tab(0),new DataObject(DataFormats.Text,"https://example.com/model.nam")))throw new Exception("Unsupported drop target accepted");
  if(dataView.SendEnabled||dataView.HasDataOwner||AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="EngineCLIWrapper"))throw new Exception("Preparing initial resources opened or enabled hardware");
 }
}
sealed partial class EditorForm {
 internal async Task<object> CheckWorkflow(string model,string wav,string folder){
  while(initializing)await Task.Delay(1);StoreChecks.Run();TestNeed(catalog.Amps.Select(a=>a.id).SequenceEqual(new[]{"Clean","Clean 2","Chime","Chime 2","Boutique","Boutique 2","Crunch","Crunch 2","Dynamic","Dynamic 2","Hi Gain","Hi Gain 2"}),"Wrong amp order");
  reading=true;resyncRequested=false;OnChanged("systemState","presetDidChange","");TestNeed(resyncRequested,"Preset event lost during read");readingModels=true;resyncRequested=false;OnChanged("systemState","currentAmpBank",5);TestNeed(!resyncRequested,"Ordinary discovery read schedules a duplicate refresh");readingModels=reading=false;
  // Decorative headings must never reach the model-selection backend.
  foreach(string group in new[]{"CatalystGroupEffect","CatalystGroupReverb"}){
   var choice=selectors[group];var headingIndices=Enumerable.Range(0,choice.Items.Count).Where(i=>choice.Items[i] is EffectHeading).ToArray();
   TestNeed(headingIndices.Length==6&&choice.Items.OfType<Asset>().Count()==24,"Effect categories incomplete");
   int selected=choice.SelectedIndex,writes=((PreviewBackend)backend).Writes.Count;
   foreach(int i in headingIndices){choice.SelectedIndex=i;TestNeed(choice.SelectedIndex==selected,"Heading became selected");}
   TestNeed(((PreviewBackend)backend).Writes.Count==writes,"Clicking a heading sent a model command");
   refreshing=true;try{choice.SelectedIndex=choice.Items.IndexOf(catalog.Effects[0]);typeof(AssetComboBox).GetMethod("OnKeyDown",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(choice,new object[]{new KeyEventArgs(Keys.Home)});TestNeed(choice.SelectedItem is Asset&&choice.SelectedIndex==1,"Home selected a heading");typeof(AssetComboBox).GetMethod("OnKeyDown",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(choice,new object[]{new KeyEventArgs(Keys.Up)});TestNeed(choice.SelectedItem is Asset,"Up selected a heading");choice.SelectedIndex=selected;}finally{refreshing=false;}
  }
  await ChangePage(5);await firmware.CheckDrops(model,wav,folder);await ChangePage(0);var mock=(PreviewBackend)backend;int writesBefore=mock.Writes.Count;
  foreach(var asset in catalog.Amps){await mock.SelectAsset("CatalystGroupAmp",asset.id);await RefreshDevice();ArrangeSound();TestNeed(AllChildren(strips["amp"]).OfType<Knob>().Count()==9,"Rebuilding amp duplicates/misses dials");TestNeed(strips["amp"].Controls.OfType<PreampPanel>().Count()==1,"Missing approved preamp section");TestNeed(strips["advancedAmp"].Controls.Count==(asset.id=="Clean"?2:4),"Rebuilding advanced amp duplicates/misses controls");}
  TestNeed(mock.Writes.Count==writesBefore+12,"Model refresh writes additional controls");
  return new{passed=true,hardware_accessed=false,drag_nam=true,drag_wav_into_each_slot=true,drop_uses_selected_slot=true,invalid_drop_preserves_model=true,drop_does_not_install=true,save_bank_channel_cases=12,save_requires_completion_receipt=true,save_rejects_mismatch_and_timeout=true,native_selector_rc4_requires_matching_readback=true,unknown_sdk_selector_quirk_rejected=true,save_commit_rc4_rejected=true,preset_events_retained_during_read=true,amp_order_correct=true,effect_categories=6,effect_headers_not_selectable=true,effect_headers_issue_no_commands=true,amp_rebuilds=12,scope="Offline host and SDK transport protocol checks; physical save and audio require user verification"};
 }
}
static class WorkflowChecks {
 public static int Run(string output,string model,string wav){int result=1;string folder=Path.GetDirectoryName(Path.GetFullPath(output));Directory.CreateDirectory(folder);using(var form=new EditorForm(true,false,false,false)){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-12000,-12000);form.ShowInTaskbar=false;form.Shown+=async delegate{try{var report=await form.CheckWorkflow(model,wav,folder);File.WriteAllText(output,new JavaScriptSerializer().Serialize(report));result=0;}catch(Exception error){File.WriteAllText(output,new JavaScriptSerializer().Serialize(new{passed=false,hardware_accessed=false,error=error.ToString()}));}finally{form.Close();}};Application.Run(form);}return result;}
}
}
