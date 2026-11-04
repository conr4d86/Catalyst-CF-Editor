using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Drawing;using System.Text.RegularExpressions;using System.Windows.Forms;
namespace CFEditor {
static class AppLanguage {
 static readonly Dictionary<string,string> english=new Dictionary<string,string>(StringComparer.Ordinal),cache=new Dictionary<string,string>(StringComparer.Ordinal);
 static bool en;public static bool English{get{return en;}}public static event Action Changed;
 static string SettingsPath{get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CatalystCFEditor","language.txt");}}
 static AppLanguage(){using(var stream=typeof(AppLanguage).Assembly.GetManifestResourceStream("ui-en.tsv"))using(var reader=new StreamReader(stream)){string line;while((line=reader.ReadLine())!=null){int split=line.IndexOf('\t');if(split>0)english.Add(line.Substring(0,split),line.Substring(split+1));}}}
 public static void Load(bool preview){bool value=false;if(!preview)try{value=File.ReadAllText(SettingsPath).Trim()=="en";}catch(IOException){}catch(UnauthorizedAccessException){}Set(value,false);}
 public static void Set(bool value,bool persist){if(persist)try{Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));File.WriteAllText(SettingsPath,value?"en":"es");}catch(IOException){}catch(UnauthorizedAccessException){}if(en==value)return;en=value;cache.Clear();if(Changed!=null)Changed();}
 public static string Text(string source){if(!en||string.IsNullOrEmpty(source))return source;string result;if(english.TryGetValue(source,out result)||cache.TryGetValue(source,out result))return result;result=Dynamic(source);if(cache.Count<4096)cache[source]=result;return result;}
 public static string CanonicalCaption(string value){if(!en)return value;foreach(var pair in english)if(pair.Value==value)return pair.Key;return value;}
 static string Dynamic(string s){
  if(s.StartsWith("Amplificador conectado · CATALYST-CF-"))return s.Replace("Amplificador conectado","Amplifier connected");
  var m=Regex.Match(s,@"^BANCO (\d+)$");if(m.Success)return "BANK "+m.Groups[1].Value;
  m=Regex.Match(s,@"^(.*) · GRUPO ([12])$");if(m.Success)return m.Groups[1].Value+" · GROUP "+m.Groups[2].Value;
  m=Regex.Match(s,@"^3\. Enviar (.+) al ampli$");if(m.Success)return "3. Send "+m.Groups[1].Value+" to amp";
  m=Regex.Match(s,@"^3\. Comprobar guardado de (.+)$");if(m.Success)return "3. Verify "+m.Groups[1].Value+" save";
  m=Regex.Match(s,@"^Solo se modifica (.+)\. Las demás posiciones se conservan\.$");if(m.Success)return "Only "+m.Groups[1].Value+" will change. Other slots are preserved.";
  m=Regex.Match(s,@"^✓ (.+) guardado en el amplificador\.?$");if(m.Success)return "✓ "+m.Groups[1].Value+" saved to the amplifier.";
  m=Regex.Match(s,@"^✓ Preset (\d+[AB]) guardado y confirmado por el amplificador\.$");if(m.Success)return "✓ Preset "+m.Groups[1].Value+" saved and confirmed by the amplifier.";
  m=Regex.Match(s,@"^(.*)  ✓ Preparado$");if(m.Success)return m.Groups[1].Value+"  ✓ Ready";
  m=Regex.Match(s,@"^En el ampli: (.*) · Generación (\d+)$");if(m.Success)return "On amp: "+m.Groups[1].Value+" · Generation "+m.Groups[2].Value;
  m=Regex.Match(s,@"^¿Quieres guardar los ajustes actuales en (\d+[AB])\? Antes se creará una copia local de estos ajustes\.$");if(m.Success)return "Save the current settings to "+m.Groups[1].Value+"? A local backup of these settings will be created first.";
  m=Regex.Match(s,@"^CF compatible · (.+)$");if(m.Success)return "Compatible CF · "+Text(m.Groups[1].Value);
  if(s.StartsWith("Conversión automática a mono"))return s.Replace("Conversión automática a mono","Automatic conversion to mono").Replace("muestras","samples");
  if(s.StartsWith("A2 Lite · Mono")||s.StartsWith("Mono ·"))return s.Replace("pesos","weights").Replace("hasta ","up to ").Replace("muestras","samples");
  return s;
 }
}
// Translate captions as they change, including controls rebuilt after a model
// switch. Preset names, model/file names, values and protocol IDs stay intact.
sealed class LanguageUI:IDisposable {
 sealed class Caption{public Control Control;public string Source;}
 readonly Control root;readonly Dictionary<Control,Caption> captions=new Dictionary<Control,Caption>();readonly HashSet<Control> attached=new HashSet<Control>();bool applying;
 public LanguageUI(Control value){root=value;Attach(root);AppLanguage.Changed+=Refresh;Refresh();}
 void Attach(Control c){if(!attached.Add(c))return;c.ControlAdded+=Added;c.Disposed+=Removed;if(c is Label||c is CheckBox||c is ActionButton){captions[c]=new Caption{Control=c,Source=c.Text};c.TextChanged+=CaptionChanged;Apply(captions[c]);}foreach(Control child in c.Controls)Attach(child);}
 void Added(object sender,ControlEventArgs e){Attach(e.Control);}
 void Removed(object sender,EventArgs e){var c=(Control)sender;captions.Remove(c);attached.Remove(c);c.ControlAdded-=Added;c.TextChanged-=CaptionChanged;c.Disposed-=Removed;}
 void CaptionChanged(object sender,EventArgs e){if(applying)return;Caption caption;if(captions.TryGetValue((Control)sender,out caption)){caption.Source=AppLanguage.CanonicalCaption(caption.Control.Text);Apply(caption);}}
 void Apply(Caption caption){bool before=applying;applying=true;try{caption.Control.Text=AppLanguage.Text(caption.Source);}finally{applying=before;}}
 public void Refresh(){root.SuspendLayout();try{foreach(var caption in captions.Values.ToArray())if(!caption.Control.IsDisposed)Apply(caption);foreach(var grid in attached.OfType<DataGridView>())foreach(DataGridViewColumn column in grid.Columns){string source=Convert.ToString(column.HeaderCell.Tag??column.HeaderText);column.HeaderCell.Tag=source;column.HeaderText=AppLanguage.Text(source);}root.Invalidate(true);}finally{root.ResumeLayout(true);}}
 public void Dispose(){AppLanguage.Changed-=Refresh;foreach(var c in attached.ToArray()){c.ControlAdded-=Added;c.TextChanged-=CaptionChanged;c.Disposed-=Removed;}attached.Clear();captions.Clear();}
}
sealed partial class EditorForm {
 LanguageUI languageUI;ActionButton spanish,english;
 void BuildLanguageChoices(Control parent){spanish=new ActionButton{Text="Español",Name="LanguageES",Font=UITheme.SmallBold,Margin=Padding.Empty};english=new ActionButton{Text="English",Name="LanguageEN",Font=UITheme.SmallBold,Margin=Padding.Empty};spanish.SetBounds(0,48,77,25);english.SetBounds(83,48,77,25);parent.Controls.Add(spanish);parent.Controls.Add(english);spanish.Click+=delegate{AppLanguage.Set(false,true);};english.Click+=delegate{AppLanguage.Set(true,true);};AppLanguage.Changed+=RefreshLanguageChoices;RefreshLanguageChoices();}
 void RefreshLanguageChoices(){spanish.Active=!AppLanguage.English;english.Active=AppLanguage.English;spanish.Invalidate();english.Invalidate();}
}
}
