/* SPDX-License-Identifier: MIT. No file writes, MMIO, heap or audio work. */
#include "asset_seed.h"
static void put(uint8_t *p,uint32_t v){for(unsigned i=0;i<4;++i)p[i]=(uint8_t)(v>>(8*i));}
int cf_seed_init(CfAssetSeed *s,const CfSeedManifest *m,uint32_t product){
    if(!s||!m||s->manifest||product<2424833u||product>2424835u)return CF_BAD_INPUT;
#if defined(__arm__) || defined(__thumb__)
    if((uintptr_t)m<0x60001000u||(uintptr_t)m>0x60200000u-sizeof(*m)||((uintptr_t)m&3u))return CF_BAD_INPUT;
#endif
    if(m->magic!=CF_SEED_MAGIC||m->version!=1||m->mask>31||m->count!=5)return CF_BAD_INPUT;
    unsigned any=0,all=255;for(unsigned i=0;i<32;++i){any|=m->edition[i];all&=m->edition[i];}if(!any||all==255)return CF_BAD_INPUT;
    for(unsigned i=0;i<5;++i)if(m->mask&(1u<<i)){
        const CfSeedEntry *e=&m->entry[i];unsigned n=i<3?25088u:7772u;
        uintptr_t p=(uintptr_t)e->payload;if(!p||(p&3u)||p+n<p||e->length!=n||!e->transaction)return CF_BAD_INPUT;
#if defined(__arm__) || defined(__thumb__)
        if(p<0x60001000u||p>0x60200000u-n)return CF_BAD_INPUT;
#endif
    }
    s->product=product;s->manifest=m;return CF_OK;
}
static void header(CfAssetSeed *s,unsigned slot){
    const CfSeedEntry *e=&s->manifest->entry[slot];uint8_t *b=s->header;
    for(unsigned i=0;i<256;++i)b[i]=0;
    put(b,0x32584643u);put(b+4,2);put(b+8,s->product);put(b+12,slot);
    put(b+16,e->length);put(b+20,e->crc);put(b+24,1);put(b+28,e->transaction);put(b+32,48000);put(b+36,slot<3?2048:1871);
    for(unsigned i=0;i<sizeof(e->info);++i)b[40+i]=((const uint8_t*)&e->info)[i];
    put(b+252,cf_crc32(0xffffffffu,b,252)^0xffffffffu);
}
int cf_seed_read(CfAssetSeed *s,unsigned slot,unsigned bank,unsigned offset,void *out,unsigned bytes){
    if(!s||!s->manifest||slot>=5||bank>=2||!out||offset>CF_BANK_BYTES||bytes>CF_BANK_BYTES-offset||bytes>256)return CF_BAD_INPUT;
    uint8_t *p=out;const CfSeedEntry *e=&s->manifest->entry[slot];unsigned have=bank==1&&(s->manifest->mask&(1u<<slot));
    if(have&&offset<256)header(s,slot);
    for(unsigned i=0;i<bytes;++i){unsigned a=offset+i;p[i]=!have?255:a<256?s->header[a]:a-256<e->length?e->payload[a-256]:255;}
    return CF_OK;
}
int cf_seed_name(const CfAssetSeed *s,unsigned slot,unsigned bank,char *out,unsigned capacity){
    if(!s||!s->manifest||slot>=5||bank>=2||!out||capacity<74)return CF_BAD_INPUT;
    const char *hex="0123456789abcdef",*prefix="cfcx_";for(unsigned i=0;i<5;++i)out[i]=prefix[i];
    for(unsigned i=0;i<32;++i){out[5+i*2]=hex[s->manifest->edition[i]>>4];out[6+i*2]=hex[s->manifest->edition[i]&15];}
    out[69]='_';out[70]=(char)('0'+slot);out[71]='_';out[72]=(char)('0'+bank);out[73]=0;return CF_OK;
}
unsigned cf_seed_bytes(void){return sizeof(CfAssetSeed);}
