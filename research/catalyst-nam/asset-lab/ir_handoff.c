/* SPDX-License-Identifier: MIT. LAB ONLY: bounded IR preparation, no USB API.
 * A missing magic marker makes the existing IR route use its stock fallback
 * while the FFT/history workspace is prepared. This is not a crossfade.
 */
#include "ir_handoff.h"
#define IR_MAGIC 0x52494358u
#define CTX_MAGIC 0x31485249u
#define IR_RAM ((volatile unsigned*)0x20040000u)
#define FREQUENCIES ((volatile unsigned*)0x20045000u)
#define HISTORY ((volatile unsigned*)0x20040100u)
#define OVERLAP ((volatile unsigned*)0x20044600u)
#define TWIDDLE ((volatile unsigned*)0x20049800u)
#define ROM_TWIDDLE ((const unsigned*)0x601b3400u)
typedef struct {
    unsigned magic,source[3],mask,selected,graph,phase,index,copy_source;
    volatile unsigned state;
    unsigned transaction,kind,slot,pointer;
} IRHandoff;
static IRHandoff I;
static void barrier(void){__asm__ volatile("dmb":::"memory");}
int cf_ir_init(unsigned a,unsigned b,unsigned c,unsigned mask){
    if(I.magic==CTX_MAGIC||mask>7u||((mask&1u)&&(!a||(a&3u)))||((mask&2u)&&(!b||(b&3u)))||((mask&4u)&&(!c||(c&3u))))return CF_BAD_INPUT;
    I.source[0]=a;I.source[1]=b;I.source[2]=c;I.mask=mask;I.selected=3;
    I.phase=I.state=I.transaction=0;barrier();I.magic=CTX_MAGIC;return CF_OK;
}
static int request(unsigned tx,unsigned kind,unsigned slot,unsigned pointer){
    if(I.magic!=CTX_MAGIC||!tx||slot>=3)return CF_BAD_INPUT;
    if(I.transaction==tx)return I.kind==kind&&I.slot==slot&&I.pointer==pointer?CF_ALREADY:CF_STALE;
    if(I.state==1||I.state==3||I.phase)return CF_BUSY;
    I.state=3;barrier();I.transaction=tx;I.kind=kind;I.slot=slot;I.pointer=pointer;barrier();I.state=1;return CF_OK;
}
int cf_ir_bank_request(unsigned tx,unsigned slot,unsigned source,unsigned bytes){
    if(!source||(source&3u)||bytes!=0x4200u)return CF_BAD_INPUT;
    return request(tx,1,slot,source);
}
int cf_ir_select_request(unsigned tx,unsigned slot,unsigned graph){
    if((graph&3u)||graph<0x20000000u||graph>0x20050000u-24u)return CF_BAD_INPUT;
    return request(tx,2,slot,graph);
}
unsigned cf_ir_boundary(void){
    if(I.magic!=CTX_MAGIC)return 0;
    if(I.state==1&&!I.phase){
        barrier();
        if(I.kind==1){I.source[I.slot]=I.pointer;I.mask|=1u<<I.slot;
            if(I.selected!=I.slot){barrier();I.state=2;return 0;}}
        else{I.selected=I.slot;I.graph=I.pointer;}
        IR_RAM[0]=0;IR_RAM[3]=0;IR_RAM[4]=0;IR_RAM[1]=I.selected;IR_RAM[6]=I.graph;
        IR_RAM[7]=0;barrier();
        if(!(I.mask&(1u<<I.selected))){I.state=2;return 0;}
        I.copy_source=I.source[I.selected];I.index=0;I.phase=1;
    }
    unsigned budget=128;
    while(I.phase&&budget){
        unsigned count=I.phase==1||I.phase==2?4224u:I.phase==3?32u:64u;
        volatile unsigned *destination=I.phase==1?FREQUENCIES:I.phase==2?HISTORY:I.phase==3?OVERLAP:TWIDDLE;
        const unsigned *source=I.phase==1?(const unsigned*)I.copy_source:I.phase==4?ROM_TWIDDLE:0;
        unsigned n=count-I.index;if(n>budget)n=budget;
        for(unsigned k=0;k<n;++k)destination[I.index+k]=source?source[I.index+k]:0;
        I.index+=n;budget-=n;
        if(I.index==count){I.index=0;++I.phase;
            if(I.phase==5){I.phase=0;IR_RAM[4]=0;IR_RAM[3]=0;IR_RAM[7]=1;barrier();IR_RAM[0]=IR_MAGIC;barrier();I.state=2;}}
    }
    return 128-budget;
}
int cf_ir_status(unsigned tx){
    if(I.magic!=CTX_MAGIC||!tx)return CF_BAD_INPUT;
    if(I.transaction!=tx)return CF_STALE;
    return I.state==2?CF_OK:CF_PENDING;
}
