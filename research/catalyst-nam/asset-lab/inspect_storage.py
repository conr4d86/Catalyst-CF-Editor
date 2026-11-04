"""Read-only firmware storage xrefs. Never loads a driver or opens a device."""
import hashlib,json,re,struct,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent
EDITOR=ROOT.parents[1]/'catalyst-editor'
sys.path.insert(0,str(EDITOR/'tools/disasm'))
from capstone import Cs,CS_ARCH_ARM,CS_MODE_THUMB
IR=ROOT.parents[1]/'catalyst-ir'
B=(IR/'analysis/image-0-segment-1-60001000.bin').read_bytes()
MAPS=json.loads((IR/'analysis/runtime-maps.json').read_text())
def runtime(a):
    for m in MAPS:
        if m['source']<=a<m['source']+m['end']-m['destination']:return m['destination']+a-m['source']
    return a
def physical(a):
    for m in MAPS:
        if m['destination']<=a<m['end']:return m['source']+a-m['destination']
    return a
def read(a,n):
    p=physical(a)-0x60001000
    if p<0 or p+n>len(B):raise ValueError(hex(a))
    return B[p:p+n]
md=Cs(CS_ARCH_ARM,CS_MODE_THUMB);md.skipdata=True
def listing(a,n):
    return [f'{i.address:08x} {i.mnemonic} {i.op_str}' for i in md.disasm(read(a,n),a)]
def main():
    names={}
    for match in re.finditer(rb'[ -~]{4,}',B):
        text=match.group().decode()
        if text in ('FileSystem:task','littlefs','bank_%d_channel_%s','a45_metadata','globals','channel_manual_edited','channel_a_edited','channel_b_edited'):
            names[runtime(0x60001000+match.start())]=text
    literals=[]
    for m in MAPS:
        start=m['destination']
        if 0x20000000<=start<0x80000000:continue
        end=m['end']
        for i in md.disasm(read(start,end-start),start):
            if not i.mnemonic.startswith('ldr'):continue
            found=re.search(r'\[pc, #(-?(?:0x[0-9a-f]+|[0-9]+))\]',i.op_str)
            if not found:continue
            slot=((i.address+4)&~3)+int(found[1],0)
            try:v=struct.unpack('<I',read(slot,4))[0]
            except ValueError:continue
            if v in names:literals.append(dict(address=hex(i.address),name=names[v],value=hex(v),context=listing(max(start,i.address-48),176)))
    report=dict(hardware_accessed=False,stock_sha256=hashlib.sha256(B).hexdigest(),strings={hex(a):s for a,s in names.items()},literal_xrefs=literals,
                filesystem_config_function='0x8002d848',block_size=4096,block_count=4096,
                filesystem_range=['0x61000000','0x62000000'],
                limitation='Addresses and strings are static leads. No storage write API or concurrency contract is validated.')
    out=ROOT/'storage-xrefs.json';out.write_text(json.dumps(report,indent=2));print(json.dumps({k:v for k,v in report.items() if k!='literal_xrefs'},indent=2));print('xref_count',len(literals))
if __name__=='__main__':main()
