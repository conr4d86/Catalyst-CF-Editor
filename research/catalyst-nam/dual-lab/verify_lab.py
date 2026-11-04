"""Exercise the combined ARM ELF with synthetic graph/buffer fixtures only.

The M7 instructions execute in Unicorn. DWT is scripted, not a clock or USB
benchmark. No native firmware boot, CTF or physical storage is involved.
"""
import ctypes as C,hashlib,json,math,struct,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent;EDITOR=NAM.parent/'catalyst-editor'
sys.path[:0]=[str(EDITOR/'tools/emulation'),str(NAM)]
import unicorn
from unicorn import Uc,UC_ARCH_ARM,UC_MODE_THUMB,UC_MODE_MCLASS,UC_HOOK_MEM_READ,UC_HOOK_CODE
from unicorn.arm_const import *
from build import elf
import model
from verify import Arm,S as REF_STATE
from trial_build import AutoRuntime
sys.path.insert(0,str(NAM/'boost-lab'))
import verify_lab as pedal_lab
U=C.c_uint32;F=C.c_float
class Config(C.Structure):_fields_=[(n,U) for n in ('mode','enabled','boost','bright')]+[(n,F) for n in ('drive','tone','level')]
class Pedal(C.Structure):_fields_=[('target',Config)]+[(n,F) for n in ('drive','tone','level','mix')]+[('up',F*8),('down',F*15)]+[(n,F) for n in ('emphasis','tone_z','dc_in','dc_out')]+[(n,U) for n in ('up_pos','down_pos','fault')]
class Combined(AutoRuntime):_fields_=[('pedal',Pedal),('pending',Config),('sequence',U),('applied',U),('bank_source',U*2),('active_slot',U),('copy_word',U),('bank_pending',U*2),('bank_state',U)]
RUNTIME=0x20064900;STATE=0x20050040;WEIGHTS=0x20062b40
ARENA=0x20010000;GRAPH=0x20011000;ENTRIES=0x20012000;NODE=0x20013000;CONFIG=0x20014000
IN=0x20001470;OUT=0x20001670;STACK=0x20067ff0;STOP=0x1fff0
def bytesf(v):return struct.pack('<%df'%len(v),*v)
def floats(b):return struct.unpack('<%df'%(len(b)//4),b)
def signature(name):return next(x['signature'] for x in json.loads((NAM/'evidence/factory-amp-signatures.json').read_text())['models'] if x['name']==name)
class Machine:
    def __init__(self,weights,inplace=False):
        self.u=u=Uc(UC_ARCH_ARM,UC_MODE_THUMB|UC_MODE_MCLASS);u.ctl_set_cpu_model(UC_CPU_ARM_CORTEX_M7)
        for a,n in [(0,0x20000),(0x20000000,0x68000),(0x80000000,0x10000),(0x83000000,0x10000),(0xe0001000,0x1000),(0xe000e000,0x1000)]:u.mem_map(a,n)
        self.segments,self.symbols=elf(ROOT/'build/combined.elf')
        for seg in self.segments:u.mem_write(seg['address'],seg['data'])
        u.reg_write(UC_ARM_REG_C1_C0_2,0xf<<20);u.reg_write(UC_ARM_REG_FPEXC,0x40000000)
        assert C.sizeof(Combined)==self.call('trial_runtime_bytes')==512
        r=Combined();r.magic=0x324d414e;r.min_sp=r.min_period=0xffffffff
        r.drive_scale=r.boost_scale=1;r.input_gain=r.input_current=10**(-12/20);r.level=r.level_current=1;r.stock_fade=1
        for i in range(4):r.eq_index[i]=10
        r.bank_source[:]=[0x83001000,0x83004000]
        self.weights=weights;self.silences=[]
        g=model.geometry()
        for i,w in enumerate(weights):
            a=Arm(w);state=bytes(a.u.mem_read(REF_STATE,g['state_bytes']))
            silence=b''.join(state[96+o*4:96+o*4+12] for o in g['ring_offset']+[g['head_ring_offset']])
            self.silences.append(silence);u.mem_write(r.bank_source[i],model.pack(w)+silence)
        self.put(r);self.call('preamp_init',RUNTIME+Combined.pedal.offset)
        self.w32(0x20062b38,0xa5a5a5a5);self.w32(0x20064880,0x5a5a5a5a);self.w32(0x200657e0,0xa7a7a7a7);self.w32(0x20065c80,0xa6a6a6a6)
        u.mem_write(RUNTIME-16,b'\xc1'*16);self.after_guard=bytes(u.mem_read(RUNTIME+512,16));self.guarded=True
        self.w32(ARENA+0x14c,128);self.w32(GRAPH+8,ENTRIES);self.w32(GRAPH+12,2)
        self.destination=IN if inplace else OUT
        u.mem_write(ENTRIES,struct.pack('<10I',1,1,0,IN,0,1,1,0,0,self.destination))
        self.w32(NODE+12,1);self.w32(NODE+20,GRAPH)
        for i,n in enumerate(('HD2_AmpImpossibleAC','A45_AmpImpossibleElmsleyNFB','HD2_AmpImpossibleClean')):
            u.mem_write(0x80001000+i*0x100,struct.pack('<6I',*signature(n)))
        self.slot(0);self.w32(0x20023498,600000000);self.ticks=0;self.step=70000
        def clock(u,access,address,size,value,user):self.ticks=(self.ticks+self.step)&0xffffffff;self.w32(0xe0001004,self.ticks)
        self.hook=u.hook_add(UC_HOOK_MEM_READ,clock,begin=0xe0001004,end=0xe0001007)
    def w32(self,p,*v):self.u.mem_write(p,struct.pack('<%dI'%len(v),*v))
    def get(self):return Combined.from_buffer_copy(bytes(self.u.mem_read(RUNTIME,512)))
    def put(self,r):self.u.mem_write(RUNTIME,bytes(r))
    def slot(self,index):self.w32(GRAPH+20,0x80001000+index*0x100)
    def call(self,name,*args):
        u=self.u;u.reg_write(UC_ARM_REG_SP,STACK);u.reg_write(UC_ARM_REG_LR,STOP|1)
        for reg,value in zip((UC_ARM_REG_R0,UC_ARM_REG_R1,UC_ARM_REG_R2,UC_ARM_REG_R3),args):u.reg_write(reg,value)
        if len(args)>4:self.w32(STACK,*args[4:])
        saved=[UC_ARM_REG_R4,UC_ARM_REG_R5,UC_ARM_REG_R6,UC_ARM_REG_R7,UC_ARM_REG_R8,UC_ARM_REG_R9,UC_ARM_REG_R10,UC_ARM_REG_R11]
        fp=[UC_ARM_REG_S16,UC_ARM_REG_S17,UC_ARM_REG_S18,UC_ARM_REG_S19,UC_ARM_REG_S20,UC_ARM_REG_S21,UC_ARM_REG_S22,UC_ARM_REG_S23,UC_ARM_REG_S24,UC_ARM_REG_S25,UC_ARM_REG_S26,UC_ARM_REG_S27,UC_ARM_REG_S28,UC_ARM_REG_S29,UC_ARM_REG_S30,UC_ARM_REG_S31]
        for i,r in enumerate(saved):u.reg_write(r,0xabcd0000+i)
        for i,r in enumerate(fp):u.reg_write(r,0x3f000000+i)
        u.emu_start(self.symbols[name]['address']|1,STOP,count=5000000)
        assert u.reg_read(UC_ARM_REG_PC)==STOP and u.reg_read(UC_ARM_REG_SP)==STACK,name
        assert [u.reg_read(r) for r in saved]==[0xabcd0000+i for i in range(8)],name
        assert [u.reg_read(r) for r in fp]==[0x3f000000+i for i in range(16)],name
        if getattr(self,'guarded',False):
            assert bytes(u.mem_read(RUNTIME-16,16))==b'\xc1'*16
            assert bytes(u.mem_read(RUNTIME+512,16))==self.after_guard
        return u.reg_read(UC_ARM_REG_R0)
    def request(self,c):self.u.mem_write(CONFIG,bytes(c));return self.call('cf_preamp_request',CONFIG)
    def control(self,pid,value,boolean=False):
        self.w32(CONFIG,int(value) if boolean else struct.unpack('<I',struct.pack('<f',value))[0])
        self.call('trial_node_control',ARENA,NODE,pid,CONFIG,0)
    def block(self,data,bracket=True):
        self.u.mem_write(IN,bytesf(data))
        if bracket:self.call('trial_begin',ARENA,GRAPH)
        handled=self.call('trial_route',ARENA,GRAPH)
        if bracket:self.call('trial_after',ARENA,GRAPH,handled)
        return handled,floats(bytes(self.u.mem_read(self.destination,128)))
    def warm(self):
        previous=self.get().copy_word
        for _ in range(90):
            self.block([0.]*32);r=self.get();assert r.copy_word-previous<=128;previous=r.copy_word
        r=self.get();assert r.mode==2 and r.fault==0 and r.nam_fade==1,(r.mode,r.fault,r.copy_word)
        assert bytes(self.u.mem_read(WEIGHTS,7484))==model.pack(self.weights[r.active_slot-1])
        return r

def main():
    build=json.loads((ROOT/'build/build.json').read_text());assert build['static_code_and_tables_fit']
    assert hashlib.sha256((ROOT/'build/combined.elf').read_bytes()).hexdigest()==build['elf_sha256']
    _,w=model.load(NAM/'external/NeuralAmpModelerCore/example_models/A2.nam');second=list(w);second[-1]*=-.5;weights=[w,second]
    checks=[];errors=[];s=Machine(weights);s.warm();assert s.get().active_slot==1
    s.slot(1);s.warm();assert s.get().active_slot==2
    s.slot(0);s.warm();assert s.get().active_slot==1
    s.slot(2);s.control(1,.5);s.block([0.]*32);assert s.get().mode==0 and s.block([.01]*32)[0]==0
    checks.append('Two graph signatures select independent weight banks with <=128 words copied per frame, bounded reset, return to stock and back')
    for inplace in (False,True):
        for mode in range(4):
            m=Machine(weights,inplace);m.control(2,.5);m.control(20,mode!=0,True)
            cfg=Config(mode,1,0,0,.5,.5,1);assert m.request(cfg)==0;m.warm()
            r=m.get();assert r.pedal.target.mode==mode and r.applied==r.sequence
            reference=Arm(w);reference.u.mem_write(REF_STATE,bytes(m.u.mem_read(STATE,76536)))
            pedal_elf=(NAM/build.get('preamp_reference_elf','boost-lab/build/preamp.elf')).resolve()
            assert pedal_elf.is_relative_to(NAM.resolve())
            if 'preamp_reference_sha256' in build:
                assert hashlib.sha256(pedal_elf.read_bytes()).hexdigest()==build['preamp_reference_sha256']
            ps,sy=elf(pedal_elf);pedal=pedal_lab.Machine(ps,sy)
            pedal.u.mem_write(pedal_lab.STATE,bytes(r.pedal))
            signal=[.10*math.sin(i*.072)+.03*math.sin(i*.321) for i in range(1024)]
            out=[]
            for p in range(0,len(signal),32):
                handled,y=m.block(signal[p:p+32]);assert handled==1 and m.get().fault==0;out.extend(y)
            pre=pedal.process(signal) if mode else signal
            expected=reference.process([v*float(r.input_gain) for v in pre])
            err=max(abs(x-y) for x,y in zip(out,expected));errors.append(dict(mode=mode,inplace=inplace,max_error=err))
            assert err<3e-5,errors[-1]
    checks.append('Combined preamp->NAM matches separate scalar NAM/preamp references in all four modes, with separate and in-place buffers')
    m=Machine(weights);m.control(2,.5);m.control(20,True,True)
    assert m.request(Config(0,1,0,0,.5,.5,1))==0;m.warm();unity=m.get().input_gain
    assert m.request(Config(0,1,0,0,.5,.5,.5))==0;m.block([0.]*32)
    assert abs(m.get().input_gain-unity*.5)<1e-7
    assert m.request(Config(0,1,0,0,.5,.5,2))==0;m.block([0.]*32)
    assert abs(m.get().input_gain-unity*2)<1e-7
    m.control(20,False,True);m.block([0.]*32)
    assert abs(m.get().input_gain-10**(-12/20)*m.get().drive_scale)<1e-7
    checks.append('Transparent NAM Boost applies its independent Level exactly once before NAM; OFF bypasses both amount and Level')
    m=Machine(weights);m.warm();before=bytes(m.u.mem_read(RUNTIME,512))
    bad=Config(9,1,0,0,.5,.5,1);assert m.request(bad)==0xffffffff and bytes(m.u.mem_read(RUNTIME,512))==before
    r=m.get();r.pending=Config(3,1,1,1,.9,.9,1.2);r.sequence=1;m.put(r);m.block([0.]*32);assert m.get().applied==0 and m.get().pedal.target.mode==0
    assert m.request(Config(2,1,0,0,.5,.6,1))==0;m.block([0.]*32);assert m.get().applied==m.get().sequence and m.get().pedal.target.mode==2
    checks.append('Invalid mailbox requests leave runtime untouched; audio skips an incomplete odd revision and applies only a complete snapshot')
    # Publish a different immutable weights+silence bank, including replacement
    # while NAM is active. A partial control publication must remain invisible.
    m=Machine(weights);m.warm();previous=m.get().bank_source[0]
    third=list(w);third[-1]+=.075
    reference=Arm(third);g=model.geometry();state=bytes(reference.u.mem_read(REF_STATE,g['state_bytes']))
    silence=b''.join(state[96+o*4:96+o*4+12] for o in g['ring_offset']+[g['head_ring_offset']])
    target=0x83007000;m.u.mem_write(target,model.pack(third)+silence)
    before=bytes(m.u.mem_read(RUNTIME,512))
    assert m.call('cf_nam_bank_request',1,target,7484)==0xffffffff and bytes(m.u.mem_read(RUNTIME,512))==before
    assert m.call('cf_nam_bank_request',1,target,7772)==0 and m.get().bank_source[0]==previous and m.get().bank_state==1
    assert m.call('cf_nam_bank_request',2,0x83001000,7772)==0xfffffffe
    assert m.block([0.]*32)[1]==(0.,)*32 and m.get().bank_state==2 and m.get().bank_source[0]==target
    # Once the audio owner has taken the pointer, it must not reread the
    # retired source during the bounded weight/history reset.
    m.u.mem_write(previous,b'\xff'*7772);m.weights=[third,second];m.warm()
    reference.u.mem_write(REF_STATE,bytes(m.u.mem_read(STATE,76536)))
    signal=[.08*math.sin(i*.081) for i in range(256)];output=[]
    for p in range(0,len(signal),32):output.extend(m.block(signal[p:p+32])[1])
    expected=reference.process([x*m.get().input_gain for x in signal]);assert max(abs(x-y) for x,y in zip(output,expected))<3e-5
    # A preparing state never exposes the new pointer. An inactive slot can
    # then be replaced without resetting the currently sounding NAM.
    r=m.get();r.bank_pending[0]=0x8300f000;r.bank_state=3;m.put(r);m.block([.01]*32);assert m.get().bank_source[0]==target
    r=m.get();r.bank_state=2;m.put(r);m.u.mem_write(previous,model.pack(w)+m.silences[0])
    assert m.call('cf_nam_bank_request',2,previous,7772)==0
    m.block([.01]*32);assert m.get().bank_source[1]==previous and m.get().mode==2 and m.get().copy_word==1871
    m.weights=[third,w];m.slot(1);m.warm();assert m.get().active_slot==2
    checks.append('Immutable weights+silence publication occurs only at an outer audio boundary; active replacement resets in bounded steps, retired sources are not reread, partial/busy requests remain isolated')
    m=Machine(weights);m.warm();m.step=250000
    for _ in range(6):m.block([0.]*32,False)
    assert m.get().fault==9 and not m.get().want and m.get().mode==0
    checks.append('Three consecutive scripted late periods retain the existing cadence fallback latch (not a timing measurement)')
    m=Machine(weights);m.warm();m.w32(0x20064880,0)
    assert m.block([.1]*32)[1]==(0.,)*32 and m.get().fault==6
    checks.append('Memory sentinel fault silences output; callee-saved registers, stack and runtime guards checked on every test call')
    result=dict(passed=True,hardware_accessed=False,firmware_created=False,graph_fixtures='synthetic, not native graph loaders',dwt='scripted, not measured',runtime_bytes=512,runtime_limit=512,text_bytes=build['text_bytes'],table_bytes=build['table_bytes'],elf_sha256=build['elf_sha256'],checks=checks,comparisons=errors,
      not_validated=['Native NAM 2 graph/setter ABI','Real cycle/IRQ/USB budget','Storage/receiver/preset persistence integration','Physical audio and cold boot'])
    (ROOT/'build/tests.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
if __name__=='__main__':
    try:main()
    except BaseException:
        import traceback
        (ROOT/'build/test-error.txt').write_text(traceback.format_exc());raise
