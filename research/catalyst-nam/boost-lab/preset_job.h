/* SPDX-License-Identifier: MIT. FS-owner bridge, never a host-provided CRC. */
#ifndef CF_PREAMP_PRESET_JOB_H
#define CF_PREAMP_PRESET_JOB_H
#include "preset_store.h"
typedef int (*CfNativePresetRead)(unsigned index,unsigned offset,void *out,unsigned count);
typedef int (*CfApplyPreamp)(const void *config28);
typedef struct {
    CfPreampPresets *presets;CfNativePresetRead read;CfApplyPreamp apply;
    CfPreampRecord record;uint8_t chunk[256];
    unsigned phase,index,transaction,offset,rolling,save,interrupted,result;
} CfPreampPresetJob;
int cf_preamp_job_init(CfPreampPresetJob*,CfPreampPresets*,CfNativePresetRead,CfApplyPreamp);
int cf_preamp_job_save(CfPreampPresetJob*,unsigned preset,unsigned transaction,const CfPreampRecord*);
int cf_preamp_job_load(CfPreampPresetJob*,unsigned preset);
int cf_preamp_job_pump(CfPreampPresetJob*);
/* Notify BEFORE forwarding any original/native FS request. Hashing is then
 * abandoned; an already committing sidecar is finished but never reported as
 * a save of the new native state. All calls share the serialized FS owner. */
void cf_preamp_job_native_activity(CfPreampPresetJob*);
unsigned cf_preamp_job_bytes(void);
#endif
