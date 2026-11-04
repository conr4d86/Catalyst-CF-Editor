"""ROM seed fallback + real C store; simulated files, never flash or USB."""
import ctypes as C,hashlib,json,os,struct,subprocess,uuid
from pathlib import Path
import verify_store as store
ROOT=Path(__file__).resolve().parent;U=C.c_uint32
class Entry(C.Structure):_fields_=[('payload',C.c_void_p),('length',U),('crc',U),('transaction',U),('info',store.Info)]
class Manifest(C.Structure):_fields_=[('magic',U),('version',U),('mask',U),('count',U),('edition',C.c_ubyte*32),('entry',Entry*5)]
class Seed(C.Structure):_fields_=[('manifest',C.POINTER(Manifest)),('product',U),('header',C.c_ubyte*256)]
def main():
    out=ROOT/('checks/seed-'+uuid.uuid4().hex[:10]);out.mkdir(parents=True)
    zig=store.TV/'.tools/zig-cross/zig-windows-x86_64-0.13.0/zig.exe';env=os.environ.copy();env['ZIG_GLOBAL_CACHE_DIR']=str(out/'global-cache');env['ZIG_LOCAL_CACHE_DIR']=str(out/'local-cache')
    sources=[ROOT/'asset_seed.c',ROOT/'asset_store.c']
    for i,cmd in enumerate([[zig,'cc','-O2','-Wall','-Wextra','-Werror','-shared','-target','x86_64-windows-gnu',*sources,'-Wl,--export-all-symbols','-o',out/'seed.dll'],[zig,'cc','-Os','-Wall','-Wextra','-Werror','-ffreestanding','-fno-builtin','-target','thumb-freestanding-eabihf','-mcpu=cortex_m7+fp_armv8d16sp','-c',ROOT/'asset_seed.c','-o',out/'seed.arm.o']]):
        p=subprocess.run(list(map(str,cmd)),capture_output=True,text=True,env=env,timeout=90);assert p.returncode==0,p.stdout+p.stderr
    lib=C.CDLL(str(out/'seed.dll'))
    args={'cf_seed_init':[C.POINTER(Seed),C.POINTER(Manifest),U],'cf_seed_read':[C.POINTER(Seed),U,U,U,C.c_void_p,U],'cf_seed_name':[C.POINTER(Seed),U,U,C.c_void_p,U],
          'cf_asset_init':[C.POINTER(store.State),C.POINTER(store.IO),U],'cf_asset_recover':[C.POINTER(store.State),U,C.POINTER(store.Meta)],'cf_asset_begin':[C.POINTER(store.State),U,U,U,U,U],
          'cf_asset_chunk':[C.POINTER(store.State),U,U,C.c_void_p,U],'cf_asset_commit':[C.POINTER(store.State),U],'cf_asset_pump':[C.POINTER(store.State)]}
    for name,a in args.items():getattr(lib,name).argtypes=a;getattr(lib,name).restype=C.c_int
    payloads=[store.payload(i) for i in range(5)];buffers=[C.create_string_buffer(p) for p in payloads]
    m=Manifest(0x31445343,1,31,5);m.edition[:]=hashlib.sha256(b'seed fixture').digest()
    for slot,p in enumerate(payloads):m.entry[slot]=Entry(C.addressof(buffers[slot]),len(p),store.crc(p),1000+slot,store.Info())
    class Device(store.Device):
        def __init__(self):
            self.present=set();self.seed=Seed();assert lib.cf_seed_init(C.byref(self.seed),C.byref(m),store.PRODUCT)==0;super().__init__(lib)
        def _read(self,user,slot,bank,offset,out,n):
            if (slot,bank) not in self.present:return lib.cf_seed_read(C.byref(self.seed),slot,bank,offset,out,n)
            return super()._read(user,slot,bank,offset,out,n)
        def _erase(self,user,slot,bank):self.present.add((slot,bank));return super()._erase(user,slot,bank)
    d=Device();checks=[]
    for slot,p in enumerate(payloads):
        rc,meta=d.recover(slot);assert rc==0 and meta.bank==1 and meta.generation==1 and meta.transaction==1000+slot and meta.crc==store.crc(p)
    assert not d.calls and not d.present;checks.append('All five initial resources recover from ROM without any native file creation or write')
    def replace(slot,data,tx,base):
        assert lib.cf_asset_begin(C.byref(d.state),tx,slot,base,len(data),store.crc(data))==0
        for offset in range(0,len(data),256):chunk=data[offset:offset+256];assert lib.cf_asset_chunk(C.byref(d.state),tx,offset,chunk,len(chunk))==0
        assert lib.cf_asset_commit(C.byref(d.state),tx)==6
        while (rc:=lib.cf_asset_pump(C.byref(d.state)))==6:pass
        assert rc==0
    for slot in range(5):
        replace(slot,store.payload(slot,1),2000+slot,1);rc,meta=d.recover(slot);assert rc==0 and meta.bank==0 and meta.generation==2
        d.reboot();assert d.recover(slot)[1].crc==store.crc(store.payload(slot,1))
        replace(slot,store.payload(slot,2),3000+slot,2);rc,meta=d.recover(slot);assert rc==0 and meta.bank==1 and meta.generation==3
        d.reboot();assert d.recover(slot)[1].crc==store.crc(store.payload(slot,2))
    assert all(bytes(b)[:-1]==p for b,p in zip(buffers,payloads));checks.append('Two updates per slot alternate native banks, survive reopen and never modify ROM seeds')
    # An existing torn bank 1 must stay invalid. It must NOT resurrect its ROM
    # fallback once that filename has been created by a real replacement.
    d=Device();replace(4,store.payload(4,1),2200,1);d.present.add((4,1));d.reboot();assert d.recover(4)[1].generation==2
    checks.append('An existing empty native bank never falls back to ROM and the previous native generation survives')
    for slot in range(5):
        a=C.create_string_buffer(74);assert lib.cf_seed_name(C.byref(d.seed),slot,1,a,74)==0
        assert a.value==b'cfcx_'+bytes(m.edition).hex().encode()+b'_'+str(slot).encode()+b'_1'
        assert lib.cf_seed_name(C.byref(d.seed),slot,1,a,73)==2
    changed=Manifest.from_buffer_copy(bytes(m));changed.edition[0]^=1;s=Seed();assert lib.cf_seed_init(C.byref(s),C.byref(changed),store.PRODUCT)==0
    a=C.create_string_buffer(74);b=C.create_string_buffer(74);lib.cf_seed_name(C.byref(d.seed),0,0,a,74);lib.cf_seed_name(C.byref(s),0,0,b,74);assert a.value!=b.value
    checks.append('A different full-CF edition gets distinct dedicated filenames, preserving previous edition files')
    for mode in ('magic','version','mask','count','edition','length','transaction','pointer'):
        bad=Manifest.from_buffer_copy(bytes(m))
        if mode=='edition':bad.edition[:]=bytes(32)
        elif mode=='length':bad.entry[0].length=1
        elif mode=='transaction':bad.entry[0].transaction=0
        elif mode=='pointer':bad.entry[0].payload=0
        elif mode=='mask':bad.mask=32
        else:setattr(bad,mode,0)
        state=Seed();assert lib.cf_seed_init(C.byref(state),C.byref(bad),store.PRODUCT)==2 and not state.manifest
    checks.append('Invalid manifest geometry, edition, length, transaction and pointers are refused before binding')
    report=dict(passed=True,hardware_accessed=False,USB_opened=False,MMIO_accessed=False,firmware_created=False,checks=checks,build_folder=str(out),limitations=['ROM manifest addresses and native filesystem fallback are not installed','Physical cold boot/cache and NVM durability are untested'],sources={str(p.relative_to(store.TV)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources+[ROOT/'asset_seed.h',Path(__file__)]})
    (ROOT/'checks/seed-latest.json').write_text(json.dumps(report,indent=2));print(json.dumps({k:v for k,v in report.items() if k!='sources'},indent=2))
if __name__=='__main__':main()
