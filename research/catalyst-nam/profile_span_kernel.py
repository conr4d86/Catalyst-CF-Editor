"""Offline contiguous-span M7 microkernel; no firmware/device access."""
import hashlib,json,math,sys,os
from pathlib import Path
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT.parent/'catalyst-editor/tools-recovery'))
import unicorn,capstone
import build,model,verify

def main():
    use_max='--vmax' in sys.argv
    folder=ROOT/('build-fast/span-max-r94' if use_max else 'build-fast/span-r93');folder.mkdir(exist_ok=True)
    protected=ROOT/'build-fast/selected-block-fma/kernel-embedded.c'
    before=hashlib.sha256(protected.read_bytes()).hexdigest()
    original=(ROOT/'kernel.c').read_text();body=(ROOT/'kernel_block_process.inc').read_text()
    start=body.index('            for(unsigned t=0;t<k;++t)');end=body.index('            const float *rw=',start)
    body=body[:start]+'''            for(unsigned t=0;t<k;++t){
                const float *cw=w+6+t*9;
                unsigned delay=(k-1-t)*d,old=delay<block?delay:block;
                unsigned pos=start>=delay?start-delay:start+len-delay;
                unsigned first=old<len-pos?old:len-pos;
                if(first)nam_accumulate_span(&a[0][0],history+pos*3,first,cw);
                if(old>first)nam_accumulate_span(&a[first][0],history,old-first,cw);
                if(block>old)nam_accumulate_span(&a[old][0],&x[old-delay][0],block-old,cw);
            }
'''+body[end:]
    body=body.replace('float x[32][3],hs[32][3],a[32][3];','float (*x)[3]=(float(*)[3])0x20065800u;\n        float (*hs)[3]=(float(*)[3])0x20065980u;\n        float (*a)[3]=(float(*)[3])0x20065b00u;')
    # ABI: r0/r1 contiguous 3-float rows, r2 count, r3 3x3 weights.
    # Only caller-saved r0-r3/s0-s14 are modified; no stack or extra scratch.
    # The nine FMAs retain the scalar tap/input accumulation order exactly.
    assembly='''
extern void nam_accumulate_span(float*,const float*,unsigned,const float*);
__asm__(
".syntax unified\\n.text\\n.thumb\\n.thumb_func\\n.global nam_accumulate_span\\n"
".type nam_accumulate_span,%function\\nnam_accumulate_span:\\n"
"cmp r2,#0\\nbeq 2f\\nvldmia r3!,{s0-s8}\\n1:\\n"
"vldmia r1!,{s9-s11}\\nvldmia r0,{s12-s14}\\n"
"vfma.f32 s12,s0,s9\\nvfma.f32 s13,s1,s9\\nvfma.f32 s14,s2,s9\\n"
"vfma.f32 s12,s3,s10\\nvfma.f32 s13,s4,s10\\nvfma.f32 s14,s5,s10\\n"
"vfma.f32 s12,s6,s11\\nvfma.f32 s13,s7,s11\\nvfma.f32 s14,s8,s11\\n"
"vstmia r0!,{s12-s14}\\nsubs r2,r2,#1\\nbne 1b\\n2: bx lr\\n"
".size nam_accumulate_span,.-nam_accumulate_span\\n");
'''
    if use_max:
        assembly+='''
static __attribute__((always_inline)) inline float nam_leaky(float x){
    float product,result;
    __asm__("vmul.f32 %0,%2,%3\\n\\tvmaxnm.f32 %1,%2,%0"
        : "=&t"(product),"=&t"(result):"t"(x),"t"(.01f));
    return result;
}
'''
        old='if(a0<0)a0*=.01f;if(a1<0)a1*=.01f;if(a2<0)a2*=.01f;'
        assert body.count(old)==1;body=body.replace(old,'a0=nam_leaky(a0);a1=nam_leaky(a1);a2=nam_leaky(a2);')
    (folder/'kernel-embedded.c').write_text(original.split('static void layer_constant')[0]+assembly+body)
    (folder/'kernel-preview.c').write_text(original.split('void nam_process(')[0]+assembly+body)
    build.OUT=folder
    build.run([build.ZIG,'cc','-target','thumb-freestanding-eabihf','-mcpu=cortex_m7+fp_armv8d16sp','-mfpu=fpv5-sp-d16','-mfloat-abi=hard','-O3','-ffp-contract=fast','-ffreestanding','-fno-builtin','-fno-stack-protector','-I',ROOT/'build','-c',folder/'kernel-preview.c','-o',folder/'kernel.o'],'compile.log')
    build.run([build.ZIG,'build-exe',folder/'kernel.o','-target','thumb-freestanding-eabihf','-mcpu','cortex_m7+fp_armv8d16sp','--script',ROOT/'lab.ld','-fentry=nam_process','-fno-compiler-rt','-O','ReleaseFast','-femit-bin='+str(folder/'kernel.elf')],'link.log')
    rows=[]
    paths=[ROOT/'external/NeuralAmpModelerCore/example_models/A2.nam']
    if os.environ.get('CATALYST_NAM_REFERENCE_FIXTURE'):paths.append(Path(os.environ['CATALYST_NAM_REFERENCE_FIXTURE']))
    for path in paths:
        _,weights=model.load(path);signal=verify.floats(verify.bytesf([.06*math.sin(i*.139)+.025*math.cos(i*.319) for i in range(8192)]))
        verify.OUT=ROOT/'build-fast/selected-block-fma';expected=verify.Arm(weights).process(signal)
        verify.OUT=folder;actual=verify.Arm(weights).process(signal);chunks=verify.Arm(weights).process(signal,chunks=(1,7,13,32,5,64),inplace=True)
        error=max(abs(a-b) for a,b in zip(actual,expected));chunkerror=max(abs(a-b) for a,b in zip(actual,chunks));assert verify.bytesf(actual)==verify.bytesf(expected) and verify.bytesf(actual)==verify.bytesf(chunks),(error,chunkerror)
        profiles=[]
        for out in (ROOT/'build-fast/selected-block-fma',folder):
            verify.OUT=out;m=verify.Arm(weights);m.process(signal[:2048]);m.u.ctl_flush_tb();count=[0]
            def tick(*args):count[0]+=1
            hook=m.u.hook_add(unicorn.UC_HOOK_CODE,tick);m.process(signal[2048:2080]);m.u.hook_del(hook);profiles.append(count[0])
        rows.append(dict(model=path.name,max_error=error,chunk_error=chunkerror,previous_instructions=profiles[0],span_instructions=profiles[1]))
    assert before==hashlib.sha256(protected.read_bytes()).hexdigest()
    segments,_=build.elf(folder/'kernel.elf');report=dict(passed=True,hardware_accessed=False,firmware_created=False,original_kernel_unchanged=True,text_bytes=sum(s['size'] for s in segments if s['flags']&1),source_sha256=hashlib.sha256((folder/'kernel-embedded.c').read_bytes()).hexdigest(),comparisons=rows)
    (folder/'result.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':main()
