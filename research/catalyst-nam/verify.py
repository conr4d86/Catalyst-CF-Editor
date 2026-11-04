"""Run the compiled ARM kernel on M7 ISA and compare official NAM output.

No clock/scheduler/peripheral emulation. This is NOT a real-time benchmark.
The memory addresses below are private emulator fixtures, NOT firmware patches.
"""
from pathlib import Path
import copy, hashlib, json, math, random, struct, subprocess, sys
from build import ROOT, OUT, CORE, elf
import model
sys.path.insert(0,str(ROOT.parent/'catalyst-ir/tools/python'))
from unicorn import Uc, UC_ARCH_ARM, UC_MODE_THUMB, UC_MODE_MCLASS, UC_HOOK_CODE
from unicorn.arm_const import *

S=0x20020040; W=0x20010040; IN=0x80000040; OUTPUT=0x80010040
STACK=0x20067ff0; STOP=0x17ff0
EVIDENCE=ROOT/'evidence'

def floats(data): return list(struct.unpack('<%df'%(len(data)//4),data))
def bytesf(values): return struct.pack('<%df'%len(values),*values)

class Arm:
    def __init__(self,weights):
        self.u=u=Uc(UC_ARCH_ARM, UC_MODE_THUMB|UC_MODE_MCLASS)
        u.ctl_set_cpu_model(UC_CPU_ARM_CORTEX_M7)
        u.mem_map(0,0x18000);u.mem_map(0x20000000,0x68000);u.mem_map(0x80000000,0x30000)
        segments,self.symbols=elf(OUT/'kernel.elf')
        for seg in segments:
            assert seg['address']>=0x10000 and seg['address']+seg['size']<STOP
            u.mem_write(seg['address'],seg['data'])
        u.reg_write(UC_ARM_REG_C1_C0_2,0xf<<20);u.reg_write(UC_ARM_REG_FPEXC,0x40000000)
        self.blob=model.pack(weights);u.mem_write(W,self.blob)
        self.state_bytes=model.geometry()['state_bytes']
        u.mem_write(S-64,b'\xa5'*64);u.mem_write(S+self.state_bytes,b'\x5a'*64)
        self.call('nam_reset',S,W)
        assert self.call('nam_state_bytes')==self.state_bytes

    def call(self,name,*args,count=200000000):
        u=self.u; u.reg_write(UC_ARM_REG_SP,STACK);u.reg_write(UC_ARM_REG_LR,STOP|1)
        for reg,value in zip([UC_ARM_REG_R0,UC_ARM_REG_R1,UC_ARM_REG_R2,UC_ARM_REG_R3],args[:4]):
            u.reg_write(reg,value)
        if len(args)>4:u.mem_write(STACK,struct.pack('<I',args[4]))
        saved=[UC_ARM_REG_R4,UC_ARM_REG_R5,UC_ARM_REG_R6,UC_ARM_REG_R7,
               UC_ARM_REG_R8,UC_ARM_REG_R9,UC_ARM_REG_R10,UC_ARM_REG_R11]
        sentinels=[0xabcd0000+i for i in range(len(saved))]
        for r,v in zip(saved,sentinels):u.reg_write(r,v)
        fp_saved=[UC_ARM_REG_S16,UC_ARM_REG_S17,UC_ARM_REG_S18,UC_ARM_REG_S19,
                  UC_ARM_REG_S20,UC_ARM_REG_S21,UC_ARM_REG_S22,UC_ARM_REG_S23,
                  UC_ARM_REG_S24,UC_ARM_REG_S25,UC_ARM_REG_S26,UC_ARM_REG_S27,
                  UC_ARM_REG_S28,UC_ARM_REG_S29,UC_ARM_REG_S30,UC_ARM_REG_S31]
        for i,r in enumerate(fp_saved):u.reg_write(r,0x3f000000+i)
        u.emu_start(self.symbols[name]['address']|1,STOP,count=count)
        assert u.reg_read(UC_ARM_REG_PC)==STOP, 'Instruction budget reached'
        assert u.reg_read(UC_ARM_REG_SP)==STACK
        assert [u.reg_read(r) for r in saved]==sentinels, 'AAPCS callee-saved GPR corruption'
        assert [u.reg_read(r) for r in fp_saved]==[0x3f000000+i for i in range(16)], 'AAPCS VFP corruption'
        assert bytes(u.mem_read(S-64,64))==b'\xa5'*64
        assert bytes(u.mem_read(S+self.state_bytes,64))==b'\x5a'*64
        assert bytes(u.mem_read(W,len(self.blob)))==self.blob
        return u.reg_read(UC_ARM_REG_R0)

    def process(self,signal,chunks=(32,),inplace=False):
        output=[]; n=0; part=0
        while n<len(signal):
            size=min(chunks[part%len(chunks)],len(signal)-n);part+=1
            self.u.mem_write(IN,bytesf(signal[n:n+size]));target=IN if inplace else OUTPUT
            self.call('nam_process',S,W,IN,target,size)
            output+=floats(bytes(self.u.mem_read(target,size*4)));n+=size
        return output

def rejection_tests(selected):
    mutations={
        'wrong_rate':lambda d:d.update(sample_rate=44100),
        'nonfinite_weights':lambda d:d['weights'].__setitem__(3,float('nan')),
        'float32_overflow':lambda d:d['weights'].__setitem__(3,1e100),
        'wrong_count':lambda d:d['weights'].pop(),
        'gating':lambda d:d['config']['layers'][0].update(gating_mode=['gated']*23),
        'wrong_channels':lambda d:d['config']['layers'][0].update(channels=8),
        'different_dilation':lambda d:d['config']['layers'][0]['dilations'].__setitem__(6,240),
        'different_activation':lambda d:d['config']['layers'][0]['activation'][0].update(type='Tanh'),
        'conditioning_dsp':lambda d:d['config'].update(condition_dsp={'architecture':'Linear'}),
        'film':lambda d:d['config']['layers'][0].update(conv_pre_film={'active':True}),
        'post_head':lambda d:d['config'].update(head={'channels':1}),
        'residual_groups':lambda d:d['config']['layers'][0].update(layer1x1={'active':True,'groups':3}),
        'missing_rate':lambda d:d.pop('sample_rate'),
    }
    passed=[]
    for name,mutate in mutations.items():
        d=copy.deepcopy(selected);mutate(d);p=OUT/(name+'.nam');p.write_text(json.dumps(d))
        try:model.load(p)
        except ValueError:passed.append(name)
        else:raise AssertionError('Importer admitted '+name)
    return passed

def verify(source=None, label='kernel'):
    EVIDENCE.mkdir(exist_ok=True)
    original=Path(source) if source else CORE/'example_models/A2.nam'; m,w=model.load(original)
    original_hash=hashlib.sha256(original.read_bytes()).hexdigest()
    # Save just the selected model as a test input; metadata is official fixture data.
    fixture=OUT/'selected-official-lite.nam';fixture.write_text(json.dumps(m))
    rng=random.Random(11012026)
    signal=[0.0]*64+[1.0]+[0.0]*63
    signal += [0.08*math.sin(i*.139)+rng.uniform(-.025,.025) for i in range(8192-len(signal))]
    signal=floats(bytesf(signal));(OUT/'input.f32').write_bytes(bytesf(signal))
    subprocess.run([str(OUT/'reference.exe'),str(fixture),str(OUT/'input.f32'),str(OUT/'official.f32')],check=True)
    expected=floats((OUT/'official.f32').read_bytes());assert len(expected)==len(signal)
    a=Arm(w);actual=a.process(signal)
    (OUT/'arm.f32').write_bytes(bytesf(actual))
    error=max(abs(x-y) for x,y in zip(actual,expected))
    rms=math.sqrt(sum((x-y)**2 for x,y in zip(actual,expected))/len(actual))
    assert all(math.isfinite(x) for x in actual)
    assert error<2e-5,(error,rms)
    # Ring wrap in > 6347 samples, non-multiple chunks, same input/output buffer.
    b=Arm(w);chunks=b.process(signal,chunks=(1,7,13,32,5,64),inplace=True)
    assert bytesf(chunks)==bytesf(actual), 'Chunk/in-place output changed'
    a.call('nam_reset',S,W);again=a.process(signal[:256]);assert bytesf(again)==bytesf(actual[:256])
    before=bytes(a.u.mem_read(S,a.state_bytes));a.call('nam_process',S,W,IN,OUTPUT,0)
    assert bytes(a.u.mem_read(S,a.state_bytes))==before
    # Count one executed 32-sample call. Instructions are not clock cycles.
    # Attach before the first process call in a fresh emulator. Adding a code
    # hook after execution can miss existing translated blocks in Unicorn.
    prof=Arm(w)
    instructions=0;minsp=STACK
    def track(uc,address,size,user):
        nonlocal instructions,minsp
        instructions+=1;minsp=min(minsp,uc.reg_read(UC_ARM_REG_SP))
    h=prof.u.hook_add(UC_HOOK_CODE,track);prof.process(signal[128:160]);prof.u.hook_del(h)
    rejected=rejection_tests(m)
    report=dict(status='PASS_OFFLINE_M7_ISA_AND_OFFICIAL_REFERENCE',scope=__doc__,
        samples=len(signal),block_samples=32,sample_rate=48000,channels=3,weights=1871,
        model_source_sha256=original_hash,model_source_name=original.name,
        selected_architecture='A2 Lite, 3 channels (not the 8-channel Full submodel)',
        arm_elf_sha256=hashlib.sha256((OUT/'kernel.elf').read_bytes()).hexdigest(),
        max_abs_error=error,rms_error=rms,output_peak=max(map(abs,actual)),
        exact_inplace_and_varying_chunks=True,reset_reproducible=True,zero_count_preserves_state=True,
        callee_saved_integer_and_vfp_registers_verified=True,state_guards_intact=True,
        weights_unchanged=True,import_rejections=rejected,geometry=model.geometry(),
        instruction_profile_32_samples=instructions,stack_highwater_fixture_bytes=STACK-minsp,
        profile_hook_attached_before_first_process=True,
        hardware_accessed=False,firmware_created=False,real_time_validated=False,
        limitations=['M7 ISA emulator does not measure cycles, DMA, caches, interrupts or scheduler.',
                    'No placement or reservation within Catalyst firmware has been applied.',
                    'Input/output calibration and amplifier graph hook on the physical unit remain unvalidated.'])
    assert hashlib.sha256(original.read_bytes()).hexdigest()==original_hash
    report['source_unchanged']=True
    (EVIDENCE/(label+'-verification.json')).write_text(json.dumps(report,indent=2))
    print(json.dumps({k:v for k,v in report.items() if k not in ['geometry','scope']},indent=2))
    return report

if __name__=='__main__':
    import argparse
    p=argparse.ArgumentParser();p.add_argument('--model',type=Path);p.add_argument('--label',default='kernel')
    args=p.parse_args()
    if not args.label.replace('-','').isalnum():raise ValueError('Invalid report label')
    verify(args.model,args.label)
