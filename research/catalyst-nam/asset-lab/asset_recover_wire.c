/* SPDX-License-Identifier: MIT. Conditional operation; no addresses from host. */
#include "asset_recover_wire.h"
#include "asset_recover.h"
static uint32_t read32(const uint8_t *p){return (uint32_t)p[0]|((uint32_t)p[1]<<8)|((uint32_t)p[2]<<16)|((uint32_t)p[3]<<24);}
static void write32(uint8_t *p,uint32_t n){for(unsigned i=0;i<4;++i)p[i]=(uint8_t)(n>>(8*i));}
int cf_recovery_receiver_init(CfAssetReceiver *r,CfAssetStore *s,uint32_t flags){
    if(flags&~(CF_DATA_WRITE|CF_RECOVER_RECEIVING))return CF_BAD_INPUT;
    if((flags&CF_RECOVER_RECEIVING)&&!(flags&CF_DATA_WRITE))return CF_BAD_INPUT;
    int rc=cf_receiver_init(r,s,flags&CF_DATA_WRITE);
    if(rc==CF_OK)r->capabilities=flags;
    return rc;
}
int cf_recovery_receiver_handle(CfAssetReceiver *r,const CfWireMessage *in,CfWireMessage *out){
    if(!r||!r->store||!in||!out)return CF_BAD_INPUT;
    if(in->opcode!=CF_ABANDON_RECEIVING)return cf_receiver_handle(r,in,out);
    CfAssetStore *s=r->store;int status=CF_BAD_INPUT;
    if(in->product==s->product&&in->transaction&&in->slot<CF_ASSET_SLOTS&&
       in->session&&in->session==r->session&&
       (r->capabilities&(CF_DATA_WRITE|CF_RECOVER_RECEIVING))==(CF_DATA_WRITE|CF_RECOVER_RECEIVING)&&
       !in->number&&in->bytes==12+sizeof(CfAssetInfo)){
        CfAssetMeta expected;expected.generation=read32(in->data);
        expected.transaction=in->transaction;expected.length=read32(in->data+4);
        expected.crc=read32(in->data+8);expected.bank=0;
        for(unsigned i=0;i<sizeof(expected.info);++i)((uint8_t*)&expected.info)[i]=in->data[12+i];
        status=cf_asset_abandon_receiving(s,in->product,in->slot,&expected);
    }
    *out=*in;out->opcode=CF_ABANDON_RECEIVING|64u;out->bytes=8;
    write32(out->data,(uint32_t)status);write32(out->data+4,0);
    r->last_transaction=in->transaction;r->last_status=(uint32_t)status;return status;
}
