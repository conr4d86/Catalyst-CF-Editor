"""Joined emulation: MIDI -> native files -> verified staging -> NAM audio.

No installable firmware, USB or real scheduler. Publication is invoked by the
fixture, not advertised as an installed wire capability. All RAM is emulator
memory, not a physical allocation approval.
"""
import contextlib,ctypes as C,hashlib,importlib.util,json,math,struct
from pathlib import Path
import verify_actor as actor
import verify_store as store
import verify_wire as wire
ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent
spec=importlib.util.spec_from_file_location('pipeline_audio',NAM/'dual-lab/verify_lab.py');audio=importlib.util.module_from_spec(spec);spec.loader.exec_module(audio)
from unicorn import UC_HOOK_MEM_READ

def main():
    ai=json.loads((ROOT/'actor-build/build.json').read_text());di=json.loads((NAM/'dual-lab/build/build.json').read_text())
    assert hashlib.sha256((ROOT/'actor-build/actor.elf').read_bytes()).hexdigest()==ai['elf_sha256']
    assert hashlib.sha256((NAM/'dual-lab/build/combined.elf').read_bytes()).hexdigest()==di['elf_sha256']
    wi=json.loads((ROOT/'checks/wire-latest.json').read_text());lib=C.CDLL(str(Path(wi['build_folder'])/'receiver.dll'))
    lib.cf_wire_encode.argtypes=[C.POINTER(wire.Message),C.c_void_p,C.c_uint32];lib.cf_wire_encode.restype=C.c_uint32
    lib.cf_wire_decode.argtypes=[C.c_void_p,C.c_uint32,C.POINTER(wire.Message)];lib.cf_wire_decode.restype=C.c_int
    _,original=audio.model.load(NAM/'external/NeuralAmpModelerCore/example_models/A2.nam');changed=list(original);changed[-1]+=.075
    fixture=audio.Machine([original,changed]);blob=audio.model.pack(changed)+fixture.silences[1];assert len(blob)==7772
    with (ROOT/'actor-build/pipeline.log').open('w') as log,contextlib.redirect_stdout(log):
        h=actor.Harness();m=h.m;u=m.u;u.mem_map(0xe0001000,0x1000);segments,symbols=audio.elf(NAM/'dual-lab/build/combined.elf')
        for segment in segments:
            if segment['data']:u.mem_write(segment['address'],segment['data'])
        r=fixture.get();r.bank_source[:]=[0x80864000,0x80866000];u.mem_write(audio.RUNTIME,bytes(r))
        for source in r.bank_source:u.mem_write(source,audio.model.pack(original)+fixture.silences[0])
        for address,value in ((0x20062b38,0xa5a5a5a5),(0x20064880,0x5a5a5a5a),(0x200657e0,0xa7a7a7a7),(0x20065c80,0xa6a6a6a6),(0x20023498,600000000)):m.put(address,value)
        m.put(audio.ARENA+0x14c,128);m.put(audio.GRAPH+8,audio.ENTRIES,2);m.put(audio.GRAPH+20,0x80871000)
        m.put(0x80871000,*audio.signature('HD2_AmpImpossibleAC'))
        m.put(audio.ENTRIES,1,1,0,audio.IN,0,1,1,0,0,audio.OUT)
        ticks=[0]
        def clock(u,access,address,size,value,user):ticks[0]=(ticks[0]+70000)&0xffffffff;m.put(0xe0001004,ticks[0])
        u.hook_add(UC_HOOK_MEM_READ,clock,begin=0xe0001004,end=0xe0001007)
        def call(name,*args):return m.call(symbols[name]['address'],*args)
        def runtime():return audio.Combined.from_buffer_copy(bytes(u.mem_read(audio.RUNTIME,512)))
        def block(signal):
            u.mem_write(audio.IN,audio.bytesf(signal));call('trial_begin',audio.ARENA,audio.GRAPH)
            handled=call('trial_route',audio.ARENA,audio.GRAPH);call('trial_after',audio.ARENA,audio.GRAPH,handled)
            assert handled==1 and runtime().fault==0
            return audio.floats(bytes(u.mem_read(audio.OUT,128)))
        for _ in range(90):block([0.]*32)
        assert runtime().mode==2 and runtime().active_slot==1
        preset=b'SYNTHETIC PIPELINE PRESET\0';h.fs.write('bank_0_channel_a',0,preset,True)
        assert h.exchange(lib,wire.msg(1,tx=9,session=0))[0]==store.OK
        assert h.exchange(lib,wire.msg(2,3,103,data=struct.pack('<II',len(blob),store.crc(blob))+wire.metadata(3)))[0]==store.OK
        for offset in range(0,len(blob),256):
            assert h.exchange(lib,wire.msg(3,3,103,offset,blob[offset:offset+256]))[0]==store.OK
            block([0.]*32) # Explicit serialized interleaving, not real RTOS concurrency.
        assert h.exchange(lib,wire.msg(4,3,103))[0]==store.PENDING
        rc,ack=h.exchange(lib,wire.msg(5,3,103));assert rc==store.OK and struct.unpack_from('<I',bytes(ack.data),16)[0]==store.crc(blob)
        loader=0x8301d100;stage=0x8086a000;store_address=h.call('cf_actor_store_address')
        assert h.call('cf_load_begin',loader,store_address,3,stage,len(blob))==store.PENDING
        rc=store.PENDING;reads=0
        while rc==store.PENDING:
            rc=h.call('cf_load_pump',loader);reads+=1;block([0.]*32)
        assert rc==store.OK and m.get(loader+228)==1 and bytes(u.mem_read(stage,len(blob)))==blob
        previous=runtime().bank_source[0]
        assert call('cf_nam_bank_request',1,stage,7772)==0
        assert runtime().bank_source[0]==previous and runtime().bank_state==1
        assert block([0.]*32)==(0.,)*32 and runtime().bank_state==2 and runtime().bank_source[0]==stage
        u.mem_write(previous,b'\xff'*7772)
        for _ in range(90):block([0.]*32)
        assert runtime().mode==2 and bytes(u.mem_read(audio.WEIGHTS,7484))==audio.model.pack(changed)
        reference=audio.Arm(changed);reference.u.mem_write(audio.REF_STATE,bytes(u.mem_read(audio.STATE,76536)))
        signal=[.08*math.sin(i*.081) for i in range(512)];output=[]
        for p in range(0,len(signal),32):output.extend(block(signal[p:p+32]))
        expected=reference.process([x*runtime().input_gain for x in signal]);error=max(abs(x-y) for x,y in zip(output,expected));assert error<3e-5,error
        assert h.fs.read('bank_0_channel_a',0,len(preset))==preset
    report=dict(passed=True,hardware_accessed=False,USB_opened=False,SDK_loaded=False,firmware_created=False,
        pipeline=['native MidiTask observer','native FileSystem task wrapper','original littlefs on synthetic disk','bounded verified staging','outer audio boundary publication','bounded weights/history reset','NAM scalar reference comparison'],
        loader_steps=reads,model_bytes=len(blob),max_audio_error=error,old_source_poisoned_after_pointer_take=True,synthetic_preset_preserved=True,
        actor_elf_sha256=ai['elf_sha256'],audio_elf_sha256=di['elf_sha256'],source_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        not_validated=['Physical USB and flash','Real concurrent RTOS/cache/IRQ timing','Memory reservation ownership','Boot initialization','Automatic storage-to-audio coordinator','IR activation and new preset configuration','App UI/native transport integration'])
    (ROOT/'actor-build/pipeline-tests.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':
    try:main()
    except BaseException:
        import traceback
        (ROOT/'actor-build/pipeline-error.txt').write_text(traceback.format_exc());raise
