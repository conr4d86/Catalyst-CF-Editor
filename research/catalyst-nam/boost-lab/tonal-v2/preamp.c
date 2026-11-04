/* SPDX-License-Identifier: MIT.
 * PRIVATE OFFLINE CANDIDATE, NOT INSTALLED. Circuit-informed approximation.
 * Linear tone networks derive from the documented TS/SD op-amp circuits.
 * Clipping is an antialiased cubic approximation, not a full diode/FET solve.
 * Two volts per digital unit is an unmeasured calibration assumption.
 * Same public config, 164-byte ABI, no heap/libc/USB or independent dry route.
 */
#include "preamp.h"
#ifndef RATE
#define RATE 48000.0f
#endif
#define SMOOTH 0.0078125f
static int finite_value(float x){union{float f;unsigned u;}v={x};return (v.u&0x7f800000u)!=0x7f800000u;}
static float unit(float x){return x<0?0:(x>1?1:x);}
static float soft(float x,float pos,float neg){
    float limit=x<0?neg:pos,y=x/limit;y=y>1?1:(y<-1?-1:y);
    return limit*(y-y*y*y*(1.0f/3.0f));
}
static float primitive(float x,float pos,float neg){
    float limit=x<0?neg:pos,a=x<0?-x:x,xx=x*x;
    if(a>=limit)return (2.0f/3.0f)*limit*a-.25f*limit*limit;
    return .5f*xx-xx*xx/(12.0f*limit*limit);
}
static float antialiased(PreampState *s,float x,float pos,float neg){
#ifdef PREAMP_NO_ADAA
    s->up[0]=x;return soft(x,pos,neg);
#else
    float previous=s->up[0],y,limit=x<0?neg:pos;
    if(!s->up_pos){y=soft(x,pos,neg);s->up_pos=1;}
    else if((pos==neg||((x<0)==(previous<0)))&&x<pos&&x>-neg&&previous<pos&&previous>-neg){
        float sum=x+previous;y=.5f*sum-sum*(x*x+previous*previous)/(12.0f*limit*limit);
    }else if(x>=pos&&previous>=pos)y=(2.0f/3.0f)*pos;
    else if(x<=-neg&&previous<=-neg)y=-(2.0f/3.0f)*neg;
    else{
        float delta=x-previous,a=delta<0?-delta:delta;
        y=a<.001f?soft(.5f*(x+previous),pos,neg):
          (primitive(x,pos,neg)-primitive(previous,pos,neg))/delta;
    }
    s->up[0]=x;return y;
#endif
}
static float lowpass(float x,float k,float *state){
    /* Trapezoidal one-pole, k = (1/(2*Fs*RC))/(1+1/(2*Fs*RC)). */
    float v=(x-*state)*k,y=v+*state;*state=y+v;return y;
}
static float rc_coefficient(float rc){return 1.0f/(1.0f+2.0f*RATE*rc);}
static void tone_coefficients(unsigned mode,float tone,float *out){
    /* A/B are the two legs of the 20k tone pot. Ideal op-amp nodal solution:
     * D = A*B + (Rg + 1/(s*C))*(A+B)
     * H = [D*(1+s*Rf*Cf)+Rf*A] /
     *     [(1+s*Rf*Cf)*((q+s*Rs*Cp)*D+Rs*B)].
     * The final (1+s*Rf*Cf) pole is evaluated separately for SD-1.
     * TS: Rs/Rf=1k, Cp/C=220n, Rg=220, bias=10k.
     * SD: Rs/Rf=10k, Cp=18n, C=27n, Rg=470, Cf=10n, no bias R.
     * Pot rotation is presently linear in resistance, not a measured W taper.
     */
    float a=20000.0f*tone,b=20000.0f-a,p=a+b;
    float rs=mode==2?1000.0f:10000.0f,rf=rs;
    float cp=mode==2?220e-9f:18e-9f,c=mode==2?220e-9f:27e-9f;
    float rg=mode==2?220.0f:470.0f,q=mode==2?1.1f:1.0f;
    float cf=mode==2?0.0f:10e-9f;
    float d=c*(a*b+rg*p),t=rf*cf;
    float b0=p,b1=d+p*t+rf*a*c,b2=d*t;
    float a0=q*p,a1=q*d+rs*cp*p+rs*b*c,a2=rs*cp*d;
    float k=2*RATE,k2=k*k,den=a0+a1*k+a2*k2;
    out[0]=(b0+b1*k+b2*k2)/den;
    out[1]=(2*b0-2*b2*k2)/den;
    out[2]=(b0-b1*k+b2*k2)/den;
    out[3]=(2*a0-2*a2*k2)/den;
    out[4]=(a0-a1*k+a2*k2)/den;
}
void preamp_init(PreampState *s){
    unsigned char *p=(unsigned char*)s;
    for(unsigned i=0;i<sizeof(*s);++i)p[i]=0;
    s->target.drive=s->target.tone=s->drive=s->tone=.5f;
    s->target.level=s->level=1;
}
int preamp_configure(PreampState *s,const PreampConfig *c){
    if(c->mode>3||c->enabled>1||c->boost>1||c->bright>1||
       !finite_value(c->drive)||!finite_value(c->tone)||!finite_value(c->level)||
       c->drive<0||c->drive>1||c->tone<0||c->tone>1||c->level<0||c->level>2)return -1;
    PreampConfig next=*c;
    unsigned reset=c->mode!=s->target.mode||(!s->target.enabled&&c->enabled);
    unsigned tone_changed=reset||c->tone!=s->target.tone;
    unsigned ep_changed=tone_changed||c->bright!=s->target.bright;
    if(reset){preamp_init(s);s->drive=next.drive;s->tone=next.tone;s->level=next.level;}
    if(c->mode==1&&ep_changed){
        float factor=.25f+3*c->tone*c->tone;
        s->down[12]=rc_coefficient((c->bright?1000.0f:16000.0f)*3.3e-9f/factor);
        if(reset){s->emphasis=s->down[12];s->down[14]=c->boost?1.0f:.70794578f;}
    }
    if(c->mode>=2&&tone_changed){
        tone_coefficients(c->mode,c->tone,s->down);
        if(reset)for(unsigned i=0;i<5;++i)s->down[5+i]=s->down[i];
    }
    if(c->mode==2){
        float rf=51000.0f+500000.0f*c->drive;
        s->down[12]=rc_coefficient(rf*51e-12f);
        if(reset)s->down[13]=s->down[12];
    }
    s->target=next;return 0;
}
static float nonlinear(PreampState *s,float input){
    unsigned mode=s->target.mode;
    float x=input,y;
    if(mode==1){
        /* EP: input coupling and broad-band gain, then coloration. The +3dB
         * switch is OUTPUT attenuation, never a different clipping drive.
         * Neutral extra Tone (50%) gives 16k*3n3 warm RC / 1k*3n3 bright RC.
         * Tone/Level are Catalyst extensions, absent from Kinky Boost OEM UI.
         */
        x-=lowpass(x,rc_coefficient(47e-9f*1000000.0f),&s->up[2]);
        float gain=1.41253754f+(10.0f-1.41253754f)*s->drive*s->drive;
        y=antialiased(s,x*gain,2.0f,1.8f);
        s->emphasis+=(s->down[12]-s->emphasis)*SMOOTH;
        s->down[14]+=((s->target.boost?1.0f:.70794578f)-s->down[14])*SMOOTH;
        y=lowpass(y,s->emphasis,&s->tone_z)*s->down[14];
    }else{
        /* Coupling caps, then non-inverting feedback branch. The unity term
         * belongs INSIDE the pedal approximation, before NAM. No output path
         * bypasses NAM. Linear term is delayed 1/2 sample to match first ADAA.
         */
        float rc1=mode==2?.022e-6f*510000.0f:.047e-6f*470000.0f;
        float rc2=mode==2?1e-6f*10000.0f:.018e-6f*100000.0f;
        x-=lowpass(x,rc_coefficient(rc1),&s->up[2]);
        x-=lowpass(x,rc_coefficient(rc2),&s->up[3]);
        float hp=x-lowpass(x,rc_coefficient(4700.0f*47e-9f),&s->up[4]);
        float rf=mode==2?51000.0f+500000.0f*s->drive:33000.0f+1000000.0f*s->drive;
        float branch=hp*(rf/4700.0f);
        if(mode==2){
            s->down[13]+=(s->down[12]-s->down[13])*SMOOTH;
            branch=lowpass(branch,s->down[13],&s->up[5]);
        }
        float direct=.5f*(x+s->up[1]);
#ifdef PREAMP_NO_ADAA
        direct=x;
#endif
        y=direct+antialiased(s,branch,mode==2?.275f:.55f,.275f);
        s->up[1]=x;
        for(unsigned j=0;j<5;++j)s->down[5+j]+=(s->down[j]-s->down[5+j])*SMOOTH;
        float *c=s->down+5;
        float z=c[0]*y+s->up[6];
        s->up[6]=c[1]*y-c[3]*z+s->up[7];
        s->up[7]=c[2]*y-c[4]*z;
        y=z;
        if(mode==3){
            float k=rc_coefficient(10000.0f*10e-9f);
            y=k*(z+s->down[10])-(2*k-1)*s->down[11];
            s->down[10]=z;s->down[11]=y;
        }
        /* Output loading at full volume: 1k ->100k TS /4k7 ->10k SD. */
        y*=mode==2?.99009901f:.68027211f;
    }
    float dc=y-s->dc_in+.998953374f*s->dc_out;
    s->dc_in=y;s->dc_out=dc;return dc;
}
void preamp_process(PreampState *s,const float *in,float *out,unsigned count){
    for(unsigned i=0;i<count;++i){
        float x=in[i];if(!finite_value(x)||x>32||x<-32)s->fault=1;
        if(s->fault){out[i]=0;continue;}
        s->drive+=(s->target.drive-s->drive)*SMOOTH;
        s->tone+=(s->target.tone-s->tone)*SMOOTH;
        s->level+=(s->target.level-s->level)*SMOOTH;
        float desired=s->target.enabled?1.0f:0.0f;
        if(desired>s->mix)s->mix+=SMOOTH;else if(desired<s->mix)s->mix-=SMOOTH;
        s->mix=unit(s->mix);
        if(s->mix==0&&!s->target.enabled){out[i]=x;continue;}
        float y=(s->target.mode?nonlinear(s,x):x*(1+s->drive))*s->level;
        y=x+(y-x)*s->mix;if(!finite_value(y)){s->fault=1;y=0;}
        out[i]=y;
    }
}
unsigned preamp_state_bytes(void){return sizeof(PreampState);}
