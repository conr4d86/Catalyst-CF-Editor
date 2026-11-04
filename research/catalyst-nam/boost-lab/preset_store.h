/* SPDX-License-Identifier: MIT. Dedicated CF files, never native presets. */
#ifndef CF_PREAMP_PRESET_STORE_H
#define CF_PREAMP_PRESET_STORE_H
#include <stdint.h>
#define CF_PREAMP_PRESETS 13u
#define CF_PREAMP_RECORD_BYTES 32u
#define CF_PREAMP_TABLE_BYTES (CF_PREAMP_PRESETS*CF_PREAMP_RECORD_BYTES)
typedef struct {
    void *user;
    int (*read)(void*,unsigned bank,unsigned offset,void*,unsigned count);
    int (*erase)(void*,unsigned bank);
    int (*write)(void*,unsigned bank,unsigned offset,const void*,unsigned count);
    int (*sync)(void*);
} CfPreampPresetIO;
/* Seven words match PreampConfig. Binding to a CRC of the exact native preset
 * prevents a stale sidecar from overriding a different/restored native preset.
 * CRC is corruption/version binding, not an authenticity guarantee. */
typedef struct {uint32_t mode,enabled,boost,bright,drive_bits,tone_bits,level_bits,preset_crc;} CfPreampRecord;
typedef struct {
    CfPreampPresetIO io;uint32_t product,phase,bank,offset,rolling,generation,transaction,mask,best_bank,have_best;
    uint32_t candidate_generation,candidate_transaction,candidate_mask,candidate_crc,last_preset,candidate_preset;
    uint8_t table[CF_PREAMP_TABLE_BYTES],candidate[CF_PREAMP_TABLE_BYTES],header[64];
} CfPreampPresets;
/* All methods run on the serialized FS owner. IO callbacks address only two
 * dedicated files of <=480 bytes. Missing bytes must read as 0xff. */
int cf_preamp_presets_open(CfPreampPresets*,const CfPreampPresetIO*,unsigned product);
int cf_preamp_presets_pump(CfPreampPresets*); /* <=1 read/write, max256 B */
int cf_preamp_presets_get(CfPreampPresets*,unsigned preset,unsigned native_crc,CfPreampRecord*);
int cf_preamp_presets_save(CfPreampPresets*,unsigned preset,unsigned transaction,const CfPreampRecord*);
unsigned cf_preamp_presets_bytes(void);
int cf_preamp_record_valid(const CfPreampRecord*);
int cf_preamp_presets_idle(const CfPreampPresets*);
#endif
