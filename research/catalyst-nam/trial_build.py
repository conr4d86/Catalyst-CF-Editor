"""Compile the private diagnostic adapter. No USB, updater or installation."""
from pathlib import Path
import ctypes, hashlib, json, math, struct
import build as base
import model
import target
from verify import Arm,S

ROOT=base.ROOT;OUT=ROOT/'build-trial'
RUNTIME=0x20064900

class Runtime(ctypes.LittleEndianStructure):
    _fields_=[(n,ctypes.c_uint32) for n in
        'magic want mode fault gate roots nam_calls seen reset_ring reset_col reset_done max_nam max_root last_nam core_hz min_sp'.split()]+[
        (n,ctypes.c_float) for n in 'input_gain level input_current level_current nam_fade stock_fade'.split()]+[
        ('eq_index',ctypes.c_uint32*4),('eq_z',(ctypes.c_float*2)*4),
        ('root_start',ctypes.c_uint32),('dwt_on',ctypes.c_uint32),('gate_tick',ctypes.c_uint32)]+[
        (n,ctypes.c_uint32) for n in 'ctrl_seen ctrl_calls last_param last_raw arena_bytes target_frames route_reject io_source io_destination depth max_depth outer_graph outer_def0 outer_def1 outer_def2 outer_def3 leave_target last_target_cycle min_period max_period late_periods late_streak stock_target_calls stock_during_nam'.split()]+[
        ('input_peak',ctypes.c_float),('output_peak',ctypes.c_float),('clipped_samples',ctypes.c_uint32)]+[
        (n,ctypes.c_uint32) for n in 'diagnostic_mute diagnostic_gate diagnostic_tick'.split()]

class AutoRuntime(Runtime):
    _fields_=[('drive_scale',ctypes.c_float),('boost_scale',ctypes.c_float),('auto_entry',ctypes.c_uint32),('boost_enabled',ctypes.c_uint32)]

def runtime_blob(auto_boot=None):
    if auto_boot is None:
        path=OUT/'build.json';auto_boot=json.loads(path.read_text()).get('auto_boot',False) if path.exists() else False
    r=AutoRuntime() if auto_boot else Runtime();r.magic=0x324d414e;r.min_sp=0xffffffff;r.min_period=0xffffffff
    r.input_gain=r.level=r.input_current=r.level_current=r.stock_fade=1
    for i in range(4):r.eq_index[i]=10
    if auto_boot:
        r.drive_scale=r.boost_scale=1;r.input_gain=r.input_current=10**(-12/20)
        r.level=r.level_current=0
    return bytes(r)

def coeff(kind,hz,db):
    # RBJ biquad, 48kHz. Shelves S=1, peaking Q=.7. Return normalized
    # transposed-DF2 order b0,b1,b2,-a1,-a2.
    A=10**(db/40);w=2*math.pi*hz/48000;c=math.cos(w);s=math.sin(w)
    if kind=='peak':
        a=s/(2*.7);b=[1+a*A,-2*c,1-a*A];v=[1+a/A,-2*c,1-a/A]
    else:
        a=s/math.sqrt(2);z=2*math.sqrt(A)*a
        if kind=='low':
            b=[A*((A+1)-(A-1)*c+z),2*A*((A-1)-(A+1)*c),A*((A+1)-(A-1)*c-z)]
            v=[(A+1)+(A-1)*c+z,-2*((A-1)+(A+1)*c),(A+1)+(A-1)*c-z]
        else:
            b=[A*((A+1)+(A-1)*c+z),-2*A*((A-1)+(A+1)*c),A*((A+1)+(A-1)*c-z)]
            v=[(A+1)-(A-1)*c+z,2*((A-1)-(A+1)*c),(A+1)-(A-1)*c-z]
    return [x/v[0] for x in b]+[-v[1]/v[0],-v[2]/v[0]]

def lit(value):
    f=struct.unpack('<f',struct.pack('<f',value))[0]
    return f'{f:.9e}f'

def build(source,auto_boot=False):
    OUT.mkdir(exist_ok=True)
    target_header='#define TARGET_MODEL_INDEX '+str(target.MODEL_INDEX)+'u\n'
    target_header+=''.join('#define TARGET_SIG_'+str(i)+' '+hex(v)+'u\n' for i,v in enumerate(target.SIGNATURE))
    target_header+=''.join('#define TARGET_'+k.upper()+' '+str(v)+'u\n' for k,v in target.CONTROL_IDS.items())
    family=json.loads((ROOT/'evidence/factory-amp-signatures.json').read_text())
    assert family['parent_sha256']=='439aa011588a170219eca2993bcdc1009930d7df891fb5e5498dc26341e4a810'
    assert len(family['models'])==12 and not family['hardware_accessed']
    target_header+='static const unsigned factory_amp_signatures[12][6] = {'+','.join('{'+','.join(str(v)+'u' for v in row['signature'])+'}' for row in family['models'])+'};\n'
    (OUT/'trial_target.h').write_text(target_header)
    _,weights=model.load(source)
    warm=Arm(weights);state=bytes(warm.u.mem_read(S,model.geometry()['state_bytes']))
    g=model.geometry()
    silence=[]
    for off in g['ring_offset']+[g['head_ring_offset']]:
        silence.append(struct.unpack_from('<3f',state,24*4+off*4))
    attribute=' __attribute__((section(".nam_tables")))'
    header='static const float drive_table[257]'+attribute+' = {'+','.join(lit(10**((i/256-.5)*24/20)) for i in range(257))+'};\n'
    header+='static const float silence[24][3]'+attribute+' = {'+','.join('{'+','.join(map(lit,x))+'}' for x in silence)+'};\n'
    header+='static const float eq_table[4][21][5]'+attribute+' = {'
    for kind,hz in [('low',100),('peak',650),('high',2500),('high',5500)]:
        header+='{'+','.join('{'+','.join(map(lit,coeff(kind,hz,(i-10)*1.2)))+'}' for i in range(21))+'},'
    header+='};\n'
    (OUT/'trial_tables.h').write_text(header)
    # Keep the validated standalone build untouched.
    previous=base.OUT;base.OUT=OUT
    try:
        selected=ROOT/'build-fast/selected-block-fma/kernel-embedded.c'
        selection=json.loads((selected.parent/'selection.json').read_text())
        assert hashlib.sha256(selected.read_bytes()).hexdigest()==selection['source_sha256']
        assert selection['status']=='PASS_M7_BLOCK_FMA_STATIC_SCRATCH_OFFLINE'
        objects=[]
        for name in ['kernel','trial']:
            obj=OUT/(name+'.o');objects.append(obj)
            base.run([base.ZIG,'cc','-target','thumb-freestanding-eabihf','-mcpu=cortex_m7+fp_armv8d16sp',
                '-mfpu=fpv5-sp-d16','-mfloat-abi=hard','-O3' if name=='kernel' else '-O2',
                '-ffp-contract=fast' if name=='kernel' else '-ffp-contract=off','-ffreestanding',
                '-fno-builtin','-fno-stack-protector','-I',ROOT/'build','-I',OUT,'-c',selected if name=='kernel' else ROOT/('autoboot.c' if auto_boot else 'trial.c'),'-o',obj],name+'-compile.log')
        base.run([base.ZIG,'build-exe',*objects,'-target','thumb-freestanding-eabihf',
            '-mcpu','cortex_m7+fp_armv8d16sp','--script',ROOT/'trial-fast.ld','-fentry=trial_route',
            '-fno-compiler-rt','-O','ReleaseFast','-femit-bin='+str(OUT/'trial.elf')],'link.log')
    finally:base.OUT=previous
    segments,symbols=base.elf(OUT/'trial.elf')
    text=next(s for s in segments if s['flags']&1)
    assert text['address']==0x15000 and text['address']+text['size']<0x17e00,(hex(text['size']),hex(text['address']+text['size']))
    tables=next(s for s in segments if s['address']==0x20064b00)
    assert tables['address']+tables['size']<=0x200657e0
    required=['trial_begin','trial_route','trial_after','trial_node_control','trial_send_diagnostics','trial_runtime_bytes']
    assert all(k in symbols for k in required)
    report=dict(status='PRIVATE_TRIAL_ELF_BUILT_NO_HARDWARE',source_sha256=hashlib.sha256(Path(source).read_bytes()).hexdigest(),
        elf_sha256=hashlib.sha256((OUT/'trial.elf').read_bytes()).hexdigest(),text_address=hex(text['address']),text_bytes=text['size'],
        runtime_address=hex(RUNTIME),runtime_bytes=len(runtime_blob(auto_boot)),auto_boot=auto_boot,
        symbols={n:symbols[n] for n in required},target_asset=target.ASSET,
        target_model_index=target.MODEL_INDEX,target_signature=list(target.SIGNATURE),hardware_accessed=False)
    report.update(selected_kernel=selection,table_address=hex(tables['address']),table_bytes=tables['size'],scratch=['0x20065800','0x20065c80'],reserved_dtcm_end='0x20065d00')
    (OUT/'build.json').write_text(json.dumps(report,indent=2))
    (OUT/'runtime-initial.bin').write_bytes(runtime_blob(auto_boot))
    return report

if __name__=='__main__':
    import argparse
    p=argparse.ArgumentParser();p.add_argument('model');p.add_argument('--auto-boot',action='store_true');a=p.parse_args();print(json.dumps(build(a.model,a.auto_boot),indent=2))
