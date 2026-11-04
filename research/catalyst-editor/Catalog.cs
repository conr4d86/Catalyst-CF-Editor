using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using System.Web.Script.Serialization;
namespace CFEditor {
sealed class Parameter {
 public string id{get;set;} public string name{get;set;} public double minimum{get;set;} public double maximum{get;set;}
 public double displayMinimum{get;set;} public double displayMaximum{get;set;} public int format{get;set;} public int curve{get;set;} public int control{get;set;}
 public string kind{get;set;} public string parent{get;set;} public string[] choices{get;set;} public string[] displayLabels{get;set;}
 public void Validate(object value){double v=value is bool?((bool)value?1:0):Convert.ToDouble(value);if(double.IsNaN(v)||double.IsInfinity(v)||v<minimum||v>maximum||kind=="int"&&Math.Truncate(v)!=v||kind=="bool"&&v!=0&&v!=1)throw new ArgumentOutOfRangeException(id);}
 public string Display(int raw){
  if(choices!=null&&raw-minimum>=0&&raw-minimum<choices.Length)return choices[raw-(int)minimum];
  if(displayLabels!=null&&raw-minimum>=0&&raw-minimum<displayLabels.Length)return displayLabels[raw-(int)minimum];
  if(maximum==1&&minimum==0)return raw==0?"OFF":"ON";
  double t=(raw-minimum)/Math.Max(1,maximum-minimum);if(curve!=0)return raw.ToString()+" / "+maximum;
  double v=displayMinimum+t*(displayMaximum-displayMinimum);
  switch(format){case 1:return v.ToString("0");case 2:return v.ToString("0")+" %";case 3:return v.ToString("0")+" dB";case 4:return v.ToString("0")+" ms";case 5:return v.ToString("0.00")+" Hz";case 8:return v>=1000?(v/1000).ToString("0.0")+" kHz":v.ToString("0")+" Hz";case 36:return v.ToString("0.00")+" s";case 38:return (v*1000).ToString("0")+" ms";default:return raw.ToString();}
 }
}
sealed class Asset {public string id{get;set;}public string name{get;set;}public string role{get;set;}public string category{get;set;}public Parameter[] parameters{get;set;}public override string ToString(){return id=="Chime"?"NAM 1":id=="Chime 2"?"NAM 2":name+(role=="ampCategory"?(id.EndsWith(" 2")?" · GRUPO 2":" · GRUPO 1"):"");}}
sealed class Catalog {
 public int schema{get;set;}public Asset[] assets{get;set;}public Dictionary<string,string[]> labels{get;set;}
 public static Catalog Load(){using(var r=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("catalog.json")))return new JavaScriptSerializer().Deserialize<Catalog>(r.ReadToEnd());}
 public Asset Find(string id){return assets.FirstOrDefault(a=>a.id==id);}
 public Asset[] Amps{get{return assets.Where(a=>a.role=="ampCategory").OrderBy(a=>Array.IndexOf(new[]{"Clean","Clean 2","Chime","Chime 2","Boutique","Boutique 2","Crunch","Crunch 2","Dynamic","Dynamic 2","Hi Gain","Hi Gain 2"},a.id)).ToArray();}}
 public static readonly string[] EffectFamilies={"Delay","Reverb","Modulación","Pitch / Octava","Distorsión","Synth"};
 public static string EffectFamily(Asset a){if(a.role=="reverbCategory")return "Reverb";if(a.id=="TycoctaviaFuzz")return "Distorsión";if(new[]{"BassOctaver","SimplePitch1","SimplePitch2"}.Contains(a.id))return "Pitch / Octava";if(a.category=="fXCategoryDelay")return "Delay";if(a.category=="fXCategoryMod")return "Modulación";return "Synth";}
 public Asset[] Effects{get{return assets.Where(a=>a.role=="fXCategory"||a.role=="reverbCategory").OrderBy(a=>Array.IndexOf(EffectFamilies,EffectFamily(a))).ThenBy(a=>a.name,StringComparer.CurrentCultureIgnoreCase).ToArray();}}
 public Parameter Param(string group,string asset,string id){var a=Find(asset);var p=a==null?null:a.parameters.FirstOrDefault(v=>v.id==id);if(p!=null)return p;
  if(group=="CatalystGroupDirectOut"&&id=="CabinetIndex")return Int(id,"IR",0,2,new[]{"IR1","IR2","IR3"});
  if(group=="CatalystGroupDirectOut"&&id=="MicIndex")return Int(id,"Micrófono",0,15);
  if(id=="graphSlot_EnabledState"||group=="global"&&id=="cabMicEnable")return Bool(id,id);
  return null;
 }
 public static Parameter Int(string id,string name,int min,int max,string[] choices=null){return new Parameter{id=id,name=name,minimum=min,maximum=max,displayMinimum=min,displayMaximum=max,kind="int",choices=choices};}
 public static Parameter Bool(string id,string name){var p=Int(id,name,0,1);p.kind="bool";return p;}
}
sealed class Binding {
 public readonly string Group;public readonly Parameter Param;
 public readonly bool Editable;public readonly string AssetID;
 public string Key{get{return Group+"/"+Param.id;}}
 public Binding(string g,Parameter p,bool editable=true,string asset=null){Group=g;Param=p;Editable=editable;AssetID=asset;}
}
static class EngineUnits {
 public static int TempoMs(int bpm){if(bpm<20||bpm>240)throw new ArgumentOutOfRangeException("bpm");return (int)Math.Round(60000.0/bpm);}
 public static int TempoBpm(int ms){if(ms<250||ms>3000)throw new ArgumentOutOfRangeException("ms");return (int)Math.Round(60000.0/ms);}
 public static string TunerNote(int value){return value<0||value>11?"—":new[]{"E","F","G♭","G","A♭","A","B♭","B","C","D♭","D","E♭"}[value];}
}
}
