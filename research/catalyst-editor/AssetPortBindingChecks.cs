using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Web.Script.Serialization;
namespace CFEditor {
static class AssetPortBindingChecks {
 static void Need(bool v,string text){if(!v)throw new Exception(text);}
 static void Refuse(Action action){try{action();}catch(IOException){return;}catch(InvalidDataException){return;}catch(NotSupportedException){return;}throw new Exception("Ambiguous/unverified binding accepted");}
 static AssetMidiEndpoint E(uint index,bool input,string parent="USB\\VID_0E41&PID_0001\\SYNTHETIC-A",string name="Catalyst",string path=null,bool unique=true){return new AssetMidiEndpoint(index,input,name,path??("\\\\?\\synthetic-"+(input?"in-":"out-")+index),parent,unique);}
 public static int Run(string file){
  var checks=new List<string>();try{
   foreach(uint product in new uint[]{2424833,2424834,2424835}){
    var pair=AssetPortBinding.Select(product,new[]{E(4,true),E(9,false)});Need(pair.Product==product&&pair.Input.Index==4&&pair.Output.Index==9,"Pair indices changed");
    Need(pair.Matches(true,pair.Input.Interface.ToUpperInvariant(),pair.Input.PhysicalParent.ToLowerInvariant()),"Canonical identity mismatch");
    Need(!pair.Matches(true,pair.Output.Interface,pair.Input.PhysicalParent)&&!pair.Matches(false,pair.Output.Interface,"USB\\VID_0E41&PID_0001\\DIFFERENT"),"Hotplug/direction mismatch accepted");
   }checks.Add("All three product IDs retain an exact directional interface and shared physical parent");
   Refuse(()=>AssetPortBinding.Select(2428928,new[]{E(0,true),E(0,false)}));
   Refuse(()=>AssetPortBinding.Select(2424833,new[]{E(0,true),E(0,false,name:"POD Go")}));
   Refuse(()=>AssetPortBinding.Select(2424833,new[]{E(0,true),E(0,false,parent:"USB\\VID_0E41&PID_0001\\OTHER")}));
   Refuse(()=>AssetPortBinding.Select(2424833,new[]{E(0,true,parent:"USB\\VID_1234&PID_0001\\FAKE"),E(0,false,parent:"USB\\VID_1234&PID_0001\\FAKE")}));
   Refuse(()=>AssetPortBinding.Select(2424833,new[]{E(0,true,path:""),E(0,false)}));
   Refuse(()=>AssetPortBinding.Select(2424833,new[]{E(0,true,unique:false),E(0,false,unique:false)}));
   var connectionOnly=AssetPortBinding.Select(2424833,new[]{E(4,true,unique:false),E(9,false,unique:false)},false);Need(!connectionOnly.RequireUnique&&connectionOnly.PhysicalIdentity.StartsWith("CFCX-PNP-CONNECTION-1:"),"A location was advertised as a persistent identity");
   Refuse(()=>AssetPortBinding.Select(2424833,new[]{E(0,true,path:"truncated\0interface"),E(0,false)}));
   Refuse(()=>AssetPortBinding.Select(2424833,new[]{E(0,true,parent:"USB\\VID_0E41&PID_0001&MI_00\\CHILD"),E(0,false)}));
   checks.Add("Bootloader, wrong vendor, POD name, missing parent and virtual/truncated identities are refused");
   Refuse(()=>AssetPortBinding.Select(2424833,new[]{E(0,true),E(0,false),E(1,true,parent:"USB\\VID_0E41&PID_0001\\SYNTHETIC-B"),E(1,false,parent:"USB\\VID_0E41&PID_0001\\SYNTHETIC-B")}));
   Refuse(()=>AssetPortBinding.Select(2424833,new[]{E(0,true),E(0,false),E(1,true)}));
   Refuse(()=>AssetPortBinding.Select(2424833,Enumerable.Range(0,513).Select(i=>E((uint)i,true))));
   checks.Add("Multiple amplifiers, duplicate logical ports and excessive inventory never select port zero implicitly");
   var bound=AssetPortBinding.Select(2424833,new[]{E(4,true),E(9,false)});var actualHandle=new IntPtr(0x12345678);int queries=0;
   Func<bool,IntPtr,uint> query=(input,handle)=>{Need(handle==actualHandle,"The real open handle was not resolved");return input?4u:9u;};
   Func<bool,uint,string> path=(input,index)=>{queries++;Need(index==(input?4u:9u),"A WinMM identity message received an HMIDI handle instead of an index");return input?bound.Input.Interface:bound.Output.Interface;};
   Func<string,bool,string> parent=(name,unique)=>{Need(unique,"Unique identity requirement lost");return bound.Input.PhysicalParent;};
   bound.RevalidateHandle(true,actualHandle,query,path,parent);bound.RevalidateHandle(false,actualHandle,query,path,parent);Need(queries==2,"Directional handle checks missing");
   Refuse(()=>bound.RevalidateHandle(true,IntPtr.Zero,query,path,parent));Refuse(()=>bound.RevalidateHandle(true,actualHandle,(i,h)=>99,path,parent));Refuse(()=>bound.RevalidateHandle(true,actualHandle,(i,h)=>{throw new IOException("Synthetic closed handle");},path,parent));Refuse(()=>bound.RevalidateHandle(true,actualHandle,query,(i,id)=>"different-interface",parent));Refuse(()=>bound.RevalidateHandle(true,actualHandle,query,path,(name,unique)=>"USB\\VID_0E41&PID_0001\\OTHER"));
   checks.Add("Open HMIDI handles resolve through GetID before index-based system identity queries; changed/closed handles and mismatched parents are rejected");
   var result=new {passed=true,USB_opened=false,SDK_loaded=false,device_enumeration=false,scope="Pure selection against synthetic endpoint evidence; native discovery and actual drivers not run",checks};Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));File.WriteAllText(file,new JavaScriptSerializer().Serialize(result));return 0;
  }catch(Exception e){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));File.WriteAllText(file,new JavaScriptSerializer().Serialize(new{passed=false,error=e.ToString(),checks}));return 1;}
 }
}
}
