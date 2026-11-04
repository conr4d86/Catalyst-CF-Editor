/* SPDX-License-Identifier: MIT. Internal, serialized filesystem-owner API. */
#ifndef CF_ASSET_COORDINATOR_H
#define CF_ASSET_COORDINATOR_H
#include "asset_stage.h"
#include "ir_handoff_leased.h"
typedef struct {
    int (*nam_request)(unsigned slot,unsigned immutable_source,unsigned bytes);
    int (*nam_status)(unsigned slot,unsigned immutable_source);
} CfAssetAudio;
typedef struct {
    CfAssetStore *store;CfAssetAudio audio;CfAssetStage stage;
    uint8_t *ir,*nam[2][2];
    unsigned revision[5],loaded_generation[5],current_nam[2];
    unsigned dirty,phase,slot,job_revision,graph,job_graph,transaction,next_transaction;
    unsigned ir_owned,spare,last_status,fault;
} CfAssetCoordinator;
/* Coordinator starts zero-initialized and binds once. Buffers are private,
 * disjoint and inactive when bound, except a NAM buffer
 * explicitly confirmed current by nam_status. No USB packet supplies addresses.
 * nam_status follows asset status codes; request uses 0/-1/-2 from the DSP API.
 * Audio acquisition is not audible-ready. Real init/hook/ownership is separate. */
int cf_coord_init(CfAssetCoordinator*,CfAssetStore*,const CfAssetAudio*,void *ir,
                  void *nam1a,void *nam1b,void *nam2a,void *nam2b);
int cf_coord_request(CfAssetCoordinator*,unsigned slot,unsigned ir_graph);
int cf_coord_pump(CfAssetCoordinator*);
unsigned cf_coord_bytes(void);
#endif
