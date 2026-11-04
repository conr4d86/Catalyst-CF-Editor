/* SPDX-License-Identifier: MIT. Stateless read-only identity query. */
#ifndef CF_ASSET_IDENTITY_H
#define CF_ASSET_IDENTITY_H
#include "asset_wire.h"
#define CF_IDENTITY_QUERY 8u
/* UID is supplied only by the trusted native bootstrap, never by the wire.
 * A port location, model number or generated session nonce is not a UID.
 * This helper does not access fuses, files, clocks or mutable session state. */
int cf_identity_valid(const uint32_t uid[2]);
int cf_identity_reply(const CfAssetReceiver*,const uint32_t uid[2],
                      const CfWireMessage*,CfWireMessage*);
#endif
