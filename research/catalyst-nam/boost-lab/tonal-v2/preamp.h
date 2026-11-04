/* SPDX-License-Identifier: MIT. Isolated feasibility prototype, not firmware. */
typedef struct {
    unsigned mode, enabled, boost, bright;
    float drive, tone, level;
} PreampConfig;
typedef struct {
    PreampConfig target;
    float drive, tone, level, mix;
    float up[8], down[15];
    float emphasis, tone_z, dc_in, dc_out;
    unsigned up_pos, down_pos, fault;
} PreampState;
void preamp_init(PreampState *s);
int preamp_configure(PreampState *s, const PreampConfig *c);
void preamp_process(PreampState *s, const float *in, float *out, unsigned count);
unsigned preamp_state_bytes(void);
