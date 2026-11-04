/* SPDX-License-Identifier: MIT. Native FS-owner integration, laboratory only.
 * This file has no PC USB APIs. The read-only MCU and native ABI addresses
 * are tied to the pinned application, not to addresses supplied by packets.
 * Installation still requires boot-copy/zero, cache/IRQ and physical review. */
#include "asset_seed.h"
#include "asset_wire.h"
#include "asset_identity.h"
#include "asset_coordinator.h"
#include "ir_handoff_leased.h"
#include "owner_addresses.h"

#define FS_OBJECT 0x8080bfb4u
#define LFS_ADDRESS (FS_OBJECT+0x9e8u)
#define CFG_ADDRESS (FS_OBJECT+0xa60u)
#define FS_QUEUE (*(volatile unsigned*)(FS_OBJECT+0x878u))
#define U8(a) (*(volatile uint8_t*)(a))
#define U32(a) (*(volatile uint32_t*)(a))
#define ACTOR_MAGIC 0x31414643u
#define SEED_ADDRESS 0x601e3000u
extern unsigned __cf_actor_bss_end;
extern int cf_actor_init(unsigned,unsigned,unsigned,unsigned);
extern int cf_actor_bind_seed(const CfSeedManifest*);
extern int cf_actor_bind_identity(unsigned,unsigned);
extern unsigned cf_actor_store_address(void);
extern int cf_coord_bind(unsigned,unsigned,unsigned,unsigned,unsigned);
extern int cf_coord_start(unsigned,unsigned);
extern int cf_coord_step(void);
extern unsigned cf_coord_stat(unsigned);
extern void cf_actor_suspend(void);
extern int cf_preamp_owner_init(void);
extern void cf_preamp_owner_step(void),cf_preamp_owner_audio(void);
extern unsigned cf_preamp_owner_stat(unsigned);

static struct {unsigned ready,fault,slot,graph,dirty,seen;volatile unsigned selection,audio_selection;unsigned services,audio_blocks,min_fs_sp;volatile unsigned ir_events,ir_instance,ir_mode,ir_data,ir_node,ir_graph,ir_slot,ir_reject;} O;
static void barrier(void){__asm__ volatile("dmb":::"memory");}
static int owner_stack(void){unsigned sp;__asm__ volatile("mov %0,sp":"=r"(sp));return sp>=FS_OBJECT+0x74u&&sp<=FS_OBJECT+0x874u;}
static unsigned align32(unsigned a){return (a+31u)&~31u;}
static int mounted(void){return U8(FS_OBJECT)==1&&U8(FS_OBJECT+1)==1&&FS_QUEUE&&U32(LFS_ADDRESS+0x68)==CFG_ADDRESS;}
/* Called only on the original FileSystem task, after mount and native model
 * identification are available. A caller's product number is never trusted. */
int cf_owner_bootstrap(void){
    if(!owner_stack())return CF_BAD_INPUT;if(O.fault)return (int)O.fault;if(O.ready)return CF_OK;if(!mounted())return CF_PENDING;
    uint8_t model[2]={0,0};int rc=((int(*)(void*,unsigned))0x8001d6e5u)(model,2);
    if(rc)return CF_PENDING;if(model[0]<1||model[0]>3||model[1])return CF_BAD_INPUT;
    /* The pinned board already accesses CCM CCGR2. Do not touch OCOTP shadow
     * registers while its CG6 clock is gated or has a reserved mode. The
     * control owner can retry later; this never enables/programs any clock. */
    unsigned otp_clock=(U32(0x400fc070u)>>12)&3u;
    if(otp_clock!=1u&&otp_clock!=3u)return CF_PENDING;
    /* Read shadow values only; never program/reload fuses or alter clocks.
     * The platform is still unverified on physical hardware. */
    uint32_t uid[2]={U32(0x401f4410u),U32(0x401f4420u)};if(!cf_identity_valid(uid))return CF_INVALID_DATA;
    unsigned product=0x250000u|model[0],first=align32((unsigned)&__cf_actor_bss_end);
    unsigned n1a=first,n1b=align32(n1a+7772),n2a=align32(n1b+7772),n2b=align32(n2a+7772),ir=align32(n2b+7772);
    if(ir+25088>0x80872000u)return CF_BAD_INPUT;
    const CfSeedManifest *seed=(const CfSeedManifest*)SEED_ADDRESS;
    /* Validate trusted ROM metadata before publishing actor readiness. */
    if(seed->magic!=CF_SEED_MAGIC||seed->version!=1||seed->count!=5||seed->mask>31)return CF_INVALID_DATA;
    rc=cf_actor_init(product,LFS_ADDRESS,FS_QUEUE,0x800174f5u);if(rc)return rc;
    if((rc=cf_actor_bind_seed(seed))||(rc=cf_actor_bind_identity(uid[0],uid[1]))||
       (rc=cf_ir_init(0,0,0,0))||(rc=cf_coord_bind(ir,n1a,n1b,n2a,n2b))||(rc=cf_preamp_owner_init())){O.fault=(unsigned)rc;cf_actor_suspend();return rc;}
    O.slot=3;O.graph=0;O.dirty=24;barrier();O.ready=1;return CF_OK;
}
/* Native queue-receive replacement at the FS call site only. A bounded wait
 * creates an explicit no-op/service message on timeout; it never replays the
 * previous native read/write request. The timeout is five native RTOS ticks. */
unsigned cf_fs_wait(unsigned queue,unsigned *message,unsigned timeout){
    if(!message||queue!=FS_QUEUE||!owner_stack())return ((unsigned(*)(unsigned,void*,unsigned))0x8004c8f9u)(queue,message,timeout);
    unsigned rc=((unsigned(*)(unsigned,void*,unsigned))0x8004c8f9u)(queue,message,5);
    if(rc!=1){for(unsigned i=0;i<6;++i)message[i]=0;message[0]=8;message[1]=ACTOR_MAGIC;}
    return rc;
}
void cf_owner_committed(unsigned slot){if(O.ready&&slot<5)O.dirty|=1u<<slot;}
/* A single 32-bit word atomically describes the graph, slot and revision.
 * This callback performs no IO and never touches the active FFT workspace.
 * Graphs are aligned DTCM pointers from the pinned native instance, not USB. */
/* Native metadata may be in the pinned read-only flash or SDRAM image; it is
 * not necessarily in the DTCM audio workspace. Only the graph passed to the
 * audio handoff must be DTCM. These bounds are fixed, never provided by USB. */
static int native_readable(unsigned address,unsigned bytes){
    if((address&3u)||!bytes||address+bytes<address)return 0;
    return (address>=0x20000000u&&address+bytes<=0x20050000u)||
           (address>=0x80000000u&&address+bytes<=0x8085ebbcu)||
           (address>=0x60001000u&&address+bytes<=0x60200000u);
}
static void select_native(unsigned instance){
    ++O.ir_events;O.ir_instance=instance;O.ir_mode=O.ir_data=O.ir_node=O.ir_graph=O.ir_slot=0;O.ir_reject=1;
    if(instance<20u||!native_readable(instance-20u,80u))return;
    O.ir_mode=U32(instance+0x38);O.ir_reject=2;if(O.ir_mode!=1)return;
    unsigned data=U32(instance+0x34),node=U32(instance-0x14);
    O.ir_data=data;O.ir_node=node;O.ir_reject=3;if(!native_readable(data,4))return;
    O.ir_reject=4;if(!native_readable(node,24))return;
    unsigned slot=U32(data),graph=U32(node+0x14);
    O.ir_slot=slot;O.ir_graph=graph;O.ir_reject=5;if(slot>=3)return;
    O.ir_reject=6;if((graph&3u)||graph<0x20000000u||graph>0x20050000u-24u)return;
    unsigned fields=((graph-0x20000000u)>>2)|(slot<<17),old=__atomic_load_n(&O.selection,__ATOMIC_ACQUIRE),next;
    do{next=((((old>>19)+1u)&8191u)<<19)|fields;if(!next)next=1u<<19;}
    while(!__atomic_compare_exchange_n(&O.selection,&old,next,0,__ATOMIC_RELEASE,__ATOMIC_ACQUIRE));
    O.ir_reject=0;
}
void cf_actor_ir_set_data(unsigned instance){((void(*)(unsigned))0x800399c5u)(instance);select_native(instance);}
void cf_actor_ir_reset(unsigned instance){((void(*)(unsigned))0x80039a11u)(instance);select_native(instance);}
void cf_owner_step(void){
    if(!owner_stack()||!O.ready||O.fault||!mounted())return;
    unsigned sp;__asm__ volatile("mov %0,sp":"=r"(sp));if(!O.min_fs_sp||sp<O.min_fs_sp)O.min_fs_sp=sp;++O.services;
    CfAssetStore *store=(CfAssetStore*)cf_actor_store_address();
    /* A live reception/commit owns its file buffers until it finishes. */
    if(store->phase==1||store->phase==2)return;
    unsigned selection=__atomic_load_n(&O.selection,__ATOMIC_ACQUIRE);
    /* Wait for the outer audio owner to stop any old preparation before
     * posting a new coordinator request. Otherwise it could cancel the NEW
     * eviction as if it belonged to the superseded selection. */
    if(selection!=O.audio_selection)return;
    if(selection&&selection!=O.seen){
        unsigned slot=(selection>>17)&3u,graph=0x20000000u+((selection&0x1ffffu)<<2);
        O.slot=slot;O.graph=graph;O.seen=selection;O.dirty=(O.dirty&~7u)|(1u<<slot);
    }
    for(unsigned i=0;i<5;++i)if(O.dirty&(1u<<i)){
        if(i<3&&i!=O.slot){O.dirty&=~(1u<<i);continue;}
        int rc=cf_coord_start(i,i<3?O.graph:0);if(rc!=CF_PENDING){O.fault=(unsigned)rc;return;}O.dirty&=~(1u<<i);
    }
    (void)cf_coord_step();if(cf_coord_stat(2))O.fault=cf_coord_stat(3);
    cf_preamp_owner_step();
}
/* This replaces the existing begin thunk without adding FS work to audio.
 * IR preparation runs only at the outermost native dispatch, never nested. */
void cf_actor_audio_begin(unsigned arena,unsigned graph){
    ((void(*)(unsigned,unsigned))CF_TRIAL_BEGIN_ADDRESS)(arena,graph);
    if(((unsigned(*)(void))CF_AUDIO_IS_OUTER_ADDRESS)()){
        ++O.audio_blocks;
        cf_preamp_owner_audio();
        unsigned selection=__atomic_load_n(&O.selection,__ATOMIC_ACQUIRE);
        if(O.ready&&selection&&selection!=O.audio_selection){
            unsigned slot=(selection>>17)&3u,target=0x20000000u+((selection&0x1ffffu)<<2);
            if(cf_ir_supersede(slot,target)==CF_OK){((void(*)(void))CF_AUDIO_DISCONTINUITY_ADDRESS)();barrier();O.audio_selection=selection;}
        }
        if(cf_ir_boundary())((void(*)(void))CF_AUDIO_DISCONTINUITY_ADDRESS)();
    }
}
unsigned cf_owner_stat(unsigned index){switch(index){case 0:return O.ready;case 1:return O.fault;case 2:return O.slot;case 3:return O.graph;case 4:return O.dirty;case 5:return O.selection;case 6:return O.audio_selection;default:return 0xffffffffu;}}

/* Fixed read-only diagnostic snapshot. No caller-supplied address, register
 * selector, flash operation or change to the currently selected MPU region.
 * The FS owner and audio counters are independent liveness observations. */
int cf_owner_diagnostic(CfAssetReceiver *receiver,const CfWireMessage *in,CfWireMessage *out){
    if(!receiver||!in||!out)return CF_BAD_INPUT;
    *out=*in;out->opcode=in->opcode|64u;out->bytes=4;
    unsigned status=CF_BAD_INPUT;
    if(in->product==receiver->store->product&&!in->session&&in->transaction&&!in->slot&&!in->number&&!in->bytes){
        unsigned words[64]={CF_OK,5u,O.ready,O.fault,O.services,O.audio_blocks,O.min_fs_sp,
            cf_coord_stat(0),cf_coord_stat(1),cf_coord_stat(2),cf_coord_stat(3),
            cf_preamp_owner_stat(0),cf_preamp_owner_stat(1),cf_preamp_owner_stat(2),
            U32(0xe000ed14u),U32(0xe000ed94u),
            ((unsigned(*)(unsigned))CF_AUDIO_TIMING_ADDRESS)(24),
            ((unsigned(*)(unsigned))CF_AUDIO_TIMING_ADDRESS)(25),
            ((unsigned(*)(unsigned))CF_AUDIO_TIMING_ADDRESS)(26),
            U32(0x20064900u),U32(0x2006490cu),U32(0x20064918u),U32(0x2006492cu),U32(0x20064930u),
            O.selection,O.audio_selection,U32(0x400fc070u),U32(0x20023498u),
            (unsigned)&__cf_actor_bss_end,0x80872000u,0x80874000u,0x8087c000u,
            O.ir_events,O.ir_instance,O.ir_mode,O.ir_data,O.ir_node,O.ir_graph,O.ir_slot,O.ir_reject};
        for(unsigned i=0;i<24;++i)words[40+i]=((unsigned(*)(unsigned))CF_AUDIO_TIMING_ADDRESS)(i);
        out->bytes=sizeof(words);for(unsigned i=0;i<sizeof(words);++i)out->data[i]=((const uint8_t*)words)[i];return CF_OK;
    }
    for(unsigned i=0;i<4;++i)out->data[i]=(uint8_t)(status>>(i*8));return (int)status;
}
