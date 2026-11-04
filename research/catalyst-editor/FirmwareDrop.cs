using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Drawing;using System.Threading.Tasks;using System.Windows.Forms;
namespace CFEditor {
sealed partial class FirmwareTab {
 bool importing;Control hoverDrop;readonly HashSet<Control> dropTargets=new HashSet<Control>();
 static bool DropPath(IDataObject data,out string path){path=null;try{if(data==null||!data.GetDataPresent(DataFormats.FileDrop))return false;var files=data.GetData(DataFormats.FileDrop) as string[];if(files==null||files.Length!=1||string.IsNullOrWhiteSpace(files[0]))return false;string extension=Path.GetExtension(files[0]);if(!extension.Equals(".nam",StringComparison.OrdinalIgnoreCase)&&!IRImport.SupportedPath(files[0]))return false;path=files[0];return true;}catch{return false;}}
 bool AcceptDrop(IDataObject data){string file;if(dataMode||!active||child==null||staging||importing||Get<bool>("busy")||!DropPath(data,out file)||!Get<Panel>("workspace").Enabled)return false;return Get<Control>(Path.GetExtension(file).Equals(".nam",StringComparison.OrdinalIgnoreCase)?"namChoose":"choose").Enabled;}
 void HoverDrop(Control target){if(hoverDrop==target)return;if(hoverDrop!=null&&!hoverDrop.IsDisposed)hoverDrop.Invalidate();hoverDrop=target;if(hoverDrop!=null)hoverDrop.Invalidate();}
 void WireDropTargets(Control root){
  if(root==null||root.IsDisposed)return;
  for(Control ancestor=root;ancestor!=null;ancestor=ancestor.Parent)if(ancestor is AssetDataPanel)return;
  if(dropTargets.Add(root)){
   if(child!=null&&(root==Get<Label>("namName")||root==Get<Label>("irName")))root.TextChanged+=delegate{UpdateDropHints();};
   root.AllowDrop=true;root.DragEnter+=DropFeedback;root.DragOver+=DropFeedback;
   root.DragLeave+=delegate{HoverDrop(null);};
   root.DragDrop+=async delegate(object sender,DragEventArgs e){HoverDrop(null);await ImportDrop((Control)sender,e.Data);};
   root.ControlAdded+=delegate(object sender,ControlEventArgs e){WireDropTargets(e.Control);};
   root.Disposed+=delegate{dropTargets.Remove(root);if(hoverDrop==root)hoverDrop=null;};
   root.Paint+=delegate(object sender,PaintEventArgs e){if(hoverDrop!=root||root.Width<8||root.Height<8)return;using(var pen=new Pen(UITheme.Success,Math.Max(1,EditorZoom.Px(2))))e.Graphics.DrawRectangle(pen,2,2,root.Width-5,root.Height-5);};
  }
  foreach(Control control in root.Controls)WireDropTargets(control);
 }
 void DropFeedback(object sender,DragEventArgs e){bool accept=AcceptDrop(e.Data)&&(e.AllowedEffect&DragDropEffects.Copy)!=0;e.Effect=accept?DragDropEffects.Copy:DragDropEffects.None;HoverDrop(accept?(Control)sender:null);}
 int DropSlot(Control target){var tabs=Get<Array>("slotButtons").Cast<Control>().ToArray();for(Control c=target;c!=null;c=c.Parent)for(int i=0;i<tabs.Length;i++)if(c==tabs[i])return i;return Get<bool>("namSelected")?0:Get<int>("selectedSlot");}
 void UpdateDropHints(){if(child==null)return;if(Get<Label>("namName").Text=="Selecciona un archivo NAM")Get<Label>("namName").Text="Arrastra un NAM o pulsa «Cargar NAM»";if(Get<Label>("irName").Text=="Sin WAV seleccionado")Get<Label>("irName").Text="Arrastra un WAV o pulsa «Cargar IR»";}
 async Task<bool> ImportDrop(Control target,IDataObject data){
  if(!AcceptDrop(data))return false;string file;if(!DropPath(data,out file))return false;
  try{
   if(!File.Exists(file))throw new FileNotFoundException("El archivo que has soltado ya no está disponible.");bool isNAM=Path.GetExtension(file).Equals(".nam",StringComparison.OrdinalIgnoreCase);long limit=isNAM?16*1024*1024:IRImport.MaxFileBytes;if(new FileInfo(file).Length>limit)throw new InvalidDataException(isNAM?"El NAM supera 16 MiB.":"El IR supera 64 MiB.");
   int slot=isNAM?-1:DropSlot(target);
   if(isNAM)Call("SelectNAM");else Call("SelectSlot",slot);
   importing=true;Notify();
   object before=type.GetField(isNAM?"nam":"original",Private).GetValue(child);
   if(isNAM)await (Task)Call("LoadNAM",file);else await (Task)Call("LoadIRAsync",file);
   object after=type.GetField(isNAM?"nam":"original",Private).GetValue(child);
   return after!=null&&!ReferenceEquals(before,after);
  }catch(Exception error){Call("SetInfo",error.Message,false,true);Call("Log",error.ToString());return false;}
  finally{importing=false;UpdateDropHints();Position();Notify();}
 }
}
}
