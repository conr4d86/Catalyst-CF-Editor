"""Execute the real OEM graph loaders/dispatchers with the all-model ELF.

Isolated emulator evidence, not a physical sound/timing/USB measurement.
"""
import contextlib, ctypes, hashlib, importlib.util, json, math, struct, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent
sys.path.insert(0,str(NAM.parent/'catalyst-editor/tools-recovery'))
import unicorn,capstone
spec=importlib.util.spec_from_file_location('all_native_fixture',NAM/'dual-lab/verify_native.py')
native=importlib.util.module_from_spec(spec);spec.loader.exec_module(native)
lab=native.lab
from unicorn import UC_HOOK_CODE,UC_HOOK_MEM_INVALID
from unicorn.arm_const import UC_ARM_REG_R1,UC_ARM_REG_R2,UC_ARM_REG_R3
original_machine=native.ready.__globals__['machine']
def traced_machine(*args,**kwargs):
    m=original_machine(*args,**kwargs);put=m.put
    def checked_put(address,*values):
        assert address>=0x20000,('fixture attempted to write instruction memory',hex(address),values)
        return put(address,*values)
    m.put=checked_put
    return m
native.ready.__globals__['machine']=traced_machine

def main():
    build=json.loads((ROOT/'build/build.json').read_text());assert build['static_code_and_tables_fit']
    original=(NAM/'app/resources/nam-engine.ctf').read_bytes()
    image=native.decode_hex(native.image_dict(original)[0])[0][1][1]
    segments,symbols=native.elf(ROOT/'build/combined.elf');old=native.elf(NAM/'build-trial/trial.elf')[1]
    rows=json.loads((ROOT/'build/OEM-controls.json').read_text());results=[]
    with (ROOT/'build/native.log').open('w') as log,contextlib.redirect_stdout(log):
        boot=native.bootstrap(image)
        # ELF file-header PT_LOAD is not part of the firmware reset copy.
        # Loading it at 0x10000 would corrupt an unrelated OEM DSP function.
        for segment in segments:
            if segment['flags']&1 or segment['address']==0x20064b00:boot.mem_write(segment['address'],segment['data'])
        thunk=bytearray(boot.mem_read(0x17e80,0x180))
        for name in ('trial_begin','trial_route','trial_after','trial_node_control'):
            before=(old[name]['address']|1).to_bytes(4,'little');after=(symbols[name]['address']|1).to_bytes(4,'little');assert thunk.count(before)==1;thunk=thunk.replace(before,after)
        boot.mem_write(0x17e80,bytes(thunk))
        r=lab.Combined();r.magic=0x324d414e;r.min_sp=r.min_period=0xffffffff
        r.drive_scale=r.boost_scale=1;r.input_gain=r.input_current=10**(-12/20);r.level=r.level_current=1;r.stock_fade=1
        for i in range(4):r.eq_index[i]=10
        # Empty NAM sources intentionally exercise the original Chime models,
        # proving that custom pedals do not depend on a NAM bank being filled.
        boot.mem_write(lab.RUNTIME,bytes(r))
        for model in rows:
            m=native.ready(image,boot,symbol=model['symbol']);native.Clock(m)
            def invalid_memory(u,access,address,size,value,user):
                from unicorn.arm_const import UC_ARM_REG_PC,UC_ARM_REG_R0,UC_ARM_REG_R1,UC_ARM_REG_R2,UC_ARM_REG_R3,UC_ARM_REG_R4,UC_ARM_REG_R5
                pc=u.reg_read(UC_ARM_REG_PC);md=capstone.Cs(capstone.CS_ARCH_ARM,capstone.CS_MODE_THUMB)
                detail=dict(model=model['name'],address=hex(address),pc=hex(pc),registers=[hex(u.reg_read(x)) for x in (UC_ARM_REG_R0,UC_ARM_REG_R1,UC_ARM_REG_R2,UC_ARM_REG_R3,UC_ARM_REG_R4,UC_ARM_REG_R5)],code=[x.mnemonic+' '+x.op_str for x in md.disasm(bytes(u.mem_read(pc-16,32)),pc-16)])
                (ROOT/'build/invalid-memory.json').write_text(json.dumps(detail,indent=2));return False
            m.u.hook_add(UC_HOOK_MEM_INVALID,invalid_memory)
            # Newly allocated primitive gains are muted in this control-free
            # fixture. Use only fresh loader events, never stale Main records.
            for block in {e['node']:e for e in m.block_events}.values():
                if block['name'] in ('L6SPB_GainStage','L6SPB_LinearGain','L6SPB_LinearGainAdjustableGain'):
                    instance=int(block['instance'],16);m.put(m.get(instance+4),0x3f800000);m.put(m.get(instance+8),0x3f800000)
            # The full stock graphs contain many INTERNAL copy operations.
            # Rebind only the external input; retain every internal destination.
            source=destination=0x200012f0
            m.put(m.get(m.amp_graph+8)+12,source)
            p=model['params']
            for name in ('Drive','Bass','Mid','Treble','Presence','Master'):
                if name in p:native.setparam(m,p[name],.5)
            native.setparam(m,p['Boost'],.3)
            native.setparam(m,p['ChVol'],.4);native.setparam(m,p['BoostSwitch'],1,5)
            table=m.get(m.amp_graph+8);entry_original=m.get(table+12)
            # Exercise the unchanged native route with CF disabled first.
            m.put(lab.RUNTIME,0)
            for _ in range(2):native.block(m,source,destination,[0.]*32)
            m.put(lab.RUNTIME,0x324d414e)
            (ROOT/'build/current-graph.json').write_text(json.dumps(dict(model=model['name'],graph=hex(m.amp_graph),entries=[[hex(m.get(table+i*20+j*4)) for j in range(5)] for i in range(m.get(m.amp_graph+12))]),indent=2))
            setter=[]
            def original_setter(u,address,size,user):
                if address==0x17f80 and m.get(u.reg_read(UC_ARM_REG_R1)+20)==m.amp_graph:setter.append((u.reg_read(UC_ARM_REG_R2),m.get(u.reg_read(UC_ARM_REG_R3))))
            m.u.hook_add(UC_HOOK_CODE,original_setter,begin=0x17f80,end=0x17f81)
            values=[.04*math.sin(i*.127)+.01*math.sin(i*.031) for i in range(256)]
            output_modes=[]
            for mode in (0,1,2,3,0):
                config=lab.Config(mode,1,1,0,.3,.5,1)
                m.u.mem_write(0x83000600,bytes(config));assert m.call(symbols['cf_preamp_request']['address'],0x83000600)==0
                for _ in range(12):native.block(m,source,destination,[0.]*32)
                audio=[]
                for off in range(0,len(values),32):
                    audio.extend(native.block(m,source,destination,values[off:off+32]))
                    assert m.get(table+12)==entry_original,'temporary graph input was not restored'
                state=lab.Combined.from_buffer_copy(bytes(m.u.mem_read(lab.RUNTIME,512)))
                assert state.pedal.target.mode==mode and state.pedal.fault==0
                assert state.nam_calls==0 and state.depth==0,'stock model was replaced by NAM or left a nested owner'
                assert all(math.isfinite(x) for x in audio) and max(abs(x) for x in audio)>1e-8,(model['name'],mode,min(audio),max(audio),list(audio[:8]))
                output_modes.append(audio)
                assert setter[-1]==(p['BoostSwitch'],int(mode==0)),(model['name'],mode,setter[-1])
            assert max(abs(a-b) for a,b in zip(output_modes[2],output_modes[3]))>1e-7,'808/SD1 produced identical native-model audio'
            # Level must also work with the native Boost selected. It scales
            # only the input; no second transparent Boost is mixed in.
            config=lab.Config(0,1,0,0,.3,.5,.5);m.u.mem_write(0x83000600,bytes(config));assert m.call(symbols['cf_preamp_request']['address'],0x83000600)==0
            for _ in range(40):native.block(m,source,destination,[0.]*32)
            native.block(m,source,destination,values[:32])
            stock_address=symbols['stock']['address'];level=struct.unpack('<f',bytes(m.u.mem_read(stock_address+28,4)))[0]
            stock_input=struct.unpack('<32f',bytes(m.u.mem_read(stock_address+32,128)))
            assert abs(level-.5)<.001 and max(abs(a-b*.5) for a,b in zip(stock_input,values[:32]))<1e-5
            assert m.get(table+12)==entry_original
            results.append(dict(model=model['name'],native_symbol=model['symbol'],modes=[1,2,3,0],finite_nonzero_audio=True,original_boost_bypassed_and_restored=True,stock_Boost_Level_scales_input_without_double_boost=True,temporary_input_restored=True,NAM_calls=0))
            print('ALL-MODEL PASS',model['name'],flush=True,file=sys.__stdout__)
    report=dict(passed=True,hardware_accessed=False,USB_opened=False,physical_audio_validated=False,real_time_validated=False,firmware_created=False,empty_NAM_banks_supported=True,models=results,elf_sha256=hashlib.sha256((ROOT/'build/combined.elf').read_bytes()).hexdigest(),verification_source_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest())
    (ROOT/'build/native-tests.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))

if __name__=='__main__':
    try:main()
    except BaseException:
        import traceback
        (ROOT/'build/native-error.txt').write_text(traceback.format_exc());raise
