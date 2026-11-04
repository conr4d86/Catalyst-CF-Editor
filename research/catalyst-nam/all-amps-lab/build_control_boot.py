"""Exact reset/owner/thunk verification for DSP-control reconciliation.

Outputs a noninstallable segment only. Does not access SDK, USB or hardware.
"""
import hashlib,json,os,subprocess,sys
from pathlib import Path
BASE=Path(__file__).resolve().parent;LAB=BASE/'control-owner-lab';NAM=BASE.parent
source=NAM/'asset-lab/verify_boot_layout.py'
sys.path[:0]=[str(NAM.parent/'catalyst-editor/tools-recovery'),str(NAM),str(source.parent)]
import unicorn,capstone
from build import elf
actorseg,actorsym=elf(LAB/'actor-build/actor.elf')
out=LAB/'thunk-build';out.mkdir(exist_ok=True)
assembly=''.join('.equ %s, %du\n'%(k,actorsym[v]['address']|1) for k,v in [('CF_STOCK_PREPARE_ADDRESS','cf_stock_prepare'),('CF_STOCK_OBSERVE_ADDRESS','cf_stock_observe')])+(LAB/'node_thunk.S').read_text()
(out/'node_thunk.S').write_text(assembly)
(out/'thunk.ld').write_text('ENTRY(cf_stock_set_thunk)\nSECTIONS { . = 0x17f00; .text : { *(.text*) *(.rodata*) } /DISCARD/ : { *(.ARM.exidx*) *(.ARM.extab*) *(.comment*) } }\n')
zig=NAM.parents[1]/'.tools/zig-cross/zig-windows-x86_64-0.13.0/zig.exe'
env=os.environ.copy();env['ZIG_GLOBAL_CACHE_DIR']=str(out/'global-cache');env['ZIG_LOCAL_CACHE_DIR']=str(out/'local-cache')
for name,cmd in [('assemble',[zig,'cc','-target','thumb-freestanding-eabihf','-mcpu=cortex_m7+fp_armv8d16sp','-c',out/'node_thunk.S','-o',out/'node_thunk.o']),('link',[zig,'build-exe',out/'node_thunk.o','-target','thumb-freestanding-eabihf','-mcpu','cortex_m7+fp_armv8d16sp','--script',out/'thunk.ld','-fentry=cf_stock_set_thunk','-fno-compiler-rt','-O','ReleaseSmall','-femit-bin='+str(out/'node_thunk.elf')])]:
 result=subprocess.run(list(map(str,cmd)),env=env,capture_output=True,text=True,timeout=120);(out/(name+'.log')).write_text(result.stdout+result.stderr);assert result.returncode==0,result.stdout+result.stderr
segments,symbols=elf(out/'node_thunk.elf');thunk=next(s for s in segments if s['flags']&1)
assert thunk['address']==0x17f00 and thunk['size']<=128
text=source.read_text(encoding='utf-8')
def change(old,new):
 global text
 assert text.count(old)==1,(old,text.count(old));text=text.replace(old,new)
change('ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent;IR=NAM.parent/\'catalyst-ir\';EDITOR=NAM.parent/\'catalyst-editor\'',
       'LAB=Path(__file__).resolve().parent/"control-owner-lab";ROOT=LAB.parent.parent/"asset-lab";NAM=ROOT.parent;IR=NAM.parent/"catalyst-ir";EDITOR=NAM.parent/"catalyst-editor"')
change("actor_folder=ROOT/('actor-owner-lab/actor-build' if options.owner_loop else 'actor-build')",'actor_folder=LAB/"actor-build"')
change("audio_folder=NAM/'dual-lab'/('lease-build' if options.owner_loop else 'build')",'audio_folder=LAB/"build"')
change("out=ROOT/('boot-owner-lab' if options.owner_loop else 'boot-build');out.mkdir(exist_ok=True)", 'out=LAB/"boot-build";out.mkdir(exist_ok=True)')
marker='    put(rows[6][0]+0x2e80,thunk)'
change(marker,marker+'\n    put(rows[6][0]+0x2f00,'+repr(thunk['data']+b'\xff'*(128-thunk['size']))+')')
marker='    flash=0x601dd000;put(flash,actorcode[\'data\']'
assert text.count(marker)==1
text=text.replace(marker,'''    main_wait=0x8000b9b0
    assert bytes(image[offset(main_wait):offset(main_wait)+4])==helper.bl(main_wait,0x8004c8f8)
    put(main_wait,helper.bl(main_wait,actorsym['cf_stock_main_wait']['address']));native_patches.append((main_wait,4))
    load_entry=0x80048d78
    assert bytes(image[offset(load_entry):offset(load_entry)+4])==bytes.fromhex('2de9f04f')
    branch=bytearray(helper.bl(load_entry,actorsym['cf_stock_model_load']['address']));branch[3]&=0xbf
    put(load_entry,bytes(branch));native_patches.append((load_entry,4))
'''+marker)
sys.argv=[str(Path(__file__)),'--owner-loop']
scope={'__name__':'main_control_boot_verifier','__file__':str(Path(__file__).resolve())}
exec(compile(text,str(source),'exec'),scope);scope['main']()
report_path=LAB/'boot-build/tests.json';report=json.loads(report_path.read_text())
report.update(dsp_control_owner_hook=True,no_native_graph_setter_in_audio=True,HELLO_capability_32_requires_DSP_hook=True,node_thunk_bytes=thunk['size'],control_diagnostic_opcode=14,control_diagnostic_words=21)
report_path.write_text(json.dumps(report,indent=2))
