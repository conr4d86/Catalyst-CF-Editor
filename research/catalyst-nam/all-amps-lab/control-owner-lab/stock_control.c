/* SPDX-License-Identifier: MIT.
 * Native graph mutation belongs ONLY to the original DSP/control owner.
 * No filesystem API, heap, flash write or audio callback is used here.
 * The trusted DSP queue-wait wrapper services the latest mailbox; there is
 * no host-supplied node pointer or stale queued pointer/payload to replay.
 */
#include "stock_binding.h"
#include "asset_wire.h"
#define U32(a) (*(volatile unsigned*)(a))
#define RUNTIME 0x20064900u
#define MAGIC 0x324d414eu
typedef struct {unsigned node,arena,graph,entry,original_input,dirty,native;float level,input[32];} Stock;
#define S ((volatile Stock*)CF_STOCK_STATE_ADDRESS)
static struct {unsigned epoch,node,arena,graph,definition,model,known,completed,requested,applied,service_calls,blocked_context,native_completed,native_active,wait_calls,wait_polls,wait_messages,min_sp,unexpected_receive,loading,release_fault;} C;
static const unsigned char switch_ids[12]=CF_STOCK_SWITCH_IDS;
static void barrier(void){__asm__ volatile("dmb":::"memory");}
static unsigned ipsr(void){unsigned v;__asm__ volatile("mrs %0,ipsr":"=r"(v));return v;}
static int main_context(void){unsigned sp;__asm__ volatile("mov %0,sp":"=r"(sp));return !ipsr()&&sp>=0x80806370u&&sp<=0x80806b70u;}
static int readable(unsigned a,unsigned n){return !(a&3u)&&a>=0x20000000u&&a<=0x20050000u-n;}
static int table_readable(unsigned a,unsigned n){return readable(a,n)||(!(a&3u)&&a>=0x80000000u&&a<=0x8085ebbcu-n);}
static unsigned model(const unsigned *node){
    if(U32(RUNTIME)!=MAGIC||!readable((unsigned)node,24)||!(node[3]&1u)||!readable(node[5],28))return 0;
    return ((unsigned(*)(const unsigned*))CF_FACTORY_AMP_ADDRESS)((const unsigned*)node[5]);
}
/* Read the same complete publication consumed by the audio owner. A pending
 * complete mode takes precedence so the native switch is masked immediately. */
static unsigned mode(unsigned *generation){
    unsigned first=U32(RUNTIME+CF_SEQUENCE_OFFSET);barrier();
    if(first&1u)return 4;
    unsigned value=first?U32(RUNTIME+CF_PENDING_OFFSET):U32(RUNTIME+CF_PEDAL_MODE_OFFSET);
    barrier();if(first!=U32(RUNTIME+CF_SEQUENCE_OFFSET)||value>3u)return 4;
    *generation=first;return value;
}
static void bind(unsigned arena,const unsigned *node,unsigned m){
    unsigned graph=node[5],definition=U32(graph+20);
    if(C.node!=(unsigned)node||C.arena!=arena||C.graph!=graph||C.definition!=definition||C.model!=m){
        C.known=0;S->native=2;C.node=(unsigned)node;C.arena=arena;C.graph=graph;C.definition=definition;C.model=m;
    }
    __atomic_add_fetch(&C.epoch,1u,__ATOMIC_SEQ_CST);if(!C.epoch)__atomic_add_fetch(&C.epoch,1u,__ATOMIC_SEQ_CST);
}
/* Called before the original setter, on its normal native control caller.
 * The caller owns `actual` on its stack and preserves the original pointer
 * for the post-observer; no caller-owned payload is modified. */
const unsigned *cf_stock_prepare(unsigned arena,const unsigned *node,unsigned param,const unsigned *data,unsigned *actual){
    if(!data||!actual)return data;*actual=*data;
    unsigned m=model(node);if(!m||m>12u)return data;bind(arena,node,m);
    if(param!=switch_ids[m-1])return data;
    unsigned generation=0,value=mode(&generation);(void)generation;
    if(value>0u&&value<4u){*actual=0;return actual;}
    return data;
}
/* Wrapper around native PUBLIC loader0x80048d78. The original prologue is
 * relocated verbatim; arguments and return status pass through unchanged.
 * Invalidate even identical-address/model reuse and unrelated FX loads. */
extern int cf_stock_load_original(unsigned,const void*,unsigned,unsigned);
int cf_stock_model_load(unsigned object,const void *data,unsigned handle,unsigned bytes){
    unsigned context=U32(0x20026378u+0x6cu),tracked=readable(context,0x164u)&&object==context+8u;
    if(tracked){__atomic_add_fetch(&C.loading,1u,__ATOMIC_SEQ_CST);__atomic_add_fetch(&C.epoch,1u,__ATOMIC_SEQ_CST);S->native=2;C.known=0;barrier();}
    int rc=cf_stock_load_original(object,data,handle,bytes);
    if(tracked){barrier();__atomic_sub_fetch(&C.loading,1u,__ATOMIC_SEQ_CST);}
    return rc;
}
/* Resolve the selected Amp descriptor exactly as native 0x8004ba68 does.
 * All pointer/slot checks and node resolution run WITH the external DSP mutex
 * held, the same mutex used by public setter0x80048b44 and loader0x80048d78.
 * Never retain a queued node address across load/unload or an allocator reuse. */
static int active_amp(unsigned object,unsigned *arena,unsigned *node){
    unsigned context=U32(0x20026378u+0x6cu);
    if(!readable(context,0x164u)||object!=context+8u)return 0;
    unsigned current_arena=U32(object+0xdcu);
    if(current_arena!=context+0x160u||!readable(current_arena,0x150u))return 0;
    unsigned descriptor=U32(0x80807458u);
    if(!readable(descriptor,40u))return 0;
    unsigned indirect=U32(descriptor),slot=U32(descriptor+4u);
    if(!readable(indirect,4u))return 0;
    unsigned stream=U32(indirect);
    if(!readable(stream,32u)||U32(stream+4u)!=object)return 0;
    unsigned count=U32(stream+0x18u),table=U32(stream+0x1cu);
    if(!count||count>256u||slot>=count||!table_readable(table,12u*count))return 0;
    unsigned entry=table+12u*slot;
    if(!*(volatile unsigned char*)(entry+8u))return 0;
    unsigned handle=U32(entry+4u);
    unsigned current_node=((unsigned(*)(unsigned,unsigned))0x80046d3du)(current_arena,handle);
    if(!readable(current_node,28u))return 0;
    *arena=current_arena;*node=current_node;return 1;
}
/* No native graph function is invoked from audio or FS. The trusted DSP
 * control task tries its external native mutex WITHOUT waiting, resolves the
 * CURRENT Amp slot after acquisition, validates the publication, then calls
 * the original setter with exactly the native outer->arena lock order. */
void cf_stock_main_service(void){
    if(!main_context()){++C.blocked_context;return;}
    if(C.loading||C.release_fault)return;
    unsigned context=U32(0x20026378u+0x6cu);
    if(!readable(context,0x164u))return;
    unsigned object=context+8u;
    if(((unsigned(*)(unsigned,unsigned))0x6c29u)(object,0u)!=1u)return;
    unsigned node=0,arena=0,epoch=C.epoch;
    if(C.loading||!active_amp(object,&arena,&node))goto release;
    unsigned graph=U32(node+20u),m=model((const unsigned*)node);
    if(!m||m>12u)goto release;
    /* Rebind from the authoritative active Amp descriptor while disposal is
     * excluded. No scalar setter callback is required after an FX-only load. */
    C.node=node;C.arena=arena;C.graph=graph;C.definition=U32(graph+20u);C.model=m;
    S->node=node;S->arena=arena;C.known=1;
    unsigned native=(U32(graph+24u+4u*(switch_ids[m-1]>>5))>>(switch_ids[m-1]&31u))&1u;
    S->native=native;
    unsigned sequence=0,value=mode(&sequence);if(value>3u)goto release;
    unsigned desired=value?0u:U32(RUNTIME+CF_BOOST_ENABLED_OFFSET);
    desired=desired!=0;C.requested=sequence;
    if(S->native!=desired){
        ++C.service_calls;C.native_active=1;barrier();
        int rc=((int(*)(unsigned,unsigned,unsigned,const void*))0x17f81u)(arena,node,switch_ids[m-1],&desired);
        barrier();C.native_active=0;++C.native_completed;
        if(rc<0)goto release;
        /* The outer mutex excludes native dispose/reuse. Still reject newer
         * configuration generations; they are reconciled by the next poll. */
        if(C.loading||epoch!=C.epoch||node!=C.node||arena!=C.arena||graph!=U32(node+20u)||m!=model((const unsigned*)node))goto release;
        S->native=desired;barrier();
    }
    unsigned current=0;if(!C.loading&&epoch==C.epoch&&mode(&current)==value&&current==sequence){C.completed=epoch;C.applied=sequence;S->dirty=0;}
release:
    if(((unsigned(*)(unsigned,unsigned,unsigned,unsigned))0x646du)(object,0u,0u,0u)!=1u){C.release_fault=1;C.native_active=2;C.known=0;S->native=2;barrier();}
}
/* The wrapper supplies the ORIGINAL requested data and the word actually
 * given to the native setter separately, preserving requested Boost ON/OFF. */
void cf_stock_observe(unsigned arena,const unsigned *node,unsigned param,const unsigned *data,int rc,unsigned actual){
    /* Native callers hold the external mutex. Ignore controls on inactive
     * implementation graphs so they cannot overwrite the active Boost intent. */
    unsigned context=U32(0x20026378u+0x6cu),current_arena=0,current_node=0;
    if(readable(context,0x164u)&&active_amp(context+8u,&current_arena,&current_node)&&
       (current_arena!=arena||current_node!=(unsigned)node))return;
    ((void(*)(unsigned,const unsigned*,unsigned,const unsigned*,int))CF_TRIAL_NODE_CONTROL_ADDRESS)(arena,node,param,data,rc);
    if(rc<0)return;unsigned m=model(node);if(!m||m>12u||C.node!=(unsigned)node||C.arena!=arena)return;
    if(param==switch_ids[m-1]){S->native=actual!=0;C.known=1;barrier();}
    /* No native mutation here. Asynchronous restore/mode changes are serviced
     * only by the proven DspSchedulerCtrlTask queue wait below, outside native handlers. */
}
unsigned cf_stock_control_stat(unsigned i){switch(i){case 0:return C.epoch;case 1:return C.completed;case 2:return C.requested;case 3:return C.applied;case 4:return C.service_calls;case 5:return C.blocked_context;case 6:return C.known;default:return 0xffffffffu;}}
/* Exact original DspSchedulerCtrlTask call site: 0x8000b9b0. Its caller ignores the
 * queue return code and always dispatches the buffer. Therefore a bounded
 * timeout is NEVER returned to it: service and retry internally until a real
 * message arrives. Preserve finite/foreign calls exactly. No synthetic opcode.
 * DSP task provenance: podgo-review/DSP_CONTROL_OWNER.md and dsp_owner_review.py. */
unsigned cf_stock_main_wait(unsigned queue,unsigned *message,unsigned timeout){
    unsigned sp;__asm__ volatile("mov %0,sp":"=r"(sp));
    if(!message||!main_context()||queue!=U32(0x80806bd4u)||timeout!=0xffffffffu)
        return ((unsigned(*)(unsigned,void*,unsigned))0x8004c8f9u)(queue,message,timeout);
    ++C.wait_calls;if(!C.min_sp||sp<C.min_sp)C.min_sp=sp;
    for(;;){
        cf_stock_main_service();
        unsigned rc=((unsigned(*)(unsigned,void*,unsigned))0x8004c8f9u)(queue,message,5);
        if(rc==1u){++C.wait_messages;return rc;}
        if(rc!=0u){++C.unexpected_receive;((void(*)(unsigned))0x589du)(1u);} /* Proven native vTaskDelay: no error hot-spin. */
        ++C.wait_polls;
    }
}
/* Fixed stateless snapshot, not a raw memory/register reader or audio ACK. */
int cf_stock_control_diagnostic(CfAssetReceiver *receiver,const CfWireMessage *in,CfWireMessage *out){
    if(!receiver||!receiver->store||!in||!out)return CF_BAD_INPUT;
    *out=*in;out->opcode=in->opcode|64u;out->bytes=4;
    unsigned status=CF_BAD_INPUT;
    if(in->product==receiver->store->product&&!in->session&&in->transaction&&!in->slot&&!in->number&&!in->bytes){
        unsigned words[21]={CF_OK,1u,C.wait_calls,C.wait_polls,C.wait_messages,C.min_sp,C.epoch,C.completed,C.requested,C.applied,C.service_calls,C.native_completed,C.native_active,C.blocked_context,C.known,C.node,C.arena,C.graph,C.model,S->native,C.unexpected_receive};
        out->bytes=sizeof(words);for(unsigned i=0;i<sizeof(words);++i)out->data[i]=((const unsigned char*)words)[i];return CF_OK;
    }
    for(unsigned i=0;i<4;++i)out->data[i]=(unsigned char)(status>>(i*8));return (int)status;
}
