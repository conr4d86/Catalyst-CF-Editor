"""Reuse the declared native routing fixture with the new reset-loaded image."""
from verify_tcm_ir import build, bootstrap, machine, RAM, OUT
import verify_partitioned_routing as routing
import json

if __name__=='__main__':
 image,plan,_=build();boot=bootstrap(image)
 routing.machine=lambda:machine(image,boot)
 routing.RAM=RAM
 tests=[]
 # No custom FFT is selected at boot, even if the stored cabinet was 1x12.
 routing.machine=lambda:machine(image,boot,arm=False)
 stock=routing.run(False,238);unarmed=routing.run(True,238)
 assert all(a==b for a,b in zip(stock,unarmed))
 assert all(max(map(abs,stock[i]))>0 for i in (0,1,2,3,4,5,6,7,8,9))
 tests.append(dict(name='unarmed boot uses nonzero stock 1x12',bit_identical_buses=list(range(10))))
 routing.machine=lambda:machine(image,boot,arm=True)
 for cab in (238,257,259):
  stock=routing.run(False,cab);modified=routing.run(True,cab);equal=[a==b for a,b in zip(stock,modified)]
  if cab==238:
   assert all(equal[i] for i in (0,1,2,8,9))
   assert all(not equal[i] for i in (3,4,5,6,7))
  else:
   assert all(max(map(abs,stock[i]))>0 for i in (3,4,5,6,7))
   assert all(equal)
  tests.append(dict(cabinet_symbol=cab,bit_identical_buses=[i for i,v in enumerate(equal) if v],
                    guard_intact=True))
 report=dict(scope=__doc__,segment_sha256=plan['patched_segment_sha256'],tests=tests,
             status='PASS OFFLINE; physical routing/timing not measured')
 (OUT/'tcm-routing-tests.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
 print(json.dumps(report,indent=2))
