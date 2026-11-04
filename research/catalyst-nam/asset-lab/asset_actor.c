/* SPDX-License-Identifier: MIT. Native-task integration LAB, not firmware.
 * cf_actor_init must be called with a verified mounted FS and native queue.
 * Every filesystem operation runs on its existing serialized owner, never
 * from cf_rx_byte/MidiTask. No format, raw flash, heap or identity hook exists.
 */
#include "asset_wire.h"
typedef struct {
    volatile unsigned state,used,cookie;
    unsigned char packet[CF_WIRE_MAX];
} PacketSlot;
typedef struct {
    unsigned magic,lfs,queue,send,rx_slot,rx_cookie,pump_queued;
    unsigned posted,processed,sent,malformed,dropped;
    PacketSlot ring[2];
    CfAssetStore store;CfAssetReceiver receiver;
    CfWireMessage incoming,outgoing;
    unsigned char reply[CF_WIRE_MAX];
    unsigned file[32],filecfg[3];unsigned char cache[256];
} Actor;
static Actor A;
static unsigned original_dispatch;
#define ACTOR_MAGIC 0x31414643u
#define FILE ((void*)A.file)
#define LFS ((void*)A.lfs)
#define OPEN ((int(*)(void*,void*,const char*,unsigned,void*))0x8002cffdu)
#define SEEK ((int(*)(void*,void*,unsigned,unsigned))0x8002d329u)
#define READ ((int(*)(void*,void*,void*,unsigned))0x80029d3du)
#define WRITE ((int(*)(void*,void*,const void*,unsigned))0x80029819u)
#define CLOSE ((int(*)(void*,void*))0x8002d269u)
#define POST ((int(*)(unsigned,const void*,unsigned,unsigned))0x8004c831u)
#define ORIGINAL ((unsigned(*)(const void*))0x80015dc5u)
void *__aeabi_memcpy(void *out,const void *in,unsigned bytes){for(unsigned i=0;i<bytes;++i)((unsigned char*)out)[i]=((const unsigned char*)in)[i];return out;}
void *__aeabi_memcpy4(void *out,const void *in,unsigned bytes){return __aeabi_memcpy(out,in,bytes);}
static void clear(void *out,unsigned n){for(unsigned i=0;i<n;++i)((unsigned char*)out)[i]=0;}
static void barrier(void){__asm__ volatile("dmb":::"memory");}
static const char *const names[5][2]={
    {"cfcx_asset_0_0","cfcx_asset_0_1"},{"cfcx_asset_1_0","cfcx_asset_1_1"},
    {"cfcx_asset_2_0","cfcx_asset_2_1"},{"cfcx_asset_3_0","cfcx_asset_3_1"},
    {"cfcx_asset_4_0","cfcx_asset_4_1"}};
static int open(unsigned slot,unsigned bank,unsigned flags){
    if(slot>=5||bank>=2)return -1;clear(A.file,sizeof(A.file));A.filecfg[0]=(unsigned)A.cache;A.filecfg[1]=A.filecfg[2]=0;
    return OPEN(LFS,FILE,names[slot][bank],flags,A.filecfg);
}
static int read_asset(void *user,unsigned slot,unsigned bank,unsigned offset,void *data,unsigned count){
    (void)user;if(offset>CF_BANK_BYTES||count>CF_BANK_BYTES-offset)return -1;
    int rc=open(slot,bank,1);
    if(rc==-2){for(unsigned i=0;i<count;++i)((unsigned char*)data)[i]=255;return 0;}
    if(rc)return -1;
    rc=SEEK(LFS,FILE,offset,0);
    int got=rc==(int)offset?READ(LFS,FILE,data,count):-1,closed=CLOSE(LFS,FILE);
    if(got<0||got>(int)count||closed)return -1;
    for(unsigned i=(unsigned)got;i<count;++i)((unsigned char*)data)[i]=255;return 0;
}
static int erase_asset(void *user,unsigned slot,unsigned bank){(void)user;if(open(slot,bank,0x503))return -1;return CLOSE(LFS,FILE);}
static int write_asset(void *user,unsigned slot,unsigned bank,unsigned offset,const void *data,unsigned count){
    (void)user;if(offset>CF_BANK_BYTES||count>CF_BANK_BYTES-offset||open(slot,bank,0x103))return -1;
    int rc=SEEK(LFS,FILE,offset,0),written=rc==(int)offset?WRITE(LFS,FILE,data,count):-1;
    int closed=CLOSE(LFS,FILE);return written==(int)count&&!closed?0:-1;
}
/* Every write/erase above closes and synchronizes its file through native lfs.
 * This says nothing about physical HAL durability, which is still untested. */
static int sync_assets(void *user){(void)user;return 0;}
int cf_actor_init(unsigned product,unsigned lfs,unsigned queue,unsigned send){
    if(A.magic==ACTOR_MAGIC||!lfs||!queue||!send)return CF_BAD_INPUT;
    clear(&A,sizeof(A));A.lfs=lfs;A.queue=queue;A.send=send;A.rx_slot=2;
    CfAssetIO io={(void*)&A,read_asset,erase_asset,write_asset,sync_assets};
    int rc=cf_asset_init(&A.store,&io,product);if(rc)return rc;
    rc=cf_receiver_init(&A.receiver,&A.store,CF_DATA_WRITE);if(rc)return rc;
    barrier();A.magic=ACTOR_MAGIC;return 0;
}
static int post(unsigned opcode,unsigned index,unsigned cookie){
    unsigned words[6]={opcode|(index<<8),ACTOR_MAGIC,cookie,0,0,0};
    return POST(A.queue,words,0,0)==1;
}
void cf_rx_byte(unsigned byte){
    if(A.magic!=ACTOR_MAGIC||byte>255||byte>=0xf8)return;
    if(byte==0xf0){
        if(A.rx_slot<2){A.ring[A.rx_slot].state=0;A.rx_slot=2;}
        for(unsigned i=0;i<2;++i)if(!A.ring[i].state){A.rx_slot=i;A.ring[i].used=0;A.ring[i].state=1;break;}
        if(A.rx_slot>=2)A.dropped++;
    }
    if(A.rx_slot>=2)return;PacketSlot *slot=&A.ring[A.rx_slot];unsigned used=slot->used;
    if(used>=CF_WIRE_MAX||(byte>=0x80&&byte!=0xf0&&byte!=0xf7)){slot->state=0;A.rx_slot=2;A.dropped++;return;}
    slot->packet[used++]=(unsigned char)byte;slot->used=used;
    const unsigned char match[6]={0xf0,0x7d,'C','F','C','X'};
    if(used<=6&&byte!=match[used-1]){slot->state=0;A.rx_slot=2;return;}
    if(byte==0xf7){
        unsigned index=A.rx_slot;slot->cookie=++A.rx_cookie;barrier();slot->state=2;barrier();
        if(post(6,index,slot->cookie))A.posted++;else{slot->state=0;A.dropped++;}
        A.rx_slot=2;
    }
}
static void schedule_pump(void){
    if(A.store.phase==2&&!A.pump_queued){A.pump_queued=1;if(!post(7,0,0))A.pump_queued=0;}
}
unsigned cf_fs_dispatch(const unsigned *request){
    if(!request)return 0;
    if(A.magic!=ACTOR_MAGIC||(request[0]&255u)<6u||request[1]!=ACTOR_MAGIC){
        /* Mirror native 0x8002f218: a nonzero return replaces the handler and
         * immediately reprocesses the SAME request. Keep that state internally
         * so the outer dispatcher never replaces the CF wrapper itself. */
        unsigned fn=original_dispatch?original_dispatch:(unsigned)ORIGINAL;
        for(unsigned n=0;n<16;++n){unsigned next=((unsigned(*)(const void*))fn)(request);
            if(!next)return 0;original_dispatch=fn=next;}
        A.dropped++;return 0;
    }
    unsigned opcode=request[0]&255u;
    if(opcode==7){A.pump_queued=0;if(A.store.phase==2)cf_receiver_pump(&A.receiver);schedule_pump();return 0;}
    unsigned index=(request[0]>>8)&255u;if(opcode!=6||index>=2)return 0;
    PacketSlot *slot=&A.ring[index];barrier();
    if(slot->state!=2||slot->cookie!=request[2])return 0;slot->state=3;barrier();
    if(cf_wire_decode(slot->packet,slot->used,&A.incoming)==1){
        clear(&A.outgoing,sizeof(A.outgoing));cf_receiver_handle(&A.receiver,&A.incoming,&A.outgoing);
        if(A.outgoing.opcode&64u){unsigned n=cf_wire_encode(&A.outgoing,A.reply,sizeof(A.reply));
            if(n){((void(*)(const void*,unsigned))A.send)(A.reply,n);A.sent++;}}
    }else A.malformed++;
    A.processed++;barrier();slot->state=0;schedule_pump();return 0;
}
unsigned cf_actor_bytes(void){return sizeof(A);}
unsigned cf_actor_address(void){return (unsigned)&A;}
/* Laboratory entry point; not exposed as a wire operation. */
unsigned cf_actor_store_address(void){return (unsigned)&A.store;}
unsigned cf_actor_stat(unsigned index){
    switch(index){case 0:return A.posted;case 1:return A.processed;case 2:return A.sent;
    case 3:return A.malformed;case 4:return A.dropped;case 5:return A.store.phase;default:return 0xffffffffu;}
}
