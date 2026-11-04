/* Internal API, NOT a raw-address USB operation. */
#ifndef CF_IR_HANDOFF_LEASED_H
#define CF_IR_HANDOFF_LEASED_H
#include "ir_handoff.h"
/* Only after cf_ir_status(tx)==CF_OK may the control owner overwrite the
 * immutable FFT source. Removes ALL references, including unselected slots.
 * During/after eviction the affected route is factory fallback, not crossfade.
 * Requires a single non-reentrant outer audio owner and no external readers. */
int cf_ir_evict_request(unsigned transaction,unsigned immutable_fft);
int cf_ir_supersede(unsigned slot,unsigned graph);
#endif
