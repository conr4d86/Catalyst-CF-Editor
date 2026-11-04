/* Internal laboratory owner binding. No wire or boot hook. */
#include "asset_coordinator.h"
extern unsigned cf_actor_store_address(void);
static CfAssetCoordinator Q;
unsigned cf_coord_address(void){return (unsigned)&Q;}
int cf_coord_bind(unsigned ir,unsigned n1a,unsigned n1b,unsigned n2a,unsigned n2b){
    CfAssetAudio api={(int(*)(unsigned,unsigned,unsigned))88253u,(int(*)(unsigned,unsigned))88399u};
    return cf_coord_init(&Q,(CfAssetStore*)cf_actor_store_address(),&api,(void*)ir,(void*)n1a,(void*)n1b,(void*)n2a,(void*)n2b);
}
int cf_coord_start(unsigned slot,unsigned graph){return cf_coord_request(&Q,slot,graph);}
int cf_coord_step(void){return cf_coord_pump(&Q);}
unsigned cf_coord_stat(unsigned index){switch(index){case 0:return Q.phase;case 1:return Q.dirty;case 2:return Q.fault;case 3:return Q.last_status;case 4:return Q.stage.offset;case 5:return Q.stage.active;default:return 0xffffffffu;}}
