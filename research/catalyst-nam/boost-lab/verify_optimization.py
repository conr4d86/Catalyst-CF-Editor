"""Compare the optimized ARM pedal to the previous ELF, offline only."""
import importlib.util,json,math,random,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent
sys.path[:0]=[str(NAM.parent/'catalyst-editor/tools-recovery'),str(NAM)]
import unicorn,capstone
spec=importlib.util.spec_from_file_location('pedal_optimization_fixture',ROOT/'verify_lab.py')
lab=importlib.util.module_from_spec(spec);spec.loader.exec_module(lab)

def main(before,after,output):
    def machine(path):
        segments,symbols=lab.elf(Path(path))
        return lab.Machine([s for s in segments if s['flags']&1],symbols)
    randomizer=random.Random(841)
    signal=[.2*math.sin(n*.127)+.07*randomizer.uniform(-1,1) for n in range(2048)]
    comparisons=[];profiles=[]
    for mode in range(4):
        for drive,tone,level in ((0,0,.5),(.72,.5,1),(1,1,2)):
            a,b=machine(before),machine(after)
            for m in (a,b):assert m.configure(mode,drive=drive,tone=tone,level=level,boost=1,bright=mode==1)==0
            x,y=a.process(signal),b.process(signal)
            error=max(abs(v-w) for v,w in zip(x,y));assert error<1e-6,error
            comparisons.append(dict(mode=mode,drive=drive,tone=tone,level=level,max_error=error))
            # Include reset-free steady-state processing, not an idle bypass.
            if drive==.72:
                counts=[]
                for m in (a,b):
                    count=[0]
                    def tick(*args):count[0]+=1
                    m.u.ctl_flush_tb();hook=m.u.hook_add(unicorn.UC_HOOK_CODE,tick)
                    m.process(signal[:32]);m.u.hook_del(hook);counts.append(count[0])
                profiles.append(dict(mode=mode,previous_instructions=counts[0],optimized_instructions=counts[1],reduction=1-counts[1]/counts[0]))
        # OFF and mixed chunks remain exactly transparent after transition.
        b.configure(mode,enabled=0);b.process([0.]*256)
        off=b.process(signal,chunks=(1,7,32,19),inplace=True);ref=lab.floats(lab.packed(signal));assert tuple(off)==ref,(mode,max(abs(x-y) for x,y in zip(off,ref)),[(i,x,y) for i,(x,y) in enumerate(zip(off,ref)) if x!=y][:3])
    assert all(p['optimized_instructions']<p['previous_instructions'] for p in profiles if p['mode'])
    report=dict(passed=True,comparisons=comparisons,instruction_profiles=profiles,hardware_accessed=False,cycles_measured=False)
    Path(output).write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':main(*sys.argv[1:])
