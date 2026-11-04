using System;using System.IO;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;
namespace CFEditor {
// Explicit development command, using the same journal and data client as the UI.
// Never invokes the updater or changes the selected model/preset.
static class DataSendTrial {
 sealed class Evidence {public byte slot=4;public string payload_sha256;public uint transaction,expected_crc,expected_length;public AssetInfo expected_info;public AssetReceipt[] before;}
 static uint Nonce(){uint value;do{value=BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(),0);}while(value==0);return value;}
 static bool Same(AssetReceipt a,AssetReceipt b){return a==null?b==null:b!=null&&a.Slot==b.Slot&&a.Generation==b.Generation&&a.Transaction==b.Transaction&&a.Length==b.Length&&a.Crc==b.Crc&&a.Info.Encode().SequenceEqual(b.Info.Encode());}
 internal static int Prepare(string model,string report){var a=AssetWorkspace.Import(4,model).InitialPreparation;File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{payload_sha256=FirmwareCodec.Hash(a.Data),length=a.Data.Length,crc=AssetWire.Crc(a.Data),info=a.Info,USB_opened=false}));return 0;}
 internal static int Run(string model,string expected,string report){return Execute(AssetWorkspace.Import(4,model).InitialPreparation,expected,Path.GetFullPath(report)).GetAwaiter().GetResult();}
 internal static int ReloadIR(byte slot,string file,string expected,string report){if(slot>=3)throw new ArgumentOutOfRangeException("slot");var data=File.ReadAllBytes(file);if(data.Length!=25088||!FFTBank.Build(data.Take(8192).ToArray()).SequenceEqual(data.Skip(8192)))throw new InvalidDataException("El IR no coincide con su banco FFT.");return Execute(new AssetPreparation{Slot=slot,Data=data},expected,Path.GetFullPath(report),true).GetAwaiter().GetResult();}
 internal static int Confirm(string source,string report){return ReadBack(source,Path.GetFullPath(report)).GetAwaiter().GetResult();}
 static void Verify(Evidence e,AssetReceipt[] after){
  for(int i=0;i<5;i++)if(i!=e.slot&&!Same(e.before[i],after[i]))throw new InvalidDataException("Ha cambiado otra ranura durante el envío.");
  var p=e.before[e.slot];var n=after[e.slot];if(n==null||n.Generation!=(p==null?1:p.Generation+1)||n.Transaction!=e.transaction||n.Length!=e.expected_length||n.Crc!=e.expected_crc||!n.Info.Encode().SequenceEqual(e.expected_info.Encode()))throw new InvalidDataException("El recibo no corresponde al recurso enviado.");
 }
 static async Task<int> Execute(AssetPreparation a,string expected,string report,bool preserveInfo=false){
  Directory.CreateDirectory(Path.GetDirectoryName(report));if(File.Exists(report)||File.Exists(report+".before.json"))throw new IOException("Ya existe este ensayo; comprueba el recibo antes de repetir.");
  if(FirmwareCodec.Hash(a.Data)!=expected)throw new InvalidDataException("El archivo preparado ha cambiado.");
  var e=new Evidence{slot=a.Slot,payload_sha256=expected,transaction=Nonce(),expected_crc=AssetWire.Crc(a.Data),expected_length=(uint)a.Data.Length,expected_info=a.Info,before=new AssetReceipt[5]};
  var session=AssetDataSession.Native(Path.Combine(Path.GetDirectoryName(report),"reload-journal"));Exception error=null;var after=new AssetReceipt[5];bool closed=false;var clock=System.Diagnostics.Stopwatch.StartNew();
  try{
   await session.Connect(2424833,Nonce(),CancellationToken.None).ConfigureAwait(false);var client=session.Client;
   if(Enumerable.Range(0,5).Any(i=>client.HasPending((byte)i)))throw new IOException("Hay un envío pendiente: hay que reconciliarlo antes de enviar otro NAM.");
   for(byte i=0;i<5;i++)e.before[i]=await client.ReadSlot(i,Nonce(),CancellationToken.None).ConfigureAwait(false);
   if(preserveInfo){var prior=e.before[e.slot];if(prior==null||prior.Crc!=e.expected_crc||prior.Length!=e.expected_length)throw new InvalidDataException("La prueba solo permite reenviar el mismo IR que ya contiene esa ranura.");a.Info=e.expected_info=AssetInfo.Decode(prior.Info.Encode());}
   while(e.before.Any(r=>r!=null&&r.Transaction==e.transaction))e.transaction=Nonce();
   // Persist exact expected receipt and untouched-slot baseline BEFORE any write.
   File.WriteAllText(report+".before.json",new JavaScriptSerializer().Serialize(e));
   await client.Upload(e.slot,e.transaction,a.Data,a.Info,null,CancellationToken.None).ConfigureAwait(false);
   for(byte i=0;i<5;i++)after[i]=await client.ReadSlot(i,Nonce(),CancellationToken.None).ConfigureAwait(false);
   Verify(e,after);
  }catch(Exception ex){error=ex;}
  try{await session.Close().ConfigureAwait(false);closed=!session.HasOwner;}catch(Exception ex){error=error==null?ex:new AggregateException(error,ex);}
  bool passed=error==null&&closed;File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{passed,e.slot,e.payload_sha256,e.transaction,e.expected_crc,e.expected_length,e.expected_info,e.before,after,shutdown_confirmed=closed,firmware_installed=false,presets_saved=false,elapsed_ms=clock.ElapsedMilliseconds,error=error==null?null:error.ToString()}));if(!closed)new ManualResetEvent(false).WaitOne();return passed?0:1;
 }
 static async Task<int> ReadBack(string source,string report){
  var e=new JavaScriptSerializer().Deserialize<Evidence>(File.ReadAllText(source));if(e.before==null||e.before.Length!=5||e.expected_info==null||e.transaction==0)throw new InvalidDataException("Falta el recibo esperado.");
  var session=AssetDataSession.Native(Path.Combine(Path.GetDirectoryName(source),"reload-journal"));var after=new AssetReceipt[5];Exception error=null;bool closed=false;
  try{await session.Connect(2424833,Nonce(),CancellationToken.None).ConfigureAwait(false);var c=session.Client;for(byte i=0;i<5;i++){if(c.HasPending(i)){var resolution=await c.Reconcile(i,Nonce(),CancellationToken.None).ConfigureAwait(false);if(!resolution.Stored)throw new IOException("El envío pendiente no está guardado; no se ha reenviado.");}after[i]=await c.ReadSlot(i,Nonce(),CancellationToken.None).ConfigureAwait(false);}Verify(e,after);}catch(Exception ex){error=ex;}
  try{await session.Close().ConfigureAwait(false);closed=!session.HasOwner;}catch(Exception ex){error=error==null?ex:new AggregateException(error,ex);}
  bool passed=error==null&&closed;File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{passed,e.payload_sha256,e.transaction,after,shutdown_confirmed=closed,assets_written=false,firmware_installed=false,error=error==null?null:error.ToString()}));if(!closed)new ManualResetEvent(false).WaitOne();return passed?0:1;
 }
}
}
