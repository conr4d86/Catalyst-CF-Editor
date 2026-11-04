"""Native reset copy/zero loops and FFT with ITCM/DTCM placement, offline only.

SystemInit and SDRAM-controller initialization are explicit hardware stubs.
The original Reset_Handler performs all application memory copies and clears.
No peripheral timing or actual hardware boot is claimed.
"""
from build_tcm_ir import *
from emulate_graph import FirmwareMachine
from verify_extension_graph import bind_cabinet, switch
from verify_partitioned_ir import process, load_ir
from unicorn import Uc, UC_ARCH_ARM, UC_MODE_THUMB, UC_MODE_MCLASS, UC_HOOK_CODE, UC_HOOK_MEM_READ
from unicorn.arm_const import *
import random

def bootstrap(image):
 u=Uc(UC_ARCH_ARM,UC_MODE_THUMB|UC_MODE_MCLASS);u.ctl_set_cpu_model(UC_CPU_ARM_CORTEX_M7)
 for a,n in [(0,0x18000),(0x20000000,0x68000),(0x60000000,0x200000),
             (0x80000000,0x2000000),(0x40000000,0x600000),(0xe000e000,0x2000)]:u.mem_map(a,n)
 u.mem_write(0x60001000,image)
 u.mem_write(CODE,b'\xa5'*TCM_CODE_BYTES)
 u.mem_write(0x2003ae30,b'\xa5'*(0x20050000-0x2003ae30))
 def skip_hardware(uc,a,size,user):uc.reg_write(UC_ARM_REG_PC,uc.reg_read(UC_ARM_REG_LR))
 for a in (0x60038c84,0x60038da8):u.hook_add(UC_HOOK_CODE,skip_hardware,begin=a,end=a)
 u.reg_write(UC_ARM_REG_SP,0x20068000)
 u.emu_start(0x600388f9,0x80000014,count=20000000)
 assert u.reg_read(UC_ARM_REG_PC)==0x80000014,hex(u.reg_read(UC_ARM_REG_PC))
 assert bytes(u.mem_read(CODE,TCM_CODE_BYTES))==image[FLASH-0x60001000:FLASH-0x60001000+TCM_CODE_BYTES]
 assert bytes(u.mem_read(0x2003ae30,0x20050000-0x2003ae30))==b'\0'*(0x20050000-0x2003ae30)
 for m in MAPS:
  offset=m['source']-0x60001000;assert bytes(u.mem_read(m['destination'],m['end']-m['destination']))==image[offset:offset+m['end']-m['destination']]
 return u

def machine(image,boot,arm=True):
 m=FirmwareMachine(image=image);m.quiet=True
 for a,n in [(0,0x18000),(0x20000000,0x68000),(0x80000000,0x1000000)]:
  m.u.mem_write(a,bytes(boot.mem_read(a,n)))
 # Hardware init is stubbed only in the reset test. DSP loader notifications
 # and scheduler boundaries retain the declared functional-fixture stubs.
 m.initialize();m.load_main();bind_cabinet(m)
 assert m.get(RAM+0x1c)==0,'IR armed automatically during bootstrap'
 if arm:
  switch(m,257);switch(m,238);assert m.get(RAM+0x1c)==1
 return m

if __name__=='__main__':
 image,plan,sources=build();boot=bootstrap(image)
 m=machine(image,boot)
 h=[v*IR_TRIM for v in load_ir()];signal=[1.0]+[0.0]*2111
 actual=process(m,signal);error=max(abs(a-b) for a,b in zip(actual,h+[0.0]*64));assert error<1e-6,error
 assert bytes(m.u.mem_read(FREQUENCIES,0x4200))==image[FLASH-0x60001000+0x3000:FLASH-0x60001000+0x7200]
 assert bytes(m.u.mem_read(TWIDDLE,0x100))==image[FLASH-0x60001000+0x7400:FLASH-0x60001000+0x7500]
 # Verify that processing does not fetch code or read frequency/state data
 # from the memory-mapped flash. Initial parameter preparation may read it.
 fetched_flash=[];read_flash=[]
 def check_pc(uc,a,size,user):
  if 0x60000000<=a<0x60200000:fetched_flash.append(hex(a))
 rng=random.Random(13456);signal=[rng.uniform(-.1,.1) for _ in range(8192)]
 reset=next(int(v['address'],16) for v in plan['functions'] if v['name']=='reset')
 m.call(reset,m.hybrid+0x24,label='TCM history reset')
 hook=m.u.hook_add(UC_HOOK_CODE,check_pc)
 def check_read(uc,access,address,size,value,user):
  if 0x60000000<=address<0x60200000:read_flash.append(hex(address))
 read_hook=m.u.hook_add(UC_HOOK_MEM_READ,check_read)
 actual=process(m,signal);expected=[sum(h[k]*signal[n-k] for k in range(min(n+1,2048))) for n in range(len(signal))]
 random_error=max(abs(a-b) for a,b in zip(actual,expected));assert random_error<2e-6,random_error
 m.u.hook_del(hook);m.u.hook_del(read_hook)
 assert not fetched_flash,fetched_flash[:5]
 assert not read_flash,read_flash[:5]
 switch(m,257);switch(m,238);actual=process(m,[1.0]+[0.0]*2111)
 switched_error=max(abs(a-b) for a,b in zip(actual,h+[0.0]*64));assert switched_error<1e-6,switched_error
 # Repeat the actual relocated dispatcher on the M7 model after the graph
 # fixture has prepared the native state. Host graph-loader stubs are absent.
 original=m.u;u=Uc(UC_ARCH_ARM,UC_MODE_THUMB|UC_MODE_MCLASS);u.ctl_set_cpu_model(UC_CPU_ARM_CORTEX_M7)
 for start,end,perms in original.mem_regions():
  u.mem_map(start,end-start+1,perms);u.mem_write(start,bytes(original.mem_read(start,end-start+1)))
 u.reg_write(UC_ARM_REG_C1_C0_2,0xf<<20);u.reg_write(UC_ARM_REG_FPEXC,0x40000000);m.u=u
 m.call(reset,m.hybrid+0x24,label='M7 relocated FFT reset')
 actual=process(m,[1.0]+[0.0]*2111);m7_error=max(abs(a-b) for a,b in zip(actual,h+[0.0]*64));assert m7_error<1e-6,m7_error
 report=dict(scope=__doc__,status='PASS OFFLINE; NO HARDWARE TEST',
  segment_sha256=plan['patched_segment_sha256'],native_reset_reaches_application=True,
  itcm_copy_verified=True,dtcm_zero_reservation_verified=True,all_original_copy_rows_verified=True,
  immutable_banks_copied_to_dtcm=True,audio_code_fetches_from_flash=fetched_flash,audio_data_reads_from_flash=read_flash,
  impulse_error=error,random_8192_error=random_error,selection_reset_error=switched_error,
  m7_isa_error=m7_error,bank_configuration_unchanged=True,initial_msp_unchanged=True)
 (OUT/'tcm-ir-tests.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
 print(json.dumps(report,indent=2))
