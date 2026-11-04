/* SPDX-License-Identifier: MIT. Bounded hashing of the exact native file. */
#include "preset_job.h"
#include "../asset-lab/asset_store.h"
static void zero(void *p,unsigned n){for(unsigned i=0;i<n;++i)((uint8_t*)p)[i]=0;}
static int finish(CfPreampPresetJob *j,int rc){j->phase=0;j->result=(unsigned)rc;return rc;}
int cf_preamp_job_init(CfPreampPresetJob *j,CfPreampPresets *p,CfNativePresetRead read,CfApplyPreamp apply){
    if(!j||!p||!read||!apply)return CF_BAD_INPUT;zero(j,sizeof(*j));j->presets=p;j->read=read;j->apply=apply;return CF_OK;
}
static int begin(CfPreampPresetJob *j,unsigned preset,unsigned transaction,unsigned save){
    if(!j||!j->presets||preset>=13||(save&&!transaction))return CF_BAD_INPUT;
    if(j->phase||!cf_preamp_presets_idle(j->presets))return CF_BUSY;
    j->index=preset;j->transaction=transaction;j->save=save;j->offset=j->interrupted=0;j->rolling=0xffffffffu;j->phase=1;j->result=CF_PENDING;return CF_PENDING;
}
int cf_preamp_job_save(CfPreampPresetJob *j,unsigned preset,unsigned transaction,const CfPreampRecord *r){
    if(!cf_preamp_record_valid(r))return CF_BAD_INPUT;
    int rc=begin(j,preset,transaction,1);if(rc==CF_PENDING){j->record=*r;j->record.preset_crc=0;}return rc;
}
int cf_preamp_job_load(CfPreampPresetJob *j,unsigned preset){
    int rc=begin(j,preset,0,0);
    if(rc==CF_PENDING){
        /* Do not carry the previous preset's custom pedal into an empty,
         * corrupt or unavailable new preset while its file is being read. */
        zero(&j->record,sizeof(j->record));j->record.tone_bits=0x3f000000u;j->record.level_bits=0x3f800000u;
        if(j->apply(&j->record))return finish(j,CF_BAD_INPUT);
    }return rc;
}
void cf_preamp_job_native_activity(CfPreampPresetJob *j){if(j&&j->phase)j->interrupted=1;}
int cf_preamp_job_pump(CfPreampPresetJob *j){
    if(!j||!j->presets)return CF_BAD_INPUT;if(!j->phase)return (int)j->result;
    if(j->phase==1){
        if(j->interrupted)return finish(j,CF_STALE);
        int n=j->read(j->index,j->offset,j->chunk,256);
        if(n<0)return finish(j,CF_IO);if(n>256||j->offset>65536u-(unsigned)n)return finish(j,CF_INVALID_DATA);
        if(n){j->rolling=cf_crc32(j->rolling,j->chunk,(unsigned)n);j->offset+=(unsigned)n;return CF_PENDING;}
        if(!j->offset)return finish(j,CF_EMPTY);
        unsigned digest=j->rolling^0xffffffffu;
        if(j->save){
            j->record.preset_crc=digest;
            int rc=cf_preamp_presets_save(j->presets,j->index,j->transaction,&j->record);
            if(rc!=CF_PENDING)return finish(j,rc);j->phase=2;return CF_PENDING;
        }
        int rc=cf_preamp_presets_get(j->presets,j->index,digest,&j->record);
        if(rc!=CF_OK&&rc!=CF_EMPTY&&rc!=CF_STALE)return finish(j,rc);
        if(rc!=CF_OK){zero(&j->record,sizeof(j->record));j->record.tone_bits=0x3f000000u;j->record.level_bits=0x3f800000u;}
        if(j->apply(&j->record))return finish(j,CF_BAD_INPUT);
        return finish(j,rc); /* EMPTY/STALE deliberately restore factory mode */
    }
    if(j->phase==2){
        int rc=cf_preamp_presets_pump(j->presets);if(rc==CF_PENDING)return rc;
        return finish(j,rc==CF_OK&&j->interrupted?CF_STALE:rc);
    }
    return finish(j,CF_BAD_INPUT);
}
unsigned cf_preamp_job_bytes(void){return sizeof(CfPreampPresetJob);}
