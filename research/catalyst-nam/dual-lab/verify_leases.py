"""Actual ARM acquisition receipts and nested-boundary tests, no devices."""
import hashlib,importlib.util,json,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent
sys.path.insert(0,str(NAM.parent/'catalyst-editor/tools-recovery'))
import unicorn,capstone
spec=importlib.util.spec_from_file_location('dual_fixtures',ROOT/'verify_lab.py');lab=importlib.util.module_from_spec(spec);spec.loader.exec_module(lab)
def main():
    # Reuse exactly the original numeric/native-ABI checks on the new ELF.
    lab.ROOT=ROOT/'lease-fixture';lab.ROOT.mkdir(exist_ok=True)
    # Machine reads build/combined.elf via ROOT. Use a file-only dedicated tree.
    import shutil
    b=lab.ROOT/'build';b.mkdir(exist_ok=True)
    for name in ('combined.elf','build.json'):shutil.copyfile(ROOT/'lease-build'/name,b/name)
    lab.main()
    _,w=lab.model.load(NAM/'external/NeuralAmpModelerCore/example_models/A2.nam')
    second=list(w);second[-1]*=-.5;m=lab.Machine([w,second]);r=m.get();r.magic=0;m.put(r)
    before=bytes(m.u.mem_read(lab.RUNTIME,512));assert m.request(lab.Config(2,1,0,0,.5,.5,1))==0xffffffff and bytes(m.u.mem_read(lab.RUNTIME,512))==before
    r.magic=0x324d414e;m.put(r);m.warm()
    discontinuity=lab.Machine([w,second]);discontinuity.warm();discontinuity.step=250000
    for _ in range(6):discontinuity.block([0.]*32,False)
    assert discontinuity.get().fault==9 and not discontinuity.get().want
    discontinuity.call('cf_audio_discontinuity')
    assert discontinuity.get().fault==9 and not discontinuity.get().auto_entry and not discontinuity.get().last_target_cycle and not discontinuity.get().late_streak
    discontinuity.step=70000;discontinuity.warm();assert not discontinuity.get().fault
    discontinuity.w32(0x20064880,0);discontinuity.block([.1]*32);damaged=discontinuity.get()
    assert damaged.fault==6 and damaged.mode==3
    discontinuity.call('cf_audio_discontinuity');assert discontinuity.get().fault==6 and discontinuity.get().mode==3 and discontinuity.get().auto_entry==damaged.auto_entry
    old=m.get().bank_source[0];target=0x83007000
    m.u.mem_write(target,lab.model.pack(second)+m.silences[1])
    assert m.call('cf_nam_bank_status',1,old)==0
    assert m.call('cf_nam_bank_request',1,target,7772)==0
    assert m.call('cf_nam_bank_status',1,target)==6
    # An inner node boundary cannot retire a source in use by the outer node.
    r=m.get();r.depth=1;m.put(r);m.call('trial_begin',lab.ARENA,lab.GRAPH)
    assert m.get().depth==2 and m.call('cf_nam_bank_status',1,target)==6
    m.call('trial_after',lab.ARENA,lab.GRAPH,0);m.call('trial_after',lab.ARENA,lab.GRAPH,0)
    assert m.get().depth==0 and m.call('cf_nam_bank_status',1,target)==6
    m.call('trial_begin',lab.ARENA,lab.GRAPH)
    assert m.call('cf_nam_bank_status',1,target)==0 and m.call('cf_nam_bank_status',1,old)==3
    assert m.get().mode==0 and not m.get().reset_done,'Receipt must not be confused with audible-ready'
    m.call('trial_after',lab.ARENA,lab.GRAPH,1)
    m.u.mem_write(old,b'\xff'*7772);m.weights=[second,second];m.warm()
    report=json.loads((b/'tests.json').read_text());report.update(acquisition_receipt=True,nested_boundary_does_not_retire=True,retired_source_poisoned=True,receipt_is_not_audible_ready=True,intentional_ir_discontinuity_rearms_only_cadence_fallback=True,memory_fault_is_not_cleared=True,
        runtime_bytes=m.call('trial_runtime_bytes'),physical_owner_preemption_proven=False,
        verification_source_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest())
    controls_spec=importlib.util.spec_from_file_location('native_control_regression',ROOT/'verify_native_controls.py');controls=importlib.util.module_from_spec(controls_spec);controls_spec.loader.exec_module(controls)
    controls.run(ROOT/'lease-build',ROOT/'lease-build/native-controls.json')
    report['OEM_control_mapping_and_identical_capture_parity']=True
    (ROOT/'lease-build/lease-tests.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':main()
