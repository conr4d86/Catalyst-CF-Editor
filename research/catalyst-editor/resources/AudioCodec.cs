using System;using System.IO;using System.Linq;using System.Text;using System.Security.Cryptography;using System.Collections.Generic;using System.Web.Script.Serialization;
static class FirmwareCodec {
 public static string Hash(byte[] b){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
 public static string Canonical(object v){var d=v as Dictionary<string,object>;var j=new JavaScriptSerializer();if(d!=null)return "{"+string.Join(",",d.Keys.OrderBy(k=>k,StringComparer.Ordinal).Select(k=>j.Serialize(k)+":"+Canonical(d[k])).ToArray())+"}";var a=v as object[];return a==null?j.Serialize(v):"["+string.Join(",",a.Select(Canonical).ToArray())+"]";}
 static string Text(byte[] b,int p,int n){return Encoding.ASCII.GetString(b,p,n);}
 static byte[] Slice(byte[] b,int p,int n){if(p<0||n<0||p>b.Length-n)throw new InvalidDataException("Datos truncados");var r=new byte[n];Buffer.BlockCopy(b,p,r,0,n);return r;}
 public static byte[] ReadIR(string path){return IRImport.Read(path).Coefficients;}
}
