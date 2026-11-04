/* Private hardware-trial adapter. SPDX-License-Identifier: MIT.
 * No USB/update functions. Off at boot; arm with Boost OFF/ON/OFF/ON.
 * All model-state reset work is bounded and performed by the audio owner.
 */
#include "geometry.h"
#include "trial_tables.h"
#include "trial_target.h"
extern void nam_process(void*,const float*,const float*,float*,unsigned);
typedef struct {
    unsigned magic,want,mode,fault,gate,roots,nam_calls,seen,reset_ring,reset_col,reset_done;
    unsigned max_nam,max_root,last_nam,core_hz,min_sp;
    float input_gain,level,input_current,level_current,nam_fade,stock_fade;
    unsigned eq_index[4];
    float eq_z[4][2];
    unsigned root_start,dwt_on,gate_tick;
    unsigned ctrl_seen,ctrl_calls,last_param,last_raw,arena_bytes,target_frames,route_reject,io_source,io_destination;
    unsigned depth,max_depth,outer_graph,outer_def0,outer_def1,outer_def2,outer_def3,leave_target;
    unsigned last_target_cycle,min_period,max_period,late_periods,late_streak,stock_target_calls,stock_during_nam;
    float input_peak,output_peak;unsigned clipped_samples;
    unsigned diagnostic_mute,diagnostic_gate,diagnostic_tick;
} Trial;
#define R ((volatile Trial*)0x20064900u)
#define STATE ((unsigned*)0x20050040u)
#define WEIGHTS ((const float*)0x20062b40u)
#define DWT_CTRL (*(volatile unsigned*)0xe0001000u)
#define DWT_CYCLE (*(volatile unsigned*)0xe0001004u)
#define DEMCR (*(volatile unsigned*)0xe000edfcu)
#define CORE_HZ (*(volatile unsigned*)0x20023498u)
#define MAGIC 0x324d414eu
static int finite(float f) { union {float f;unsigned u;} v={f};return (v.u&0x7f800000u)!=0x7f800000u; }
static float clamp01(float f) { return f<0?0:(f>1?1:f); }
static unsigned clock_now(void) { return DWT_CYCLE; }
static void fault(unsigned code) { if(!R->fault)R->fault=code; R->mode=3; }
static void counters(void) {
    /* Peripheral setup belongs to the audio owner, not the UI/control task. */
    if(!R->dwt_on) {
        DEMCR=DEMCR|(1u<<24);DWT_CTRL=DWT_CTRL|1;
        R->dwt_on=(DEMCR&(1u<<24))&&(DWT_CTRL&1);
    }
    R->core_hz=CORE_HZ;
    if(R->core_hz<100000000u||R->core_hz>800000000u)fault(1);
    if(!R->dwt_on)fault(2);
}
static int signature(const unsigned *g) {
    unsigned p=g[5];if(p<0x80000000u || p>0x81ffffe0u)return 0;
    const unsigned *d=(const unsigned*)p;
    return d[0]==TARGET_SIG_0&&d[1]==TARGET_SIG_1&&d[2]==TARGET_SIG_2&&
           d[3]==TARGET_SIG_3&&d[4]==TARGET_SIG_4&&d[5]==TARGET_SIG_5;
}
static int audio_buffer(unsigned p) {return !(p&3u)&&p>=0x20000000u&&p<=0x20050000u-128u;}
static int factory_amp(const unsigned *g) {
    unsigned p=g[5];if(p<0x80000000u||p>0x81ffffe0u)return 0;
    const unsigned *d=(const unsigned*)p;
    for(unsigned a=0;a<12;++a) {
        unsigned equal=1;
        for(unsigned i=0;i<6;++i)if(d[i]!=factory_amp_signatures[a][i])equal=0;
        if(equal)return 1;
    }
    return 0;
}
static void observe_stack(void) {
    unsigned sp;__asm__ volatile("mov %0, sp":"=r"(sp));
    if(sp<R->min_sp)R->min_sp=sp;
    if((R->want||R->mode)&&sp>=0x20050000u && sp<0x20065d00u)fault(7);
}

static void apply_control(unsigned param,const unsigned *value,int rc) {
    if(rc<0||R->magic!=MAGIC)return;
    union {unsigned u;float f;} v={value[1]};
    float f=value[0]==3?v.f:((value[0]==1||value[0]==2||value[0]==5)?(float)value[1]:-1.0f);
    if(!finite(f)||f<0||f>1)return;
    if(param==TARGET_BOOSTSWITCH) {
        R->diagnostic_mute=R->diagnostic_gate=0;
        if(!R->want && R->target_frames-R->gate_tick>15000)R->gate=0;
        R->gate_tick=R->target_frames;
        unsigned on=f>=0.5f;
        if(!on) {
            if(R->want||R->mode==3) {R->want=0;R->gate=1;}
            else if(R->gate==2)R->gate=3;
            else if(R->gate!=3)R->gate=1;
        } else if(R->gate==1) R->gate=2;
        else if(R->gate==3) {
            R->fault=0;R->want=1;R->gate=0;
            R->dwt_on=0;
            R->last_target_cycle=0;R->min_period=0xffffffffu;R->max_period=0;
            R->late_periods=R->late_streak=0;R->input_peak=R->output_peak=0;R->clipped_samples=0;
        }
        return;
    }
    if(param==TARGET_DRIVE) {
        /* Private isolation probe while NAM is already active. Four native
         * Gain edges 127/128/127/128 toggle only the final NAM write. The
         * final native Gain value remains 128; CH VOL is never changed.
         * Off at boot, cleared by Boost and leaving the target model.
         */
        if(R->want&&R->mode==2) {
            unsigned value=(unsigned)(f*255.0f+0.5f);
            unsigned gate=R->diagnostic_gate;
            if(R->target_frames-R->diagnostic_tick>15000)gate=0;
            R->diagnostic_tick=R->target_frames;
            if(value==((gate&1u)?128u:127u)) {
                if(++gate==4){R->diagnostic_mute^=1u;gate=0;}
            } else gate=value==127u?1u:0u;
            R->diagnostic_gate=gate;
        } else R->diagnostic_gate=0;
        R->input_gain=drive_table[(unsigned)(f*256.0f+0.5f)];
        if(!R->want && R->mode==0)R->gate=0;
    }
    if(param==TARGET_CHVOL) R->level=2.0f*f;
    unsigned band=4;
    if(param==TARGET_BASS)band=0;if(param==TARGET_MID)band=1;
    if(param==TARGET_TREBLE)band=2;if(param==TARGET_PRESENCE)band=3;
    if(band<4)R->eq_index[band]=(unsigned)(f*20.0f+0.5f);
}

/* Observe the native DSP-node setter shared by scalar and bulk calls.
 * Identify the loaded Chime graph itself; no assumed high-level slot address.
 * The original setter has already validated/consumed the same scalar payload.
 */
void trial_node_control(const unsigned *arena,const unsigned *node,unsigned param,const unsigned *data,int rc) {
    (void)arena;
    if(R->magic!=MAGIC)return;
    R->ctrl_seen++;
    if(rc<0||!node||!(node[3]&1u))return;
    unsigned graph=node[5];
    if(graph<0x20000000u||graph>0x20050000u-24)return;
    if(!signature((const unsigned*)graph)) {
        if(factory_amp((const unsigned*)graph))R->leave_target=1;
        return;
    }
    R->leave_target=0;
    if(param!=TARGET_DRIVE&&param!=TARGET_CHVOL&&param!=TARGET_BASS&&param!=TARGET_MID&&
       param!=TARGET_TREBLE&&param!=TARGET_PRESENCE&&param!=TARGET_BOOSTSWITCH)return;
    R->ctrl_calls++;R->last_param=param;R->last_raw=data[0];
    unsigned value[2]={param==TARGET_BOOSTSWITCH?1u:3u,data[0]};
    apply_control(param,value,rc);
}

void trial_begin(const unsigned *arena,const unsigned *g) {
    if(R->magic!=MAGIC||arena[0x14c/4]!=128)return;
    unsigned depth=R->depth+1;R->depth=depth;
    if(depth>R->max_depth)R->max_depth=depth;
    if(depth!=1)return;
    /* The control task only requests a leave. The audio owner changes mode;
     * independent cab/FX dispatches must not be mistaken for leaving the amp. */
    if(R->leave_target) {
        R->diagnostic_mute=R->diagnostic_gate=0;
        R->want=0;R->mode=0;R->gate=0;R->stock_fade=1;R->leave_target=0;
    }
    /* The real audio owner need not have the laboratory Main definition.
     * Bracket the outermost native dispatch and its recursive children. */
    R->outer_graph=(unsigned)g;
    unsigned p=g[5];
    if(p>=0x80000000u&&p<=0x81ffffe0u) {
        const unsigned *d=(const unsigned*)p;
        R->outer_def0=d[0];R->outer_def1=d[1];R->outer_def2=d[2];R->outer_def3=d[3];
    }
    R->roots++;R->seen=0;observe_stack();
    if(R->want||R->mode) {
        counters();
        R->root_start=clock_now();
    }
}

/* At most 512 history floats per block, avoiding a 76-KiB reset in one IRQ. */
static void reset_step(void) {
    unsigned ring=R->reset_ring,col=R->reset_col,budget=512;
    float *history=(float*)(STATE+24);
    while(ring<24 && budget>=3) {
        unsigned length=ring==23?16:ring_length[ring];
        unsigned off=ring==23?HEAD_RING_OFFSET:ring_offset[ring];
        history[off+col*3]=silence[ring][0];
        history[off+col*3+1]=silence[ring][1];
        history[off+col*3+2]=silence[ring][2];
        budget-=3;
        if(++col==length){STATE[ring]=0;col=0;++ring;}
    }
    R->reset_ring=ring;R->reset_col=col;R->reset_done=ring==24;
}

int trial_route(const unsigned *arena,const unsigned *g) {
    if(R->magic!=MAGIC||!signature(g))return 0;
    R->arena_bytes=arena[0x14c/4];R->target_frames++;
    if(R->arena_bytes!=128){R->route_reject=1;return 0;}
    unsigned count=g[3],ptr=g[2];
    if(count<2||count>128||(ptr&3u)||ptr<0x20000000u||ptr>0x20050000u-count*20){R->route_reject=2;return 0;}
    const unsigned *e=(const unsigned*)ptr;unsigned destination=0;
    for(unsigned i=0;i<count;++i)if(e[i*5]==1&&e[i*5+1]==1)destination=e[i*5+4];
    R->io_source=e[3];R->io_destination=destination;
    if(e[0]!=1||e[1]!=1||!audio_buffer(e[3])||!audio_buffer(destination)){R->route_reject=3;return 0;}
    R->route_reject=0;
    R->seen=1;observe_stack();
    float *output=(float*)destination;
    if(R->want&&!R->dwt_on)counters();
    if(R->want&&R->dwt_on) {
        unsigned now=clock_now(),last=R->last_target_cycle;R->last_target_cycle=now;
        if(last&&R->mode==2) {
            unsigned period=now-last;
            if(period<R->min_period)R->min_period=period;
            if(period>R->max_period)R->max_period=period;
            if(period>(R->core_hz/1500u)*3u/2u){R->late_periods++;R->late_streak++;}
            else R->late_streak=0;
            if(R->late_streak>=3) {
                /* This catches missed whole-audio-frame deadlines even when
                 * the isolated NAM dispatch is below its local cycle limit. */
                R->fault=9;R->want=0;R->mode=0;R->gate=0;R->stock_fade=0;
                return 0;
            }
        }
    }
    if(R->mode==3) {
        if(!R->want){R->mode=0;R->stock_fade=0;return 0;}
        for(unsigned i=0;i<32;++i)output[i]=0;return 1;
    }
    if(R->mode==0 && R->want) {
        R->mode=1;R->reset_ring=R->reset_col=R->reset_done=0;
        R->nam_fade=0;R->stock_fade=1;
        for(unsigned b=0;b<4;++b)R->eq_z[b][0]=R->eq_z[b][1]=0;
    }
    if(R->mode==1) {
        if(!R->want){R->mode=0;return 0;}
        if(!R->reset_done)reset_step();
        if(!R->reset_done||R->stock_fade>0)return 0;
        R->mode=2;R->input_current=R->input_gain;R->level_current=R->level;
    }
    if(R->mode!=2)return 0;
    if(*(const unsigned*)0x20062b38u!=0xa5a5a5a5u ||
       *(const unsigned*)0x20064880u!=0x5a5a5a5au ||
       *(const unsigned*)0x200657e0u!=0xa7a7a7a7u ||
       *(const unsigned*)0x20065c80u!=0xa6a6a6a6u){fault(6);for(unsigned i=0;i<32;++i)output[i]=0;return 1;}
    /* Snapshot all input samples before writing output: native Chime uses
     * 0x20001470 in-place on the unit, unlike the two-buffer lab fixture. */
    const float *input=(const float*)R->io_source;float scaled[32];
    float gain=R->input_current,level=R->level_current;
    float input_peak=R->input_peak,output_peak=R->output_peak;
    float dg=(R->input_gain-gain)*0.03125f,dl=(R->level-level)*0.03125f;
    for(unsigned i=0;i<32;++i) {
        if(!finite(input[i])||input[i]>32||input[i]<-32){fault(8);for(unsigned n=0;n<32;++n)output[n]=0;return 1;}
        float magnitude=input[i]<0?-input[i]:input[i];if(magnitude>input_peak)input_peak=magnitude;
        gain+=dg;scaled[i]=input[i]*gain;
    }
    R->input_current=gain;
    R->input_peak=input_peak;
    unsigned start=clock_now();nam_process((void*)STATE,WEIGHTS,scaled,output,32);
    unsigned elapsed=clock_now()-start;R->last_nam=elapsed;
    if(elapsed>R->max_nam)R->max_nam=elapsed;
    R->nam_calls++;
    if(!elapsed||elapsed>(R->core_hz/1500u)*3u/4u)fault(3);
    float fade=R->nam_fade;
    for(unsigned i=0;i<32;++i) {
        float x=output[i];
        for(unsigned b=0;b<4;++b) {
            unsigned index=R->eq_index[b];if(index>20)index=10;
            const float *c=eq_table[b][index];
            float y=c[0]*x+R->eq_z[b][0];
            R->eq_z[b][0]=c[1]*x+c[3]*y+R->eq_z[b][1];
            R->eq_z[b][1]=c[2]*x+c[4]*y;x=y;
        }
        level+=dl;
        fade=clamp01(fade+(R->want?0.00390625f:-0.00390625f));
        float y=x*level*fade;
        if(!finite(y)){fault(4);y=0;}
        float magnitude=y<0?-y:y;if(magnitude>output_peak)output_peak=magnitude;
        if(y>1||y<-1)R->clipped_samples++;
        output[i]=(R->fault||R->diagnostic_mute)?0:(y>1?1:(y<-1?-1:y));
    }
    R->level_current=level;R->nam_fade=fade;
    R->output_peak=output_peak;
    if(!R->want&&fade==0){R->mode=0;R->stock_fade=0;}
    return 1;
}

void trial_after(const unsigned *arena,const unsigned *g,unsigned handled) {
    if(R->magic!=MAGIC||arena[0x14c/4]!=128)return;
    if(!handled&&signature(g)) {
        R->stock_target_calls++;
        if(R->want&&R->mode==2)R->stock_during_nam++;
    }
    if(!handled&&signature(g)&&R->route_reject==0&&audio_buffer(R->io_destination)&&R->mode!=2&&R->mode!=3) {
        float f=R->stock_fade;
        float step=(R->mode==1&&R->reset_done)?-0.00390625f:0.00390625f;
        float *out=(float*)R->io_destination;
        for(unsigned i=0;i<32;++i){f=clamp01(f+step);out[i]*=f;}
        R->stock_fade=f;
    }
    unsigned depth=R->depth;
    if(depth)R->depth=depth-1;
    if(depth==1&&R->seen&&(R->want||R->mode)) {
        unsigned elapsed=clock_now()-R->root_start;
        if(elapsed>R->max_root)R->max_root=elapsed;
        if(R->mode==2&&(!elapsed||elapsed>(R->core_hz/1500u)*9u/10u))fault(5);
    }
}
unsigned trial_runtime_bytes(void){return sizeof(Trial);}

/* Read-only diagnostic reply after the ordinary universal identity handler.
 * Noncommercial manufacturer 0x7d; each uint32 occupies five 7-bit bytes.
 * Uses the existing packet-copy sender. Never changes DSP state or presets.
 */
void trial_send_diagnostics(void) {
    union{float f;unsigned u;} in={R->input_peak},out={R->output_peak};
    union{float f;unsigned u;} gain={R->input_gain},level={R->level};
    /* Read the exact immutable weight storage consumed by nam_process.
     * FNV-1a is a corruption/load fingerprint, not a security signature.
     * No audio histories, controls or runtime fields are changed here.
     */
    unsigned digest=2166136261u;
    const unsigned char *weights=(const unsigned char*)WEIGHTS;
    for(unsigned i=0;i<1871u*4u;++i)digest=(digest^weights[i])*16777619u;
    unsigned values[]={0x00020005u,R->magic,R->want,R->mode,R->fault,R->gate,
        R->roots,R->nam_calls,R->ctrl_calls,R->ctrl_seen,R->last_param,R->last_raw,
        R->arena_bytes,R->target_frames,R->route_reject,R->io_source,R->io_destination,
        R->max_nam,R->max_root,R->core_hz,R->min_sp,
        R->depth,R->max_depth,R->outer_graph,R->outer_def0,R->outer_def1,R->outer_def2,R->outer_def3,R->leave_target,
        R->last_target_cycle,R->min_period,R->max_period,R->late_periods,R->late_streak,R->stock_target_calls,R->stock_during_nam,
        in.u,out.u,R->clipped_samples,R->diagnostic_mute,R->diagnostic_gate,R->diagnostic_tick,
        digest,gain.u,level.u,R->eq_index[0],R->eq_index[1],R->eq_index[2],R->eq_index[3]};
    unsigned char packet[8+5*49+1];
    packet[0]=0xf0;packet[1]=0x7d;packet[2]='C';packet[3]='N';
    packet[4]='A';packet[5]='M';packet[6]=5;packet[7]=49;
    unsigned p=8;
    for(unsigned i=0;i<49;++i)for(unsigned j=0;j<5;++j)packet[p++]=(values[i]>>(j*7))&127u;
    packet[p]=0xf7;
    ((void(*)(const unsigned char*,unsigned))0x800174f5u)(packet,p+1);
}
