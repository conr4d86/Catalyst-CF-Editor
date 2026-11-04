from pathlib import Path
import json,struct,sys
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT/'tools/python'))
from capstone import Cs, CS_ARCH_ARM, CS_MODE_THUMB
OUT=ROOT/'analysis'
MAPS=json.loads((OUT/'runtime-maps.json').read_text())
FW=(OUT/'image-0-segment-1-60001000.bin').read_bytes()
def physical(a):
 for m in MAPS:
  if m['destination']<=a<m['end']: return m['source']+a-m['destination']
 return a
def read(a,n):
 p=physical(a)-0x60001000
 if not 0<=p<len(FW):raise ValueError(hex(a))
 return FW[p:p+n]
def word(a):return struct.unpack('<I',read(a,4))[0]
def dump(a,n):
 for p in range(a,a+n,16):
  d=read(p,16)
  print(f'{p:08x}', ' '.join(f'{v:08x}' for v in struct.unpack('<4I',d)), ''.join(chr(c) if 32<=c<127 else '.' for c in d))
def disasm(a,n):
 md=Cs(CS_ARCH_ARM,CS_MODE_THUMB);md.skipdata=True
 import re
 result=[]
 for i in md.disasm(read(a,n),a):
  note=''
  m=re.search(r'\[pc, #(0x[0-9a-f]+|[0-9]+)\]',i.op_str)
  if m:
   addr=((i.address+4)&~3)+int(m.group(1),0)
   try:
    v=word(addr);note=f' ; [{addr:08x}]={v:08x}'
    try:
     s=read(v,90).split(b'\0')[0]
     if len(s)>3 and all(32<=c<127 for c in s):note+=' '+s.decode()
    except ValueError:pass
   except ValueError:pass
  result.append(f'{i.address:08x} {i.mnemonic:10} {i.op_str}{note}')
 return '\n'.join(result)
if __name__=='__main__':
 a=int(sys.argv[1],0);n=int(sys.argv[2],0)
 print(disasm(a,n))
