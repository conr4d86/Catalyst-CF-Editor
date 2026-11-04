using System;using System.Linq;
namespace CFEditor {
// Public label derived from the firmware edition, never the amplifier's UID.
// The short display label is not used instead of the live protocol handshake.
static class FirmwareIdentifier {
 const string Prefix="CATALYST-CF-";
 internal static bool FullEdition(string identity,out string edition){edition=null;if(identity==null||identity.Length!=91||!identity.StartsWith("CFCX-UID2:",StringComparison.Ordinal)||identity[26]!=':')return false;string uid=identity.Substring(10,16),value=identity.Substring(27);if(!uid.All(Uri.IsHexDigit)||!value.All(Uri.IsHexDigit)||uid.All(c=>c=='0')||uid.All(c=>c=='F'||c=='f')||value.All(c=>c=='0')||value.All(c=>c=='F'||c=='f'))return false;edition=value.ToUpperInvariant();return true;}
 public static bool PublicLabel(string identity,out string label){label=null;string edition;if(!FullEdition(identity,out edition))return false;label=Prefix+edition.Substring(0,12);return true;}
 public static bool ValidLabel(string label){return label!=null&&label.Length==Prefix.Length+12&&label.StartsWith(Prefix,StringComparison.Ordinal)&&label.Substring(Prefix.Length).All(Uri.IsHexDigit);}
}
}
