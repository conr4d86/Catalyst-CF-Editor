"""Recovery via native MIDI parser -> queue -> FileSystem owner in Unicorn.
Original flash HAL calls are forbidden. Only synthetic files/RAM are used.
"""
import contextlib,ctypes as C,hashlib,importlib.metadata,json,struct,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent;TV=ROOT.parents[2]
# Preload private fresh packages; older sandbox-owned package dirs are unreadable.
sys.path.insert(0,str(ROOT.parents[1]/'catalyst-editor/tools-recovery'))
import unicorn,capstone
assert importlib.metadata.version('unicorn')=='2.1.4' and importlib.metadata.version('capstone')=='5.0.9'
import verify_actor as actor
import verify_store as store
import verify_wire as wire
LAB=ROOT/'actor-recovery-lab';BUILD=LAB/'actor-build'

def main():
    info=json.loads((BUILD/'build.json').read_text());assert hashlib.sha256((BUILD/'actor.elf').read_bytes()).hexdigest()==info['elf_sha256']
    for name,digest in info['source_sha256'].items():assert hashlib.sha256((TV/name).read_bytes()).hexdigest()==digest,name
    old=json.loads((ROOT/'actor-build/build.json').read_text());assert hashlib.sha256((ROOT/'actor-build/actor.elf').read_bytes()).hexdigest()==old['elf_sha256']
    wi=json.loads((ROOT/'checks/wire-latest.json').read_text());lib=C.CDLL(str(Path(wi['build_folder'])/'receiver.dll'))
    lib.cf_wire_encode.argtypes=[C.POINTER(wire.Message),C.c_void_p,C.c_uint32];lib.cf_wire_encode.restype=C.c_uint32
    lib.cf_wire_decode.argtypes=[C.c_void_p,C.c_uint32,C.POINTER(wire.Message)];lib.cf_wire_decode.restype=C.c_int
    actor.ROOT=LAB  # Only changes the ELF input of this isolated harness.
    with (BUILD/'recovery-test.log').open('w') as log,contextlib.redirect_stdout(log):
        h=actor.Harness();m=h.m;u=h.u
        address=h.call('cf_actor_store_address');size=h.call('cf_asset_store_bytes');assert size==512
        def state():return bytes(u.mem_read(address,size))
        def phase():return struct.unpack_from('<I',state(),24)[0]
        assert struct.unpack_from('<I',state(),20)[0]==store.PRODUCT
        def one():
            request=h.queue.pop(0);u.mem_write(actor.MESSAGE,request);assert h.call('cf_fs_dispatch',actor.MESSAGE)==0
        def take(message):
            before=len(h.packets);h.feed(wire.encode(lib,message));h.drain();packets=h.packets[before:];assert len(packets)==1
            rc,ack=wire.decode(lib,packets[0]);assert rc==1 and ack.opcode==message.opcode|64;return struct.unpack('<I',bytes(ack.data[:4]))[0],ack
        identity=bytes.fromhex('f0 7e 7f 06 01 f7');h.tracing(True);h.feed(identity);h.tracing(False);identity_before=list(h.packets);assert identity_before
        status,ack=take(wire.msg(1,tx=9,session=0));assert status==0 and struct.unpack('<4I',bytes(ack.data[4:20]))==(5,256,5,2)
        preset=b'SYNTHETIC RECOVERY PRESET THROUGH NATIVE DISPATCH\0'*3;u.mem_write(0x8300c100,preset)
        assert m.call(0x80016040,5,0,0x8300c100,len(preset),0x8301e901)==1
        h.tracing(True);h.drain();h.tracing(False);assert h.native_receipts==[0]
        assert h.fs.read('bank_0_channel_a',0,len(preset))==preset
        # An existing NAM must survive abandonment of its replacement.
        old_data=store.payload(3,2);old_info=wire.metadata(3)
        h.tracing(True);assert take(wire.msg(2,3,33,data=struct.pack('<II',len(old_data),store.crc(old_data))+old_info))[0]==0;h.tracing(False)
        for off in range(0,len(old_data),256):
            h.tracing(off==0);assert take(wire.msg(3,3,33,off,old_data[off:off+256]))[0]==0;h.tracing(False)
        h.pump_skip=(len(old_data)+255)//256;h.tracing(True);assert take(wire.msg(4,3,33))[0]==6;h.tracing(False)
        cases=[]
        for slot in range(5):
            print('Recovery actor slot',slot,file=sys.__stdout__,flush=True)
            data=store.payload(slot,1);metadata=wire.metadata(slot);tx=100+slot;generation=2 if slot==3 else 1
            h.tracing(True);assert take(wire.msg(2,slot,tx,generation-1,struct.pack('<II',len(data),store.crc(data))+metadata))[0]==0
            assert take(wire.msg(3,slot,tx,0,data[:256]))[0]==0;h.tracing(False);assert phase()==1
            payload=struct.pack('<III',generation,len(data),store.crc(data))+metadata;before=state();io=dict(h.fs.io_counts)
            wrong=bytearray(payload);wrong[-1]^=1
            h.tracing(True);assert take(wire.msg(7,slot,tx,data=bytes(wrong)))[0]==3
            assert take(wire.msg(7,slot,tx,data=payload,session=10))[0]!=0
            assert state()==before and h.fs.io_counts==io
            assert take(wire.msg(7,slot,tx,data=payload))[0]==0;h.tracing(False)
            changed=bytearray(before);struct.pack_into('<I',changed,24,0);assert state()==bytes(changed) and h.fs.io_counts==io
            assert take(wire.msg(7,slot,tx,data=payload))[0]==3
            code,receipt=take(wire.msg(5,slot,tx));assert code==(0 if slot==3 else 7)
            if slot==3:assert struct.unpack('<4I',bytes(receipt.data[4:20]))==(1,33,len(old_data),store.crc(old_data))
            assert h.fs.read('bank_0_channel_a',0,len(preset))==preset
            cases.append(dict(slot=slot,conditional_reception_abandoned=True,strict_receipt_and_session=True,recovery_IO_calls=0,previous_NAM_preserved=slot==3,preset_preserved=True))
        # Queue interleaving: COMMIT is accepted before the recovery request.
        slot=4;tx=204;data=store.payload(slot,1);metadata=wire.metadata(slot)
        assert take(wire.msg(2,slot,tx,data=struct.pack('<II',len(data),store.crc(data))+metadata))[0]==0
        for off in range(0,len(data),256):assert take(wire.msg(3,slot,tx,off,data[off:off+256]))[0]==0
        before=len(h.packets);h.feed(wire.encode(lib,wire.msg(4,slot,tx)));one();assert phase()==2 and len(h.packets)==before+1
        payload=struct.pack('<III',1,len(data),store.crc(data))+metadata;before=len(h.packets)
        h.feed(wire.encode(lib,wire.msg(7,slot,tx,data=payload)))
        # The previously queued verification pump precedes the queued request.
        assert struct.unpack('<I',h.queue[0][:4])[0]&255==7;one();assert phase()==2
        snapshot=state();io=dict(h.fs.io_counts);h.tracing(True);one();h.tracing(False)
        assert phase()==2 and state()==snapshot and h.fs.io_counts==io
        rc,ack=wire.decode(lib,h.packets[before]);assert rc==1 and ack.opcode==71 and struct.unpack('<I',bytes(ack.data[:4]))[0]==1
        h.pump_skip=(len(data)+255)//256-1;h.tracing(True);h.drain();h.tracing(False);assert phase()==0
        code,ack=take(wire.msg(5,slot,tx));assert code==0 and struct.unpack('<4I',bytes(ack.data[4:20]))==(1,tx,len(data),store.crc(data))
        # Explicit read-lease fixture: it must remain untouched by the request.
        m.put(address+24,3);snapshot=state();io=dict(h.fs.io_counts);h.tracing(True);assert take(wire.msg(7,slot,tx,data=payload))[0]==1;h.tracing(False)
        assert state()==snapshot and h.fs.io_counts==io;m.put(address+24,0)
        assert h.fs.read('bank_0_channel_a',0,len(preset))==preset
        before=len(h.packets);h.feed(identity);assert h.packets[before:]==identity_before
        assert not h.queue and h.call('cf_actor_stat',3)==0
        midi_top=(actor.MIDI_CTX+0xd8+1024)&~7;midi_used=midi_top-h.midimin;fs_used=actor.FS_TOP-h.fsmin
        assert midi_used<=1024 and fs_used<=2048
    report=dict(passed=True,hardware_accessed=False,SDK_loaded=False,USB_opened=False,firmware_created=False,original_actor_preserved=True,elf_sha256=info['elf_sha256'],text_bytes=info['text_bytes'],bss_bytes=info['bss_bytes'],native_MIDI_parser=True,native_FileSystem_on_synthetic_disk=True,original_identity_unchanged=True,native_preset_preserved=True,commit_queue_interleaving_protected=True,read_lease_preserved=True,cases=cases,
                midi_stack_observed_bytes=midi_used,filesystem_stack_observed_bytes=fs_used,stack_scope='Full instruction tracking for identity, preset dispatch, BEGIN/first CHUNK, recovery/rejection and verification finalization; not IRQ or all native paths',
                sources={str(p.relative_to(TV)):hashlib.sha256(p.read_bytes()).hexdigest() for p in [ROOT/'verify_recovery_actor.py',ROOT/'verify_actor.py',ROOT/'verify_native_fs.py',BUILD/'actor.elf']},
                limitations=['Emulator reservations are not physical memory allocation approval','Real scheduler, cache/IRQ, USB, boot and audio activation not validated','Read lease is an explicit fixture; automatic loader coordination remains pending'])
    (BUILD/'recovery-tests.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':main()
