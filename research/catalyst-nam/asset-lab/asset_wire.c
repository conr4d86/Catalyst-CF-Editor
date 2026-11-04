/* SPDX-License-Identifier: MIT. Run by the serialized storage owner, never ISR.
 * Native MIDI observer, task queue and physical adapter are NOT linked here.
 */
#include "asset_wire.h"
static const uint8_t prefix[7]={0xf0,0x7d,'C','F','C','X',1};
static uint32_t get32(const uint8_t *p){return p[0]|((uint32_t)p[1]<<8)|((uint32_t)p[2]<<16)|((uint32_t)p[3]<<24);}
static void put32(uint8_t *p,uint32_t v){for(unsigned i=0;i<4;++i)p[i]=(uint8_t)(v>>(8*i));}
static void zero(void *p,unsigned n){for(unsigned i=0;i<n;++i)((uint8_t*)p)[i]=0;}
int cf_wire_decode(const uint8_t *p,unsigned count,CfWireMessage *m){
    if(!p||!m||count<6)return 0;
    for(unsigned i=0;i<6;++i)if(p[i]!=prefix[i])return 0;
    if(count<9||count>CF_WIRE_MAX||p[6]!=1||p[7]>127||p[count-1]!=0xf7)return -1;
    uint8_t raw[280];unsigned n=0,pos=8;
    while(pos<count-1){
        uint8_t high=p[pos++];if(high>127)return -1;
        unsigned group=count-1-pos;if(group>7)group=7;
        if(!group||(high>>group))return -1;
        for(unsigned i=0;i<group;++i){if(p[pos]>127||n>=sizeof(raw))return -1;raw[n++]=p[pos++]|(((high>>i)&1u)<<7);}
    }
    if(n<24||raw[19]||raw[18]>1)return -1;
    unsigned bytes=raw[17]|((unsigned)raw[18]<<8);
    if(bytes>CF_CHUNK_BYTES||n!=24+bytes)return -1;
    uint32_t crc=cf_crc32(0xffffffffu,p+7,1);crc=cf_crc32(crc,raw,n-4)^0xffffffffu;
    if(crc!=get32(raw+n-4))return -1;
    CfWireMessage next;zero(&next,sizeof(next));next.product=get32(raw);next.session=get32(raw+4);next.transaction=get32(raw+8);next.number=get32(raw+12);next.slot=raw[16];next.bytes=(uint16_t)bytes;next.opcode=p[7];
    for(unsigned i=0;i<bytes;++i)next.data[i]=raw[20+i];*m=next;return 1;
}
unsigned cf_wire_encode(const CfWireMessage *m,uint8_t *p,unsigned capacity){
    if(!m||!p||m->bytes>CF_CHUNK_BYTES||m->opcode>127)return 0;
    unsigned n=24+m->bytes,need=9+n+(n+6)/7;if(capacity<need)return 0;
    uint8_t raw[280];zero(raw,20);put32(raw,m->product);put32(raw+4,m->session);put32(raw+8,m->transaction);put32(raw+12,m->number);raw[16]=m->slot;raw[17]=(uint8_t)m->bytes;raw[18]=(uint8_t)(m->bytes>>8);
    for(unsigned i=0;i<m->bytes;++i)raw[20+i]=m->data[i];
    uint32_t crc=cf_crc32(0xffffffffu,&m->opcode,1);crc=cf_crc32(crc,raw,n-4)^0xffffffffu;put32(raw+n-4,crc);
    for(unsigned i=0;i<7;++i)p[i]=prefix[i];p[7]=m->opcode;unsigned pos=8;
    for(unsigned off=0;off<n;off+=7){unsigned group=n-off;if(group>7)group=7;uint8_t high=0;
        for(unsigned i=0;i<group;++i)high|=(raw[off+i]>>7)<<i;
        p[pos++]=high;for(unsigned i=0;i<group;++i)p[pos++]=raw[off+i]&127u;
    }p[pos++]=0xf7;return pos;
}
int cf_receiver_init(CfAssetReceiver *r,CfAssetStore *s,uint32_t capabilities){
    if(!r||!s||(capabilities&~CF_DATA_WRITE))return CF_BAD_INPUT;
    zero(r,sizeof(*r));r->store=s;r->capabilities=capabilities;return CF_OK;
}
static void reply(CfWireMessage *out,const CfWireMessage *in,uint32_t status,uint32_t value){
    *out=*in;out->opcode=in->opcode|64u;out->bytes=8;put32(out->data,status);put32(out->data+4,value);
}
int cf_receiver_handle(CfAssetReceiver *r,const CfWireMessage *in,CfWireMessage *out){
    if(!r||!in||!out||in->opcode&64u)return CF_BAD_INPUT;
    CfAssetStore *s=r->store;int status=CF_BAD_INPUT;uint32_t value=0;
    if(in->product!=s->product||!in->transaction){reply(out,in,CF_BAD_INPUT,0);return CF_BAD_INPUT;}
    if(in->opcode==CF_HELLO){
        if(in->bytes||in->session||in->number||in->slot){reply(out,in,CF_BAD_INPUT,0);return CF_BAD_INPUT;}
        if(s->phase&&r->session!=in->transaction)status=CF_BUSY;
        else{r->session=in->transaction;status=CF_OK;}
        reply(out,in,status,r->capabilities);out->session=r->session;
        out->bytes=20;put32(out->data+8,CF_CHUNK_BYTES);put32(out->data+12,CF_ASSET_SLOTS);put32(out->data+16,2);return status;
    }
    if(!r->session||in->session!=r->session||in->slot>=CF_ASSET_SLOTS){reply(out,in,CF_STALE,0);return CF_STALE;}
    if(in->opcode==CF_STATUS&&in->bytes==0){
        if(s->phase==3)status=CF_BUSY;
        else if(s->phase){status=in->slot!=s->slot?CF_STALE:s->transaction==in->transaction?CF_PENDING:CF_BUSY;value=s->received;}
        else{
            CfAssetMeta meta;status=cf_asset_recover(s,in->slot,&meta);
            if(status==CF_OK){value=meta.generation;reply(out,in,status,value);out->bytes=20+sizeof(meta.info);put32(out->data+8,meta.transaction);put32(out->data+12,meta.length);put32(out->data+16,meta.crc);for(unsigned i=0;i<sizeof(meta.info);++i)out->data[20+i]=((const uint8_t*)&meta.info)[i];return status;}
        }
    }else if(!(r->capabilities&CF_DATA_WRITE))status=CF_BAD_INPUT;
    else if(in->opcode==CF_BEGIN&&in->bytes==8){status=cf_asset_begin(s,in->transaction,in->slot,in->number,get32(in->data),get32(in->data+4));value=s->received;}
    else if(in->opcode==CF_BEGIN&&in->bytes==8+sizeof(CfAssetInfo)){status=cf_asset_begin_with_info(s,in->transaction,in->slot,in->number,get32(in->data),get32(in->data+4),(const CfAssetInfo*)(in->data+8));value=s->received;}
    else if(in->slot!=s->slot)status=CF_STALE;
    else if(in->opcode==CF_CHUNK){status=cf_asset_chunk(s,in->transaction,in->number,in->data,in->bytes);value=s->received;}
    else if(in->opcode==CF_COMMIT&&!in->bytes&&!in->number)status=cf_asset_commit(s,in->transaction);
    else if(in->opcode==CF_CANCEL&&!in->bytes&&!in->number)status=cf_asset_cancel(s,in->transaction);
    r->last_transaction=in->transaction;r->last_status=status;reply(out,in,status,value);return status;
}
int cf_receiver_pump(CfAssetReceiver *r){
    if(!r||!r->store)return CF_BAD_INPUT;
    r->last_status=cf_asset_pump(r->store);r->last_transaction=r->store->transaction;return (int)r->last_status;
}
