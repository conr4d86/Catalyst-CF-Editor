"""Observe stock SDRAM setup arguments in an emulator, never a device.

The board's clock and SEMC calls are explicit fixtures. This establishes what
the pinned firmware requests, not physical RAM ownership or initialization.
"""
import hashlib,json,struct,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT.parents[1]/'catalyst-editor/tools-recovery'))
import unicorn,capstone
from unicorn.arm_const import *
import inspect_storage as binary

def main():
    digest=hashlib.sha256(binary.B).hexdigest()
    assert digest=='36abd9792bb633440e99e40990d9fdd8bd76dcf954b5df1d73bc3be05448d6a6'
    runs=[]
    for frequency in (132000000,166000000,198000000):
        uc=unicorn.Uc(unicorn.UC_ARCH_ARM,unicorn.UC_MODE_THUMB|unicorn.UC_MODE_MCLASS)
        uc.ctl_set_cpu_model(UC_CPU_ARM_CORTEX_M7)
        for address,size in ((0,0x4000),(0x20000000,0x68000),(0x40000000,0x400000)):
            uc.mem_map(address,size)
        for row in binary.MAPS:
            if row['destination'] in (0,0x400):uc.mem_write(row['destination'],binary.read(row['destination'],row['end']-row['destination']))
        calls=[]
        def stub(machine,address,size,user):
            if address==0x12dc:
                result=frequency
            elif address in (0x25d8,0x20c8):
                result=0
            elif address==0x2134:
                pointer=machine.reg_read(UC_ARM_REG_R2)
                raw=bytes(machine.mem_read(pointer,48))
                calls.append({'controller':hex(machine.reg_read(UC_ARM_REG_R0)),
                    'chip_select':machine.reg_read(UC_ARM_REG_R1),
                    'clock_hz':machine.reg_read(UC_ARM_REG_R3),
                    'configuration_hex':raw.hex(),
                    'base':hex(struct.unpack_from('<I',raw,4)[0]),
                    'size_kbytes':struct.unpack_from('<I',raw,8)[0]})
                result=0
            else:return
            machine.reg_write(UC_ARM_REG_R0,result)
            machine.reg_write(UC_ARM_REG_PC,machine.reg_read(UC_ARM_REG_LR))
        for address in (0x12dc,0x25d8,0x20c8,0x2134):uc.hook_add(unicorn.UC_HOOK_CODE,stub,begin=address,end=address)
        stop=0x3ff0;uc.reg_write(UC_ARM_REG_SP,0x20068000);uc.reg_write(UC_ARM_REG_LR,stop|1)
        uc.emu_start(0xbd1,stop,count=50000)
        assert uc.reg_read(UC_ARM_REG_PC)==stop and len(calls)==1
        row=calls[0]
        assert row['controller']=='0x402f0000' and row['chip_select']==0 and row['clock_hz']==frequency
        assert row['base']=='0x80000000' and row['size_kbytes']==32768
        runs.append(row)
    result={'passed':True,'hardware_accessed':False,'USB_opened':False,'SDK_loaded':False,'firmware_created':False,
        'stock_sha256':digest,'stock_board_entry':'0xbd0','SDRAM_configuration_requested':["0x80000000","0x82000000"],
        'configuration_matches_NXP_SEMC_layout':True,'physical_memory_ownership_approved':False,
        'reference':'https://raw.githubusercontent.com/nxp-mcuxpresso/mcux-sdk/master/drivers/semc/fsl_semc.h',
        'runs':runs,'limitations':['Clock and SEMC functions are stubbed, no memory-controller initialization is proved',
        'Configured size does not prove free memory, heap/DMA exclusion, cache/MPU attributes or IRQ safety'],
        'sources':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (Path(__file__),ROOT/'inspect_storage.py')}}
    (ROOT/'board-memory-leads.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(json.dumps(result,indent=2))
if __name__=='__main__':main()
