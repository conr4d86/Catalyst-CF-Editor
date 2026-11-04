/* Internal FS-owner entrypoints, no public wire commands. */
#include "preset_job.h"
#include "asset_store.h"
extern unsigned cf_actor_store_address(void);
extern int cf_preamp_io_read(void*,unsigned,unsigned,void*,unsigned);
extern int cf_preamp_io_erase(void*,unsigned);
extern int cf_preamp_io_write(void*,unsigned,unsigned,const void*,unsigned);
extern int cf_preamp_io_sync(void*);
static CfPreampPresets P;
static CfPreampPresetJob J;
extern int cf_preamp_read_native(unsigned,unsigned,void*,unsigned);
extern void cf_preamp_owner_native(const unsigned*);
extern int cf_preamp_owner_apply_for(unsigned,const void*);
static int context_apply(const void *r){return cf_preamp_owner_apply_for(J.index,r);}
void cf_owner_native_request(const unsigned *request){cf_preamp_job_native_activity(&J);cf_preamp_owner_native(request);}
void cf_actor_preamp_job_invalidate(void){cf_preamp_job_native_activity(&J);}
unsigned cf_actor_preamp_job_interrupted(void){return J.interrupted;}
int cf_actor_preamp_open(void){CfPreampPresetIO io={0,cf_preamp_io_read,cf_preamp_io_erase,cf_preamp_io_write,cf_preamp_io_sync};return cf_preamp_presets_open(&P,&io,((CfAssetStore*)cf_actor_store_address())->product);}
int cf_actor_preamp_pump(void){return cf_preamp_presets_pump(&P);}
int cf_actor_preamp_save(unsigned index,unsigned tx,const CfPreampRecord *r){return cf_preamp_presets_save(&P,index,tx,r);}
int cf_actor_preamp_get(unsigned index,unsigned native_crc,CfPreampRecord *out){return cf_preamp_presets_get(&P,index,native_crc,out);}
unsigned cf_actor_preamp_address(void){return (unsigned)&P;}
int cf_actor_preamp_job_init(void){return cf_preamp_job_init(&J,&P,cf_preamp_read_native,context_apply);}
int cf_actor_preamp_job_save(unsigned index,unsigned tx,const CfPreampRecord *r){return cf_preamp_job_save(&J,index,tx,r);}
int cf_actor_preamp_job_load(unsigned index){return cf_preamp_job_load(&J,index);}
int cf_actor_preamp_job_pump(void){return cf_preamp_job_pump(&J);}
unsigned cf_actor_preamp_job_phase(void){return J.phase;}
unsigned cf_actor_preamp_last_transaction(void){return cf_preamp_presets_idle(&P)&&P.have_best?P.transaction:0;}
unsigned cf_actor_preamp_last_index(void){return cf_preamp_presets_idle(&P)&&P.have_best?P.last_preset:13;}
int cf_actor_preamp_saved(unsigned tx,unsigned index,void *out){
    if(!out||!cf_preamp_presets_idle(&P)||!P.have_best||P.transaction!=tx||P.last_preset!=index||J.interrupted||!J.offset||J.phase==1)return CF_STALE;
    return cf_preamp_presets_get(&P,index,J.rolling^0xffffffffu,(CfPreampRecord*)out);
}
