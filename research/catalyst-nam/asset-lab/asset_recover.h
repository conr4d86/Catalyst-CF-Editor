/* SPDX-License-Identifier: MIT. LAB ONLY: no wire opcode or native hook yet. */
#ifndef CF_ASSET_RECOVER_H
#define CF_ASSET_RECOVER_H
#include "asset_store.h"
/* Must run on the SAME serialized control owner as BEGIN/COMMIT/pump/load.
 * Never invoke from a USB callback, ISR or audio owner. No IO is performed.
 * Only an exact, still-receiving transaction can be abandoned. Verification,
 * loader leases and already committed generations cannot be cancelled here. */
int cf_asset_abandon_receiving(CfAssetStore *store,uint32_t product,
                             unsigned slot,const CfAssetMeta *expected);
#endif
