"""Create a model-free, IR-free template from the accepted automatic engine.
Offline only. Original firmware and source captures are never modified.
"""
import sys,json,hashlib,struct,math,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parent
sys.path[:0]=[str(ROOT.parent),str(ROOT.parent.parent/'catalyst-ir')]
from ctf_tool import parse,repack
from package_extension import decode_hex,record
from build import elf
import model

def main(template_only=False):
    source=ROOT.parent/'candidates/usb-compat-851fd8d6599a/Catalyst-NAM-5150-USB-CANDIDATO.ctf'
    original=source.read_bytes();assert hashlib.sha256(original).hexdigest()=='851fd8d6599ab4df1ea71271073d95e239d236564453359bfad6ddc96a84b53b'
    _,raw,regions=parse(original);images={r['kind']:raw[r['offset']:r['offset']+r['size']] for r in regions}
    segments,_=decode_hex(images[0]);base,data=segments[1];table=0x601dc000
    rows=[struct.unpack_from('<III',data,table-base+i*12) for i in range(10)]
    state=rows[7][0];weights=rows[8][0];runtime=rows[9][0]
    assert rows[7][1:]==(0x20050040,0x20062b40) and rows[8][1:]==(0x20062b40,0x2006487c)
    _,symbols=elf(ROOT.parent/'build-trial/trial.elf')
    silence=runtime+symbols['silence']['address']-rows[9][1]
    empty_ranges=[(state,76536),(weights,7484),(silence,288),(0x601b3500,4)]+[(0x601ad000+i*0x7000,0x6200) for i in range(3)]
    zeros={a+i:0 for a,n in empty_ranges for i in range(n)}
    high=0;output=[];written=0
    for line in images[0].splitlines(keepends=True):
        b=bytes.fromhex(line.strip()[1:].decode());kind=b[3];address=int.from_bytes(b[1:3],'big')
        if kind==4:high=int.from_bytes(b[4:-1],'big')<<16
        if kind==0:
            v=bytearray(b[4:-1]);changed=False
            for i in range(len(v)):
                a=high+address+i
                if a in zeros:v[i]=0;changed=True;written+=1
            if changed:line=record(address,0,bytes(v))
        output.append(line)
    assert written==len(zeros);images[0]=b''.join(output);template=repack(original,images)
    resources=ROOT/'resources';resources.mkdir(exist_ok=True);(resources/'nam-engine.ctf').write_bytes(template)
    layout=dict(schema='CatalystNAM.A2Lite.Layout.v1',engine_sha256=hashlib.sha256(template).hexdigest(),accepted_engine_sha256=hashlib.sha256(original).hexdigest(),state_flash=state,weights_flash=weights,silence_flash=silence,state_bytes=76536,weights_bytes=7484,silence_bytes=288,input_trim_db=-12,model_free=True,ir_free=True)
    (resources/'nam-layout.json').write_text(json.dumps(layout,indent=2))
    (resources/'NamLayout.cs').write_text('static class NamLayout {\n'+f' public const string TemplateHash="{layout["engine_sha256"]}";\n'+''.join(f' public const uint {k}={layout[v]}u;\n' for k,v in [('State','state_flash'),('Weights','weights_flash'),('Silence','silence_flash')])+'}\n')
    if template_only:
        print(json.dumps(layout,indent=2))
        return
    # Canonical test fixtures use the same scalar M7 reset as the original build.
    from verify import Arm,S
    fixtures=ROOT/'tests';fixtures.mkdir(exist_ok=True)
    paths=[ROOT.parents[3]/'Desktop/none']
    paths=[Path.home()/'Desktop/5150 Stealth 100w Red Preamp - jp_is_out_of_tune.nam',Path.home()/'Desktop/APP-5153-Ivory-Red-III.nam',ROOT.parent/'external/NeuralAmpModelerCore/example_models/A2.nam']
    info=[]
    for i,path in enumerate(paths):
        selected,w=model.load(path);packed=model.pack(w);arm=Arm(w);state_data=bytes(arm.u.mem_read(S,76536));g=model.geometry()
        silence_data=b''.join(state_data[96+4*o:96+4*o+12] for o in g['ring_offset']+[g['head_ring_offset']])
        for name,b in [('weights',packed),('state',state_data),('silence',silence_data)]: (fixtures/f'model{i}-{name}.bin').write_bytes(b)
        (fixtures/f'model{i}.nam').write_text(json.dumps(selected))
        signal=[];seed=0x5150
        for n in range(9216):
            if n<1024:signal.append(0.0);continue
            seed=(seed*1664525+1013904223)&0xffffffff;noise=((seed>>8)/16777216-.5)*.08;level=.06 if n<4096 else .15 if n<6656 else .30
            signal.append(level*(.50*math.sin(n*.028797932658)+.25*math.sin(n*.08325220532)+.15*math.sin(n*.3110176727)+noise)*10**(-12/20))
        inp=fixtures/f'model{i}-calibration-input.f32';inp.write_bytes(struct.pack('<9216f',*signal));out=fixtures/f'model{i}-official.f32'
        subprocess.run([str(ROOT.parent/'build/reference.exe'),str(fixtures/f'model{i}.nam'),str(inp),str(out)],check=True,capture_output=True)
        values=struct.unpack('<9216f',out.read_bytes());rms=math.sqrt(sum(x*x for x in values[1024:])/8192)
        info.append(dict(index=i,source=str(path),source_hash=hashlib.sha256(path.read_bytes()).hexdigest(),rms=rms,peak=max(map(abs,values))))
    (fixtures/'reference.json').write_text(json.dumps(info,indent=2))
    (resources/'NamLayout.cs').write_text('static class NamLayout {\n'+f' public const string TemplateHash="{layout["engine_sha256"]}";\n'+''.join(f' public const uint {k}={layout[v]}u;\n' for k,v in [('State','state_flash'),('Weights','weights_flash'),('Silence','silence_flash')])+'}\n')
    text=(ROOT/'NamModel.cs').read_text(encoding='utf-8');import re
    text=re.sub(r'public const double ReferenceRms=[^;]+;',f'public const double ReferenceRms={info[0]["rms"]:.17g};',text)
    (ROOT/'NamModel.cs').write_text(text,encoding='utf-8')
    print(json.dumps(dict(layout=layout,calibration=info),indent=2))
if __name__=='__main__':
    import argparse
    parser=argparse.ArgumentParser();parser.add_argument('--template-only',action='store_true')
    main(parser.parse_args().template_only)
