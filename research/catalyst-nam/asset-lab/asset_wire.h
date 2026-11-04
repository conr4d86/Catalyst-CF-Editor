/* SPDX-License-Identifier: MIT. CFCX laboratory protocol, no USB driver. */
#ifndef CF_ASSET_WIRE_H
#define CF_ASSET_WIRE_H
#include "asset_store.h"
#define CF_WIRE_MAX 329u
#define CF_DATA_WRITE 1u
#define CF_AUDIO_ACTIVATE 2u /* Never advertised by the unwired lab receiver. */
enum { CF_HELLO=1,CF_BEGIN=2,CF_CHUNK=3,CF_COMMIT=4,CF_STATUS=5,CF_CANCEL=6 };
typedef struct {
    uint32_t product,session,transaction,number;
    uint8_t opcode,slot;
    uint16_t bytes;
    uint8_t data[CF_CHUNK_BYTES];
} CfWireMessage;
typedef struct {
    CfAssetStore *store;
    uint32_t session,capabilities,last_transaction,last_status;
} CfAssetReceiver;
/* Decode: 0 = unrelated MIDI (pass unchanged), -1 = malformed CFCX, 1 = valid. */
int cf_wire_decode(const uint8_t *packet,unsigned bytes,CfWireMessage *message);
unsigned cf_wire_encode(const CfWireMessage *message,uint8_t *packet,unsigned capacity);
int cf_receiver_init(CfAssetReceiver *r,CfAssetStore *s,uint32_t capabilities);
int cf_receiver_handle(CfAssetReceiver *r,const CfWireMessage *request,CfWireMessage *response);
int cf_receiver_pump(CfAssetReceiver *r);
#endif
