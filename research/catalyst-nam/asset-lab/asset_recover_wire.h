/* SPDX-License-Identifier: MIT. Experimental opt-in wrapper, not a native hook. */
#ifndef CF_ASSET_RECOVER_WIRE_H
#define CF_ASSET_RECOVER_WIRE_H
#include "asset_wire.h"
#define CF_RECOVER_RECEIVING 4u
#define CF_ABANDON_RECEIVING 7u
/* Same serialized control-owner contract as asset_recover.h. The legacy actor
 * does not call these entrypoints or advertise this capability. */
int cf_recovery_receiver_init(CfAssetReceiver*,CfAssetStore*,uint32_t);
int cf_recovery_receiver_handle(CfAssetReceiver*,const CfWireMessage*,CfWireMessage*);
#endif
