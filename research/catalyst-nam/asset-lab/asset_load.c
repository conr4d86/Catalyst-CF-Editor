/* SPDX-License-Identifier: MIT. No device address, allocation or audio IO. */
#include "asset_load.h"
static int finish(CfAssetLoad *l,int result){l->store->phase=0;l->ready=result==CF_OK;return result;}
int cf_load_begin(CfAssetLoad *l,CfAssetStore *s,unsigned slot,void *out,unsigned capacity){
    if(!l||!s||!out||((uintptr_t)out&3u)||slot>=CF_ASSET_SLOTS)return CF_BAD_INPUT;
    if(s->phase)return CF_BUSY;
    unsigned needed=slot<3?25088u:7772u;if(capacity<needed)return CF_BAD_INPUT;
    CfAssetMeta meta;int rc=cf_asset_recover(s,slot,&meta);if(rc)return rc;
    l->store=s;l->meta=meta;l->destination=out;l->capacity=capacity;l->slot=slot;
    l->offset=l->ready=0;l->rolling=0xffffffffu;
    s->phase=3; /* Dedicated serialized read lease, not a USB transaction. */
    return CF_PENDING;
}
int cf_load_pump(CfAssetLoad *l){
    if(!l||!l->store||l->store->phase!=3||l->ready||!l->destination||l->offset>l->meta.length||l->meta.length>l->capacity)return CF_BAD_INPUT;
    unsigned n=l->meta.length-l->offset;if(n>CF_CHUNK_BYTES)n=CF_CHUNK_BYTES;
    if(n){
        if(l->store->io.read(l->store->io.user,l->slot,l->meta.bank,CF_HEADER_BYTES+l->offset,l->destination+l->offset,n))return finish(l,CF_IO);
        l->rolling=cf_crc32(l->rolling,l->destination+l->offset,n);l->offset+=n;
        return CF_PENDING;
    }
    if((l->rolling^0xffffffffu)!=l->meta.crc)return finish(l,CF_INVALID_DATA);
    return finish(l,CF_OK);
}
int cf_load_cancel(CfAssetLoad *l){
    if(!l||!l->store||l->store->phase!=3)return CF_BAD_INPUT;
    finish(l,CF_BAD_INPUT);return CF_OK; /* No partial buffer is declared ready. */
}
