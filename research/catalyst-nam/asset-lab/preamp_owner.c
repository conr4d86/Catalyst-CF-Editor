/* SPDX-License-Identifier: MIT. Serialized CF control/preset owner, laboratory.
 * Uses the pinned native bank/channel state and native file CRC binding.
 * No supplied address, raw flash, USB API, heap or audio-side file access. */
#include "asset_wire.h"
#include "preset_job.h"
#include "owner_addresses.h"
extern int cf_actor_preamp_open(void),cf_actor_preamp_pump(void),cf_actor_preamp_job_init(void);
extern int cf_actor_preamp_job_save(unsigned,unsigned,const CfPreampRecord*),cf_actor_preamp_job_load(unsigned),cf_actor_preamp_job_pump(void);
extern unsigned cf_actor_preamp_job_phase(void);
extern unsigned cf_actor_preamp_last_transaction(void),cf_actor_preamp_last_index(void);
extern int cf_actor_preamp_saved(unsigned,unsigned,void*);
extern unsigned cf_actor_preamp_job_interrupted(void);
extern void cf_actor_preamp_job_invalidate(void);
extern unsigned cf_actor_store_address(void);
#define U8(a) (*(volatile uint8_t*)(a))
#define U32(a) (*(volatile uint32_t*)(a))
static struct {
    unsigned initialized,opened,ready,fault,index,revision,loading,reload,save_transaction,save_status,save_revision;
    CfPreampRecord current;
    volatile unsigned sequence;
    CfPreampRecord pending;
    unsigned pending_index;
    unsigned audio_sequence,audio_index;
} P;
static void zero(void *p,unsigned n){for(unsigned i=0;i<n;++i)((uint8_t*)p)[i]=0;}
static void barrier(void){__asm__ volatile("dmb":::"memory");}
static void put(uint8_t *p,unsigned v){for(unsigned i=0;i<4;++i)p[i]=(uint8_t)(v>>(8*i));}
static CfPreampRecord factory(void){CfPreampRecord r;zero(&r,sizeof(r));r.tone_bits=0x3f000000u;r.level_bits=0x3f800000u;return r;}
/* Main's original preset-selection handler uses channel 0=A,1=B,2=Manual
 * and the current bank at these pinned state fields. A changing/invalid
 * snapshot is rejected; a wire packet never selects its own native context. */
static unsigned context(void){
    unsigned bank=U32(0x80808fecu),channel=U8(0x80808fe8u);barrier();
    if(bank!=U32(0x80808fecu)||channel!=U8(0x80808fe8u)||bank>=6||channel>=3)return 13;
    return channel==2?12:bank*2+channel;
}
static void publish(const CfPreampRecord *record){
    P.sequence=(P.sequence+2u)|1u;barrier();P.pending=*record;P.pending_index=P.index;barrier();++P.sequence;
}
int cf_preamp_owner_apply(const void *record){
    if(!record||!P.initialized||P.index>=13||context()!=P.index)return -1;
    P.current=*(const CfPreampRecord*)record;++P.revision;publish(&P.current);return 0;
}
int cf_preamp_owner_apply_for(unsigned index,const void *record){return index==P.index?cf_preamp_owner_apply(record):-1;}
int cf_preamp_owner_init(void){
    if(P.initialized)return CF_ALREADY;P.index=P.audio_index=13;P.current=factory();
    int rc=cf_actor_preamp_open();if(rc!=CF_PENDING)return rc;
    rc=cf_actor_preamp_job_init();if(rc)return rc;P.initialized=1;return CF_OK;
}
void cf_preamp_owner_native(const unsigned *request){
    if(!P.initialized||!request)return;
    unsigned opcode=request[0]&255u,file=request[1]&255u;
    if(opcode==4&&((file>=5&&file<17&&file-5==P.index)||(file==2&&P.index==12)))P.reload=1;
}
void cf_preamp_owner_step(void){
    if(!P.initialized||P.fault)return;
    CfAssetStore *store=(CfAssetStore*)cf_actor_store_address();if(store->phase==1||store->phase==2)return;
    if(!P.opened){int rc=cf_actor_preamp_pump();if(rc==CF_PENDING)return;if(rc){P.fault=(unsigned)rc;return;}P.opened=1;}
    unsigned next=context();if(next>=13){P.ready=0;return;}
    if(next!=P.index){cf_actor_preamp_job_invalidate();P.index=next;++P.revision;P.ready=0;P.reload=1;P.current=factory();publish(&P.current);}
    if(cf_actor_preamp_job_phase()){
        int rc=cf_actor_preamp_job_pump();if(rc==CF_PENDING)return;
        if(P.loading){P.loading=0;P.ready=rc==CF_OK||rc==CF_EMPTY||rc==CF_STALE;if(rc==CF_STALE&&cf_actor_preamp_job_interrupted())P.reload=1;}
        else if(P.save_transaction)P.save_status=(unsigned)rc;
        if(rc!=CF_OK&&rc!=CF_EMPTY&&rc!=CF_STALE){P.fault=(unsigned)rc;P.ready=0;}
        return;
    }
    if(P.reload){int rc=cf_actor_preamp_job_load(P.index);if(rc==CF_PENDING){P.loading=1;P.reload=0;P.ready=0;}else if(rc!=CF_BUSY){P.fault=(unsigned)rc;P.ready=0;}}
}
/* Called only at an outer audio boundary. A pending configuration for the old
 * native preset can never override a newly selected preset. */
void cf_preamp_owner_audio(void){
    if(!P.initialized)return;
    unsigned index=context(),sequence=P.sequence;barrier();
    if(index>=13||sequence&1u)return;
    if(index!=P.index||index!=P.pending_index){
        if(P.audio_index!=13){CfPreampRecord r=factory();if(!((int(*)(const void*))CF_PREAMP_REQUEST_ADDRESS)(&r))P.audio_index=13;}
        return;
    }
    if(P.audio_sequence==sequence&&P.audio_index==index)return;
    CfPreampRecord r=P.pending;barrier();if(sequence!=P.sequence||index!=context())return;
    if(!((int(*)(const void*))CF_PREAMP_REQUEST_ADDRESS)(&r)){P.audio_sequence=sequence;P.audio_index=index;}
}
int cf_preamp_owner_wire(CfAssetReceiver *receiver,const CfWireMessage *in,CfWireMessage *out){
    if(!receiver||!receiver->store||!in||!out)return CF_BAD_INPUT;
    int status=CF_BAD_INPUT;unsigned revision=P.revision,index=context();CfPreampRecord stored;const CfPreampRecord *response=&P.current;
    if(in->product==receiver->store->product&&in->session&&in->session==receiver->session&&in->transaction){
        if(!P.initialized||!P.opened||!P.ready||P.loading||P.fault||index!=P.index)status=CF_BUSY;
        else if(in->opcode==9&&!in->bytes&&!in->number&&!in->slot)status=CF_OK;
        else if(in->opcode==10&&in->slot==index&&in->number==P.revision&&in->bytes==sizeof(CfPreampRecord)){
            CfPreampRecord r;for(unsigned i=0;i<sizeof(r);++i)((uint8_t*)&r)[i]=in->data[i];
            if(cf_preamp_record_valid(&r)&&!r.preset_crc){status=cf_preamp_owner_apply(&r)?CF_STALE:CF_OK;revision=P.revision;}
        }else if(in->opcode==11&&in->slot==index&&in->number==P.revision&&!in->bytes){
            if(in->transaction==P.save_transaction)status=in->number==P.save_revision?(int)P.save_status:CF_STALE;
            else if(!cf_actor_preamp_job_phase()){
                status=cf_actor_preamp_job_save(index,in->transaction,&P.current);
                if(status==CF_PENDING){P.save_transaction=in->transaction;P.save_revision=P.revision;P.save_status=CF_PENDING;}
            }else status=CF_BUSY;
        }else if(in->opcode==12&&!in->bytes&&in->slot==index&&!in->number){
            status=in->transaction==P.save_transaction?(int)P.save_status:CF_OK;
            if(status==CF_OK){status=cf_actor_preamp_saved(in->transaction,index,&stored);if(status==CF_OK)response=&stored;}
        }
    }
    *out=*in;out->opcode=in->opcode|64u;out->bytes=44;put(out->data,(unsigned)status);put(out->data+4,index);put(out->data+8,revision);
    for(unsigned i=0;i<32;++i)out->data[12+i]=status==CF_OK?((const uint8_t*)response)[i]:0;
    /* Input CRC field is never trusted; only the job computes the file CRC. */
    if(status==CF_OK)put(out->data+40,0);
    return status;
}
unsigned cf_preamp_owner_stat(unsigned i){switch(i){case 0:return P.ready;case 1:return P.fault;case 2:return P.index;case 3:return P.revision;case 4:return P.save_status;case 5:return P.opened;default:return 0xffffffffu;}}
