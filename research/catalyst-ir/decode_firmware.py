from pathlib import Path
import json, struct, re, hashlib, collections, math
ROOT=Path(__file__).resolve().parent
OUT=ROOT/'analysis'
b=(OUT/'firmware.decompressed.bin').read_bytes()
regions=json.loads((OUT/'ctf_chunks.json').read_text())
fw=(ROOT/'originals/Catalyst_60_100_200_v2.02.0-rc9_0x77f2d40.ctf').read_bytes()
images=[]
for ch in regions:
 if ch['tag']!='dinf':continue
 d=fw[ch['offset']+8:ch['offset']+24]
 kind,ver,offset,length=struct.unpack('>IIII',d)
 raw=b[offset:offset+length]
 if not raw.startswith(b':'):
  (OUT/f'image-{kind}.bin').write_bytes(raw)
  print('raw binary image',kind,len(raw),raw[:128].hex(' '))
  images.append(dict(kind=kind,version=hex(ver),offset=offset,length=length,encoding='binary',sha256=hashlib.sha256(raw).hexdigest()))
  continue
 (OUT/f'image-{kind}.hex').write_bytes(raw)
 blocks=[];base=0;current=None;checks=0;types=collections.Counter()
 for i,line in enumerate(raw.splitlines()):
  if not line:continue
  assert line.startswith(b':'),(i,line[:50])
  r=bytes.fromhex(line[1:].decode())
  assert len(r)==r[0]+5 and sum(r)%256==0,(i,line)
  checks+=1; n=r[0];a=int.from_bytes(r[1:3],'big');t=r[3];v=r[4:4+n];types[t]+=1
  if t==4:base=int.from_bytes(v,'big')<<16
  elif t==0:
   addr=base+a
   if current is None or current[0]+len(current[1])!=addr:
    current=[addr,bytearray()];blocks.append(current)
   current[1].extend(v)
 info=dict(kind=kind,version=hex(ver),offset=offset,length=length,validated_records=checks,record_types=dict(types),segments=[])
 for j,(addr,data) in enumerate(blocks):
  dest=OUT/f'image-{kind}-segment-{j}-{addr:08x}.bin';dest.write_bytes(data)
  ss=[dict(offset=hex(m.start()),address=hex(addr+m.start()),text=m.group().decode('ascii')) for m in re.finditer(rb'[ -~]{6,}',data)]
  (OUT/(dest.name+'.strings.json')).write_text(json.dumps(ss,indent=1))
  seg=dict(address=hex(addr),size=len(data),file=str(dest),sha256=hashlib.sha256(data).hexdigest(),strings=len(ss))
  info['segments'].append(seg)
  print(seg);print('first bytes',data[:128].hex(' '));print('interesting strings',[s for s in ss if re.search(r'(?i)cab|impulse|convol|sharc|cortex|catalyst|checksum|boot|flash|signature|L6Image',s['text'])][:80])
 images.append(info)
(OUT/'images.json').write_text(json.dumps(images,indent=2))
print(json.dumps([{k:v for k,v in im.items() if k!='segments'} for im in images],indent=2))
