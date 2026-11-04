using System;using System.Threading.Tasks;
partial class OneClickApp {
 bool irImporting;public bool ImportingIR{get{return irImporting;}}
 async Task LoadIRAsync(string file){
  if(irImporting||busy)return;irImporting=true;workspace.Enabled=false;SetInfo("Preparando el IR…");
  try{await Task.Run(()=>IRImport.Read(file));LoadIR(file);}
  catch(Exception error){SetInfo(error.Message,false,true);Log(error.ToString());if(preview)throw;}
  finally{irImporting=false;RefreshUI();RefreshEmbeddedPreview();}
 }
}
