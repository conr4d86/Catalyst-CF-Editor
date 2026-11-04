using System;using System.IO;
public static class CFCandidateGate {
 public static Func<byte[],bool> Validator;
 public static bool TryValidate(byte[] candidate){var callback=Validator;if(callback==null)return false;if(candidate==null||candidate.Length<12||candidate.Length>16000000)throw new InvalidDataException("Firmware inicial no válido.");if(!callback((byte[])candidate.Clone()))throw new InvalidDataException("El firmware no coincide con el candidato preparado por el editor.");return true;}
}
public static class CFCandidateProbe {
 public static string Run(byte[] original,byte[] initial){
  bool legacy=false,unarmed=false,accepted=false,rejected=false,identity=false;CFCandidateGate.Validator=null;
  FirmwareCodec.ValidateCandidate(original,original);legacy=true;
  try{FirmwareCodec.ValidateCandidate(original,initial);}catch(InvalidDataException){unarmed=true;}
  string expected=FirmwareCodec.Hash(initial);CFCandidateGate.Validator=b=>FirmwareCodec.Hash(b)==expected;
  try{FirmwareCodec.ValidateCandidate(original,initial);accepted=true;var altered=(byte[])initial.Clone();altered[altered.Length/2]^=1;try{FirmwareCodec.ValidateCandidate(original,altered);}catch(InvalidDataException){rejected=true;}identity=FirmwareCodec.Hash(initial)==expected;}
  finally{CFCandidateGate.Validator=null;}
  if(!legacy||!unarmed||!accepted||!rejected||!identity)throw new Exception("Candidate gate regression");return "Legacy validation preserved; unarmed initial image rejected; exact prepared image accepted; changed image rejected; caller bytes unchanged";
 }
}
