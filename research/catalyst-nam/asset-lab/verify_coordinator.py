"""Native files -> C coordinator -> real ARM NAM/IR handoffs, in Unicorn.
The fixture schedules owner/audio steps explicitly: no USB or real RTOS proof.
"""
import argparse,contextlib,ctypes as C,hashlib,importlib.util,json,math,struct,sys,types,zlib
from pathlib import Path
ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent;TV=ROOT.parents[2]
sys.path.insert(0,str(NAM.parent/'catalyst-editor/tools-recovery'))
import unicorn,capstone
from unicorn import UC_HOOK_MEM_READ
import verify_actor as actor
import verify_store as store
import verify_wire as wire
spec=importlib.util.spec_from_file_location('coordinator_audio',NAM/'dual-lab/verify_lab.py');audio=importlib.util.module_from_spec(spec);spec.loader.exec_module(audio)
LAB=ROOT/'actor-coordinator-lab';BUILD=LAB/'actor-build'
def main():
    global LAB,BUILD
    parser=argparse.ArgumentParser();parser.add_argument('--preamp',action='store_true');parser.add_argument('--preamp-only',action='store_true');options=parser.parse_args()
    if options.preamp_only:options.preamp=True
    if options.preamp:LAB=ROOT/'actor-preamp-lab';BUILD=LAB/'actor-build'
    ai=json.loads((BUILD/'build.json').read_text());di=json.loads((NAM/'dual-lab/lease-build/build.json').read_text())
    assert hashlib.sha256((BUILD/'actor.elf').read_bytes()).hexdigest()==ai['elf_sha256']
    assert hashlib.sha256((NAM/'dual-lab/lease-build/combined.elf').read_bytes()).hexdigest()==di['elf_sha256']
    wi=json.loads((ROOT/'checks/wire-latest.json').read_text());lib=C.CDLL(str(Path(wi['build_folder'])/'receiver.dll'))
    lib.cf_wire_encode.argtypes=[C.POINTER(wire.Message),C.c_void_p,C.c_uint32];lib.cf_wire_encode.restype=C.c_uint32
    lib.cf_wire_decode.argtypes=[C.c_void_p,C.c_uint32,C.POINTER(wire.Message)];lib.cf_wire_decode.restype=C.c_int
    _,weights=audio.model.load(NAM/'external/NeuralAmpModelerCore/example_models/A2.nam');changed=list(weights);changed[-1]+=.075
    fixture=audio.Machine([weights,changed]);blobs=[audio.model.pack(w)+fixture.silences[i] for i,w in enumerate((weights,changed))]
    assembly=types.ModuleType('keystone')
    def forbidden(*args,**kwargs):raise RuntimeError('Firmware assembly is forbidden in this test')
    assembly.Ks=forbidden;assembly.KS_ARCH_ARM=assembly.KS_MODE_THUMB=assembly.KS_MODE_LITTLE_ENDIAN=assembly.KS_MODE_V8=0;sys.modules['keystone']=assembly
    from ctf_tool import parse
    from package_extension import decode_hex
    base=(NAM/'app/resources/nam-engine.ctf').read_bytes();assert hashlib.sha256(base).hexdigest()=='e80bba31906c0032b4b92f9d062c54879ea88fbabbf0a9d073dd1794d2a287a1'
    _,raw,regions=parse(base);region=next(r for r in regions if r['kind']==0);image=decode_hex(raw[region['offset']:region['offset']+region['size']])[0][1][1]
    twiddle=audio.floats(image[0x601b3400-0x60001000:0x601b3500-0x60001000])
    def ir_blob(slot):
        impulse=[0.]*2048;impulse[slot*17]=1.+slot*.25;impulse[127+slot]=-.125;out=[]
        for part in range(64):
            for k in range(33):
                re=im=0.
                for n,x in enumerate(impulse[part*32:part*32+32]):
                    t=(k*n)&63;sign=-1. if t>=32 else 1.;t&=31
                    re+=x*twiddle[t*2]*sign;im+=x*twiddle[t*2+1]*sign
                out.extend((re,im))
        return audio.bytesf(impulse[::-1])+audio.bytesf(out),impulse
    irs=[ir_blob(i) for i in range(3)];actor.ROOT=LAB
    with (BUILD/'coordinator-test.log').open('w') as log,contextlib.redirect_stdout(log):
        h=actor.Harness();m=h.m;u=h.u;u.mem_map(0xe0001000,0x1000)
        segments,symbols=audio.elf(NAM/'dual-lab/lease-build/combined.elf')
        for seg in segments:
            if seg['data']:u.mem_write(seg['address'],seg['data'])
        u.mem_write(0x60001000,image)
        u.mem_write(0x14000,image[0x601ac000-0x60001000:0x601ad000-0x60001000])
        r=fixture.get();r.bank_source[:]=[0,0];u.mem_write(audio.RUNTIME,bytes(r))
        for address,value in ((0x20062b38,0xa5a5a5a5),(0x20064880,0x5a5a5a5a),(0x200657e0,0xa7a7a7a7),(0x20065c80,0xa6a6a6a6),(0x20023498,600000000)):m.put(address,value)
        m.put(audio.ARENA+0x14c,128);m.put(audio.GRAPH+8,audio.ENTRIES,2);m.put(audio.GRAPH+20,0x80871000)
        m.put(0x80871000,*audio.signature('HD2_AmpImpossibleAC'));m.put(0x80871100,*audio.signature('A45_AmpImpossibleElmsleyNFB'))
        m.put(audio.ENTRIES,1,1,0,audio.IN,0,1,1,0,0,audio.OUT)
        ticks=[0]
        def clock(u,access,address,size,value,user):ticks[0]=(ticks[0]+70000)&0xffffffff;m.put(0xe0001004,ticks[0])
        u.hook_add(UC_HOOK_MEM_READ,clock,begin=0xe0001004,end=0xe0001007)
        def acall(name,*args):return m.call(symbols[name]['address'],*args)
        def runtime():return audio.Combined.from_buffer_copy(bytes(u.mem_read(audio.RUNTIME,512)))
        def block(signal=None):
            u.mem_write(audio.IN,audio.bytesf(signal if signal is not None else [0.]*32))
            acall('trial_begin',audio.ARENA,audio.GRAPH);words=h.call('cf_ir_boundary');assert words<=128
            handled=acall('trial_route',audio.ARENA,audio.GRAPH);acall('trial_after',audio.ARENA,audio.GRAPH,handled)
            assert not runtime().fault
            return audio.floats(bytes(u.mem_read(audio.OUT,128)))
        # Measured actor BSS precedes four NAM allocations and one shared IR.
        cursor=(0x80860000+ai['bss_bytes']+31)&~31;buffers=[]
        for _ in range(4):buffers.append(cursor);cursor=(cursor+7772+31)&~31
        ir=cursor;end=ir+25088;assert end<0x80872000
        assert h.call('cf_ir_init',0,0,0,0)==0
        assert h.call('cf_coord_bind',ir,*buffers)==0
        assert h.call('cf_coord_bind',ir,*buffers)==8
        preset=b'SYNTHETIC COORDINATOR PRESET\0';h.fs.write('bank_0_channel_a',0,preset,True)
        addon_result=None
        if options.preamp:
            names=[f'bank_{i//2}_channel_{"a" if i%2==0 else "b"}' for i in range(12)]+['channel_manual_edited']
            native={name:(preset if i==0 else b'SYNTHETIC NATIVE PRESET '+str(i).encode()) for i,name in enumerate(names)}
            for name,data in native.items():h.fs.write(name,0,data,True)
            def addon_pump():
                rc=6;steps=0
                while rc==6:
                    h.tracing(True);rc=h.call('cf_actor_preamp_pump');h.tracing(False);steps+=1;assert steps<40
                return rc
            assert h.call('cf_actor_preamp_open')==6 and addon_pump()==0
            records=[];configuration=0x8301c000;output=0x8301c080
            def fbits(v):return struct.unpack('<I',struct.pack('<f',v))[0]
            for i,name in enumerate(names):
                data=struct.pack('<8I',i%4,i%2,(i//2)%2,(i//3)%2,fbits(i/12),fbits(.6),fbits(1.1),zlib.crc32(native[name])&0xffffffff);records.append(data);u.mem_write(configuration,data)
                assert h.call('cf_actor_preamp_save',i,700+i,configuration)==6 and addon_pump()==0
            assert h.call('cf_actor_preamp_open')==6 and addon_pump()==0
            for i,name in enumerate(names):
                digest=zlib.crc32(native[name])&0xffffffff
                assert h.call('cf_actor_preamp_get',i,digest,output)==0 and bytes(u.mem_read(output,32))==records[i]
                assert h.call('cf_actor_preamp_get',i,digest^1,output)==3
                assert h.fs.read(name,0,len(native[name]))==native[name]
            # The actor now computes the native-file CRC itself. Caller CRC
            # is deliberately wrong, then tested against actual file bytes.
            assert h.call('cf_actor_preamp_job_init')==0
            job_config=struct.pack('<8I',2,1,0,1,fbits(.3),fbits(.7),fbits(1.2),0xdeadbeef);u.mem_write(configuration,job_config)
            assert h.call('cf_actor_preamp_job_save',4,900,configuration)==6
            def job_pump():
                rc=6;steps=0
                while rc==6:
                    h.tracing(True);rc=h.call('cf_actor_preamp_job_pump');h.tracing(False);steps+=1;assert steps<300
                return rc
            assert job_pump()==0
            digest=zlib.crc32(native[names[4]])&0xffffffff
            assert h.call('cf_actor_preamp_get',4,digest,output)==0
            assert bytes(u.mem_read(output,32))==job_config[:28]+struct.pack('<I',digest)
            assert h.call('cf_actor_preamp_job_load',4)==6 and runtime().pending.mode==0
            assert job_pump()==0 and runtime().pending.mode==2
            changed_native=b'RESTORED DIFFERENT NATIVE PRESET';h.fs.write(names[4],0,changed_native,True)
            assert h.call('cf_actor_preamp_job_load',4)==6 and runtime().pending.mode==0
            assert job_pump()==3 and runtime().pending.mode==0
            h.fs.write(names[4],0,native[names[4]],True)
            assert h.call('cf_actor_preamp_job_load',4)==6
            assert h.call('cf_actor_preamp_job_pump')==6
            # Real original dispatcher activity invalidates a hash in flight.
            u.mem_write(0x8300c100,preset)
            assert m.call(0x80016040,5,0,0x8300c100,len(preset),0x8301e901)==1
            h.drain();assert h.call('cf_actor_preamp_job_pump')==3
            addon_result=dict(presets=13,dedicated_native_sidecars=True,reopen_preserves_all=True,native_files_unchanged=True,wrong_native_crc_rejected=True,
                              native_preset_CRC_computed_by_actor=True,caller_CRC_ignored=True,restored_native_preset_resets_pedal=True,original_dispatch_activity_invalidates_hash=True,SDK_save_binding_pending=True)
            if options.preamp_only:
                used=actor.FS_TOP-h.fsmin;assert used<=2048
                report=dict(passed=True,hardware_accessed=False,USB_opened=False,SDK_loaded=False,firmware_created=False,preamp_native=addon_result,
                    filesystem_stack_observed_bytes=used,filesystem_stack_limit=2048,stack_scope='Instrumented sidecar/hash pumps, not IRQ or all native paths',
                    actor_elf_sha256=ai['elf_sha256'],audio_elf_sha256=di['elf_sha256'],source_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest())
                (BUILD/'preamp-native-tests.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2),file=sys.__stdout__);return
        assert h.exchange(lib,wire.msg(1,tx=9,session=0))[0]==0
        def upload(slot,data,transaction,generation):
            assert h.exchange(lib,wire.msg(2,slot,transaction,generation,struct.pack('<II',len(data),store.crc(data))+wire.metadata(slot)))[0]==0
            for off in range(0,len(data),256):assert h.exchange(lib,wire.msg(3,slot,transaction,off,data[off:off+256]))[0]==0
            assert h.exchange(lib,wire.msg(4,slot,transaction))[0]==6
        for slot in range(5):upload(slot,irs[slot][0] if slot<3 else blobs[slot-3],100+slot,0)
        def settle(max_steps=1000):
            count=0
            while h.call('cf_coord_stat',0) or h.call('cf_coord_stat',1):
                assert h.call('cf_coord_step') in (0,3,6,7);block();count+=1;assert count<max_steps
            assert not h.call('cf_coord_stat',2);return count
        results=[]
        for slot in (3,4):
            h.tracing(True);assert h.call('cf_coord_start',slot,0)==6;h.tracing(False)
            steps=settle();assert runtime().bank_source[slot-3] in buffers
            assert bytes(u.mem_read(runtime().bank_source[slot-3],7772))==blobs[slot-3]
            results.append(dict(slot=slot,owner_steps=steps))
        for slot in range(3):
            assert h.call('cf_coord_start',slot,0x20018000)==6;steps=settle()
            assert m.get(0x20040000)==0x52494358 and m.get(0x20040004)==slot
            assert bytes(u.mem_read(0x20045000,16896))==irs[slot][0][8192:]
            signal=[.02]+[0.]*2111;response=[]
            for off in range(0,len(signal),32):
                u.mem_write(0x20044800,audio.bytesf(signal[off:off+32]));m.call(0x14901,0x20001470)
                response.extend(audio.floats(bytes(u.mem_read(0x20001470,128))))
            expected=[x*.02/32 for x in irs[slot][1]]+[0.]*64
            error=max(abs(x-y) for x,y in zip(response,expected));assert error<1e-6,error
            results.append(dict(slot=slot,owner_steps=steps,native_FFT_error=error))
        # No boundary => no eviction acknowledgement and no mutation of cache.
        cache=bytes(u.mem_read(ir,25088));assert h.call('cf_coord_start',0,0x20018000)==6
        for _ in range(20):assert h.call('cf_coord_step')==6
        assert h.call('cf_coord_stat',0)==2 and bytes(u.mem_read(ir,25088))==cache
        # Newer selection supersedes an in-flight load before publication.
        block()
        while not h.call('cf_coord_stat',5):assert h.call('cf_coord_step')==6
        for _ in range(5):assert h.call('cf_coord_step')==6
        assert h.call('cf_coord_start',1,0x20018000)==6;settle()
        assert m.get(0x20040004)==1 and bytes(u.mem_read(0x20045000,16896))==irs[1][0][8192:]
        # Replace active NAM1; old bytes remain immutable until outer ack.
        upload(3,blobs[1],201,1);before=runtime().bank_source[0];old=bytes(u.mem_read(before,7772))
        assert h.call('cf_coord_start',3,0)==6
        while h.call('cf_coord_stat',0)!=6:assert h.call('cf_coord_step')==6
        assert runtime().bank_source[0]==before and bytes(u.mem_read(before,7772))==old
        for _ in range(8):assert h.call('cf_coord_step')==6 and bytes(u.mem_read(before,7772))==old
        block();settle();assert runtime().bank_source[0]!=before
        u.mem_write(before,b'\xff'*7772)
        for _ in range(90):block()
        reference=audio.Arm(changed);reference.u.mem_write(audio.REF_STATE,bytes(u.mem_read(audio.STATE,76536)))
        signal=[.08*math.sin(i*.081) for i in range(256)];output=[]
        for p in range(0,len(signal),32):output.extend(block(signal[p:p+32]))
        expected=reference.process([x*runtime().input_gain for x in signal]);nam_error=max(abs(x-y) for x,y in zip(output,expected));assert nam_error<3e-5,nam_error
        assert h.fs.read('bank_0_channel_a',0,len(preset))==preset
        assert h.call('cf_coord_bytes')==584 and h.call('cf_stage_bytes')==456
    report=dict(passed=True,hardware_accessed=False,USB_opened=False,SDK_loaded=False,firmware_created=False,
        cases=results,coordinator_ARM_bytes=584,stage_ARM_bytes=456,pool_end=hex(end),
        IR_reuse_requires_audio_ack=True,newer_selection_replaces_inflight_load=True,NAM_old_buffer_immutable_until_ack=True,NAM_retired_buffer_poisoned=True,
        NAM_scalar_max_error=nam_error,native_preset_preserved=True,preamp_native=addon_result,actor_elf_sha256=ai['elf_sha256'],audio_elf_sha256=di['elf_sha256'],
        not_validated=['Physical memory/cache/IRQ ownership','Boot/native-selection hooks and automatic scheduling','Native app MIDI driver','Preamp preset persistence','Physical audio and cold boot'],
        sources={str(p.relative_to(TV)):hashlib.sha256(p.read_bytes()).hexdigest() for p in (ROOT/'asset_coordinator.c',ROOT/'asset_coordinator.h',ROOT/'asset_stage.c',ROOT/'derive_leased_ir.py',Path(__file__))})
    (BUILD/'coordinator-tests.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':main()
