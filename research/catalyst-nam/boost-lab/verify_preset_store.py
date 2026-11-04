"""Dedicated preamp sidecars: compiled C, synthetic disks and torn writes."""
import ctypes as C,hashlib,json,os,struct,subprocess,sys,uuid
from pathlib import Path
ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent;TV=NAM.parents[1]
sys.path.insert(0,str(NAM/'asset-lab'))
import verify_store as assets
U=C.c_uint32;P=C.c_void_p
READ=C.CFUNCTYPE(C.c_int,P,U,U,P,U);ERASE=C.CFUNCTYPE(C.c_int,P,U);WRITE=C.CFUNCTYPE(C.c_int,P,U,U,P,U);SYNC=C.CFUNCTYPE(C.c_int,P)
class IO(C.Structure):_fields_=[('user',P),('read',READ),('erase',ERASE),('write',WRITE),('sync',SYNC)]
class Record(C.Structure):_fields_=[(n,U) for n in 'mode enabled boost bright drive_bits tone_bits level_bits preset_crc'.split()]
class State(C.Structure):_fields_=[('io',IO)]+[(n,U) for n in 'product phase bank offset rolling generation transaction mask best_bank have_best candidate_generation candidate_transaction candidate_mask candidate_crc last_preset candidate_preset'.split()]+[('table',C.c_ubyte*416),('candidate',C.c_ubyte*416),('header',C.c_ubyte*64)]
def bits(f):return struct.unpack('<I',struct.pack('<f',f))[0]
class Disk:
    def __init__(self,lib,data=None):
        self.lib=lib;self.data=bytearray(b'\xff'*960 if data is None else data);self.calls=[];self.fail_after=None;self.error=None
        self.callbacks=[READ(self.read),ERASE(self.erase),WRITE(self.write),SYNC(self.sync)];self.io=IO(None,*self.callbacks);self.open()
    def open(self):
        self.state=State();assert self.lib.cf_preamp_presets_open(C.byref(self.state),C.byref(self.io),assets.PRODUCT)==6
        assert self.pump()==0
    def at(self,bank,offset,n):assert bank<2 and offset<=480 and n<=480-offset;return bank*480+offset
    def read(self,user,bank,offset,out,n):
        try:p=self.at(bank,offset,n);self.calls.append(('read',n));C.memmove(out,bytes(self.data[p:p+n]),n);return 0
        except BaseException as e:self.error=str(e);return 1
    def erase(self,user,bank):
        try:p=self.at(bank,0,480);self.data[p:p+480]=b'\xff'*480;self.calls.append(('erase',480));return 0
        except BaseException as e:self.error=str(e);return 1
    def write(self,user,bank,offset,data,n):
        try:
            p=self.at(bank,offset,n);self.calls.append(('write',n));count=n if self.fail_after is None else min(n,self.fail_after)
            self.data[p:p+count]=C.string_at(data,count)
            if self.fail_after is not None:self.fail_after-=count
            return int(count!=n)
        except BaseException as e:self.error=str(e);return 1
    def sync(self,user):self.calls.append(('sync',0));return 0
    def pump(self):
        rc=6;count=0
        while rc==6:
            before=len(self.calls);rc=self.lib.cf_preamp_presets_pump(C.byref(self.state));delta=self.calls[before:]
            assert len(delta)<=1 and all(op not in ('read','write') or n<=256 for op,n in delta)
            count+=1;assert count<40
        assert self.error is None;return rc
    def save(self,index,tx,record):
        rc=self.lib.cf_preamp_presets_save(C.byref(self.state),index,tx,C.byref(record));return self.pump() if rc==6 else rc
    def get(self,index,checksum):
        out=Record();rc=self.lib.cf_preamp_presets_get(C.byref(self.state),index,checksum,C.byref(out));return rc,bytes(out)
def main():
    out=ROOT/'checks'/('presets-'+uuid.uuid4().hex[:10]);out.mkdir(parents=True)
    env=os.environ.copy();env['ZIG_GLOBAL_CACHE_DIR']=str(out/'global-cache');env['ZIG_LOCAL_CACHE_DIR']=str(out/'local-cache')
    zig=TV/'.tools/zig-cross/zig-windows-x86_64-0.13.0/zig.exe'
    commands=[
      [zig,'cc','-O2','-Wall','-Wextra','-Werror','-shared','-target','x86_64-windows-gnu',ROOT/'preset_store.c',NAM/'asset-lab/asset_store.c','-Wl,--export-all-symbols','-o',out/'presets.dll'],
      [zig,'cc','-Os','-Wall','-Wextra','-Werror','-ffreestanding','-fno-builtin','-fno-stack-protector','-target','thumb-freestanding-eabihf','-mcpu=cortex_m7+fp_armv8d16sp','-c',ROOT/'preset_store.c','-o',out/'presets.arm.o']]
    for i,cmd in enumerate(commands):
        p=subprocess.run(list(map(str,cmd)),capture_output=True,text=True,env=env,timeout=180);(out/f'compile-{i}.log').write_text(p.stdout+p.stderr);assert p.returncode==0,p.stdout+p.stderr
    lib=C.CDLL(str(out/'presets.dll'))
    for name,args in {'cf_preamp_presets_open':[C.POINTER(State),C.POINTER(IO),U],'cf_preamp_presets_pump':[C.POINTER(State)],'cf_preamp_presets_save':[C.POINTER(State),U,U,C.POINTER(Record)],'cf_preamp_presets_get':[C.POINTER(State),U,U,C.POINTER(Record)]}.items():getattr(lib,name).argtypes=args;getattr(lib,name).restype=C.c_int
    assert lib.cf_preamp_presets_bytes()==C.sizeof(State)
    disk=Disk(lib);records=[]
    for i in range(13):
        record=Record(i%4,i%2,(i//2)%2,(i//3)%2,bits(i/12),bits(.6),bits(1.1),0x12340000+i);records.append(record)
        assert disk.save(i,100+i,record)==0
    disk.open()
    for i,r in enumerate(records):assert disk.get(i,r.preset_crc)==(0,bytes(r)) and disk.get(i,r.preset_crc^1)[0]==3
    seed=bytes(disk.data);new=Record(3,1,0,1,bits(.25),bits(.7),bits(1.3),records[5].preset_crc)
    assert disk.save(5,200,new)==0 and disk.save(5,200,new)==8
    assert disk.save(4,200,records[4])==3
    changed=Record.from_buffer_copy(bytes(new));changed.mode=1;assert disk.save(5,200,changed)==3
    # Every possible byte boundary in the two data writes and header write.
    torn=[]
    for cut in range(481):
        d=Disk(lib,seed);d.fail_after=cut;rc=d.save(5,300,new);assert rc in (0,4,9)
        recovered=Disk(lib,d.data);result=recovered.get(5,new.preset_crc)
        assert result in ((0,bytes(records[5])),(0,bytes(new))),cut
        for i,r in enumerate(records):
            if i!=5:assert recovered.get(i,r.preset_crc)==(0,bytes(r)),(cut,i)
        torn.append(result[1]==bytes(new))
    # Invalid config cannot start IO or mutate any record.
    d=Disk(lib,seed);before=bytes(d.data);calls=len(d.calls)
    for mode,drive,tone,level in ((4,.5,.5,1),(1,float('nan'),.5,1),(1,.5,-.1,1),(1,.5,.5,2.1)):
        invalid=Record(mode,1,0,0,bits(drive),bits(tone),bits(level),1);assert d.save(1,400,invalid)==2
    assert bytes(d.data)==before and len(d.calls)==calls
    # Corrupt the newest table after successful save: reopen selects old valid.
    d=Disk(lib,seed);assert d.save(5,500,new)==0;bank=d.state.best_bank;d.data[bank*480+64+5*32]^=1;d.open();assert d.get(5,records[5].preset_crc)==(0,bytes(records[5]))
    report=dict(passed=True,hardware_accessed=False,USB_opened=False,SDK_loaded=False,firmware_created=False,preset_count=13,host_state_bytes=C.sizeof(State),torn_write_boundaries=len(torn),all_other_presets_preserved=True,native_preset_crc_binding=True,invalid_configs_do_not_write=True,corrupt_latest_falls_back=True,IO_limit=256,
        native_filesystem_binding_pending=True,SDK_save_and_native_preset_CRC_binding_pending=True,build_folder=str(out),
        sources={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (ROOT/'preset_store.c',ROOT/'preset_store.h',Path(__file__))})
    (ROOT/'checks/presets-latest.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':main()
