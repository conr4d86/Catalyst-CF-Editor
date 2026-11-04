"""Read-only allocator/reservation leads. No physical ownership assertion."""
import hashlib,json,re,struct,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT.parents[1]/'catalyst-editor/tools-recovery'))
import capstone
import inspect_storage as binary
def main():
    values={0x8085ebbc,0x80860000,0x80872000,0x80874000,0x80878000,0x81000000,0x82000000,0x8002d8c9,0x8002d8c8}
    literals={}
    for p in range(0,len(binary.B)-3,4):
        v=struct.unpack_from('<I',binary.B,p)[0]
        if v in values:literals[binary.runtime(0x60001000+p)]=v
    refs=[];md=capstone.Cs(capstone.CS_ARCH_ARM,capstone.CS_MODE_THUMB);md.skipdata=True
    for m in binary.MAPS:
        if 0x20000000<=m['destination']<0x80000000:continue
        for i in md.disasm(binary.read(m['destination'],m['end']-m['destination']),m['destination']):
            if i.mnemonic.startswith('ldr'):
                found=re.search(r'\[pc, #(-?(?:0x[0-9a-f]+|[0-9]+))\]',i.op_str)
                if found:
                    address=((i.address+4)&~3)+int(found[1],0)
                    if address in literals:refs.append(dict(instruction=hex(i.address),literal=hex(literals[address]),context=binary.listing(max(m['destination'],i.address-32),112)))
            if i.mnemonic in ('bl','b.w') and i.op_str in ('#0x8002d8c8','#0x8002d8c9'):
                refs.append(dict(allocator_call=hex(i.address),context=binary.listing(max(m['destination'],i.address-24),80)))
    report=dict(hardware_accessed=False,physical_memory_ownership_approved=False,stock_sha256=hashlib.sha256(binary.B).hexdigest(),
        literals={hex(k):hex(v) for k,v in literals.items()},references=refs,
        allocator_candidate=binary.listing(0x8002d8c8,224),
        limitation='Literal/call leads only. Computed ranges, heap/DMA and scheduler ownership must still be traced before a physical reservation.')
    (ROOT/'heap-leads.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':main()
