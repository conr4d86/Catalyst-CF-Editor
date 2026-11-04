"""Static budget ONLY. An address layout is not physical ownership approval."""
import argparse,hashlib,json
from pathlib import Path
ROOT=Path(__file__).resolve().parent
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--owner-loop',action='store_true');options=parser.parse_args()
    build=ROOT/('actor-owner-lab/actor-build/build.json' if options.owner_loop else 'actor-preamp-lab/actor-build/build.json');info=json.loads(build.read_text())
    assert info['preamp_presets'] and info['coordinator'] and info['leased_staging']
    image=build.parent/'actor.elf';assert hashlib.sha256(image.read_bytes()).hexdigest()==info['elf_sha256']
    base,end=0x80860000,0x80872000;cursor=base;regions=[]
    def reserve(name,size,purpose):
        nonlocal cursor
        cursor=(cursor+31)&~31;start=cursor;cursor+=size;assert cursor<=end,(name,hex(cursor))
        regions.append(dict(name=name,start=hex(start),end=hex(cursor),bytes=size,purpose=purpose))
    reserve('actor_BSS',info['bss_bytes'],'Measured complete actor including coordinator, staging, IR handoff, preamp store and native-preset job; no duplicate budgets')
    for slot in (1,2):
        for bank in ('current','replacement'):reserve(f'NAM{slot}_{bank}',7772,'Immutable weights + silence; never overwrite until audio owner retires this bank')
    reserve('IR_load_window',25088,'One full verified IR payload, shared between IR selections; evict handoff references before reuse')
    code_limit=int(info['code_limit'],16)
    assert cursor<0x80874000 and 0x80874000+info['text_bytes']<=code_limit
    report=dict(hardware_accessed=False,firmware_created=False,static_budget_fits=True,physical_memory_ownership_approved=False,execution_permission_approved=False,
                reservation_start=hex(base),reservation_end=hex(end),reserved_bytes=end-base,used_with_alignment=cursor-base,remaining_bytes=end-cursor,regions=regions,
                code_start='0x80874000',code_end=hex(0x80874000+info['text_bytes']),code_limit=hex(code_limit),actor_elf_sha256=info['elf_sha256'],
                prerequisites=['Physical SDRAM/cache/DMA/IRQ and MPU ownership still required','IR cache requires audio-owner eviction acknowledgement before reuse','Retain each current NAM bank until outer audio acquisition retires the old source','Measured structs fit in actor BSS; actual bootstrap, scheduler and native selection hooks remain required','No heap, runtime or DTCM expansion is assumed'],
                sources={str(p.name):hashlib.sha256(p.read_bytes()).hexdigest() for p in (build,ROOT/'plan_asset_memory.py')})
    (ROOT/('owner-memory-plan.json' if options.owner_loop else 'asset-memory-plan.json')).write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':main()
