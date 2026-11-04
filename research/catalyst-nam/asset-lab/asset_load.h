/* SPDX-License-Identifier: MIT. Control-owner staging, not audio activation. */
#ifndef CF_ASSET_LOAD_H
#define CF_ASSET_LOAD_H
#include "asset_store.h"
typedef struct {
    CfAssetStore *store;CfAssetMeta meta;
    uint8_t *destination;uint32_t capacity,slot,offset,rolling,ready;
} CfAssetLoad;
/* The destination must be a staging buffer NOT used by audio. Its ownership
 * lasts until success/cancel. The caller must serialize this loader with the
 * receiver/store for the entire operation; this is enforced by phase 3. */
int cf_load_begin(CfAssetLoad*,CfAssetStore*,unsigned slot,void *destination,unsigned capacity);
int cf_load_pump(CfAssetLoad*); /* at most one 256-byte payload read */
int cf_load_cancel(CfAssetLoad*);
#endif
