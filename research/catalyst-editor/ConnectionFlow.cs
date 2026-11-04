using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Threading.Tasks;
namespace CFEditor {
sealed partial class EditorForm {
 readonly Dictionary<string,string> renderedAssets=new Dictionary<string,string>();
 bool editorUSBReleased,resumeEditorAfterFirmware;
 static bool IsFirmwarePage(int page){return page==4||page==5;}
 static int PageSurface(int page){return page==5?4:page;}
 async Task ChangePage(int i){
  if(i<0||i>=nav.Count)throw new ArgumentOutOfRangeException("i");
  if(!PageAllowed(i)||busy||initializing||firmware!=null&&firmware.Busy)return;
  bool workspace=IsFirmwarePage(i),wasWorkspace=IsFirmwarePage(currentPage);
  if(workspace&&currentPage==i&&firmware.Active&&!backend.Connected)return;
  bool leaving=wasWorkspace&&!workspace;
  if(leaving){bool released=false;await Run(async()=>{await firmware.CloseData();released=firmware.Deactivate();});if(!released)return;}
  ShowPageSurface(i);
  if(workspace)await Run(async()=>{
   bool showLoading=!wasWorkspace||!firmware.Created;
   if(showLoading)ShowLoading("Loading & Syncing",i==5?"Preparando el cambio de NAM e IR…":"Preparando el instalador de firmware…");
   await Task.Yield();
   try{
    if(!preview&&backend.Connected)await HandoverToFirmware();
    await firmware.SelectMode(i==5);if(!firmware.Active)await firmware.Activate(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-data","firmware"));
    ScaleFreshControls(pages[4]);firmware.Relayout();
   }finally{if(showLoading)FinishLoading();}
  });
  else if(leaving&&resumeEditorAfterFirmware&&!preview)await Run(async()=>{
   ShowLoading("Loading & Syncing","Recuperando la conexión del editor…");
   try{editorUSBReleased=false;await backend.Connect();LoadingPhase("Leyendo los ajustes del amplificador…");await RefreshDevice();resumeEditorAfterFirmware=false;}
   finally{FinishLoading();}
  });
  else if(leaving)editorUSBReleased=false;
  if(i==3)RefreshAdvanced();if(i==0)ScheduleSoundLayout();UpdateEnabled();
 }
}
}
