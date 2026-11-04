"""Derive a family-aware host component; never modify the proven CX60 app."""
from pathlib import Path
from legacy_sources import legacy_input
import hashlib,json,shutil,subprocess
ROOT=Path(__file__).resolve().parent
NAM=ROOT.parent/'catalyst-nam/app'
IR=ROOT.parent/'catalyst-ir'
SOURCE=ROOT/'family-source'
OUT=ROOT/'resources/CatalystIRFamily.dll'
NAMES=['OneClickApp.cs','OneClickBundle.cs','OneClickWorkflow.cs','GlassUI.cs','ProjectLinks.cs','FirmwareCodec.cs','MultiIRCodec.cs','MultiIRSlots.cs','FFTBank.cs','FirmwarePatch.cs','TemplateSource.cs','IRLevels.cs','IRFilters.cs','EngineProbe.cs','CentralAdapter.cs','InProcessSDK.cs','AppVersion.cs']
def replace(s,a,b):
    assert s.count(a)==1,(a[:100],s.count(a))
    return s.replace(a,b)
def main():
    SOURCE.mkdir(exist_ok=True)
    for name in NAMES:shutil.copy2(NAM/'source'/name,SOURCE/name)
    for name in ['NamModel.cs','NamFirmwareCodec.cs','NamTab.cs']:shutil.copy2(NAM/name,SOURCE/name)
    shutil.copy2(NAM/'resources/NamLayout.cs',SOURCE/'NamLayout.cs')
    p=SOURCE/'OneClickApp.cs';s=p.read_text(encoding='utf-8-sig')
    s=replace(s,'choose.Click+=delegate{using(var dialog=new OpenFileDialog{Filter="IR WAV (*.wav)|*.wav",Title="Cargar tu respuesta de impulso"})if(dialog.ShowDialog(this)==DialogResult.OK)LoadIR(dialog.FileName);};',
        'choose.Click+=async delegate{using(var dialog=new OpenFileDialog{Filter=IRImport.FileFilter,Title="Cargar tu respuesta de impulso"})if(dialog.ShowDialog(this)==DialogResult.OK)await LoadIRAsync(dialog.FileName);};')
    s=replace(s,'byte[] b=FirmwareCodec.ReadIR(file);IRLevels.Analyze(b);string next=FirmwareCodec.Hash(b);','var importedIR=IRImport.Read(file);byte[] b=importedIR.Coefficients;IRLevels.Analyze(b);string next=FirmwareCodec.Hash(b);')
    s=replace(s,'SetInfo("IR válido. El botón guarda la copia de presets y prepara la instalación.",true);','SetInfo(importedIR.Summary+". IR preparado para instalar.",true);')
    s=replace(s,'  ShowNAM();\n }','  ShowNAM();\n  RefreshEmbeddedPreview();\n }')
    s=replace(s,' async Task Action(){',' async Task Action(){\n  if(embeddedPreview){SetInfo("Instalación desactivada en vista de diseño.");return;}')
    s=replace(s,' async Task Poll(){',' async Task Poll(){\n  if(embeddedPreview)return;')
    p.write_text(s,encoding='utf-8')
    p=SOURCE/'FirmwareCodec.cs';s=p.read_text(encoding='utf-8-sig')
    first=s.index(' public static byte[] ReadIR(');last=s.index(' static byte[] HexBytes(',first)
    s=s[:first]+' public static byte[] ReadIR(string path){return IRImport.Read(path).Coefficients;}\n'+s[last:]
    s=replace(s,'if(Convert.ToInt32(meta["device"])!=2424833||Convert.ToInt32(meta["device_version"])!=0x02020000)',
        'int archiveProduct=Convert.ToInt32(meta["device"]);\n  if(!FamilyProfile.IsModel(archiveProduct)||Convert.ToInt32(meta["device_version"])!=0x02020000)')
    s=replace(s,'  return presets.Count;','  foreach(Dictionary<string,object> preset in presets)if(Convert.ToInt32(preset["device"])!=archiveProduct||Convert.ToInt32(preset["device_version"])!=0x02020000)throw new InvalidDataException("La copia mezcla productos o versiones");\n  return presets.Count;')
    s=replace(s,'var presets=new List<object>();var reads=new List<object>();','var presets=new List<object>();var reads=new List<object>();int product=0;')
    s=replace(s,'if(Convert.ToInt32(data["device"])!=2424833||Convert.ToInt32(data["device_version"])!=0x02020000)',
        'int readProduct=Convert.ToInt32(data["device"]);if(product==0)product=readProduct;\n   if(!FamilyProfile.IsModel(product)||readProduct!=product||FamilyProfile.ConnectedProduct!=0&&product!=FamilyProfile.ConnectedProduct||Convert.ToInt32(data["device_version"])!=0x02020000)')
    s=replace(s,'device=2424833,device_version=0x02020000','device=product,device_version=0x02020000')
    p.write_text(s,encoding='utf-8')
    p=SOURCE/'MultiIRSlots.cs';s=p.read_text(encoding='utf-8-sig')
    s=replace(s,'public string path,name,key;','public string path,name,key,sourcePath,conversion;')
    s=replace(s,'if(!File.Exists(target))File.Copy(file,target);',
        'if(!File.Exists(target))File.WriteAllBytes(target,IRImport.WaveBytes(b));\n  string ext=Path.GetExtension(file).ToLowerInvariant();if(!IRImport.SupportedPath(file))ext=".audio";string source=Path.Combine(folder,IRImport.Read(file).SourceHash+"-source"+ext);if(!File.Exists(source))File.Copy(file,source);')
    s=replace(s,'name=Path.GetFileName(file),key=key,profile=p','name=Path.GetFileName(file),key=key,profile=p,sourcePath=source,conversion=IRImport.Read(file).Summary')
    s=replace(s,'if(!Path.IsPathRooted(s.path))s.path=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path),s.path));',
        'if(!Path.IsPathRooted(s.path))s.path=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path),s.path));\n   if(!string.IsNullOrEmpty(s.sourcePath)&&!Path.IsPathRooted(s.sourcePath))s.sourcePath=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path),s.sourcePath));')
    s=replace(s,'saved.slots[i]=new Slot{path=name,name=s.name,key=s.key,profile=Clone(s.profile)};',
        'string original=null;if(!string.IsNullOrEmpty(s.sourcePath)&&File.Exists(s.sourcePath)){original="IR"+(i+1)+"-source"+Path.GetExtension(s.sourcePath);File.Copy(s.sourcePath,Path.Combine(folder,original));}\n   saved.slots[i]=new Slot{path=name,name=s.name,key=s.key,profile=Clone(s.profile),sourcePath=original,conversion=s.conversion};')
    p.write_text(s,encoding='utf-8')
    p=SOURCE/'EngineProbe.cs';s=p.read_text(encoding='utf-8-sig')
    s=replace(s,'EngineWrapper.SendString+=(g,p,v,d)=>{if(g=="systemState"&&p=="deviceSyncCompleted")synchronized.Set();','EngineWrapper.SendString+=(g,p,v,d)=>{if(g=="systemState"&&p=="deviceSyncStarted")FamilyProfile.RecordSync(v);if(g=="systemState"&&p=="deviceSyncCompleted")synchronized.Set();')
    s=replace(s,'  var engine=new EngineWrapper();keepEngineAlive=engine;','  FamilyProfile.SyncProduct=0;\n  var engine=new EngineWrapper();keepEngineAlive=engine;')
    marker='    Log("Full device synchronization confirmed");'
    s=replace(s,marker,marker+'\n    int familyProduct=engine.getIntParamValue("systemState","currentDeviceID");\n    if(!FamilyProfile.IsModel(familyProduct))familyProduct=FamilyProfile.SyncProduct;\n    if(!FamilyProfile.IsModel(familyProduct)||FamilyProfile.IsModel(FamilyProfile.SyncProduct)&&familyProduct!=FamilyProfile.SyncProduct)throw new InvalidOperationException("El producto Catalyst no está identificado o hay discrepancia de identidad");\n    FamilyProfile.ConnectedProduct=familyProduct;Log("FAMILY_PRODUCT="+familyProduct);')
    p.write_text(s,encoding='utf-8')
    p=SOURCE/'CentralAdapter.cs';s=p.read_text(encoding='utf-8-sig')
    s=replace(s,'   var listing=engine.getDictionaryParamValue("systemState","deviceListData");','   int expectedProduct=install?FamilyProfile.BackupProduct(args[2]):0;\n   var listing=engine.getDictionaryParamValue("systemState","deviceListData");')
    s=replace(s,'   var compatible=ids.Select((v,i)=>new{ID=v,Index=i}).Where(v=>v.ID==2424833||(recovery&&(v.ID==2428928||v.ID==2428929))).ToList();\n   if(compatible.Count!=1)throw new InvalidOperationException("Expected exactly one Catalyst 60 or its recovery bootloader");\n   int index=compatible[0].Index;',
        '   int index=FamilyProfile.TargetIndex(ids,recovery,expectedProduct);')
    s=replace(s,'if(recovery&&compatible[0].ID!=2424833)','if(recovery&&FamilyProfile.IsBootloader(ids[index]))')
    s=replace(s,'var identity=new Dictionary<string,object>{{"setDeviceIdentity_deviceIndexKey",index},{"setDeviceIdentity_currentDeviceProductIDKey",compatible[0].ID},{"setDeviceIdentity_newDeviceProductIDKey",2424833},{"setDeviceIdentity_newDeviceFriendlyNameKey","Catalyst 60"}};',
        'var identity=FamilyProfile.RecoveryIdentity(index,ids[index],args[2]);')
    s=s.replace('Identify known Catalyst 60 recovery target=','Identify recovery target from verified product backup=')
    s=replace(s,'   if(id!=2424833)throw new InvalidOperationException("Expected Catalyst 60");',
        '   if(!FamilyProfile.IsModel(id)||install&&id!=expectedProduct)throw new InvalidOperationException("La copia no corresponde al modelo conectado");\n   FamilyProfile.ConnectedProduct=id;')
    p.write_text(s,encoding='utf-8')
    for name in ['OneClickApp.cs','GlassUI.cs']:
        p=SOURCE/name;s=p.read_text(encoding='utf-8-sig').replace('TextRenderer.DrawText(', 'ClippedText.Draw(');p.write_text(s,encoding='utf-8')
    reviewed=json.loads((ROOT/'copy-review.json').read_text(encoding='utf-8'))
    for name in ['OneClickApp.cs','NamTab.cs']:
        p=SOURCE/name;s=p.read_text(encoding='utf-8-sig')
        for before,after in reviewed.items():s=s.replace('"'+before+'"','"'+after+'"')
        p.write_text(s,encoding='utf-8')
    p=SOURCE/'GlassUI.cs';s=p.read_text(encoding='utf-8-sig').replace('9.5f','10f').replace('8.5f','9f');p.write_text(s,encoding='utf-8')
    import re
    for name in ['OneClickApp.cs','NamTab.cs']:
        p=SOURCE/name;s=p.read_text(encoding='utf-8-sig');s=re.sub(r'\bButton\b','StableButton',s);s=s.replace('  base.OnPaintBackground(e);if(Width','  if(Width');p.write_text(s,encoding='utf-8')
    compiler=Path('C:/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe')
    args=[str(compiler),'/nologo','/platform:x86','/target:library','/define:IN_PROCESS,AUTOBOOT,MULTI_IR,NAM','/out:'+str(OUT)]
    args+=['/reference:'+n+'.dll' for n in ['System.Windows.Forms','System.Drawing','System.Web.Extensions','System.IO.Compression']]
    for f,n in [(IR/'assets/catalystir.ico','application.ico'),(IR/'assets/catalyst-cx60-front.png','amplifier.png'),(IR/'build-v2-alpha/private-bundle.zip','private-bundle.zip'),(IR/'distribution/firmware-multi-patch.json','patch.json'),(NAM/'resources/nam-engine.ctf','nam-engine.ctf')]:args+=['/resource:'+str(f)+','+n]
    external=[legacy_input(ROOT,n) for n in ['family/FamilyProfile.cs','family/FamilyProbe.cs','family/EmbeddedPreview.cs','family/IRImportIntegration.cs','IRImport.cs','IRMediaFoundation.cs','ClippedText.cs','StableButton.cs']]
    args+=list(map(str,SOURCE.glob('*.cs')))+list(map(str,external))
    p=subprocess.run(args,capture_output=True,text=True);(SOURCE/'build.log').write_text(p.stdout+p.stderr,encoding='utf-8')
    assert p.returncode==0,p.stdout+p.stderr
    report=dict(component=str(OUT),sha256=hashlib.sha256(OUT.read_bytes()).hexdigest(),hardware_accessed=False,enabled_in_main_app=False,firmware_engine_sha256=hashlib.sha256((NAM/'resources/nam-engine.ctf').read_bytes()).hexdigest(),sources={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in list(SOURCE.glob('*.cs'))+external})
    (ROOT/'resources/family-build.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps({k:v for k,v in report.items() if k!='sources'},indent=2))
if __name__=='__main__':main()
