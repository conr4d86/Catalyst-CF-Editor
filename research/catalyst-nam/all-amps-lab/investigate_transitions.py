"""Read-only native graph reload investigation; no CTF, SDK or USB actions."""
import contextlib,ctypes,hashlib,importlib.util,json,math,struct,sys,traceback
from pathlib import Path
ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent
spec=importlib.util.spec_from_file_location('all_amp_transition_fixture',ROOT/'verify_all_amps.py')
fixture=importlib.util.module_from_spec(spec);spec.loader.exec_module(fixture)
native=fixture.native;lab=native.lab
from verify_fallback import amp_graph

def main():
 out=ROOT/'transition-check';out.mkdir(exist_ok=True)
 original=(NAM/'app/resources/nam-engine.ctf').read_bytes()
 image=native.decode_hex(native.image_dict(original)[0])[0][1][1]
 segments,symbols=native.elf(ROOT/'build/combined.elf');old=native.elf(NAM/'build-trial/trial.elf')[1]
 rows=json.loads((ROOT/'build/OEM-controls.json').read_text());mapping={x['symbol']:x for x in rows}
 _,w=lab.model.load(NAM/'external/NeuralAmpModelerCore/example_models/A2.nam')
 seed=lab.Machine([w,w]);r=seed.get();r.bank_source[:]=[0x83008000,0x8300a000]
 events=[]
 with (out/'native.log').open('w') as log,contextlib.redirect_stdout(log):
  boot=native.bootstrap(image)
  for segment in segments:
   if segment['flags']&1 or segment['address']==0x20064b00:boot.mem_write(segment['address'],segment['data'])
  thunk=bytearray(boot.mem_read(0x17e80,0x180))
  for name in ('trial_begin','trial_route','trial_after','trial_node_control'):
   before=(old[name]['address']|1).to_bytes(4,'little');after=(symbols[name]['address']|1).to_bytes(4,'little');assert thunk.count(before)==1;thunk=thunk.replace(before,after)
  boot.mem_write(0x17e80,bytes(thunk));boot.mem_write(lab.RUNTIME,bytes(r))
  m=native.ready(image,boot,symbol=159);clock=native.Clock(m)
  for i in range(2):m.u.mem_write(r.bank_source[i],lab.model.pack(w)+seed.silences[i])
  def runtime():return lab.Combined.from_buffer_copy(bytes(m.u.mem_read(lab.RUNTIME,512)))
  def prepare(symbol,reload):
   model=mapping[symbol]
   if reload:
    m.block_events.clear();assert m.call(0x8004a914,m.control,0x20024208,symbol,count=100000000)==0
    m.rootnode=m.get(m.parent+16)
    if m.block_events:m.amp_graph=amp_graph(m,m.rootnode)
    m.root=m.get(m.rootnode+20);m.arena=m.rootnode-0x58
   for block in {e['node']:e for e in m.block_events}.values():
    if block['name'] in ('L6SPB_GainStage','L6SPB_LinearGain','L6SPB_LinearGainAdjustableGain'):
     instance=int(block['instance'],16);m.put(m.get(instance+4),0x3f800000);m.put(m.get(instance+8),0x3f800000)
   p=model['params'];source=destination=0x20001470
   if symbol in (82,159):native.bind(m,source,destination)
   else:m.put(m.get(m.amp_graph+8)+12,source)
   for name in ('Drive','Bass','Mid','Treble','Presence','Master'):
    if name in p:native.setparam(m,p[name],.5)
   native.setparam(m,p['Boost'],.3);native.setparam(m,p['ChVol'],.4);native.setparam(m,p['BoostSwitch'],1,5)
   config=lab.Config(3,1,0,0,.3,.5,1);m.u.mem_write(0x83000600,bytes(config));assert m.call(symbols['cf_preamp_request']['address'],0x83000600)==0
   for _ in range(90):native.block(m,source,destination,[0.]*32)
   values=[.04*math.sin(i*.127) for i in range(256)];audio=[]
   for off in range(0,len(values),32):audio.extend(native.block(m,source,destination,values[off:off+32]))
   state=runtime();event=dict(symbol=symbol,model=model['name'],reloaded=reload,graph=hex(m.amp_graph),peak=max(abs(x) for x in audio),mode=state.mode,fault=state.fault,want=state.want,depth=state.depth,slot=state.active_slot,node=hex(m.get(symbols['stock']['address'])),boost=state.boost_enabled,level=state.level,seen=state.seen)
   events.append(event);print(json.dumps(event),file=sys.__stdout__,flush=True)
   assert all(math.isfinite(x) for x in audio) and event['peak']>1e-8,event
   assert state.depth==0 and state.fault==0,event
  # Same active NAM, the other NAM, a stock model, return, and stock-to-stock.
  for i,symbol in enumerate((159,159,82,159,24,159,72,80,159,82)):
   prepare(symbol,i>0)
 result=dict(passed=True,hardware_accessed=False,SDK_loaded=False,firmware_created=False,elf_sha256=hashlib.sha256((ROOT/'build/combined.elf').read_bytes()).hexdigest(),events=events,limitations=['Actual preset file reader/IR owner not included; native model graph unload/load and controls only','DWT is scripted; no physical deadline evidence'])
 (out/'result.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))

if __name__=='__main__':
 try:main()
 except BaseException:
  out=ROOT/'transition-check';out.mkdir(exist_ok=True);(out/'error.txt').write_text(traceback.format_exc());raise
