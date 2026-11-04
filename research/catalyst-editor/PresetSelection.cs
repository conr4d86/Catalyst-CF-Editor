using System;using System.IO;using System.Diagnostics;
namespace CFEditor {
// Native selectors start asynchronous preset loads, including redundant writes.
// This helper neither opens a connection nor retries a selector. The caller
// executes it inside its existing serialized SDK queue.
static class PresetSelection {
 sealed class Context {
  public readonly int Bank,Channel;public Context(int bank,int channel){Bank=bank;Channel=channel;}
  public bool Same(Context other){return other!=null&&Bank==other.Bank&&Channel==other.Channel;}
 }
 static Context Read(Func<string,int> read){int bank=read("currentAmpBank"),channel=read("currentAmpChannel");if(bank<0||bank>5||channel<0||channel>2||read("currentAmpBank")!=bank||read("currentAmpChannel")!=channel)throw new IOException("El preset está cambiando. Espera y vuelve a sincronizar antes de seleccionar otro.");return new Context(bank,channel);}
 public static void Select(int bank,int channel,Func<string,int> read,Func<string,int,int> write,Func<int> receipt,Action pump,int timeoutMs=12000,Func<long> elapsed=null){
  if(bank<0||bank>5||channel<0||channel>2)throw new ArgumentOutOfRangeException();if(read==null||write==null||receipt==null||pump==null)throw new ArgumentNullException();if(timeoutMs<1||timeoutMs>30000)throw new ArgumentOutOfRangeException("timeoutMs");
  var clock=Stopwatch.StartNew();if(elapsed==null)elapsed=()=>clock.ElapsedMilliseconds;var current=Read(read);
  foreach(string key in new[]{"currentAmpChannel","currentAmpBank"}){
   int target=key=="currentAmpChannel"?channel:bank;if((key=="currentAmpChannel"?current.Channel:current.Bank)==target)continue;
   if(!Read(read).Same(current))throw new IOException("Ha cambiado el preset; no se ha enviado otra selección.");
   var expected=new Context(key=="currentAmpBank"?target:current.Bank,key=="currentAmpChannel"?target:current.Channel);int before=receipt();
   int result=write(key,target);if(result!=0)throw new IOException("El amplificador rechazó la selección de preset (código "+result+"). No se ha reintentado.");
   long started=elapsed();int stable=0,lastReceipt=before;bool confirmed=false;
   while(elapsed()-started<timeoutMs){
    int observed=receipt();var actual=Read(read);
    if(observed!=before&&actual.Same(expected)){stable=observed==lastReceipt?stable+1:1;if(stable>=2){confirmed=true;break;}}
    else stable=0;
    lastReceipt=observed;pump();
   }
   if(!confirmed)throw new TimeoutException("El amplificador no ha confirmado el cambio de preset. Espera y vuelve a sincronizar; no se ha repetido la selección.");
   current=expected;
  }
 }
}
}
