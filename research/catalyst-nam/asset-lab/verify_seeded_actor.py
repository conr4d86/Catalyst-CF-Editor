"""Seed ROM -> actual native file ABI -> MIDI C actor, only in Unicorn."""
import contextlib,ctypes as C,hashlib,json,struct,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent
sys.path.insert(0,str(NAM.parent/'catalyst-editor/tools-recovery'))
import unicorn,capstone
import verify_actor as actor
import verify_store as store
import verify_wire as wire
LAB=ROOT/'actor-seeded-lab';BUILD=LAB/'actor-build';ROM=0x601e3000

def manifest(payloads,edition):
    blob=bytearray(1048);struct.pack_into('<4I',blob,0,0x31445343,1,31,5);blob[16:48]=edition
    for slot,payload in enumerate(payloads):
        while len(blob)&3:blob.append(0)
        address=ROM+len(blob);offset=48+slot*200
        struct.pack_into('<4I',blob,offset,address,len(payload),store.crc(payload),1000+slot)
        info=wire.metadata(slot);blob[offset+16:offset+200]=info;blob.extend(payload)
    assert ROM+len(blob)<0x60200000
    return bytes(blob)

def main():
    info=json.loads((BUILD/'build.json').read_text());assert info['immutable_seeds'] and hashlib.sha256((BUILD/'actor.elf').read_bytes()).hexdigest()==info['elf_sha256']
    wi=json.loads((ROOT/'checks/wire-latest.json').read_text());lib=C.CDLL(str(Path(wi['build_folder'])/'receiver.dll'))
    lib.cf_wire_encode.argtypes=[C.POINTER(wire.Message),C.c_void_p,C.c_uint32];lib.cf_wire_encode.restype=C.c_uint32
    lib.cf_wire_decode.argtypes=[C.c_void_p,C.c_uint32,C.POINTER(wire.Message)];lib.cf_wire_decode.restype=C.c_int
    payloads=[store.payload(i) for i in range(5)];edition=hashlib.sha256(b'native seeded actor fixture').digest();seed=manifest(payloads,edition)
    actor.ROOT=LAB
    def bind(h,blob=seed):
        h.u.mem_write(ROM,blob);assert h.call('cf_actor_bind_identity',0x10203040,0xa0b0c0d0)==0
        assert h.call('cf_actor_bind_seed',ROM)==0
        assert h.call('cf_actor_bind_seed',ROM)==2
    results=[]
    with (BUILD/'seeded-test.log').open('w') as log,contextlib.redirect_stdout(log):
        h=actor.Harness();bind(h);original_disk=bytes(h.fs.disk);h.fs.write('bank_0_channel_a',0,b'protected fixture preset',True);preset=bytes(h.fs.disk)
        h.feed(bytes.fromhex('f0 7e 7f 06 01 f7'));native_identity=h.packets[-1]
        rc,ack=h.exchange(lib,wire.msg(8,session=0,tx=77));assert rc==0 and ack.bytes==52 and bytes(ack.data[4:8])==struct.pack('<I',2) and bytes(ack.data[20:52])==edition
        h.feed(bytes.fromhex('f0 7e 7f 06 01 f7'));assert h.packets[-1]==native_identity and bytes(h.fs.disk)==preset
        s=h.call('cf_actor_store_address');meta=0x83000800
        for slot,p in enumerate(payloads):
            assert h.call('cf_asset_recover',s,slot,meta)==0
            fields=struct.unpack('<5I',bytes(h.u.mem_read(meta,20)));assert fields==(1,1000+slot,len(p),store.crc(p),1),fields
        assert bytes(h.fs.disk)==preset
        results.append('Five immutable initial slots recover via the native missing-file path without creating files or changing the disk')
        assert h.exchange(lib,wire.msg(1,session=0,tx=9))[0]==0
        slot=4;data=store.payload(slot,1);metadata=wire.metadata(slot)
        assert h.exchange(lib,wire.msg(2,slot,tx=222,number=1,data=struct.pack('<II',len(data),store.crc(data))+metadata))[0]==0
        for p in range(0,len(data),256):assert h.exchange(lib,wire.msg(3,slot,tx=222,number=p,data=data[p:p+256]))[0]==0
        assert h.exchange(lib,wire.msg(4,slot,tx=222))[0]==6
        rc,ack=h.exchange(lib,wire.msg(5,slot,tx=223));assert rc==0 and struct.unpack('<I',bytes(ack.data[4:8]))[0]==2
        filename='cfcx_'+edition.hex()+'_4_0';assert h.fs.read(filename,256,len(data))==data
        assert h.fs.read('bank_0_channel_a',0,24)==b'protected fixture preset'
        assert bytes(h.u.mem_read(ROM,len(seed)))==seed
        disk=bytes(h.fs.disk);results.append('A MIDI upload replaces only NAM 2 in a real native file with the edition namespace and leaves ROM and preset intact')
        # New RAM/task instances on the SAME synthetic persistent disk.
        original=actor.fsmod.NativeFS
        actor.fsmod.NativeFS=lambda:original(disk)
        try:second=actor.Harness()
        finally:actor.fsmod.NativeFS=original
        bind(second);s2=second.call('cf_actor_store_address');assert second.call('cf_asset_recover',s2,4,meta)==0
        assert struct.unpack('<I',bytes(second.u.mem_read(meta,4)))[0]==2
        results.append('Recreated native filesystem and actor recover the uploaded generation after simulated cold reopen')
        actor.fsmod.NativeFS=lambda:original(disk)
        try:third=actor.Harness()
        finally:actor.fsmod.NativeFS=original
        changed=hashlib.sha256(b'new full CF edition').digest();bind(third,manifest(payloads,changed));s3=third.call('cf_actor_store_address');assert third.call('cf_asset_recover',s3,4,meta)==0
        assert struct.unpack('<I',bytes(third.u.mem_read(meta,4)))[0]==1 and third.fs.read(filename,256,len(data))==data
        assert third.fs.read('bank_0_channel_a',0,24)==b'protected fixture preset'
        results.append('A new full-CF edition uses its own initial resources while previous edition data and native presets remain preserved')
    report=dict(passed=True,hardware_accessed=False,USB_opened=False,SDK_loaded=False,MMIO_accessed=False,firmware_created=False,actor_elf_sha256=info['elf_sha256'],checks=results,
                limitations=['RTOS transport and flash HAL are emulator fixtures','Native bootstrap, real cold boot and physical memory/cache ownership remain pending'])
    (BUILD/'seeded-tests.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':main()
