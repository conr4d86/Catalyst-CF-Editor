using System;using System.IO;using System.Threading.Tasks;using System.Windows.Forms;using System.Reflection;
[assembly:AssemblyTitle("Catalyst CF Editor")]
[assembly:AssemblyProduct("Catalyst CF Editor")]
[assembly:AssemblyCompany("conr4d")]
[assembly:AssemblyVersion("1.0.0.0")]
[assembly:AssemblyFileVersion("1.0.0.0")]
[assembly:AssemblyInformationalVersion("1.0.0-development")]
namespace CFEditor {
static class Program {
 [STAThread]static int Main(string[] args){
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
  if(args.Length>0&&args[0]=="--self-test")return EditorTests.Run(args.Length>1?args[1]:"self-test.json");
#if DEVELOPMENT
  if(args.Length==2&&args[0]=="--firmware-access-test")return FirmwareAccessChecks.Run(args[1]);
  if(args.Length==2&&args[0]=="--system-ui-test")return SystemUIChecks.Run(args[1]);
  if(args.Length==3&&args[0]=="--initial-install-review")return InitialInstallReview.Run(args[1],args[2]);
  if(args.Length==3&&args[0]=="--firmware-ui-test")return FirmwareUIChecks.Run(args[1],args[2]);
  if(args.Length==6&&args[0]=="--prepare-demo-bundle"){try{return DemoAssets.Prepare(args[1],args[2],args[3],args[4],args[5]);}catch(Exception error){File.WriteAllText(args[5]+".error.txt",error.ToString());return 1;}}
  if(args.Length==2&&args[0]=="--preamp-layout-test")return PreampLayoutChecks.Run(args[1]);
  if(args.Length==2&&args[0]=="--preamp-factory-trial")return StateSnapshotTrial.Run(args[1],true);
  if(args.Length==2&&args[0]=="--state-snapshot-trial")return StateSnapshotTrial.Run(args[1]);
  if(args.Length==4&&args[0]=="--rebase-hardware-trial"){try{LegacyCFMigration.Rebase(args[1],args[2],args[3]);return 0;}catch(Exception e){Directory.CreateDirectory(args[3]);File.WriteAllText(Path.Combine(args[3],"prepare-error.txt"),e.ToString());return 1;}}
  if(args.Length==5&&args[0]=="--reload-ir-data")return DataSendTrial.ReloadIR(byte.Parse(args[1]),args[2],args[3],args[4]);
  if(args.Length==2&&args[0]=="--status-timing-trial")return StatusTimingTrial.Run(args[1]);
  if(args.Length==5&&args[0]=="--install-hardware-trial-with-backup")return PhysicalTrial.Install(args[1],args[2],args[3],args[4]);
  if(args.Length==3&&args[0]=="--prepare-nam2-data")return DataSendTrial.Prepare(args[1],args[2]);
  if(args.Length==4&&args[0]=="--send-nam2-data")return DataSendTrial.Run(args[1],args[2],args[3]);
  if(args.Length==3&&args[0]=="--confirm-nam2-data")return DataSendTrial.Confirm(args[1],args[2]);
  if(args.Length==3&&args[0]=="--confirm-data-reload")return DataReloadTrial.Confirm(args[1],args[2]);
  if(args.Length==4&&args[0]=="--data-reload-trial")return DataReloadTrial.Run(args[1],args[2],args[3]);
  if(args.Length==3&&args[0]=="--native-exercise")return NativeExercise.Run(args[1],args[2]);
  if(args.Length==2&&args[0]=="--trial-backup")return PhysicalTrial.Backup(args[1]);
  if(args.Length==3&&args[0]=="--recover-hardware-trial")return PhysicalTrial.Recover(args[1],args[2]);
  if(args.Length==2&&args[0]=="--receiver-diagnostic")return ReceiverDiagnostic.Run(args[1]);
  if(args.Length==2&&args[0]=="--receiver-soak")return ReceiverDiagnostic.Run(args[1],32);
  if(args.Length==4&&args[0]=="--install-hardware-trial")return PhysicalTrial.Install(args[1],args[2],args[3]);
  if(args.Length==6&&args[0]=="--prepare-hardware-trial"){try{LegacyCFMigration.Prepare(args[1],args[2],args[3],args[4],args[5]);return 0;}catch(Exception e){Directory.CreateDirectory(args[5]);File.WriteAllText(Path.Combine(args[5],"prepare-error.txt"),e.ToString());return 1;}}
  if(args.Length==2&&args[0]=="--connection-check")return NativeConnectionCheck.Run(args[1]);
  if(args.Length==2&&args[0]=="--connection-check-cf")return NativeConnectionCheck.Run(args[1],true);
  if(args.Length==4&&args[0]=="--cf-install-test"){try{CFInstallChecks.Run(args[1],args[2],args[3]);return 0;}catch(Exception error){Directory.CreateDirectory(args[3]);File.WriteAllText(Path.Combine(args[3],"error.txt"),error.ToString());return 1;}}
  if(args.Length==5&&args[0]=="--cf-component-test"){try{var transport=Assembly.LoadFile(Path.GetFullPath(args[1]));string result=(string)transport.GetType("CFCandidateProbe",true).GetMethod("Run").Invoke(null,new object[]{File.ReadAllBytes(args[2]),File.ReadAllBytes(args[3])});File.WriteAllText(args[4],new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{passed=true,USB_opened=false,SDK_loaded=false,device_enumeration=false,result}));return 0;}catch(Exception error){File.WriteAllText(args[4]+".error.txt",error.ToString());return 1;}}
  if(args.Length==5&&args[0]=="--package-test"){try{CFPackageChecks.Run(args[1],args[2],args[3],args[4]);return 0;}catch(Exception error){Directory.CreateDirectory(args[4]);File.WriteAllText(Path.Combine(args[4],"error.txt"),error.ToString());return 1;}}
  if(args.Length==2&&args[0]=="--native-preamp-test"){try{NativePreampChecks.Run(args[1]);return 0;}catch(Exception error){File.WriteAllText(args[1]+".error.txt",error.ToString());return 1;}}
  if(args.Length==2&&args[0]=="--sdk-borrow-test"){try{SdkMidiBorrowChecks.Run(args[1]);return 0;}catch(Exception error){File.WriteAllText(args[1]+".error.txt",error.ToString());return 1;}}
  if(args.Length==2&&args[0]=="--data-session-test")return AssetDataSessionChecks.Run(args[1]);
  if(args.Length==2&&args[0]=="--port-binding-test")return AssetPortBindingChecks.Run(args[1]);
  if(args.Length==2&&args[0]=="--port-owner-test")return AssetPortOwnerChecks.Run(args[1]);
  if(args.Length==2&&args[0]=="--native-shutdown-test")return NativeShutdownChecks.Run(args[1]);
  if(args.Length==2&&args[0]=="--journal-test")return AssetJournalChecks.Run(args[1]);
  if(args.Length==2&&args[0]=="--midi-exchange-test")return MidiExchangeChecks.Run(args[1]);
  if(args.Length==3&&args[0]=="--data-workflow-test")return DataWorkspaceChecks.Run(args[1],args[2]);
  if(args.Length==2&&args[0]=="--interaction-test")return InteractionChecks.Run(args[1]);
  if(args.Length==2&&args[0]=="--opening-test")return OpeningChecks.Run(args[1]);
  if(args.Length==4&&args[0]=="--workflow-test")return WorkflowChecks.Run(args[1],args[2],args[3]);
  if(args.Length==3&&args[0]=="--family-test"){try{var assembly=Assembly.LoadFile(Path.GetFullPath(args[1]));string folder=Path.GetFullPath(args[2]);Directory.CreateDirectory(folder);string report=(string)assembly.GetType("FamilyProbe",true).GetMethod("Run").Invoke(null,new object[]{folder});File.WriteAllText(Path.Combine(folder,"result.json"),report);return 0;}catch(Exception e){string folder=Path.GetFullPath(args[2]);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"error.txt"),e.ToString());return 1;}}
  if(args.Length==5&&args[0]=="--family-parity"){string folder=Path.GetFullPath(args[4]);Directory.CreateDirectory(folder);try{var assembly=Assembly.LoadFile(Path.GetFullPath(args[1]));string report=(string)assembly.GetType("FamilyProbe",true).GetMethod("Parity").Invoke(null,new object[]{Path.GetFullPath(args[2]),Path.GetFullPath(args[3]),folder});File.WriteAllText(Path.Combine(folder,"parity.json"),report);return 0;}catch(Exception e){File.WriteAllText(Path.Combine(folder,"parity-error.txt"),e.ToString());return 1;}}
#endif
  bool offline=Array.IndexOf(args,"--offline-preview")>=0;
  bool preview=offline||(Array.IndexOf(args,"--preview")>=0||Array.IndexOf(args,"--review")>=0)||args.Length>0&&args[0]=="--snapshots";
  try{var form=new EditorForm(preview,!offline&&(args.Length==0||Array.IndexOf(args,"--preview")>=0||Array.IndexOf(args,"--review")>=0),!offline&&Array.IndexOf(args,"--review")>=0);if(offline)form.Text+=" · Vista previa sin USB";if(args.Length==2&&args[0]=="--snapshots"){string folder=Path.GetFullPath(args[1]);Directory.CreateDirectory(folder);form.Shown+=async delegate{await Task.Delay(500);for(int i=0;i<6;i++){form.Snapshot(Path.Combine(folder,"page-"+i+".png"),i);await Task.Delay(50);}form.Close();};}Application.Run(form);return 0;}catch(Exception e){if(args.Length>0){string errorFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"last-error.txt");File.WriteAllText(errorFile,e.ToString());}else MessageBox.Show(AppLanguage.Text(e.Message),"Catalyst CF Editor",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
 }
}
}
