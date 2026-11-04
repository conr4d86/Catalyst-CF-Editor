using System;using System.IO;using System.Collections.Generic;
namespace CFEditor {
// Codec only. Does not open MIDI/USB, query a device or enable any UI control.
// The installed CF must positively advertise the actual receiver capability.
sealed class AssetMessage {
 public uint Product,Session,Transaction,Number;public byte Opcode,Slot;public byte[] Data=new byte[0];
}
static class AssetWire {
 public const int MaxPacket=329,MaxChunk=256;public const uint DataWrite=1,AudioActivate=2,RecoverReceiving=4,PreampControl=8,AllAmpPreamp=16;
 static readonly byte[] Prefix={0xf0,0x7d,67,70,67,88,1};
 static uint U32(byte[] b,int p){return (uint)b[p]|((uint)b[p+1]<<8)|((uint)b[p+2]<<16)|((uint)b[p+3]<<24);}
 static void Put(byte[] b,int p,uint v){for(int i=0;i<4;i++)b[p+i]=(byte)(v>>(i*8));}
 static uint Update(uint crc,byte b){crc^=b;for(int i=0;i<8;i++)crc=(crc>>1)^((0u-(crc&1))&0xedb88320u);return crc;}
 public static uint Crc(byte[] b){if(b==null)throw new ArgumentNullException("b");uint crc=0xffffffff;foreach(byte v in b)crc=Update(crc,v);return crc^0xffffffff;}
 public static byte[] Encode(AssetMessage m){
  if(m==null||m.Data==null||m.Data.Length>MaxChunk||m.Opcode>127)throw new InvalidDataException("Mensaje CFCX no válido");
  var raw=new byte[24+m.Data.Length];Put(raw,0,m.Product);Put(raw,4,m.Session);Put(raw,8,m.Transaction);Put(raw,12,m.Number);raw[16]=m.Slot;raw[17]=(byte)m.Data.Length;raw[18]=(byte)(m.Data.Length>>8);Buffer.BlockCopy(m.Data,0,raw,20,m.Data.Length);
  uint crc=Update(0xffffffff,m.Opcode);for(int i=0;i<raw.Length-4;i++)crc=Update(crc,raw[i]);Put(raw,raw.Length-4,crc^0xffffffff);
  var result=new List<byte>(MaxPacket);result.AddRange(Prefix);result.Add(m.Opcode);
  for(int off=0;off<raw.Length;off+=7){int n=Math.Min(7,raw.Length-off);byte high=0;for(int i=0;i<n;i++)high|=(byte)((raw[off+i]>>7)<<i);result.Add(high);for(int i=0;i<n;i++)result.Add((byte)(raw[off+i]&127));}
  result.Add(0xf7);return result.ToArray();
 }
 public static bool IsOurMessage(byte[] b){if(b==null||b.Length<6)return false;for(int i=0;i<6;i++)if(b[i]!=Prefix[i])return false;return true;}
 public static bool RepliesTo(AssetMessage request,AssetMessage reply){
  if(request==null||reply==null||reply.Data==null||reply.Product!=request.Product||reply.Opcode!=(request.Opcode|64)||reply.Transaction!=request.Transaction||reply.Slot!=request.Slot||reply.Number!=request.Number)return false;
  if(reply.Session==(request.Opcode==1?request.Transaction:request.Session))return true;
  // A refused HELLO keeps the receiver's previous session. Its nonce is still
  // echoed as Transaction; accepting this NACK must never adopt that session.
  return request.Opcode==1&&reply.Data.Length==20&&U32(reply.Data,0)==1;
 }
 public static AssetMessage Decode(byte[] b){
  if(!IsOurMessage(b))return null;
  if(b.Length<9||b.Length>MaxPacket||b[6]!=1||b[7]>127||b[b.Length-1]!=0xf7)throw new InvalidDataException("Trama CFCX no válida");
  var raw=new List<byte>(280);int pos=8;
  while(pos<b.Length-1){byte high=b[pos++];int n=Math.Min(7,b.Length-1-pos);if(high>127||n==0||(high>>n)!=0)throw new InvalidDataException("Codificación MIDI no válida");for(int i=0;i<n;i++){if(b[pos]>127||raw.Count>=280)throw new InvalidDataException("Datos MIDI no válidos");raw.Add((byte)(b[pos++]|(((high>>i)&1)<<7)));}}
  var d=raw.ToArray();if(d.Length<24)throw new InvalidDataException("Mensaje CFCX truncado");int bytes=d[17]+(d[18]<<8);if(d[19]!=0||bytes>MaxChunk||d.Length!=24+bytes)throw new InvalidDataException("Longitud CFCX no válida");
  uint crc=Update(0xffffffff,b[7]);for(int i=0;i<d.Length-4;i++)crc=Update(crc,d[i]);if((crc^0xffffffff)!=U32(d,d.Length-4))throw new InvalidDataException("El mensaje CFCX no supera su comprobación de integridad");
  var m=new AssetMessage{Product=U32(d,0),Session=U32(d,4),Transaction=U32(d,8),Number=U32(d,12),Opcode=b[7],Slot=d[16],Data=new byte[bytes]};Buffer.BlockCopy(d,20,m.Data,0,bytes);return m;
 }
}
}
