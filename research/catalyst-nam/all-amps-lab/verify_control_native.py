"""Native DSP control-owner regression. Offline, no device or USB.

Real OEM load/set/dispatch routines run. Native RTOS and MRS IPSR are explicit
functional mocks; no scheduler timing, silicon stack or sound claim is made.
The OLD r103 binary must fail if it reaches the original graph setter in audio.
"""
import contextlib,ctypes,hashlib,importlib.util,json,math,struct,sys
from pathlib import Path
BASE=Path(__file__).resolve().parent;LAB=BASE/'control-owner-lab';NAM=BASE.parent
spec=importlib.util.spec_from_file_location('fixture',BASE/'verify_all_amps.py');fixture=importlib.util.module_from_spec(spec);spec.loader.exec_module(fixture)
native=fixture.native;lab=fixture.lab
import capstone
from unicorn import UC_HOOK_CODE,UC_HOOK_BLOCK
from unicorn.arm_const import *
AS=native.elf(LAB/'actor-build/actor.elf')[1];AU=native.elf(LAB/'build/combined.elf')[1]
CODE=native.elf(LAB/'actor-build/actor.elf')[0]
MODELS=json.loads((LAB/'build/OEM-controls.json').read_text())

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
class Rig:
 def __init__(self,m):
  self.m=m;self.phase='native';self.ipsr=0;self.busy=False;self.release_fail=False;self.locked=False;self.native_calls=[];self.sem_calls=[];self.delay_calls=[];self.receives=[];self.loader_calls=[];self.queued=[];self.sentinel=0x12345678;self.low_sp=0x80806b70
  m.put(0x80807458,0x20024208);m.put(0x80806bd4,0x83000c00)
  native.Clock(m)
  def original(u,a,size,user):
   if self.phase=='audio':raise AssertionError('Native graph mutation reached from AUDIO at '+hex(a))
   if self.phase=='DSP' and not self.locked:raise AssertionError('DSP native mutation without outer mutex')
   self.native_calls.append(dict(phase=self.phase,node=u.reg_read(UC_ARM_REG_R1),param=u.reg_read(UC_ARM_REG_R2),word=m.get(u.reg_read(UC_ARM_REG_R3))))
  m.u.hook_add(UC_HOOK_CODE,original,begin=0x17f80,end=0x17f80)
  def floor(u,a,z,user):
   if self.phase=='DSP':
    sp=u.reg_read(UC_ARM_REG_SP);self.low_sp=min(self.low_sp,sp);assert sp>=0x80806370+64,'DSP native call crossed stack guard'
  m.u.hook_add(UC_HOOK_BLOCK,floor)

  def loader(u,a,z,user):
   obj=u.reg_read(UC_ARM_REG_R0);current=m.get(0x20026378+0x6c)+8
   if obj==current:
    proof=m.get(AU['stock']['address']+24);self.loader_calls.append(dict(object=obj,proof=proof,args=[u.reg_read(x) for x in (UC_ARM_REG_R0,UC_ARM_REG_R1,UC_ARM_REG_R2,UC_ARM_REG_R3)]));assert proof==2,'loader entered original with stale OEM bypass proof'
  m.u.hook_add(UC_HOOK_CODE,loader,begin=AS['cf_stock_load_original']['address']&~1,end=AS['cf_stock_load_original']['address']&~1)

  def lock(u,a,z,user):
   obj=u.reg_read(UC_ARM_REG_R0);timeout=u.reg_read(UC_ARM_REG_R1);self.sem_calls.append(('take',obj,timeout,self.phase))
   assert self.phase!='audio' and timeout==0
   if self.busy:u.reg_write(UC_ARM_REG_R0,0)
   else:assert not self.locked;self.locked=True;u.reg_write(UC_ARM_REG_R0,1)
   u.reg_write(UC_ARM_REG_PC,u.reg_read(UC_ARM_REG_LR))
  def give(u,a,z,user):
   args=[u.reg_read(x) for x in (UC_ARM_REG_R0,UC_ARM_REG_R1,UC_ARM_REG_R2,UC_ARM_REG_R3)];self.sem_calls.append(('give',*args,self.phase));assert args[1:]==[0,0,0] and self.locked
   if not self.release_fail:self.locked=False
   u.reg_write(UC_ARM_REG_R0,0 if self.release_fail else 1);u.reg_write(UC_ARM_REG_PC,u.reg_read(UC_ARM_REG_LR))
  m.u.hook_add(UC_HOOK_CODE,lock,begin=0x6c28,end=0x6c28);m.u.hook_add(UC_HOOK_CODE,give,begin=0x646c,end=0x646c)
  md=capstone.Cs(capstone.CS_ARCH_ARM,capstone.CS_MODE_THUMB|capstone.CS_MODE_MCLASS)
  for row in CODE:
   if row['flags']&1:
    for k in range(0,len(row['data'])-4,2):
     ins=next(md.disasm(row['data'][k:k+4],row['address']+k),None)
     if ins and ins.mnemonic=='mrs' and 'ipsr' in ins.op_str:
      reg=globals()['UC_ARM_REG_'+ins.op_str.split(',')[0].upper()]
      def mrs(u,a,z,user,reg=reg):u.reg_write(reg,self.ipsr);u.reg_write(UC_ARM_REG_PC,a+5)
      m.u.hook_add(UC_HOOK_CODE,mrs,begin=ins.address,end=ins.address)
  def receive(u,a,z,user):
   q,msg,to=[u.reg_read(x) for x in (UC_ARM_REG_R0,UC_ARM_REG_R1,UC_ARM_REG_R2)];self.receives.append((q,msg,to));rc=self.queued.pop(0) if self.queued else 1
   if rc==1:m.put(msg,0xabcdef01,2,3,4)
   else:assert m.get(msg)==self.sentinel,'wrapper dispatched/changed stale native message'
   u.reg_write(UC_ARM_REG_R0,rc);u.reg_write(UC_ARM_REG_PC,u.reg_read(UC_ARM_REG_LR))
  m.u.hook_add(UC_HOOK_CODE,receive,begin=0x8004c8f8,end=0x8004c8f8)
  def delay(u,a,z,user):self.delay_calls.append(u.reg_read(UC_ARM_REG_R0));u.reg_write(UC_ARM_REG_PC,u.reg_read(UC_ARM_REG_LR))
  m.u.hook_add(UC_HOOK_CODE,delay,begin=0x589c,end=0x589c)
 def call(self,fn,*args,sp=0x80806b58,phase='DSP'):
  self.phase=phase;m=self.m
  for r,v in zip((UC_ARM_REG_R0,UC_ARM_REG_R1,UC_ARM_REG_R2,UC_ARM_REG_R3),args):m.u.reg_write(r,v)
  if len(args)>4:m.put(sp,*args[4:])
  m.u.reg_write(UC_ARM_REG_SP,sp);m.u.reg_write(UC_ARM_REG_LR,0x8301f001)
  m.u.emu_start(fn|1,0x8301f000,count=10000000)
  assert m.u.reg_read(UC_ARM_REG_PC)==0x8301f000,hex(m.u.reg_read(UC_ARM_REG_PC));assert m.queue_yield is None
  self.phase='native';return m.u.reg_read(UC_ARM_REG_R0)
 def service(self,**kw):return self.call(AS['cf_stock_main_service']['address'],**kw)
 def request(self,mode,enabled=1,level=1):
  self.m.u.mem_write(0x83000600,bytes(lab.Config(mode,enabled,0,0,.3,.5,level)))
  assert self.m.call(AU['cf_preamp_request']['address'],0x83000600)==0
 def block(self,values):
  self.phase='audio'
  try:return native.block(self.m,0x200012f0,0x200012f0,values)
  finally:self.phase='native'

def ready(image,boot,model):
 m=native.ready(image,boot,symbol=model['symbol']);rig=Rig(m)
 r=lab.Combined();r.magic=0x324d414e;r.min_sp=r.min_period=0xffffffff;r.drive_scale=r.boost_scale=1;r.input_gain=r.input_current=.25;r.level=r.level_current=1;r.stock_fade=1
 for i in range(4):r.eq_index[i]=10
 m.u.mem_write(lab.RUNTIME,bytes(r))
 for block in {e['node']:e for e in m.block_events}.values():
  if block['name'] in ('L6SPB_GainStage','L6SPB_LinearGain','L6SPB_LinearGainAdjustableGain'):
   instance=int(block['instance'],16);m.put(m.get(instance+4),0x3f800000);m.put(m.get(instance+8),0x3f800000)
 m.put(m.get(m.amp_graph+8)+12,0x200012f0)
 p=model['params']
 for name in ('Drive','Bass','Mid','Treble','Presence','Master'):
  if name in p:native.setparam(m,p[name],.5)
 native.setparam(m,p['Boost'],.3);native.setparam(m,p['ChVol'],.4);native.setparam(m,p['BoostSwitch'],1,5)
 return m,rig

def main():
 out=LAB/'build';results=[];image=(LAB/'boot-build/NON-INSTALLABLE-segment.bin').read_bytes()
 with (out/'control-native.log').open('w') as log,contextlib.redirect_stdout(log):
  boot=native.bootstrap(image)
  # Failing regression uses preserved exact unsafe r103 ELF, not a source stub.
  old=(NAM/'app/resources/nam-engine.ctf').read_bytes();old_image=native.decode_hex(native.image_dict(old)[0])[0][1][1]
  ob=native.bootstrap(old_image);oldseg,olds=native.elf(BASE/'build/combined.elf');oldsy=native.elf(NAM/'build-trial/trial.elf')[1]
  for seg in oldseg:
   if seg['flags']&1 or seg['address']==0x20064b00:ob.mem_write(seg['address'],seg['data'])
  thunk=bytearray(ob.mem_read(0x17e80,0x180))
  for name in ('trial_begin','trial_route','trial_after','trial_node_control'):thunk=thunk.replace((oldsy[name]['address']|1).to_bytes(4,'little'),(olds[name]['address']|1).to_bytes(4,'little'))
  ob.mem_write(0x17e80,bytes(thunk));model=MODELS[0];om=native.ready(old_image,ob,symbol=model['symbol']);native.Clock(om)
  r=lab.Combined();r.magic=0x324d414e;r.min_sp=r.min_period=0xffffffff;r.drive_scale=r.boost_scale=r.level=r.level_current=r.stock_fade=1;om.u.mem_write(lab.RUNTIME,bytes(r));om.put(om.get(om.amp_graph+8)+12,0x200012f0)
  native.setparam(om,model['params']['BoostSwitch'],1,5)
  cfg=lab.Config(2,1,0,0,.3,.5,1);om.u.mem_write(0x83000600,bytes(cfg));assert om.call(olds['cf_preamp_request']['address'],0x83000600)==0
  hit=[]
  def reject(u,a,z,user):hit.append(hex(a));raise AssertionError('OLD native graph setter called from AUDIO')
  om.u.hook_add(UC_HOOK_CODE,reject,begin=0x17f80,end=0x17f80)
  try:native.block(om,0x200012f0,0x200012f0,[0.]*32)
  except (AssertionError,RuntimeError) as e:assert 'OLD native graph setter called from AUDIO' in str(e),str(e)
  assert hit,'preserved unsafe binary did not trigger context regression'
  oldproof=dict(elf_sha256=sha(BASE/'build/combined.elf'),rejected_from_audio=True,addresses=hit)
  for model in MODELS:
   m,rig=ready(image,boot,model);p=model['params'];stock=AU['stock']['address'];signal=[.04*math.sin(i*.127)+.01*math.sin(i*.031) for i in range(256)];outputs=[]
   for mode in (0,1,2,3,0):
    rig.request(mode);rig.service();assert m.get(stock+24)==int(mode==0),(model['name'],mode,m.get(stock+24))
    for _ in range(12):rig.block([0.]*32)
    audio=[]
    for pos in range(0,len(signal),32):audio+=rig.block(signal[pos:pos+32])
    state=lab.Combined.from_buffer_copy(bytes(m.u.mem_read(lab.RUNTIME,512)))
    assert state.pedal.fault==0 and state.fault==0 and state.nam_calls==0 and state.depth==0
    assert state.boost_enabled==1,'masked OEM switch overwrote requested UI Boost'
    assert all(math.isfinite(v) for v in audio) and max(abs(v) for v in audio)>1e-8
    outputs.append(audio)
   assert max(abs(a-b) for a,b in zip(outputs[2],outputs[3]))>1e-7
   # Actual native OFF/ON controls preserve original requested value.
   for enabled in (0,1):
    rig.request(0);native.setparam(m,p['BoostSwitch'],enabled,5);rig.service();assert m.get(stock+24)==enabled
    rig.request(3);rig.service();native.setparam(m,p['BoostSwitch'],enabled,5);rig.service();assert m.get(stock+24)==0
    assert m.get(lab.RUNTIME+lab.Combined.boost_enabled.offset)==enabled
   # Mutex contention must not reach graph setter or falsely confirm bypass.
   rig.request(0);native.setparam(m,p['BoostSwitch'],1,5);rig.request(2);rig.busy=True;count=len(rig.native_calls);rig.service();assert len(rig.native_calls)==count;assert m.get(stock+24)==1
   rig.busy=False;rig.service();assert m.get(stock+24)==0
   # Audio and FS/foreign stacks, plus IRQ, cannot execute service mutation.
   for phase,sp,irq in [('audio',0x8301d000,0),('FS',0x80806200,0),('IRQ',0x80806b60,16)]:
    count=len(rig.native_calls);rig.ipsr=irq;rig.service(sp=sp,phase=phase);assert len(rig.native_calls)==count
   rig.ipsr=0
   results.append(dict(model=model['name'],symbol=model['symbol'],modes=[0,1,2,3,0],native_off_on=True,custom_bypasses_OEM_before_audio=True,restores_factory_Boost=True,outer_mutex_busy_skips=True,audio_FS_IRQ_rejected=True,finite_nonzero_native_audio=True,DSP_stack_min=hex(rig.low_sp),DSP_stack_reserve=rig.low_sp-0x80806370))
   print('CONTROL NATIVE PASS',model['name'],flush=True,file=sys.__stdout__)
  # Queue wrapper never returns timeout or unexpected rc with stale message.
  m,rig=ready(image,boot,MODELS[0]);m.put(0x83000d00,rig.sentinel,0,0,0);rig.queued=[0,0,2,1]
  assert rig.call(AS['cf_stock_main_wait']['address'],0x83000c00,0x83000d00,0xffffffff)==1
  assert [r[2] for r in rig.receives]==[5,5,5,5] and rig.delay_calls==[1] and m.get(0x83000d00)==0xabcdef01
  # Foreign and finite native calls keep their original timeout and return.
  for timeout,sp in [(17,0x80806b60),(0xffffffff,0x8301d000)]:
   m.put(0x83000d00,rig.sentinel,0,0,0);rig.queued=[0];assert rig.call(AS['cf_stock_main_wait']['address'],0x83000c00,0x83000d00,timeout,sp=sp)==0;assert rig.receives[-1][2]==timeout
  # Same-address SAME-model reload and unrelated FX load invalidate old proof.
  rig.request(2);rig.service();before_node=m.get(AU['stock']['address']);before_graph=m.amp_graph
  assert m.call(0x8004a914,m.control,0x20024208,MODELS[0]['symbol'],count=100000000)==0
  from verify_fallback import amp_graph
  m.rootnode=m.get(m.parent+16);m.amp_graph=amp_graph(m,m.rootnode);m.arena=m.rootnode-0x58
  assert rig.loader_calls and all(x['proof']==2 for x in rig.loader_calls),'loader did not invalidate prior bypass proof before rebuilding'
  rig.service();assert m.get(AU['stock']['address']+24)==0
  reloaded=dict(before_node=hex(before_node),after_node=hex(m.get(AU['stock']['address'])),before_graph=hex(before_graph),after_graph=hex(m.amp_graph),same_addresses=before_graph==m.amp_graph)
  # Unrelated cabinet graph reload invalidates proof but does not strand the
  # active pedal waiting for an Amp BoostSwitch callback that never occurs.
  before=len(rig.loader_calls)
  assert m.call(0x8004a914,m.control,int(m.cab_slot,16),257,count=100000000)==0
  assert len(rig.loader_calls)>before and m.get(AU['stock']['address']+24)==2
  rig.service();assert m.get(AU['stock']['address']+24)==0
  fx_rebound=True
  # Inactive implementation-node controls must not replace active UI intent.
  original_node=m.get(AU['stock']['address']);fake=0x2004fe00
  m.u.mem_write(fake,bytes(m.u.mem_read(original_node,28)));m.put(0x83000e80,0)
  native.setparam(m,MODELS[0]['params']['BoostSwitch'],1,5);rig.service()
  before_boost=m.get(lab.RUNTIME+lab.Combined.boost_enabled.offset)
  assert before_boost==1
  m.call(AS['cf_stock_observe']['address'],m.arena,fake,MODELS[0]['params']['BoostSwitch'],0x83000e80,0,0)
  assert m.get(lab.RUNTIME+lab.Combined.boost_enabled.offset)==before_boost
  inactive_rejected=True
  # A complete newer generation arriving while the arena setter runs is not
  # falsely reported complete, and is reconciled at the next owner service.
  rig.request(0);rig.service();rig.request(2);published=[]
  def publish_during_setter(u,a,z,user):
   if rig.phase=='DSP' and not published:
    seq=m.get(lab.RUNTIME+lab.Combined.sequence.offset);m.put(lab.RUNTIME+lab.Combined.sequence.offset,seq+3)
    m.u.mem_write(lab.RUNTIME+lab.Combined.pending.offset,bytes(lab.Config(0,1,0,0,.3,.5,1)))
    m.put(lab.RUNTIME+lab.Combined.sequence.offset,seq+2);published.append(seq+2)
  hook=m.u.hook_add(UC_HOOK_CODE,publish_during_setter,begin=0x17f80,end=0x17f80)
  rig.service();m.u.hook_del(hook);assert published
  assert m.get(AS['C']['address']+9*4)!=published[0]
  rig.service();assert m.get(AU['stock']['address']+24)==1 and m.get(AS['C']['address']+9*4)==published[0]
  generation_changed_safely=True
  rig.request(2);rig.service()
  # Bad loader status is forwarded and does not strand loading generation.
  context=m.get(0x20026378+0x6c)+8;m.put(0x83000e00,2,0,0,0)
  rc=m.call(AS['cf_stock_model_load']['address'],context,0x83000e00,0,4)
  assert rc>=0x80000000;rig.service();assert m.get(AU['stock']['address']+24)==0
  # Opcode14 is a bounded read-only 84-byte snapshot, never an audio ACK or
  # an arbitrary memory read interface. Foreign products/fields are rejected.
  store=0x83001000;receiver=0x83001400;incoming=0x83001500;outgoing=0x83001700
  m.u.mem_write(store,bytes(600));m.put(store+20,2424833);m.put(receiver,store,0,61,0,0)
  message=struct.pack('<4IBBH',2424833,0,7,0,14,0,0)+bytes(256);m.u.mem_write(incoming,message)
  before_native=len(rig.native_calls);before_store=bytes(m.u.mem_read(store,600))
  assert m.call(AS['cf_stock_control_diagnostic']['address'],receiver,incoming,outgoing)==0
  payload=bytes(m.u.mem_read(outgoing,276));assert payload[16]==78 and struct.unpack_from('<H',payload,18)[0]==84
  words=struct.unpack_from('<21I',payload,20);assert words[:2]==(0,1) and words[2]>=1 and words[3]==3 and words[4]==1 and words[20]==1
  assert before_store==bytes(m.u.mem_read(store,600)) and len(rig.native_calls)==before_native
  m.put(incoming,2424834);assert m.call(AS['cf_stock_control_diagnostic']['address'],receiver,incoming,outgoing)==2
  assert struct.unpack('<H',bytes(m.u.mem_read(outgoing+18,2)))[0]==4
  diagnostic_read_only=True
  # Giving with unexpected error is fail-closed and never retried blindly.
  rig.release_fail=True;rig.service();count=len(rig.sem_calls);rig.service();assert len(rig.sem_calls)==count
  assert m.get(AU['stock']['address']+24)==2
 report=dict(passed=True,hardware_accessed=False,USB_opened=False,physical_audio_validated=False,real_time_validated=False,old_unsafe_binary=oldproof,models=results,queue_zero_unexpected_only_returns_real_message=True,queue_finite_foreign_preserved=True,unexpected_receive_yields_one_tick=True,ABA_native_reload=reloaded,negative_loader_preserves_rc=True,unrelated_FX_load_rebinds=fx_rebound,inactive_graph_controls_rejected=inactive_rejected,new_generation_during_setter_reconciled=generation_changed_safely,opcode14_fixed_read_only=diagnostic_read_only,release_failure_fail_closed=True,elf_sha256=sha(LAB/'build/combined.elf'),actor_sha256=sha(LAB/'actor-build/actor.elf'),segment_sha256=sha(LAB/'boot-build/NON-INSTALLABLE-segment.bin'),verification_source_sha256=sha(Path(__file__)),limits=['RTOS mutex/scheduler and IPSR are explicit mocks','No physical timing, USB or audible validation','Native load/controls/audio dispatch execute as compiled Thumb code'])
 (out/'native-tests.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':
 try:main()
 except BaseException:
  import traceback
  (LAB/'build/control-native-error.txt').write_text(traceback.format_exc());raise
