using System;using System.IO;using System.IO.Compression;using System.Linq;using System.Collections.Generic;using System.Text;using System.Web.Script.Serialization;
namespace CFEditor {
sealed class LibrarySlot {
 public string Name;public string Hash;public byte[] Taps;public NamModel Model;
 public const double DefaultInputDb=-5;public const double DefaultNamOutputDb=-3;
 public bool Automatic=true,Filters=true;public double InputDb=DefaultInputDb,OutputDb,LowHz=80,HighHz=8000;
 public double EffectiveDb(){if(Model!=null)return Automatic?Model.AutomaticForInput(InputDb):OutputDb;if(Taps==null)throw new InvalidOperationException("Selecciona un archivo");var filtered=IRFilters.Apply(Taps,Filters,LowHz,HighHz);return Automatic?IRLevels.Analyze(filtered).automaticDb:OutputDb;}
 public byte[] ProcessIR(){if(Taps==null)throw new InvalidOperationException("IR vacío");return IRLevels.Apply(IRFilters.Apply(Taps,Filters,LowHz,HighHz),EffectiveDb());}
}
sealed class AssetLibrary {
 public readonly LibrarySlot[] Slots=new LibrarySlot[4];
 public void LoadIR(int index,string file){if(index<0||index>2)throw new ArgumentOutOfRangeException();if(new FileInfo(file).Length>IRImport.MaxFileBytes)throw new InvalidDataException("IR demasiado grande");var taps=FirmwareCodec.ReadIR(file);IRLevels.Analyze(taps);Slots[index]=new LibrarySlot{Name=Path.GetFileName(file),Hash=FirmwareCodec.Hash(taps),Taps=taps};}
 public void LoadNAM(string file){var model=NamModel.Load(file);Slots[3]=new LibrarySlot{Name=model.Name,Hash=model.SourceHash,Model=model,Automatic=false,OutputDb=LibrarySlot.DefaultNamOutputDb};}
 public byte[] Build(){
  if(Slots.All(s=>s==null))throw new InvalidOperationException("Carga al menos un NAM o un IR");
  var files=new SortedDictionary<string,byte[]>(StringComparer.Ordinal);var items=new List<object>();
  for(int i=0;i<4;i++){var s=Slots[i];if(s==null)continue;double db=s.EffectiveDb();var paths=new List<string>();
   if(i<3){string prefix="IR"+(i+1)+"/";byte[] taps=s.ProcessIR();files[prefix+"taps.f32"]=taps;files[prefix+"fft.f32"]=FFTBank.Build(taps);paths.Add(prefix+"taps.f32");paths.Add(prefix+"fft.f32");}
   else{byte[] w=s.Model.Packed(s.InputDb,db),state,silence;NamModel.Reset(NamModel.Floats(w),out state,out silence);files["NAM/weights.f32"]=w;files["NAM/silence.f32"]=silence;paths.Add("NAM/weights.f32");paths.Add("NAM/silence.f32");}
   items.Add(new{slot=i,name=s.Name,source_sha256=s.Hash,automatic=s.Automatic,input_db=i==3?s.InputDb:0,output_db=db,filters=i<3&&s.Filters,low_hz=s.LowHz,high_hz=s.HighHz,files=paths});
  }
  var manifest=new{schema="CatalystCFEditor.AssetPack.v1",requires_receiver="CFCX60-ASSET-1",model_geometry="NAM-A2-Lite-3ch-1871-f32-48000",empty_slots="preserve",items=items,files=files.Select(p=>new{path=p.Key,bytes=p.Value.Length,sha256=FirmwareCodec.Hash(p.Value)}).ToArray()};
  files["manifest.json"]=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(manifest));using(var m=new MemoryStream()){using(var z=new ZipArchive(m,ZipArchiveMode.Create,true))foreach(var p in files){var e=z.CreateEntry(p.Key,CompressionLevel.Optimal);using(var stream=e.Open())stream.Write(p.Value,0,p.Value.Length);}byte[] packed=m.ToArray();Validate(packed);return packed;}
 }
 public static void Validate(byte[] packed){
  using(var z=new ZipArchive(new MemoryStream(packed),ZipArchiveMode.Read)){
   if(z.Entries.Count>10||z.Entries.Select(e=>e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=z.Entries.Count)throw new InvalidDataException("Paquete de recursos incorrecto");
   var entry=z.GetEntry("manifest.json");if(entry==null||entry.Length>65536)throw new InvalidDataException("Falta el inventario");Dictionary<string,object> manifest;using(var r=new StreamReader(entry.Open()))manifest=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(r.ReadToEnd());
   if(Convert.ToString(manifest["schema"])!="CatalystCFEditor.AssetPack.v1"||Convert.ToString(manifest["requires_receiver"])!="CFCX60-ASSET-1")throw new InvalidDataException("Receptor no compatible");
   var allowed=new HashSet<string>{"manifest.json"};foreach(Dictionary<string,object> item in (object[])manifest["files"]){string name=(string)item["path"];if(!new[]{"IR1/taps.f32","IR1/fft.f32","IR2/taps.f32","IR2/fft.f32","IR3/taps.f32","IR3/fft.f32","NAM/weights.f32","NAM/silence.f32"}.Contains(name)||!allowed.Add(name))throw new InvalidDataException("Ruta no admitida");var f=z.GetEntry(name);int expected=name.EndsWith("taps.f32")?8192:name.EndsWith("fft.f32")?0x4200:name.EndsWith("weights.f32")?7484:288;if(f==null||f.Length!=expected||Convert.ToInt32(item["bytes"])!=expected)throw new InvalidDataException("Longitud incorrecta");byte[] b;using(var s=f.Open())using(var m=new MemoryStream()){s.CopyTo(m);b=m.ToArray();}if(FirmwareCodec.Hash(b)!=(string)item["sha256"])throw new InvalidDataException("Recurso alterado");NamModel.Floats(b);}
   if(!z.Entries.All(e=>allowed.Contains(e.FullName)))throw new InvalidDataException("Contenido no inventariado");
  }
 }
}
}
