"""Conditional recovery core, simulated files only. No wire/USB/firmware hook."""
import ctypes as C, hashlib, json, os, subprocess, uuid
from pathlib import Path
import verify_store as store
ROOT=Path(__file__).resolve().parent

def main():
    out=ROOT/('checks/recover-'+uuid.uuid4().hex[:10]);out.mkdir(parents=True)
    env=os.environ.copy();env['ZIG_GLOBAL_CACHE_DIR']=str(out/'global-cache');env['ZIG_LOCAL_CACHE_DIR']=str(out/'local-cache')
    zig=store.TV/'.tools/zig-cross/zig-windows-x86_64-0.13.0/zig.exe'
    sources=[ROOT/'asset_store.c',ROOT/'asset_recover.c']
    commands=[[zig,'cc','-O2','-Wall','-Wextra','-Werror','-shared','-target','x86_64-windows-gnu',*sources,'-Wl,--export-all-symbols','-o',out/'recover.dll'],
              [zig,'cc','-Os','-Wall','-Wextra','-Werror','-ffreestanding','-fno-builtin','-target','thumb-freestanding-eabihf','-mcpu=cortex_m7+fp_armv8d16sp','-c',ROOT/'asset_recover.c','-o',out/'recover.arm.o']]
    for i,args in enumerate(commands):
        result=subprocess.run(list(map(str,args)),capture_output=True,text=True,env=env,timeout=180);(out/f'compile-{i}.log').write_text(result.stdout+result.stderr);assert result.returncode==0,result.stdout+result.stderr
    lib=C.CDLL(str(out/'recover.dll'));U=store.U
    signatures={'cf_asset_init':[C.POINTER(store.State),C.POINTER(store.IO),U],
                'cf_asset_recover':[C.POINTER(store.State),U,C.POINTER(store.Meta)],
                'cf_asset_begin_with_info':[C.POINTER(store.State),U,U,U,U,U,C.POINTER(store.Info)],
                'cf_asset_chunk':[C.POINTER(store.State),U,U,C.c_void_p,U],
                'cf_asset_commit':[C.POINTER(store.State),U],
                'cf_asset_pump':[C.POINTER(store.State)],
                'cf_asset_abandon_receiving':[C.POINTER(store.State),U,U,C.POINTER(store.Meta)]}
    for name,args in signatures.items():getattr(lib,name).argtypes=args;getattr(lib,name).restype=C.c_int
    cases=[]
    def info():
        value=store.Info();text='NAM o IR · recuperación'.encode();value.name_bytes=len(text);value.name[:len(text)]=text;return value
    def begin(device,slot,tx,base,data):
        meta=info();assert lib.cf_asset_begin_with_info(C.byref(device.state),tx,slot,base,len(data),store.crc(data),C.byref(meta))==0
        return meta
    def chunk(device,tx,data):
        for offset in range(0,len(data),256):part=data[offset:offset+256];assert lib.cf_asset_chunk(C.byref(device.state),tx,offset,C.create_string_buffer(part),len(part))==0
    def commit(device,tx):
        assert lib.cf_asset_commit(C.byref(device.state),tx)==store.PENDING
        while lib.cf_asset_pump(C.byref(device.state))==store.PENDING:pass
    def expected(device):
        s=device.state;m=store.Meta();m.generation=s.generation;m.transaction=s.transaction;m.length=s.length;m.crc=s.expected;m.info=s.info;return m
    def call(device,slot,meta,product=store.PRODUCT):return lib.cf_asset_abandon_receiving(C.byref(device.state),product,slot,C.byref(meta))
    for slot in range(5):
        d=store.Device(lib);old=store.payload(slot);begin(d,slot,30,0,old);chunk(d,30,old);commit(d,30)
        other=(slot+1)%5;other_data=store.payload(other,2);begin(d,other,40,0,other_data);chunk(d,40,other_data);commit(d,40)
        before=bytes(d.disk);new=store.payload(slot,1);begin(d,slot,100+slot,1,new);assert lib.cf_asset_chunk(C.byref(d.state),100+slot,0,C.create_string_buffer(new[:256]),256)==0
        wanted=expected(d);state=bytes(d.state);disk=bytes(d.disk);work=bytes(d.work);calls=len(d.calls)
        mutations=[lambda m:setattr(m,'generation',m.generation+1),lambda m:setattr(m,'transaction',m.transaction+1),lambda m:setattr(m,'length',m.length+4),lambda m:setattr(m,'crc',m.crc^1),lambda m:setattr(m.info,'flags',m.info.flags^1),lambda m:m.info.source_sha256.__setitem__(0,1)]
        for mutate in mutations:
            wrong=store.Meta.from_buffer_copy(bytes(wanted));mutate(wrong);assert call(d,slot,wrong)==store.STALE
            assert bytes(d.state)==state and bytes(d.disk)==disk and bytes(d.work)==work and len(d.calls)==calls
        assert call(d,other,wanted)==store.STALE and call(d,slot,wanted,store.PRODUCT+1)==store.BAD
        assert call(d,slot,wanted)==store.OK and d.state.phase==0
        assert bytes(d.state)[C.sizeof(store.IO)+8:]==state[C.sizeof(store.IO)+8:],'Recovery changed state beyond phase'
        assert bytes(d.disk)==disk and bytes(d.work)==work and len(d.calls)==calls,'Recovery performed IO'
        rc,meta=d.recover(slot);assert rc==store.OK and meta.generation==1 and meta.transaction==30 and meta.crc==store.crc(old)
        rc,meta=d.recover(other);assert rc==store.OK and meta.transaction==40
        assert call(d,slot,wanted)==store.STALE and bytes(d.disk)==disk
        # Retrying after recovery can create a new generation normally.
        begin(d,slot,200+slot,1,new);chunk(d,200+slot,new);wanted=expected(d);assert lib.cf_asset_commit(C.byref(d.state),200+slot)==store.PENDING
        phase2=bytes(d.state);disk2=bytes(d.disk);calls2=len(d.calls);assert call(d,slot,wanted)==store.BUSY
        assert bytes(d.state)==phase2 and bytes(d.disk)==disk2 and len(d.calls)==calls2
        while lib.cf_asset_pump(C.byref(d.state))==store.PENDING:pass
        rc,meta=d.recover(slot);assert rc==store.OK and meta.generation==2 and meta.transaction==200+slot
        assert call(d,slot,wanted)==store.STALE
        # A loader read lease is never released by recovery.
        d.state.phase=3;phase3=bytes(d.state);calls3=len(d.calls);assert call(d,slot,wanted)==store.BUSY
        assert bytes(d.state)==phase3 and len(d.calls)==calls3
        cases.append(dict(slot=slot,partial_receive_only=True,strict_receipt_match=True,no_IO_on_recovery=True,old_generation_preserved=True,other_slot_preserved=True,commit_verification_not_canceled=True,loader_lease_not_released=True,reupload_after_recovery=True))
    report=dict(passed=True,hardware_accessed=False,SDK_loaded=False,USB_opened=False,firmware_created=False,wire_opcode_added=False,native_hook_added=False,compiled_for_cortex_m7=True,cases=cases,
                sources={str(p.relative_to(store.TV)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources+[ROOT/'asset_store.h',ROOT/'asset_recover.h',ROOT/'verify_recover.py']},
                limitation='Requires serialized control owner; no concurrency/IRQ/native integration or physical recovery tested')
    (out/'result.json').write_text(json.dumps(report,indent=2));(ROOT/'checks/recover-latest.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':main()
