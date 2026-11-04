"""NON-INSTALLABLE memory-image/Reset_Handler laboratory. No CTF or USB.

Adds explicit copy/zero rows for the prototype and checks the actual native
reset loops in Unicorn. Hardware SDRAM init remains stubbed. A gap after BSS
and absence of literal pointers are NOT proof of physical DMA/cache ownership.
"""
import argparse,hashlib,json,struct,sys,importlib.util
from pathlib import Path
ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent;IR=NAM.parent/'catalyst-ir';EDITOR=NAM.parent/'catalyst-editor'
sys.path[:0]=[str(EDITOR/'tools/disasm'),str(EDITOR/'tools/emulation'),str(NAM),str(IR)]
import capstone,unicorn
# Reuse the read-only decoder and reset fixture with assembly prohibited.
spec=importlib.util.spec_from_file_location('dual_native',NAM/'dual-lab/verify_native.py');native=importlib.util.module_from_spec(spec);spec.loader.exec_module(native)
from build import elf
from ctf_tool import parse
from package_extension import decode_hex
from verify_tcm_ir import bootstrap
BASE=0x60001000
def sha(b):return hashlib.sha256(b).hexdigest()
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--owner-loop',action='store_true');options=parser.parse_args()
    source=(NAM/'app/resources/nam-engine.ctf').read_bytes();assert sha(source)=='e80bba31906c0032b4b92f9d062c54879ea88fbabbf0a9d073dd1794d2a287a1'
    _,raw,regions=parse(source);hex_image=next(raw[r['offset']:r['offset']+r['size']] for r in regions if r['kind']==0)
    original=decode_hex(hex_image)[0][1][1];image=bytearray(original)
    maps=json.loads((IR/'analysis/runtime-maps.json').read_text())
    def physical(a):
        for row in maps:
            if row['destination']<=a<row['end']:return row['source']+a-row['destination']
        return a
    def offset(a):return physical(a)-BASE
    def get(a):return struct.unpack_from('<I',image,offset(a))[0]
    def put(a,b):
        p=offset(a);assert p>=0
        if p+len(b)>len(image):image.extend(b'\xff'*(p+len(b)-len(image)))
        image[p:p+len(b)]=b
    changes=[]
    def word(a,expected,next,purpose):
        assert get(a)==expected,(hex(a),hex(get(a)),hex(expected));put(a,struct.pack('<I',next));changes.append(dict(address=hex(a),before=hex(expected),after=hex(next),purpose=purpose))
    copy_start=get(0x60038b30);copy_end=get(0x60038b2c);assert copy_start==0x601dc000 and copy_end-copy_start==120
    rows=[struct.unpack_from('<3I',image,copy_start-BASE+i*12) for i in range(10)]
    assert rows[6][1:]==(0x15000,0x18000) and rows[9][1:]==(0x20064880,0x20065d00)
    oldzero=get(0x60038b34);oldzeroend=get(0x60038b38)
    assert (oldzero,oldzeroend)==(0x601ab1e8,0x601ab1f8)
    zeros=[struct.unpack_from('<2I',image,oldzero-BASE+i*8) for i in range((oldzeroend-oldzero)//8)]
    assert zeros[-1]==(0x801723d0,0x8085ebbc)
    actor_folder=ROOT/('actor-owner-lab/actor-build' if options.owner_loop else 'actor-build')
    audio_folder=NAM/'dual-lab'/('lease-build' if options.owner_loop else 'build')
    actorseg,actorsym=elf(actor_folder/'actor.elf');audioseg,audiosym=elf(audio_folder/'combined.elf');oldsym=elf(NAM/'build-trial/trial.elf')[1]
    actorcode=next(s for s in actorseg if s['flags']&1);audiocode=next(s for s in audioseg if s['flags']&1);tables=next(s for s in audioseg if s['address']==0x20064b00)
    assert audiocode['address']+audiocode['size']<=0x17e00 and tables['address']+tables['size']<=0x200657e0
    newrange=(0x80860000,0x80872000);coderange=(actorcode['address'],actorcode['address']+((actorcode['size']+3)&~3))
    code_limit=0x8087c000 if options.owner_loop else 0x80878000
    assert zeros[-1][1]<newrange[0]<newrange[1]<coderange[0] and coderange[1]<code_limit
    literal_refs=[];stock=(IR/'analysis/image-0-segment-1-60001000.bin').read_bytes()
    inspect_spec=importlib.util.spec_from_file_location('layout_disasm',ROOT/'inspect_storage.py');inspect=importlib.util.module_from_spec(inspect_spec);inspect_spec.loader.exec_module(inspect)
    for p in range(0,len(stock)-3,4):
        v=struct.unpack_from('<I',stock,p)[0]
        if newrange[0]<=v<code_limit:
            run=inspect.runtime(BASE+p);decoded=list(inspect.md.disasm(inspect.read(run,4),run))
            branch=len(decoded)==1 and decoded[0].size==4 and decoded[0].mnemonic in ('beq.w','bne.w','b.w','bl')
            literal_refs.append(dict(flash=hex(BASE+p),runtime=hex(run),value=hex(v),classification='branch_instruction_encoding' if branch else 'unresolved',instruction=inspect.listing(run,4),context=inspect.listing(run-12,24)))
    unresolved=[r for r in literal_refs if r['classification']=='unresolved']
    # This copy is a neutral laboratory image; no user capture or IR is added.
    put(rows[6][0],audiocode['data']+b'\xff'*(0x2e00-len(audiocode['data'])))
    runtime=bytearray(original[rows[9][0]-BASE:rows[9][0]-BASE+rows[9][2]-rows[9][1]])
    start=0x20064900-rows[9][1];runtime[start+284:start+512]=bytes(228)
    for off,value in ((16,.5),(20,.5),(24,1.),(28,.5),(32,.5),(36,1.)):struct.pack_into('<f',runtime,start+284+off,value)
    tabledata=bytearray(tables['data'])
    for name in ('silence','silence2'):
        if name not in audiosym:continue
        p=audiosym[name]['address']-tables['address'];tabledata[p:p+288]=bytes(288)
    p=tables['address']-rows[9][1];runtime[p:p+len(tabledata)]=tabledata;put(rows[9][0],runtime)
    thunk=bytearray(image[rows[6][0]-BASE+0x2e80:rows[6][0]-BASE+0x3000])
    for name in ('trial_begin','trial_route','trial_after','trial_node_control'):
        old=(oldsym[name]['address']|1).to_bytes(4,'little');target=actorsym['cf_actor_audio_begin'] if options.owner_loop and name=='trial_begin' else audiosym[name];new=(target['address']|1).to_bytes(4,'little');assert thunk.count(old)==1;thunk=thunk.replace(old,new)
    put(rows[6][0]+0x2e80,thunk)
    # Patch only the receive-call pairs and the constructor's handler literal.
    spec2=importlib.util.spec_from_file_location('actor_bl',ROOT/'verify_actor.py');helper=importlib.util.module_from_spec(spec2);spec2.loader.exec_module(helper)
    native_patches=[]
    for a in (0x8001d2be,0x8001d324):
        assert image[offset(a):offset(a)+4]==bytes.fromhex('b8 47 f7 28');put(a,helper.bl(a,actorsym['cf_receive_wrapper']['address']));native_patches.append((a,4))
    word(0x80016000,0x80015dc5,actorsym['cf_fs_dispatch']['address']|1,'Preserve native dispatcher through CF wrapper');native_patches.append((0x80016000,4))
    if options.owner_loop:
        # Replace precisely one FS-task wait, not the shared RTOS function.
        address=0x80015d36;assert bytes(image[offset(address):offset(address)+4])==helper.bl(address,0x8004c8f8)
        put(address,helper.bl(address,actorsym['cf_fs_wait']['address']));native_patches.append((address,4))
        for address,expected,name in ((0x80039a94,0x14241,'cf_actor_ir_set_data'),(0x80039a90,0x14201,'cf_actor_ir_reset')):
            word(address,expected,actorsym[name]['address']|1,'Native IR selection/reset observer');native_patches.append((address,4))
    flash=0x601dd000;put(flash,actorcode['data']+b'\xff'*(coderange[1]-coderange[0]-len(actorcode['data'])))
    rows.append((flash,*coderange));put(copy_start,b''.join(struct.pack('<3I',*r) for r in rows))
    if options.owner_loop:
        assert flash+actorcode['size']<=0x601e3000
        from verify_seeded_actor import manifest
        from verify_store import payload
        seed=manifest([payload(i) for i in range(5)],hashlib.sha256(b'boot-reset-only-synthetic-fixture').digest())
        put(0x601e3000,seed)
    word(0x60038b2c,copy_end,copy_start+len(rows)*12,'Additional explicit SDRAM code copy')
    zeros.append(newrange);newzero=0x601dc100;put(newzero,b''.join(struct.pack('<2I',*r) for r in zeros))
    word(0x60038b34,oldzero,newzero,'Relocated zero-table start');word(0x60038b38,oldzeroend,newzero+len(zeros)*8,'Relocated zero-table end')
    assert BASE+len(image)<=0x60200000
    # Actual application reset copies/clears run; hardware init is stubbed.
    baseline=bootstrap(original);boot=bootstrap(bytes(image))
    for src,dst,end in rows:assert bytes(boot.mem_read(dst,end-dst))==image[src-BASE:src-BASE+end-dst]
    for start,end in zeros:assert bytes(boot.mem_read(start,end-start))==bytes(end-start)
    assert bytes(boot.mem_read(0x20000000,0x3ae30))==bytes(baseline.mem_read(0x20000000,0x3ae30))
    permitted={a+i for a,n in native_patches for i in range(n)};oldcode=bytes(baseline.mem_read(0x80000000,0x1723d0));newcode=bytes(boot.mem_read(0x80000000,0x1723d0))
    diff={0x80000000+i for i,(a,b) in enumerate(zip(oldcode,newcode)) if a!=b};assert diff<=permitted
    assert bytes(boot.mem_read(0x80016b74,16))==bytes(baseline.mem_read(0x80016b74,16))
    assert get(0x60038944)==0xaaaaaabf and get(0x60038948)==0x20068000
    out=ROOT/('boot-owner-lab' if options.owner_loop else 'boot-build');out.mkdir(exist_ok=True);(out/'NON-INSTALLABLE-segment.bin').write_bytes(image)
    report=dict(passed=True,scope='Reset copy/zero checks ONLY',hardware_layout_approved=False,native_reference_collision_requires_resolution=bool(unresolved),hardware_candidate_allowed=False,hardware_accessed=False,firmware_created=False,output_is_noninstallable_segment=True,base_engine_unchanged=sha((NAM/'app/resources/nam-engine.ctf').read_bytes())==sha(source),segment_sha256=sha(image),image_end=hex(BASE+len(image)),initial_msp_unchanged=True,flexram_partition_unchanged=True,original_identity_entry_unchanged=True,original_control_ram_unchanged=True,original_runtime_changed_bytes=len(diff),
      explicit_copy_rows=[list(map(hex,r)) for r in rows],explicit_zero_rows=[list(map(hex,r)) for r in zeros],native_literal_candidates_in_proposed_reservation=literal_refs,patches=changes,
      owner_loop_included=options.owner_loop,actor_elf_sha256=sha((actor_folder/'actor.elf').read_bytes()),audio_elf_sha256=sha((audio_folder/'combined.elf').read_bytes()),verification_source_sha256=sha(Path(__file__).read_bytes()),
      limits=['No physical SDRAM/DMA/cache ownership proof','Hardware init is stubbed, no real cold boot','Owner bootstrap/FS scheduling separately emulated; native IR selection and preamp control remain incomplete','No CTF/updater/USB access'])
    (out/'tests.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':
    try:main()
    except BaseException:
        import traceback
        out=ROOT/'boot-build';out.mkdir(exist_ok=True);(out/'error.txt').write_text(traceback.format_exc());raise
