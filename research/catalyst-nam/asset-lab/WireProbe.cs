using System;using System.Linq;using System.Collections.Generic;using System.Web.Script.Serialization;using CFEditor;
static class WireProbe {
 static int Main(){
  Console.InputEncoding=new System.Text.UTF8Encoding(false);Console.OutputEncoding=new System.Text.UTF8Encoding(false);
  var json=new JavaScriptSerializer{MaxJsonLength=4000000};var input=(object[])json.DeserializeObject(Console.In.ReadToEnd());var output=new List<object>();
  foreach(Dictionary<string,object> row in input){try{
   if(row.ContainsKey("info")){var info=AssetInfo.Decode(((object[])row["info"]).Select(x=>Convert.ToByte(x)).ToArray());output.Add(new{kind="metadata",name=info.Name,info=info.Encode().Select(x=>(int)x).ToArray()});continue;}
   if(row.ContainsKey("file")){output.Add(new{kind="display",name=AssetInfo.DisplayName((string)row["file"])});continue;}
   byte[] packet=((object[])row["packet"]).Select(x=>Convert.ToByte(x)).ToArray();var m=AssetWire.Decode(packet);
   output.Add(m==null?(object)new{kind="unrelated"}:new{kind="valid",packet=AssetWire.Encode(m).Select(x=>(int)x).ToArray(),product=m.Product,session=m.Session,transaction=m.Transaction,number=m.Number,opcode=m.Opcode,slot=m.Slot,data=m.Data.Select(x=>(int)x).ToArray()});
  }catch(Exception e){output.Add(new{kind="invalid",error=e.GetType().Name});}}
  Console.Write(json.Serialize(output));return 0;
 }
}
