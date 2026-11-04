using System;using System.IO;using System.Security.Cryptography;using System.Threading;using System.Threading.Tasks;
namespace CFEditor {
// A stateless query precedes HELLO and journal selection. It must not replace
// or acquire another session, cancel a transfer or access preset/asset files.
static class AssetDeviceIdentity {
 static uint Read(byte[] b,int p){return BitConverter.ToUInt32(b,p);}
 static uint Nonce(){using(var random=RandomNumberGenerator.Create()){var b=new byte[4];uint n;do{random.GetBytes(b);n=Read(b,0);}while(n==0);return n;}}
 internal static async Task<string> Read(IAssetExchange channel,uint product,CancellationToken cancel,bool requireEdition=false){
  if(channel==null||!DeviceFamily.Supported((int)product))throw new ArgumentException("Modelo de amplificador no comprobado.");
  var request=new AssetMessage{Product=product,Opcode=8,Transaction=Nonce()};
  var packet=await channel.Exchange(AssetWire.Encode(request),1500,cancel).ConfigureAwait(false);var reply=AssetWire.Decode(packet);
  if(!AssetWire.RepliesTo(request,reply)||reply.Data.Length!=20&&reply.Data.Length!=52||Read(reply.Data,0)!=0||Read(reply.Data,8)!=product||Read(reply.Data,4)!=(reply.Data.Length==52?2u:1u)||requireEdition&&reply.Data.Length!=52)
   throw new IOException("El firmware no proporciona una identidad de datos compatible. No se enviará ningún recurso.");
  uint low=Read(reply.Data,12),high=Read(reply.Data,16);
  if((low|high)==0||(low&high)==uint.MaxValue)throw new InvalidDataException("La identidad del amplificador no es válida.");
  string uid=low.ToString("X8")+high.ToString("X8");if(reply.Data.Length==20)return "CFCX-UID1:"+uid;
  var edition=new byte[32];Buffer.BlockCopy(reply.Data,20,edition,0,32);int any=0,all=255;foreach(byte b in edition){any|=b;all&=b;}if(any==0||all==255)throw new InvalidDataException("La edición de datos del firmware no es válida.");
  return "CFCX-UID2:"+uid+":"+BitConverter.ToString(edition).Replace("-","");
 }
}
}
