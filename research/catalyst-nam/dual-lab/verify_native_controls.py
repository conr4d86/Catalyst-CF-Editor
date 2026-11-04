"""OEM-table-driven control parity on the ARM adapter; no devices or timing proof."""
import hashlib,importlib.util,json,math,struct,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent
sys.path[:0]=[str(NAM.parent/'catalyst-editor/tools-recovery'),str(NAM)]
import unicorn,capstone
spec=importlib.util.spec_from_file_location('dual_control_fixtures',ROOT/'verify_lab.py')
lab=importlib.util.module_from_spec(spec);spec.loader.exec_module(lab)

def native_controls():
    p=NAM.parent/'catalyst-ir/analysis'
    db=json.loads((p/'asset-database.json').read_text())
    maps=json.loads((p/'runtime-maps.json').read_text())
    fw=(p/'image-0-segment-1-60001000.bin').read_bytes()
    def read(a,n):
        for m in maps:
            if m['destination']<=a<m['end']:
                a=m['source']+a-m['destination'];break
        return fw[a-0x60001000:a-0x60001000+n]
    result=[]
    for name in ('HD2_AmpImpossibleAC','A45_AmpImpossibleElmsleyNFB'):
        a=next(x for x in db['models'] if x['name']==name)
        f=[int(x,16) for x in a['fields']];controls={}
        for i in range(f[7]):
            row=struct.unpack('<8I',read(int(db['base'],16)+f[11]+32*i,32))
            controls[db['symbols'][row[2]]]=row[4]
        result.append(controls)
    return result

def run(elf_folder,output):
    lab.ROOT=Path(elf_folder).resolve().parent
    # Machine opens ROOT/build/combined.elf: own tree, no active build overwrite.
    import shutil
    tree=Path(output).resolve().parent/'native-control-fixture'
    (tree/'build').mkdir(parents=True,exist_ok=True)
    shutil.copyfile(Path(elf_folder)/'combined.elf',tree/'build/combined.elf')
    lab.ROOT=tree
    _,w=lab.model.load(NAM/'external/NeuralAmpModelerCore/example_models/A2.nam')
    maps=native_controls();rows=[]
    for mode in range(4):
        machines=[];outputs=[]
        for slot in range(2):
            m=lab.Machine([w,w]);m.slot(slot)
            values={'Drive':.69,'Boost':.72,'Bass':.43,'Mid':.53,'Treble':.57,'ChVol':.27,'Presence':.57,'BoostSwitch':1}
            # Exercise every raw OEM ID. Ignored advanced parameters must not
            # alias a supported EQ/gain control (especially Master=5 in NAM2).
            for name,pid in maps[slot].items():
                m.control(pid,values.get(name,.91),name in ('BoostSwitch','HumSwitch','FullPreSwitch'))
            m.request(lab.Config(mode,1,1,0,.72,.5,1));m.warm()
            r=m.get();machines.append(m)
            rows.append(dict(mode=mode,slot=slot+1,input_gain=r.input_gain,drive_scale=r.drive_scale,boost_scale=r.boost_scale,boost_enabled=r.boost_enabled,level=r.level,eq_index=list(r.eq_index)))
            signal=[.04*math.sin(i*.127)+.02*math.sin(i*.031) for i in range(256)]
            outputs.append([v for p in range(0,len(signal),32) for v in m.block(signal[p:p+32])[1]])
        a,b=rows[-2:]
        for key in ('input_gain','drive_scale','boost_scale','boost_enabled','level','eq_index'):
            assert a[key]==b[key],(mode,key,a[key],b[key])
        error=max(abs(a-b) for a,b in zip(*outputs));assert error<3e-5,error
        rows[-1]['same_capture_max_error']=error
    report=dict(passed=True,native_control_ids=maps,comparisons=rows,hardware_accessed=False,real_time_proven=False,
        elf_sha256=hashlib.sha256((Path(elf_folder)/'combined.elf').read_bytes()).hexdigest(),
        verification_source_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        OEM_sources_sha256={str(p.relative_to(NAM.parents[1])):hashlib.sha256(p.read_bytes()).hexdigest() for p in (NAM.parent/'catalyst-ir/analysis'/n for n in ('asset-database.json','runtime-maps.json','image-0-segment-1-60001000.bin'))})
    Path(output).write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))

if __name__=='__main__':run(sys.argv[1],sys.argv[2])
