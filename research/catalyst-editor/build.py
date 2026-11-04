"""Build the independent editor. Does not load the SDK or access USB."""
import hashlib, json, plistlib, subprocess, zipfile, io
from pathlib import Path
from legacy_sources import legacy_input

ROOT = Path(__file__).resolve().parent
TV = ROOT.parents[1]
IR = ROOT.parent / 'catalyst-ir'
OUT = TV / 'CatalystEditor/Development'
MANAGED_CHECKS = False
BOOTSTRAP_DEMOS = False
RES = ROOT / 'resources'

def sha(b): return hashlib.sha256(b).hexdigest()

def prepare():
    RES.mkdir(parents=True, exist_ok=True)
    # Approved knob emblem: full name at larger sizes, compact mark at 16..32px.
    import struct
    icon_sizes = [16, 24, 32, 48, 64, 128, 256]
    icon_images = [(RES/'icon-v2'/f'{n}.png').read_bytes() for n in icon_sizes]
    offset = 6 + 16*len(icon_sizes)
    icon = bytearray(struct.pack('<HHH', 0, 1, len(icon_sizes)))
    for n, image in zip(icon_sizes, icon_images):
        icon += struct.pack('<BBBBHHII', n % 256, n % 256, 0, 0, 1, 32, len(image), offset)
        offset += len(image)
    (RES/'catalyst-cf-editor-v2.ico').write_bytes(icon + b''.join(icon_images))
    defs = plistlib.loads((TV/'CatalystIR/V3.0-NAM-USB-corrected/AssetDefs.plist').read_bytes())
    assets = []
    def walk(c, role=None):
        role = role or c['categoryID']
        for a in c.get('assets', []):
            if role == 'uITest': continue
            assets.append(dict(id=a['engineID'], name=a['friendlyNameLong'], role=role,category=c['categoryID'],
                parameters=[dict(id=p['engineID'], name=p['displayName'], minimum=p['minEngineValue'],
                    maximum=p['maxEngineValue'], displayMinimum=p['displayValueMin'],
                    displayMaximum=p['displayValueMax'], format=p['valueDisplayFormatIdentifier'],
                    curve=p['displayCurveIdentifier'], control=p['controlTypeIdentifier'],
                    parent=p.get('parentParamEngineID', ''),
                    kind='bool' if p['engineID'] in ('graphSlot_EnabledState','graphSlotAsset_TempoSyncActive') else 'int')
                    for p in a.get('assetParameters', [])]))
        for n in c.get('childCategories', []): walk(n, role)
    for c in defs['Categories']: walk(c)
    scripts = IR/'build/runtime/editor/afd97bb774dc570c/PresentationScripts'
    lists = json.loads((scripts/'QuadPlatform_ValueLists.json').read_text(encoding='utf-8-sig'))
    labels = {v['ExplicitIdentifier']:v['Items'] for s in lists if s['ObjectType']=='ItemNames' for v in s['ObjectData']}
    rhythm=next(v['ValueList'] for s in lists for v in s.get('ObjectData',[]) if v.get('ExplicitIdentifier')=='DisplayValues_TempoSyncSubDivision')
    rhythm=['1/1' if s=='Whole' else s.lower().replace(' dotted',' ·').replace(' triplet',' T') for s in rhythm]
    switch_labels={'Channel A/B Select':'Canal A/B','Effect 1 Bypass':'Efecto 1: ON/OFF','Boost Bypass':'Boost: ON/OFF','Effect 2 Bypass':'Efecto 2: ON/OFF','Effect 1 and Effect 2 Bypass':'Efectos 1 y 2: ON/OFF','Boost and Effect 1 Bypass':'Boost y efecto 1: ON/OFF','Boost and Effect 2 Bypass':'Boost y efecto 2: ON/OFF','Tap Tempo':'TAP Tempo','Preset Bank Up':'Siguiente banco'}
    for k in labels:
        if 'Footswitch' in k:labels[k]=[switch_labels.get(s,s) for s in labels[k]]
    for a in assets:
        for p in a['parameters']:
            if p['id']=='graphSlotAsset_TempoSyncSubDivision':p['displayLabels']=rhythm
            key='ItemNames_'+p['id']
            if p['control']==0 and key in labels and len(labels[key])==p['maximum']-p['minimum']+1:
                p['choices']=labels[key]
    data=dict(schema=1,assets=assets,labels=labels)
    (RES/'catalog.json').write_text(json.dumps(data,ensure_ascii=False),encoding='utf-8')
    # Editor runtime only: no updater, firmware, user presets, IRs or NAM models.
    with zipfile.ZipFile(IR/'build-v2-alpha/private-bundle.zip') as z:
        entries=[e for e in json.loads(z.read('manifest.json')) if e['path'].startswith('editor/')]
        b=io.BytesIO()
        with zipfile.ZipFile(b,'w',zipfile.ZIP_DEFLATED) as target:
            for e in entries:
                payload=z.read(e['path']); assert sha(payload)==e['sha256']
                target.writestr(e['path'],payload)
            target.writestr('manifest.json',json.dumps(entries))
    (RES/'editor-runtime.zip').write_bytes(b.getvalue())
    with zipfile.ZipFile(RES/'knobs.zip','w',zipfile.ZIP_DEFLATED) as target:
        for i in range(127):
            p=IR/'editor-source/graphics'/f'catalystknob_{i}.png'
            assert p.exists(),p
            target.writestr(f'{i}.png',p.read_bytes())
    # Reuse the exact validated WAV decoder and model importer without firmware operations.
    codec=(IR/'FirmwareCodec.cs').read_text(encoding='utf-8-sig')
    wav=' public static byte[] ReadIR(string path){return IRImport.Read(path).Coefficients;}\n'
    utils='''using System;using System.IO;using System.Linq;using System.Text;using System.Security.Cryptography;using System.Collections.Generic;using System.Web.Script.Serialization;
static class FirmwareCodec {
 public static string Hash(byte[] b){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
 public static string Canonical(object v){var d=v as Dictionary<string,object>;var j=new JavaScriptSerializer();if(d!=null)return "{"+string.Join(",",d.Keys.OrderBy(k=>k,StringComparer.Ordinal).Select(k=>j.Serialize(k)+":"+Canonical(d[k])).ToArray())+"}";var a=v as object[];return a==null?j.Serialize(v):"["+string.Join(",",a.Select(Canonical).ToArray())+"]";}
 static string Text(byte[] b,int p,int n){return Encoding.ASCII.GetString(b,p,n);}
 static byte[] Slice(byte[] b,int p,int n){if(p<0||n<0||p>b.Length-n)throw new InvalidDataException("Datos truncados");var r=new byte[n];Buffer.BlockCopy(b,p,r,0,n);return r;}
'''+wav+'}\n'
    (RES/'AudioCodec.cs').write_text(utils,encoding='utf-8')
    glass=(IR/'GlassUI.cs').read_text(encoding='utf-8-sig')
    start=glass.index(' public static void Background(')
    end=glass.index('\n}\n\nclass GlassPanel',start)
    glass=glass[:start]+' public static void Background(Graphics g,Rectangle r){CFEditor.BlackTheme.Background(g,r);}\n'+glass[end:]
    panel_start=glass.index(' protected override void OnPaintBackground(PaintEventArgs e){',glass.index('class GlassPanel'))
    panel_end=glass.index('\n }',panel_start)+3
    glass=glass[:panel_start]+' protected override void OnPaintBackground(PaintEventArgs e){CFEditor.BlackTheme.Surface(e.Graphics,this); }'+glass[panel_end:]
    glass=glass.replace('Color.FromArgb(168,36,51,72)','Color.FromArgb(238,24,25,28)').replace('Color.FromArgb(178,18,29,45)','Color.FromArgb(240,13,14,17)').replace('Color.FromArgb(75,123,153,180)','Color.FromArgb(110,72,75,80)')
    glass=glass.replace('TextRenderer.DrawText(', 'ClippedText.Draw(')
    glass=glass.replace('9.5f','10f').replace('8.5f','9f')
    (RES/'GlassUI.cs').write_text(glass,encoding='utf-8')
    return data

def main():
    data=prepare(); OUT.mkdir(parents=True,exist_ok=True)
    # The initial receiver package is embedded separately from the unchanged
    # legacy UI. No physical approval is inferred from a codec/emulator pass.
    cf_build=ROOT/'cf-transport-build/build.json'
    cf_info=json.loads(cf_build.read_text(encoding='utf-8'))
    cf_transport=ROOT/'cf-transport-build/CatalystCFTransport.dll'
    assert cf_info['host_gate_reviewed'] and sha(cf_transport.read_bytes())==cf_info['component_sha256']
    assert sha((ROOT/cf_info['host_gate_proof']).read_bytes())==cf_info['host_gate_proof_sha256']
    for name,digest in cf_info['sources'].items():assert sha(legacy_input(ROOT,name,digest).read_bytes())==digest,name
    cf_segment=ROOT.parent/'catalyst-nam/asset-lab/boot-owner-lab/NON-INSTALLABLE-segment.bin'
    assert sha(cf_segment.read_bytes())=='ff8bbdc15b631b5a1f1909fa477c06837ab6181406acab2b0061fdd55538595a'
    from initial_build_proof import verify as verify_initial_build
    initial_proof=verify_initial_build(TV,cf_segment)
    approved=[];physically_verified=[];family_unverified=[]
    approval=RES/'initial-cf-hardware-approved.json'
    approval_applied=approval.exists() and not BOOTSTRAP_DEMOS
    if approval_applied:
        permit=json.loads(approval.read_text(encoding='utf-8'))
        assert permit['segment_sha256']==sha(cf_segment.read_bytes()) and permit['transport_sha256']==sha(cf_transport.read_bytes())
        if permit.get('schema') == 2:
            from initial_release_proof import verify as verify_initial_release
            initial_release=verify_initial_release(TV,permit,cf_segment,cf_transport,RES/'demo-assets.zip')
        else:
            assert permit['physical_boot_audio_USB_verified'] and permit['memory_cache_IRQ_UID_reviewed']
        approved=permit['products'];assert approved and len(set(approved))==len(approved) and set(approved)<={2424833,2424834,2424835}
        physically_verified=permit['physically_verified_products'];family_unverified=permit['family_unverified_products']
        assert physically_verified and set(physically_verified).isdisjoint(family_unverified) and set(physically_verified)|set(family_unverified)==set(approved)
        for name,digest in permit['evidence'].items():
            evidence=(TV/name).resolve();assert evidence.is_relative_to(TV.resolve()) and sha(evidence.read_bytes())==digest,'Reviewed hardware evidence changed: '+name
    (RES/'InitialCFInfo.cs').write_text('namespace CFEditor { static class InitialCFInfo { public const string TransportHash="'+cf_info['component_sha256']+'"; public static readonly int[] ApprovedProducts=new int[]{'+','.join(map(str,approved))+'}; } }',encoding='utf-8')
    installer=TV/'CatalystIR/V3.0-NAM-USB-corrected/CatalystIR.exe'
    baseline_hash='5ea2edc9b8aec952a05c5c1b6832579eae02e82ee65e265d0a084171f8e51901'
    assert sha(installer.read_bytes())==baseline_hash,'The tested CatalystIR executable changed'
    installer_hash=baseline_hash;family_enabled=False
    validation=RES/'family-validated.json'
    if validation.exists():
        proof=json.loads(validation.read_text(encoding='utf-8'))
        installer=RES/'CatalystIRFamily.dll';installer_hash=proof['component_sha256']
        assert proof['host_fixtures_passed'] and proof['firmware_byte_identical'] and proof['CX60_backup_byte_identical']
        assert sha(installer.read_bytes())==installer_hash,'Family component changed; rerun its validation'
        assert proof['firmware_engine_sha256']==sha((ROOT.parent/'catalyst-nam/app/resources/nam-engine.ctf').read_bytes())
        for name,digest in proof['sources'].items():assert sha(legacy_input(ROOT,name,digest).read_bytes())==digest,'Validated family source changed: '+name
        family_enabled=True
    (RES/'EmbeddedFirmwareInfo.cs').write_text('namespace CFEditor { static class EmbeddedFirmwareInfo { public const string ComponentHash="'+installer_hash+'"; public const string BaselineHash="'+baseline_hash+'"; public const bool FamilyEnabled='+str(family_enabled).lower()+'; } }',encoding='utf-8')
    sources=list(ROOT.glob('*.cs'))+[RES/'AudioCodec.cs',RES/'GlassUI.cs',RES/'EmbeddedFirmwareInfo.cs',RES/'InitialCFInfo.cs',IR/'IRFilters.cs',IR/'IRLevels.cs',IR/'FFTBank.cs',ROOT.parent/'catalyst-nam/app/NamModel.cs']
    executable=OUT/('OfflineManagedChecks.exe' if MANAGED_CHECKS else 'CatalystCFEditor.exe')
    if MANAGED_CHECKS:sources.append(ROOT/'offline-checks/ManagedHarness.cs')
    args=[r'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe','/nologo','/utf8output','/platform:x86','/target:'+('exe' if MANAGED_CHECKS else 'winexe'),'/define:DEVELOPMENT','/main:CFEditor.'+('ManagedHarness' if MANAGED_CHECKS else 'Program'),'/out:'+str(executable),'/win32icon:'+str(RES/'catalyst-cf-editor-v2.ico')]
    args+=['/reference:'+x+'.dll' for x in ['System.Drawing','System.Windows.Forms','System.Web.Extensions','System.IO.Compression']]
    for path,name in [(RES/'ui-en.tsv','ui-en.tsv'),(RES/'catalog.json','catalog.json'),(RES/'editor-runtime.zip','editor-runtime.zip'),(RES/'catalyst-cf-editor-v2.ico','application.ico'),(IR/'assets/catalyst-cx60-front.png','amplifier.png'),(installer,'catalystir.embedded'),(RES/'knobs.zip','knobs.zip'),(RES/'tolex-black.png','tolex.png'),(cf_transport,'initial-cf.transport'),(cf_segment,'initial-cf.segment'),(ROOT.parent/'catalyst-nam/app/resources/nam-engine.ctf','initial-cf.base')]:
        args+=['/resource:'+str(path)+','+name]
    args+=list(map(str,sources))
    demos=RES/'demo-assets.zip'
    if demos.exists():
        import re
        expected=re.search(r'ResourceHash="([0-9a-f]{64})"',(ROOT/'DemoAssets.cs').read_text(encoding='utf-8')).group(1)
        assert sha(demos.read_bytes())==expected,'Demo bundle changed'
        args+=['/resource:'+str(demos)+',initial-cf.demos']
    elif not BOOTSTRAP_DEMOS:
        raise RuntimeError('Prepare the selected demo bundle first; --bootstrap-demos is a private offline preparation build only')
    result=subprocess.run(args,capture_output=True,text=True,encoding='utf-8',errors='replace')
    (OUT/'build.log').write_text(result.stdout+result.stderr,encoding='utf-8')
    if result.returncode: raise RuntimeError(result.stdout+result.stderr)
    report=dict(executable=str(executable),sha256=sha(executable.read_bytes()),offline_managed_harness=MANAGED_CHECKS,hardware_accessed=False,assets=len(data['assets']),parameters=sum(len(a['parameters']) for a in data['assets']),embedded_catalystir_sha256=installer_hash,baseline_catalystir_sha256=baseline_hash,family_component_enabled=family_enabled,editor_hardware_tested_models=[],firmware_base_previously_tested_models=[60],sources={str(p.relative_to(TV)):sha(p.read_bytes()) for p in sources})
    report['host_IR_importer_sha256']=sha((ROOT/'IRImport.cs').read_bytes())
    report['legacy_IR_importer_sha256']=sha((ROOT/'legacy-pinned-sources/IRImport.cs').read_bytes())
    report['demo_assets_sha256']=sha(demos.read_bytes()) if demos.exists() else None
    report['initial_cf_transport_sha256']=cf_info['component_sha256'];report['initial_cf_segment_sha256']=sha(cf_segment.read_bytes());report['initial_cf_approved_products']=approved
    report['initial_cf_physically_verified_products']=physically_verified;report['initial_cf_experimental_unverified_products']=family_unverified
    report['initial_cf_release_basis']=initial_release if approval_applied and permit.get('schema')==2 else None
    report['initial_cf_build_proof']=initial_proof
    (OUT/'BUILD.json').write_text(json.dumps(report,indent=2))
    print(json.dumps({k:v for k,v in report.items() if k!='sources'},indent=2))
if __name__=='__main__':
    import argparse
    parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path);parser.add_argument('--managed-checks',action='store_true');parser.add_argument('--bootstrap-demos',action='store_true')
    args=parser.parse_args()
    if args.managed_checks and not args.output:parser.error('--managed-checks requires a separate --output folder')
    if args.output:OUT=args.output.resolve()
    MANAGED_CHECKS=args.managed_checks
    BOOTSTRAP_DEMOS=args.bootstrap_demos
    main()
