"""Compiled C bounded staging tests. Synthetic files; no SDK or devices."""
import ctypes as C,hashlib,json,os,struct,subprocess,uuid
from pathlib import Path
import verify_store as store
ROOT=Path(__file__).resolve().parent;TV=ROOT.parents[2]
U=C.c_uint32;P=C.c_void_p
class Stage(C.Structure):
    _fields_=[('store',C.POINTER(store.State)),('best',store.Meta),('candidate',store.Meta),('destination',P)]+[(n,U) for n in 'capacity slot bank step offset rolling nonzero have_best active ready'.split()]
def main():
    out=ROOT/'checks'/('stage-'+uuid.uuid4().hex[:10]);out.mkdir(parents=True)
    env=os.environ.copy();env['ZIG_GLOBAL_CACHE_DIR']=str(out/'global-cache');env['ZIG_LOCAL_CACHE_DIR']=str(out/'local-cache')
    zig=TV/'.tools/zig-cross/zig-windows-x86_64-0.13.0/zig.exe'
    commands=[
      [zig,'cc','-O2','-Wall','-Wextra','-Werror','-shared','-target','x86_64-windows-gnu',ROOT/'asset_stage.c','-Wl,--export-all-symbols','-o',out/'stage.dll'],
      [zig,'cc','-Os','-Wall','-Wextra','-Werror','-ffreestanding','-fno-builtin','-fno-stack-protector','-target','thumb-freestanding-eabihf','-mcpu=cortex_m7+fp_armv8d16sp','-c',ROOT/'asset_stage.c','-o',out/'stage.arm.o']]
    for i,cmd in enumerate(commands):
        p=subprocess.run(list(map(str,cmd)),capture_output=True,text=True,env=env,timeout=180);(out/f'compile-{i}.log').write_text(p.stdout+p.stderr);assert p.returncode==0,p.stdout+p.stderr
    lib=C.CDLL(str(out/'stage.dll'))
    signature={
      'cf_asset_init':[C.POINTER(store.State),C.POINTER(store.IO),U],
      'cf_asset_recover':[C.POINTER(store.State),U,C.POINTER(store.Meta)],
      'cf_asset_begin':[C.POINTER(store.State),U,U,U,U,U],
      'cf_asset_chunk':[C.POINTER(store.State),U,U,P,U],
      'cf_asset_commit':[C.POINTER(store.State),U], 'cf_asset_pump':[C.POINTER(store.State)],
      'cf_stage_begin':[C.POINTER(Stage),C.POINTER(store.State),U,P,U],
      'cf_stage_pump':[C.POINTER(Stage)],'cf_stage_cancel':[C.POINTER(Stage)]}
    for name,args in signature.items():getattr(lib,name).argtypes=args;getattr(lib,name).restype=C.c_int
    assert lib.cf_stage_bytes()==C.sizeof(Stage)
    cases=[]
    for slot in range(5):
        d=store.Device(lib);old=store.payload(slot);new=store.payload(slot,1)
        assert d.upload(slot,old,10,0)==0 and d.upload(slot,new,11,1)==0
        target=(C.c_uint32*(len(old)//4+8))();address=C.addressof(target)+16
        C.memset(target,0xa5,C.sizeof(target));stage=Stage();before=bytes(d.disk)
        calls=len(d.calls);assert lib.cf_stage_begin(C.byref(stage),C.byref(d.state),slot,address,len(old))==6 and len(d.calls)==calls
        assert lib.cf_asset_begin(C.byref(d.state),12,slot,2,len(old),store.crc(old))==1
        rc=6;steps=0;max_reads=0
        while rc==6:
            calls=len(d.calls);rc=lib.cf_stage_pump(C.byref(stage));delta=d.calls[calls:];steps+=1
            assert len(delta)<=1 and all(op=='read' and n<=256 for op,n in delta),delta
            max_reads=max(max_reads,len(delta));assert stage.ready==int(rc==0)
        assert rc==0 and stage.best.generation==2 and C.string_at(address,len(new))==new
        assert C.string_at(C.addressof(target),16)==b'\xa5'*16 and C.string_at(address+len(new),16)==b'\xa5'*16
        assert bytes(d.disk)==before and not d.state.phase and not d.callback_errors
        # A corrupt newer payload falls back to the fully verified old bank.
        p=(slot*2+1)*32768+256;d.disk[p]^=1;d.work[:]=d.disk
        assert lib.cf_stage_begin(C.byref(stage),C.byref(d.state),slot,address,len(old))==6
        rc=6
        while rc==6:rc=lib.cf_stage_pump(C.byref(stage))
        assert rc==0 and stage.best.generation==1 and C.string_at(address,len(old))==old
        # Change storage after validation: destination never becomes ready.
        assert lib.cf_stage_begin(C.byref(stage),C.byref(d.state),slot,address,len(old))==6
        while stage.step!=2:assert lib.cf_stage_pump(C.byref(stage))==6
        d.work[slot*2*32768+256]^=2;rc=6
        while rc==6:rc=lib.cf_stage_pump(C.byref(stage))
        assert rc==5 and not stage.ready and not d.state.phase
        cases.append(dict(slot=slot,steps=steps,max_IO_calls_per_pump=max_reads,bytes_per_read_limit=256,old_generation_fallback=True,post_scan_mutation_rejected=True,guards_preserved=True))
    # Cancellation at every scan/copy boundary is non-writing and releases lease.
    d=store.Device(lib);data=store.payload(3);assert d.upload(3,data,9,0)==0;disk=bytes(d.disk)
    target=(U*(len(data)//4))();stage=Stage()
    for stop in range(70):
        assert lib.cf_stage_begin(C.byref(stage),C.byref(d.state),3,C.addressof(target),len(data))==6
        rc=6
        for _ in range(stop):
            rc=lib.cf_stage_pump(C.byref(stage))
            if rc!=6:break
        if rc==6:assert lib.cf_stage_cancel(C.byref(stage))==0 and not stage.ready
        assert not d.state.phase and bytes(d.disk)==disk
    # IO failure releases only its own lease, does not declare an empty slot.
    assert lib.cf_stage_begin(C.byref(stage),C.byref(d.state),3,C.addressof(target),len(data))==6
    d.failure=('read',0);assert lib.cf_stage_pump(C.byref(stage))==4 and not stage.ready and not d.state.phase
    # Invalid header/empty destination is reported distinctly.
    d=store.Device(lib);assert lib.cf_stage_begin(C.byref(stage),C.byref(d.state),3,C.addressof(target),len(data))==6
    assert lib.cf_stage_pump(C.byref(stage))==6 and lib.cf_stage_pump(C.byref(stage))==7
    report=dict(passed=True,hardware_accessed=False,USB_opened=False,SDK_loaded=False,firmware_created=False,stage_host_bytes=C.sizeof(Stage),cases=cases,cancel_boundaries=70,io_failure_releases_lease=True,empty_distinct_from_IO=True,build_folder=str(out),
                sources={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (ROOT/'asset_stage.c',ROOT/'asset_stage.h',ROOT/'asset_store.c',ROOT/'verify_stage.py')})
    (ROOT/'checks/stage-latest.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':main()
