"""Separate recovery-actor ELF. Never replaces actor-build or creates a CTF."""
import argparse,hashlib,json,os,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent;TV=ROOT.parents[2]
sys.path.insert(0,str(ROOT.parent))
from build import elf
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--leased',action='store_true');parser.add_argument('--coordinator',action='store_true');parser.add_argument('--preamp',action='store_true');parser.add_argument('--identity',action='store_true');parser.add_argument('--seeded',action='store_true');parser.add_argument('--owner-loop',action='store_true');options=parser.parse_args()
    if options.owner_loop:options.seeded=True
    if options.seeded:options.identity=True
    if options.identity:options.preamp=True
    if options.preamp:options.coordinator=True
    if options.coordinator:options.leased=True
    original=ROOT/'asset_actor.c';prior=json.loads((ROOT/'actor-build/build.json').read_text())
    assert hashlib.sha256(original.read_bytes()).hexdigest()==prior['source_sha256']['asset_actor.c']
    out=ROOT/('actor-owner-lab' if options.owner_loop else 'actor-seeded-lab' if options.seeded else 'actor-identity-lab' if options.identity else 'actor-preamp-lab' if options.preamp else 'actor-coordinator-lab' if options.coordinator else 'actor-stage-lab' if options.leased else 'actor-recovery-lab')/'actor-build';out.mkdir(parents=True,exist_ok=True)
    text=original.read_text();assert text.count('cf_receiver_init(')==1 and text.count('cf_receiver_handle(')==1
    text=text.replace('#include "asset_wire.h"','#include "asset_recover_wire.h"')
    text=text.replace('cf_receiver_init(','cf_recovery_receiver_init(').replace('cf_receiver_handle(','cf_recovery_receiver_handle(')
    assert text.count('&A.store,CF_DATA_WRITE)')==1
    text=text.replace('&A.store,CF_DATA_WRITE)','&A.store,CF_DATA_WRITE|CF_RECOVER_RECEIVING)')
    if options.preamp:
        text=text.replace('#include "asset_recover_wire.h"','#include "asset_recover_wire.h"\nextern void cf_owner_native_request(void);')
        marker='''        unsigned fn=original_dispatch?original_dispatch:(unsigned)ORIGINAL;'''
        assert text.count(marker)==1;text=text.replace(marker,'''        cf_owner_native_request();
'''+marker)
        text+='''
/* Dedicated preamp sidecars use the SAME serialized FS owner. Each call
 * closes its handle before returning; no native preset filename is written. */
static int open_preamp(unsigned bank,unsigned flags){
    if(A.magic!=ACTOR_MAGIC||bank>=2)return -1;
    static const char *const files[2]={"cfcx_preamp_0","cfcx_preamp_1"};
    clear(A.file,sizeof(A.file));A.filecfg[0]=(unsigned)A.cache;A.filecfg[1]=A.filecfg[2]=0;
    return OPEN(LFS,FILE,files[bank],flags,A.filecfg);
}
int cf_preamp_io_read(void *unused,unsigned bank,unsigned offset,void *data,unsigned count){
    (void)unused;if(offset>480||count>480-offset)return -1;int rc=open_preamp(bank,1);
    if(rc==-2){for(unsigned i=0;i<count;++i)((unsigned char*)data)[i]=255;return 0;}if(rc)return -1;
    rc=SEEK(LFS,FILE,offset,0);int got=rc==(int)offset?READ(LFS,FILE,data,count):-1,closed=CLOSE(LFS,FILE);
    if(got<0||got>(int)count||closed)return -1;
    for(unsigned i=(unsigned)got;i<count;++i)((unsigned char*)data)[i]=255;return 0;
}
int cf_preamp_io_erase(void *unused,unsigned bank){(void)unused;if(open_preamp(bank,0x503))return -1;return CLOSE(LFS,FILE);}
int cf_preamp_io_write(void *unused,unsigned bank,unsigned offset,const void *data,unsigned count){
    (void)unused;if(offset>480||count>480-offset||open_preamp(bank,0x103))return -1;
    int rc=SEEK(LFS,FILE,offset,0),written=rc==(int)offset?WRITE(LFS,FILE,data,count):-1;
    int closed=CLOSE(LFS,FILE);return written==(int)count&&!closed?0:-1;
}
int cf_preamp_io_sync(void *unused){(void)unused;return 0;} /* close synchronizes native lfs */
int cf_preamp_read_native(unsigned index,unsigned offset,void *data,unsigned count){
    static const char *const native[13]={"bank_0_channel_a","bank_0_channel_b","bank_1_channel_a","bank_1_channel_b","bank_2_channel_a","bank_2_channel_b","bank_3_channel_a","bank_3_channel_b","bank_4_channel_a","bank_4_channel_b","bank_5_channel_a","bank_5_channel_b","channel_manual_edited"};
    if(A.magic!=ACTOR_MAGIC||index>=13||offset>65536||count>256)return -1;
    clear(A.file,sizeof(A.file));A.filecfg[0]=(unsigned)A.cache;A.filecfg[1]=A.filecfg[2]=0;
    int rc=OPEN(LFS,FILE,native[index],1,A.filecfg);if(rc==-2)return 0;if(rc)return -1;
    rc=SEEK(LFS,FILE,offset,0);int got=rc==(int)offset?READ(LFS,FILE,data,count):-1,closed=CLOSE(LFS,FILE);
    return got>=0&&got<=(int)count&&!closed?got:-1;
}
'''
    if options.identity:
        text=text.replace('#include "asset_recover_wire.h"','#include "asset_recover_wire.h"\n#include "asset_identity.h"\nstatic uint32_t native_uid[2];')
        old='cf_recovery_receiver_handle(&A.receiver,&A.incoming,&A.outgoing);'
        assert text.count(old)==1
        text=text.replace(old,'if(A.incoming.opcode==CF_IDENTITY_QUERY)cf_identity_reply(&A.receiver,native_uid,&A.incoming,&A.outgoing);else '+old)
        text+='''
/* Bootstrap-only binding, not a MIDI command. Native bootstrap must obtain
 * this stable silicon identity without changing fuses or USB descriptors. */
int cf_actor_bind_identity(unsigned low,unsigned high){
    uint32_t uid[2]={low,high};
    if(A.magic!=ACTOR_MAGIC||A.store.phase||A.receiver.session||cf_identity_valid(native_uid)||!cf_identity_valid(uid))return CF_BAD_INPUT;
    native_uid[0]=low;native_uid[1]=high;barrier();return CF_OK;
}
'''
    if options.seeded:
        text=text.replace('#include "asset_identity.h"','#include "asset_identity.h"\n#include "asset_seed.h"\nstatic CfAssetSeed initial;')
        old='return OPEN(LFS,FILE,names[slot][bank],flags,A.filecfg);'
        new='char path[74];const char *name=names[slot][bank];if(initial.manifest){if(cf_seed_name(&initial,slot,bank,path,sizeof(path)))return -1;name=path;}return OPEN(LFS,FILE,name,flags,A.filecfg);'
        assert text.count(old)==1;text=text.replace(old,new)
        old="if(rc==-2){for(unsigned i=0;i<count;++i)((unsigned char*)data)[i]=255;return 0;}"
        assert text.count(old)==2
        text=text.replace(old,"if(rc==-2){if(initial.manifest)return cf_seed_read(&initial,slot,bank,offset,data,count)?-1:0;for(unsigned i=0;i<count;++i)((unsigned char*)data)[i]=255;return 0;}",1)
        old='return OPEN(LFS,FILE,files[bank],flags,A.filecfg);'
        new="char path[74];const char *name=files[bank];if(initial.manifest){if(cf_seed_name(&initial,0,bank,path,sizeof(path)))return -1;path[70]='p';name=path;}return OPEN(LFS,FILE,name,flags,A.filecfg);"
        assert text.count(old)==1;text=text.replace(old,new)
        old='if(A.incoming.opcode==CF_IDENTITY_QUERY)cf_identity_reply(&A.receiver,native_uid,&A.incoming,&A.outgoing);else '
        new='''if(A.incoming.opcode==CF_IDENTITY_QUERY){
            if(cf_identity_reply(&A.receiver,native_uid,&A.incoming,&A.outgoing)==CF_OK&&initial.manifest){
                A.outgoing.bytes=52;A.outgoing.data[4]=2;
                for(unsigned i=0;i<32;++i)A.outgoing.data[20+i]=initial.manifest->edition[i];
            }
        }else '''
        assert text.count(old)==1;text=text.replace(old,new)
        text+='''
int cf_actor_bind_seed(const CfSeedManifest *manifest){
    if(A.magic!=ACTOR_MAGIC||A.store.phase||A.receiver.session)return CF_BAD_INPUT;
    return cf_seed_init(&initial,manifest,A.store.product);
}
'''
    if options.owner_loop:
        text=text.replace('#include "asset_recover_wire.h"','#include "asset_recover_wire.h"\nextern int cf_owner_bootstrap(void);\nextern unsigned cf_owner_stat(unsigned);\nextern void cf_owner_step(void);\nextern void cf_owner_committed(unsigned);')
        text=text.replace('void cf_rx_byte(unsigned byte){','void cf_rx_byte(unsigned byte){\n    if(!cf_owner_stat(0))return;')
        text=text.replace('extern void cf_owner_native_request(void);','extern void cf_owner_native_request(const unsigned*);\nextern int cf_preamp_owner_wire(CfAssetReceiver*,const CfWireMessage*,CfWireMessage*);')
        text=text.replace('cf_owner_native_request();','cf_owner_native_request(request);')
        text=text.replace('}else cf_recovery_receiver_handle(&A.receiver,&A.incoming,&A.outgoing);','}else if(A.incoming.opcode>=9&&A.incoming.opcode<=12)cf_preamp_owner_wire(&A.receiver,&A.incoming,&A.outgoing);else cf_recovery_receiver_handle(&A.receiver,&A.incoming,&A.outgoing);')
        text=text.replace('extern int cf_owner_bootstrap(void);','extern int cf_owner_bootstrap(void);\nextern int cf_owner_diagnostic(CfAssetReceiver*,const CfWireMessage*,CfWireMessage*);')
        text=text.replace('}else if(A.incoming.opcode>=9', '}else if(A.incoming.opcode==13)cf_owner_diagnostic(&A.receiver,&A.incoming,&A.outgoing);else if(A.incoming.opcode>=9')
        text=text.replace('barrier();A.magic=ACTOR_MAGIC;return 0;','A.receiver.capabilities|=8u;barrier();A.magic=ACTOR_MAGIC;return 0;')
        text=text.replace('unsigned cf_fs_dispatch(const unsigned *request){','static unsigned cf_fs_dispatch_inner(const unsigned *request){')
        old='if(A.store.phase==2)cf_receiver_pump(&A.receiver);'
        assert text.count(old)==1
        text=text.replace(old,'if(A.store.phase==2){int rc=cf_receiver_pump(&A.receiver);if(rc==CF_OK)cf_owner_committed(A.store.slot);}')
        text+='''
/* Readiness is published only after all bindings succeed. No packet is
 * accepted from MidiTask during bootstrap or after a failed bootstrap. */
void cf_actor_suspend(void){barrier();A.magic=0;}
unsigned cf_fs_dispatch(const unsigned *request){
    if(!request)return 0;
    (void)cf_owner_bootstrap();
    unsigned result=0;
    if(!((request[0]&255u)==8u&&request[1]==ACTOR_MAGIC))result=cf_fs_dispatch_inner(request);
    cf_owner_step();return result;
}
'''
    generated=out/'asset_actor_recovery.c';generated.write_text(text,encoding='utf-8')
    linker=out/'actor.ld';linker.write_text('ENTRY(cf_fs_dispatch)\nSECTIONS { . = 0x80874000; .text : { *(.text*) *(.rodata*) } . = 0x80860000; .bss (NOLOAD) : { *(.bss*) *(COMMON) __cf_actor_bss_end = .; } /DISCARD/ : { *(.ARM.exidx*) *(.ARM.extab*) *(.comment*) } }\n')
    env=os.environ.copy();env['ZIG_GLOBAL_CACHE_DIR']=str(out/'global-cache');env['ZIG_LOCAL_CACHE_DIR']=str(out/'local-cache')
    zig=TV/'.tools/zig-cross/zig-windows-x86_64-0.13.0/zig.exe';objects=[]
    sources=[ROOT/n for n in ('asset_store.c','asset_wire.c','asset_load.c','ir_handoff.c','asset_recover.c','asset_recover_wire.c')]+[generated,ROOT/'receive_wrapper.S']
    if options.identity:sources.append(ROOT/'asset_identity.c')
    if options.seeded:sources.append(ROOT/'asset_seed.c')
    if options.leased:
        from derive_leased_ir import derive
        leased=out/'ir_handoff_leased.c';leased.write_text(derive(),encoding='utf-8')
        sources=[ROOT/'asset_stage.c' if p.name=='asset_store.c' else leased if p.name=='ir_handoff.c' else p for p in sources]
    if options.coordinator:
        audio=json.loads((ROOT.parent/'dual-lab/lease-build/build.json').read_text())
        assert audio['leases_enabled'] and audio['static_code_and_tables_fit']
        image=ROOT.parent/'dual-lab/lease-build/combined.elf';assert hashlib.sha256(image.read_bytes()).hexdigest()==audio['elf_sha256']
        addresses={k:audio['symbols'][k]['address']|1 for k in ('cf_nam_bank_request','cf_nam_bank_status')}
        binding=out/'coordinator_binding.c'
        binding.write_text('''/* Internal laboratory owner binding. No wire or boot hook. */
#include "asset_coordinator.h"
extern unsigned cf_actor_store_address(void);
static CfAssetCoordinator Q;
unsigned cf_coord_address(void){return (unsigned)&Q;}
int cf_coord_bind(unsigned ir,unsigned n1a,unsigned n1b,unsigned n2a,unsigned n2b){
    CfAssetAudio api={(int(*)(unsigned,unsigned,unsigned))%du,(int(*)(unsigned,unsigned))%du};
    return cf_coord_init(&Q,(CfAssetStore*)cf_actor_store_address(),&api,(void*)ir,(void*)n1a,(void*)n1b,(void*)n2a,(void*)n2b);
}
int cf_coord_start(unsigned slot,unsigned graph){return cf_coord_request(&Q,slot,graph);}
int cf_coord_step(void){return cf_coord_pump(&Q);}
unsigned cf_coord_stat(unsigned index){switch(index){case 0:return Q.phase;case 1:return Q.dirty;case 2:return Q.fault;case 3:return Q.last_status;case 4:return Q.stage.offset;case 5:return Q.stage.active;default:return 0xffffffffu;}}
'''%(addresses['cf_nam_bank_request'],addresses['cf_nam_bank_status']),encoding='utf-8')
        sources.extend([ROOT/'asset_coordinator.c',binding])
    if options.preamp:
        preset=out/'preamp_binding.c'
        preset.write_text('''/* Internal FS-owner entrypoints, no public wire commands. */
#include "preset_job.h"
#include "asset_store.h"
extern unsigned cf_actor_store_address(void);
extern int cf_preamp_io_read(void*,unsigned,unsigned,void*,unsigned);
extern int cf_preamp_io_erase(void*,unsigned);
extern int cf_preamp_io_write(void*,unsigned,unsigned,const void*,unsigned);
extern int cf_preamp_io_sync(void*);
static CfPreampPresets P;
static CfPreampPresetJob J;
extern int cf_preamp_read_native(unsigned,unsigned,void*,unsigned);
void cf_owner_native_request(void){cf_preamp_job_native_activity(&J);}
int cf_actor_preamp_open(void){CfPreampPresetIO io={0,cf_preamp_io_read,cf_preamp_io_erase,cf_preamp_io_write,cf_preamp_io_sync};return cf_preamp_presets_open(&P,&io,((CfAssetStore*)cf_actor_store_address())->product);}
int cf_actor_preamp_pump(void){return cf_preamp_presets_pump(&P);}
int cf_actor_preamp_save(unsigned index,unsigned tx,const CfPreampRecord *r){return cf_preamp_presets_save(&P,index,tx,r);}
int cf_actor_preamp_get(unsigned index,unsigned native_crc,CfPreampRecord *out){return cf_preamp_presets_get(&P,index,native_crc,out);}
unsigned cf_actor_preamp_address(void){return (unsigned)&P;}
int cf_actor_preamp_job_init(void){return cf_preamp_job_init(&J,&P,cf_preamp_read_native,(CfApplyPreamp)%du);}
int cf_actor_preamp_job_save(unsigned index,unsigned tx,const CfPreampRecord *r){return cf_preamp_job_save(&J,index,tx,r);}
int cf_actor_preamp_job_load(unsigned index){return cf_preamp_job_load(&J,index);}
int cf_actor_preamp_job_pump(void){return cf_preamp_job_pump(&J);}
unsigned cf_actor_preamp_job_phase(void){return J.phase;}
unsigned cf_actor_preamp_last_transaction(void){return cf_preamp_presets_idle(&P)&&P.have_best?P.transaction:0;}
unsigned cf_actor_preamp_last_index(void){return cf_preamp_presets_idle(&P)&&P.have_best?P.last_preset:13;}
int cf_actor_preamp_saved(unsigned tx,unsigned index,void *out){
    if(!out||!cf_preamp_presets_idle(&P)||!P.have_best||P.transaction!=tx||P.last_preset!=index||J.interrupted||!J.offset||J.phase==1)return CF_STALE;
    return cf_preamp_presets_get(&P,index,J.rolling^0xffffffffu,(CfPreampRecord*)out);
}
'''%(audio['symbols']['cf_preamp_request']['address']|1),encoding='utf-8')
        sources.extend([ROOT.parent/'boost-lab/preset_store.c',ROOT.parent/'boost-lab/preset_job.c',preset])
    if options.owner_loop:
        s=preset.read_text()
        s=s.replace('void cf_owner_native_request(void){cf_preamp_job_native_activity(&J);}','extern void cf_preamp_owner_native(const unsigned*);\nextern int cf_preamp_owner_apply_for(unsigned,const void*);\nstatic int context_apply(const void *r){return cf_preamp_owner_apply_for(J.index,r);}\nvoid cf_owner_native_request(const unsigned *request){cf_preamp_job_native_activity(&J);cf_preamp_owner_native(request);}\nvoid cf_actor_preamp_job_invalidate(void){cf_preamp_job_native_activity(&J);}\nunsigned cf_actor_preamp_job_interrupted(void){return J.interrupted;}')
        s=s.replace('(CfApplyPreamp)%du'%(audio['symbols']['cf_preamp_request']['address']|1),'context_apply');preset.write_text(s,encoding='utf-8')
        owner=out/'owner_addresses.h'
        owner.write_text('/* Pinned DSP binding, generated locally. */\n'+''.join('#define %s %du\n'%(name,audio['symbols'][symbol]['address']|1) for name,symbol in [('CF_TRIAL_BEGIN_ADDRESS','trial_begin'),('CF_AUDIO_IS_OUTER_ADDRESS','cf_audio_is_outer'),('CF_PREAMP_REQUEST_ADDRESS','cf_preamp_request'),('CF_AUDIO_DISCONTINUITY_ADDRESS','cf_audio_discontinuity'),('CF_AUDIO_TIMING_ADDRESS','cf_audio_timing')]))
        sources.extend([ROOT/'owner_loop.c',ROOT/'preamp_owner.c'])
    for i,source in enumerate(sources):
        obj=out/f'{i}.o';objects.append(obj)
        cmd=[zig,'cc','-target','thumb-freestanding-eabihf','-mcpu=cortex_m7+fp_armv8d16sp','-Os','-ffreestanding','-fno-builtin','-fno-stack-protector','-Wall','-Wextra','-Werror','-I',ROOT,'-I',out,'-I',ROOT.parent/'boost-lab','-c',source,'-o',obj]
        p=subprocess.run(list(map(str,cmd)),capture_output=True,text=True,env=env,timeout=120);(out/f'compile-{i}.log').write_text(p.stdout+p.stderr);assert p.returncode==0,p.stdout+p.stderr
    cmd=[zig,'build-exe',*objects,'-target','thumb-freestanding-eabihf','-mcpu','cortex_m7+fp_armv8d16sp','--script',linker,'-fentry=cf_fs_dispatch','--no-gc-sections','-fno-strip','-fno-compiler-rt','-O','ReleaseSmall','-femit-bin='+str(out/'actor.elf')]
    p=subprocess.run(list(map(str,cmd)),capture_output=True,text=True,env=env,timeout=120);(out/'link.log').write_text(p.stdout+p.stderr);assert p.returncode==0,p.stdout+p.stderr
    segments,symbols=elf(out/'actor.elf');assert not [name for name,s in symbols.items() if s['index']==0 and s['info']>>4 in (1,2)]
    code=next(s for s in segments if s['flags']&1);bss=next(s for s in segments if s['address']==0x80860000)
    code_limit=0x8087c000 if options.owner_loop else 0x80878000
    assert code['address']+code['size']<=code_limit and bss['size']<=(6144 if options.preamp else 4096)
    if options.owner_loop:assert code['size']<=0x6000,'Actor flash load overlaps the immutable seed manifest'
    assert hashlib.sha256((ROOT/'actor-build/actor.elf').read_bytes()).hexdigest()==prior['elf_sha256']
    # Include transitive sources/headers even when a generated translation unit
    # includes them rather than passing them directly to the compiler.
    dependencies=[original,ROOT/'asset_store.c',ROOT/'build_recovery_actor.py',linker]
    dependencies.extend(ROOT.glob('*.h'))
    if options.leased:dependencies.extend([ROOT/'ir_handoff.c',ROOT/'derive_leased_ir.py'])
    if options.coordinator:dependencies.extend([ROOT.parent/'dual-lab/lease-build/build.json',image])
    if options.preamp:dependencies.extend((ROOT.parent/'boost-lab').glob('preset*.h'))
    if options.owner_loop:dependencies.append(owner)
    report=dict(compiled=True,hardware_accessed=False,firmware_created=False,original_actor_preserved=True,addresses_are_emulator_reservations=True,leased_staging=options.leased,coordinator=options.coordinator,preamp_presets=options.preamp,identity_query=options.identity,immutable_seeds=options.seeded,native_owner_loop=options.owner_loop,code_limit=hex(code_limit),elf_sha256=hashlib.sha256((out/'actor.elf').read_bytes()).hexdigest(),text_bytes=code['size'],bss_bytes=bss['size'],
                source_sha256={str(p.relative_to(TV)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(set(sources+dependencies))})
    (out/'build.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':main()
