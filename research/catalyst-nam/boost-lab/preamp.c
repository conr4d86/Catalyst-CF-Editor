/* SPDX-License-Identifier: MIT.
 * Independent 48-kHz mono feasibility prototype; not a pedal-circuit model.
 * 0: current transparent NAM Boost; 1: EP-style; 2: 808-style; 3: SD-style.
 * No proprietary pedal code, heap, libc, USB, firmware patch or dry amp route.
 * Configure/reset must belong to the same audio owner as process.
 * Polynomial antiderivative antialiasing, no oversampled history convolution.
 * Tonal approximation; real pedal/circuit calibration remains pending.
 */
#include "preamp.h"
static int finite_value(float x) {
    union {float f;unsigned u;} v={x};return (v.u&0x7f800000u)!=0x7f800000u;
}
static float unit(float x) {return x<0?0:(x>1?1:x);}
static float soft(float x,float positive,float negative) {
    float limit=x<0?negative:positive;
    float y=x/limit;y=y>1?1:(y<-1?-1:y);
    return limit*(y-y*y*y*(1.0f/3.0f));
}
static float primitive(float x,float positive,float negative){
    float limit=x<0?negative:positive,a=x<0?-x:x;
    if(a>=limit)return (2.0f/3.0f)*limit*a-.25f*limit*limit;
    float xx=x*x;return .5f*xx-xx*xx/(12.0f*limit*limit);
}
static float antialiased(PreampState *s,float x,float positive,float negative){
    /* up[0] retains only the previous nonlinear input. Keep the existing ABI
     * and reservation unchanged; all obsolete FIR cells remain unused. */
    float previous=s->up[0],y;
    if(!s->up_pos){y=soft(x,positive,negative);s->up_pos=1;}
    else {
        float limit=x<0?negative:positive;
        if((positive==negative||((x<0)==(previous<0)))&&
           x<positive&&x>-negative&&previous<positive&&previous>-negative){
            /* Exact divided difference of the quartic primitive. Factoring
             * avoids subtracting close primitive values and any x-p divide. */
            float sum=x+previous;
            y=.5f*sum-sum*(x*x+previous*previous)/(12.0f*limit*limit);
        }else if(x>=positive&&previous>=positive)y=(2.0f/3.0f)*positive;
        else if(x<=-negative&&previous<=-negative)y=-(2.0f/3.0f)*negative;
        else {
            float delta=x-previous,a=delta<0?-delta:delta;
            if(a<.001f)y=soft(.5f*(x+previous),positive,negative);
            else y=(primitive(x,positive,negative)-primitive(previous,positive,negative))/delta;
        }
    }
    s->up[0]=x;return y;
}
void preamp_init(PreampState *s) {
    unsigned char *p=(unsigned char*)s;
    for(unsigned i=0;i<sizeof(*s);++i)p[i]=0;
    s->target.drive=s->target.tone=s->drive=s->tone=0.5f;
    s->target.level=s->level=1.0f;
}
int preamp_configure(PreampState *s,const PreampConfig *c) {
    if(c->mode>3||c->enabled>1||c->boost>1||c->bright>1||
       !finite_value(c->drive)||!finite_value(c->tone)||!finite_value(c->level)||
       c->drive<0||c->drive>1||c->tone<0||c->tone>1||c->level<0||c->level>2)return -1;
    PreampConfig next=*c;
    if(c->mode!=s->target.mode||(!s->target.enabled&&c->enabled)) {
        preamp_init(s);s->drive=next.drive;s->tone=next.tone;s->level=next.level;
    }
    s->target=next;return 0;
}
static float nonlinear(PreampState *s,float x) {
    float drive=s->drive,y;
    if(s->target.mode==1) {
        float minimum=s->target.boost?1.41253754f:1.0f;
        y=antialiased(s,x*(minimum+(10.0f-minimum)*drive*drive),2.0f,1.8f);
    } else {
        /* High-pass in the drive branch, followed by asymmetric or symmetric
         * soft clipping. This is a compact tonal approximation, not a TS/SD
         * circuit solver or the corresponding Line 6 algorithm.
         */
        s->emphasis+=0.087975f*(x-s->emphasis);
        float emphasis=x-s->emphasis;
        float gain=1.0f+30.0f*drive*drive;
        y=s->target.mode==2?antialiased(s,emphasis*gain,1,1):antialiased(s,emphasis*gain,0.55f,0.9f);
    }
    float a=0.03f+0.35f*s->tone*s->tone;
    a=a*(2.0f-a); /* Same pole rate at 48 kHz as the old 96-kHz shaping. */
    if(s->target.mode==1&&s->target.bright)a=1.0f;
    s->tone_z+=a*(y-s->tone_z);
    y=s->tone_z-s->dc_in+0.998953374f*s->dc_out;
    s->dc_in=s->tone_z;s->dc_out=y;return y;
}
static float wet_sample(PreampState *s,float x) {
    if(s->target.mode==0)return x*(1+s->drive);
    return nonlinear(s,x);
}
void preamp_process(PreampState *s,const float *in,float *out,unsigned count) {
    for(unsigned i=0;i<count;++i) {
        float x=in[i];
        if(!finite_value(x)||x>32||x<-32)s->fault=1;
        if(s->fault){out[i]=0;continue;}
        s->drive+=(s->target.drive-s->drive)*0.0078125f;
        s->tone+=(s->target.tone-s->tone)*0.0078125f;
        s->level+=(s->target.level-s->level)*0.0078125f;
        float desired=s->target.enabled?1.0f:0.0f;
        if(desired>s->mix)s->mix+=0.0078125f;
        else if(desired<s->mix)s->mix-=0.0078125f;
        s->mix=unit(s->mix);
        if(s->mix==0&&!s->target.enabled){out[i]=x;continue;}
        float y=wet_sample(s,x)*s->level;
        y=x+(y-x)*s->mix;
        if(!finite_value(y)){s->fault=1;y=0;}
        out[i]=y;
    }
}
unsigned preamp_state_bytes(void) {return sizeof(PreampState);}
