using System;using System.IO;using System.Linq;
namespace CFEditor {
// Import and preparation only. No SDK, firmware builder or device discovery.
sealed class WorkspaceAsset {
 public byte Slot;public string Name,Summary;public LibrarySlot Settings;public AssetPreparation InitialPreparation;
 public bool IsNAM{get{return Slot>=3;}}
}
sealed class AssetPreparation {
 public byte Slot;public byte[] Data;public AssetInfo Info;
}
sealed class AssetWorkspace {
 public readonly WorkspaceAsset[] Slots=new WorkspaceAsset[5];
 public static string SlotName(int slot){if(slot<0||slot>=5)throw new ArgumentOutOfRangeException("slot");return slot<3?"IR"+(slot+1):"NAM "+(slot-2);}
 public static bool Accepts(int slot,string file){if(slot<0||slot>=5||string.IsNullOrWhiteSpace(file))return false;return slot>=3?Path.GetExtension(file).Equals(".nam",StringComparison.OrdinalIgnoreCase):IRImport.SupportedPath(file);}
 public static WorkspaceAsset Import(byte slot,string file){
  if(!Accepts(slot,file))throw new InvalidDataException("El archivo no corresponde a "+SlotName(slot)+".");
  if(!File.Exists(file))throw new FileNotFoundException("El archivo ya no está disponible.");
  if(new FileInfo(file).Length>(slot>=3?16*1024*1024:IRImport.MaxFileBytes))throw new InvalidDataException("El archivo supera el tamaño admitido.");
  string name=Path.GetFileName(file);LibrarySlot settings;string summary;
  if(slot>=3){var model=NamModel.Load(file);settings=new LibrarySlot{Name=name,Hash=model.SourceHash,Model=model,Filters=false,Automatic=false,OutputDb=LibrarySlot.DefaultNamOutputDb};summary="A2 Lite · Mono · 48 kHz";}
  else{var imported=IRImport.Read(file);IRLevels.Analyze(imported.Coefficients);settings=new LibrarySlot{Name=name,Hash=imported.SourceHash,Taps=imported.Coefficients};summary=imported.Summary;}
  // Complete validation before replacing any previous selection.
  var prepared=Prepare(slot,settings);return new WorkspaceAsset{Slot=slot,Name=name,Summary=summary,Settings=settings,InitialPreparation=prepared};
 }
 public static LibrarySlot Snapshot(LibrarySlot s){if(s==null)throw new InvalidOperationException("Carga un archivo primero.");return new LibrarySlot{Name=s.Name,Hash=s.Hash,Model=s.Model,Taps=s.Taps==null?null:(byte[])s.Taps.Clone(),Automatic=s.Automatic,Filters=s.Filters,InputDb=s.InputDb,OutputDb=s.OutputDb,LowHz=s.LowHz,HighHz=s.HighHz};}
 public static AssetPreparation Prepare(byte slot,LibrarySlot source){
  if(slot>=5)throw new ArgumentOutOfRangeException("slot");var s=Snapshot(source);double db;byte[] payload;
  if(slot<3){
   if(s.Taps==null||s.Model!=null)throw new InvalidDataException("Falta el IR de esta ranura.");
   byte[] filtered=IRFilters.Apply(s.Taps,s.Filters,s.LowHz,s.HighHz);db=s.Automatic?IRLevels.Analyze(filtered).automaticDb:s.OutputDb;
   byte[] taps=IRLevels.Apply(filtered,db),fft=FFTBank.Build(taps);payload=new byte[taps.Length+fft.Length];Buffer.BlockCopy(taps,0,payload,0,taps.Length);Buffer.BlockCopy(fft,0,payload,taps.Length,fft.Length);
  }else{
   if(s.Model==null||s.Taps!=null)throw new InvalidDataException("Falta el NAM de esta ranura.");
   db=s.Automatic?s.Model.AutomaticForInput(s.InputDb):s.OutputDb;byte[] weights=s.Model.Packed(s.InputDb,db),state,silence;NamModel.Reset(NamModel.Floats(weights),out state,out silence);
   payload=new byte[weights.Length+silence.Length];Buffer.BlockCopy(weights,0,payload,0,weights.Length);Buffer.BlockCopy(silence,0,payload,weights.Length,silence.Length);
  }
  if(payload.Length!=(slot<3?25088:7772))throw new InvalidDataException("Geometría del recurso no compatible.");
  if(s.Hash==null||s.Hash.Length!=64||!s.Hash.All(Uri.IsHexDigit))throw new InvalidDataException("Falta la huella del archivo original.");
  var info=new AssetInfo{Name=AssetInfo.DisplayName(s.Name),Automatic=s.Automatic,Filters=slot<3&&s.Filters,InputDb=(float)(slot<3?0:s.InputDb),OutputDb=(float)db,LowHz=(float)(slot<3?s.LowHz:0),HighHz=(float)(slot<3?s.HighHz:0),SourceHash=Enumerable.Range(0,32).Select(i=>Convert.ToByte(s.Hash.Substring(i*2,2),16)).ToArray()};info.Encode();
  return new AssetPreparation{Slot=slot,Data=payload,Info=info};
 }
}
}
