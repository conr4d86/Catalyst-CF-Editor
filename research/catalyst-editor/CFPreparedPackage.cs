using System;using System.IO;
namespace CFEditor {
// Prepared bytes are immutable across background preparation, export and
// installation. Callers receive snapshots, never the installer's backing data.
sealed class CFPreparedPackage {
 readonly byte[] bytes,edition;public readonly string Hash;public readonly int InitialSlots;
 public byte[] Bytes{get{return (byte[])bytes.Clone();}}public byte[] Edition{get{return (byte[])edition.Clone();}}
 internal CFPreparedPackage(byte[] payload,byte[] identity,int slots){if(payload==null||payload.Length==0||identity==null||identity.Length!=32||slots<0||slots>5)throw new ArgumentException("El firmware preparado no está completo.");bytes=(byte[])payload.Clone();edition=(byte[])identity.Clone();Hash=FirmwareCodec.Hash(bytes);InitialSlots=slots;}
 public void Export(string target){target=Path.GetFullPath(target);string temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}if(FirmwareCodec.Hash(File.ReadAllBytes(temporary))!=Hash)throw new IOException("No se pudo verificar el firmware preparado.");if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);}finally{if(File.Exists(temporary))File.Delete(temporary);}}
}
}
