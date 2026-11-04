using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Diagnostics;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;using System.Windows.Forms;
namespace CFEditor {
// Explicit development-only connection diagnostic, no visible form. Uses the
// same native backend/STA shutdown as the editor. The default probe omits CF;
// its explicit second stage adds only identity/HELLO/control metadata reads.
// No parameter edits, preset selection/store, backup cycling or updater API.
static class NativeConnectionCheck {
 public static int Run(string report,bool controls=false){
  report=Path.GetFullPath(report);Directory.CreateDirectory(Path.GetDirectoryName(report));
  int exit=1;var events=new List<string>();var clock=Stopwatch.StartNew();
  using(var app=new ApplicationContext())using(var start=new System.Windows.Forms.Timer{Interval=10}){
   start.Tick+=async delegate{
    start.Stop();SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
    var backend=new NativeBackend(controls);backend.Status+=line=>{events.Add(line);File.AppendAllText(report+".log",line+Environment.NewLine);};Exception failure=null;bool synchronized=false,closed=false;int product=0;
    try{await backend.Connect();synchronized=backend.Connected;product=backend.ProductID;if(!synchronized)throw new IOException("No se ha confirmado la sincronización.");await Task.Delay(1500);if(!backend.Connected)throw new IOException("La conexión se ha perdido durante la comprobación.");}catch(Exception error){failure=error;}
    // C# 5: asynchronous cleanup stays outside catch/finally. A failed close
    // keeps the backend's quarantined owner; never kill it or announce free USB.
    try{await backend.Disconnect();closed=true;}catch(Exception error){failure=failure==null?error:new AggregateException(failure,error);}
    try{exit=failure==null&&synchronized&&closed?0:1;File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{passed=exit==0,hardware_connection_attempted=backend.NativeInitializationAttempted,SDK_wrapper_loaded=AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="EngineCLIWrapper"),connection_synchronized=synchronized,product,shutdown_confirmed=closed,CF_controls_probed=controls,firmware_installed=false,presets_changed=false,parameters_edited=false,elapsed_ms=clock.ElapsedMilliseconds,events,error=failure==null?null:failure.ToString()}));}finally{app.ExitThread();}
   };start.Start();Application.Run(app);
  }return exit;
 }
}
}
