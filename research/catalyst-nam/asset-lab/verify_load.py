"""Stage a committed bank with bounded reads, exclusively on synthetic IO."""
import ctypes as C,hashlib,json,os,struct,subprocess,uuid
from pathlib import Path
import verify_store as store
import verify_wire as wire
ROOT=Path(__file__).resolve().parent
class Load(C.Structure):
    _fields_=[('store',C.POINTER(store.State)),('meta',store.Meta),('destination',C.c_void_p)]+[(n,store.U) for n in ('capacity','slot','offset','rolling','ready')]

def main():
    out=ROOT/('checks/load-'+uuid.uuid4().hex[:10]);out.mkdir(parents=True)
    env=os.environ.copy();env['ZIG_GLOBAL_CACHE_DIR']=str(out/'global-cache');env['ZIG_LOCAL_CACHE_DIR']=str(out/'local-cache')
    sources=[ROOT/n for n in ('asset_store.c','asset_wire.c','asset_load.c')];zig=store.TV/'.tools/zig-cross/zig-windows-x86_64-0.13.0/zig.exe'
    commands=[[zig,'cc','-O2','-Wall','-Wextra','-Werror','-shared','-target','x86_64-windows-gnu',*sources,'-Wl,--export-all-symbols','-o',out/'load.dll'],
        [zig,'cc','-Os','-Wall','-Wextra','-Werror','-ffreestanding','-fno-builtin','-fno-stack-protector','-target','thumb-freestanding-eabihf','-mcpu=cortex_m7+fp_armv8d16sp','-c',ROOT/'asset_load.c','-o',out/'load.arm.o']]
    for i,command in enumerate(commands):
        p=subprocess.run(list(map(str,command)),capture_output=True,text=True,env=env,timeout=180);(out/f'compile-{i}.log').write_text(p.stdout+p.stderr);assert p.returncode==0,p.stdout+p.stderr
    lib=C.CDLL(str(out/'load.dll'))
    signatures={'cf_asset_init':[C.POINTER(store.State),C.POINTER(store.IO),store.U],
        'cf_asset_recover':[C.POINTER(store.State),store.U,C.POINTER(store.Meta)],'cf_asset_begin':[C.POINTER(store.State)]+[store.U]*5,
        'cf_asset_chunk':[C.POINTER(store.State),store.U,store.U,C.c_void_p,store.U],
        'cf_asset_commit':[C.POINTER(store.State),store.U],'cf_asset_pump':[C.POINTER(store.State)],'cf_asset_cancel':[C.POINTER(store.State),store.U],
        'cf_load_begin':[C.POINTER(Load),C.POINTER(store.State),store.U,C.c_void_p,store.U],
        'cf_load_pump':[C.POINTER(Load)],'cf_load_cancel':[C.POINTER(Load)],
        'cf_receiver_init':[C.POINTER(wire.Receiver),C.POINTER(store.State),store.U],
        'cf_receiver_handle':[C.POINTER(wire.Receiver),C.POINTER(wire.Message),C.POINTER(wire.Message)]}
    for name,args in signatures.items():getattr(lib,name).argtypes=args;getattr(lib,name).restype=C.c_int
    d=store.Device(lib);data=[store.payload(slot) for slot in range(5)]
    for slot,b in enumerate(data):assert d.upload(slot,b,100+slot,0)==0
    original=bytes(d.disk);checks=[]
    for slot,b in enumerate(data):
        buffer=C.create_string_buffer(b'\xa7'*(len(b)+32),len(b)+32);pointer=C.addressof(buffer)+16;loader=Load()
        assert lib.cf_load_begin(C.byref(loader),C.byref(d.state),slot,pointer,len(b)-1)==store.BAD
        assert d.state.phase==0
        assert lib.cf_load_begin(C.byref(loader),C.byref(d.state),slot,pointer,len(b))==store.PENDING and not loader.ready
        # The read lease blocks receiver writes and cannot be released by CANCEL.
        assert d.begin(slot,b,200,1)==store.BUSY
        assert lib.cf_asset_cancel(C.byref(d.state),104)==store.BUSY and d.state.phase==3
        receiver=wire.Receiver();assert lib.cf_receiver_init(C.byref(receiver),C.byref(d.state),1)==0
        ack=wire.Message();hello=wire.msg(1,tx=9,session=0)
        assert lib.cf_receiver_handle(C.byref(receiver),C.byref(hello),C.byref(ack))==store.BUSY
        receiver.session=9;status=wire.msg(5,slot,100+slot)
        assert lib.cf_receiver_handle(C.byref(receiver),C.byref(status),C.byref(ack))==store.BUSY
        rc=store.PENDING;steps=0
        while rc==store.PENDING:
            previous=loader.offset;calls=len(d.calls);rc=lib.cf_load_pump(C.byref(loader));steps+=1
            assert 0<=loader.offset-previous<=256
            assert len(d.calls)-calls<=1
            if rc==store.PENDING:assert not loader.ready
        assert rc==store.OK and loader.ready and loader.meta.transaction==100+slot and d.state.phase==0
        assert buffer.raw[:16]==b'\xa7'*16 and buffer.raw[16:16+len(b)]==b and buffer.raw[-16:]==b'\xa7'*16
        assert bytes(d.disk)==original
        checks.append(dict(slot=slot,bytes=len(b),steps=steps,max_payload_read=256,ready_only_after_crc=True))
    # Staging failure/cancellation can never expose a partial bank as ready.
    for mode in ('cancel','read_error','changed_after_recover'):
        d.disk[:]=original;d.work[:]=original;buffer=C.create_string_buffer(25088);loader=Load()
        assert lib.cf_load_begin(C.byref(loader),C.byref(d.state),0,C.addressof(buffer),25088)==store.PENDING
        if mode=='cancel':
            assert lib.cf_load_pump(C.byref(loader))==store.PENDING
            assert lib.cf_load_cancel(C.byref(loader))==store.OK
        else:
            if mode=='read_error':d.failure=('read',0)
            else:d.disk[store.HEADER+64]^=1;d.work[store.HEADER+64]^=1
            rc=store.PENDING
            while rc==store.PENDING:rc=lib.cf_load_pump(C.byref(loader))
            assert rc==(store.ERROR if mode=='read_error' else store.INVALID)
        assert not loader.ready and d.state.phase==0
        d.failure=None;d.disk[:]=original;d.work[:]=original
        assert d.recover(0)[0]==store.OK
    report=dict(passed=True,hardware_accessed=False,USB_opened=False,SDK_loaded=False,audio_activation=False,slots=checks,
        staging_failures_not_published=['cancel','read_error','changed_after_recover'],writes_blocked_during_read_lease=True,buffer_guards_preserved=True,
        sources={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in sources+[ROOT/'asset_store.h',ROOT/'asset_load.h',ROOT/'verify_load.py']},
        limitations=['No physical storage','Staging memory not allocated in a firmware image','Audio quiesce/publication not implemented','BEGIN recovery reads complete banks; no timing proof'])
    (out/'result.json').write_text(json.dumps(report,indent=2));(ROOT/'checks/load-latest.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':
    try:main()
    except BaseException:
        import traceback
        (ROOT/'checks/load-error.txt').write_text(traceback.format_exc());raise
