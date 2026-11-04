/* Internal laboratory API. Addresses come from the verified control owner,
 * never a raw USB request. No native selection hook or boot init yet. */
#ifndef CF_IR_HANDOFF_H
#define CF_IR_HANDOFF_H
#include "asset_store.h"
int cf_ir_init(unsigned source0,unsigned source1,unsigned source2,unsigned mask);
int cf_ir_bank_request(unsigned transaction,unsigned slot,unsigned immutable_fft,unsigned bytes);
int cf_ir_select_request(unsigned transaction,unsigned slot,unsigned graph);
unsigned cf_ir_boundary(void); /* Call only at the outer audio boundary. */
int cf_ir_status(unsigned transaction);
#endif
