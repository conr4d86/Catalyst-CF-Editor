"""Stateless identity C <-> production C# query; synthetic UIDs, no MMIO/USB."""
import base64,ctypes as C,hashlib,json,os,struct,subprocess,uuid
from pathlib import Path
import verify_store as store
import verify_wire as wire
ROOT=Path(__file__).resolve().parent;EDITOR=ROOT.parents[1]/'catalyst-editor';U=C.c_uint32

def main():
    out=ROOT/('checks/identity-'+uuid.uuid4().hex[:10]);out.mkdir(parents=True)
    env=os.environ.copy();env['ZIG_GLOBAL_CACHE_DIR']=str(out/'global-cache');env['ZIG_LOCAL_CACHE_DIR']=str(out/'local-cache')
    zig=store.TV/'.tools/zig-cross/zig-windows-x86_64-0.13.0/zig.exe'
    sources=[ROOT/n for n in ('asset_store.c','asset_wire.c','asset_identity.c')]
    host=[ROOT/'IdentityProbe.cs']+[EDITOR/n for n in ('AssetDeviceIdentity.cs','AssetTransfer.cs','AssetInfo.cs','AssetWire.cs','AssetJournal.cs','DeviceFamily.cs')]
    commands=[[zig,'cc','-O2','-Wall','-Wextra','-Werror','-shared','-target','x86_64-windows-gnu',*sources,'-Wl,--export-all-symbols','-o',out/'identity.dll'],
              [zig,'cc','-Os','-Wall','-Wextra','-Werror','-ffreestanding','-fno-builtin','-target','thumb-freestanding-eabihf','-mcpu=cortex_m7+fp_armv8d16sp','-c',ROOT/'asset_identity.c','-o',out/'identity.arm.o'],
              ['C:/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe','/nologo','/utf8output','/reference:System.Web.Extensions.dll','/out:'+str(out/'IdentityProbe.exe'),*host]]
    for i,args in enumerate(commands):
        done=subprocess.run(list(map(str,args)),capture_output=True,text=True,encoding='utf-8',errors='replace',env=env,timeout=90);(out/f'compile-{i}.log').write_text(done.stdout+done.stderr);assert done.returncode==0,done.stdout+done.stderr
    lib=C.CDLL(str(out/'identity.dll'))
    for name,args in {'cf_asset_init':[C.POINTER(store.State),C.POINTER(store.IO),U],'cf_receiver_init':[C.POINTER(wire.Receiver),C.POINTER(store.State),U],'cf_identity_reply':[C.POINTER(wire.Receiver),C.POINTER(U),C.POINTER(wire.Message),C.POINTER(wire.Message)],'cf_wire_encode':[C.POINTER(wire.Message),C.c_void_p,U],'cf_wire_decode':[C.c_void_p,U,C.POINTER(wire.Message)]}.items():
        getattr(lib,name).argtypes=args;getattr(lib,name).restype=C.c_int
    cases=[]
    uid=(U*2)(0x10203040,0xa0b0c0d0)
    for product in (2424833,2424834,2424835):
        d=store.Device(lib);d.state.product=product;r=wire.Receiver();assert lib.cf_receiver_init(C.byref(r),C.byref(d.state),1)==0
        r.session=44;r.last_transaction=123;r.last_status=6
        for phase in (0,1,2,3):
            d.state.phase=phase;before=bytes(d.state),bytes(r),bytes(d.disk),list(d.calls)
            request=wire.msg(8,session=0,tx=77);request.product=product;reply=wire.Message()
            assert lib.cf_identity_reply(C.byref(r),uid,C.byref(request),C.byref(reply))==0
            assert bytes(reply.data[:20])==struct.pack('<5I',0,1,product,*uid) and reply.session==0
            assert before==(bytes(d.state),bytes(r),bytes(d.disk),d.calls)
            cases.append(dict(product=product,phase=phase,no_IO_or_session_mutation=True))
    d=store.Device(lib);r=wire.Receiver();assert lib.cf_receiver_init(C.byref(r),C.byref(d.state),1)==0
    for mode in ('opcode','product','session','transaction','number','slot','bytes','zero_UID','erased_UID'):
        request=wire.msg(8,session=0,tx=77);chosen=uid
        if mode in ('zero_UID','erased_UID'):chosen=(U*2)(*([0,0] if mode=='zero_UID' else [0xffffffff]*2))
        elif mode=='bytes':request.bytes=1
        elif mode=='transaction':request.transaction=0
        else:setattr(request,mode,getattr(request,mode)+1)
        reply=wire.Message();assert lib.cf_identity_reply(C.byref(r),chosen,C.byref(request),C.byref(reply))==2
        assert bytes(reply.data[4:20])==bytes(16) and not d.calls
        cases.append(dict(rejected=mode,no_UID_disclosure=True))
    for mode in ('valid','wrong_UID','wrong_nonce','wrong_product','unknown_schema','truncated','nack','timeout','edition','edition_required','edition_missing','edition_zero','edition_erased','edition_schema'):
        proc=subprocess.Popen([str(out/'IdentityProbe.exe')]+(['--edition'] if mode in ('edition_required','edition_missing') else []),stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8')
        proc.stdin.write('2424833\n');proc.stdin.flush();request=json.loads(proc.stdout.readline());rc,message=wire.decode(lib,base64.b64decode(request['packet']));assert rc==1 and message.opcode==8 and message.session==0
        response=wire.Message();assert lib.cf_identity_reply(C.byref(r),uid,C.byref(message),C.byref(response))==0
        edition=hashlib.sha256(b'Synthetic edition').digest()
        if mode.startswith('edition') and mode!='edition_missing':response.bytes=52;response.data[4]=2;response.data[20:52]=edition
        if mode=='edition_zero':response.data[20:52]=bytes(32)
        elif mode=='edition_erased':response.data[20:52]=bytes([255]*32)
        elif mode=='edition_schema':response.data[4]=1
        elif mode=='wrong_UID':response.data[12:20]=bytes(8)
        elif mode=='wrong_nonce':response.transaction^=1
        elif mode=='wrong_product':response.product+=1
        elif mode=='unknown_schema':response.data[4]=2
        elif mode=='truncated':response.bytes=16
        elif mode=='nack':response.data[0]=2
        row={'error':True} if mode=='timeout' else {'packet':base64.b64encode(wire.encode(lib,response)).decode()}
        proc.stdin.write(json.dumps(row)+'\n');proc.stdin.flush();result=json.loads(proc.stdout.readline());proc.wait(timeout=8);assert proc.returncode==0
        assert result['accepted']==(mode in ('valid','edition','edition_required')),(mode,result)
        if mode=='valid':assert result['identity']=='CFCX-UID1:10203040A0B0C0D0'
        if mode in ('edition','edition_required'):assert result['identity']=='CFCX-UID2:10203040A0B0C0D0:'+edition.hex().upper()
        cases.append(dict(host=mode,accepted=result['accepted']))
    report=dict(passed=True,hardware_accessed=False,USB_opened=False,SDK_loaded=False,MMIO_accessed=False,firmware_created=False,UIDs_are_synthetic=True,query_has_no_IO_or_session_mutation=True,build_folder=str(out),cases=cases,
                limits=['Native silicon UID provider and bootstrap are not connected','No physical MCU/UID or MIDI driver was queried'],sources={str(p.relative_to(store.TV)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources+host+[ROOT/'asset_identity.h',Path(__file__)]})
    (ROOT/'checks/identity-latest.json').write_text(json.dumps(report,indent=2));print(json.dumps({k:v for k,v in report.items() if k!='sources'},indent=2))
if __name__=='__main__':main()
