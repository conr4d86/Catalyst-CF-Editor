"""Read-only MPU/cache leads + original SystemInit on synthetic MMIO.
Does not prove physical SDRAM ownership or emulate the rest of the boot.
"""
import hashlib,json,struct,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent;EDITOR=ROOT.parents[1]/'catalyst-editor'
sys.path.insert(0,str(EDITOR/'tools-recovery'))
import unicorn,capstone
from unicorn.arm_const import UC_ARM_REG_SP,UC_ARM_REG_LR,UC_ARM_REG_PC,UC_CPU_ARM_CORTEX_M7
import inspect_storage as binary

def main():
    literals={}
    for p in range(0,len(binary.B)-3,4):
        value=struct.unpack_from('<I',binary.B,p)[0]
        if 0xe000ed90<=value<=0xe000edb8 or value in (0xe000e000,0xe000ed00):literals[binary.runtime(0x60001000+p)]=value
    refs=[];md=capstone.Cs(capstone.CS_ARCH_ARM,capstone.CS_MODE_THUMB);md.detail=True;md.skipdata=True
    regions=[(m['destination'],m['end']-m['destination']) for m in binary.MAPS if not 0x20000000<=m['destination']<0x80000000]+[(0x600388f8,0x800)]
    from capstone.arm import ARM_OP_MEM,ARM_REG_PC
    for start,size in regions:
        for instruction in md.disasm(binary.read(start,size),start):
            if not instruction.mnemonic.startswith('ldr') or instruction.id==0:continue
            operands=[p for p in instruction.operands if p.type==ARM_OP_MEM and p.mem.base==ARM_REG_PC]
            for operand in operands:
                target=((instruction.address+4)&~3)+operand.mem.disp
                if target in literals:refs.append(dict(instruction=hex(instruction.address),value=hex(literals[target]),context=binary.listing(max(start,instruction.address-24),112)))
    runs=[]
    for cache_enabled in (False,True):
        u=unicorn.Uc(unicorn.UC_ARCH_ARM,unicorn.UC_MODE_THUMB|unicorn.UC_MODE_MCLASS);u.ctl_set_cpu_model(UC_CPU_ARM_CORTEX_M7)
        for address,size in ((0x20000000,0x68000),(0x60000000,0x200000),(0x40000000,0x600000),(0xe000e000,0x2000)):u.mem_map(address,size)
        u.mem_write(0x60001000,binary.B);u.mem_write(0xe000ed14,struct.pack('<I',0x30000 if cache_enabled else 0))
        writes=[]
        def record(uc,kind,address,size,value,user):writes.append(dict(address=hex(address),size=size,value=hex(value)))
        u.hook_add(unicorn.UC_HOOK_MEM_WRITE,record,begin=0xe000e000,end=0xe000ffff)
        stop=0x6003fff0;u.reg_write(UC_ARM_REG_SP,0x20068000);u.reg_write(UC_ARM_REG_LR,stop|1)
        u.emu_start(0x60038c85,stop,count=2000000);assert u.reg_read(UC_ARM_REG_PC)==stop
        mpu=[w for w in writes if 0xe000ed90<=int(w['address'],16)<=0xe000edb8]
        runs.append(dict(initial_cache_enabled=cache_enabled,system_init_returned=True,system_control_writes=writes,MPU_writes=mpu,final_CCR=hex(struct.unpack('<I',bytes(u.mem_read(0xe000ed14,4)))[0])))
    report=dict(hardware_accessed=False,SDK_loaded=False,USB_opened=False,firmware_created=False,stock_sha256=hashlib.sha256(binary.B).hexdigest(),
        literal_leads=[dict(address=hex(a),value=hex(v)) for a,v in literals.items()],literal_xrefs=refs,system_init_entry='0x60038c84',system_init_runs=runs,
        physical_memory_ownership_approved=False,execution_permission_approved=False,
        limitations=['MMIO starts at synthetic zero values except the cache flag; no physical hardware state','SystemInit only: later boot/runtime MPU changes are not excluded','No SDRAM-controller, DMA, heap/pool or IRQ ownership proof; no inference from an unused address gap'],
        reference='https://support.arm.com/documentation/dui0646/c/Cortex-M7-Peripherals/Optional-Memory-Protection-Unit/MPU-Region-Attribute-and-Size-Register',
        sources={str(p.relative_to(ROOT.parents[2])):hashlib.sha256(p.read_bytes()).hexdigest() for p in [ROOT/'inspect_mpu.py',ROOT/'inspect_storage.py']})
    (ROOT/'mpu-leads.json').write_text(json.dumps(report,indent=2));print(json.dumps({k:v for k,v in report.items() if k not in ('literal_xrefs','sources')},indent=2))
if __name__=='__main__':main()
