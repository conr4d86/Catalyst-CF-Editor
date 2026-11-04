/* SPDX-License-Identifier: MIT. No storage, MMIO or session mutation. */
#include "asset_identity.h"
static void put(uint8_t *p,uint32_t v){for(unsigned i=0;i<4;++i)p[i]=(uint8_t)(v>>(8*i));}
int cf_identity_valid(const uint32_t uid[2]){
    return uid&&((uid[0]|uid[1])!=0)&&((uid[0]&uid[1])!=0xffffffffu);
}
int cf_identity_reply(const CfAssetReceiver *r,const uint32_t uid[2],const CfWireMessage *in,CfWireMessage *out){
    if(!r||!r->store||!in||!out)return CF_BAD_INPUT;
    int rc=CF_BAD_INPUT;
    if(in->opcode==CF_IDENTITY_QUERY&&in->product==r->store->product&&
       !in->session&&in->transaction&&!in->number&&!in->slot&&!in->bytes&&cf_identity_valid(uid))rc=CF_OK;
    *out=*in;out->opcode=CF_IDENTITY_QUERY|64u;out->session=0;out->bytes=20;
    for(unsigned i=0;i<20;++i)out->data[i]=0;
    put(out->data,(uint32_t)rc);
    if(rc==CF_OK){put(out->data+4,1);put(out->data+8,r->store->product);put(out->data+12,uid[0]);put(out->data+16,uid[1]);}
    return rc;
}
