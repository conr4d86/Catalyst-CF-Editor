"""Build the existing glass UI plus one NAM tab, without loading device SDKs."""
from pathlib import Path
import shutil,subprocess,json,hashlib
ROOT=Path(__file__).resolve().parent
IR=ROOT.parent.parent/'catalyst-ir'
GENERATED=ROOT/'source'
OUT=ROOT.parents[2]/'CatalystIR/V3.0-NAM-experimental'

def replace(s,old,new):
    assert s.count(old)==1,(old[:100],s.count(old))
    return s.replace(old,new)

def main(output=None):
    global OUT
    if output:OUT=Path(output).resolve()
    GENERATED.mkdir(exist_ok=True);OUT.mkdir(parents=True,exist_ok=True)
    sources=['OneClickApp.cs','OneClickBundle.cs','OneClickWorkflow.cs','GlassUI.cs','ProjectLinks.cs','FirmwareCodec.cs','MultiIRCodec.cs','MultiIRSlots.cs','FFTBank.cs','FirmwarePatch.cs','TemplateSource.cs','IRLevels.cs','IRFilters.cs','EngineProbe.cs','CentralAdapter.cs','InProcessSDK.cs','AppVersion.cs']
    for name in sources:shutil.copy2(IR/name,GENERATED/name)
    path=GENERATED/'OneClickApp.cs';s=path.read_text(encoding='utf-8-sig')
    s=replace(s,'class OneClickApp : Form','partial class OneClickApp : Form')
    s=replace(s,'  try{return slots==null?null:slots.Recipe();}catch{return null;}','  try{return FullRecipe();}catch{return null;}')
    s=replace(s,'   // Test switches never install firmware or change device parameters.','   int namResult;if(NamCommands(args,out namResult))return namResult;\n   // Test switches never install firmware or change device parameters.')
    s=s.replace('OneClickBundle.PatchHash','NamFirmwareCodec.AppTemplateHash')
    s=replace(s,'data=Path.Combine(data,"v2");','data=Path.Combine(data,"v3-nam");')
    s=replace(s,'   "v2.0 · Experimental",','   "v3.0 · NAM experimental",')
    s=replace(s,'  choose.Click+=delegate{','  InitializeNAM();\n  choose.Click+=delegate{')
    s=replace(s,'  if(busy)return;selectedSlot=index;','  if(busy)return;namSelected=false;selectedSlot=index;')
    s=replace(s,'  // A previous process cannot prove a new physical power cycle.','  // A previous process cannot prove a new physical power cycle.')
    marker=' async Task Poll(){'
    at=s.index(marker);before=s[:at];tail=s[at:]
    end=before.rfind('\n }');before=before[:end]+'\n  ShowNAM();'+before[end:];s=before+tail
    s=replace(s,'session.activationSlot=slots.slots[selectedSlot]!=null?selectedSlot:slots.First;session.Save(Journal);','session.activationSlot=slots.slots[selectedSlot]!=null?selectedSlot:slots.First;session.hasNAM=nam!=null;session.Save(Journal);')
    s=replace(s,'byte[][] processed=slots.Process();byte[] candidate=await Task.Run(()=>{byte[] template=OneClickBundle.Template();byte[] built=MultiIRCodec.WithIRs(template,processed);FirmwareCodec.TestGuards(template,built);return built;});','if(nam!=null){File.WriteAllBytes(Path.Combine(folder,"NAM-original.nam"),nam.Source);File.WriteAllText(Path.Combine(folder,"NAM-settings.json"),json.Serialize(NAMSettings()));}\n   byte[] candidate=await Task.Run(()=>BuildSelection());')
    s=replace(s,'SetInfo("Preparando y comprobando el IR…");','SetInfo(nam==null?"Preparando y comprobando el IR…":"Preparando NAM + IR y comprobando el firmware…");')
    s=replace(s,'  busy=true;RefreshUI();progress.Style=ProgressBarStyle.Marquee;\n  try{\n   // Preparation is offline;', '  try{RequireFreeEditors();}catch(Exception e){SetInfo(e.Message,false,true);return;}\n  busy=true;RefreshUI();progress.Style=ProgressBarStyle.Marquee;\n  try{\n   // Preparation is offline;')
    s=replace(s,' void Log(string text){if(!closing){details.AppendText(text+Environment.NewLine);',' void Log(string text){if(!closing){details.AppendText(text+Environment.NewLine);if(session!=null&&Directory.Exists(session.folder))File.AppendAllText(Path.Combine(session.folder,"activity.log"),text+Environment.NewLine);')
    s=replace(s,' async Task ActivateIR(){\n  if(busy||session==null)return;',' async Task ActivateIR(){\n  if(session!=null&&session.hasNAM){session.phase="active";session.bootRecallSaved=false;session.Save(Journal);completedThisWindow=true;SetInfo("Transferencia terminada. NAM está en CHIME GROUP 1. Ajusta el volumen y guarda el preset desde el ampli.",true);RefreshUI();return;}\n  if(busy||session==null)return;')
    # Keep the original single-IR and three-IR startup/interaction behavior.
    path.write_text(s,encoding='utf-8')
    path=GENERATED/'OneClickWorkflow.cs';s=path.read_text(encoding="utf-8-sig");s=replace(s,' public int activationSlot;',' public int activationSlot;\n public bool hasNAM;');path.write_text(s,encoding="utf-8")
    path=GENERATED/'FirmwareCodec.cs';s=path.read_text(encoding="utf-8-sig");s=replace(s,'  MultiIRCodec.ValidateCandidate(template,candidate);return;','  NamFirmwareCodec.ValidateAny(template,candidate);return;');s=replace(s,'  MultiIRCodec.TestGuards(template,candidate);return;','  NamFirmwareCodec.TestGuards(template,candidate);return;');path.write_text(s,encoding="utf-8")
    path=GENERATED/'MultiIRCodec.cs';s=path.read_text(encoding="utf-8-sig");s=s.replace('Mutable(byte[][] irs)','Mutable(byte[][] irs,bool allowEmpty)');s=replace(s,'if(mask==0)throw','if(mask==0&&!allowEmpty)throw');s=replace(s,'WithIRs(byte[] template,byte[][] irs)','WithIRs(byte[] template,byte[][] irs,bool allowEmpty=false)');s=replace(s,'Transform(c,Mutable(irs),null)','Transform(c,Mutable(irs,allowEmpty),null)');s=replace(s,'ReadIRs(byte[] candidate)','ReadIRs(byte[] candidate,bool allowEmpty=false)');s=replace(s,'if(mask<1||mask>7)','if(mask>7||mask==0&&!allowEmpty)');path.write_text(s,encoding="utf-8")
    path=GENERATED/'AppVersion.cs';s=path.read_text();s=s.replace('2.0.0.0','3.0.0.0').replace('2.0.0-alpha.1','3.0.0-alpha.1');path.write_text(s)
    # The native updater uses the same guarded validator as offline export.
    # No new helper process or device command is introduced.
    calibration=ROOT/'resources/nam-layout.json';assert calibration.exists()
    compiler=Path('C:/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe')
    args=[str(compiler),'/nologo','/platform:x86','/target:winexe','/define:IN_PROCESS,AUTOBOOT,MULTI_IR,NAM','/main:OneClickApp','/out:'+str(OUT/'CatalystIR.exe'),'/win32icon:'+str(IR/'assets/catalystir.ico')]
    args+=['/reference:'+n+'.dll' for n in ['System.Windows.Forms','System.Drawing','System.Web.Extensions','System.IO.Compression']]
    args+=['/resource:'+str(IR/'assets/catalystir.ico')+',application.ico','/resource:'+str(IR/'assets/catalyst-cx60-front.png')+',amplifier.png','/resource:'+str(IR/'build-v2-alpha/private-bundle.zip')+',private-bundle.zip','/resource:'+str(IR/'distribution/firmware-multi-patch.json')+',patch.json','/resource:'+str(ROOT/'resources/nam-engine.ctf')+',nam-engine.ctf']
    args+=[str(GENERATED/name) for name in sources]+[str(ROOT/name) for name in ['NamModel.cs','NamFirmwareCodec.cs','NamTab.cs']]+[str(ROOT/'resources/NamLayout.cs')]
    p=subprocess.run(args,capture_output=True,text=True);(OUT/'build.log').write_text(p.stdout+p.stderr,encoding='utf-8');assert p.returncode==0,p.stdout+p.stderr
    inventory={str(f.relative_to(ROOT)):hashlib.sha256(f.read_bytes()).hexdigest() for f in [*GENERATED.glob('*.cs'),ROOT/'NamModel.cs',ROOT/'NamFirmwareCodec.cs',ROOT/'NamTab.cs',ROOT/'resources/nam-engine.ctf']}
    (OUT/'BUILD.json').write_text(json.dumps(dict(executable_sha256=hashlib.sha256((OUT/'CatalystIR.exe').read_bytes()).hexdigest(),hardware_accessed=False,firmware_installed=False,source_sha256=inventory),indent=2))
    print(json.dumps(dict(executable=str(OUT/'CatalystIR.exe'),sha256=hashlib.sha256((OUT/'CatalystIR.exe').read_bytes()).hexdigest(),hardware_accessed=False),indent=2))
if __name__=='__main__':
    import argparse
    parser=argparse.ArgumentParser();parser.add_argument('--output')
    main(parser.parse_args().output)
