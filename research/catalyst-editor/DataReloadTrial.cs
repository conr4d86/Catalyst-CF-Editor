using System;using System.IO;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;
namespace CFEditor {
// Explicit exact-payload NAM 2 trial through the production data client. No
// updater/SDK/model selection. A lost receipt leaves its journal for reconcile.
static class DataReloadTrial {
 sealed class Previous {public bool passed;public string payload_sha256;public AssetReceipt[] before;}
 internal static int Confirm(string source,string report){return Check(source,report).GetAwaiter().GetResult();}
 static async Task<int> Check(string source,string report){
  source=Path.GetFullPath(source);report=Path.GetFullPath(report);var prior=new JavaScriptSerializer().Deserialize<Previous>(File.ReadAllText(source));if(prior.before==null||prior.before.Length!=5||prior.before.Any(r=>r==null))throw new InvalidDataException("Falta el estado previo del ensayo.");
  var session=AssetDataSession.Native(Path.Combine(Path.GetDirectoryName(source),"reload-journal"));Exception error=null;var after=new AssetReceipt[5];bool closed=false;
  try{await session.Connect(2424833,0x70101502,CancellationToken.None).ConfigureAwait(false);var client=session.Client;if(Enumerable.Range(0,5).Any(i=>client.HasPending((byte)i)))throw new IOException("Queda un envío pendiente. No se repite ni se cancela.");
   for(byte i=0;i<5;i++)after[i]=await client.ReadSlot(i,0x70101900u+i,CancellationToken.None).ConfigureAwait(false);
   for(int i=0;i<5;i++){var a=prior.before[i];var b=after[i];if(b==null||b.Generation!=a.Generation+(i==4?1u:0u)||b.Transaction!=(i==4?0x70101701u:a.Transaction)||b.Length!=a.Length||b.Crc!=a.Crc||!b.Info.Encode().SequenceEqual(a.Info.Encode()))throw new InvalidDataException("El estado no coincide con la transacción y los recursos conservados.");}
  }catch(Exception e){error=e;}try{await session.Close().ConfigureAwait(false);closed=!session.HasOwner;}catch(Exception e){error=error==null?e:new AggregateException(error,e);}
  bool passed=error==null&&closed;File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{passed,source_report_sha256=FirmwareCodec.Hash(File.ReadAllBytes(source)),prior.payload_sha256,after,shutdown_confirmed=closed,assets_written=false,firmware_installed=false,error=error==null?null:error.ToString()}));if(!closed)new ManualResetEvent(false).WaitOne();return passed?0:1;
 }
 internal static int Run(string model,string expected,string report){return Execute(model,expected,Path.GetFullPath(report)).GetAwaiter().GetResult();}
 static async Task<int> Execute(string model,string expected,string report){
  Directory.CreateDirectory(Path.GetDirectoryName(report));if(File.Exists(report))throw new IOException("Revisa el resultado anterior antes de repetir esta prueba.");
  var asset=AssetWorkspace.Import(4,model).InitialPreparation;if(FirmwareCodec.Hash(asset.Data)!=expected)throw new InvalidDataException("La captura preparada no coincide con los datos de la prueba.");
  var session=AssetDataSession.Native(Path.Combine(Path.GetDirectoryName(report),"reload-journal"));Exception error=null;AssetReceipt[] before=new AssetReceipt[5],after=new AssetReceipt[5];bool closed=false;var clock=System.Diagnostics.Stopwatch.StartNew();
  try{await session.Connect(2424833,0x70101501,CancellationToken.None).ConfigureAwait(false);var client=session.Client;
   if(Enumerable.Range(0,5).Any(i=>client.HasPending((byte)i)))throw new IOException("Hay un envío pendiente que debe reconciliarse, no repetirse.");
   for(byte i=0;i<5;i++)before[i]=await client.ReadSlot(i,0x70101600u+i,CancellationToken.None).ConfigureAwait(false);
   var prior=before[4];if(prior==null||prior.Crc!=AssetWire.Crc(asset.Data)||prior.Info==null||!prior.Info.Encode().SequenceEqual(asset.Info.Encode()))throw new InvalidDataException("La prueba exige reenviar exactamente el Friedman ya instalado.");
   await client.Upload(4,0x70101701,asset.Data,asset.Info,null,CancellationToken.None).ConfigureAwait(false);
   for(byte i=0;i<5;i++)after[i]=await client.ReadSlot(i,0x70101800u+i,CancellationToken.None).ConfigureAwait(false);
   for(int i=0;i<4;i++)if(before[i]==null||after[i]==null||before[i].Generation!=after[i].Generation||before[i].Transaction!=after[i].Transaction||before[i].Crc!=after[i].Crc||!before[i].Info.Encode().SequenceEqual(after[i].Info.Encode()))throw new InvalidDataException("Ha cambiado otra ranura durante la prueba.");
   if(after[4]==null||after[4].Generation!=prior.Generation+1||after[4].Transaction!=0x70101701||after[4].Crc!=AssetWire.Crc(asset.Data)||!after[4].Info.Encode().SequenceEqual(asset.Info.Encode()))throw new InvalidDataException("El recibo no corresponde a los datos enviados.");
  }catch(Exception e){error=e;}
  try{await session.Close().ConfigureAwait(false);closed=!session.HasOwner;}catch(Exception e){error=error==null?e:new AggregateException(error,e);}
  bool passed=error==null&&closed;File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{passed,slot=4,payload_sha256=expected,before,after,shutdown_confirmed=closed,firmware_installed=false,presets_saved=false,elapsed_ms=clock.ElapsedMilliseconds,error=error==null?null:error.ToString()}));if(!closed)new ManualResetEvent(false).WaitOne();return passed?0:1;
 }
}
}
