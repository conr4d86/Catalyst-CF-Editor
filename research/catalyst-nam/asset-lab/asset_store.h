/* SPDX-License-Identifier: MIT. Laboratory storage core, NOT wired to hardware.
 * IO callbacks must address only dedicated CF asset files/banks. Never call
 * from the audio owner or a USB ISR. The physical littlefs adapter is pending.
 */
#ifndef CF_ASSET_STORE_H
#define CF_ASSET_STORE_H
#include <stdint.h>
#define CF_ASSET_SLOTS 5u /* IR1, IR2, IR3, NAM1, NAM2 */
#define CF_BANK_BYTES 32768u
#define CF_HEADER_BYTES 256u
#define CF_CHUNK_BYTES 256u
enum { CF_OK=0, CF_BUSY=1, CF_BAD_INPUT=2, CF_STALE=3, CF_IO=4,
       CF_INVALID_DATA=5, CF_PENDING=6, CF_EMPTY=7, CF_ALREADY=8,
       CF_UNCERTAIN=9 };
typedef struct {
    void *user;
    int (*read)(void*,unsigned,unsigned,unsigned,void*,unsigned);
    int (*erase)(void*,unsigned,unsigned);
    int (*write)(void*,unsigned,unsigned,unsigned,const void*,unsigned);
    int (*sync)(void*);
} CfAssetIO;
typedef struct {
    uint32_t flags,input_db_bits,output_db_bits,low_hz_bits,high_hz_bits,name_bytes;
    uint8_t name[128],source_sha256[32];
} CfAssetInfo;
typedef struct {
    uint32_t generation,transaction,length,crc,bank;
    CfAssetInfo info;
} CfAssetMeta;
typedef struct {
    CfAssetIO io;
    uint32_t product,phase,transaction,slot,bank,generation,length,expected;
    uint32_t received,rolling,checked,verified,nonzero;
    uint8_t scratch[CF_CHUNK_BYTES];
    CfAssetInfo info;
} CfAssetStore;
uint32_t cf_crc32(uint32_t crc,const void *data,unsigned count);
int cf_asset_init(CfAssetStore *s,const CfAssetIO *io,uint32_t product);
int cf_asset_recover(CfAssetStore *s,unsigned slot,CfAssetMeta *meta);
int cf_asset_begin(CfAssetStore *s,uint32_t tx,unsigned slot,uint32_t base_generation,
                   uint32_t bytes,uint32_t crc);
int cf_asset_begin_with_info(CfAssetStore *s,uint32_t tx,unsigned slot,uint32_t base_generation,
                            uint32_t bytes,uint32_t crc,const CfAssetInfo *info);
int cf_asset_chunk(CfAssetStore *s,uint32_t tx,uint32_t offset,const void *data,unsigned bytes);
int cf_asset_commit(CfAssetStore *s,uint32_t tx);
int cf_asset_pump(CfAssetStore *s); /* <=256 payload bytes per step */
int cf_asset_cancel(CfAssetStore *s,uint32_t tx);
unsigned cf_asset_store_bytes(void);
#endif
