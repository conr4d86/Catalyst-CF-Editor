"""Native MidiTask -> queued CF actor -> native littlefs, all in Unicorn.

Only emulator RAM is patched. RX, RTOS queues and packet transport are explicit
fixtures. The original HAL callbacks remain forbidden by verify_native_fs.
No SDK, USB, physical NVM, installable CTF or actual scheduler is involved.
"""
import ctypes as C,contextlib,hashlib,json,struct,sys
from pathlib import Path
import verify_native_fs as fsmod
import verify_store as store
import verify_wire as wire
from unicorn import UC_HOOK_CODE
from unicorn.arm_const import *
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT.parent))
from build import elf
FS_OBJECT=0x8080bfb4;FS_TOP=(FS_OBJECT+0x874)&~7;MIDI_CTX=0x808532c0
FS_QUEUE=0x12340000;MESSAGE=0x83007000
def signed(v):return C.c_int32(v).value
def bl(source,target):
    delta=(target&~1)-(source+4);assert not delta&1 and -(1<<24)<=delta<(1<<24)
    s=(delta>>24)&1;i1=(delta>>23)&1;i2=(delta>>22)&1
    return struct.pack('<HH',0xf000|(s<<10)|((delta>>12)&1023),0xd000|(((~(i1^s))&1)<<13)|(((~(i2^s))&1)<<11)|((delta>>1)&2047))
class Harness:
    def __init__(self,initialize=True):
        fsmod.LFS=FS_OBJECT+0x9e8;fsmod.CFG=FS_OBJECT+0xa60
        self.fs=fsmod.NativeFS();self.m=m=self.fs.m;self.u=u=m.u
        segments,self.symbols=elf(ROOT/'actor-build/actor.elf')
        for seg in segments:
            if seg['data']:u.mem_write(seg['address'],seg['data'])
            if seg['size']>len(seg['data']):u.mem_write(seg['address']+len(seg['data']),bytes(seg['size']-len(seg['data'])))
        for address in (0x8001d2be,0x8001d324):
            assert bytes(u.mem_read(address,4))==bytes.fromhex('b8 47 f7 28')
            u.mem_write(address,bl(address,self.symbols['cf_receive_wrapper']['address']))
        self.queue=[];self.packets=[];self.native_receipts=[];self.stream=[];self.delivered=[];self.midimin=0xffffffff;self.fsmin=0xffffffff;self.mode=None;self.profile=False
        self.m.put(FS_OBJECT+0x878,FS_QUEUE)
        def intercept(u,address,size,user):
            r0=u.reg_read(UC_ARM_REG_R0);r1=u.reg_read(UC_ARM_REG_R1);r2=u.reg_read(UC_ARM_REG_R2)
            if address==0x8300b100:
                if not self.stream:u.emu_stop();return
                value=self.stream.pop(0);self.delivered.append(value);u.reg_write(UC_ARM_REG_R0,value)
            elif address==0x8004c830:
                if r0==FS_QUEUE:self.queue.append(bytes(u.mem_read(r1,24)))
                u.reg_write(UC_ARM_REG_R0,1)
            elif address==0x8004c980:u.reg_write(UC_ARM_REG_R0,1)
            elif address==0x8001d5ec:self.m.put(r1,0x8300a000);u.reg_write(UC_ARM_REG_R0,0)
            elif address==0x8001d928:
                assert r0==0x8300a000 and r2<=0x257;self.packets.append(bytes(u.mem_read(r1,r2)));u.reg_write(UC_ARM_REG_R0,0)
            elif address==0x8001d8c8:assert r0==0x8300a000;u.reg_write(UC_ARM_REG_R0,0)
            elif address==0x8301e900:self.native_receipts.append(bytes(u.mem_read(r0,1))[0]);u.reg_write(UC_ARM_REG_R0,0)
            else:return
            u.reg_write(UC_ARM_REG_PC,u.reg_read(UC_ARM_REG_LR))
        # Keep normal functional runs JIT-only outside the explicit boundaries.
        # Full per-instruction SP tracking is enabled only for named probes.
        for address in (0x8300b100,0x8004c830,0x8004c980,0x8001d5ec,0x8001d928,0x8001d8c8,0x8301e900):u.hook_add(UC_HOOK_CODE,intercept,begin=address,end=address)
        def stack(u,address,size,user):
            if not self.profile:return
            sp=u.reg_read(UC_ARM_REG_SP)
            if self.mode=='midi':self.midimin=min(self.midimin,sp)
            elif self.mode=='fs':self.fsmin=min(self.fsmin,sp)
        self.stack_callback=stack;self.stack_hook=None;self.pump_skip=0
        self.m.call(0x8001f18c);u.mem_write(0x8080cb18+0x110,b'\0');u.mem_write(0x8080cb18+0x438,b'\x25\x00')
        if initialize:assert self.call('cf_actor_init',store.PRODUCT,fsmod.LFS,FS_QUEUE,0x800174f5)==0
        assert self.call('cf_actor_bytes')<=4096
        # Empty native callback lists, then register its original identity handler.
        for event in range(32):head=MIDI_CTX+0x754+event*8;self.m.put(head,head,head)
        head=MIDI_CTX+0x754+2*8;node=0x8300b200;self.m.put(head,node,node);self.m.put(node,head,head,0x80016b75)
        self.m.put(MIDI_CTX+0x64,0x8300b000);self.m.put(0x8300b000,0x8300b101,0);u.mem_write(0x8300b00c,b'\0')
    def call(self,name,*args):
        u=self.u;u.reg_write(UC_ARM_REG_SP,FS_TOP);u.reg_write(UC_ARM_REG_LR,0x8301f001)
        for r,v in zip((UC_ARM_REG_R0,UC_ARM_REG_R1,UC_ARM_REG_R2,UC_ARM_REG_R3),args):u.reg_write(r,v)
        if len(args)>4:self.m.put(FS_TOP,*args[4:])
        self.mode='fs';u.emu_start(self.symbols[name]['address']|1,0x8301f000,count=30000000);self.mode=None
        assert u.reg_read(UC_ARM_REG_PC)==0x8301f000 and u.reg_read(UC_ARM_REG_SP)==FS_TOP
        return u.reg_read(UC_ARM_REG_R0)
    def feed(self,packet):
        self.stream=list(packet);before=len(self.delivered);self.mode='midi'
        top=(MIDI_CTX+0xd8+1024)&~7;self.u.reg_write(UC_ARM_REG_SP,top);self.u.reg_write(UC_ARM_REG_R0,MIDI_CTX);self.u.reg_write(UC_ARM_REG_LR,0x8301f001)
        self.u.emu_start(0x8001d27d,0x8301f000,count=10000000);self.mode=None
        assert not self.stream and self.delivered[before:]==list(packet)
    def drain(self):
        count=0
        while self.queue:
            count+=1;assert count<1000
            request=self.queue.pop(0);self.u.mem_write(MESSAGE,request)
            skip=self.profile and (struct.unpack_from('<I',request)[0]&255)==7 and self.pump_skip>0
            if skip:self.pump_skip-=1;self.tracing(False)
            assert self.call('cf_fs_dispatch',MESSAGE)==0
            if skip:self.tracing(True)
    def exchange(self,lib,message):
        before=len(self.packets);self.feed(wire.encode(lib,message));self.drain()
        output=self.packets[before:];assert len(output)==1,(message.opcode,len(output))
        rc,ack=wire.decode(lib,output[0]);assert rc==1 and ack.opcode==message.opcode|64 and ack.transaction==message.transaction
        return struct.unpack('<I',bytes(ack.data[:4]))[0],ack
    def tracing(self,enabled):
        self.profile=enabled
        if enabled and self.stack_hook is None:self.stack_hook=self.u.hook_add(UC_HOOK_CODE,self.stack_callback)
        if not enabled and self.stack_hook is not None:self.u.hook_del(self.stack_hook);self.stack_hook=None

def main():
    info=json.loads((ROOT/'actor-build/build.json').read_text());assert hashlib.sha256((ROOT/'actor-build/actor.elf').read_bytes()).hexdigest()==info['elf_sha256']
    w=json.loads((ROOT/'checks/wire-latest.json').read_text());lib=C.CDLL(str(Path(w['build_folder'])/'receiver.dll'))
    lib.cf_wire_encode.argtypes=[C.POINTER(wire.Message),C.c_void_p,C.c_uint32];lib.cf_wire_encode.restype=C.c_uint32
    lib.cf_wire_decode.argtypes=[C.c_void_p,C.c_uint32,C.POINTER(wire.Message)];lib.cf_wire_decode.restype=C.c_int
    with (ROOT/'actor-build/test.log').open('w') as log,contextlib.redirect_stdout(log):
        h=Harness();m=h.m
        # Compare ordinary identity before/after CF traffic, same original handler.
        identity=bytes.fromhex('f0 7e 7f 06 01 f7');h.tracing(True);h.feed(identity);h.tracing(False);old=list(h.packets);assert old and old[0][:2]==b'\xf0\x7e'
        assert not h.queue
        status,ack=h.exchange(lib,wire.msg(1,tx=9,session=0));assert status==store.OK
        assert struct.unpack('<4I',bytes(ack.data[4:20]))==(1,256,5,2)
        # Ordinary preset write still traverses the original queue API/handler.
        preset=b'SYNTHETIC PRESET THROUGH NATIVE DISPATCH\0'*3;m.u.mem_write(0x8300c100,preset)
        assert m.call(0x80016040,5,0,0x8300c100,len(preset),0x8301e901)==1
        h.drain();assert h.native_receipts==[0]
        assert h.fs.read('bank_0_channel_a',0,len(preset))==preset
        completed=[]
        for slot in (0,3,4):
            print('Starting slot',slot,file=sys.__stdout__,flush=True)
            data=store.payload(slot);tx=100+slot
            h.tracing(True)
            assert h.exchange(lib,wire.msg(2,slot,tx,data=struct.pack('<II',len(data),store.crc(data))+wire.metadata(slot)))[0]==store.OK
            h.tracing(False)
            for off in range(0,len(data),256):
                request=wire.msg(3,slot,tx,off,data[off:off+256]);h.tracing(off==0);assert h.exchange(lib,request)[0]==store.OK;h.tracing(False)
                if off==0:assert h.exchange(lib,request)[0]==store.ALREADY
            # Instrument the final verification/header write as well as decode.
            h.pump_skip=(len(data)+255)//256
            h.tracing(True);assert h.exchange(lib,wire.msg(4,slot,tx))[0]==store.PENDING;h.tracing(False)
            h.tracing(True)
            status,ack=h.exchange(lib,wire.msg(5,slot,tx));assert status==store.OK
            h.tracing(False)
            generation,receipt,length,digest=struct.unpack('<4I',bytes(ack.data[4:20]));assert (generation,receipt,length,digest)==(1,tx,len(data),store.crc(data))
            assert ack.bytes==204 and bytes(ack.data[20:204])==wire.metadata(slot)
            assert h.fs.read('bank_0_channel_a',0,len(preset))==preset
            completed.append(dict(slot=slot,bytes=length,transaction=tx))
            print('Completed slot',slot,file=sys.__stdout__,flush=True)
        # Explicit loader fixture: runs on the serialized FS owner, with an
        # emulator-only staging buffer that no audio routine can observe.
        load=0x8301d000;stage=0x83010010;store_address=h.call('cf_actor_store_address');loaded=[]
        for slot in (0,3,4):
            data=store.payload(slot);m.u.mem_write(stage-16,b'\xa6'*16+bytes(len(data))+b'\x6a'*16)
            h.tracing(True);assert h.call('cf_load_begin',load,store_address,slot,stage,len(data))==store.PENDING;h.tracing(False)
            assert h.exchange(lib,wire.msg(5,slot,100+slot))[0]==store.BUSY
            rc=store.PENDING;steps=0
            while rc==store.PENDING:
                previous=m.get(load+220);h.tracing(steps==0);rc=h.call('cf_load_pump',load);h.tracing(False);steps+=1
                assert m.get(load+220)-previous<=256
                if rc==store.PENDING:assert m.get(load+228)==0
            assert rc==store.OK and m.get(load+228)==1
            assert bytes(m.u.mem_read(stage,len(data)))==data
            assert bytes(m.u.mem_read(stage-16,16))==b'\xa6'*16 and bytes(m.u.mem_read(stage+len(data),16))==b'\x6a'*16
            assert h.fs.read('bank_0_channel_a',0,len(preset))==preset
            loaded.append(dict(slot=slot,bytes=len(data),steps=steps,ready_after_crc=True))
        before=len(h.packets);h.feed(identity);assert h.packets[before:]==old
        assert not h.queue
        # CRC failure cannot write, and a new valid frame resynchronizes RX.
        before=bytes(h.fs.disk);packet=bytearray(wire.encode(lib,wire.msg(3,slot=4,tx=200,data=bytes(256))));packet[30]^=1
        h.feed(packet);h.drain();assert bytes(h.fs.disk)==before
        assert h.exchange(lib,wire.msg(5,4,104))[0]==store.OK
        assert h.call('cf_actor_stat',3)==1
        # Declared transition-double: verify the wrapper mirrors the original
        # trampoline's replace-and-replay contract without being replaced.
        transitions=[]
        def transition(u,address,size,user):
            assert u.reg_read(UC_ARM_REG_R0)==MESSAGE
            transitions.append('old' if address==0x80015dc4 else 'new')
            u.reg_write(UC_ARM_REG_R0,0x8301e981 if address==0x80015dc4 else 0)
            u.reg_write(UC_ARM_REG_PC,u.reg_read(UC_ARM_REG_LR))
        hooks=[m.u.hook_add(UC_HOOK_CODE,transition,begin=a,end=a) for a in (0x80015dc4,0x8301e980)]
        m.put(MESSAGE,0x55,0,0,0,0,0);m.put(0x8300c800,h.symbols['cf_fs_dispatch']['address']|1)
        assert m.call(0x8002f218,0x8300c800,MESSAGE)==0
        assert transitions==['old','new'] and m.get(0x8300c800)==h.symbols['cf_fs_dispatch']['address']|1
        assert m.call(0x8002f218,0x8300c800,MESSAGE)==0 and transitions==['old','new','new']
        for hook in hooks:m.u.hook_del(hook)
        assert h.midimin>=MIDI_CTX+0xd8 and h.fsmin>=FS_OBJECT+0x74
        stats={key:h.call('cf_actor_stat',i) for i,key in enumerate(('posted','processed','send_attempts','malformed','observer_dropped','store_phase'))}
        native_code=bytes(m.u.mem_read(0x80016b74,32))
    report=dict(passed=True,hardware_accessed=False,USB_opened=False,firmware_created=False,elf_sha256=info['elf_sha256'],
      native_midi_parser_and_task=True,original_identity_packets_unchanged=True,native_preset_write_preserved=True,CRC_failure_does_not_write=True,dispatcher_replace_and_replay_preserved_with_double=True,
      transfers=completed,staged_loads=loaded,actor_stats=stats,midi_stack_observed_bytes=((MIDI_CTX+0xd8+1024)&~7)-h.midimin,midi_stack_capacity=1024,
        filesystem_stack_observed_bytes=FS_TOP-h.fsmin,filesystem_stack_capacity=2048,stack_probes='Full instruction SP tracking: identity, BEGIN, first chunk, COMMIT final, STATUS, loader BEGIN and first loader read; other chunks functional-only',metadata_stored_and_returned=True,native_identity_handler_sha256=hashlib.sha256(native_code).hexdigest(),
      limitations=['RTOS queues and packet transport are mocks','HAL operations use a fresh synthetic disk','No IRQ stack/cache or real USB timing','RAM addresses are emulator reservations, not a hardware-approved map','No boot/audio activation or persistent preamp configuration'])
    (ROOT/'actor-build/tests.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':
    try:main()
    except BaseException:
        import traceback
        (ROOT/'actor-build/test-error.txt').write_text(traceback.format_exc());raise
