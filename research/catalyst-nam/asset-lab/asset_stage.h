/* SPDX-License-Identifier: MIT. Serialized filesystem owner only; no audio IO. */
#ifndef CF_ASSET_STAGE_H
#define CF_ASSET_STAGE_H
#include "asset_store.h"
typedef struct {
    CfAssetStore *store;
    CfAssetMeta best,candidate;
    uint8_t *destination;
    unsigned capacity,slot,bank,step,offset,rolling,nonzero,have_best,active,ready;
} CfAssetStage;
/* Takes a read lease before any IO. The caller owns an aligned, inactive
 * destination for the entire request. Each pump makes at most ONE <=256-byte
 * read; both banks and the destination are validated before ready becomes 1.
 * The same serialized owner must service this and the receiver. */
int cf_stage_begin(CfAssetStage*,CfAssetStore*,unsigned slot,void*,unsigned capacity);
int cf_stage_pump(CfAssetStage*);
int cf_stage_cancel(CfAssetStage*);
unsigned cf_stage_bytes(void);
#endif
