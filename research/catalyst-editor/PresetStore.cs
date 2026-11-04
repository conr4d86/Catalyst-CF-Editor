using System;using System.IO;using System.Diagnostics;
namespace CFEditor {
sealed class PresetSaveContext {
 public readonly int Bank,Channel,Index;
 PresetSaveContext(int bank,int channel,int index){Bank=bank;Channel=channel;Index=index;}
 // Catalyst's bank/channel controls are authoritative. The cross-product
 // currentDevicePresetIndex can stay zero/stale (also observed by the original
 // verified-preset reader); it is not a second independent device receipt.
 public static PresetSaveContext Read(Func<string,string,int> read){
  int bank=read("systemState","currentAmpBank"),channel=read("systemState","currentAmpChannel");
  if(read("systemState","currentAmpBank")!=bank||read("systemState","currentAmpChannel")!=channel)throw new IOException("El preset está cambiando. Espera a que termine la sincronización antes de guardar.");
  if(channel==0)throw new IOException("El amplificador está en modo Manual. Selecciona un preset A o B antes de guardar.");
  if(bank<0||bank>5||channel<1||channel>2)throw new IOException("No se ha podido confirmar un banco y canal válidos. No se ha guardado ningún preset.");
  return new PresetSaveContext(bank,channel,bank*2+channel);
 }
}
// Same bank/channel API and completion events used by Catalyst Edit's Save dialog.
static class PresetStore {
 public static void Commit(int expected,Func<string,string,int> read,Func<string,string,int,int> write,Func<int> receipt,Action arm,Action pump,int timeoutMs=12000,bool selectorFallthrough=false){
  int bank=read("systemState","currentAmpBank"),channel=read("systemState","currentAmpChannel");
  Action verify=()=>{var current=PresetSaveContext.Read(read);if(current.Bank!=bank||current.Channel!=channel||current.Index!=expected)throw new IOException("Ha cambiado el preset; no se ha enviado el guardado.");};
  // In the hash-pinned 2.01 SDK, selector setters store the value and then
  // fall through to the generic parameter dispatcher, which can return 4.
  // Only these two local selectors may use that quirk, with exact readback.
  Action<string,int> select=(parameter,value)=>{int result=write("devicePresetSaveTarget",parameter,value);if(result!=0&&!(selectorFallthrough&&result==4))throw new IOException("El SDK rechazó el destino de guardado (código "+result+", "+parameter+"). Se conserva la copia local.");if(read("devicePresetSaveTarget",parameter)!=value)throw new IOException("El destino de guardado no coincide; no se ha enviado el guardado.");};
  verify();select("targetDevicePresetBank",bank);select("targetDevicePresetIndex",channel-1);
  if(read("devicePresetSaveTarget","targetDevicePresetBank")!=bank||read("devicePresetSaveTarget","targetDevicePresetIndex")!=channel-1)throw new IOException("El destino de guardado no coincide; no se ha enviado el guardado.");
  verify();arm();int commit=write("devicePresetSaveTarget","saveDevicePresetToCurrentTarget",1);if(commit!=0)throw new IOException("El amplificador rechazó la orden de guardar (código "+commit+"). Se conserva la copia local.");
  var clock=Stopwatch.StartNew();while(receipt()==0&&clock.ElapsedMilliseconds<timeoutMs)pump();
  if(receipt()<0)throw new IOException("El amplificador ha comunicado un fallo al guardar. La copia local se conserva.");
  if(receipt()==0)throw new TimeoutException("El amplificador no ha confirmado el guardado. La copia local se conserva; no se ha reintentado automáticamente.");
 }
}
}
