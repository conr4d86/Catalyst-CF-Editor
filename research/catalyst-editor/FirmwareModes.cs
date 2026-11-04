using System;using System.IO;using System.Threading.Tasks;using System.Windows.Forms;
namespace CFEditor {
sealed partial class FirmwareTab {
 AssetDataPanel dataView;bool dataMode,upgradeRequiresBackup;string previewDataRoot,upgradeEdition;uint dataProduct;
 internal void SetDetectedReceiver(bool confirmed){if(dataView!=null)dataView.SetKnownReceiver(confirmed);}
 internal void SetUpgradeContext(bool required,string edition){upgradeRequiresBackup=required;upgradeEdition=CFUpgradeBackup.ValidEdition(edition)?edition:null;}
 internal string VerifiedUpgradeEdition{get{return upgradeRequiresBackup?upgradeEdition:null;}}
 internal void SetDataProduct(uint product){dataProduct=product;if(dataView!=null)ConfigureDataConnection();}
 void ConfigureDataConnection(){dataView.ConfigureConnection(dataProduct,()=>AssetDataSession.Native(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CatalystCFEditor","transfers")));dataView.ConfigureInitial(resources=>Task.Run(()=>InitialCFResources.Builder().Build(resources)),async(resources,log)=>{int product=(int)dataProduct;bool upgrade=upgradeRequiresBackup;string edition=upgradeEdition;if(!DeviceFamily.Supported(product))throw new IOException("Conecta primero el editor para comprobar el modelo del amplificador.");var builder=InitialCFResources.Builder();var package=await Task.Run(()=>builder.Build(resources));var installer=InitialCFResources.Installer(product,builder);string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CatalystCFEditor","initial-installations");return upgrade?await installer.Upgrade(package,product,root,edition,log):await installer.Install(package,product,root,log);},()=>DeviceFamily.Supported((int)dataProduct)&&InitialCFResources.InstallAllowed((int)dataProduct));dataView.RestoreInitialContext(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CatalystCFEditor","initial-installations"),(int)dataProduct);}
 internal Task CloseData(){return dataView==null?Task.FromResult(0):dataView.CloseConnection();}
 public bool DataMode{get{return dataMode;}}internal AssetDataPanel DataView{get{return dataView;}}
 void EnsureModes(){if(dataView!=null)return;dataView=new AssetDataPanel(preview);dataView.Visible=false;dataView.StateChanged+=delegate{PositionModes();Notify();};parent.Controls.Add(dataView);ConfigureDataConnection();PositionModes();}
 internal async Task SelectMode(bool data){EnsureModes();if(Busy||dataMode==data)return;bool resume=active;if(resume){await CloseData();if(!Deactivate())return;}dataMode=data;PositionModes();if(resume)await Activate(previewDataRoot??Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-data","firmware"));else Notify();}
 void PositionModes(){if(dataView==null||dataView.IsDisposed)return;if(child!=null)child.Visible=!InitialCFResources.UnifiedWorkspace&&!dataMode&&active;dataView.Visible=(InitialCFResources.UnifiedWorkspace||dataMode)&&active;dataView.SetBounds(0,0,parent.ClientSize.Width,Math.Max(1,parent.ClientSize.Height));dataView.Relayout();}
 internal void PreviewDataMode(bool data){if(!preview)throw new InvalidOperationException("Solo vista previa");SelectMode(data).GetAwaiter().GetResult();}
 void DisposeModes(){if(dataView!=null){dataView.Dispose();dataView=null;}}
}
}
