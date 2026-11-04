/* SPDX-License-Identifier: MIT. No IRQ/filesystem/audio work is mixed here.
 * Only the serialized FS owner calls this API; outer audio boundaries run the
 * handoffs. A stall never licenses buffer reuse or reports audible activation. */
#include "asset_coordinator.h"
enum { IDLE, EVICT_REQUEST,EVICT_WAIT,STAGE_BEGIN,STAGE_WAIT,
       PUBLISH_REQUEST,PUBLISH_WAIT,SELECT_REQUEST,SELECT_WAIT };
static void zero(void *p,unsigned bytes){for(unsigned i=0;i<bytes;++i)((uint8_t*)p)[i]=0;}
static int aligned(const void *p){return p&&!((uintptr_t)p&3u);}
static int failed(CfAssetCoordinator *c,int rc){c->last_status=(unsigned)rc;c->fault=1;return rc;}
int cf_coord_init(CfAssetCoordinator *c,CfAssetStore *s,const CfAssetAudio *a,void *ir,
                  void *n1a,void *n1b,void *n2a,void *n2b){
    if(!c||!s||!a||!a->nam_request||!a->nam_status||s->phase)return CF_BAD_INPUT;
    if(c->store)return CF_ALREADY; /* Zero-initialized, bind-once owner. */
    void *buffers[5]={ir,n1a,n1b,n2a,n2b};unsigned sizes[5]={25088,7772,7772,7772,7772};
    for(unsigned i=0;i<5;++i){
        uintptr_t x=(uintptr_t)buffers[i];if(!aligned(buffers[i])||x+sizes[i]<x)return CF_BAD_INPUT;
        for(unsigned j=0;j<i;++j){uintptr_t y=(uintptr_t)buffers[j];if(x<y+sizes[j]&&y<x+sizes[i])return CF_BAD_INPUT;}
    }
    unsigned current[2]={2,2};
    for(unsigned i=0;i<2;++i)for(unsigned b=0;b<2;++b){
        int rc=a->nam_status(i+1,(unsigned)(uintptr_t)buffers[1+i*2+b]);
        if(rc==CF_OK){if(current[i]!=2)return CF_BAD_INPUT;current[i]=b;}
        else if(rc!=CF_STALE)return rc;
    }
    zero(c,sizeof(*c));c->store=s;c->audio=*a;c->ir=ir;
    c->nam[0][0]=n1a;c->nam[0][1]=n1b;c->nam[1][0]=n2a;c->nam[1][1]=n2b;
    c->current_nam[0]=current[0];c->current_nam[1]=current[1];
    return CF_OK;
}
int cf_coord_request(CfAssetCoordinator *c,unsigned slot,unsigned graph){
    if(!c||!c->store||c->fault||slot>=5||
       (slot<3&&((graph&3u)||graph<0x20000000u||graph>0x20050000u-24u)))return CF_BAD_INPUT;
    if(c->revision[slot]==0xffffffffu)return CF_STALE;
    ++c->revision[slot];if(slot<3){c->dirty&=~7u;c->graph=graph;}
    c->dirty|=1u<<slot;return CF_PENDING;
}
static int outdated(CfAssetCoordinator *c){return c->job_revision!=c->revision[c->slot]||!(c->dirty&(1u<<c->slot));}
static int done(CfAssetCoordinator *c,int rc){
    if(!outdated(c))c->dirty&=~(1u<<c->slot);
    c->phase=IDLE;c->last_status=(unsigned)rc;return rc;
}
static int tx(CfAssetCoordinator *c){
    if(c->next_transaction==0xffffffffu)return failed(c,CF_STALE);
    c->transaction=++c->next_transaction;return CF_OK;
}
int cf_coord_pump(CfAssetCoordinator *c){
    if(!c||!c->store)return CF_BAD_INPUT;if(c->fault)return (int)c->last_status;
    int rc;
    if(c->phase==IDLE){
        if(!c->dirty)return CF_OK;
        for(c->slot=0;c->slot<5&&!(c->dirty&(1u<<c->slot));++c->slot){}
        c->job_revision=c->revision[c->slot];c->job_graph=c->graph;
        if(c->slot<3){if(tx(c))return (int)c->last_status;c->phase=EVICT_REQUEST;}
        else{c->spare=c->current_nam[c->slot-3]==0?1:0;c->phase=STAGE_BEGIN;}
        return CF_PENDING;
    }
    if(c->phase==EVICT_REQUEST){
        rc=cf_ir_evict_request(c->transaction,(unsigned)(uintptr_t)(c->ir+8192));
        if(rc==CF_BUSY)return CF_PENDING;if(rc!=CF_OK&&rc!=CF_ALREADY)return failed(c,rc);
        c->phase=EVICT_WAIT;return CF_PENDING;
    }
    if(c->phase==EVICT_WAIT){
        rc=cf_ir_status(c->transaction);if(rc==CF_PENDING)return CF_PENDING;if(rc==CF_STALE&&outdated(c))return done(c,rc);if(rc)return failed(c,rc);
        c->ir_owned=0;c->phase=STAGE_BEGIN;return CF_PENDING;
    }
    if(c->phase==STAGE_BEGIN){
        if(outdated(c))return done(c,CF_STALE);
        void *destination=c->slot<3?c->ir:c->nam[c->slot-3][c->spare];
        rc=cf_stage_begin(&c->stage,c->store,c->slot,destination,c->slot<3?25088:7772);
        if(rc==CF_BUSY)return CF_PENDING;if(rc!=CF_PENDING)return done(c,rc);
        c->phase=STAGE_WAIT;return CF_PENDING;
    }
    if(c->phase==STAGE_WAIT){
        if(outdated(c)){rc=cf_stage_cancel(&c->stage);return rc?failed(c,rc):done(c,CF_STALE);}
        rc=cf_stage_pump(&c->stage);if(rc==CF_PENDING)return rc;if(rc)return done(c,rc);
        if(tx(c))return (int)c->last_status;c->phase=PUBLISH_REQUEST;return CF_PENDING;
    }
    if(c->phase==PUBLISH_REQUEST){
        if(outdated(c))return done(c,CF_STALE);
        if(c->slot<3)rc=cf_ir_bank_request(c->transaction,c->slot,(unsigned)(uintptr_t)(c->ir+8192),16896);
        else{rc=c->audio.nam_request(c->slot-2,(unsigned)(uintptr_t)c->nam[c->slot-3][c->spare],7772);if(rc==-2)return CF_PENDING;if(rc)return failed(c,CF_BAD_INPUT);}
        if(rc==CF_BUSY)return CF_PENDING;if(rc!=CF_OK&&rc!=CF_ALREADY)return failed(c,rc);
        c->phase=PUBLISH_WAIT;return CF_PENDING;
    }
    if(c->phase==PUBLISH_WAIT){
        rc=c->slot<3?cf_ir_status(c->transaction):c->audio.nam_status(c->slot-2,(unsigned)(uintptr_t)c->nam[c->slot-3][c->spare]);
        if(rc==CF_PENDING||rc==CF_BUSY)return CF_PENDING;if(rc==CF_STALE&&c->slot<3&&outdated(c))return done(c,rc);if(rc)return failed(c,rc);
        c->loaded_generation[c->slot]=c->stage.best.generation;
        if(c->slot>=3){c->current_nam[c->slot-3]=c->spare;return done(c,CF_OK);}
        c->ir_owned=1;if(outdated(c))return done(c,CF_STALE);
        if(tx(c))return (int)c->last_status;c->phase=SELECT_REQUEST;return CF_PENDING;
    }
    if(c->phase==SELECT_REQUEST){
        if(outdated(c))return done(c,CF_STALE);
        rc=cf_ir_select_request(c->transaction,c->slot,c->job_graph);
        if(rc==CF_BUSY)return CF_PENDING;if(rc!=CF_OK&&rc!=CF_ALREADY)return failed(c,rc);
        c->phase=SELECT_WAIT;return CF_PENDING;
    }
    if(c->phase==SELECT_WAIT){
        rc=cf_ir_status(c->transaction);if(rc==CF_PENDING)return CF_PENDING;if(rc==CF_STALE&&outdated(c))return done(c,rc);if(rc)return failed(c,rc);
        return done(c,CF_OK);
    }
    return failed(c,CF_BAD_INPUT);
}
unsigned cf_coord_bytes(void){return sizeof(CfAssetCoordinator);}
