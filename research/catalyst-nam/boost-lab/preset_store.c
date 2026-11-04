/* SPDX-License-Identifier: MIT. No heap, addresses, SDK, audio or raw flash. */
#include "preset_store.h"
#include "../asset-lab/asset_store.h"
enum{SCAN_HEADER=1,SCAN_DATA,IDLE,ERASE,WRITE_DATA,SYNC_DATA,VERIFY_DATA,WRITE_HEADER,SYNC_HEADER,VERIFY_HEADER,FAILED};
static void copy(void *a,const void *b,unsigned n){for(unsigned i=0;i<n;++i)((uint8_t*)a)[i]=((const uint8_t*)b)[i];}
static void zero(void *a,unsigned n){for(unsigned i=0;i<n;++i)((uint8_t*)a)[i]=0;}
static unsigned get(const uint8_t *p){return (unsigned)p[0]|((unsigned)p[1]<<8)|((unsigned)p[2]<<16)|((unsigned)p[3]<<24);}
static void put(uint8_t *p,unsigned v){for(unsigned i=0;i<4;++i)p[i]=(uint8_t)(v>>(8*i));}
static unsigned crc(const void *p,unsigned n){return cf_crc32(0xffffffffu,p,n)^0xffffffffu;}
static int same(const void *a,const void *b,unsigned n){for(unsigned i=0;i<n;++i)if(((const uint8_t*)a)[i]!=((const uint8_t*)b)[i])return 0;return 1;}
static int record_ok(const CfPreampRecord *r){return r&&r->mode<=3&&r->enabled<=1&&r->boost<=1&&r->bright<=1&&r->drive_bits<=0x3f800000u&&r->tone_bits<=0x3f800000u&&r->level_bits<=0x40000000u;}
static int table_ok(const uint8_t *p,unsigned mask){
    for(unsigned i=0;i<13;++i){const CfPreampRecord *r=(const CfPreampRecord*)(p+i*32);
        if(mask&(1u<<i)){if(!record_ok(r))return 0;}else for(unsigned j=0;j<32;++j)if(p[i*32+j])return 0;
    }return 1;
}
static int header_ok(CfPreampPresets *s){
    const uint8_t *h=s->header;
    if(get(h)!=0x31504643u||get(h+4)!=1||get(h+8)!=s->product||!get(h+12)||!get(h+16)||get(h+20)>8191||get(h+24)!=416||get(h+32)>=13||!(get(h+20)&(1u<<get(h+32)))||get(h+60)!=crc(h,60))return 0;
    for(unsigned i=36;i<60;++i)if(h[i])return 0;
    s->candidate_generation=get(h+12);s->candidate_transaction=get(h+16);s->candidate_mask=get(h+20);s->candidate_crc=get(h+28);s->candidate_preset=get(h+32);return 1;
}
static void make_header(CfPreampPresets *s){
    zero(s->header,64);put(s->header,0x31504643u);put(s->header+4,1);put(s->header+8,s->product);
    put(s->header+12,s->candidate_generation);put(s->header+16,s->candidate_transaction);put(s->header+20,s->candidate_mask);
    put(s->header+24,416);put(s->header+28,s->candidate_crc);put(s->header+32,s->candidate_preset);put(s->header+60,crc(s->header,60));
}
int cf_preamp_presets_open(CfPreampPresets *s,const CfPreampPresetIO *io,unsigned product){
    if(!s||!io||!io->read||!io->erase||!io->write||!io->sync||product<2424833||product>2424835)return CF_BAD_INPUT;
    zero(s,sizeof(*s));s->io=*io;s->product=product;s->phase=SCAN_HEADER;return CF_PENDING;
}
static int fail(CfPreampPresets *s,int result){s->phase=FAILED;return result;}
static int scan_next(CfPreampPresets *s,int valid){
    if(valid){
        if(s->have_best&&s->generation==s->candidate_generation&&
           (s->transaction!=s->candidate_transaction||s->mask!=s->candidate_mask||s->last_preset!=s->candidate_preset||!same(s->table,s->candidate,416)))return fail(s,CF_INVALID_DATA);
        if(!s->have_best||s->candidate_generation>s->generation){
            copy(s->table,s->candidate,416);s->generation=s->candidate_generation;s->transaction=s->candidate_transaction;s->mask=s->candidate_mask;s->last_preset=s->candidate_preset;s->best_bank=s->bank;s->have_best=1;
        }
    }
    ++s->bank;s->offset=0;s->phase=s->bank<2?SCAN_HEADER:IDLE;return s->phase==IDLE?CF_OK:CF_PENDING;
}
int cf_preamp_presets_pump(CfPreampPresets *s){
    if(!s)return CF_BAD_INPUT;
    unsigned n;int rc;
    switch(s->phase){
    case IDLE:return CF_OK;
    case SCAN_HEADER:
        if(s->io.read(s->io.user,s->bank,0,s->header,64))return fail(s,CF_IO);
        if(!header_ok(s))return scan_next(s,0);
        s->offset=0;s->rolling=0xffffffffu;s->phase=SCAN_DATA;return CF_PENDING;
    case SCAN_DATA:
        n=416-s->offset;if(n>256)n=256;
        if(n){if(s->io.read(s->io.user,s->bank,64+s->offset,s->candidate+s->offset,n))return fail(s,CF_IO);
            s->rolling=cf_crc32(s->rolling,s->candidate+s->offset,n);s->offset+=n;return CF_PENDING;}
        return scan_next(s,(s->rolling^0xffffffffu)==s->candidate_crc&&table_ok(s->candidate,s->candidate_mask));
    case ERASE:
        if(s->io.erase(s->io.user,s->bank))return fail(s,CF_IO);s->offset=0;s->phase=WRITE_DATA;return CF_PENDING;
    case WRITE_DATA:
        n=416-s->offset;if(n>256)n=256;
        if(n){if(s->io.write(s->io.user,s->bank,64+s->offset,s->candidate+s->offset,n))return fail(s,CF_IO);s->offset+=n;return CF_PENDING;}
        s->phase=SYNC_DATA;return CF_PENDING;
    case SYNC_DATA:
        if(s->io.sync(s->io.user))return fail(s,CF_IO);s->offset=0;s->rolling=0xffffffffu;s->phase=VERIFY_DATA;return CF_PENDING;
    case VERIFY_DATA:
        n=416-s->offset;if(n>64)n=64;
        if(n){if(s->io.read(s->io.user,s->bank,64+s->offset,s->header,n))return fail(s,CF_IO);
            s->rolling=cf_crc32(s->rolling,s->header,n);s->offset+=n;return CF_PENDING;}
        if((s->rolling^0xffffffffu)!=s->candidate_crc)return fail(s,CF_INVALID_DATA);
        make_header(s);s->phase=WRITE_HEADER;return CF_PENDING;
    case WRITE_HEADER:
        if(s->io.write(s->io.user,s->bank,0,s->header,64))return fail(s,CF_UNCERTAIN);s->phase=SYNC_HEADER;return CF_PENDING;
    case SYNC_HEADER:
        if(s->io.sync(s->io.user))return fail(s,CF_UNCERTAIN);s->phase=VERIFY_HEADER;return CF_PENDING;
    case VERIFY_HEADER:{
        unsigned generation=s->candidate_generation,transaction=s->candidate_transaction,mask=s->candidate_mask,digest=s->candidate_crc,preset=s->candidate_preset;
        rc=s->io.read(s->io.user,s->bank,0,s->header,64);
        if(rc||!header_ok(s)||s->candidate_generation!=generation||s->candidate_transaction!=transaction||s->candidate_mask!=mask||s->candidate_crc!=digest||s->candidate_preset!=preset)return fail(s,CF_UNCERTAIN);
        copy(s->table,s->candidate,416);s->generation=generation;s->transaction=transaction;s->mask=mask;s->last_preset=preset;s->best_bank=s->bank;s->have_best=1;s->phase=IDLE;return CF_OK;
    }
    default:return CF_BAD_INPUT;
    }
}
int cf_preamp_presets_get(CfPreampPresets *s,unsigned preset,unsigned native_crc,CfPreampRecord *out){
    if(!s||!out||preset>=13)return CF_BAD_INPUT;if(s->phase!=IDLE)return CF_BUSY;
    if(!(s->mask&(1u<<preset)))return CF_EMPTY;
    const CfPreampRecord *r=(const CfPreampRecord*)(s->table+preset*32);
    if(r->preset_crc!=native_crc)return CF_STALE;*out=*r;return CF_OK;
}
int cf_preamp_presets_save(CfPreampPresets *s,unsigned preset,unsigned transaction,const CfPreampRecord *r){
    if(!s||preset>=13||!transaction||!record_ok(r))return CF_BAD_INPUT;if(s->phase!=IDLE)return CF_BUSY;
    if(s->transaction==transaction)return s->last_preset==preset&&(s->mask&(1u<<preset))&&same(s->table+preset*32,r,32)?CF_ALREADY:CF_STALE;
    if(s->generation==0xffffffffu)return CF_STALE;
    copy(s->candidate,s->table,416);copy(s->candidate+preset*32,r,32);s->candidate_mask=s->mask|(1u<<preset);
    s->candidate_generation=s->generation+1;s->candidate_transaction=transaction;s->candidate_crc=crc(s->candidate,416);s->candidate_preset=preset;
    s->bank=s->have_best?1u-s->best_bank:0;s->phase=ERASE;return CF_PENDING;
}
unsigned cf_preamp_presets_bytes(void){return sizeof(CfPreampPresets);}
int cf_preamp_record_valid(const CfPreampRecord *r){return record_ok(r);}
int cf_preamp_presets_idle(const CfPreampPresets *s){return s&&s->phase==IDLE;}
