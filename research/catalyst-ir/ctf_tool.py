"""Parse/rebuild Line 6 L6FF containers offline. Never accesses USB.

Repacking is format work only, not proof that a device accepts a modified file.
"""
from pathlib import Path
import argparse,hashlib,struct,zlib,json

def parse(blob):
 if blob[:4]!=b'FORM' or blob[8:12]!=b'L6FF':raise ValueError('Expected FORM/L6FF')
 if struct.unpack_from('>I',blob,4)[0]!=len(blob)-8:raise ValueError('Bad FORM size')
 chunks=[];p=12
 while p<len(blob):
  if p+8>len(blob):raise ValueError('Truncated chunk header')
  tag=blob[p:p+4];size=struct.unpack_from('>I',blob,p+4)[0];d=blob[p+8:p+8+size]
  if len(d)!=size:raise ValueError('Truncated chunk')
  chunks.append((tag,d));p+=8+size
 heads=[d for tag,d in chunks if tag==b'HEAD'];data=[d for tag,d in chunks if tag==b'data']
 if len(heads)!=1 or len(data)!=1:raise ValueError('Unsupported HEAD/data layout')
 raw=zlib.decompress(data[0]);head=heads[0]
 if len(head)!=52:raise ValueError('Unsupported HEAD size')
 if struct.unpack_from('>I',head,16)[0]!=len(raw):raise ValueError('Bad decompressed size')
 if head[20:36]!=hashlib.md5(raw).digest():raise ValueError('Bad payload MD5')
 regions=[]
 for tag,d in chunks:
  if tag==b'dinf':
   if len(d)!=16:raise ValueError('Unsupported dinf')
   kind,version,offset,size=struct.unpack('>4I',d)
   if offset+size>len(raw):raise ValueError('Image outside payload')
   regions.append(dict(kind=kind,version=version,offset=offset,size=size))
 if not regions:raise ValueError('No images')
 ordered=sorted(regions,key=lambda r:r['offset']);expected=0
 for r in ordered:
  if r['offset']!=expected:raise ValueError('Non-contiguous image layout')
  expected+=r['size']
 if expected!=len(raw):raise ValueError('Unreferenced payload bytes')
 return chunks,raw,regions

def repack(blob,images):
 chunks,raw,regions=parse(blob)
 if set(images)!={r['kind'] for r in regions}:raise ValueError('Provide every image exactly once')
 offset=0;metadata={};pieces=[]
 for r in sorted(regions,key=lambda r:r['offset']):
  piece=images[r['kind']];pieces.append(piece)
  metadata[r['kind']]=(r['kind'],r['version'],offset,len(piece));offset+=len(piece)
 raw=b''.join(pieces);parts=[]
 for tag,d in chunks:
  if tag==b'HEAD':
   h=bytearray(d);struct.pack_into('>I',h,16,len(raw));h[20:36]=hashlib.md5(raw).digest();d=bytes(h)
  elif tag==b'dinf':d=struct.pack('>4I',*metadata[struct.unpack_from('>I',d)[0]])
  elif tag==b'data':d=zlib.compress(raw,9)
  parts.append(tag+struct.pack('>I',len(d))+d)
 body=b'L6FF'+b''.join(parts);result=b'FORM'+struct.pack('>I',len(body))+body
 _,verified,newregions=parse(result)
 assert verified==raw
 return result

def roundtrip(path):
 original=path.read_bytes();chunks,raw,regions=parse(original)
 images={r['kind']:raw[r['offset']:r['offset']+r['size']] for r in regions}
 rebuilt=repack(original,images);newchunks,newraw,newregions=parse(rebuilt)
 assert raw==newraw and regions==newregions
 assert [(t,d) for t,d in chunks if t!=b'data']==[(t,d) for t,d in newchunks if t!=b'data']
 return dict(passed=True,original_bytes=len(original),rebuilt_bytes=len(rebuilt),
             byte_identical=original==rebuilt,payload_sha256=hashlib.sha256(raw).hexdigest(),
             images=regions,device_acceptance='not tested; no device accessed')

if __name__=='__main__':
 parser=argparse.ArgumentParser();parser.add_argument('file',type=Path);parser.add_argument('--roundtrip',action='store_true')
 args=parser.parse_args()
 if args.roundtrip:print(json.dumps(roundtrip(args.file),indent=2))
 else:
  chunks,raw,regions=parse(args.file.read_bytes());print(json.dumps(dict(images=regions,payload_bytes=len(raw),md5=hashlib.md5(raw).hexdigest()),indent=2))
