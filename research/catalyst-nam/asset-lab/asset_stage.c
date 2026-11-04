/* SPDX-License-Identifier: MIT.
 * Optional store translation unit: compile this INSTEAD OF asset_store.c.
 * Including the pinned store shares its exact header/float validators without
 * exporting or duplicating a subtly different validation policy. The existing
 * store source and the baseline actor binary remain unchanged. */
#include "asset_store.c"
#include "asset_stage.h"
static int stage_finish(CfAssetStage *l,int result){
    l->store->phase=0;l->active=0;l->ready=result==CF_OK;return result;
}
int cf_stage_begin(CfAssetStage *l,CfAssetStore *s,unsigned slot,void *out,unsigned capacity){
    if(!l||!s||!out||((uintptr_t)out&3u)||slot>=CF_ASSET_SLOTS||capacity<length_for(slot))return CF_BAD_INPUT;
    if(s->phase)return CF_BUSY;
    clear(l,sizeof(*l));l->store=s;l->slot=slot;l->destination=out;l->capacity=capacity;
    l->active=1;s->phase=3;return CF_PENDING;
}
static int next_bank(CfAssetStage *l,int valid){
    if(valid){
        CfAssetMeta *a=&l->best,*b=&l->candidate;
        if(l->have_best&&a->generation==b->generation&&
           (a->crc!=b->crc||a->transaction!=b->transaction||!equal(&a->info,&b->info,sizeof(a->info))))
            return stage_finish(l,CF_INVALID_DATA);
        if(!l->have_best||b->generation>a->generation){*a=*b;l->have_best=1;}
    }
    ++l->bank;l->step=0;l->offset=0;
    if(l->bank<2)return CF_PENDING;
    if(!l->have_best)return stage_finish(l,CF_EMPTY);
    l->step=2;l->rolling=0xffffffffu;return CF_PENDING;
}
int cf_stage_pump(CfAssetStage *l){
    if(!l||!l->active||!l->store||l->store->phase!=3||l->ready)return CF_BAD_INPUT;
    CfAssetStore *s=l->store;
    if(l->step==0){
        if(l->bank>=2)return stage_finish(l,CF_BAD_INPUT);
        if(s->io.read(s->io.user,l->slot,l->bank,0,s->scratch,CF_HEADER_BYTES))return stage_finish(l,CF_IO);
        if(!header_ok(s,l->slot,s->scratch,&l->candidate))return next_bank(l,0);
        l->candidate.bank=l->bank;l->offset=0;l->rolling=0xffffffffu;l->nonzero=0;l->step=1;
        return CF_PENDING;
    }
    CfAssetMeta *m=l->step==1?&l->candidate:&l->best;
    if(l->step>2||l->offset>m->length||m->length>l->capacity)return stage_finish(l,CF_BAD_INPUT);
    unsigned n=m->length-l->offset;if(n>CF_CHUNK_BYTES)n=CF_CHUNK_BYTES;
    if(n){
        uint8_t *out=l->step==1?s->scratch:l->destination+l->offset;
        if(s->io.read(s->io.user,l->slot,m->bank,CF_HEADER_BYTES+l->offset,out,n))return stage_finish(l,CF_IO);
        if(!floats_ok(out,n,&l->nonzero))return l->step==1?next_bank(l,0):stage_finish(l,CF_INVALID_DATA);
        l->rolling=cf_crc32(l->rolling,out,n);l->offset+=n;return CF_PENDING;
    }
    int valid=l->nonzero&&(l->rolling^0xffffffffu)==m->crc;
    if(l->step==1)return next_bank(l,valid);
    return stage_finish(l,valid?CF_OK:CF_INVALID_DATA);
}
int cf_stage_cancel(CfAssetStage *l){
    if(!l||!l->active||!l->store||l->store->phase!=3)return CF_BAD_INPUT;
    stage_finish(l,CF_BAD_INPUT);return CF_OK;
}
unsigned cf_stage_bytes(void){return sizeof(CfAssetStage);}
