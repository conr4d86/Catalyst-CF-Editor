"""Opt-in recovery protocol + actual C# client, simulated files and stream.
No USB, SDK, actor/firmware hook or physical device identity is used.
"""
import base64,ctypes as C,hashlib,json,os,struct,subprocess,uuid
from pathlib import Path
import verify_store as store
import verify_wire as wire
ROOT=Path(__file__).resolve().parent;EDITOR=ROOT.parents[1]/'catalyst-editor'

def main():
    out=ROOT/('checks/recovery-wire-'+uuid.uuid4().hex[:10]);out.mkdir(parents=True)
    env=os.environ.copy();env['ZIG_GLOBAL_CACHE_DIR']=str(out/'global-cache');env['ZIG_LOCAL_CACHE_DIR']=str(out/'local-cache')
    zig=store.TV/'.tools/zig-cross/zig-windows-x86_64-0.13.0/zig.exe'
    native=[ROOT/n for n in ('asset_store.c','asset_wire.c','asset_recover.c','asset_recover_wire.c')]
    host=[ROOT/'RecoveryProbe.cs']+[EDITOR/n for n in ('AssetTransfer.cs','AssetInfo.cs','AssetWire.cs','AssetMidiExchange.cs','AssetJournal.cs')]
    commands=[[zig,'cc','-O2','-Wall','-Wextra','-Werror','-shared','-target','x86_64-windows-gnu',*native,'-Wl,--export-all-symbols','-o',out/'receiver.dll'],
              [zig,'cc','-Os','-Wall','-Wextra','-Werror','-ffreestanding','-fno-builtin','-target','thumb-freestanding-eabihf','-mcpu=cortex_m7+fp_armv8d16sp','-c',ROOT/'asset_recover_wire.c','-o',out/'recovery-wire.arm.o'],
              ['C:/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe','/nologo','/reference:System.Web.Extensions.dll','/out:'+str(out/'RecoveryProbe.exe'),*host]]
    for i,args in enumerate(commands):
        p=subprocess.run(list(map(str,args)),capture_output=True,text=True,env=env,timeout=180);(out/f'compile-{i}.log').write_text(p.stdout+p.stderr);assert p.returncode==0,p.stdout+p.stderr
    lib=C.CDLL(str(out/'receiver.dll'));U=store.U
    sigs={'cf_asset_init':[C.POINTER(store.State),C.POINTER(store.IO),U],
          'cf_asset_recover':[C.POINTER(store.State),U,C.POINTER(store.Meta)],
          'cf_wire_decode':[C.c_void_p,U,C.POINTER(wire.Message)],'cf_wire_encode':[C.POINTER(wire.Message),C.c_void_p,U],
          'cf_recovery_receiver_init':[C.POINTER(wire.Receiver),C.POINTER(store.State),U],
          'cf_recovery_receiver_handle':[C.POINTER(wire.Receiver),C.POINTER(wire.Message),C.POINTER(wire.Message)],
          'cf_receiver_pump':[C.POINTER(wire.Receiver)]}
    for name,args in sigs.items():getattr(lib,name).argtypes=args;getattr(lib,name).restype=C.c_int
    def handle(r,m):
        ack=wire.Message();rc=lib.cf_recovery_receiver_handle(C.byref(r),C.byref(m),C.byref(ack));return rc,ack
    def prepare(slot,phase,previous=False,caps=5):
        d=store.Device(lib);r=wire.Receiver();assert lib.cf_recovery_receiver_init(C.byref(r),C.byref(d.state),caps)==0
        assert handle(r,wire.msg(1,tx=9,session=0))[0]==0
        def populate(target,tx,data,metadata,base=0,commit=True):
            assert handle(r,wire.msg(2,target,tx,base,struct.pack('<II',len(data),store.crc(data))+metadata))[0]==0
            for p in range(0,len(data),256):assert handle(r,wire.msg(3,target,tx,p,data[p:p+256]))[0]==0
            if commit:
                assert handle(r,wire.msg(4,target,tx))[0]==6
                while lib.cf_receiver_pump(C.byref(r))==6:pass
        other=(slot+1)%5;populate(other,55,store.payload(other,2),wire.metadata(other))
        prior=None
        if previous:
            old=store.payload(slot,2);old_info=wire.metadata(slot);populate(slot,60,old,old_info)
            prior=dict(generation=1,transaction=60,length=len(old),crc=store.crc(old),info=base64.b64encode(old_info).decode())
        data=store.payload(slot,1);info=wire.metadata(slot);tx=100+slot;base=1 if previous else 0
        if phase!='idle':
            assert handle(r,wire.msg(2,slot,tx,base,struct.pack('<II',len(data),store.crc(data))+info))[0]==0
            n=len(data) if phase in ('complete','verifying','reading') else 256
            for p in range(0,n,256):assert handle(r,wire.msg(3,slot,tx,p,data[p:p+256]))[0]==0
            if phase in ('verifying','reading'):
                assert handle(r,wire.msg(4,slot,tx))[0]==6
                if phase=='reading':
                    while lib.cf_receiver_pump(C.byref(r))==6:pass
                    d.state.phase=3  # Explicit control-owner read-lease fixture.
        return d,r,data,info,prior,tx,base+1
    cases=[]
    plans=[('partial',s,False) for s in range(5)]+[('partial',s,True) for s in range(5)]+[(m,3,True) for m in ('complete','verifying','reading','lost_ack','not_delivered','mismatch','commit_race','unsupported')]
    for mode,slot,previous in plans:
        phase='complete' if mode=='commit_race' else mode if mode in ('complete','verifying','reading') else 'partial'
        d,r,data,info,prior,tx,generation=prepare(slot,phase,previous,1 if mode=='unsupported' else 5)
        disk=bytes(d.disk);work=bytes(d.work);other=(slot+1)%5;preserved=bytes(d.disk[other*65536:(other+1)*65536]);initial_phase=d.state.phase
        expected_info=bytearray(info)
        if mode=='mismatch':expected_info[-1]^=1
        client=subprocess.Popen([str(out/'RecoveryProbe.exe')],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8')
        def send(row):client.stdin.write(json.dumps(row)+'\n');client.stdin.flush()
        start=dict(product=store.PRODUCT,slot=slot,transaction=tx,length=len(data),crc=store.crc(data),info=base64.b64encode(expected_info).decode())
        if prior is not None:start['previous']=prior
        send(start);ops=[];recovery_io=None
        for _ in range(30):
            line=client.stdout.readline();assert line,client.stderr.read();row=json.loads(line)
            if row.get('done'):result=row;break
            rc,m=wire.decode(lib,bytes(row['packet']));assert rc==1;ops.append(m.opcode)
            if m.opcode==7:
                assert m.bytes==196 and m.transaction==tx and m.session==9
                assert struct.unpack('<III',bytes(m.data[:12]))==(generation,len(data),store.crc(data))
                assert bytes(m.data[12:196])==bytes(expected_info)
                if mode=='not_delivered':send(dict(error='timeout'));continue
                if mode=='commit_race':
                    assert handle(r,wire.msg(4,slot,tx))[0]==6
                    while lib.cf_receiver_pump(C.byref(r))==6:pass
                calls=len(d.calls);rc,ack=handle(r,m);recovery_io=len(d.calls)-calls;assert recovery_io==0
                if mode=='lost_ack':send(dict(error='timeout'));continue
            else:rc,ack=handle(r,m)
            send(dict(packet=list(wire.encode(lib,ack))))
        else:raise AssertionError('Unbounded recovery client traffic')
        client.stdin.close();assert client.wait(timeout=10)==0
        assert not any(op in (2,3,4,6) for op in ops),'Recovery issued an upload or unconditional CANCEL'
        assert bytes(d.disk[other*65536:(other+1)*65536])==preserved
        recovered=mode in ('partial','complete','lost_ack')
        if recovered:
            assert result['resolved'] and not result['stored'] and not result['pending'] and d.state.phase==0
            assert bytes(d.disk)==disk and bytes(d.work)==work
            assert result['generation']==(1 if previous else 0)
        elif mode=='commit_race':assert result['resolved'] and result['stored'] and not result['pending'] and d.state.phase==0
        else:
            assert not result['resolved'] and result['pending'] and d.state.phase==initial_phase
            assert bytes(d.disk)==disk and bytes(d.work)==work
        assert not result['activated'];assert ops.count(7)==(0 if mode=='unsupported' else 1)
        cases.append(dict(mode=mode,slot=slot,previous_preserved=previous,resolved=result['resolved'],stored=result.get('stored'),pending=result['pending'],other_slot_preserved=True,unconditional_cancel_sent=False,recovery_io_calls=recovery_io))
    # Malformed or unauthorized recovery requests never mutate storage/phase.
    d,r,data,info,prior,tx,generation=prepare(3,'partial',True)
    body=struct.pack('<III',generation,len(data),store.crc(data))+info
    mutations=[lambda m:setattr(m,'product',m.product+1),lambda m:setattr(m,'session',8),lambda m:setattr(m,'transaction',0),lambda m:setattr(m,'slot',5),lambda m:setattr(m,'number',1),lambda m:setattr(m,'bytes',195)]
    for mutate in mutations:
        m=wire.msg(7,3,tx,data=body);mutate(m);state=bytes(d.state);disk=bytes(d.disk);calls=len(d.calls);rc,ack=handle(r,m);assert rc!=0 and bytes(d.state)==state and bytes(d.disk)==disk and len(d.calls)==calls
    for flags in (2,4,8,7):assert lib.cf_recovery_receiver_init(C.byref(wire.Receiver()),C.byref(d.state),flags)==store.BAD
    report=dict(passed=True,hardware_accessed=False,SDK_loaded=False,USB_opened=False,actual_C_receiver=True,actual_CSharp_client=True,fragmented_stream=True,compiled_for_cortex_m7=True,native_actor_hook_added=False,physical_recovery_validated=False,cases=cases,malformed_requests_checked=len(mutations),
        sources={str(p.relative_to(store.TV)):hashlib.sha256(p.read_bytes()).hexdigest() for p in native+host+[ROOT/'asset_recover_wire.h',ROOT/'asset_recover.h',ROOT/'asset_store.h',ROOT/'asset_wire.h',ROOT/'verify_recovery_wire.py']})
    (out/'result.json').write_text(json.dumps(report,indent=2));(ROOT/'checks/recovery-wire-latest.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':main()
