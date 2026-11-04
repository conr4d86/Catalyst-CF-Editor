"""Pinned FS owner bootstrap/queue service and actual ARM audio, no hardware.
UID registers, model getter and RTOS receive are explicit emulator fixtures.
The original native filesystem executes on a synthetic persistent disk.
"""
import argparse,base64,contextlib,ctypes as C,hashlib,importlib.util,json,struct,subprocess,sys,time,types,uuid
from pathlib import Path
ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent;LAB=ROOT/'actor-owner-lab';BUILD=LAB/'actor-build'
sys.path.insert(0,str(NAM.parent/'catalyst-editor/tools-recovery'))
import unicorn,capstone
from unicorn import UC_HOOK_CODE,UC_HOOK_MEM_READ
from unicorn.arm_const import *
import verify_actor as actor,verify_store as store,verify_wire as wire
from verify_seeded_actor import manifest,ROM
spec=importlib.util.spec_from_file_location('owner_audio',NAM/'dual-lab/verify_lab.py');audio=importlib.util.module_from_spec(spec);spec.loader.exec_module(audio)

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--package',type=Path);parser.add_argument('--output',type=Path);options=parser.parse_args()
    output=options.output.resolve() if options.output else BUILD;output.mkdir(parents=True,exist_ok=True)
    ai=json.loads((BUILD/'build.json').read_text());di=json.loads((NAM/'dual-lab/lease-build/build.json').read_text())
    assert ai['native_owner_loop'] and hashlib.sha256((BUILD/'actor.elf').read_bytes()).hexdigest()==ai['elf_sha256']
    wi=json.loads((ROOT/'checks/wire-latest.json').read_text());lib=C.CDLL(str(Path(wi['build_folder'])/'receiver.dll'))
    lib.cf_wire_encode.argtypes=[C.POINTER(wire.Message),C.c_void_p,C.c_uint32];lib.cf_wire_encode.restype=C.c_uint32
    lib.cf_wire_decode.argtypes=[C.c_void_p,C.c_uint32,C.POINTER(wire.Message)];lib.cf_wire_decode.restype=C.c_int
    _,weights=audio.model.load(NAM/'external/NeuralAmpModelerCore/example_models/A2.nam');second=list(weights);second[-1]+=.075
    fixture=audio.Machine([weights,second]);blobs=[audio.model.pack(w)+fixture.silences[i] for i,w in enumerate((weights,second))]
    fake=types.ModuleType('keystone');fake.Ks=lambda *a,**k:(_ for _ in ()).throw(RuntimeError('No firmware assembly in checks'));fake.KS_ARCH_ARM=fake.KS_MODE_THUMB=fake.KS_MODE_LITTLE_ENDIAN=fake.KS_MODE_V8=0;sys.modules['keystone']=fake
    from ctf_tool import parse
    from package_extension import decode_hex
    base=(NAM/'app/resources/nam-engine.ctf').read_bytes();_,raw,regions=parse(base);region=next(r for r in regions if r['kind']==0);image=decode_hex(raw[region['offset']:region['offset']+region['size']])[0][1][1]
    twiddle=audio.floats(image[0x601b3400-0x60001000:0x601b3500-0x60001000])
    def ir_blob(slot):
        impulse=[0.]*2048;impulse[slot*17]=1.+slot*.25;impulse[127+slot]=-.125;frequency=[]
        for part in range(64):
            for k in range(33):
                re=im=0.
                for n,x in enumerate(impulse[part*32:part*32+32]):
                    index=(k*n)&63;sign=-1. if index>=32 else 1.;index&=31
                    re+=x*twiddle[index*2]*sign;im+=x*twiddle[index*2+1]*sign
                frequency.extend((re,im))
        return audio.bytesf(impulse[::-1])+audio.bytesf(frequency)
    irs=[ir_blob(i) for i in range(3)]
    edition=hashlib.sha256(b'owner loop ROM test').digest();seed=manifest(irs+blobs,edition)
    package_hash=None;package_reset=False
    if options.package:
        package=options.package.read_bytes();package_hash=hashlib.sha256(package).hexdigest()
        _,package_raw,package_regions=parse(package);r=next(r for r in package_regions if r['kind']==0)
        images,_=decode_hex(package_raw[r['offset']:r['offset']+r['size']]);assert len(images)==2 and images[1][0]==0x60001000
        image=images[1][1];seed=image[ROM-0x60001000:];assert struct.unpack_from('<4I',seed)==(0x31445343,1,31,5)
        edition=seed[16:48];loaded=[]
        for slot in range(5):
            address,length,crc,tx=struct.unpack_from('<4I',seed,48+slot*200);assert length==(25088 if slot<3 else 7772)
            value=image[address-0x60001000:address-0x60001000+length];assert len(value)==length and store.crc(value)==crc
            loaded.append(value)
        irs=loaded[:3];blobs=loaded[3:]
        from verify_tcm_ir import bootstrap
        boot=bootstrap(image)
        for folder,name in ((BUILD,'actor.elf'),(NAM/'dual-lab/lease-build','combined.elf')):
            for segment in audio.elf(folder/name)[0]:
                if segment['data'] and segment['flags']&1:assert bytes(boot.mem_read(segment['address'],segment['size']))==segment['data']
        assert bytes(boot.mem_read(ROM,len(seed)))==seed;package_reset=True
    actor.ROOT=LAB;results=[]
    editor=NAM.parent/'catalyst-editor';probe=output/'PreampProbe.exe'
    sources=[ROOT/'PreampProbe.cs']+[editor/n for n in ('PreampClient.cs','PreampJournal.cs','AssetMidiExchange.cs','AssetWire.cs','AssetTransfer.cs','AssetJournal.cs','AssetInfo.cs','DeviceFamily.cs')]
    compiled=subprocess.run(['C:/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe','/nologo','/utf8output','/reference:System.Web.Extensions.dll','/out:'+str(probe),*map(str,sources)],capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=45)
    (output/'preamp-probe-compile.log').write_text(compiled.stdout+compiled.stderr);assert compiled.returncode==0,compiled.stdout+compiled.stderr
    journal_folder=output/('preamp-journal-'+uuid.uuid4().hex[:10]);journal_folder.mkdir()
    identity='CFCX-UID2:10203040A0B0C0D0:'+edition.hex().upper()
    def run_client(h,mode):
        proc=subprocess.Popen([str(probe)],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8')
        proc.stdin.write(json.dumps(dict(root=str(journal_folder),mode=mode,identity=identity,nonce={'normal':900,'leave-pending':1900,'recover':2900}[mode]))+'\n');proc.stdin.flush();packets=[]
        while True:
            line=proc.stdout.readline();assert line,'Preamp probe exited without a result';row=json.loads(line)
            if row.get('done'):break
            request=base64.b64decode(row['packet']);_,message=wire.decode(lib,request);packets.append(message.opcode);before=len(h.packets);started=time.monotonic();h.feed(request);h.drain()
            assert len(h.packets)==before+1
            if message.opcode==11:
                files=list(journal_folder.glob('*.cfj'));assert len(files)==1
                recorded=files[0].read_bytes();assert len(recorded)==128 and struct.unpack_from('<I',recorded,28)[0]==1
                assert struct.unpack_from('<3I',recorded,12)==(message.session,message.transaction,message.slot)
            answer={'drop':True} if mode=='leave-pending' and message.opcode==11 else {'packet':base64.b64encode(h.packets[-1]).decode()}
            # Real FS-owner service continues even when the host loses a reply.
            for _ in range(40):h.m.put(actor.MESSAGE,8,0x31414643,0,0,0,0);h.call('cf_fs_dispatch',actor.MESSAGE);h.audio_block()
            _,reply=wire.decode(lib,h.packets[-1]);print('host exchange',mode,message.opcode,'seconds',round(time.monotonic()-started,3),'request',message.session,message.transaction,message.slot,message.number,'reply',reply.session,reply.transaction,reply.slot,reply.number,'bytes',reply.bytes,'status',struct.unpack_from('<I',bytes(reply.data))[0],flush=True)
            proc.stdin.write(json.dumps(answer)+'\n');proc.stdin.flush()
        proc.wait(timeout=8);assert proc.returncode==0 and row['passed'],row
        assert packets.count(11)==(0 if mode=='recover' else 1),packets
        return row
    def create(model=1,disk=None):
        original=actor.fsmod.NativeFS
        if disk is not None:actor.fsmod.NativeFS=lambda:original(disk)
        try:h=actor.Harness(initialize=False)
        finally:actor.fsmod.NativeFS=original
        m,u=h.m,h.u
        u.mem_map(0x401f4000,0x1000);m.put(0x401f4410,0x10203040);m.put(0x401f4420,0xa0b0c0d0)
        u.mem_map(0x400fc000,0x1000);m.put(0x400fc070,3<<12)
        u.mem_map(0xe0001000,0x1000);u.mem_write(0x60001000,image);u.mem_write(ROM,seed)
        u.mem_write(0x14000,image[0x601ac000-0x60001000:0x601ad000-0x60001000])
        segs,symbols=audio.elf(NAM/'dual-lab/lease-build/combined.elf')
        for seg in segs:
            if seg['data']:u.mem_write(seg['address'],seg['data'])
        runtime=fixture.get();runtime.bank_source[:]=[0,0];u.mem_write(audio.RUNTIME,bytes(runtime))
        for address,value in ((0x20062b38,0xa5a5a5a5),(0x20064880,0x5a5a5a5a),(0x200657e0,0xa7a7a7a7),(0x20065c80,0xa6a6a6a6),(0x20023498,600000000)):m.put(address,value)
        m.put(audio.ARENA+0x14c,128);m.put(audio.GRAPH+8,audio.ENTRIES,2);m.put(audio.GRAPH+20,0x80871000);m.put(0x80871000,*audio.signature('HD2_AmpImpossibleAC'))
        m.put(audio.ENTRIES,1,1,0,audio.IN,0,1,1,0,0,audio.OUT)
        state={'model':model,'model_ready':False,'receive':0,'waits':[],'clock':0,'UID_reads':0}
        def native(u,address,size,user):
            if address==0x8001d6e4:
                if state['model_ready']:u.mem_write(u.reg_read(UC_ARM_REG_R0),bytes([state['model'],0]));u.reg_write(UC_ARM_REG_R0,0)
                else:u.reg_write(UC_ARM_REG_R0,9)
            elif address==0x8004c8f8:
                state['waits'].append(u.reg_read(UC_ARM_REG_R2));u.reg_write(UC_ARM_REG_R0,state['receive'])
            else:return
            u.reg_write(UC_ARM_REG_PC,u.reg_read(UC_ARM_REG_LR))
        for address in (0x8001d6e4,0x8004c8f8):u.hook_add(UC_HOOK_CODE,native,begin=address,end=address)
        def clock(u,access,address,size,value,user):state['clock']+=70000;m.put(0xe0001004,state['clock']&0xffffffff)
        u.hook_add(UC_HOOK_MEM_READ,clock,begin=0xe0001004,end=0xe0001007)
        def uid(u,access,address,size,value,user):state['UID_reads']+=1
        u.hook_add(UC_HOOK_MEM_READ,uid,begin=0x401f4410,end=0x401f4423)
        def block():
            u.mem_write(audio.IN,bytes(128));h.call('cf_actor_audio_begin',audio.ARENA,audio.GRAPH)
            handled=m.call(symbols['trial_route']['address'],audio.ARENA,audio.GRAPH);m.call(symbols['trial_after']['address'],audio.ARENA,audio.GRAPH,handled)
            assert not audio.Combined.from_buffer_copy(bytes(u.mem_read(audio.RUNTIME,512))).fault
        h.owner_state=state;h.audio_block=block;return h
    with (output/'owner-loop-test.log').open('w') as log,contextlib.redirect_stdout(log):
        for model in (1,2,3):
            h=create(model);m,u=h.m,h.u;s=h.owner_state;before=bytes(h.fs.disk)
            assert h.call('cf_owner_bootstrap')==6 and s['UID_reads']==0
            u.mem_write(actor.FS_OBJECT,b'\x01\x01');assert h.call('cf_owner_bootstrap')==6 and s['UID_reads']==0
            s['model_ready']=True
            for clock_mode in (0,2):
                m.put(0x400fc070,clock_mode<<12);assert h.call('cf_owner_bootstrap')==6 and s['UID_reads']==0
                assert m.get(0x400fc070)==clock_mode<<12
            m.put(0x400fc070,3<<12);h.tracing(True);assert h.call('cf_owner_bootstrap')==0;h.tracing(False)
            assert h.call('cf_owner_stat',0)==1 and s['UID_reads']==2
            # Binding is boot-only and repeat calls do not read fuses or reset work.
            assert h.call('cf_owner_bootstrap')==0 and s['UID_reads']==2
            rc,reply=h.exchange(lib,wire.msg(8,session=0,tx=77)) if model==1 else (None,None)
            if model==1:assert rc==0 and bytes(reply.data[20:52])==edition
            if model==1:
                untouched=bytes(h.fs.disk);h.tracing(True)
                rc,diagnostic=h.exchange(lib,wire.msg(13,session=0,tx=78));h.tracing(False)
                assert rc==0 and diagnostic.bytes==256 and bytes(h.fs.disk)==untouched
                fields=struct.unpack('<64I',bytes(diagnostic.data[:256]))
                assert fields[:4]==(0,5,1,0) and fields[28:32]==(h.symbols['__cf_actor_bss_end']['address'],0x80872000,0x80874000,0x8087c000)
                for invalid in (wire.msg(13,session=1,tx=79),wire.msg(13,slot=1,session=0,tx=79),wire.msg(13,number=4,session=0,tx=79),wire.msg(13,session=0,tx=79,data=b'\x01')):
                    assert h.exchange(lib,invalid)[0]==2 and bytes(h.fs.disk)==untouched
                results.append('Fixed diagnostic snapshot reads readiness/cadence/cache/MPU only; malformed selectors and session cannot read arbitrary memory or write files')
            store_address=h.call('cf_actor_store_address');assert m.get(store_address+20)==(0x250000|model)
            if model==1:
                handoff=h.symbols['I']['address'];saved=bytes(u.mem_read(handoff,60));m.put(handoff+40,3)
                pending=bytes(u.mem_read(handoff,60))
                assert h.call('cf_ir_supersede',0,0x20018000)==1 and bytes(u.mem_read(handoff,60))==pending
                assert h.call('cf_ir_bank_request',100,0,0x80869000,16896)==1 and bytes(u.mem_read(handoff,60))==pending
                u.mem_write(handoff,saved)
                results.append('Audio selection cannot cancel or mutate an IR command interrupted during FS publication; atomic ownership defers without disabling interrupts')
            assert bytes(h.fs.disk)==before
            results.append('Model %d: deferred mount/model/OCOTP-clock readiness, no UID read while gated or reserved, bootstrap once without clock writes or file writes'%model)
            if model!=1:continue
            # Original timeout applies to foreign queues. Owned queue receives a
            # finite wait and all stale message words are cleared on timeout.
            message=0x83007000;u.mem_write(message,b'\xa5'*24)
            assert h.call('cf_fs_wait',actor.FS_QUEUE,message,0xffffffff)==0 and s['waits'][-1]==5
            assert bytes(u.mem_read(message,24))==struct.pack('<6I',8,0x31414643,0,0,0,0)
            assert h.call('cf_fs_wait',actor.FS_QUEUE+1,message,123)==0 and s['waits'][-1]==123
            s['receive']=1;original=struct.pack('<6I',1,2,3,4,5,6);u.mem_write(message,original)
            assert h.call('cf_fs_wait',actor.FS_QUEUE,message,0xffffffff)==1 and bytes(u.mem_read(message,24))==original
            s['receive']=0
            # Service the actual owner wrapper, never explicit coordinator init,
            # start, load or audio publish from this fixture.
            m.put(message,8,0x31414643,0,0,0,0)
            for step in range(400):h.call('cf_fs_dispatch',message);h.audio_block()
            runtime=audio.Combined.from_buffer_copy(bytes(u.mem_read(audio.RUNTIME,512)))
            for i in range(2):assert bytes(u.mem_read(runtime.bank_source[i],7772))==blobs[i]
            assert not h.call('cf_owner_stat',1) and not h.call('cf_coord_stat',2)
            assert bytes(h.fs.disk)==before
            results.append('Automatic FS service recovers and publishes both NAM seeds through outer audio boundaries without explicit fixture activation')
            # The real OEM SetData and Reset routines still run. Only their
            # old custom-copy callbacks are replaced by the owner observer.
            instance=0x20019020;node=0x20018000;data=0x20019c00;graph=0x20018000
            m.put(instance-0x14,node);m.put(node+0x14,graph);m.put(instance+4,0x2001a000,0x2001a800);m.put(instance+0x34,data,1)
            def select(slot,reset=False):
                m.put(data,slot);h.call('cf_actor_ir_reset' if reset else 'cf_actor_ir_set_data',instance)
            def idle(count):
                for _ in range(count):
                    h.call('cf_fs_wait',actor.FS_QUEUE,message,0xffffffff);h.call('cf_fs_dispatch',message);h.audio_block()
            # Native SetData consumes const metadata before our observer. A
            # valid graph can be DTCM while its slot metadata is in SDRAM/ROM.
            for metadata in (0x8085eb80,0x601dbf00):
                saved_metadata=bytes(u.mem_read(metadata,4));m.put(metadata,1);m.put(instance+0x34,metadata)
                h.call('cf_actor_ir_set_data',instance)
                assert h.call('cf_owner_stat',5)!=h.call('cf_owner_stat',6)
                idle(400);assert not h.call('cf_owner_stat',1) and m.get(0x20040004)==1
                u.mem_write(metadata,saved_metadata)
            m.put(instance+0x34,data)
            results.append('Native IR slot metadata in pinned SDRAM or ROM is accepted while graph validation remains DTCM-only')
            for slot in range(3):
                select(slot);assert h.call('cf_owner_stat',5)!=h.call('cf_owner_stat',6)
                idle(400);assert not h.call('cf_owner_stat',1)
                assert m.get(0x20040000)==0x52494358 and m.get(0x20040004)==slot
                assert bytes(u.mem_read(0x20045000,16896))==irs[slot][8192:]
            select(0);idle(9);select(1);idle(17);select(2);idle(400)
            assert not h.call('cf_owner_stat',1) and bytes(u.mem_read(0x20045000,16896))==irs[2][8192:]
            select(2,reset=True);idle(400)
            assert m.get(0x20040000)==0x52494358 and bytes(u.mem_read(0x20045000,16896))==irs[2][8192:]
            results.append('Native SetData/reset observer selects all three IRs; rapid selection cancels old preparation and the last selection wins')
            # Native requests continue through the same wrapper while idle work
            # is bounded; a native preset is not replaced by CF timeout service.
            preset=b'PROTECTED OWNER LOOP PRESET';u.mem_write(0x8300c100,preset)
            assert m.call(0x80016040,5,0,0x8300c100,len(preset),0x8301e901)==1;h.drain()
            assert h.native_receipts==[0] and h.fs.read('bank_0_channel_a',0,len(preset))==preset
            for step in range(20):
                assert h.call('cf_fs_wait',actor.FS_QUEUE,message,0xffffffff)==0
                h.call('cf_fs_dispatch',message);h.audio_block()
            assert h.native_receipts==[0]
            results.append('Original preset write reaches native dispatcher exactly once; idle receive never replays it')
            assert h.call('cf_coord_stat',0)==0 and h.call('cf_coord_stat',1)==0
            assert h.exchange(lib,wire.msg(1,session=0,tx=9))[0]==0
            data=blobs[0];assert h.exchange(lib,wire.msg(2,4,tx=222,number=1,data=struct.pack('<II',len(data),store.crc(data))+wire.metadata(4)))[0]==0
            for p in range(0,len(data),256):assert h.exchange(lib,wire.msg(3,4,tx=222,number=p,data=data[p:p+256]))[0]==0
            assert h.exchange(lib,wire.msg(4,4,tx=222))[0]==6
            m.put(message,8,0x31414643,0,0,0,0)
            for step in range(250):h.call('cf_fs_dispatch',message);h.audio_block()
            runtime=audio.Combined.from_buffer_copy(bytes(u.mem_read(audio.RUNTIME,512)));assert bytes(u.mem_read(runtime.bank_source[1],7772))==data
            assert bytes(u.mem_read(runtime.bank_source[0],7772))==blobs[0] and h.fs.read('bank_0_channel_a',0,len(preset))==preset
            results.append('Committed NAM 2 update triggers owner publication automatically and preserves NAM 1 and native preset')
            # Native bank/channel state is distinct from cabinet/microphone.
            # Use complete native-size synthetic files for this binding test.
            native_preset=b'\x01\x00\x00\x00'+bytes(0x214)
            h.fs.write('bank_0_channel_a',0,native_preset,True);m.put(0x80808fec,0);u.mem_write(0x80808fe8,b'\0');idle(100)
            assert h.call('cf_preamp_owner_stat',0)==1 and h.call('cf_preamp_owner_stat',2)==0
            rc,response=h.exchange(lib,wire.msg(9,slot=0,tx=400));assert rc==0 and response.bytes==44
            revision=struct.unpack_from('<I',bytes(response.data),8)[0]
            def fbits(value):return struct.unpack('<I',struct.pack('<f',value))[0]
            configuration=struct.pack('<8I',3,1,0,0,fbits(.5),fbits(.7),fbits(1.2),0)
            rc,response=h.exchange(lib,wire.msg(10,slot=0,tx=401,number=revision,data=configuration));assert rc==0
            revision=struct.unpack_from('<I',bytes(response.data),8)[0];h.audio_block()
            runtime=audio.Combined.from_buffer_copy(bytes(u.mem_read(audio.RUNTIME,512)));assert runtime.pedal.target.mode==3 and abs(runtime.pedal.target.tone-.7)<1e-6
            # The SET is volatile. Only explicit SAVE binds to the CRC of the
            # exact native file, after normal preset storage has completed.
            assert h.exchange(lib,wire.msg(11,slot=0,tx=402,number=revision))[0]==6;idle(100)
            assert h.exchange(lib,wire.msg(12,slot=0,tx=402))[0]==0
            assert h.fs.read('bank_0_channel_a',0,len(native_preset))==native_preset
            saved_disk=bytes(h.fs.disk)
            rc,snapshot=h.exchange(lib,wire.msg(9,tx=404));assert rc==0
            revision=struct.unpack_from('<I',bytes(snapshot.data),8)[0];changed=bytearray(configuration);struct.pack_into('<f',changed,20,.2)
            rc,snapshot=h.exchange(lib,wire.msg(10,slot=0,tx=405,number=revision,data=bytes(changed)));assert rc==0
            new_revision=struct.unpack_from('<I',bytes(snapshot.data),8)[0]
            rc,stored=h.exchange(lib,wire.msg(12,slot=0,tx=402));assert rc==0 and bytes(stored.data[12:44])==configuration
            assert h.exchange(lib,wire.msg(11,slot=0,tx=402,number=new_revision))[0]==3
            m.put(0x80808fec,1);h.audio_block();idle(100)
            runtime=audio.Combined.from_buffer_copy(bytes(u.mem_read(audio.RUNTIME,512)));assert runtime.pedal.target.mode==0
            assert h.exchange(lib,wire.msg(10,slot=0,tx=403,number=revision,data=configuration))[0]!=0
            m.put(0x80808fec,0);idle(100)
            runtime=audio.Combined.from_buffer_copy(bytes(u.mem_read(audio.RUNTIME,512)));assert runtime.pedal.target.mode==3
            # A real original FS write represents a restored/different preset:
            # stale custom state must be abandoned, not retried forever.
            restored=bytes([1,0,0,0,4])+bytes(0x213);u.mem_write(0x8300c100,restored)
            assert m.call(0x80016040,5,0,0x8300c100,len(restored),0x8301e901)==1;h.drain();idle(150)
            runtime=audio.Combined.from_buffer_copy(bytes(u.mem_read(audio.RUNTIME,512)))
            assert runtime.pedal.target.mode==0 and h.call('cf_preamp_owner_stat',0)==1 and not h.call('cf_preamp_owner_stat',1)
            assert h.fs.read('bank_0_channel_a',0,len(restored))==restored
            results.append('Preamp wire set/save/status binds native file CRC, follows bank/channel selection, rejects stale context and resets on native restore')
            results.append('SAVE status returns the persisted configuration, never a later unsaved edit; reusing a SAVE transaction with a new revision is rejected')
            assert run_client(h,'normal')['result']=='saved'
            assert run_client(h,'leave-pending')['pending']
            client_disk=bytes(h.fs.disk)
            assert actor.FS_TOP-h.fsmin<2048
            cold=create(disk=saved_disk);cold.u.mem_write(actor.FS_OBJECT,b'\x01\x01');cold.owner_state['model_ready']=True
            assert cold.call('cf_owner_bootstrap')==0;cold.m.put(actor.MESSAGE,8,0x31414643,0,0,0,0)
            for _ in range(400):cold.call('cf_fs_dispatch',actor.MESSAGE);cold.audio_block()
            restored_runtime=audio.Combined.from_buffer_copy(bytes(cold.u.mem_read(audio.RUNTIME,512)))
            assert restored_runtime.pedal.target.mode==3 and abs(restored_runtime.pedal.target.tone-.7)<1e-6
            assert bytes(cold.fs.disk)==saved_disk and not cold.call('cf_preamp_owner_stat',1)
            results.append('Fresh RAM/actor/filesystem on the same saved synthetic disk restores the per-preset preamp automatically, without another host SET')
            recover=create(disk=client_disk);recover.u.mem_write(actor.FS_OBJECT,b'\x01\x01');recover.owner_state['model_ready']=True
            assert recover.call('cf_owner_bootstrap')==0
            for _ in range(400):recover.m.put(actor.MESSAGE,8,0x31414643,0,0,0,0);recover.call('cf_fs_dispatch',actor.MESSAGE);recover.audio_block()
            assert run_client(recover,'recover')['result']=='reconciled'
            assert bytes(recover.fs.disk)==client_disk
            results.append('Production C# preamp client and persistent journal pass against actual ARM actor/native files; lost SAVE reply reconciles after fresh RAM without repeating SAVE')
        h=create(4);u=h.u;u.mem_write(actor.FS_OBJECT,b'\x01\x01');h.owner_state['model_ready']=True
        assert h.call('cf_owner_bootstrap')==2 and h.call('cf_owner_stat',0)==0 and h.owner_state['UID_reads']==0
        h=create();u=h.u;u.mem_write(actor.FS_OBJECT,b'\x01\x01');h.owner_state['model_ready']=True;h.m.put(0x401f4410,0);h.m.put(0x401f4420,0)
        assert h.call('cf_owner_bootstrap')!=0 and h.call('cf_owner_stat',0)==0
        results.append('Unknown model and invalid synthetic UID fail before readiness or data IO')
    report=dict(passed=True,hardware_accessed=False,USB_opened=False,SDK_loaded=False,firmware_created=False,MMIO_is_emulated=True,actor_elf_sha256=ai['elf_sha256'],audio_elf_sha256=di['elf_sha256'],checks=results,
        limitations=['Native RTOS timing, silicon UID provider, physical memory/cache/IRQ and real cold boot are not validated','Native IR instances and Main bank/channel fields are synthetic; host SDK/control transport integration remains pending'])
    report['package_sha256']=package_hash;report['packaged_reset_copy_verified']=package_reset
    report['verification_sha256']=hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    report['host_sources']={str(path.relative_to(ROOT.parents[2])):hashlib.sha256(path.read_bytes()).hexdigest() for path in sources}
    report['host_probe_sha256']=hashlib.sha256(probe.read_bytes()).hexdigest()
    report['emulated_metadata_reply_timeout_ms']=30000
    report['production_reply_timeout_ms']=1500
    report['limitations'].append('Metadata queries use an extended host deadline only in the emulator; this does not verify physical response latency')
    (output/'owner-loop-tests.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':main()
