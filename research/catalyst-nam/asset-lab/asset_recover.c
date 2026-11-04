/* SPDX-License-Identifier: MIT. Deliberately separate from the live protocol. */
#include "asset_recover.h"
int cf_asset_abandon_receiving(CfAssetStore *s,uint32_t product,unsigned slot,
                              const CfAssetMeta *expected){
    if(!s||!expected||product!=s->product||slot>=CF_ASSET_SLOTS||
       !expected->transaction||!expected->generation)return CF_BAD_INPUT;
    if(s->phase==2||s->phase==3)return CF_BUSY;
    if(s->phase!=1||s->slot!=slot||s->transaction!=expected->transaction||
       s->generation!=expected->generation||s->length!=expected->length||
       s->expected!=expected->crc)return CF_STALE;
    const uint8_t *a=(const uint8_t*)&s->info,*b=(const uint8_t*)&expected->info;
    for(unsigned i=0;i<sizeof(s->info);++i)if(a[i]!=b[i])return CF_STALE;
    /* No scheduling point or IO between the phase check and cancellation.
     * Safety depends on the serialized control-owner contract above. */
    return cf_asset_cancel(s,expected->transaction);
}
