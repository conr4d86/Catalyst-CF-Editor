/* SPDX-License-Identifier: MIT. No device addresses, allocator, OS or USB. */
#include "asset_store.h"
static uint32_t get32(const uint8_t *p) {
    return (uint32_t)p[0]|((uint32_t)p[1]<<8)|((uint32_t)p[2]<<16)|((uint32_t)p[3]<<24);
}
static void put32(uint8_t *p,uint32_t v) {for(unsigned i=0;i<4;++i)p[i]=(uint8_t)(v>>(8*i));}
static void clear(void *p,unsigned n) {uint8_t *b=p;for(unsigned i=0;i<n;++i)b[i]=0;}
static int equal(const void *a,const void *b,unsigned n){for(unsigned i=0;i<n;++i)if(((const uint8_t*)a)[i]!=((const uint8_t*)b)[i])return 0;return 1;}
static int db_range(uint32_t bits,uint32_t negative_limit,uint32_t positive_limit){uint32_t magnitude=bits&0x7fffffffu;return magnitude<=((bits>>31)?negative_limit:positive_limit);}
static int metadata_ok(const CfAssetInfo *info,unsigned slot){
    if(!info||info->flags>3u||(slot>=3&&(info->flags&2u))||info->name_bytes>127u||
       !db_range(info->input_db_bits,0x42700000u,0x41400000u)|| /* -60..12 dB */
       !db_range(info->output_db_bits,0x42f00000u,0x42100000u)|| /* -120..36 dB */
       info->low_hz_bits>0x46bb8000u||info->high_hz_bits>0x46bb8000u||
       info->low_hz_bits>info->high_hz_bits)return 0; /* 0..24000 Hz */
    unsigned n=info->name_bytes;
    for(unsigned i=n;i<128;++i)if(info->name[i])return 0;
    /* Canonical UTF-8, no path separators, controls, surrogates or overlongs.
     * Names are display metadata, never used as a filesystem path. */
    for(unsigned i=0;i<n;){unsigned c=info->name[i++],tail=0,value=0,minimum=0;
        if(c<128){if(c<32||c==127||c=='/'||c=='\\')return 0;continue;}
        if(c>=0xc2&&c<=0xdf){tail=1;value=c&31u;minimum=0x80;}
        else if(c>=0xe0&&c<=0xef){tail=2;value=c&15u;minimum=0x800;}
        else if(c>=0xf0&&c<=0xf4){tail=3;value=c&7u;minimum=0x10000;}
        else return 0;
        if(tail>n-i)return 0;
        while(tail--){unsigned t=info->name[i++];if((t&0xc0u)!=0x80u)return 0;value=(value<<6)|(t&63u);}
        if(value<minimum||value>0x10ffffu||(value>=0xd800u&&value<=0xdfffu)||
           value==0x2028u||value==0x2029u)return 0;
    }return 1;
}
uint32_t cf_crc32(uint32_t crc,const void *data,unsigned count) {
    const uint8_t *p=data;
    for(unsigned i=0;i<count;++i){crc^=p[i];for(unsigned b=0;b<8;++b)crc=(crc>>1)^((0u-(crc&1u))&0xedb88320u);}
    return crc;
}
static unsigned length_for(unsigned slot) {return slot<3?25088u:(slot<5?7772u:0);}
static int floats_ok(const uint8_t *p,unsigned n,unsigned *nonzero) {
    for(unsigned i=0;i<n;i+=4){uint32_t v=get32(p+i),m=v&0x7fffffffu;
        if(m>=0x7f800000u)return 0; /* Reject NaN and infinity. */
        if(m>0x4b800000u)return 0; /* Reject pathological magnitudes >2^24. */
        if(m)*nonzero=1;
    }return 1;
}
static int header_ok(CfAssetStore *s,unsigned slot,const uint8_t *b,CfAssetMeta *m) {
    if(get32(b)!=0x32584643u||get32(b+4)!=2||get32(b+8)!=s->product||get32(b+12)!=slot||
       get32(b+16)!=length_for(slot)||!get32(b+24)||!get32(b+28)||
       get32(b+32)!=48000||get32(b+36)!=(slot<3?2048u:1871u)||
       get32(b+252)!=(cf_crc32(0xffffffffu,b,252)^0xffffffffu))return 0;
    for(unsigned i=224;i<252;++i)if(b[i])return 0;
    for(unsigned i=0;i<sizeof(m->info);++i)((uint8_t*)&m->info)[i]=b[40+i];
    if(!metadata_ok(&m->info,slot))return 0;
    m->length=get32(b+16);m->crc=get32(b+20);m->generation=get32(b+24);m->transaction=get32(b+28);return 1;
}
static int read_bank(CfAssetStore *s,unsigned slot,unsigned bank,CfAssetMeta *m) {
    /* The serialized control owner already owns scratch; reuse it instead of
     * adding another 256-byte object to the native FileSystem task stack. */
    if(s->io.read(s->io.user,slot,bank,0,s->scratch,CF_HEADER_BYTES))return CF_IO;
    if(!header_ok(s,slot,s->scratch,m))return CF_EMPTY;
    uint32_t crc=0xffffffffu;unsigned nonzero=0;
    for(unsigned p=0;p<m->length;p+=CF_CHUNK_BYTES){unsigned n=m->length-p;if(n>CF_CHUNK_BYTES)n=CF_CHUNK_BYTES;
        if(s->io.read(s->io.user,slot,bank,CF_HEADER_BYTES+p,s->scratch,n))return CF_IO;
        if(!floats_ok(s->scratch,n,&nonzero))return CF_EMPTY;
        crc=cf_crc32(crc,s->scratch,n);
    }
    if(!nonzero||(crc^0xffffffffu)!=m->crc)return CF_EMPTY;
    m->bank=bank;return CF_OK;
}
int cf_asset_init(CfAssetStore *s,const CfAssetIO *io,uint32_t product) {
    if(!s||!io||!io->read||!io->erase||!io->write||!io->sync||
       (product!=2424833u&&product!=2424834u&&product!=2424835u))return CF_BAD_INPUT;
    clear(s,sizeof(*s));s->io=*io;s->product=product;return CF_OK;
}
int cf_asset_recover(CfAssetStore *s,unsigned slot,CfAssetMeta *meta) {
    if(!s||!meta||slot>=CF_ASSET_SLOTS)return CF_BAD_INPUT;
    if(s->phase)return CF_BUSY; /* Scratch and IO are control-owner exclusive. */
    CfAssetMeta a;int x=read_bank(s,slot,0,&a),y=read_bank(s,slot,1,meta);
    if(x==CF_IO||y==CF_IO)return CF_IO;
    if(x&&y){clear(meta,sizeof(*meta));return CF_EMPTY;}
    if(!x&&!y&&a.generation==meta->generation&&(a.crc!=meta->crc||a.transaction!=meta->transaction||!equal(&a.info,&meta->info,sizeof(a.info))))return CF_INVALID_DATA;
    if(!x&&(y||a.generation>=meta->generation))*meta=a;return CF_OK;
}
int cf_asset_begin(CfAssetStore *s,uint32_t tx,unsigned slot,uint32_t base_generation,uint32_t bytes,uint32_t crc){CfAssetInfo info;clear(&info,sizeof(info));return cf_asset_begin_with_info(s,tx,slot,base_generation,bytes,crc,&info);}
int cf_asset_begin_with_info(CfAssetStore *s,uint32_t tx,unsigned slot,uint32_t base_generation,uint32_t bytes,uint32_t crc,const CfAssetInfo *info) {
    if(!s||!tx||slot>=CF_ASSET_SLOTS||bytes!=length_for(slot)||!metadata_ok(info,slot))return CF_BAD_INPUT;
    if(s->phase){if(s->phase==1&&s->transaction==tx&&s->slot==slot&&s->generation==base_generation+1u&&s->length==bytes&&s->expected==crc&&equal(info,&s->info,sizeof(*info)))return CF_ALREADY;return CF_BUSY;}
    CfAssetMeta old;int rc=cf_asset_recover(s,slot,&old);
    if(rc!=CF_OK&&rc!=CF_EMPTY)return rc;
    if(rc==CF_OK&&old.transaction==tx)return old.length==bytes&&old.crc==crc&&equal(info,&old.info,sizeof(*info))?CF_ALREADY:CF_STALE;
    if(base_generation!=(rc==CF_OK?old.generation:0)||base_generation==0xffffffffu)return CF_STALE;
    unsigned bank=rc==CF_OK?1u-old.bank:0u;
    if(s->io.erase(s->io.user,slot,bank)||s->io.sync(s->io.user))return CF_IO;
    s->transaction=tx;s->slot=slot;s->bank=bank;s->generation=base_generation+1u;s->length=bytes;s->expected=crc;s->info=*info;
    s->received=s->checked=s->nonzero=0;s->rolling=s->verified=0xffffffffu;s->phase=1;return CF_OK;
}
int cf_asset_chunk(CfAssetStore *s,uint32_t tx,uint32_t offset,const void *data,unsigned bytes) {
    if(!s||s->phase!=1||s->transaction!=tx||!data||!bytes||bytes>CF_CHUNK_BYTES||
       (offset&3u)||(bytes&3u)||offset>s->length||bytes>s->length-offset)return CF_BAD_INPUT;
    if(offset<s->received){if(bytes>s->received-offset)return CF_STALE;
        if(s->io.read(s->io.user,s->slot,s->bank,CF_HEADER_BYTES+offset,s->scratch,bytes))return CF_IO;
        for(unsigned i=0;i<bytes;++i)if(s->scratch[i]!=((const uint8_t*)data)[i])return CF_STALE;
        return CF_ALREADY;
    }
    if(offset!=s->received)return CF_STALE;
    if(s->io.write(s->io.user,s->slot,s->bank,CF_HEADER_BYTES+offset,data,bytes)){s->phase=0;return CF_IO;}
    s->rolling=cf_crc32(s->rolling,data,bytes);s->received+=bytes;return CF_OK;
}
int cf_asset_commit(CfAssetStore *s,uint32_t tx) {
    if(!s||s->transaction!=tx)return CF_BAD_INPUT;
    if(s->phase==2)return CF_PENDING;
    if(s->phase!=1||s->received!=s->length)return CF_BAD_INPUT;
    if((s->rolling^0xffffffffu)!=s->expected){s->phase=0;return CF_INVALID_DATA;}
    if(s->io.sync(s->io.user)){s->phase=0;return CF_IO;}
    s->phase=2;return CF_PENDING;
}
int cf_asset_pump(CfAssetStore *s) {
    if(!s||s->phase!=2)return CF_BAD_INPUT;
    if(s->checked<s->length){
        unsigned n=s->length-s->checked;if(n>CF_CHUNK_BYTES)n=CF_CHUNK_BYTES;
        if(s->io.read(s->io.user,s->slot,s->bank,CF_HEADER_BYTES+s->checked,s->scratch,n)){s->phase=0;return CF_IO;}
        if(!floats_ok(s->scratch,n,&s->nonzero)){s->phase=0;return CF_INVALID_DATA;}
        s->verified=cf_crc32(s->verified,s->scratch,n);s->checked+=n;return CF_PENDING;
    }
    if(!s->nonzero||(s->verified^0xffffffffu)!=s->expected){s->phase=0;return CF_INVALID_DATA;}
    uint8_t *header=s->scratch;clear(header,CF_HEADER_BYTES);
    put32(header,0x32584643u);put32(header+4,2);put32(header+8,s->product);put32(header+12,s->slot);
    put32(header+16,s->length);put32(header+20,s->expected);put32(header+24,s->generation);put32(header+28,s->transaction);
    put32(header+32,48000);put32(header+36,s->slot<3?2048:1871);
    for(unsigned i=0;i<sizeof(s->info);++i)header[40+i]=((const uint8_t*)&s->info)[i];
    put32(header+252,cf_crc32(0xffffffffu,header,252)^0xffffffffu);
    /* Only the inactive bank is touched. A torn header is not a valid commit.
     * An IO error here is uncertain: host must query, never blindly retry. */
    int rc=s->io.write(s->io.user,s->slot,s->bank,0,header,CF_HEADER_BYTES)||s->io.sync(s->io.user);
    CfAssetMeta meta;
    if(!rc)rc=s->io.read(s->io.user,s->slot,s->bank,0,header,CF_HEADER_BYTES)||!header_ok(s,s->slot,header,&meta);
    if(!rc)rc=meta.length!=s->length||meta.crc!=s->expected||meta.generation!=s->generation||meta.transaction!=s->transaction||!equal(&meta.info,&s->info,sizeof(s->info));
    s->phase=0;return rc?CF_UNCERTAIN:CF_OK;
}
int cf_asset_cancel(CfAssetStore *s,uint32_t tx) {
    if(!s||!tx||s->transaction!=tx)return CF_BAD_INPUT;
    if(s->phase==3)return CF_BUSY; /* A USB CANCEL cannot release a loader lease. */
    s->phase=0;return CF_OK;
}
unsigned cf_asset_store_bytes(void) {return sizeof(CfAssetStore);}
