"""IR file/staging/bounded preparation -> existing native ARM FFT kernel.

All work is emulated. Selection and audio-boundary coordination are explicit
fixtures, not installed native hooks. Cache buffers here are intentionally
outside the tentative firmware reservation; no hardware layout is approved.
"""
import contextlib,ctypes as C,hashlib,json,math,struct,sys,types
from pathlib import Path
import verify_actor as actor
import verify_store as store
import verify_wire as wire
from ctf_tool import parse
# The decoder imports historical assembly helpers. This test only reads the
# existing image: refuse assembly explicitly instead of resolving Keystone.
assembly=types.ModuleType('keystone')
def forbidden(*args,**kwargs):raise RuntimeError('Assembly/firmware generation forbidden in IR handoff verification')
assembly.Ks=forbidden
assembly.KS_ARCH_ARM=assembly.KS_MODE_THUMB=assembly.KS_MODE_LITTLE_ENDIAN=assembly.KS_MODE_V8=0
sys.modules['keystone']=assembly
from package_extension import decode_hex
ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent

def floats(b):return struct.unpack('<%df'%(len(b)//4),b)
def pack(v):return struct.pack('<%df'%len(v),*v)

def main():
    build=json.loads((ROOT/'actor-build/build.json').read_text());assert hashlib.sha256((ROOT/'actor-build/actor.elf').read_bytes()).hexdigest()==build['elf_sha256']
    base=(NAM/'app/resources/nam-engine.ctf').read_bytes();assert hashlib.sha256(base).hexdigest()=='e80bba31906c0032b4b92f9d062c54879ea88fbabbf0a9d073dd1794d2a287a1'
    _,raw,regions=parse(base);region=next(r for r in regions if r['kind']==0);image=decode_hex(raw[region['offset']:region['offset']+region['size']])[0][1][1]
    twiddle=floats(image[0x601b3400-0x60001000:0x601b3500-0x60001000])
    def frequency(h):
        result=[]
        for part in range(64):
            for k in range(33):
                re=im=0.
                for n,x in enumerate(h[part*32:part*32+32]):
                    t=(k*n)&63;sign=-1. if t>=32 else 1.;t&=31
                    re+=x*twiddle[t*2]*sign;im+=x*twiddle[t*2+1]*sign
                result.extend((re,im))
        return pack(result)
    wi=json.loads((ROOT/'checks/wire-latest.json').read_text());lib=C.CDLL(str(Path(wi['build_folder'])/'receiver.dll'))
    lib.cf_wire_encode.argtypes=[C.POINTER(wire.Message),C.c_void_p,C.c_uint32];lib.cf_wire_encode.restype=C.c_uint32
    lib.cf_wire_decode.argtypes=[C.c_void_p,C.c_uint32,C.POINTER(wire.Message)];lib.cf_wire_decode.restype=C.c_int
    with (ROOT/'actor-build/ir-handoff.log').open('w') as log,contextlib.redirect_stdout(log):
        h=actor.Harness();m=h.m;u=m.u
        u.mem_write(0x60001000,image);u.mem_write(0x14000,image[0x601ac000-0x60001000:0x601ad000-0x60001000])
        assert h.call('cf_ir_init',0,0,0,0)==store.OK
        assert h.call('cf_ir_select_request',1,2,0x20011000)==store.OK
        assert h.call('cf_ir_boundary')==0 and h.call('cf_ir_status',1)==store.OK
        assert m.get(0x20040000)==0 and m.get(0x2004001c)==0,'Empty slot must use stock fallback'
        assert h.exchange(lib,wire.msg(1,tx=9,session=0))[0]==store.OK
        preset=b'SYNTHETIC IR HANDOFF PRESET\0';h.fs.write('bank_0_channel_a',0,preset,True)
        loader=0x8301d100;store_address=h.call('cf_actor_store_address');cases=[];impulses=[]
        def store_and_load(slot,tx,generation,impulse,address):
            b=pack(impulse[::-1])+frequency(impulse);assert len(b)==25088
            assert h.exchange(lib,wire.msg(2,slot,tx,generation,struct.pack('<II',len(b),store.crc(b))+wire.metadata(slot)))[0]==store.OK
            for off in range(0,len(b),256):assert h.exchange(lib,wire.msg(3,slot,tx,off,b[off:off+256]))[0]==store.OK
            assert h.exchange(lib,wire.msg(4,slot,tx))[0]==store.PENDING
            assert h.exchange(lib,wire.msg(5,slot,tx))[0]==store.OK
            assert h.call('cf_load_begin',loader,store_address,slot,address,len(b))==store.PENDING
            rc=store.PENDING
            while rc==store.PENDING:rc=h.call('cf_load_pump',loader)
            assert rc==store.OK and m.get(loader+228)==1 and bytes(u.mem_read(address,len(b)))==b
            return address+8192
        def settle(tx):
            blocks=words=0
            while h.call('cf_ir_status',tx)==store.PENDING:
                assert m.get(0x20040000)!=0x52494358 or blocks==0
                n=h.call('cf_ir_boundary');assert n<=128;blocks+=1;words+=n;assert blocks<100
            assert h.call('cf_ir_status',tx)==store.OK
            return blocks,words
        def response(impulse):
            signal=[.02]+[0.]*2111;out=[]
            for off in range(0,len(signal),32):
                u.mem_write(0x20044800,pack(signal[off:off+32]));m.call(0x14901,0x20001470)
                out.extend(floats(bytes(u.mem_read(0x20001470,128))))
            expected=[x*.02/32 for x in impulse]+[0.]*64;error=max(abs(x-y) for x,y in zip(out,expected));assert error<1e-6,error
            return error
        for slot in range(3):
            impulse=[0.]*2048;impulse[slot*17]=1.+slot*.25;impulse[127+slot]=-.125;impulse[-1]=.05*(slot+1);impulse=list(floats(pack(impulse)));impulses.append(impulse)
            pointer=store_and_load(slot,100+slot,0,impulse,0x80880000+slot*0x7000)
            assert h.call('cf_ir_bank_request',200+slot,slot,pointer,0x4200)==store.OK
            assert h.call('cf_ir_select_request',999,slot,0x20011000)==store.BUSY
            settle(200+slot)
            assert h.call('cf_ir_bank_request',200+slot,slot,pointer,0x4200)==store.ALREADY
            assert h.call('cf_ir_select_request',300+slot,slot,0x20011000)==store.OK
            blocks,words=settle(300+slot);assert blocks==67 and words==8544
            assert bytes(u.mem_read(0x20045000,0x4200))==frequency(impulse)
            assert bytes(u.mem_read(0x20040100,0x4200))==bytes(0x4200) and bytes(u.mem_read(0x20044600,128))==bytes(128)
            assert m.get(0x20040000)==0x52494358 and m.get(0x20040004)==slot
            cases.append(dict(slot=slot,blocks=blocks,max_words_per_boundary=128,max_impulse_error=response(impulse)))
        # Replace the currently selected IR through a new persisted generation.
        changed=[v*-.5 for v in impulses[2]];new=store_and_load(2,402,1,changed,0x80895000)
        assert h.call('cf_ir_bank_request',502,2,new,0x4200)==store.OK
        blocks,words=settle(502);assert blocks==67
        u.mem_write(0x8088e000+8192,b'\xff'*0x4200)
        replacement_error=response(changed)
        assert h.fs.read('bank_0_channel_a',0,len(preset))==preset
    report=dict(passed=True,hardware_accessed=False,USB_opened=False,SDK_loaded=False,firmware_created=False,
        actor_elf_sha256=build['elf_sha256'],source_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        IRs=cases,active_replacement_error=replacement_error,active_replacement_generation=2,empty_slot_stock_fallback_flag=True,
        bounded_preparation_words=128,preparation_frames=67,sample_rate=48000,frame_samples=32,synthetic_preset_preserved=True,
        not_validated=['Native selection/reset hooks not installed','Automatic outer audio-boundary coordinator','Real USB/flash/cache/IRQ timing','Physical cache/staging memory ownership','Stock fallback sound during preparation is not a crossfade','Boot/preset/UI integration'])
    (ROOT/'actor-build/ir-handoff-tests.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':
    try:main()
    except BaseException:
        import traceback
        (ROOT/'actor-build/ir-handoff-error.txt').write_text(traceback.format_exc());raise
