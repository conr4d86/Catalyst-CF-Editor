"""Cross-check the C receiver and C# host codec entirely offline."""
import ctypes as C,hashlib,json,os,random,struct,subprocess,uuid
from pathlib import Path
import verify_store as store
ROOT=Path(__file__).resolve().parent;TV=ROOT.parents[2];EDITOR=ROOT.parents[1]/'catalyst-editor'
U=C.c_uint32;PTR=C.c_void_p
class Message(C.Structure):_fields_=[(n,U) for n in ('product','session','transaction','number')]+[('opcode',C.c_uint8),('slot',C.c_uint8),('bytes',C.c_uint16),('data',C.c_uint8*256)]
class Receiver(C.Structure):_fields_=[('store',C.POINTER(store.State))]+[(n,U) for n in ('session','capabilities','last_transaction','last_status')]
def compile(out):
    env=os.environ.copy();env['ZIG_GLOBAL_CACHE_DIR']=str(out/'global-cache');env['ZIG_LOCAL_CACHE_DIR']=str(out/'local-cache')
    zig=TV/'.tools/zig-cross/zig-windows-x86_64-0.13.0/zig.exe';dll=out/'receiver.dll'
    commands=[
      [zig,'cc','-O2','-Wall','-Wextra','-Werror','-shared','-target','x86_64-windows-gnu',ROOT/'asset_store.c',ROOT/'asset_wire.c','-Wl,--export-all-symbols','-o',dll],
      [zig,'cc','-Os','-Wall','-Wextra','-Werror','-ffreestanding','-fno-builtin','-fno-stack-protector','-target','thumb-freestanding-eabihf','-mcpu=cortex_m7+fp_armv8d16sp','-c',ROOT/'asset_wire.c','-o',out/'receiver.arm.o'],
      ['C:/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe','/nologo','/target:exe','/main:WireProbe','/reference:System.Web.Extensions.dll','/out:'+str(out/'WireProbe.exe'),ROOT/'WireProbe.cs',EDITOR/'AssetWire.cs',EDITOR/'AssetInfo.cs']]
    for i,args in enumerate(commands):
        p=subprocess.run(list(map(str,args)),capture_output=True,text=True,env=env,timeout=180);(out/f'compile-{i}.log').write_text(p.stdout+p.stderr);assert p.returncode==0,p.stdout+p.stderr
    lib=C.CDLL(str(dll))
    signatures={
      'cf_asset_init':[C.POINTER(store.State),C.POINTER(store.IO),U],'cf_asset_recover':[C.POINTER(store.State),U,C.POINTER(store.Meta)],
      'cf_asset_begin':[C.POINTER(store.State),U,U,U,U,U],'cf_asset_chunk':[C.POINTER(store.State),U,U,PTR,U],
      'cf_asset_commit':[C.POINTER(store.State),U],'cf_asset_pump':[C.POINTER(store.State)],'cf_asset_cancel':[C.POINTER(store.State),U],
      'cf_wire_decode':[PTR,U,C.POINTER(Message)],'cf_wire_encode':[C.POINTER(Message),PTR,U],
      'cf_receiver_init':[C.POINTER(Receiver),C.POINTER(store.State),U],
      'cf_receiver_handle':[C.POINTER(Receiver),C.POINTER(Message),C.POINTER(Message)],'cf_receiver_pump':[C.POINTER(Receiver)]}
    for name,args in signatures.items():getattr(lib,name).argtypes=args;getattr(lib,name).restype=C.c_int
    return lib
def encode(lib,m):
    p=C.create_string_buffer(329);n=lib.cf_wire_encode(C.byref(m),p,329);assert 0<n<=329;return p.raw[:n]
def decode(lib,p):
    m=Message();rc=lib.cf_wire_decode(p,len(p),C.byref(m));return rc,m
def msg(opcode,slot=0,tx=11,number=0,data=b'',session=9):
    m=Message(store.PRODUCT,session,tx,number,opcode,slot,len(data));m.data[:len(data)]=data;return m

def metadata(slot):
    name=('IR de pantalla · válvulas 🎸' if slot<3 else 'NAM 5150 · válvulas 🎸').encode('utf-8')
    return struct.pack('<I4fI',3 if slot<3 else 1,-12 if slot>=3 else 0,1.9,80 if slot<3 else 0,8000 if slot<3 else 0,len(name))+name+bytes(128-len(name))+hashlib.sha256(bytes([slot])).digest()
def main():
    out=ROOT/('checks/wire-'+uuid.uuid4().hex[:10]);out.mkdir(parents=True);lib=compile(out);rng=random.Random(20261103)
    vectors=[]
    for n in range(257):
        m=Message(rng.getrandbits(32),rng.getrandbits(32),rng.getrandbits(32),rng.getrandbits(32),rng.randrange(128),rng.randrange(256),n);m.data[:n]=bytes(rng.randrange(256) for _ in range(n))
        wire=encode(lib,m);rc,new=decode(lib,wire);assert rc==1 and bytes(new)==bytes(m)
        assert all(b<128 for b in wire[1:-1]);vectors.append(dict(packet=list(wire)))
    invalid=[]
    for b in (b'',b'\xf0\x7e\x7f\x06\x01\xf7',b'\xf0\x00\x01\x0c\x10\xf7'):
        assert decode(lib,b)[0]==0;vectors.append(dict(packet=list(b)));invalid.append('unrelated')
    original=encode(lib,msg(3,data=bytes(range(256))))
    for i in range(6,len(original)-1):
        b=bytearray(original);b[i]^=1
        rc,_=decode(lib,bytes(b));assert rc==-1;vectors.append(dict(packet=list(b)));invalid.append('invalid')
    for i in range(6,len(original)):
        b=original[:i];rc,_=decode(lib,b);assert rc==-1;vectors.append(dict(packet=list(b)));invalid.append('invalid')
    for _ in range(2000):
        n=rng.randrange(0,380);b=b'\xf0\x7dCFCX'+bytes(rng.randrange(256) for _ in range(n))
        dest=Message();C.memset(C.byref(dest),0xa5,C.sizeof(dest));before=bytes(dest)
        rc=lib.cf_wire_decode(b,len(b),C.byref(dest));assert rc==-1 and bytes(dest)==before
    p=subprocess.run([str(out/'WireProbe.exe')],input=json.dumps(vectors),capture_output=True,text=True,encoding='utf-8',timeout=90)
    assert p.returncode==0,p.stderr;host=json.loads(p.stdout);assert len(host)==len(vectors)
    for i,row in enumerate(host):
        if i<257:
            assert row['kind']=='valid' and row['packet']==vectors[i]['packet'],(i,row)
            rc,m=decode(lib,bytes(row['packet']));assert rc==1 and list(m.data[:m.bytes])==row['data']
        else:assert row['kind']==invalid[i-257],(i,row)
    # The display/calibration record is identical across the two languages.
    meta_vectors=[dict(info=list(metadata(slot))) for slot in range(5)]
    invalid_meta=[]
    for name in (b'X:\\fixtures\\private.wav',b'/tmp/fixtures/private.wav',b'a\0b',b'a\nb',b'\xc0\x80',b'\xed\xa0\x80',b'\xf4\x90\x80\x80',b'\xe2\x80\xa8'):
        b=bytearray(metadata(0));struct.pack_into('<I',b,20,len(name));b[24:152]=name+bytes(128-len(name));invalid_meta.append(bytes(b))
    for offset,value in ((0,4),(12,0x80000000),(16,0x7fc00000),(4,0xc2740000)):
        b=bytearray(metadata(0));struct.pack_into('<I',b,offset,value);invalid_meta.append(bytes(b))
    for b in invalid_meta:meta_vectors.append(dict(info=list(b)))
    files=['X:\\fixtures\\private\\modelo.nam','X:\\fixtures\\private\\'+('🎸'*80)+'.nam']
    meta_vectors.extend(dict(file=f) for f in files)
    p=subprocess.run([str(out/'WireProbe.exe')],input=json.dumps(meta_vectors),capture_output=True,text=True,encoding='utf-8',timeout=90)
    assert p.returncode==0,p.stderr;metadata_host=json.loads(p.stdout)
    for slot in range(5):assert bytes(metadata_host[slot]['info'])==metadata(slot)
    assert all(x['kind']=='invalid' for x in metadata_host[5:5+len(invalid_meta)])
    assert metadata_host[-2]['name']=='modelo' and len(metadata_host[-1]['name'].encode('utf-8'))<=127 and metadata_host[-1]['name'].endswith('…')
    # All requests traverse encode/decode and the compiled receiver; no USB API.
    d=store.Device(lib);r=Receiver();assert lib.cf_receiver_init(C.byref(r),C.byref(d.state),0)==store.OK
    def request(m):
        raw=encode(lib,m);rc,parsed=decode(lib,raw);assert rc==1
        response=Message();status=lib.cf_receiver_handle(C.byref(r),C.byref(parsed),C.byref(response))
        assert response.opcode==m.opcode|64
        code,ack=decode(lib,encode(lib,response));assert code==1 and ack.product==m.product and ack.transaction==m.transaction and ack.slot==m.slot
        assert struct.unpack('<I',bytes(ack.data[:4]))[0]==status
        return status,ack
    assert request(msg(1,tx=9,session=0))[0]==store.OK
    b=store.payload(3);before=bytes(d.disk)
    assert request(msg(2,slot=3,tx=100,data=struct.pack('<II',len(b),store.crc(b))))[0]==store.BAD
    assert bytes(d.disk)==before
    assert lib.cf_receiver_init(C.byref(r),C.byref(d.state),1)==store.OK
    status,ack=request(msg(1,tx=9,session=0));assert status==store.OK and struct.unpack('<III',bytes(ack.data[4:16]))==(1,256,5)
    assert ack.bytes==20 and struct.unpack('<I',bytes(ack.data[16:20]))[0]==2
    assert request(msg(2,slot=3,tx=100,data=struct.pack('<II',len(b),store.crc(b)),session=88))[0]==store.STALE
    assert bytes(d.disk)==before
    statuses=[]
    for slot in range(5):
        data=store.payload(slot);tx=100+slot
        prefix=struct.pack('<II',len(data),store.crc(data));before=bytes(d.disk)
        for bad in invalid_meta:assert request(msg(2,slot,tx,data=prefix+bad))[0]==store.BAD
        if slot>=3:assert request(msg(2,slot,tx,data=prefix+metadata(0)))[0]==store.BAD
        assert bytes(d.disk)==before
        info=bytes(metadata_host[slot]['info'])
        assert request(msg(2,slot,tx,data=prefix+info))[0]==store.OK
        assert request(msg(5,(slot+1)%5,tx))[0]==store.STALE
        assert request(msg(1,tx=8,session=0))[0]==store.BUSY
        for offset in range(0,len(data),256):
            m=msg(3,slot,tx,offset,data[offset:offset+256]);assert request(m)[0]==store.OK
            if offset==0:assert request(m)[0]==store.ALREADY
        assert request(msg(4,slot,tx))[0]==store.PENDING
        rc=store.PENDING
        while rc==store.PENDING:rc=lib.cf_receiver_pump(C.byref(r))
        assert rc==store.OK
        status,ack=request(msg(5,slot,tx));assert status==store.OK
        generation,receipt,length,digest=struct.unpack('<4I',bytes(ack.data[4:20]));assert (generation,receipt,length,digest)==(1,tx,len(data),store.crc(data))
        assert ack.bytes==204 and bytes(ack.data[20:204])==info
        cold=store.State();assert lib.cf_asset_init(C.byref(cold),C.byref(d.io),store.PRODUCT)==store.OK
        recovered=store.Meta();rc=lib.cf_asset_recover(C.byref(cold),slot,C.byref(recovered));assert rc==store.OK and bytes(recovered.info)==info
        statuses.append(dict(slot=slot,bytes=length,transaction=receipt,generation=generation))
    assert not (r.capabilities&2),'Unwired receiver claims audio activation'
    result=dict(passed=True,hardware_accessed=False,USB_opened=False,protocol='CFCX laboratory v1, storage schema 2, not installed',max_wire_packet=329,cross_language_valid_vectors=257,cross_language_invalid_or_unrelated=len(invalid),random_malformed_frames=2000,metadata_cross_language_and_cold_recovery=True,metadata_invalid_before_writes=len(invalid_meta),capability_and_session_required=True,busy_handshake_keeps_session=True,commit_receipts_verified=True,slots=statuses,
      sources={str(p.relative_to(TV)):hashlib.sha256(p.read_bytes()).hexdigest() for p in (ROOT/'asset_store.c',ROOT/'asset_store.h',ROOT/'asset_wire.c',ROOT/'asset_wire.h',ROOT/'verify_wire.py',ROOT/'WireProbe.cs',EDITOR/'AssetWire.cs',EDITOR/'AssetInfo.cs')},
      build_folder=str(out),not_validated=['Native MIDI observer/packet dispatch','FileSystem task queue integration','Physical storage or audio activation','Host USB transport and ownership handover'])
    (out/'result.json').write_text(json.dumps(result,indent=2));(ROOT/'checks/wire-latest.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
if __name__=='__main__':
    try:main()
    except BaseException:
        import traceback
        (ROOT/'checks/wire-error.txt').write_text(traceback.format_exc());raise
