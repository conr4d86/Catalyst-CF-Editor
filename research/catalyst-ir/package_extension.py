"""Package the experimental image offline, preserving stock HEX record layout.

Does not invoke an updater or access a device. Output remains hardware-untested.
"""
from arm_tools import ROOT,OUT,FW
from build_ir_extension import build_extension
from ctf_tool import parse,repack
import struct,hashlib,json,argparse

ORIGINAL=ROOT/'originals/Catalyst_60_100_200_v2.02.0-rc9_0x77f2d40.ctf'
def record(address,kind,data):
 b=bytes([len(data)])+address.to_bytes(2,'big')+bytes([kind])+data
 return b':'+(b+bytes([(-sum(b))&255])).hex().upper().encode()+b'\r\n'

def decode_hex(blob):
 segments=[];base=0;eof=False;records=0
 for line in blob.splitlines():
  if not line:continue
  if eof or not line.startswith(b':'):raise ValueError('Invalid HEX sequence')
  b=bytes.fromhex(line[1:].decode());records+=1
  if len(b)!=b[0]+5 or sum(b)&255:raise ValueError('Invalid HEX checksum/length')
  n=b[0];a=int.from_bytes(b[1:3],'big');kind=b[3];data=b[4:-1]
  if kind==4:
   if n!=2 or a:raise ValueError('Invalid extended address')
   base=int.from_bytes(data,'big')<<16
  elif kind==0:
   address=base+a
   if segments and address<segments[-1][0]+len(segments[-1][1]):raise ValueError('Overlapping/unsorted HEX')
   if segments and address==segments[-1][0]+len(segments[-1][1]):segments[-1][1].extend(data)
   else:segments.append([address,bytearray(data)])
  elif kind==1:
   if n or a:raise ValueError('Invalid EOF')
   eof=True
  elif kind==5:
   if n!=4 or a:raise ValueError('Invalid start-linear-address record')
  else:raise ValueError('Unsupported HEX record type')
 if not eof:raise ValueError('Missing EOF')
 return [(a,bytes(b)) for a,b in segments],records

def patch_hex(original,patched):
 segments,_=decode_hex(original)
 assert len(segments)==2 and segments[1]==(0x60001000,FW)
 result=[];postamble=[];base=0;changed_records=0
 for line in original.splitlines(keepends=True):
  b=bytes.fromhex(line.strip()[1:].decode());kind=b[3];a=int.from_bytes(b[1:3],'big')
  if kind==4:base=int.from_bytes(b[4:-1],'big')<<16
  if kind==1:break
  # Line 6 Central stops consuming HEX on type 5. Keep that original
  # start-address record, but only AFTER every appended extension byte.
  if kind==5:postamble.append(line);continue
  address=base+a
  if kind==0 and address>=0x60001000:
   data=patched[address-0x60001000:address-0x60001000+b[0]]
   if data!=b[4:-1]:line=record(a,0,data);changed_records+=1
  result.append(line)
 address=0x60001000+len(FW);end=0x60001000+len(patched)
 while address<end:
  if address&0xffff0000!=base:
   base=address&0xffff0000;result.append(record(0,4,(base>>16).to_bytes(2,'big')))
  n=min(16,end-address,0x10000-(address&0xffff));off=address-0x60001000
  result.append(record(address&0xffff,0,patched[off:off+n]));address+=n
 result.extend(postamble);result.append(record(0,1,b''));output=b''.join(result)
 verified,count=decode_hex(output)
 assert verified==[segments[0],(0x60001000,patched)]
 return output,dict(changed_existing_hex_records=changed_records,validated_records=count)

def build(output_dir):
 output_dir=output_dir.resolve();output_dir.mkdir(parents=True,exist_ok=True)
 original=ORIGINAL.read_bytes();_,raw,regions=parse(original)
 assert hashlib.sha256(original).hexdigest()=='f9fe2482e561bef8a04128a38af89cadee4f8d06071b06a9053e36c206471ae8'
 images={r['kind']:raw[r['offset']:r['offset']+r['size']] for r in regions}
 patched,plan,_=build_extension();images[0],info=patch_hex(images[0],patched)
 result=repack(original,images);_,verified,metadata=parse(result)
 newimages={r['kind']:verified[r['offset']:r['offset']+r['size']] for r in metadata}
 assert images==newimages
 assert newimages[5]==raw[regions[1]['offset']:regions[1]['offset']+regions[1]['size']]
 destination=output_dir/'Catalyst-2.02-UserIR-EXPERIMENTAL.ctf'
 # Preserve any earlier candidate rather than silently replacing it.
 if destination.exists() and destination.read_bytes()!=result:raise FileExistsError(destination)
 destination.write_bytes(result)
 plan.update(container_sha256=hashlib.sha256(result).hexdigest(),container_bytes=len(result),
             packaging='Start-linear-address type 5 follows ALL original and extension data; Central native-reader gate required',
             hex_validation=info,unchanged_kind5_sha256=hashlib.sha256(newimages[5]).hexdigest(),
             install_eligible=False,hardware_tests_passed=False,
             required_before_programming=['Preset backup verified','Live hardware identity/version match',
              'Physical SDRAM/MPU/flash bounds confirmed','Real-time CPU margin assessed',
              'Update/boot verification and recovery strategy reviewed'])
 (output_dir/'candidate-manifest.json').write_text(json.dumps(plan,indent=2))
 return plan

if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--output',type=__import__('pathlib').Path,default=OUT/'candidate')
 args=p.parse_args();print(json.dumps(build(args.output),indent=2))
