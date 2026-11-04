using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Web.Script.Serialization;using System.Drawing;using System.Windows.Forms;
namespace CFEditor {
static class NamCaption {
 public static long Revision(){try{return File.GetLastWriteTimeUtc(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CatalystIR","v3-nam","installation.json")).Ticks;}catch{return 0;}}
 public static string LastInstalled(int product){try{
  string journal=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CatalystIR","v3-nam","installation.json");if(!File.Exists(journal)||new FileInfo(journal).Length>100000)return null;
  var json=new JavaScriptSerializer();var d=(Dictionary<string,object>)json.DeserializeObject(File.ReadAllText(journal));if(Convert.ToString(d["phase"])!="active"||!d.ContainsKey("hasNAM")||!Convert.ToBoolean(d["hasNAM"]))return null;
  string backup=Convert.ToString(d["backup"]);if(!File.Exists(backup))return null;var b=(Dictionary<string,object>)json.DeserializeObject(File.ReadAllText(backup));var meta=(Dictionary<string,object>)b["meta"];if(Convert.ToInt32(meta["device"])!=product)return null;
  string candidate=Convert.ToString(d["candidate"]);if(!File.Exists(candidate)||FirmwareCodec.Hash(File.ReadAllBytes(candidate))!=Convert.ToString(d["candidateHash"]))return null;
  string file=Path.Combine(Convert.ToString(d["folder"]),"NAM-settings.json");if(!File.Exists(file)||new FileInfo(file).Length>100000)return null;var settings=(Dictionary<string,object>)json.DeserializeObject(File.ReadAllText(file));string name=Path.GetFileNameWithoutExtension(Convert.ToString(settings["name"]));return name.Length>0&&name.Length<240?name:null;
 }catch{return null;}}
}
sealed partial class EditorForm {
 readonly Label namCaption=new Label{AutoSize=false,Height=28,AutoEllipsis=true,UseMnemonic=false,Font=UITheme.Bold,ForeColor=EditorColors.Amp,BackColor=Color.Transparent,TextAlign=ContentAlignment.MiddleLeft};readonly ToolTip namHelp=new ToolTip();string lastInstalledName;int captionProduct=-1;long captionRevision=-1;
 void UpdateNamCaption(){
  object asset;string id=values.TryGetValue("CatalystGroupAmp/graphSlot_AssetEngineID",out asset)?Convert.ToString(asset):"";
  namCaption.Visible=id=="Chime"||id=="Chime 2";if(!namCaption.Visible)return;
  if(backend.Preview){namCaption.Text=id=="Chime"?"NAM 1 · captura de demostración":"NAM 2 · captura de demostración";namHelp.SetToolTip(namCaption,"Vista de diseño: nombre simulado, sin consultar archivos personales.");return;}
  string slot=id=="Chime"?"NAM1":"NAM2";object known,current;
  if(values.TryGetValue("CFAssets/"+slot+"Known",out known)&&Convert.ToBoolean(known)){string name=values.TryGetValue("CFAssets/"+slot,out current)?Convert.ToString(current):"";namCaption.Text=string.IsNullOrWhiteSpace(name)?"Sin captura en "+(id=="Chime"?"NAM 1":"NAM 2"):name;namHelp.SetToolTip(namCaption,string.IsNullOrWhiteSpace(name)?"Se utiliza el modelo de fábrica en esta posición.":"Nombre del recurso guardado, leído del amplificador.");return;}
  object receiverKnown;if(values.TryGetValue("CFAssets/ReceiverKnown",out receiverKnown)&&Convert.ToBoolean(receiverKnown)){namCaption.Text="Nombre de la captura sin confirmar";namHelp.SetToolTip(namCaption,"Vuelve a leer los ajustes cuando termine la operación del amplificador.");return;}
  if(id=="Chime 2"){string pending=firmware!=null&&firmware.DataView!=null?firmware.DataView.PreparedNAM(4):null;namCaption.Text=pending==null?"Sin NAM 2 confirmado":pending+" · pendiente de instalar";namHelp.SetToolTip(namCaption,pending==null?"Carga la segunda captura en Firmware CF.":"Captura preparada localmente, aún sin confirmación del amplificador.");return;}
  long stamp=NamCaption.Revision();if(captionProduct!=backend.ProductID||captionRevision!=stamp){captionProduct=backend.ProductID;captionRevision=stamp;lastInstalledName=NamCaption.LastInstalled(captionProduct);}
  string selected=firmware!=null?firmware.SelectedNAM:null;namCaption.Text=lastInstalledName??(selected!=null?selected+" · pendiente de instalar":"Sin NAM instalado confirmado");
  namHelp.SetToolTip(namCaption,lastInstalledName!=null?"Último NAM 1 instalado: "+lastInstalledName+". Nombre del registro local; no identifica el contenido leyendo el ampli.":selected!=null?"NAM preparado en Firmware CF, todavía sin instalación confirmada: "+selected:"Carga tu NAM en la pestaña Firmware CF");
 }
}
}
