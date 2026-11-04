"""Build/test only the isolated asset store with simulated persistent banks.

No driver, device API, firmware packager, user model or commercial IR is used.
Fault tests exercise the compiled C implementation, not a Python reimplementation.
"""
import ctypes as C
import hashlib,json,math,os,random,struct,subprocess,uuid,zlib
from pathlib import Path
ROOT=Path(__file__).resolve().parent
TV=ROOT.parents[2]
U=C.c_uint32
PTR=C.c_void_p
READ=C.CFUNCTYPE(C.c_int,PTR,U,U,U,PTR,U)
ERASE=C.CFUNCTYPE(C.c_int,PTR,U,U)
WRITE=C.CFUNCTYPE(C.c_int,PTR,U,U,U,PTR,U)
SYNC=C.CFUNCTYPE(C.c_int,PTR)
class IO(C.Structure):_fields_=[('user',PTR),('read',READ),('erase',ERASE),('write',WRITE),('sync',SYNC)]
HEADER=256
class Info(C.Structure):_fields_=[(n,U) for n in ('flags','input_db_bits','output_db_bits','low_hz_bits','high_hz_bits','name_bytes')]+[('name',C.c_ubyte*128),('source_sha256',C.c_ubyte*32)]
class Meta(C.Structure):_fields_=[(n,U) for n in ('generation','transaction','length','crc','bank')]+[('info',Info)]
class State(C.Structure):_fields_=[('io',IO)]+[(n,U) for n in 'product phase transaction slot bank generation length expected received rolling checked verified nonzero'.split()]+[('scratch',C.c_ubyte*256),('info',Info)]
OK,BUSY,BAD,STALE,ERROR,INVALID,PENDING,EMPTY,ALREADY,UNCERTAIN=range(10)
PRODUCT=2424833
def payload(slot,variant=0):
    length=25088 if slot<3 else 7772
    return struct.pack('<%df'%(length//4),*[.1*math.sin((i+1)*(variant+1)*.013) for i in range(length//4)])
def crc(b):return zlib.crc32(b)&0xffffffff
class Device:
    def __init__(self,lib,snapshot=None,buffered=False):
        self.lib=lib;self.disk=bytearray(snapshot) if snapshot is not None else bytearray(b'\xff'*(5*2*32768))
        self.work=bytearray(self.disk);self.buffered=buffered;self.failure=None;self.calls=[];self.callback_errors=[]
        self.read=READ(self._read);self.erase=ERASE(self._erase);self.write=WRITE(self._write);self.sync=SYNC(self._sync)
        self.io=IO(None,self.read,self.erase,self.write,self.sync);self.start()
    def start(self):
        self.state=State();assert self.lib.cf_asset_init(C.byref(self.state),C.byref(self.io),PRODUCT)==OK
    def reboot(self):self.failure=None;self.work[:]=self.disk;self.start()
    def _base(self,slot,bank,offset=0,n=0):
        assert slot<5 and bank<2 and offset<=32768 and n<=32768-offset
        return (slot*2+bank)*32768+offset
    def _limit(self,op,n):
        self.calls.append((op,n))
        if self.failure is None or self.failure[0]!=op:return n,False
        left=self.failure[1]
        if left>=n:self.failure=(op,left-n);return n,False
        self.failure=None;return left,True
    def _read(self,user,slot,bank,offset,out,n):
        try:
            p=self._base(slot,bank,offset,n);m,fail=self._limit('read',n);C.memmove(out,bytes(self.work[p:p+m]),m)
            return int(fail)
        except BaseException as e:self.callback_errors.append(str(e));return 1
    def _erase(self,user,slot,bank):
        try:
            p=self._base(slot,bank);n,fail=self._limit('erase',32768);self.work[p:p+n]=b'\xff'*n
            if not self.buffered:self.disk[:]=self.work
            return int(fail)
        except BaseException as e:self.callback_errors.append(str(e));return 1
    def _write(self,user,slot,bank,offset,data,n):
        try:
            p=self._base(slot,bank,offset,n);m,fail=self._limit('write',n);b=C.string_at(data,m)
            # NOR-like model: no 0->1 changes without erase.
            assert all((self.work[p+i]&v)==v for i,v in enumerate(b))
            self.work[p:p+m]=b
            if not self.buffered:self.disk[:]=self.work
            return int(fail)
        except BaseException as e:self.callback_errors.append(str(e));return 1
    def _sync(self,user):
        _,fail=self._limit('sync',1)
        if not fail:self.disk[:]=self.work
        return int(fail)
    def recover(self,slot):
        m=Meta();rc=self.lib.cf_asset_recover(C.byref(self.state),slot,C.byref(m))
        return rc,m
    def begin(self,slot,b,tx,generation):return self.lib.cf_asset_begin(C.byref(self.state),tx,slot,generation,len(b),crc(b))
    def chunk(self,tx,offset,b):return self.lib.cf_asset_chunk(C.byref(self.state),tx,offset,b,len(b))
    def upload(self,slot,b,tx,generation,finish=True):
        rc=self.begin(slot,b,tx,generation)
        if rc!=OK:return rc
        for p in range(0,len(b),256):
            rc=self.chunk(tx,p,b[p:p+256])
            if rc!=OK:return rc
        rc=self.lib.cf_asset_commit(C.byref(self.state),tx)
        if not finish:return rc
        while rc==PENDING:rc=self.lib.cf_asset_pump(C.byref(self.state))
        return rc
    def content(self,slot,m):
        p=self._base(slot,m.bank,HEADER,m.length);return bytes(self.disk[p:p+m.length])

def compile(out):
    zig=TV/'.tools/zig-cross/zig-windows-x86_64-0.13.0/zig.exe'
    env=os.environ.copy();env['ZIG_GLOBAL_CACHE_DIR']=str(out/'global-cache');env['ZIG_LOCAL_CACHE_DIR']=str(out/'local-cache')
    source=ROOT/'asset_store.c';dll=out/'asset_store.dll'
    commands=[
      [zig,'cc','-O2','-Wall','-Wextra','-Werror','-shared','-target','x86_64-windows-gnu',source,'-Wl,--export-all-symbols','-o',dll],
      [zig,'cc','-Os','-Wall','-Wextra','-Werror','-ffreestanding','-fno-builtin','-fno-stack-protector','-target','thumb-freestanding-eabihf','-mcpu=cortex_m7+fp_armv8d16sp','-c',source,'-o',out/'asset_store.arm.o']]
    for i,args in enumerate(commands):
        p=subprocess.run(list(map(str,args)),capture_output=True,text=True,env=env,timeout=180)
        (out/f'compile-{i}.log').write_text(p.stdout+p.stderr)
        assert p.returncode==0,p.stdout+p.stderr
    lib=C.CDLL(str(dll))
    signatures={
      'cf_asset_init':[C.POINTER(State),C.POINTER(IO),U],
      'cf_asset_recover':[C.POINTER(State),U,C.POINTER(Meta)],
      'cf_asset_begin':[C.POINTER(State),U,U,U,U,U],
      'cf_asset_begin_with_info':[C.POINTER(State),U,U,U,U,U,C.POINTER(Info)],
      'cf_asset_chunk':[C.POINTER(State),U,U,PTR,U],
      'cf_asset_commit':[C.POINTER(State),U],
      'cf_asset_pump':[C.POINTER(State)],'cf_asset_cancel':[C.POINTER(State),U],
      'cf_crc32':[U,PTR,U],'cf_asset_store_bytes':[]}
    for name,args in signatures.items():
        f=getattr(lib,name);f.argtypes=args;f.restype=U if name in ('cf_crc32','cf_asset_store_bytes') else C.c_int
    assert lib.cf_asset_store_bytes()==C.sizeof(State)
    assert (lib.cf_crc32(0xffffffff,b'123456789',9)^0xffffffff)==0xcbf43926
    return lib

def run(lib):
    checks=[];d=Device(lib)
    assert all(d.recover(i)[0]==EMPTY for i in range(5))
    for i in range(5):
        data=payload(i);assert d.upload(i,data,100+i,0)==OK
        d.reboot();rc,m=d.recover(i);assert rc==OK and m.generation==1 and m.transaction==100+i and d.content(i,m)==data
    baseline=bytes(d.disk);checks.append('Five independent slots: complete commit, readback and cold reconstruction; synthetic data only')
    for i in range(5):
        v=Device(lib,baseline);data=payload(i,1)
        assert v.begin(i,data,200+i,0)==STALE
        assert bytes(v.disk)==baseline
        assert v.begin(i,data,200+i,1)==OK
        assert v.begin(i,data,200+i,1)==ALREADY
        assert v.begin(i,data,900,1)==BUSY
        assert v.chunk(200+i,256,data[:256])==STALE
        assert v.chunk(999,0,data[:256])==BAD
        assert v.chunk(200+i,0,data[:256])==OK
        assert v.chunk(200+i,0,data[:256])==ALREADY
        assert v.chunk(200+i,0,bytes(256))==STALE
        assert v.recover(i)[0]==BUSY
        for p in range(256,len(data),256):assert v.chunk(200+i,p,data[p:p+256])==OK
        assert lib.cf_asset_commit(C.byref(v.state),200+i)==PENDING
        assert lib.cf_asset_commit(C.byref(v.state),200+i)==PENDING
        rc=PENDING;steps=0
        while rc==PENDING:
            checked=v.state.checked;rc=lib.cf_asset_pump(C.byref(v.state));steps+=1
            assert v.state.checked-checked<=256
        assert rc==OK and steps==(len(data)+255)//256+1
        v.reboot();rc,m=v.recover(i);assert rc==OK and m.generation==2 and v.content(i,m)==data
        before=bytes(v.disk);assert v.begin(i,data,200+i,1)==ALREADY and bytes(v.disk)==before
        for j in range(5):
            if j!=i:assert v.disk[j*65536:(j+1)*65536]==baseline[j*65536:(j+1)*65536]
    checks.append('Retries, stale generations, wrong transactions, out-of-order chunks, bounded verification and preservation of every other slot')
    for slot in (0,3):
        for data in (bytes(len(payload(slot))),struct.pack('<I',0x7fc00000)+payload(slot)[4:],struct.pack('<f',float('inf'))+payload(slot)[4:]):
            v=Device(lib,baseline);assert v.upload(slot,data,300,1)==INVALID
            v.reboot();rc,m=v.recover(slot);assert rc==OK and m.generation==1
        v=Device(lib,baseline);data=payload(slot,2)
        assert v.begin(slot,data,301,1)==OK
        for p in range(0,len(data),256):assert v.chunk(301,p,bytes(min(256,len(data)-p)))==OK
        assert lib.cf_asset_commit(C.byref(v.state),301)==INVALID
        v.reboot();assert v.recover(slot)[1].generation==1
    checks.append('NaN, infinity, silence and content CRC mismatch rejected without losing the previous slot')
    powercuts=0
    for buffered in (False,True):
        for slot in (0,3):
            data=payload(slot,3)
            for cutoff in (0,1,63,255,256,257,len(data)//2,len(data)-1,len(data),len(data)+1,len(data)+32,len(data)+63,len(data)+64):
                v=Device(lib,baseline,buffered);v.failure=('write',cutoff)
                rc=v.upload(slot,data,400,1)
                assert rc in (OK,ERROR,UNCERTAIN)
                v.reboot();rc,m=v.recover(slot);assert rc==OK and m.generation in (1,2)
                assert v.content(slot,m)==(payload(slot) if m.generation==1 else data)
                if rc==OK and m.generation==2:assert m.transaction==400
                powercuts+=1
            # Every byte boundary of the final header, with old bank intact.
            for cutoff in range(HEADER):
                v=Device(lib,baseline,buffered);assert v.upload(slot,data,401,1,False)==PENDING
                while v.state.checked<len(data):assert lib.cf_asset_pump(C.byref(v.state))==PENDING
                v.failure=('write',cutoff);assert lib.cf_asset_pump(C.byref(v.state))==UNCERTAIN
                v.reboot();rc,m=v.recover(slot);assert rc==OK and m.generation in (1,2)
                assert v.content(slot,m)==(payload(slot) if m.generation==1 else data);powercuts+=1
            for op,limits in [('erase',(0,1,100,32767)),('sync',(0,1,2,3))]:
                for cutoff in limits:
                    v=Device(lib,baseline,buffered);v.failure=(op,cutoff);rc=v.upload(slot,data,402,1)
                    assert rc in (OK,ERROR,UNCERTAIN)
                    v.reboot();rc,m=v.recover(slot);assert rc==OK and m.generation in (1,2)
                    assert v.content(slot,m)==(payload(slot) if m.generation==1 else data);powercuts+=1
    checks.append(f'{powercuts} simulated interrupted writes/erases/syncs including every final-header byte boundary, with immediate and buffered durability')
    for slot in (0,4):
        data=payload(slot,4)
        for point in (0,256,len(data)):
            v=Device(lib,baseline);assert v.begin(slot,data,500,1)==OK
            for p in range(0,point,256):assert v.chunk(500,p,data[p:min(point,p+256)])==OK
            assert lib.cf_asset_cancel(C.byref(v.state),500)==OK
            v.reboot();assert v.recover(slot)[1].generation==1
    checks.append('Cancellation before/during/after payload keeps the committed generation')
    # Post-commit corruption must fall back to the previous bank.
    for offset in (0,20,60,252,HEADER,512,HEADER+7771):
        v=Device(lib,baseline);data=payload(3,6);assert v.upload(3,data,600,1)==OK
        p=v._base(3,1,offset);v.disk[p]^=1;v.reboot();rc,m=v.recover(3);assert rc==OK and m.generation==1
    checks.append('Corrupted new headers/payloads fall back to the previous valid generation')
    for product in (2424834,2424835):
        v=Device(lib,baseline);v.state.product=product
        assert all(v.recover(i)[0]==EMPTY for i in range(5));assert bytes(v.disk)==baseline
    # Store display/calibration metadata in the unit record, without PC paths.
    v=Device(lib,baseline);info=Info();name='NAM 5150 · válvulas'.encode();info.name_bytes=len(name);info.name[:len(name)]=name
    info.flags=1;info.input_db_bits=struct.unpack('<I',struct.pack('<f',-12))[0];info.output_db_bits=struct.unpack('<I',struct.pack('<f',1.9))[0]
    info.source_sha256[:]=hashlib.sha256(b'synthetic original').digest();data=payload(3,7)
    assert lib.cf_asset_begin_with_info(C.byref(v.state),700,3,1,len(data),crc(data),C.byref(info))==OK
    for p in range(0,len(data),256):assert v.chunk(700,p,data[p:p+256])==OK
    rc=lib.cf_asset_commit(C.byref(v.state),700)
    while rc==PENDING:rc=lib.cf_asset_pump(C.byref(v.state))
    assert rc==OK;v.reboot();rc,m=v.recover(3);assert rc==OK and bytes(m.info)==bytes(info)
    before=bytes(v.disk)
    for bad in (b'X:\\fixtures\\private.nam',b'bad\x00name',b'\xc0\xaf',b'\xed\xa0\x80',b'\xf4\x90\x80\x80',b'line\nname'):
        invalid=Info();invalid.name_bytes=len(bad);invalid.name[:len(bad)]=bad
        assert lib.cf_asset_begin_with_info(C.byref(v.state),701,3,2,len(data),crc(data),C.byref(invalid))==BAD and bytes(v.disk)==before
    checks.append('Names, source digest and level metadata survive reconstruction; paths, controls, invalid UTF-8 and surrogates rejected before writes')
    assert not d.callback_errors,d.callback_errors
    checks.append('Foreign product banks not accepted; no change to their contents')
    return dict(passed=True,hardware_accessed=False,storage='simulated callbacks only',native_state_bytes=C.sizeof(State),powercut_cases=powercuts,checks=checks,
      not_validated=['physical storage adapter','littlefs ABI/task synchronization','USB receiver integration','audio activation','real power-loss durability'])

def main():
    out=ROOT/('checks/store-'+uuid.uuid4().hex[:10]);out.mkdir(parents=True)
    sources={n:hashlib.sha256((ROOT/n).read_bytes()).hexdigest() for n in ('asset_store.c','asset_store.h','verify_store.py')}
    try:
        result=run(compile(out));result.update(sources=sources,build_folder=str(out))
    except BaseException as e:
        import traceback
        result=dict(passed=False,hardware_accessed=False,error=traceback.format_exc(),sources=sources,build_folder=str(out))
        (out/'result.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2));raise
    (out/'result.json').write_text(json.dumps(result,indent=2));(ROOT/'checks/store-latest.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
if __name__=='__main__':main()
