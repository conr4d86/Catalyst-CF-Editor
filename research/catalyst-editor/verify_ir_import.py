"""Synthetic format/response tests and one private regression IR. No device APIs.
The FLAC fixture uses RFC 9639 verbatim frames; no encoder executable is needed.
"""
from pathlib import Path
import hashlib,json,math,struct,subprocess,uuid,os

ROOT=Path(__file__).resolve().parent
OUT=ROOT/'checks/ir-import'/('general-'+uuid.uuid4().hex[:10]);OUT.mkdir(parents=True,exist_ok=True)
EXE=ROOT/'checks/ir-import/IRImportProbe.exe'

def chunk(tag,data,big=False):
    return tag+struct.pack('>I' if big else '<I',len(data))+data+(b'\0' if len(data)&1 else b'')

def pcm(samples,bits,kind=1,big=False,wave=True):
    result=bytearray()
    for x in samples:
        if kind==3:result+=struct.pack(('>' if big else '<')+('f' if bits==32 else 'd'),x)
        else:
            v=round(x*(1<<(bits-1)));v=max(-(1<<(bits-1)),min((1<<(bits-1))-1,v))
            if bits==8 and wave:result.append(v+128)
            else:result+=int(v).to_bytes(bits//8,'big' if big else 'little',signed=True)
    return bytes(result)

def wav(samples,rate=48000,channels=1,bits=32,kind=3,ext=False,big=False,rf=False,metadata=False):
    endian='>' if big else '<';align=channels*bits//8
    fmt=struct.pack(endian+'HHIIHH',0xfffe if ext else kind,channels,rate,rate*align,align,bits)
    if ext:fmt+=struct.pack('<HHI',22,bits,0)+struct.pack('<IHH8s',kind,0,0x10,bytes.fromhex('800000aa00389b71'))
    elif metadata:fmt+=b'\0'*24
    data=pcm(samples,bits,kind,big)
    blocks=chunk(b'fmt ',fmt,big)+(chunk(b'bext',b'test metadata\0',big) if metadata else b'')
    blocks+=chunk(b'data',data,big)
    if rf:
        blocks=chunk(b'ds64',struct.pack('<QQQI',0,len(data),len(samples)//channels,0))+blocks
        blocks=blocks.replace(b'data'+struct.pack('<I',len(data)),b'data'+b'\xff'*4)
        result=b'RF64'+b'\xff'*4+b'WAVE'+blocks
        result=result[:20]+struct.pack('<Q',len(result)-8)+result[28:]
        return result
    return (b'RIFX' if big else b'RIFF')+struct.pack(endian+'I',len(blocks)+4)+b'WAVE'+blocks

def extended(rate):
    exponent=math.floor(math.log2(rate));mantissa=round(rate/2**exponent*2**63)
    return struct.pack('>HQ',exponent+16383,mantissa)

def aiff(samples,rate=48000,channels=1,bits=24,code=None):
    kind=3 if code in ('fl32','fl64') else 1
    comm=struct.pack('>hIh',channels,len(samples)//channels,bits)+extended(rate)
    if code:comm+=code.encode()+b'\0\0'
    data=pcm(samples,bits,kind,code!='sowt',False)
    blocks=chunk(b'COMM',comm,True)+chunk(b'SSND',struct.pack('>II',0,0)+data,True)
    return b'FORM'+struct.pack('>I',len(blocks)+4)+(b'AIFC' if code else b'AIFF')+blocks

def crc(data,width,poly):
    value=0
    for b in data:
        value^=b<<(width-8)
        for _ in range(8):value=((value<<1)^poly) if value&(1<<(width-1)) else value<<1
        value&=(1<<width)-1
    return value

def flac(samples):
    integers=pcm(samples,16,1)
    info=struct.pack('>HH',len(samples),len(samples))+b'\0'*6+((48000<<44)|(15<<36)|len(samples)).to_bytes(8,'big')+hashlib.md5(integers).digest()
    header=b'\xff\xf8\x70\x08\x00'+struct.pack('>H',len(samples)-1)
    header+=bytes([crc(header,8,0x07)])
    body=header+b'\x02'+pcm(samples,16,1,True)
    return b'fLaC'+b'\x80\x00\x00\x22'+info+body+struct.pack('>H',crc(body,16,0x8005))

def run(name,data=None,path=None,valid=True):
    file=path or OUT/(name+('.flac' if name=='flac' else '.wav'))
    if data is not None:file.write_bytes(data)
    before=hashlib.sha256(file.read_bytes()).hexdigest();dest=OUT/name
    proc=subprocess.run([str(EXE),str(file),str(dest)],capture_output=True,text=True,timeout=40)
    assert (dest.with_suffix('.json')).exists(),(name,proc.returncode,proc.stdout,proc.stderr)
    report=json.loads(dest.with_suffix('.json').read_text())
    assert (proc.returncode==0)==report['passed']==valid,(name,report)
    assert hashlib.sha256(file.read_bytes()).hexdigest()==before,name
    if not valid:return report,None
    raw=Path(str(dest)+'.f32').read_bytes();assert len(raw)==8192
    values=struct.unpack('<2048f',raw)[::-1];assert all(math.isfinite(v) for v in values)
    return report,values

def main():
    csc='C:/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe'
    args=[csc,'/nologo','/target:exe','/platform:x86','/r:System.Web.Extensions.dll','/out:'+str(EXE),str(ROOT/'IRImport.cs'),str(ROOT/'IRMediaFoundation.cs'),str(ROOT/'checks/ir-import/IRImportProbe.cs')]
    p=subprocess.run(args,capture_output=True,text=True);(OUT/'compile.log').write_text(p.stdout+p.stderr);assert p.returncode==0,p.stdout+p.stderr
    signal=[.5,-.25,.125]+[0]*61;cases=[]
    for bits,kind in [(8,1),(16,1),(24,1),(32,1),(32,3),(64,3)]:
        report,y=run(f'wave-{kind}-{bits}',wav(signal,bits=bits,kind=kind));assert list(y[:3])==signal[:3];cases.append(f'WAV {kind}/{bits}')
    for name,kw in [('extensible',dict(ext=True,bits=24,kind=1)),('metadata',dict(metadata=True,bits=24,kind=1)),('rifx',dict(big=True,bits=24,kind=1)),('rf64',dict(rf=True))]:
        report,y=run(name,wav(signal,**kw));assert list(y[:3])==signal[:3];cases.append(name)
    # A complete odd-sized terminal chunk may omit only its final alignment byte.
    # Padded and unpadded variants must decode to identical coefficients.
    odd_signal=signal+[0.0]
    for name,kw in [('pcm8',dict(bits=8,kind=1)),('pcm24',dict(bits=24,kind=1)),('rifx24',dict(bits=24,kind=1,big=True)),('ext24',dict(bits=24,kind=1,ext=True)),('rf64-24',dict(bits=24,kind=1,rf=True))]:
        padded=wav(odd_signal,**kw);unpadded=bytearray(padded[:-1])
        if kw.get('rf'):struct.pack_into('<Q',unpadded,20,len(unpadded)-8)
        else:struct.pack_into('>I' if kw.get('big') else '<I',unpadded,4,len(unpadded)-8)
        _,reference=run('padded-final-'+name,padded);report,actual=run('unpadded-final-'+name,bytes(unpadded))
        assert actual==reference and report['SourceFrames']==len(odd_signal),name
        cases.append('Complete unpadded terminal data '+name+' equals padded coefficients')
    padded=wav(signal)+chunk(b'LIST',b'INFO'+chunk(b'INAM',b'IR name')[:-1])
    padded=padded[:4]+struct.pack('<I',len(padded)-8)+padded[8:]
    unpadded=padded[:-1];unpadded=unpadded[:4]+struct.pack('<I',len(unpadded)-8)+unpadded[8:]
    _,reference=run('padded-final-list',padded);_,actual=run('unpadded-final-list',unpadded);assert actual==reference
    cases.append('Complete unpadded terminal LIST metadata preserves every coefficient')
    fmt=chunk(b'fmt ',struct.pack('<HHIIHH',3,1,48000,192000,4,32))
    bad_blocks=fmt+chunk(b'JUNK',b'odd')[:-1]+chunk(b'data',pcm(signal,32,3))
    missing_internal=b'RIFF'+struct.pack('<I',len(bad_blocks)+4)+b'WAVE'+bad_blocks
    truncated=wav(odd_signal,bits=24,kind=1)[:-2];truncated=truncated[:4]+struct.pack('<I',len(truncated)-8)+truncated[8:]
    for name,data in [('missing-internal-padding',missing_internal),('truncated-pcm24',truncated),('incorrect-riff-even',wav(signal)[:4]+struct.pack('<I',len(wav(signal))-7)+wav(signal)[8:])]:
        run('invalid-'+name,data,valid=False);cases.append('Reject '+name)
    for code,bits in [(None,24),('sowt',16),('fl32',32),('fl64',64)]:
        report,y=run('aiff-'+str(code),aiff(signal,code=code,bits=bits));assert list(y[:3])==signal[:3];cases.append('AIFF '+str(code))
    for name,right,phase in [('identical',signal,False),('opposite',[-v for v in signal],True),('right-only',signal,False)]:
        left=signal if name!='right-only' else [0]*len(signal)
        stereo=[v for pair in zip(left,right) for v in pair];report,y=run('stereo-'+name,wav(stereo,channels=2));assert report['PhaseSafeChannel']==phase and list(y[:3])==signal[:3];cases.append('Stereo '+name)
    for rate in (8000,22050,44100,88200,96000,192000,384000,768000):
        source=[.2*math.exp(-2*math.pi*1000*n/rate)*(1-math.exp(-2*math.pi*1000/rate)) for n in range(int(rate*.08))]
        report,y=run('rate-'+str(rate),wav(source,rate=rate));assert report['SourceRate']==rate and abs(sum(y)-.2)<.055;cases.append('Resample '+str(rate))
    delayed=[0.0]*9600+[.5]+[0.0]*14399;report,y=run('long-delayed',wav(delayed));assert report['TrimmedFrames']==9552 and max(range(len(y)),key=lambda i:abs(y[i]))==48 and y[-1]==0;cases.append('Long IR onset and bounded window')
    for name,data in [('nan',wav([float('nan')],kind=3)),('silent',wav([0.0]*32)),('bad-size',wav(signal)[:-1]),('bad-align',wav(signal).replace(struct.pack('<HHIIHH',3,1,48000,192000,4,32),struct.pack('<HHIIHH',3,1,48000,192000,3,32))),('unknown',b'not an audio format')]:
        run('invalid-'+name,data,valid=False);cases.append('Reject '+name)
    report,y=run('flac',flac(signal*16));assert list(y[:3])==signal[:3];cases.append('FLAC native Windows decode')
    rms=[]
    for hz in (2000,38000):
        x=[.01*math.sin(2*math.pi*hz*n/96000) for n in range(8192)];report,y=run('alias-'+str(hz),wav(x,rate=96000));rms.append(math.sqrt(sum(v*v for v in y[128:1900])/1772))
    alias_db=20*math.log10(rms[1]/rms[0]);assert alias_db < -55,alias_db;cases.append('96k anti-alias rejection '+str(alias_db)+' dB')
    example=Path(os.environ['CATALYST_LONG_IR_FIXTURE']) if os.environ.get('CATALYST_LONG_IR_FIXTURE') else None
    if example is not None and example.exists():
        report,y=run('private-long-ir',path=example);assert report['SourceFrames']==24000 and report['TailWindow'];cases.append('Private 24000-frame IR accepted, original preserved')
    regression=Path(os.environ['CATALYST_IR_REGRESSION_FIXTURE']) if os.environ.get('CATALYST_IR_REGRESSION_FIXTURE') else None
    if regression is not None and regression.exists():
        report,y=run('private-missing-list-pad',path=regression);assert report['SourceFrames']==2048 and report['SourceRate']==48000
        raw=regression.read_bytes();offset=12
        while raw[offset:offset+4]!=b'data':offset+=8+struct.unpack_from('<I',raw,offset+4)[0]+(struct.unpack_from('<I',raw,offset+4)[0]&1)
        original=struct.unpack_from('<2048f',raw,offset+8);assert tuple(y)==original
        expected=b''.join(raw[offset+8+4*i:offset+12+4*i] for i in range(2047,-1,-1));assert (OUT/'private-missing-list-pad.f32').read_bytes()==expected
        cases.append('Private OD-FB P20: missing final LIST pad accepted, original file and all 2048 float32 samples unchanged')
    result=dict(passed=True,hardware_accessed=False,fixture_cases=cases,downsampling_alias_db=alias_db,commercial_audio_in_public_distribution=False,converter_sha256=hashlib.sha256((ROOT/'IRImport.cs').read_bytes()).hexdigest(),decoder_sha256=hashlib.sha256((ROOT/'IRMediaFoundation.cs').read_bytes()).hexdigest())
    result['result_path']=str(OUT/'result.json');(OUT/'result.json').write_text(json.dumps(result,indent=2));(ROOT/'checks/ir-import/general-latest.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))

if __name__=='__main__':main()
