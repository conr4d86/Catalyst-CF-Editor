/* Private hardware-trial adapter. SPDX-License-Identifier: MIT.
 * No USB/update functions. Automatic on Chime 1; fixed -12 dB input trim, independent Boost.
 * All model-state reset work is bounded and performed by the audio owner.
 */
#include "geometry.h"
#include "trial_tables.h"
#include "trial_target.h"
#include "preamp.h"
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
    float drive_scale,boost_scale;unsigned auto_entry,boost_enabled;
    PreampState pedal;PreampConfig pending;unsigned sequence,applied;
    unsigned bank_source[2],active_slot,copy_word;
    unsigned bank_pending[2],bank_state;
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
static unsigned nam_slot(const unsigned *g) {
    unsigned p=g[5];if(p<0x80000000u||p>0x81ffffe0u)return 0;
    const unsigned *d=(const unsigned*)p;unsigned first=1,second=1;
    for(unsigned i=0;i<6;++i){if(d[i]!=cf_nam_signatures[0][i])first=0;if(d[i]!=cf_nam_signatures[1][i])second=0;}
    return first?1u:(second?2u:0u);
}
static int signature(const unsigned *g) {return nam_slot(g)!=0;}
static int audio_buffer(unsigned p) {return !(p&3u)&&p>=0x20000000u&&p<=0x20050000u-128u;}
static int factory_amp(const unsigned *g) {
    unsigned p=g[5];if(p<0x80000000u||p>0x81ffffe0u)return 0;
    const unsigned *d=(const unsigned*)p;
    for(unsigned a=0;a<12;++a) {
        unsigned equal=1;
        for(unsigned i=0;i<6;++i)if(d[i]!=factory_amp_signatures[a][i])equal=0;
        if(equal)return (int)a+1;
    }
    return 0;
}
/* Dedicated scratch in the already copied writable table reservation. */
typedef struct {
    unsigned node,arena,graph,entry,original_input,boost_dirty,boost_native;
    float level;
    float input[32];
} StockPreamp;
static StockPreamp stock __attribute__((section(".stock_data")))={.level=1};
static const unsigned char boost_ids[12]={9,15,13,2,10,10,13,9,21,15,21,15};
static const unsigned char switch_ids[12]={20,20,20,20,20,20,20,20,22,20,22,20};
static void stock_switch(unsigned model){
    if(!stock.node||!model)return;
    unsigned desired=R->pedal.target.mode?0u:R->boost_enabled;
    if(stock.boost_dirty||stock.boost_native!=desired){
        /* Bypass the observer thunk; native UI values remain authoritative. */
        int rc=((int(*)(unsigned,unsigned,unsigned,const void*))0x17f81u)(stock.arena,stock.node,switch_ids[model-1],&desired);
        if(rc<0){fault(10);return;}
        stock.boost_native=desired;stock.boost_dirty=0;
    }
}
static void observe_stack(void) {
    unsigned sp;__asm__ volatile("mov %0, sp":"=r"(sp));
    if(sp<R->min_sp)R->min_sp=sp;
    if((R->want||R->mode)&&sp>=0x20050000u && sp<0x20065d00u)fault(7);
}

static void update_input_gain(void) {
    R->input_gain=0.251188643150958f*R->drive_scale*(R->boost_enabled&&R->pedal.target.mode==0?R->boost_scale*R->pedal.target.level:1.0f);
}
/* Internal control-owner API, never a host-supplied raw address.
 * source must be an immutable, verified 7772-byte staging allocation:
 * 1871 weights followed by 72 silence floats. Retain it until replaced and
 * taken at a later outer audio boundary. State 2 means taken, not audible. */
int cf_nam_bank_request(unsigned slot,unsigned source,unsigned bytes) {
    if(R->magic!=MAGIC||slot<1||slot>2||!source||(source&3u)||bytes!=7772u||source==R->bank_source[slot-1])return -1;
    if(R->bank_state==1||R->bank_state==3)return -2;
    R->bank_state=3;__asm__ volatile("dmb":::"memory");
    R->bank_pending[0]=R->bank_source[0];R->bank_pending[1]=R->bank_source[1];R->bank_pending[slot-1]=source;
    __asm__ volatile("dmb":::"memory");R->bank_state=1;return 0;
}
/* Control-owner read-only acquisition receipt. CF_OK means the outer
 * audio boundary has stopped using the previous source, NOT audible-ready.
 * Same-core, one serialized control producer and one non-reentrant outer
 * audio owner are required. No other reader/DMA may retain source pointers. */
int cf_nam_bank_status(unsigned slot,unsigned source) {
    if(R->magic!=MAGIC||slot<1||slot>2||!source||(source&3u))return 2;
    unsigned state=R->bank_state;
    if(state==3)return 1;
    __asm__ volatile("dmb":::"memory");
    unsigned current=R->bank_source[slot-1],pending=R->bank_pending[slot-1];
    __asm__ volatile("dmb":::"memory");
    if(state!=R->bank_state)return 1;
    if(state==1&&pending==source)return 6;
    return current==source?0:3;
}
static void sync_banks(void) {
    if(R->bank_state!=1)return;
    __asm__ volatile("dmb":::"memory");
    unsigned a=R->bank_pending[0],b=R->bank_pending[1],slot=R->active_slot;
    unsigned changed=(slot==1&&a!=R->bank_source[0])||(slot==2&&b!=R->bank_source[1]);
    R->bank_source[0]=a;R->bank_source[1]=b;
    if(changed){R->copy_word=0;R->auto_entry=0;R->mode=0;R->reset_done=0;}
    __asm__ volatile("dmb":::"memory");R->bank_state=2;
}
/* Single control producer; audio owner consumes complete even revisions. */
int cf_preamp_request(const PreampConfig *c) {
    if(R->magic!=MAGIC)return -1;
    if(!c||c->mode>3||c->enabled>1||c->boost>1||c->bright>1||
       !finite(c->drive)||!finite(c->tone)||!finite(c->level)||
       c->drive<0||c->drive>1||c->tone<0||c->tone>1||c->level<0||c->level>2)return -1;
    unsigned next=(R->sequence+2u)&~1u;R->sequence=next|1u;
    __asm__ volatile("dmb":::"memory");R->pending=*c;
    __asm__ volatile("dmb":::"memory");R->sequence=next;return 0;
}
static void sync_preamp(void) {
    unsigned seq=R->sequence;
    if(!(seq&1u)&&seq!=R->applied){
        __asm__ volatile("dmb":::"memory");PreampConfig next=R->pending;
        __asm__ volatile("dmb":::"memory");
        if(seq==R->sequence&&preamp_configure((PreampState*)&R->pedal,&next)==0)R->applied=seq;
    }
    PreampConfig c=R->pedal.target;c.drive=R->boost_scale-1.0f;c.enabled=R->boost_enabled;
    preamp_configure((PreampState*)&R->pedal,&c);update_input_gain();
}
static void apply_control(unsigned param,const unsigned *value,int rc) {
    if(rc<0||R->magic!=MAGIC)return;
    union {unsigned u;float f;} v={value[1]};
    float f=value[0]==3?v.f:((value[0]==1||value[0]==2||value[0]==5)?(float)value[1]:-1.0f);
    if(!finite(f)||f<0||f>1)return;
    if(param==TARGET_BOOSTSWITCH){R->boost_enabled=f>=0.5f;update_input_gain();return;}
    /* Amount maps to a transparent pre-NAM gain of 1..2 (0..6.02 dB).
     * This is a level boost, not the original Chime boost algorithm.
     */
    if(param==TARGET_BOOST){R->boost_scale=1.0f+f;update_input_gain();return;}
    if(param==TARGET_DRIVE){
        unsigned index=(unsigned)(f*255.0f+0.5f);if(index>255)index=255;
        R->drive_scale=drive_table[index];update_input_gain();
    }
    if(param==TARGET_CHVOL)R->level=2.0f*f;
    unsigned band=4;
    if(param==TARGET_BASS)band=0;if(param==TARGET_MID)band=1;
    if(param==TARGET_TREBLE)band=2;if(param==TARGET_PRESENCE)band=3;
    if(band<4)R->eq_index[band]=(unsigned)(f*20.0f+0.5f);
}

static int stock_route(const unsigned *arena,const unsigned *g,unsigned model){
    if(arena[0x14c/4]!=128||stock.entry)return 0;
    R->route_reject=4;
    if(!stock.node||((const unsigned*)stock.node)[5]!=(unsigned)g)return 0;
    unsigned count=g[3],ptr=g[2];
    if(count<2||count>128||(ptr&3u)||ptr<0x20000000u||ptr>0x20050000u-count*20)return 0;
    unsigned *e=(unsigned*)ptr;
    if(e[0]!=1||e[1]!=1||!audio_buffer(e[3]))return 0;
    sync_preamp();stock_switch(model);
    float level=R->boost_enabled?R->pedal.target.level:1.0f;
    if(!R->pedal.target.mode&&level==1.0f&&stock.level==1.0f)return 0;
    for(unsigned i=0;i<32;i++)stock.input[i]=((const float*)e[3])[i];
    if(R->pedal.target.mode){stock.level=1.0f;preamp_process((PreampState*)&R->pedal,stock.input,stock.input,32);}
    else for(unsigned i=0;i<32;i++){stock.level+=(level-stock.level)*.0078125f;stock.input[i]*=stock.level;}
    stock.graph=(unsigned)g;stock.original_input=e[3];stock.entry=(unsigned)(e+3);
    e[3]=(unsigned)stock.input;
    /* Return zero so the original model, including its own gain/EQ, runs. */
    return 0;
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
    unsigned model=(unsigned)factory_amp((const unsigned*)graph);
    if(model){
        stock.node=(unsigned)node;stock.arena=(unsigned)arena;
        if(param==boost_ids[model-1]){union{unsigned u;float f;}v={data[0]};if(finite(v.f)&&v.f>=0&&v.f<=1)R->boost_scale=1+v.f;}
        if(param==switch_ids[model-1]){R->boost_enabled=data[0]!=0;stock.boost_dirty=1;}
    }
    if(!signature((const unsigned*)graph)) {
        if(model)R->leave_target=1;
        return;
    }
    R->leave_target=0;
    if(nam_slot((const unsigned*)graph)==2u){
        switch(param){
            case 1u:case 6u:case 7u:case 20u:break;
            case 2u:param=TARGET_BASS;break;
            case 3u:param=TARGET_MID;break;
            case 4u:param=TARGET_TREBLE;break;
            case 15u:param=TARGET_BOOST;break;
            default:return;
        }
    }
    if(param!=TARGET_DRIVE&&param!=TARGET_CHVOL&&param!=TARGET_BASS&&param!=TARGET_MID&&
       param!=TARGET_TREBLE&&param!=TARGET_PRESENCE&&param!=TARGET_BOOSTSWITCH&&param!=TARGET_BOOST)return;
    R->ctrl_calls++;R->last_param=param;R->last_raw=data[0];
    unsigned value[2]={param==TARGET_BOOSTSWITCH?1u:3u,data[0]};
    apply_control(param,value,rc);
}

void trial_begin(const unsigned *arena,const unsigned *g) {
    if(R->magic!=MAGIC||arena[0x14c/4]!=128)return;
    unsigned depth=R->depth+1;R->depth=depth;
    if(depth>R->max_depth)R->max_depth=depth;
    if(depth!=1)return;
    sync_banks();
    /* The control task only requests a leave. The audio owner changes mode;
     * independent cab/FX dispatches must not be mistaken for leaving the amp. */
    if(R->leave_target) {
        R->auto_entry=0;
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
        const volatile float *row=(const volatile float*)(R->bank_source[R->active_slot-1]+7484u+ring*12u);
        history[off+col*3]=row[0];history[off+col*3+1]=row[1];history[off+col*3+2]=row[2];
        budget-=3;
        if(++col==length){STATE[ring]=0;col=0;++ring;}
    }
    R->reset_ring=ring;R->reset_col=col;R->reset_done=ring==24;
}

int trial_route(const unsigned *arena,const unsigned *g) {
    if(R->magic!=MAGIC)return 0;
    unsigned slot=nam_slot(g);
    if(!slot||!R->bank_source[slot-1]){
        unsigned model=(unsigned)factory_amp(g);
        return model?stock_route(arena,g,model):0;
    }
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
    if(R->active_slot!=slot){R->active_slot=slot;R->copy_word=0;R->auto_entry=0;}
    sync_preamp();
    if(R->mode==2){R->diagnostic_tick=R->diagnostic_gate;
        if(R->diagnostic_gate>R->gate_tick)R->gate_tick=R->diagnostic_gate;
        R->diagnostic_gate=0;}
    if(!R->auto_entry) {
        R->auto_entry=1;R->fault=0;R->want=1;R->mode=0;R->gate=0;
        R->diagnostic_mute=R->diagnostic_gate=0;R->dwt_on=0;
        R->last_target_cycle=0;R->min_period=0xffffffffu;R->max_period=0;
        R->late_periods=R->late_streak=0;
        R->input_peak=R->output_peak=0;R->clipped_samples=0;
    }
    float *output=(float*)destination;
    if(R->want&&!R->dwt_on){counters();R->root_start=clock_now();}
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
        R->nam_fade=0;R->stock_fade=0;
        for(unsigned b=0;b<4;++b)R->eq_z[b][0]=R->eq_z[b][1]=0;
    }
    if(R->mode==1) {
        if(!R->want){R->mode=0;return 0;}
        if(R->copy_word<1871u){
            unsigned p=R->copy_word,n=1871u-p;if(n>128u)n=128u;
            const unsigned *source=(const unsigned*)R->bank_source[R->active_slot-1];
            unsigned *weights=(unsigned*)WEIGHTS;
            for(unsigned i=0;i<n;++i)weights[p+i]=source[p+i];R->copy_word=p+n;
            for(unsigned i=0;i<32;++i)output[i]=0;return 1;
        }
        if(!R->reset_done)reset_step();
        if(!R->reset_done){for(unsigned i=0;i<32;++i)output[i]=0;return 1;}
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
    for(unsigned i=0;i<32;++i)scaled[i]=input[i];
    if(R->pedal.target.mode)preamp_process((PreampState*)&R->pedal,scaled,scaled,32);
    if(R->pedal.fault){fault(10);for(unsigned i=0;i<32;++i)output[i]=0;return 1;}
    for(unsigned i=0;i<32;++i) {
        if(!finite(input[i])||input[i]>32||input[i]<-32){fault(8);for(unsigned n=0;n<32;++n)output[n]=0;return 1;}
        float magnitude=input[i]<0?-input[i]:input[i];if(magnitude>input_peak)input_peak=magnitude;
        gain+=dg;scaled[i]*=gain;
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
    if(stock.entry&&stock.graph==(unsigned)g){
        *(unsigned*)stock.entry=stock.original_input;stock.entry=0;stock.graph=0;
    }
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
    if(depth==1&&(R->want||R->mode)) {
        unsigned elapsed=clock_now()-R->root_start;
        R->diagnostic_gate+=elapsed;
        if(!R->seen)return;
        if(elapsed>R->max_root)R->max_root=elapsed;
        if(R->mode==2&&(!elapsed||elapsed>(R->core_hz/1500u)*9u/10u))fault(5);
    }
}
unsigned trial_runtime_bytes(void){return sizeof(Trial);}


unsigned cf_audio_is_outer(void){return R->magic==MAGIC&&R->depth==1;}

/* A deliberate native IR workspace rebuild is a scheduling discontinuity,
 * not a steady-state sample period. The audio owner alone brackets it.
 * Keep per-dispatch NAM/root cycle limits and all memory/value guards live.
 * A new explicit selection may re-arm only the cadence fallback (9), never
 * corruption, invalid samples, pedal failure or genuine dispatch overrun. */
void cf_audio_discontinuity(void){
    if(R->magic!=MAGIC)return;
    R->last_target_cycle=0;R->late_streak=0;
    if(R->fault==9&&!R->want&&R->mode==0)R->auto_entry=0;
}
static unsigned float_bits(float f){union {float f;unsigned u;}v={f};return v.u;}
unsigned cf_audio_timing(unsigned index){switch(index){
    case 0:return R->min_period;case 1:return R->max_period;
    case 2:return R->late_periods;case 3:return R->late_streak;
    case 4:return R->mode;case 5:return R->want;
    case 6:return R->active_slot;case 7:return R->last_nam;
    case 8:return float_bits(R->drive_scale);
    case 9:return float_bits(R->boost_scale);
    case 10:return R->boost_enabled;
    case 11:return float_bits(R->input_gain);
    case 12:return float_bits(R->level);
    case 13:return R->eq_index[0]|(R->eq_index[1]<<8)|(R->eq_index[2]<<16)|(R->eq_index[3]<<24);
    case 14:return R->pedal.target.mode;
    case 15:return R->pedal.target.enabled;
    case 16:return float_bits(R->pedal.drive);
    case 17:return float_bits(R->pedal.tone);
    case 18:return float_bits(R->pedal.level);
    case 19:return float_bits(R->pedal.mix);
    case 20:return R->pedal.fault;
    case 21:return float_bits(R->input_peak);
    case 22:return float_bits(R->output_peak);
    case 23:return R->clipped_samples;
    case 24:return R->diagnostic_tick;
    case 25:return R->gate_tick;
    case 26:return R->diagnostic_gate;
    default:return 0xffffffffu;
}}
