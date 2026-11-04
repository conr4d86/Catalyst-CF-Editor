"""Overlay the lab ELF in an emulator, exercising original graph loaders.

No image/CTF is written. Boot hardware init, task synchronization and DWT are
explicit fixtures. MAX runs the native loader's FP64 conversions; the combined
audio module separately passes Cortex-M7 tests. No hardware proof is claimed.
"""
import contextlib,ctypes,hashlib,json,math,struct,sys,types
from pathlib import Path
ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent;EDITOR=NAM.parent/'catalyst-editor'
sys.path[:0]=[str(EDITOR/'tools/disasm'),str(EDITOR/'tools/emulation'),str(NAM)]
import capstone,unicorn
# The historical loader helpers import the assembler although this test only
# reads existing binaries. Explicitly prohibit assembly rather than silently
# falling back to an inaccessible old package or generating a firmware image.
assembly=types.ModuleType('keystone')
def forbid_assembly(*args,**kwargs):raise RuntimeError('Assembly/firmware generation is disabled in this test')
assembly.Ks=forbid_assembly
assembly.KS_ARCH_ARM=assembly.KS_MODE_THUMB=assembly.KS_MODE_LITTLE_ENDIAN=assembly.KS_MODE_V8=0
sys.modules['keystone']=assembly
from build import elf
from verify_trial import ready,image_dict,decode_hex,Clock,setparam
from verify_tcm_ir import bootstrap
from verify_physical_io import bind,block
# Import our fixture under an unambiguous name.
import importlib.util
spec=importlib.util.spec_from_file_location('dual_fixture',ROOT/'verify_lab.py');lab=importlib.util.module_from_spec(spec);spec.loader.exec_module(lab)

def main():
    engine=NAM/'app/resources/nam-engine.ctf';original=engine.read_bytes();assert hashlib.sha256(original).hexdigest()=='e80bba31906c0032b4b92f9d062c54879ea88fbabbf0a9d073dd1794d2a287a1'
    image=decode_hex(image_dict(original)[0])[0][1][1];segments,syms=elf(ROOT/'build/combined.elf');oldsyms=elf(NAM/'build-trial/trial.elf')[1]
    _,w=lab.model.load(NAM/'external/NeuralAmpModelerCore/example_models/A2.nam');w2=list(w);w2[-1]*=-.5;weights=[w,w2]
    fixture=lab.Machine(weights);checks=[];wrappers=[];results=[]
    with (ROOT/'build/native.log').open('w') as log,contextlib.redirect_stdout(log):
        boot=bootstrap(image)
        for seg in segments:boot.mem_write(seg['address'],seg['data'])
        # Update only the dispatch/setter thunk literals inside emulator RAM.
        region=bytearray(boot.mem_read(0x17e80,0x180))
        for name in ('trial_begin','trial_route','trial_after','trial_node_control'):
            old=(oldsyms[name]['address']|1).to_bytes(4,'little');new=(syms[name]['address']|1).to_bytes(4,'little');count=region.count(old)
            assert count==1,(name,count)
            region=region.replace(old,new);wrappers.append(dict(function=name,old=hex(int.from_bytes(old,'little')),new=hex(int.from_bytes(new,'little'))))
        boot.mem_write(0x17e80,bytes(region))
        r=fixture.get();r.bank_source[:]=[0x83008000,0x8300a000];boot.mem_write(lab.RUNTIME,bytes(r))
        for slot,symbol in ((1,82),(2,159)):
            m=ready(image,boot,symbol=symbol);Clock(m)
            for i,weights_blob in enumerate(weights):m.u.mem_write(r.bank_source[i],lab.model.pack(weights_blob)+fixture.silences[i])
            bind(m,0x20001470,0x20001470)
            def runtime():return lab.Combined.from_buffer_copy(bytes(m.u.mem_read(lab.RUNTIME,512)))
            before=runtime().ctrl_calls
            # These are the actual stock node setters, not a direct C fixture.
            for pid in (1,2,3,4,5,6,7):setparam(m,pid,.5)
            setparam(m,20,1,5)
            after=runtime();assert after.ctrl_calls==before+8 and after.boost_enabled==1,(slot,after.ctrl_calls,before,after.boost_enabled)
            cfg=lab.Config(3,1,0,0,.5,.5,1);m.u.mem_write(0x83000600,bytes(cfg));assert m.call(syms['cf_preamp_request']['address'],0x83000600)==0
            for _ in range(90):block(m,0x20001470,0x20001470,[0.]*32)
            state=runtime();assert state.mode==2 and state.fault==0 and state.active_slot==slot,(slot,state.mode,state.fault,state.active_slot)
            assert bytes(m.u.mem_read(lab.WEIGHTS,7484))==lab.model.pack(weights[slot-1])
            assert state.pedal.target.mode==3 and state.pedal.target.enabled==1
            setparam(m,20,0,5)
            for _ in range(10):block(m,0x20001470,0x20001470,[0.]*32)
            assert runtime().pedal.target.enabled==0 and runtime().pedal.mix==0
            signal=[.035*math.sin(i*.117) for i in range(256)]
            audio=[]
            for p in range(0,256,32):audio.extend(block(m,0x20001470,0x20001470,signal[p:p+32]))
            assert all(math.isfinite(x) and abs(x)<=1 for x in audio)
            results.append(dict(slot=slot,native_symbol=symbol,native_control_updates=8,bank_loaded=True,preamp_toggle=True,finite_audio=True,graph_depth=runtime().max_depth))
    assert engine.read_bytes()==original
    result=dict(passed=True,hardware_accessed=False,firmware_created=False,assembler_disabled=True,engine_unchanged=True,engine_sha256=hashlib.sha256(original).hexdigest(),elf_sha256=hashlib.sha256((ROOT/'build/combined.elf').read_bytes()).hexdigest(),native_checks=results,thunk_overlay=wrappers,
      limitations=['Emulator RAM overlay, not a bootable new firmware','Hardware init and synchronization stubbed','No real cycle/IRQ/USB measurement','No physical storage receiver or preset configuration persistence'])
    (ROOT/'build/native-tests.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
if __name__=='__main__':
    try:main()
    except BaseException:
        import traceback
        (ROOT/'build/native-error.txt').write_text(traceback.format_exc());raise
