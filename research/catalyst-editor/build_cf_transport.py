"""Separate in-process transport component. Never opens an SDK or USB port.

The legacy payload/source/proof remain unchanged. The extra gate is a bounded
host callback for ONE separately validated initial CF, not a permissive parser.
Until a host arms it, the original firmware validation remains authoritative.
"""
import hashlib,json,shutil,subprocess
from pathlib import Path
from legacy_sources import legacy_input
ROOT=Path(__file__).resolve().parent;IR=ROOT.parent/'catalyst-ir';NAM=ROOT.parent/'catalyst-nam/app'
OUT=ROOT/'cf-transport-build';OUT.mkdir(exist_ok=True)
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    proof=json.loads((ROOT/'resources/family-validated.json').read_text())
    assert sha(ROOT/'resources/CatalystIRFamily.dll')==proof['component_sha256']
    for path,digest in proof['sources'].items():assert sha(ROOT/path)==digest,path
    sources=OUT/'source';sources.mkdir(exist_ok=True)
    for p in (ROOT/'family-source').glob('*.cs'):shutil.copyfile(p,sources/p.name)
    p=sources/'FirmwareCodec.cs';s=p.read_text(encoding='utf-8-sig');needle=' public static void ValidateCandidate(byte[] template,byte[] candidate){';assert s.count(needle)==1;s=s.replace(needle,needle+'\n  if(CFCandidateGate.TryValidate(candidate))return;');p.write_text(s,encoding='utf-8')
    (sources/'CFCandidateGate.cs').write_text('''using System;using System.IO;
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
''',encoding='utf-8')
    component=OUT/'CatalystCFTransport.dll';args=['C:/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe','/nologo','/utf8output','/platform:x86','/target:library','/define:IN_PROCESS,AUTOBOOT,MULTI_IR,NAM','/out:'+str(component)]
    args+=['/reference:'+n+'.dll' for n in ['System.Windows.Forms','System.Drawing','System.Web.Extensions','System.IO.Compression']]
    for f,n in [(IR/'assets/catalystir.ico','application.ico'),(IR/'assets/catalyst-cx60-front.png','amplifier.png'),(IR/'build-v2-alpha/private-bundle.zip','private-bundle.zip'),(IR/'distribution/firmware-multi-patch.json','patch.json'),(NAM/'resources/nam-engine.ctf','nam-engine.ctf')]:args+=['/resource:'+str(f)+','+n]
    external=[legacy_input(ROOT,n) for n in ['family/FamilyProfile.cs','family/FamilyProbe.cs','family/EmbeddedPreview.cs','family/IRImportIntegration.cs','IRImport.cs','IRMediaFoundation.cs','ClippedText.cs','StableButton.cs']]
    inputs=list(sources.glob('*.cs'))+external;args+=list(map(str,inputs));run=subprocess.run(args,capture_output=True,text=True,encoding='utf-8',errors='replace');(OUT/'build.log').write_text(run.stdout+run.stderr,encoding='utf-8');assert run.returncode==0,run.stdout+run.stderr
    report=dict(component=str(component),component_sha256=sha(component),hardware_accessed=False,SDK_loaded=False,USB_opened=False,legacy_component_unchanged=True,legacy_component_sha256=proof['component_sha256'],host_gate_reviewed=False,enabled_in_main_app=False,sources={str(p.relative_to(ROOT)):sha(p) for p in inputs})
    (OUT/'build.json').write_text(json.dumps(report,indent=2));print(json.dumps({k:v for k,v in report.items() if k!='sources'},indent=2))
if __name__=='__main__':main()
