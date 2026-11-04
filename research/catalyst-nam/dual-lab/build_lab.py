"""Derive a NON-INSTALLABLE NAM/preamp ELF from the pinned working adapter.

No CTF, boot patch, device API or USB access. Bank source addresses must be
provided by the test fixture; there is no physical storage integration here.
"""
import argparse,hashlib,json,os,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent;TV=NAM.parents[1]
sys.path.insert(0,str(NAM))
from build import elf
BASE_SHA='90550ec239d73785b97c0825db90f390ef70bf0fcf59a30c2ef8bbd7e689b392'
def replace(s,a,b):
    assert s.count(a)==1,(a[:100],s.count(a))
    return s.replace(a,b)
def derive(leases=False):
    source=(NAM/'autoboot.c').read_bytes();assert hashlib.sha256(source).hexdigest()==BASE_SHA
    s=source.decode().replace('\r\n','\n');s=replace(s,'#include "trial_target.h"','#include "trial_target.h"\n#include "preamp.h"')
    s=replace(s,'float drive_scale,boost_scale;unsigned auto_entry,boost_enabled;',
      '''float drive_scale,boost_scale;unsigned auto_entry,boost_enabled;
    PreampState pedal;PreampConfig pending;unsigned sequence,applied;
    unsigned bank_source[2],active_slot,copy_word;
    unsigned bank_pending[2],bank_state;''')
    start=s.index('static int signature(');end=s.index('static int audio_buffer(',start)
    s=s[:start]+'''static unsigned nam_slot(const unsigned *g) {
    unsigned p=g[5];if(p<0x80000000u||p>0x81ffffe0u)return 0;
    const unsigned *d=(const unsigned*)p;unsigned first=1,second=1;
    for(unsigned i=0;i<6;++i){if(d[i]!=cf_nam_signatures[0][i])first=0;if(d[i]!=cf_nam_signatures[1][i])second=0;}
    return first?1u:(second?2u:0u);
}
static int signature(const unsigned *g) {return nam_slot(g)!=0;}
'''+s[end:]
    s=replace(s,'R->boost_enabled?R->boost_scale:1.0f','R->boost_enabled&&R->pedal.target.mode==0?R->boost_scale*R->pedal.target.level:1.0f')
    # Native control numbers belong to each model, not to the UI labels.
    # Elmsley/NAM2 has Bass=2 and Boost=15, whereas AC/NAM1 has Boost=2.
    # Canonicalize before either the filter or apply_control sees the number.
    s=replace(s,'    R->leave_target=0;\n    if(param!=TARGET_DRIVE', '''    R->leave_target=0;
    if(nam_slot((const unsigned*)graph)==2u){
        switch(param){
            case 1u:case 6u:case 7u:case 20u:break;
            case 2u:param=TARGET_BASS;break;
            case 3u:param=TARGET_MID;break;
            case 4u:param=TARGET_TREBLE;break;
            case 15u:param=TARGET_BOOST;break;
            default:return;
        }
    }
    if(param!=TARGET_DRIVE''')
    marker='static void apply_control('
    point=s.index(marker)
    s=s[:point]+'''/* Internal control-owner API, never a host-supplied raw address.
 * source must be an immutable, verified 7772-byte staging allocation:
 * 1871 weights followed by 72 silence floats. Retain it until replaced and
 * taken at a later outer audio boundary. State 2 means taken, not audible. */
int cf_nam_bank_request(unsigned slot,unsigned source,unsigned bytes) {
    if(R->magic!=MAGIC||slot<1||slot>2||!source||(source&3u)||bytes!=7772u||source==R->bank_source[slot-1])return -1;
    if(R->bank_state==1||R->bank_state==3)return -2;
    R->bank_state=3;__asm__ volatile("dmb":::"memory");
    R->bank_pending[0]=R->bank_source[0];R->bank_pending[1]=R->bank_source[1];R->bank_pending[slot-1]=source;
    __asm__ volatile("dmb":::"memory");R->bank_state=1;return 0;
}
static void sync_banks(void) {
    if(R->bank_state!=1)return;
    __asm__ volatile("dmb":::"memory");
    unsigned a=R->bank_pending[0],b=R->bank_pending[1],slot=R->active_slot;
    unsigned changed=(slot==1&&a!=R->bank_source[0])||(slot==2&&b!=R->bank_source[1]);
    R->bank_source[0]=a;R->bank_source[1]=b;
    if(changed){R->copy_word=0;R->auto_entry=0;R->mode=0;R->reset_done=0;}
    __asm__ volatile("dmb":::"memory");R->bank_state=2;
}
/* Single control producer; audio owner consumes complete even revisions. */
int cf_preamp_request(const PreampConfig *c) {
    if(!c||c->mode>3||c->enabled>1||c->boost>1||c->bright>1||
       !finite(c->drive)||!finite(c->tone)||!finite(c->level)||
       c->drive<0||c->drive>1||c->tone<0||c->tone>1||c->level<0||c->level>2)return -1;
    unsigned next=(R->sequence+2u)&~1u;R->sequence=next|1u;
    __asm__ volatile("dmb":::"memory");R->pending=*c;
    __asm__ volatile("dmb":::"memory");R->sequence=next;return 0;
}
static void sync_preamp(void) {
    unsigned seq=R->sequence;
    if(!(seq&1u)&&seq!=R->applied){
        __asm__ volatile("dmb":::"memory");PreampConfig next=R->pending;
        __asm__ volatile("dmb":::"memory");
        if(seq==R->sequence&&preamp_configure((PreampState*)&R->pedal,&next)==0)R->applied=seq;
    }
    PreampConfig c=R->pedal.target;c.drive=R->boost_scale-1.0f;c.enabled=R->boost_enabled;
    preamp_configure((PreampState*)&R->pedal,&c);update_input_gain();
}
'''+s[point:]
    s=replace(s,'history[off+col*3]=silence[ring][0];\n        history[off+col*3+1]=silence[ring][1];\n        history[off+col*3+2]=silence[ring][2];',
      '''const volatile float *row=(const volatile float*)(R->bank_source[R->active_slot-1]+7484u+ring*12u);
        history[off+col*3]=row[0];history[off+col*3+1]=row[1];history[off+col*3+2]=row[2];''')
    s=replace(s,'if(R->magic!=MAGIC||!signature(g))return 0;',
      '''if(R->magic!=MAGIC)return 0;
    unsigned slot=nam_slot(g);if(!slot||!R->bank_source[slot-1])return 0;''')
    s=replace(s,'R->seen=1;observe_stack();',
      '''R->seen=1;observe_stack();
    if(R->active_slot!=slot){R->active_slot=slot;R->copy_word=0;R->auto_entry=0;}
    sync_preamp();''')
    s=replace(s,'    if(depth!=1)return;','    if(depth!=1)return;\n    sync_banks();')
    s=replace(s,'        if(!R->reset_done)reset_step();',
      '''        if(R->copy_word<1871u){
            unsigned p=R->copy_word,n=1871u-p;if(n>128u)n=128u;
            const unsigned *source=(const unsigned*)R->bank_source[R->active_slot-1];
            unsigned *weights=(unsigned*)WEIGHTS;
            for(unsigned i=0;i<n;++i)weights[p+i]=source[p+i];R->copy_word=p+n;
            for(unsigned i=0;i<32;++i)output[i]=0;return 1;
        }
        if(!R->reset_done)reset_step();''')
    s=replace(s,'    float dg=(R->input_gain-gain)*0.03125f,dl=(R->level-level)*0.03125f;',
      '''    float dg=(R->input_gain-gain)*0.03125f,dl=(R->level-level)*0.03125f;
    for(unsigned i=0;i<32;++i)scaled[i]=input[i];
    if(R->pedal.target.mode)preamp_process((PreampState*)&R->pedal,scaled,scaled,32);
    if(R->pedal.fault){fault(10);for(unsigned i=0;i<32;++i)output[i]=0;return 1;}''')
    s=replace(s,'gain+=dg;scaled[i]=input[i]*gain;','gain+=dg;scaled[i]*=gain;')
    # Identity transport is deliberately absent: keep ordinary vendor replies.
    s=s[:s.index('/* Read-only diagnostic reply')]
    if leases:
        # Three legacy diagnostic-only words are unused after removing the
        # old CNAM endpoint. Reuse them for fixed audio-budget observations;
        # runtime reservation and all normal control semantics stay intact.
        s=replace(s,'    sync_preamp();','''    sync_preamp();
    if(R->mode==2){R->diagnostic_tick=R->diagnostic_gate;
        if(R->diagnostic_gate>R->gate_tick)R->gate_tick=R->diagnostic_gate;
        R->diagnostic_gate=0;}''')
        s=replace(s,'    if(depth==1&&R->seen&&(R->want||R->mode)) {','    if(depth==1&&(R->want||R->mode)) {')
        s=replace(s,'        if(elapsed>R->max_root)R->max_root=elapsed;','''        R->diagnostic_gate+=elapsed;
        if(!R->seen)return;
        if(elapsed>R->max_root)R->max_root=elapsed;''')
        s=replace(s,'if(R->want&&!R->dwt_on)counters();','if(R->want&&!R->dwt_on){counters();R->root_start=clock_now();}')
        s=replace(s,'int cf_preamp_request(const PreampConfig *c) {','int cf_preamp_request(const PreampConfig *c) {\n    if(R->magic!=MAGIC)return -1;')
        s+='\nunsigned cf_audio_is_outer(void){return R->magic==MAGIC&&R->depth==1;}\n'
        s+='''
/* A deliberate native IR workspace rebuild is a scheduling discontinuity,
 * not a steady-state sample period. The audio owner alone brackets it.
 * Keep per-dispatch NAM/root cycle limits and all memory/value guards live.
 * A new explicit selection may re-arm only the cadence fallback (9), never
 * corruption, invalid samples, pedal failure or genuine dispatch overrun. */
void cf_audio_discontinuity(void){
    if(R->magic!=MAGIC)return;
    R->last_target_cycle=0;R->late_streak=0;
    if(R->fault==9&&!R->want&&R->mode==0)R->auto_entry=0;
}
static unsigned float_bits(float f){union {float f;unsigned u;}v={f};return v.u;}
unsigned cf_audio_timing(unsigned index){switch(index){
    case 0:return R->min_period;case 1:return R->max_period;
    case 2:return R->late_periods;case 3:return R->late_streak;
    case 4:return R->mode;case 5:return R->want;
    case 6:return R->active_slot;case 7:return R->last_nam;
    case 8:return float_bits(R->drive_scale);
    case 9:return float_bits(R->boost_scale);
    case 10:return R->boost_enabled;
    case 11:return float_bits(R->input_gain);
    case 12:return float_bits(R->level);
    case 13:return R->eq_index[0]|(R->eq_index[1]<<8)|(R->eq_index[2]<<16)|(R->eq_index[3]<<24);
    case 14:return R->pedal.target.mode;
    case 15:return R->pedal.target.enabled;
    case 16:return float_bits(R->pedal.drive);
    case 17:return float_bits(R->pedal.tone);
    case 18:return float_bits(R->pedal.level);
    case 19:return float_bits(R->pedal.mix);
    case 20:return R->pedal.fault;
    case 21:return float_bits(R->input_peak);
    case 22:return float_bits(R->output_peak);
    case 23:return R->clipped_samples;
    case 24:return R->diagnostic_tick;
    case 25:return R->gate_tick;
    case 26:return R->diagnostic_gate;
    default:return 0xffffffffu;
}}
'''
        point=s.index('static void sync_banks(void)')
        s=s[:point]+'''/* Control-owner read-only acquisition receipt. CF_OK means the outer
 * audio boundary has stopped using the previous source, NOT audible-ready.
 * Same-core, one serialized control producer and one non-reentrant outer
 * audio owner are required. No other reader/DMA may retain source pointers. */
int cf_nam_bank_status(unsigned slot,unsigned source) {
    if(R->magic!=MAGIC||slot<1||slot>2||!source||(source&3u))return 2;
    unsigned state=R->bank_state;
    if(state==3)return 1;
    __asm__ volatile("dmb":::"memory");
    unsigned current=R->bank_source[slot-1],pending=R->bank_pending[slot-1];
    __asm__ volatile("dmb":::"memory");
    if(state!=R->bank_state)return 1;
    if(state==1&&pending==source)return 6;
    return current==source?0:3;
}
'''+s[point:]
    return s
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--leases',action='store_true');options=parser.parse_args()
    out=ROOT/('lease-build' if options.leases else 'build');out.mkdir(exist_ok=True)
    (out/'adapter.c').write_text(derive(options.leases))
    target=(NAM/'build-trial/trial_target.h').read_text()
    signatures=json.loads((NAM/'evidence/factory-amp-signatures.json').read_text())['models']
    rows=[next(r['signature'] for r in signatures if r['name']==name) for name in ('HD2_AmpImpossibleAC','A45_AmpImpossibleElmsleyNFB')]
    target+='\nstatic const unsigned cf_nam_signatures[2][6]={'+','.join('{'+','.join(str(v)+'u' for v in row)+'}' for row in rows)+'};\n'
    (out/'trial_target.h').write_text(target)
    tables=(NAM/'build-trial/trial_tables.h').read_text()
    (out/'trial_tables.h').write_text(tables)
    # ELF only: same bounds as the current trial, no changes to boot or flash.
    (out/'lab.ld').write_text('ENTRY(trial_route)\nSECTIONS { . = 0x15000; .text : { *(.text*) *(.rodata*) } . = 0x20064b00; .nam_tables : { *(.nam_tables*) } /DISCARD/ : { *(.ARM.exidx*) *(.ARM.extab*) *(.comment*) } }\n')
    zig=TV/'.tools/zig-cross/zig-windows-x86_64-0.13.0/zig.exe';env=os.environ.copy()
    env['ZIG_GLOBAL_CACHE_DIR']=str(out/'global-cache');env['ZIG_LOCAL_CACHE_DIR']=str(out/'local-cache')
    kernel=NAM/'build-fast/span-max-r94/kernel-embedded.c'
    parity=json.loads((kernel.parent/'result.json').read_text())
    assert parity['passed'] and parity['source_sha256']==hashlib.sha256(kernel.read_bytes()).hexdigest()
    assert all(r['max_error']==0 and r['chunk_error']==0 for r in parity['comparisons'])
    # Private tonal revision. Keep the r94 source intact and refuse stale
    # isolated circuit/ABI evidence before linking another audio engine.
    pedal=NAM/'boost-lab/tonal-v2/preamp.c'
    pedal_proof=pedal.parent/'build/result.json'
    pp=json.loads(pedal_proof.read_text())
    assert pp['status']=='PASS_ISOLATED_CIRCUIT_INFORMED_PROTOTYPE'
    assert pp['source_sha256']==hashlib.sha256(pedal.read_bytes()).hexdigest()
    assert pp['state_bytes']==164 and pp['protected_unchanged']
    assert (pedal.parent/'preamp.h').read_bytes()==(NAM/'boost-lab/preamp.h').read_bytes()
    assert pp['elf_sha256']==hashlib.sha256((pedal.parent/'build/preamp.elf').read_bytes()).hexdigest()
    sources=[kernel,out/'adapter.c',pedal]
    protected=[NAM/'autoboot.c',NAM/'app/resources/nam-engine.ctf',TV/'CatalystEditor/Development/CatalystCFEditor.exe']
    before={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}
    objects=[]
    for i,source in enumerate(sources):
        obj=out/f'{i}.o';objects.append(obj)
        args=[zig,'cc','-target','thumb-freestanding-eabihf','-mcpu=cortex_m7+fp_armv8d16sp','-mfpu=fpv5-sp-d16','-mfloat-abi=hard','-O3' if i==0 else '-Os','-ffp-contract=fast' if i==0 else '-ffp-contract=off','-ffreestanding','-fno-builtin','-fno-stack-protector','-Wall','-Werror','-I',out,'-I',NAM/'build','-I',NAM/'boost-lab/build','-I',NAM/'boost-lab','-c',source,'-o',obj]
        p=subprocess.run(list(map(str,args)),capture_output=True,text=True,env=env,timeout=120);(out/f'compile-{i}.log').write_text(p.stdout+p.stderr);assert p.returncode==0,p.stdout+p.stderr
    args=[zig,'build-exe',*objects,'-target','thumb-freestanding-eabihf','-mcpu','cortex_m7+fp_armv8d16sp','--script',out/'lab.ld','-fentry=trial_route','--no-gc-sections','-fno-strip','-fno-compiler-rt','-O','ReleaseSmall','-femit-bin='+str(out/'combined.elf')]
    p=subprocess.run(list(map(str,args)),capture_output=True,text=True,env=env,timeout=120);(out/'link.log').write_text(p.stdout+p.stderr);assert p.returncode==0,p.stdout+p.stderr
    segments,symbols=elf(out/'combined.elf');code=next(s for s in segments if s['flags']&1);tables=next(s for s in segments if s['address']==0x20064b00)
    assert not [name for name,s in symbols.items() if not s['index'] and s['info']>>4 in (1,2)]
    fits=code['address']+code['size']<=0x17e00 and tables['address']+tables['size']<=0x200657e0
    result=dict(compiled=True,static_code_and_tables_fit=fits,text_bytes=code['size'],text_limit=0x2e00,table_bytes=tables['size'],table_limit=0xce0,
      elf_sha256=hashlib.sha256((out/'combined.elf').read_bytes()).hexdigest(),hardware_accessed=False,firmware_created=False,
      protected_files_unchanged=before=={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in protected},
      leases_enabled=options.leases,
      preamp_reference_elf=str((pedal.parent/'build/preamp.elf').relative_to(NAM)),
      preamp_reference_sha256=pp['elf_sha256'],
      sources={str(p.relative_to(TV)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources+[ROOT/'build_lab.py',kernel.parent/'result.json',NAM/'build/geometry.h',out/'trial_target.h',out/'trial_tables.h',NAM/'boost-lab/preamp.h',pedal.parent/'preamp.h',pedal_proof]},symbols={n:symbols[n] for n in ('trial_begin','trial_route','trial_after','trial_node_control','trial_runtime_bytes','cf_preamp_request','cf_nam_bank_request')+ (('cf_nam_bank_status','cf_audio_is_outer','cf_audio_discontinuity','cf_audio_timing') if options.leases else ())})
    (out/'build.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
if __name__=='__main__':main()
