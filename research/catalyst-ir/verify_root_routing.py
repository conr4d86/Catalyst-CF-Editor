"""Compare all ten DSP output buses using the stock full main graph.

Uses the explicit host processing-list replay from verify_extension_graph.
IO buffers are synthetic. Physical codec/USB port naming is not established
by this test; it records bus indices, not claims about actual connectors.
"""
from verify_extension_graph import *

def run_root(patched,x):
 m=machine(patched);rootnode=m.get(m.parent+0x10);graph=m.get(rootnode+0x14)
 # Synthetic diagnostic fixture: remove default mute/zero volume from the
 # unloaded user-control state. Both images get identical unity gain state.
 for b in m.main_blocks+m.cab_blocks:
  if b['name'] in ('L6SPB_GainStage','L6SPB_LinearGain','L6SPB_LinearGainAdjustableGain'):
   instance=int(b['instance'],16)
   m.put(m.get(instance+4),0x3f800000);m.put(m.get(instance+8),0x3f800000)
 inputs=m.get(m.context+0xc);outputs=m.get(m.context+0x10)
 for i in range(9):m.put(inputs+i*4,0x83003000+i*0x100)
 for i in range(10):m.put(outputs+i*4,0x83004000+i*0x100)
 result=[[] for _ in range(10)]
 for pos in range(0,len(x),32):
  for i in range(9):m.u.mem_write(0x83003000+i*0x100,struct.pack('<32f',*(v*(i+1)/10 for v in x[pos:pos+32])))
  dispatch(m,graph)
  for i in range(10):result[i].extend(struct.unpack('<32f',m.u.mem_read(0x83004000+i*0x100,128)))
 return result

if __name__=='__main__':
 x=[.05*math.sin(i*.139) for i in range(256)]
 a=run_root(False,x);b=run_root(True,x)
 result=dict(scope=__doc__,fixture='default graph with synthetic unity gain state; signal in each of nine input buses',samples=len(x),outputs=[])
 for i in range(10):
  assert all(math.isfinite(v) for v in a[i]+b[i])
  result['outputs'].append(dict(bus=i,bit_identical=a[i]==b[i],max_difference=max(abs(v-w) for v,w in zip(a[i],b[i])),
    stock_peak=max(map(abs,a[i])),custom_peak=max(map(abs,b[i]))))
 result['meaningful_signal']=any(o['stock_peak']>1e-8 for o in result['outputs'])
 assert result['meaningful_signal']
 assert all(result['outputs'][i]['bit_identical'] and result['outputs'][i]['stock_peak']>0 for i in [0,1,2,8,9])
 assert all(not result['outputs'][i]['bit_identical'] and result['outputs'][i]['custom_peak']>0 for i in [3,4,5,6,7])
 result['status']='INCONCLUSIVE' if not result['meaningful_signal'] else 'NONZERO SYNTHETIC OUTPUTS; CONNECTOR MAPPING UNVERIFIED'
 (OUT/'root-routing-tests.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
