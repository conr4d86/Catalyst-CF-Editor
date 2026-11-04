/* SPDX-License-Identifier: MIT. Immutable initial resources in the CF image. */
#ifndef CF_ASSET_SEED_H
#define CF_ASSET_SEED_H
#include "asset_store.h"
#define CF_SEED_MAGIC 0x31445343u
typedef struct {const uint8_t *payload;uint32_t length,crc,transaction;CfAssetInfo info;} CfSeedEntry;
typedef struct {uint32_t magic,version,mask,count;uint8_t edition[32];CfSeedEntry entry[5];} CfSeedManifest;
typedef struct {const CfSeedManifest *manifest;uint32_t product;uint8_t header[256];} CfAssetSeed;
/* Trusted bootstrap only. No wire message supplies a manifest or address.
 * Metadata/payload validation is shared with the normal store on recovery.
 * ROM acts as bank 1 only if the native file does NOT EXIST. An existing
 * empty/torn file must remain invalid, never be replaced by this fallback. */
int cf_seed_init(CfAssetSeed*,const CfSeedManifest*,uint32_t product);
int cf_seed_read(CfAssetSeed*,unsigned slot,unsigned bank,unsigned offset,void*,unsigned bytes);
int cf_seed_name(const CfAssetSeed*,unsigned slot,unsigned bank,char *out,unsigned capacity);
unsigned cf_seed_bytes(void);
#endif
