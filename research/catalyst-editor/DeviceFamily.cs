using System;using System.Collections.Generic;
namespace CFEditor {
// Product identifiers from Line 6 Central's res/CatalystSteps.json. These are
// normal-mode identities, not bootloader aliases. Never infer the wattage of an
// unidentified ROM bootloader or substitute a CX60 identity for another model.
static class DeviceFamily {
 public static readonly Dictionary<int,string> Names=new Dictionary<int,string>{{2424833,"Catalyst / CX60"},{2424834,"Catalyst / CX100"},{2424835,"Catalyst / CX200"}};
 public static bool Supported(int id){return Names.ContainsKey(id);}
 public static bool Bootloader(int id){return id==2428928||id==2428929;}
 public static string Describe(int id){string name;return Names.TryGetValue(id,out name)?name:"Producto no identificado";}
}
}
