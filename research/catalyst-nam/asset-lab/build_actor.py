"""Compile the native integration LAB. Produces ELF only, never CTF or USB IO."""
import hashlib,json,os,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent;TV=NAM.parents[1]
sys.path.insert(0,str(NAM))
from build import elf
def main():
    out=ROOT/'actor-build';out.mkdir(exist_ok=True)
    linker=out/'actor.ld';linker.write_text('ENTRY(cf_fs_dispatch)\nSECTIONS { . = 0x80874000; .text : { *(.text*) *(.rodata*) } . = 0x80860000; .bss (NOLOAD) : { *(.bss*) *(COMMON) } /DISCARD/ : { *(.ARM.exidx*) *(.ARM.extab*) *(.comment*) } }\n')
    env=os.environ.copy();env['ZIG_GLOBAL_CACHE_DIR']=str(out/'global-cache');env['ZIG_LOCAL_CACHE_DIR']=str(out/'local-cache')
    zig=TV/'.tools/zig-cross/zig-windows-x86_64-0.13.0/zig.exe';objects=[]
    sources=[ROOT/n for n in ('asset_store.c','asset_wire.c','asset_load.c','ir_handoff.c','asset_actor.c','receive_wrapper.S')]
    for i,source in enumerate(sources):
        obj=out/f'{i}.o';objects.append(obj)
        args=[zig,'cc','-target','thumb-freestanding-eabihf','-mcpu=cortex_m7+fp_armv8d16sp','-Os','-ffreestanding','-fno-builtin','-fno-stack-protector','-Wall','-Wextra','-Werror','-I',ROOT,'-c',source,'-o',obj]
        p=subprocess.run(list(map(str,args)),capture_output=True,text=True,env=env,timeout=120);(out/f'compile-{i}.log').write_text(p.stdout+p.stderr);assert p.returncode==0,p.stdout+p.stderr
    args=[zig,'build-exe',*objects,'-target','thumb-freestanding-eabihf','-mcpu','cortex_m7+fp_armv8d16sp','--script',linker,'-fentry=cf_fs_dispatch','--no-gc-sections','-fno-strip','-fno-compiler-rt','-O','ReleaseSmall','-femit-bin='+str(out/'actor.elf')]
    p=subprocess.run(list(map(str,args)),capture_output=True,text=True,env=env,timeout=120);(out/'link.log').write_text(p.stdout+p.stderr);assert p.returncode==0,p.stdout+p.stderr
    segments,symbols=elf(out/'actor.elf');assert not [n for n,s in symbols.items() if s['index']==0 and s['info']>>4 in (1,2)]
    text=next(s for s in segments if s['flags']&1);bss=next(s for s in segments if s['address']==0x80860000)
    assert text['address']+text['size']<=0x80878000 and bss['size']<=4096
    result=dict(compiled=True,hardware_accessed=False,firmware_created=False,elf_sha256=hashlib.sha256((out/'actor.elf').read_bytes()).hexdigest(),text_bytes=text['size'],bss_bytes=bss['size'],addresses_are_emulator_reservations=True,
      source_sha256={str(p.name):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources+[ROOT/'asset_store.h',ROOT/'asset_wire.h',ROOT/'asset_load.h',ROOT/'ir_handoff.h',ROOT/'build_actor.py',linker]})
    (out/'build.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
if __name__=='__main__':main()
