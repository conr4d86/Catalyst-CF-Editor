"""Original littlefs machine code + asset store, with a simulated block device.

Format/mount/file calls below run ONLY inside Unicorn on a new Python bytearray.
All original HAL flash callbacks are forbidden. No device or SDK is opened.
This validates the inferred ABI against this binary, not physical flash safety.
"""
import ctypes as C,hashlib,json,struct,sys,importlib.util,contextlib
from pathlib import Path
ROOT=Path(__file__).resolve().parent;RESEARCH=ROOT.parents[1]
sys.path[:0]=[str(RESEARCH/'catalyst-editor/tools/emulation'),str(RESEARCH/'catalyst-editor/tools/disasm'),str(RESEARCH/'catalyst-ir'),str(ROOT)]
import unicorn,capstone
from unicorn import UC_HOOK_CODE
from unicorn.arm_const import *
from emulate_graph import FirmwareMachine
import verify_store as store
CFG=0x83006000;LFS=0x83006100;FILE=0x83006300;FILECFG=0x83006400
FILEBUF=0x83006500;PATH=0x83006700;DATA=0x83008000
class NativeFS:
    def __init__(self,disk=None):
        self.disk=bytearray(b'\xff'*(4096*4096)) if disk is None else bytearray(disk)
        self.io_counts={'read':0,'prog':0,'erase':0,'sync':0};self.cut=None;self.mount(disk is None)
    def mount(self,empty=False):
        m=self.m=FirmwareMachine();m.quiet=True
        assert m.call(0x8002d848,CFG)==0
        self.original_config=list(struct.unpack('<19I',bytes(m.u.mem_read(CFG,76))))
        assert self.original_config[5:12]==[256,256,4096,4096,1000,256,512]
        def forbidden(u,a,n,user):raise RuntimeError('Original HAL flash callback forbidden in storage emulation')
        for address in self.original_config[1:5]:m.u.hook_add(UC_HOOK_CODE,forbidden,begin=address&~1,end=address&~1)
        for index,name in enumerate(('read','prog','erase','sync')):
            address=0x8301e800+index*16;m.put(CFG+4+index*4,address|1)
            def callback(u,a,n,user,kind=name):
                self.io_counts[kind]+=1;rc=0
                if kind=='sync':pass
                else:
                    block=u.reg_read(UC_ARM_REG_R1);off=u.reg_read(UC_ARM_REG_R2) if kind!='erase' else 0
                    size=struct.unpack('<I',bytes(u.mem_read(u.reg_read(UC_ARM_REG_SP),4)))[0] if kind!='erase' else 4096
                    pointer=u.reg_read(UC_ARM_REG_R3) if kind!='erase' else 0
                    assert block<4096 and off<=4096 and size<=4096-off
                    p=block*4096+off;take=size
                    if self.cut and kind==self.cut[0]:
                        if self.cut[1]<size:take=self.cut[1];self.cut=None;rc=-5
                        else:self.cut=(kind,self.cut[1]-size)
                    if kind=='read':u.mem_write(pointer,bytes(self.disk[p:p+take]))
                    elif kind=='erase':self.disk[p:p+take]=b'\xff'*take
                    else:
                        b=bytes(u.mem_read(pointer,take));assert all((self.disk[p+i]&v)==v for i,v in enumerate(b));self.disk[p:p+take]=b
                u.reg_write(UC_ARM_REG_R0,rc&0xffffffff);u.reg_write(UC_ARM_REG_PC,u.reg_read(UC_ARM_REG_LR))
            m.u.hook_add(UC_HOOK_CODE,callback,begin=address,end=address)
        if empty:assert self.call(0x8002d360,LFS,CFG)==0
        assert self.call(0x8002d4b0,LFS,CFG)==0
        self.m.put(FILECFG,FILEBUF,0,0)
    def call(self,address,*args):return C.c_int32(self.m.call(address,*args,count=30000000)).value
    def open(self,name,flags):
        encoded=name.encode()+b'\0';assert len(encoded)<256
        self.m.u.mem_write(PATH,encoded);self.m.u.mem_write(FILE,bytes(128))
        return self.call(0x8002cffc,LFS,FILE,PATH,flags,FILECFG)
    def read(self,name,offset,size):
        rc=self.open(name,1)
        if rc==-2:return b'\xff'*size
        if rc:raise IOError(('open-read',rc))
        try:
            assert self.call(0x8002d328,LFS,FILE,offset,0)==offset
            rc=self.call(0x80029d3c,LFS,FILE,DATA,size)
            if rc<0:raise IOError(('read',rc))
            return bytes(self.m.u.mem_read(DATA,rc))+b'\xff'*(size-rc)
        finally:
            rc=self.call(0x8002d268,LFS,FILE)
            if rc:raise IOError(('close-read',rc))
    def write(self,name,offset,data,truncate=False):
        rc=self.open(name,0x503 if truncate else 0x103)
        if rc:raise IOError(('open-write',rc))
        try:
            assert self.call(0x8002d328,LFS,FILE,offset,0)==offset
            if data:
                self.m.u.mem_write(DATA,data);rc=self.call(0x80029818,LFS,FILE,DATA,len(data))
                if rc!=len(data):raise IOError(('write',rc))
        finally:
            rc=self.call(0x8002d268,LFS,FILE)
            if rc:raise IOError(('close-write',rc))
    def reboot(self):self.cut=None;self.mount(False)

class FileDevice(store.Device):
    def __init__(self,lib,fs):
        # Reuse the tested FFI methods, but never the flat-bank simulator.
        self.lib=lib;self.fs=fs;self.errors=[]
        self.read=store.READ(self._read);self.erase=store.ERASE(self._erase);self.write=store.WRITE(self._write);self.sync=store.SYNC(lambda user:0)
        self.io=store.IO(None,self.read,self.erase,self.write,self.sync);self.start()
    def name(self,slot,bank):assert slot<5 and bank<2;return 'cfcx_asset_%d_%d'%(slot,bank)
    def _read(self,user,slot,bank,offset,out,n):
        try:
            assert offset<=32768 and n<=32768-offset
            b=self.fs.read(self.name(slot,bank),offset,n);C.memmove(out,b,n);return 0
        except BaseException as e:self.errors.append(str(e));return 1
    def _erase(self,user,slot,bank):
        try:self.fs.write(self.name(slot,bank),0,b'',True);return 0
        except BaseException as e:self.errors.append(str(e));return 1
    def _write(self,user,slot,bank,offset,data,n):
        try:
            assert offset<=32768 and n<=32768-offset
            self.fs.write(self.name(slot,bank),offset,C.string_at(data,n));return 0
        except BaseException as e:self.errors.append(str(e));return 1
    def reboot(self):self.fs.reboot();self.start()

def main():
    latest=json.loads((ROOT/'checks/store-latest.json').read_text());folder=Path(latest['build_folder']);lib=C.CDLL(str(folder/'asset_store.dll'))
    assert latest['passed'] and hashlib.sha256((ROOT/'asset_store.c').read_bytes()).hexdigest()==latest['sources']['asset_store.c']
    # Bind the same ABI without rebuilding or loading any vendor wrapper.
    for name,args in {'cf_asset_init':[C.POINTER(store.State),C.POINTER(store.IO),store.U],'cf_asset_recover':[C.POINTER(store.State),store.U,C.POINTER(store.Meta)],'cf_asset_begin':[C.POINTER(store.State),store.U,store.U,store.U,store.U,store.U],'cf_asset_chunk':[C.POINTER(store.State),store.U,store.U,store.PTR,store.U],'cf_asset_commit':[C.POINTER(store.State),store.U],'cf_asset_pump':[C.POINTER(store.State)]}.items():getattr(lib,name).argtypes=args;getattr(lib,name).restype=C.c_int
    results=[]
    with (folder/'native-fs.log').open('w') as log,contextlib.redirect_stdout(log):
        fs=NativeFS();preset=b'SYNTHETIC PRESET PRESERVATION FIXTURE\0'*4
        fs.write('bank_0_channel_a',0,preset,True);fs.reboot();assert fs.read('bank_0_channel_a',0,len(preset))==preset
        d=FileDevice(lib,fs)
        for slot in (0,3,4):
            b=store.payload(slot);assert d.upload(slot,b,100+slot,0)==store.OK,d.errors
            d.reboot();rc,meta=d.recover(slot);assert rc==store.OK and meta.generation==1
            assert fs.read(d.name(slot,meta.bank),store.HEADER,len(b))==b
            assert fs.read('bank_0_channel_a',0,len(preset))==preset
            results.append(dict(slot=slot,bytes=len(b),committed_and_recovered=True))
        # The first bank stays available while a second version is abandoned.
        b=store.payload(3,1);assert d.begin(3,b,200,1)==store.OK
        assert d.chunk(200,0,b[:256])==store.OK;d.reboot()
        rc,meta=d.recover(3);assert rc==store.OK and meta.generation==1
        assert d.upload(3,b,201,1)==store.OK;d.reboot()
        rc,meta=d.recover(3);assert rc==store.OK and meta.generation==2 and meta.transaction==201
        assert fs.read(d.name(3,meta.bank),store.HEADER,len(b))==b
        assert fs.read('bank_0_channel_a',0,len(preset))==preset
        assert not d.errors,d.errors
    report=dict(passed=True,hardware_accessed=False,physical_flash_callbacks_forbidden=True,native_filesystem_code=True,storage='fresh synthetic block array; original firmware filesystem code in Unicorn',slots=results,interrupted_payload_recovers_previous=True,generation_update_recovers_new=True,synthetic_preset_preserved=True,source_sha256=latest['sources']['asset_store.c'],block_callbacks=fs.io_counts,
      addresses={k:hex(v) for k,v in {'config':0x8002d848,'format_mock_only':0x8002d360,'mount':0x8002d4b0,'open_configured':0x8002cffc,'seek':0x8002d328,'read':0x80029d3c,'write':0x80029818,'close':0x8002d268}.items()},
      not_validated=['Physical flash HAL/cache/interrupt behavior','Real FileSystem task queue integration','Free capacity of the user filesystem','USB and boot-time audio activation'])
    (folder/'native-fs.json').write_text(json.dumps(report,indent=2));(ROOT/'checks/native-fs-latest.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':
    try:main()
    except BaseException:
        import traceback
        (ROOT/'checks/native-fs-error.txt').write_text(traceback.format_exc());raise
