"""Offline staged pedal checks: circuit response, controls, ARM ABI and cost.

No SDK/device import. Does not select this source in the firmware builder.
The independent oracle solves the ideal tone network as complex node currents.
Nonlinear clipping remains an approximation, not measured POD Go fidelity.
"""
import cmath
import ctypes as C
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import random
import struct
import subprocess
import sys

ROOT=Path(__file__).resolve().parent
BOOST=ROOT.parent
NAM=BOOST.parent
TV=NAM.parents[1]
OUT=ROOT/'build'
sys.path[:0]=[str(TV/'research/catalyst-editor/tools-recovery'),str(NAM)]
spec=importlib.util.spec_from_file_location('pedal_abi_checks',BOOST/'verify_lab.py')
lab=importlib.util.module_from_spec(spec);spec.loader.exec_module(lab)
from unicorn import UC_HOOK_CODE
from unicorn.arm_const import UC_ARM_REG_SP
lab.ROOT=ROOT;lab.OUT=OUT
FS=48000

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def db(x):return 20*math.log10(max(abs(x),1e-30))
def amplitude(y,hz):return lab.amplitude(y,hz)

def tone_oracle(mode,tone,s):
    # Nodal circuit solution, independently from the biquad polynomials in C.
    # vp=vn=1. Solve the pot-wiper current; derive output and source voltage.
    rs=1000 if mode==2 else 10000
    rf=rs
    cp=220e-9 if mode==2 else 18e-9
    c=220e-9 if mode==2 else 27e-9
    rg=220 if mode==2 else 470
    cf=0 if mode==2 else 10e-9
    a=20000*tone;b=20000-a
    if a==0: w=1;ia=1/(rg+1/(s*c));ib=0
    elif b==0:w=1;ia=0;ib=1/(rg+1/(s*c))
    else:
        w=(1/a+1/b)/(1/a+1/b+1/(rg+1/(s*c)))
        ia=(1-w)/a;ib=(1-w)/b
    vp=1
    source=vp+rs*(s*cp*vp+ia+(vp/10000 if mode==2 else 0))
    output=vp+ib/(1/rf+s*cf)
    return output/source

def linear_oracle(mode,drive,tone,hz,boost=0,bright=0):
    z=cmath.exp(-2j*math.pi*hz/FS)
    s=2*FS*(1-z)/(1+z)
    avg=(1+z)/2
    dc=(1-z)/(1-.998953374*z)
    hp=lambda rc:s*rc/(1+s*rc)
    if mode==1:
        gain=1.41253754+(10-1.41253754)*drive*drive
        factor=.25+3*tone*tone
        rc=(1000 if bright else 16000)*3.3e-9/factor
        return hp(47e-9*1000000)*gain*avg/(1+s*rc)*(1 if boost else .70794578)*dc
    rc1=.022e-6*510000 if mode==2 else .047e-6*470000
    rc2=1e-6*10000 if mode==2 else .018e-6*100000
    rf=51000+500000*drive if mode==2 else 33000+1000000*drive
    feedback=rf/4700*hp(4700*47e-9)
    if mode==2:feedback/=1+s*rf*51e-12
    load=.99009901 if mode==2 else .68027211
    return hp(rc1)*hp(rc2)*avg*(1+feedback)*tone_oracle(mode,tone,s)*load*dc

class Host:
    def __init__(self,variant='adaa'):
        OUT.mkdir(parents=True,exist_ok=True)
        wrapper=OUT/'host.c'
        wrapper.write_text('''#include "preamp.c"
__declspec(dllexport) unsigned size(void){return sizeof(PreampState);}
__declspec(dllexport) void init(PreampState*s){preamp_init(s);}
__declspec(dllexport) int configure(PreampState*s,const PreampConfig*c){return preamp_configure(s,c);}
__declspec(dllexport) void process(PreampState*s,const float*i,float*o,unsigned n){preamp_process(s,i,o,n);}
''')
        zig=TV/'.tools/zig-cross/zig-windows-x86_64-0.13.0/zig.exe'
        env=os.environ.copy();env['ZIG_LOCAL_CACHE_DIR']=str(OUT/'zig-local-cache');env['ZIG_GLOBAL_CACHE_DIR']=str(OUT/'zig-global-cache')
        dll=OUT/(variant+'.dll')
        args=[str(zig),'cc','-shared','-O2','-ffp-contract=off','-I',str(ROOT),str(wrapper),'-o',str(dll)]
        if variant=='direct':args+=['-DPREAMP_NO_ADAA']
        r=subprocess.run(args,capture_output=True,text=True,env=env)
        (OUT/(variant+'-build.log')).write_text(r.stdout+r.stderr)
        if r.returncode:raise RuntimeError(r.stderr)
        lib=self.lib=C.CDLL(str(dll));lib.size.restype=C.c_uint32
        lib.init.argtypes=[C.c_void_p];lib.configure.argtypes=[C.c_void_p,C.POINTER(lab.Config)]
        lib.process.argtypes=[C.c_void_p,C.POINTER(C.c_float),C.POINTER(C.c_float),C.c_uint32]
        self.state_bytes=lib.size()
    def process(self,signal,mode,drive=.5,tone=.5,level=1,boost=0,bright=0):
        state=C.create_string_buffer(self.state_bytes);self.lib.init(state)
        cfg=lab.Config(mode,1,boost,bright,drive,tone,level)
        assert self.lib.configure(state,C.byref(cfg))==0
        zeros=(C.c_float*2048)();self.lib.process(state,zeros,zeros,2048)
        x=(C.c_float*len(signal))(*signal);y=(C.c_float*len(signal))()
        self.lib.process(state,x,y,len(signal));out=list(y)
        assert all(math.isfinite(v) for v in out)
        return out

def main():
    protected=[BOOST/'preamp.c',BOOST/'preamp.h',NAM/'dual-lab/lease-build/combined.elf',
               TV/'CatalystEditor/Revision-1.0/CatalystCFEditor.exe',NAM/'build-fast/span-max-r94/kernel-embedded.c']
    before={str(p.relative_to(TV)):sha(p) for p in protected}
    host=Host();assert host.state_bytes==164
    rows=[]
    for mode in (1,2,3):
        for drive,tone in ((0,0),(.5,.5),(1,1),(1,0),(0,1)):
            for hz in (80,200,400,1000,4000,8000):
                a=1e-6
                x=[a*math.sin(2*math.pi*hz*i/FS) for i in range(36000)]
                y=host.process(x,mode,drive,tone)[12000:]
                measured=db(amplitude(y,hz)/a);expected=db(linear_oracle(mode,drive,tone,hz))
                error=measured-expected
                rows.append(dict(mode=mode,drive=drive,tone=tone,hz=hz,measured_db=measured,expected_db=expected,error_db=error))
                assert abs(error)<.12,rows[-1]
    print('Independent circuit response checked:',len(rows),'points; max error dB',max(abs(r['error_db']) for r in rows),flush=True)
    # Output attenuator has exactly 3dB action even when EP is driven hard.
    ep=[]
    for drive in (0,.5,1):
        x=[.2*math.sin(2*math.pi*400*i/FS) for i in range(12000)]
        off=host.process(x,1,drive,boost=0)[6000:];on=host.process(x,1,drive,boost=1)[6000:]
        error=max(abs(a-.70794578*b) for a,b in zip(off,on))
        assert error<2e-6,error
        ep.append(dict(drive=drive,output_switch_gain_db=db(amplitude(on,400)/amplitude(off,400)),shape_max_error=error))
    # The new tonal personalities differ before any clipping or level boost.
    differences=[]
    for hz in (80,200,400,1000,4000,8000):
        a=next(r['measured_db'] for r in rows if r['mode']==2 and r['drive']==.5 and r['tone']==.5 and r['hz']==hz)
        b=next(r['measured_db'] for r in rows if r['mode']==3 and r['drive']==.5 and r['tone']==.5 and r['hz']==hz)
        differences.append(dict(hz=hz,sd_minus_ts_db=b-a))
    # Remove the absolute level at 1k: verify EQ, not just a volume difference.
    mid=next(x['sd_minus_ts_db'] for x in differences if x['hz']==1000)
    separation=max(abs(r['sd_minus_ts_db']-mid) for r in differences)
    assert separation>3,separation
    print('808/SD-1 normalized EQ separation dB:',separation,flush=True)
    direct=Host('direct');alias=[]
    for mode in (1,2,3):
        x=[.8*math.sin(2*math.pi*7000*i/FS) for i in range(9600)]
        a=host.process(x,mode,1,1)[4800:];b=direct.process(x,mode,1,1)[4800:]
        ar=amplitude(a,13000)/max(amplitude(a,7000),1e-30)
        br=amplitude(b,13000)/max(amplitude(b,7000),1e-30)
        alias.append(dict(mode=mode,folded_13k_dbc=db(ar),direct_13k_dbc=db(br),improvement_db=db(br/ar)))
    segments,symbols=lab.compile_lab();machine=lambda:lab.Machine(segments,symbols)
    rng=random.Random(8081);signal=lab.floats(lab.packed([rng.uniform(-1.2,1.2) for _ in range(1024)]))
    bypass=machine();assert lab.packed(bypass.process(signal))==lab.packed(signal)
    original=bytes(bypass.u.mem_read(lab.STATE,bypass.size))
    for mode,drive,tone,level in ((4,.5,.5,1),(2,float('nan'),.5,1),(2,.5,float('inf'),1),(2,.5,.5,3)):
        assert bypass.configure(mode,drive=drive,tone=tone,level=level)==0xffffffff
        assert bytes(bypass.u.mem_read(lab.STATE,bypass.size))==original
    bypass.call('preamp_process',lab.STATE,lab.INPUT,lab.OUTPUT,0)
    assert bytes(bypass.u.mem_read(lab.STATE,bypass.size))==original
    control_cases=[]
    for mode in range(4):
        for drive,tone,level in ((0,0,0),(0,1,1),(.5,.5,1),(1,0,1),(1,1,2)):
            m=machine();assert m.configure(mode,drive=drive,tone=tone,level=level)==0
            y=m.process(signal)
            other=machine();assert other.configure(mode,drive=drive,tone=tone,level=level)==0
            assert lab.packed(y)==lab.packed(other.process(signal,chunks=(1,7,32,13,64),inplace=True))
            peak=max(map(abs,y));assert peak<8,(mode,drive,tone,peak)
            assert m.configure(mode,enabled=0)==0
            assert lab.packed(m.process(signal[:256])[128:])==lab.packed(signal[128:256])
            control_cases.append(dict(mode=mode,drive=drive,tone=tone,level=level,peak=peak,chunk_inplace_exact=True))
    # Host and ARM execute the same source to close the analysis/compiler gap.
    arm_host=[]
    for mode in (1,2,3):
        m=machine();assert m.configure(mode)==0;m.process([0.0]*2048)
        a=m.process(signal);b=host.process(signal,mode)
        error=max(abs(x-y) for x,y in zip(a,b));assert error<2e-5,(mode,error)
        arm_host.append(dict(mode=mode,max_abs_error=error))
        m=machine();assert m.configure(mode)==0
        assert m.process([0,float('nan'),.5,-.5])[1:]==[0,0,0]
    # Preserve the existing transparent Boost and level laws exactly.
    m=machine();assert m.configure(0,drive=1)==0
    assert m.process([.125]*256)[128:]==[.25]*128
    for mode in (1,2,3):
        a=machine();b=machine()
        assert a.configure(mode,level=1)==b.configure(mode,level=2)==0
        ay,by=a.process(signal),b.process(signal)
        assert max(abs(y-2*x) for x,y in zip(ay[128:],by[128:]))<2e-6
    # Tone/Drive/Bright/Boost changes may not create unbounded filter state.
    # The fixture does not establish audible click-free operation on hardware.
    transitions=[]
    for mode in (1,2,3):
        m=machine();peak=0
        for j in range(30):
            assert m.configure(mode,drive=(j%3)/2,tone=(j%2),level=1,
                               bright=j%2,boost=(j//2)%2)==0
            audio=m.process([.08*math.sin((j*64+i)*.127) for i in range(64)])
            peak=max(peak,max(map(abs,audio)))
        assert peak<8,(mode,peak)
        transitions.append(dict(mode=mode,changes=30,peak=peak,bounded=True))
    # Constant input must decay through DC blockers, including asym clipping.
    dc=[]
    for mode in (1,2,3):
        y=host.process([.25]*96000,mode,1,1)
        peak=max(map(abs,y[-1024:]));assert peak<2e-5,(mode,peak)
        dc.append(dict(mode=mode,tail_peak=peak))
    profiles=[]
    for mode in range(4):
        m=machine();assert m.configure(mode,drive=1)==0
        count=[0];stack=[lab.STACK]
        def hook(u,address,size,user):
            count[0]+=1;stack[0]=min(stack[0],u.reg_read(UC_ARM_REG_SP))
        handle=m.u.hook_add(UC_HOOK_CODE,hook);m.process(signal[:32]);m.u.hook_del(handle)
        profiles.append(dict(mode=mode,instructions_per_32=count[0],stack_fixture_bytes=lab.STACK-stack[0]))
    assert before=={str(p.relative_to(TV)):sha(p) for p in protected}
    text_bytes=sum(s['size'] for s in segments if s['flags']&1)
    report=dict(status='PASS_ISOLATED_CIRCUIT_INFORMED_PROTOTYPE',hardware_accessed=False,
                SDK_loaded=False,firmware_created=False,editor_replaced=False,
                physically_validated=False,POD_Go_audio_matched=False,
                protected_unchanged=True,protected_sha256=before,
                state_bytes=host.state_bytes,text_and_rodata_bytes=text_bytes,
                source_sha256=sha(ROOT/'preamp.c'),elf_sha256=sha(OUT/'preamp.elf'),
                linear_response=rows,ep_output_switch=ep,sd_ts_response_delta=differences,
                level_matched_tonal_separation_db=separation,alias_spot_check=alias,
                control_cases=control_cases,arm_host=arm_host,instruction_profiles=profiles,
                parameter_transitions=transitions,dc_tests=dc,
                limitations=['Clipping is a cubic approximation, not a full diode/FET circuit solve',
                             'Two volts per digital unit is uncalibrated; pot taper is linear in resistance',
                             'No POD Go processed reference recording; no claim of exact pedal fidelity',
                             'Instruction counts are not hardware cycles or realtime proof',
                             'Single-frequency alias check is not a full aliasing validation'])
    (OUT/'result.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({k:report[k] for k in ('status','state_bytes','text_and_rodata_bytes','instruction_profiles','alias_spot_check','protected_unchanged')},indent=2),flush=True)

if __name__=='__main__':main()
