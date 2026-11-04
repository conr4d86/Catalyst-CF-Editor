"""Offline app tests. No SDK initialization, MIDI open, audio endpoint or USB writes."""
from pathlib import Path
import sys,json,subprocess,struct,math,copy,hashlib,time,wave
ROOT=Path(__file__).resolve().parent
sys.path[:0]=[str(ROOT.parent),str(ROOT.parent.parent/'catalyst-ir')]
from ctf_tool import parse
from package_extension import decode_hex
import model
EXE=ROOT.parents[2]/'CatalystIR/V3.0-NAM-experimental/CatalystIR.exe'
BASELINE=ROOT.parents[2]/'CatalystNAM/Prueba-5150-1f642505a2d3/Catalyst-NAM-A2Lite-5150-TRIAL.ctf'
RUN=ROOT/'checks'/time.strftime('run-%Y%m%d-%H%M%S')

def run(args,ok=True):
    p=subprocess.run([str(EXE),*map(str,args)],capture_output=True,text=True)
    assert (p.returncode==0)==ok,(args,p.returncode,p.stdout,p.stderr)

def read(ctf):
    _,raw,regions=parse(Path(ctf).read_bytes());r=next(r for r in regions if r['kind']==0);segs,_=decode_hex(raw[r['offset']:r['offset']+r['size']]);return raw,segs[1][1]

def main():
    RUN.mkdir(parents=True);passed=[]
    for i in range(3):
        out=RUN/f'model{i}';run(['--nam-inspect',ROOT/f'tests/model{i}.nam',out]);r=json.loads((out/'report.json').read_text())
        for name in ['weights','state','silence']:
            got=(out/('packed.bin' if name=='weights' else name+'.bin')).read_bytes();expected=(ROOT/f'tests/model{i}-{name}.bin').read_bytes();assert got==expected,(i,name)
        expected=struct.unpack('<9216f',(ROOT/f'tests/model{i}-official.f32').read_bytes());got=struct.unpack('<9216f',(out/'output.f32').read_bytes());error=max(abs(a-b) for a,b in zip(got,expected));assert error<2e-5,error
        passed.append(dict(test=f'model{i}_import_reset_reference',max_error=error,automatic_db=r['automatic_db']))
    selected=json.loads((ROOT/'tests/model0.nam').read_text());layer=lambda d:d['config']['layers'][0]
    mutations={
      'wrong_rate':lambda d:d.update(sample_rate=44100),
      'missing_rate':lambda d:d.pop('sample_rate'),
      'wrong_weight_count':lambda d:d['weights'].pop(),
      'float_overflow':lambda d:d['weights'].__setitem__(0,1e100),
      'nonfinite':lambda d:d['weights'].__setitem__(0,float('nan')),
      'boolean_weight':lambda d:d['weights'].__setitem__(0,True),
      'wrong_channels':lambda d:layer(d).update(channels=8),
      'gating':lambda d:layer(d).update(gating_mode=['gated']*23),
      'wrong_geometry':lambda d:layer(d)['dilations'].__setitem__(0,2),
      'wrong_activation':lambda d:layer(d)['activation'][0].update(type='Tanh'),
      'conditioning':lambda d:d['config'].update(condition_dsp={'architecture':'Linear'}),
      'film':lambda d:layer(d).update(conv_pre_film={'active':True}),
      'bad_head':lambda d:layer(d)['head'].update(kernel_size=15),
      'head_scale_mismatch':lambda d:d['config'].update(head_scale=100),
      'multiple_layers':lambda d:d['config']['layers'].append(copy.deepcopy(layer(d))),
      'unknown_feature':lambda d:layer(d).update(attention={'active':True}),
    }
    for label,fn in mutations.items():
        d=copy.deepcopy(selected);fn(d);p=RUN/(label+'.nam');p.write_text(json.dumps(d));run(['--nam-inspect',p,RUN/(label+'-out')],False)
    duplicate=RUN/'duplicate.nam';duplicate.write_text('{"architecture":"WaveNet",'+json.dumps(selected)[1:]);run(['--nam-inspect',duplicate,RUN/'duplicate-out'],False)
    passed.append(dict(test='unsupported_or_ambiguous_capture_rejections',count=len(mutations)+1))
    # Input calibration scales every conditioning path, not output loudness.
    out=RUN/'input-minus18';run(['--nam-inspect',ROOT/'tests/model0.nam',out,'-18'])
    original,w=model.load(ROOT/'tests/model0.nam');factor=10**(-6/20);packed=list(struct.unpack('<1871f',model.pack(w)))
    expected=list(packed);off=3
    for i in range(3):expected[i]=struct.unpack('<f',struct.pack('<f',expected[i]*factor))[0]
    for k in model.KERNELS:
        for i in range(3):expected[off+3+i]=struct.unpack('<f',struct.pack('<f',expected[off+3+i]*factor))[0]
        off+=18+9*k
    assert (out/'packed.bin').read_bytes()==struct.pack('<1871f',*expected)
    signal=struct.unpack('<9216f',(out/'input.f32').read_bytes());inp=RUN/'input18.f32';inp.write_bytes(struct.pack('<9216f',*[x*factor for x in signal]));ref=RUN/'reference18.f32'
    subprocess.run([str(ROOT.parent/'build/reference.exe'),str(ROOT/'tests/model0.nam'),str(inp),str(ref)],check=True,capture_output=True)
    got=struct.unpack('<9216f',(out/'output.f32').read_bytes());expected=struct.unpack('<9216f',ref.read_bytes());error=max(abs(a-b) for a,b in zip(got,expected));assert error<2e-5,error
    passed.append(dict(test='minus18_calibrates_input_all_layers',official_max_error=error))
    parent=BASELINE
    candidate=RUN/'roundtrip.ctf';run(['--nam-candidate',ROOT/'tests/model0.nam','-',candidate,'0',parent]);raw,data=read(candidate);original_raw,original_data=read(parent);assert raw==original_raw
    passed.append(dict(test='reproduces_accepted_engine_and_three_IR_byte_for_byte',raw_sha256=hashlib.sha256(raw).hexdigest()))
    run(['--nam-ui-state-check',RUN/'ui-NAM-state',ROOT/'tests/model0.nam',candidate,RUN/'NAM-ui-state.json']);assert json.loads((RUN/'NAM-ui-state.json').read_text())['passed'];passed.append(dict(test='NAM_profile_input_output_and_pending_journal_reopen',passed=True))
    candidate=RUN/'nam-no-ir.ctf';run(['--nam-candidate',ROOT/'tests/model1.nam','-',candidate,'auto']);_,data=read(candidate);layout=json.loads((ROOT/'resources/nam-layout.json').read_text());base=0x60001000
    assert struct.unpack_from('<I',data,0x601b3500-base)[0]==0
    for i in range(3):assert set(data[0x601ad000+i*0x7000-base:0x601ad000+i*0x7000-base+0x6200])=={0}
    passed.append(dict(test='NAM_only_restores_factory_IR_branches',mask=0))
    fixtures=RUN/'wavs';fixtures.mkdir()
    for i in range(3):
        with wave.open(str(fixtures/f'IR{i+1}.wav'),'wb') as wav:
            wav.setparams((1,2,48000,0,'NONE','not compressed'));wav.writeframes(struct.pack('<2048h',*[int(16000*math.exp(-n/50)*math.cos(n*(i+1)*.2)) for n in range(2048)]))
    run(['--ui-slot-check',RUN/'ui-regression',fixtures,RUN/'ui-regression.json']);assert json.loads((RUN/'ui-regression.json').read_text())['passed']
    passed.append(dict(test='existing_IR_ui_regression_independent_slots_reopen_reinstall',passed=True))
    for state in ['nam','ir','disconnected']:
        out=RUN/('preview-'+state);run(['--nam-ui-preview',out,ROOT/'tests/model0.nam',RUN/(state+'.png'),RUN/(state+'.json'),state]);r=json.loads((RUN/(state+'.json')).read_text());assert r['passed'];assert r['workspace_enabled']==(state!='disconnected');passed.append(dict(test='layout_'+state,passed=True))
    summary=dict(passed=True,hardware_accessed=False,firmware_installed=False,executable_sha256=hashlib.sha256(EXE.read_bytes()).hexdigest(),tests=passed)
    (RUN/'RESULTS.json').write_text(json.dumps(summary,indent=2));(ROOT/'checks/LATEST.json').write_text(json.dumps(dict(folder=str(RUN),**summary),indent=2));print(json.dumps(summary,indent=2))
if __name__=='__main__':
    import argparse
    parser=argparse.ArgumentParser();parser.add_argument('--exe',type=Path);parser.add_argument('--engine',type=Path)
    args=parser.parse_args()
    if args.exe:EXE=args.exe.resolve()
    if args.engine:BASELINE=args.engine.resolve()
    main()
