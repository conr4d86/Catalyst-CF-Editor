"""Build a standalone emulator ELF and an official host comparator. No firmware."""
from pathlib import Path
import argparse, hashlib, json, subprocess, struct, os
import model

ROOT=Path(__file__).resolve().parent
TV=ROOT.parents[1]
ZIG=TV/'.tools/zig-cross/zig-windows-x86_64-0.13.0/zig.exe'
CORE=ROOT/'external/NeuralAmpModelerCore'
OUT=ROOT/'build'

def run(args, log):
    env=os.environ.copy()
    env['ZIG_GLOBAL_CACHE_DIR']=str(OUT/'zig-global-cache')
    env['ZIG_LOCAL_CACHE_DIR']=str(OUT/'zig-local-cache')
    result=subprocess.run([str(a) for a in args],cwd=ROOT,capture_output=True,text=True,env=env)
    (OUT/log).write_text(result.stdout+result.stderr,encoding='utf-8')
    if result.returncode:
        raise RuntimeError(f'{log}: '+result.stderr[-5000:])

def elf(path):
    data=Path(path).read_bytes()
    assert data[:6]==b'\x7fELF\x01\x01'
    h=struct.unpack_from('<16sHHIIIIIHHHHHH',data)
    assert h[2]==40, 'Expected ARM ELF'
    segments=[]
    for i in range(h[10]):
        p=struct.unpack_from('<8I',data,h[5]+i*h[9])
        if p[0]==1: segments.append(dict(address=p[2],size=p[5],data=data[p[1]:p[1]+p[4]],flags=p[6]))
    sh=[struct.unpack_from('<10I',data,h[6]+i*h[11]) for i in range(h[12])]
    symbols={}
    for s in sh:
        if s[1]!=2: continue
        names=sh[s[6]]; strings=data[names[4]:names[4]+names[5]]
        for off in range(s[4],s[4]+s[5],s[9]):
            n,value,size,info,other,idx=struct.unpack_from('<IIIBBH',data,off)
            name=strings[n:].split(b'\0')[0].decode()
            if name: symbols[name]=dict(address=value,size=size,info=info,index=idx)
    return segments,symbols

def build_arm():
    OUT.mkdir(exist_ok=True)
    g=model.geometry()
    text='#pragma once\n#define N_LAYERS 23\n#define N_WEIGHTS 1871\n'
    for macro,key in [('HISTORY_FLOATS','history_floats'),('HEAD_RING_OFFSET','head_ring_offset'),
                      ('HEAD_WEIGHT_OFFSET','head_weight_offset')]: text+=f'#define {macro} {g[key]}\n'
    for key in ['kernels','dilations','ring_offset','ring_length','weight_offset']:
        text+=f'static const unsigned {key}[23] = '+'{'+','.join(map(str,g[key]))+'};\n'
    (OUT/'geometry.h').write_text(text)
    args=[ZIG,'cc','-target','thumb-freestanding-eabihf','-mcpu=cortex_m7+fp_armv8d16sp',
          '-mfpu=fpv5-sp-d16','-mfloat-abi=hard','-O2','-ffp-contract=off',
          '-ffreestanding','-fno-builtin','-fno-stack-protector','-c',
          '-I',OUT,ROOT/'kernel.c','-o',OUT/'kernel.o']
    run(args,'arm-compile.log')
    link=[ZIG,'build-exe',OUT/'kernel.o','-target','thumb-freestanding-eabihf',
          '-mcpu','cortex_m7+fp_armv8d16sp','--script',ROOT/'lab.ld','-fentry=nam_process',
          '-fno-compiler-rt','-O','ReleaseFast','-femit-bin='+str(OUT/'kernel.elf')]
    run(link,'arm-build.log')
    segments,symbols=elf(OUT/'kernel.elf')
    assert all(name in symbols for name in ['nam_reset','nam_process','nam_state_bytes']), list(symbols)
    assert any(s['flags']&1 for s in segments), 'Missing executable segment'
    unresolved=[name for name,s in symbols.items() if s['index']==0 and s['info']>>4 in (1,2)]
    assert not unresolved,unresolved
    report=dict(status='EMULATOR_ELF_ONLY_NOT_FIRMWARE',geometry=g,
        sha256=hashlib.sha256((OUT/'kernel.elf').read_bytes()).hexdigest(),
        sections=[{k:v for k,v in s.items() if k!='data'} for s in segments],
        symbols={k:v for k,v in symbols.items() if k.startswith(('nam_','catalyst_nam_'))},
        compiler=str(ZIG),unresolved_symbols=unresolved,command=[str(a) for a in args],
        link_command=[str(a) for a in link],
        hardware_accessed=False,firmware_created=False,
        memory_placement='Emulator addresses only; no reservation in Catalyst firmware')
    (OUT/'arm-build.json').write_text(json.dumps(report,indent=2))
    return report

def build_reference():
    files=list((CORE/'NAM').glob('*.cpp'))+list((CORE/'NAM/wavenet').glob('*.cpp'))
    args=[ZIG,'c++','-std=c++20','-O2','-ffp-contract=off','-DNAM_SAMPLE_FLOAT',
          '-DNAM_ENABLE_A2_FAST','-DNAM_DEFAULT_MAX_BUFFER_SIZE=32','-DNOMINMAX',
          '-I',CORE,'-I',CORE/'Dependencies/eigen','-I',CORE/'Dependencies/nlohmann',
          ROOT/'reference.cpp',*files,'-o',OUT/'reference.exe']
    run(args,'reference-build.log')
    return dict(status='BUILT_HOST_REFERENCE_ONLY',sha256=hashlib.sha256((OUT/'reference.exe').read_bytes()).hexdigest())

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--reference',action='store_true');a=p.parse_args()
    print(json.dumps(build_reference() if a.reference else build_arm(),indent=2))
