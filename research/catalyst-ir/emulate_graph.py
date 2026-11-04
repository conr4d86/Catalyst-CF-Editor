"""Isolated reconstruction of Catalyst's DSP registries and graph loader.

No device access. Thread/synchronization boundaries are stubbed explicitly;
malloc is rejected, matching the supplied firmware's disabled heap allocator.
This does not emulate boot, the scheduler, USB, audio IO or real-time behavior.
"""
from arm_tools import *
from unicorn import *
from unicorn.arm_const import *
from collections import deque,Counter

class FirmwareMachine:
 def __init__(self,patched=False,image=None):
  self.patched=patched
  if image is None:
   image=FW
   if patched:
    from build_ir_extension import build_extension
    image,self.patch_plan,_=build_extension()
  # Unicorn's M7 model rejects the FP64 conversion used by this firmware's
  # graph loader. MAX executes the same Thumb/VFPv5 instructions for this
  # functional test; Cortex-M exceptions, peripherals and timing are absent.
  self.u=Uc(UC_ARCH_ARM,UC_MODE_THUMB);u=self.u
  u.ctl_set_cpu_model(UC_CPU_ARM_MAX)
  for a,n in [(0,0x20000),(0x20000000,0x80000),(0x60000000,0x200000),
              (0x80000000,0x1000000),(0x83000000,0x20000),(0xe000e000,0x2000)]:u.mem_map(a,n)
  u.mem_write(0x60001000,image)
  for m in MAPS:
   off=m['source']-0x60001000
   u.mem_write(m['destination'],image[off:off+m['end']-m['destination']])
  u.reg_write(UC_ARM_REG_C1_C0_2,0xf<<20);u.reg_write(UC_ARM_REG_FPEXC,0x40000000)
  self.blocks=deque(maxlen=20);self.stubs=Counter();self.stages=[];self.last_call=None
  self.queue_yield=None;self.notifications=[]
  self.block_nodes={};self.block_events=[]
  u.hook_add(UC_HOOK_BLOCK,lambda uc,a,n,user:self.blocks.append(a))
  for a,name in [(0x8004c550,'synchronization-init'),(0x8004c4ec,'event-init'),
                 (0x8004c518,'worker-start'),(0x1385c,'lock'),(0x1386c,'unlock'),(0x1384c,'signal')]:
   def stub(uc,addr,size,user,label=name):
    self.stubs[label]+=1;uc.reg_write(UC_ARM_REG_R0,0);uc.reg_write(UC_ARM_REG_PC,uc.reg_read(UC_ARM_REG_LR))
   u.hook_add(UC_HOOK_CODE,stub,begin=a,end=a)
  def no_malloc(uc,addr,size,user):
   raise RuntimeError(f'Unexpected disabled malloc request: {uc.reg_read(UC_ARM_REG_R0)} bytes at caller {uc.reg_read(UC_ARM_REG_LR):08x}')
  u.hook_add(UC_HOOK_CODE,no_malloc,begin=0x8002d8c8,end=0x8002d8c8)
  def wait_event(uc,addr,size,user):
   obj=uc.reg_read(UC_ARM_REG_R0)-0xa8
   self.queue_yield=obj;uc.emu_stop()
  u.hook_add(UC_HOOK_CODE,wait_event,begin=0x13820,end=0x13820)
  def observe_block(uc,addr,size,user):
   node=uc.reg_read(UC_ARM_REG_R1);desc=self.get(node+0x14)
   try:
    textptr=self.get(desc+0x3c)
    name=bytes(uc.mem_read(textptr,100)).split(b'\0')[0].decode('ascii')
   except Exception:name='?'
   info=dict(node=hex(node),descriptor=hex(desc),type_id=hex(self.get(desc)),name=name,
             kernel=hex(self.get(desc+0x18)),instance=hex(node+0x24),instance_words=[hex(self.get(node+0x24+4*i)) for i in range(17)])
   definition=self.get(node+0x20)
   info['definition']=hex(definition);info['definition_words']=[hex(self.get(definition+4*i)) for i in range(20)]
   self.block_nodes[node]=info;self.block_events.append(info)
  u.hook_add(UC_HOOK_CODE,observe_block,begin=0x80046ff0,end=0x80046ff0)
 def put(self,a,*values):self.u.mem_write(a,struct.pack('<'+'I'*len(values),*values))
 def get(self,a):return struct.unpack('<I',self.u.mem_read(a,4))[0]
 def call(self,a,*args,label=None,count=10000000):
  regs=[UC_ARM_REG_R0,UC_ARM_REG_R1,UC_ARM_REG_R2,UC_ARM_REG_R3]
  for reg,value in zip(regs,args):self.u.reg_write(reg,value)
  sp=0x8301d000
  if len(args)>4:self.put(sp,*args[4:])
  self.u.reg_write(UC_ARM_REG_SP,sp);self.u.reg_write(UC_ARM_REG_LR,0x8301f001)
  self.last_call=dict(name=label or hex(a),address=hex(a),args=[hex(x) for x in args]);self.blocks.clear()
  try:
   self.u.emu_start(a|1,0x8301f000,count=count)
   yields=0
   while self.queue_yield is not None:
    yields+=1
    if yields>100:raise RuntimeError('Scheduler-yield budget exceeded')
    obj=self.queue_yield;self.queue_yield=None
    context=self.u.context_save();parent_sp=self.u.reg_read(UC_ARM_REG_SP);parent_lr=self.u.reg_read(UC_ARM_REG_LR)
    while self.get(obj+0x104):
     desc=0x83000800;payload=0x83000900;self.put(desc,0,0,payload,0x200)
     self.u.reg_write(UC_ARM_REG_R0,obj);self.u.reg_write(UC_ARM_REG_R1,desc)
     self.u.reg_write(UC_ARM_REG_SP,parent_sp-0x1000);self.u.reg_write(UC_ARM_REG_LR,0x8301e001)
     self.u.emu_start(0x8004b4cd,0x8301e000,count=1000000)
     if self.u.reg_read(UC_ARM_REG_PC)!=0x8301e000:raise RuntimeError('Notification drain did not return')
     n=self.get(desc+4);self.notifications.append(dict(queue=hex(obj),token=hex(self.get(desc)),words=n))
    self.u.context_restore(context)
    self.u.reg_write(UC_ARM_REG_R0,0);self.u.reg_write(UC_ARM_REG_PC,parent_lr)
    self.stubs['notification-consumer-schedule']+=1
    self.u.emu_start(parent_lr,0x8301f000,count=count)
   if self.u.reg_read(UC_ARM_REG_PC)!=0x8301f000:raise RuntimeError('Instruction budget reached')
  except Exception as e:
   raise RuntimeError(f'{self.last_call}: {e}; PC={self.u.reg_read(UC_ARM_REG_PC):08x}; blocks={[hex(x) for x in self.blocks]}') from e
  result=self.u.reg_read(UC_ARM_REG_R0)
  self.stages.append(dict(**self.last_call,result=hex(result)))
  if not getattr(self,'quiet',False):print(self.stages[-1],flush=True)
  return result
 def initialize(self):
  scratch=0x83000100
  self.put(scratch,0x8004d5e0,0x75,0x8017775c,0x7500)
  self.call(0x80049b9c,0x8017ec5c,scratch,label='initialize primitive registry')
  self.put(scratch,0x8004d7b4,0x53,0x801723d0,0x5300)
  self.call(0x80049c30,0x801776d0,scratch,label='initialize model registry')
  for obj,base in [(0x80180d7c,0x8004d900),(0x80180d70,0x8004d900),(0x80180d64,0x8005eb6c)]:
   self.call(0x8002f9f0,obj,base,label='initialize memory stream')
  self.call(0x8004938c,0x20026378,0x8015fd14,0x8017ec5c,0x801776d0,0x8004cba4,
            0x80180d7c,0x80180d70,0x80180d64,label='initialize DSP engine',count=50000000)
  self.call(0x80049570,0x20026378,0x83000200,label='get graph-control interface')
  self.call(0x80049578,0x20026378,0x83000204,label='get catalogue interface')
  self.catalogue=self.get(0x83000204);self.control=self.get(0x83000200)
  print('interfaces',hex(self.control),hex(self.catalogue),flush=True)
 def load_main(self):
  self.call(0x8004ab64,self.control,label='allocate root graph slot')
  self.root_slot=self.call(0x8004abb0,self.control,0,label='get root graph slot')
  self.u.mem_write(0x83000300,b'A45_Main\0')
  self.call(0x8004b318,self.catalogue,0x83000300,0x83000208,label='lookup A45_Main symbol')
  self.main_symbol=self.get(0x83000208)
  print('main symbol',self.main_symbol,'root slot',hex(self.root_slot),flush=True)
  self.call(0x8004a914,self.control,self.root_slot,self.main_symbol,label='load factory A45_Main graph',count=50000000)
  self.slot_tree=self.inspect_slots(self.root_slot)
  self.main_blocks=list(self.block_nodes.values());self.block_events.clear()
  self.cab_slot=next(n['address'] for n in self.slot_tree['children'] if n['slot']=='CabSlot')
  self.call(0x8004a914,self.control,int(self.cab_slot,16),238,label='load factory 1x12 cabinet graph',count=50000000)
  self.slot_tree=self.inspect_slots(self.root_slot)
  self.cab_blocks=list({e['node']:e for e in self.block_events}.values())
  print('Cabinet block types',Counter(e['name'] for e in self.cab_blocks),flush=True)
  print(json.dumps(next(n for n in self.slot_tree['children'] if n['slot']=='CabSlot'),indent=1),flush=True)
 def inspect_slots(self,address,seen=None):
  if seen is None:seen=set()
  assert address not in seen;seen.add(address)
  db=json.loads((OUT/'asset-database.json').read_text());symbols=db['symbols'];slots=db['slots'];models=db['models']
  v=[self.get(address+4*i) for i in range(10)]
  node=dict(address=hex(address),slot=slots[v[3]]['name'] if v[3]<len(slots) else hex(v[3]),
            asset=models[v[4]]['name'] if v[4]<len(models) else hex(v[4]),handle=hex(v[1]),fields=[hex(x) for x in v],children=[])
  child=v[5]
  while child:
   node['children'].append(self.inspect_slots(child,seen));child=self.get(child+28)
  return node
 def report(self):
  return dict(scope='offline DSP graph-loader reconstruction; no hardware or USB',
              experimental_extension=self.patched,
              instruction_model='Unicorn ARM MAX for Thumb/VFPv5 FP64 support; not a Cortex-M hardware or timing model',
              os_stubs=dict(self.stubs),notifications_drained=len(self.notifications),stages=self.stages,
              slot_tree=getattr(self,'slot_tree',None),main_blocks=getattr(self,'main_blocks',[]),cab_blocks=getattr(self,'cab_blocks',[]))

if __name__=='__main__':
 m=FirmwareMachine(patched='--patched' in sys.argv)
 try:
  m.initialize();m.load_main()
 finally:(OUT/('graph-emulation-patched.json' if m.patched else 'graph-emulation.json')).write_text(json.dumps(m.report(),indent=2))
