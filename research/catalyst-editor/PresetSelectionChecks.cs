#if DEVELOPMENT
using System;using System.IO;using System.Collections.Generic;using System.Linq;using System.Web.Script.Serialization;
namespace CFEditor {
static class PresetSelectionChecks {
 sealed class Fixture {
  public int Bank=5,Channel=2,Receipt,Pumps,Pending,Reject;public bool MissingReceipt,WrongReadback;public long Milliseconds;public readonly List<string> Writes=new List<string>();public readonly List<int> WriteReceipts=new List<int>();
  public int Read(string key){return key=="currentAmpBank"?Bank:Channel;}
  public int Write(string key,int value){if(Pending!=0)throw new Exception("A second asynchronous preset load was started before completion");Writes.Add(key+"="+value);WriteReceipts.Add(Receipt);if(Reject!=0)return Reject;Pending=3;if(!WrongReadback){if(key=="currentAmpBank")Bank=value;else Channel=value;}return 0;}
  public void Pump(){Pumps++;Milliseconds+=10;if(Pending>0&&!MissingReceipt&&--Pending==0)Receipt++;}
  public void Select(int bank,int channel,int timeout=120){PresetSelection.Select(bank,channel,Read,Write,()=>Receipt,Pump,timeout,()=>Milliseconds);}
 }
 static void Need(bool value,string why){if(!value)throw new Exception(why);}
 static void Refused<T>(Action action)where T:Exception{try{action();throw new Exception("Invalid preset selection was accepted");}catch(T){}}
 public static int Run(string file){var checks=new List<string>();try{
  var state=new Fixture();state.Select(5,1);Need(state.Writes.SequenceEqual(new[]{"currentAmpChannel=1"})&&state.Receipt==1&&state.Pumps>=4,"6B to6A wrote a redundant bank selector or skipped the completion event");checks.Add("6B to6A sends only its changed channel, waits for the genuine receipt and stable readback");
  state=new Fixture();state.Select(4,2);Need(state.Writes.SequenceEqual(new[]{"currentAmpBank=4"})&&state.Receipt==1,"Bank-only change rewrote its channel");checks.Add("Changing only the bank never rewrites its unchanged channel");
  state=new Fixture();state.Select(5,2);Need(state.Writes.Count==0&&state.Pumps==0,"Already selected preset triggered another load");checks.Add("Selecting the current preset is an exact no-op");
  state=new Fixture();state.Select(1,1);Need(state.Writes.SequenceEqual(new[]{"currentAmpChannel=1","currentAmpBank=1"})&&state.WriteReceipts.SequenceEqual(new[]{0,1})&&state.Receipt==2&&state.Pending==0,"Two-selector change overlapped asynchronous loads");checks.Add("When both selectors differ, the first load is acknowledged before starting the second");
  state=new Fixture{MissingReceipt=true};Refused<TimeoutException>(()=>state.Select(1,1,35));Need(state.Writes.Count==1,"Missing receipt caused another selector or retry");checks.Add("Matching readback without presetDidChange cannot authorize a second selector; bounded timeout never retries");
  state=new Fixture{WrongReadback=true};Refused<TimeoutException>(()=>state.Select(1,1,65));Need(state.Writes.Count==1&&state.Receipt==1,"Receipt with mismatched context triggered another selector");checks.Add("An event alone cannot confirm a mismatched bank/channel");
  state=new Fixture{Reject=4};Refused<IOException>(()=>state.Select(1,1));Need(state.Writes.Count==1&&state.Pumps==0,"Rejected selector was repeated");checks.Add("A rejected selector is not reissued or treated as a completion");
  state=new Fixture();int bankReads=0;Refused<IOException>(()=>PresetSelection.Select(1,1,key=>key=="currentAmpBank"&&++bankReads==2?4:state.Read(key),state.Write,()=>state.Receipt,state.Pump,100,()=>state.Milliseconds));Need(state.Writes.Count==0,"Changing read context reached a setter");checks.Add("Unstable initial readback rejects selection before any write");
  state=new Fixture();state.Select(5,0);Need(state.Writes.SequenceEqual(new[]{"currentAmpChannel=0"})&&state.Receipt==1,"Manual channel lost its actual selector value");checks.Add("Manual retains SDK channel0 and also waits for its actual completion");
  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));File.WriteAllText(file,new JavaScriptSerializer().Serialize(new{passed=true,USB_opened=false,SDK_loaded=false,device_enumeration=false,native_driver_called=false,checks}));return 0;
 }catch(Exception error){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));File.WriteAllText(file,new JavaScriptSerializer().Serialize(new{passed=false,USB_opened=false,error=error.ToString(),checks}));return 1;}}
}
}
#endif
