"""Private all-model preamp ELF. No firmware install, device API or USB.

Derive from the physically observed r95 audio adapter without overwriting it.
The OEM Boost is restored when the external pedal is not selected. Custom
pedals run once before the original model; their temporary input binding is
restored after native dispatch, including in-place native audio buffers.
"""
import hashlib, json, os, struct, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
NAM = ROOT.parent
TV = NAM.parents[1]
sys.path.insert(0, str(NAM))
from build import elf

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def replace(text, before, after):
    assert text.count(before) == 1, (before[:100], text.count(before))
    return text.replace(before, after)

def controls():
    folder = NAM.parent/'catalyst-ir/analysis'
    db = json.loads((folder/'asset-database.json').read_text())
    maps = json.loads((folder/'runtime-maps.json').read_text())
    fw = (folder/'image-0-segment-1-60001000.bin').read_bytes()
    def read(address, size):
        for row in maps:
            if row['destination'] <= address < row['end']:
                address = row['source'] + address - row['destination']; break
        return fw[address-0x60001000:address-0x60001000+size]
    signatures = json.loads((NAM/'evidence/factory-amp-signatures.json').read_text())['models']
    result = []
    for entry in signatures:
        fields = [int(x,16) for x in next(a for a in db['models'] if a['name']==entry['name'])['fields']]
        params = {}
        for i in range(fields[7]):
            row = struct.unpack('<8I',read(int(db['base'],16)+fields[11]+32*i,32))
            params[db['symbols'][row[2]]] = row[4]
        result.append(dict(name=entry['name'],symbol=entry['symbol'],signature=entry['signature'],params=params))
        assert 'BoostSwitch' in params and 'Boost' in params
    return result

def derive(rows):
    source = NAM/'dual-lab/lease-build/adapter.c'
    assert sha(source)=='79a7cb30734e0dffbd9d6fafef55cfe9bb3768735f6e1701cee47d374a56d584'
    s = source.read_text().replace('\r\n','\n')
    s = replace(s,'if(equal)return 1;','if(equal)return (int)a+1;')
    s = replace(s,'static int factory_amp(const unsigned *g)', 'static int factory_amp(const unsigned *g)')
    point = s.index('static void observe_stack(')
    s = s[:point]+'''/* Dedicated scratch in the already copied writable table reservation. */
typedef struct {
    unsigned node,arena,graph,entry,original_input,boost_dirty,boost_native;
    float level;
    float input[32];
} StockPreamp;
static StockPreamp stock __attribute__((section(".stock_data")))={.level=1};
static const unsigned char boost_ids[12]={BOOST_IDS};
static const unsigned char switch_ids[12]={SWITCH_IDS};
static void stock_switch(unsigned model){
    if(!stock.node||!model)return;
    unsigned desired=R->pedal.target.mode?0u:R->boost_enabled;
    if(stock.boost_dirty||stock.boost_native!=desired){
        /* Bypass the observer thunk; native UI values remain authoritative. */
        int rc=((int(*)(unsigned,unsigned,unsigned,const void*))0x17f81u)(stock.arena,stock.node,switch_ids[model-1],&desired);
        if(rc<0){fault(10);return;}
        stock.boost_native=desired;stock.boost_dirty=0;
    }
}
'''.replace('BOOST_IDS',','.join(str(r['params']['Boost']) for r in rows)).replace('SWITCH_IDS',','.join(str(r['params']['BoostSwitch']) for r in rows))+s[point:]
    # Forward declaration because stock routing consumes the same validated
    # configuration mailbox as NAM processing, never a second control source.
    point=s.index('/* Observe the native DSP-node setter')
    s=s[:point]+'''static int stock_route(const unsigned *arena,const unsigned *g,unsigned model){
    if(arena[0x14c/4]!=128||stock.entry)return 0;
    R->route_reject=4;
    if(!stock.node||((const unsigned*)stock.node)[5]!=(unsigned)g)return 0;
    unsigned count=g[3],ptr=g[2];
    if(count<2||count>128||(ptr&3u)||ptr<0x20000000u||ptr>0x20050000u-count*20)return 0;
    unsigned *e=(unsigned*)ptr;
    if(e[0]!=1||e[1]!=1||!audio_buffer(e[3]))return 0;
    sync_preamp();stock_switch(model);
    float level=R->boost_enabled?R->pedal.target.level:1.0f;
    if(!R->pedal.target.mode&&level==1.0f&&stock.level==1.0f)return 0;
    for(unsigned i=0;i<32;i++)stock.input[i]=((const float*)e[3])[i];
    if(R->pedal.target.mode){stock.level=1.0f;preamp_process((PreampState*)&R->pedal,stock.input,stock.input,32);}
    else for(unsigned i=0;i<32;i++){stock.level+=(level-stock.level)*.0078125f;stock.input[i]*=stock.level;}
    stock.graph=(unsigned)g;stock.original_input=e[3];stock.entry=(unsigned)(e+3);
    e[3]=(unsigned)stock.input;
    /* Return zero so the original model, including its own gain/EQ, runs. */
    return 0;
}
'''+s[point:]
    s=replace(s,'''    if(!signature((const unsigned*)graph)) {
        if(factory_amp((const unsigned*)graph))R->leave_target=1;
        return;
    }''','''    unsigned model=(unsigned)factory_amp((const unsigned*)graph);
    if(model){
        stock.node=(unsigned)node;stock.arena=(unsigned)arena;
        if(param==boost_ids[model-1]){union{unsigned u;float f;}v={data[0]};if(finite(v.f)&&v.f>=0&&v.f<=1)R->boost_scale=1+v.f;}
        if(param==switch_ids[model-1]){R->boost_enabled=data[0]!=0;stock.boost_dirty=1;}
    }
    if(!signature((const unsigned*)graph)) {
        if(model)R->leave_target=1;
        return;
    }''')
    s=replace(s,'    unsigned slot=nam_slot(g);if(!slot||!R->bank_source[slot-1])return 0;', '''    unsigned slot=nam_slot(g);
    if(!slot||!R->bank_source[slot-1]){
        unsigned model=(unsigned)factory_amp(g);
        return model?stock_route(arena,g,model):0;
    }''')
    s=replace(s,'''    if(!handled&&signature(g)) {''','''    if(stock.entry&&stock.graph==(unsigned)g){
        *(unsigned*)stock.entry=stock.original_input;stock.entry=0;stock.graph=0;
    }
    if(!handled&&signature(g)) {''')
    return s

def main():
    out=ROOT/'build';out.mkdir(exist_ok=True)
    rows=controls();(out/'OEM-controls.json').write_text(json.dumps(rows,indent=2))
    (out/'adapter.c').write_text(derive(rows))
    for name in ('trial_target.h','trial_tables.h'):
        (out/name).write_bytes((NAM/'dual-lab/lease-build'/name).read_bytes())
    (out/'lab.ld').write_text('ENTRY(trial_route)\nSECTIONS { . = 0x15000; .text : { *(.text*) *(.rodata*) } . = 0x20064b00; .nam_tables : { *(.nam_tables*) *(.stock_data*) } /DISCARD/ : { *(.ARM.exidx*) *(.ARM.extab*) *(.comment*) } }\n')
    zig=TV/'.tools/zig-cross/zig-windows-x86_64-0.13.0/zig.exe'
    env=os.environ.copy();env['ZIG_GLOBAL_CACHE_DIR']=str(out/'global-cache');env['ZIG_LOCAL_CACHE_DIR']=str(out/'local-cache')
    sources=[NAM/'build-fast/span-max-r94/kernel-embedded.c',out/'adapter.c',NAM/'boost-lab/tonal-v2/preamp.c']
    objects=[]
    for i,source in enumerate(sources):
        obj=out/f'{i}.o';objects.append(obj)
        args=[zig,'cc','-target','thumb-freestanding-eabihf','-mcpu=cortex_m7+fp_armv8d16sp','-mfpu=fpv5-sp-d16','-mfloat-abi=hard','-O3' if i==0 else '-Os','-ffp-contract=fast' if i==0 else '-ffp-contract=off','-ffreestanding','-fno-builtin','-fno-stack-protector','-Wall','-Werror','-I',out,'-I',NAM/'build','-I',NAM/'boost-lab','-c',source,'-o',obj]
        run=subprocess.run(list(map(str,args)),env=env,capture_output=True,text=True,timeout=120)
        (out/f'compile-{i}.log').write_text(run.stdout+run.stderr);assert run.returncode==0,run.stdout+run.stderr
    args=[zig,'build-exe',*objects,'-target','thumb-freestanding-eabihf','-mcpu','cortex_m7+fp_armv8d16sp','--script',out/'lab.ld','-fentry=trial_route','--no-gc-sections','-fno-strip','-fno-compiler-rt','-O','ReleaseSmall','-femit-bin='+str(out/'combined.elf')]
    run=subprocess.run(list(map(str,args)),env=env,capture_output=True,text=True,timeout=120);(out/'link.log').write_text(run.stdout+run.stderr);assert run.returncode==0,run.stdout+run.stderr
    segments,symbols=elf(out/'combined.elf');text=next(s for s in segments if s['flags']&1);tables=next(s for s in segments if s['address']==0x20064b00)
    result=dict(compiled=True,hardware_accessed=False,USB_opened=False,firmware_created=False,all_models=12,leases_enabled=True,preamp_reference_elf="boost-lab/tonal-v2/build/preamp.elf",preamp_reference_sha256=sha(NAM/"boost-lab/tonal-v2/build/preamp.elf"),text_bytes=text['size'],text_limit=0x2e00,table_bytes=tables['size'],table_limit=0xce0,static_code_and_tables_fit=text['address']+text['size']<=0x17e00 and tables['address']+tables['size']<=0x200657e0,elf_sha256=sha(out/'combined.elf'),sources={str(p.relative_to(TV)):sha(p) for p in sources+[Path(__file__)]},symbols=symbols)
    (out/'build.json').write_text(json.dumps(result,indent=2));print(json.dumps({k:v for k,v in result.items() if k not in ('sources','symbols')},indent=2))
    assert result['static_code_and_tables_fit'],'New all-model engine exceeds the existing reserved range; never package it.'

if __name__=='__main__':main()
