"""Read-only memory-boundary leads. No inference of safe allocation from a gap."""
import hashlib,json,re,struct
from pathlib import Path
import inspect_storage as binary
ROOT=Path(__file__).resolve().parent
VALUES={0x8085ebbc:'native BSS end',0x8085ebc0:'aligned native BSS end',0x80860000:'proposed actor context',
    0x81fffff0:'SDRAM upper boundary candidate',0x81ffffff:'SDRAM inclusive upper boundary candidate',
    0x82000000:'SDRAM exclusive upper boundary candidate',0x81ff0000:'upper region candidate',
    0x81000000:'16 MiB boundary',0x80800000:'8 MiB boundary'}

def main():
    literals={}
    for p in range(0,len(binary.B)-3,4):
        value=struct.unpack_from('<I',binary.B,p)[0]
        if value in VALUES:literals[binary.runtime(0x60001000+p)]=(value,p)
    refs=[]
    for region in binary.MAPS:
        start=region['destination'];length=region['end']-start
        if 0x20000000<=start<0x80000000:continue
        for op in binary.md.disasm(binary.read(start,length),start):
            if not op.mnemonic.startswith('ldr'):continue
            match=re.search(r'\[pc, #(-?(?:0x[0-9a-f]+|[0-9]+))\]',op.op_str)
            if not match:continue
            address=((op.address+4)&~3)+int(match[1],0)
            if address in literals:
                value,p=literals[address];refs.append(dict(instruction=hex(op.address),literal=hex(address),value=hex(value),meaning=VALUES[value],context=binary.listing(max(start,op.address-24),72)))
    report=dict(hardware_accessed=False,stock_sha256=hashlib.sha256(binary.B).hexdigest(),
        literals=[dict(address=hex(a),value=hex(v),meaning=VALUES[v],has_pc_relative_reference=any(r['literal']==hex(a) for r in refs)) for a,(v,p) in literals.items()],pc_relative_references=refs,
        scope='Literal leads only. Arithmetic/indirect pools, heap and DMA ownership remain unproven.',hardware_layout_approved=False)
    (ROOT/'memory-boundaries.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':main()
