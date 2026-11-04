"""C# host -> serialized mock link -> actual C receiver, no USB/SDK calls."""
import base64,ctypes as C,hashlib,json,os,struct,subprocess,uuid
from pathlib import Path
import verify_wire as wire
import verify_store as store
ROOT=Path(__file__).resolve().parent;EDITOR=ROOT.parents[1]/'catalyst-editor'

def main():
    latest=json.loads((ROOT/'checks/wire-latest.json').read_text())
    assert latest['passed']
    for file,digest in latest['sources'].items():assert hashlib.sha256((wire.TV/file).read_bytes()).hexdigest()==digest
    lib=C.CDLL(str(Path(latest['build_folder'])/'receiver.dll'))
    signatures={'cf_asset_init':[C.POINTER(store.State),C.POINTER(store.IO),store.U],
        'cf_asset_recover':[C.POINTER(store.State),store.U,C.POINTER(store.Meta)],
        'cf_wire_decode':[C.c_void_p,store.U,C.POINTER(wire.Message)],'cf_wire_encode':[C.POINTER(wire.Message),C.c_void_p,store.U],
        'cf_receiver_init':[C.POINTER(wire.Receiver),C.POINTER(store.State),store.U],
        'cf_receiver_handle':[C.POINTER(wire.Receiver),C.POINTER(wire.Message),C.POINTER(wire.Message)],'cf_receiver_pump':[C.POINTER(wire.Receiver)]}
    for name,args in signatures.items():getattr(lib,name).argtypes=args;getattr(lib,name).restype=C.c_int
    out=ROOT/('checks/transfer-'+uuid.uuid4().hex[:10]);out.mkdir(parents=True)
    sources=[ROOT/'TransferProbe.cs']+[EDITOR/n for n in ('AssetTransfer.cs','AssetInfo.cs','AssetWire.cs','AssetMidiExchange.cs','AssetJournal.cs')]
    p=subprocess.run(['C:/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe','/nologo','/reference:System.Web.Extensions.dll','/out:'+str(out/'TransferProbe.exe'),*map(str,sources)],capture_output=True,text=True)
    (out/'compile.log').write_text(p.stdout+p.stderr);assert p.returncode==0,p.stdout+p.stderr
    cases=[]
    plans=[('normal',slot) for slot in range(5)]+[('stream',slot) for slot in range(5)]+[(mode,3) for mode in ('lost_begin','lost_chunk','lost_commit','all_receipts_lost','cancel','unsupported','wrong_session','wrong_final_crc','duplicate','busy_status','reconcile_commit','reconcile_busy','busy_hello','journal_success','reconcile_restart')]
    for mode,slot in plans:
        d=store.Device(lib);r=wire.Receiver();assert lib.cf_receiver_init(C.byref(r),C.byref(d.state),0 if mode=='unsupported' else 1)==0
        data=store.payload(slot);info=wire.metadata(slot);tx=100+slot;ops=[];injected=False;committed=False;busy_count=0;resolving=False;blocked=False
        # Other slots contain a distinct previous generation and must not change.
        # Populate through the same receiver below, without a separate fake store.
        def handle(m):
            ack=wire.Message();rc=lib.cf_receiver_handle(C.byref(r),C.byref(m),C.byref(ack));return rc,ack
        assert handle(wire.msg(1,tx=9,session=0))[0]==0
        other=(slot+1)%5;b=store.payload(other,1)
        if mode!='unsupported':
            assert handle(wire.msg(2,other,500,data=struct.pack('<II',len(b),store.crc(b))+wire.metadata(other)))[0]==0
            for pos in range(0,len(b),256):assert handle(wire.msg(3,other,500,pos,b[pos:pos+256]))[0]==0
            assert handle(wire.msg(4,other,500))[0]==6
            while lib.cf_receiver_pump(C.byref(r))==6:pass
        preserved=bytes(d.disk[other*65536:(other+1)*65536]);previous=bytes(d.disk[slot*65536:(slot+1)*65536])
        # A native control-owner read lease with a different active session.
        # HELLO must refuse without replacing that session or touching files.
        if mode=='busy_hello':r.session=77;d.state.phase=3
        if mode=='duplicate':
            assert handle(wire.msg(2,slot,tx,data=struct.pack('<II',len(data),store.crc(data))+info))[0]==0
            for pos in range(0,len(data),256):assert handle(wire.msg(3,slot,tx,pos,data[pos:pos+256]))[0]==0
            assert handle(wire.msg(4,slot,tx))[0]==6
            while lib.cf_receiver_pump(C.byref(r))==6:pass
        client=subprocess.Popen([str(out/'TransferProbe.exe')],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8')
        def send(row):client.stdin.write(json.dumps(row)+'\n');client.stdin.flush()
        flags={'reconcile':True} if mode.startswith('reconcile_') else {'stream':True} if mode in ('stream','busy_hello') else {}
        journal=out/(mode+'-journal') if mode in ('journal_success','reconcile_restart') else None
        if journal is not None:flags['journal']=str(journal)
        send(dict(product=store.PRODUCT,slot=slot,transaction=tx,data=base64.b64encode(data).decode(),info=base64.b64encode(info).decode(),**flags))
        for _ in range(1000):
            line=client.stdout.readline();assert line,client.stderr.read();row=json.loads(line)
            if row.get('done'):result=row;break
            if row.get('phase')=='reconcile':
                assert mode.startswith('reconcile_') and row['blocked'];resolving=True;blocked=True;continue
            raw=bytes(row['packet']);rc,m=wire.decode(lib,raw);assert rc==1 and row['timeout']==1500;ops.append(m.opcode)
            if journal is not None and m.opcode in (2,3,4):
                record=next(journal.glob('*.cfj')).read_bytes();assert record[-32:]==hashlib.sha256(record[:-32]).digest()
                assert struct.unpack_from('<I',record,12)[0]==9 and struct.unpack_from('<I',record,48)[0]==1
                assert struct.unpack_from('<5I',record,56)==(slot,tx,len(data),store.crc(data),1)
                assert record[76:260]==info,'Write reached receiver before pending journal was persisted'
            if m.opcode==5 and d.state.phase==2:
                for i in range(16):
                    if lib.cf_receiver_pump(C.byref(r))!=6:break
            status,ack=handle(m)
            if m.opcode==4:committed=True
            drop=(mode=='lost_begin' and m.opcode==2 or mode=='lost_chunk' and m.opcode==3 and m.number==0 or mode=='lost_commit' and m.opcode==4)
            if drop and not injected:injected=True;send(dict(error='timeout'));continue
            if (mode=='all_receipts_lost' or mode.startswith('reconcile_') and not resolving) and committed:
                while d.state.phase==2:lib.cf_receiver_pump(C.byref(r))
                send(dict(error='timeout'));continue
            if mode=='cancel' and m.opcode==3 and not injected:injected=True;send(dict(error='cancel'));continue
            if mode=='wrong_session' and m.opcode==1:ack.session+=1
            if mode=='wrong_final_crc' and m.opcode==5 and committed and status==0:ack.data[16]^=1
            if mode=='busy_status' and m.opcode==5 and committed and busy_count<2:
                busy_count+=1;ack.bytes=8;ack.data[:8]=struct.pack('<II',store.BUSY,0)
            if mode=='reconcile_busy' and resolving and busy_count==0:
                assert m.opcode==5;busy_count+=1;ack.bytes=8;ack.data[:8]=struct.pack('<II',store.BUSY,0)
            send(dict(packet=list(wire.encode(lib,ack))))
        else:raise AssertionError('Unbounded client requests')
        client.stdin.close();assert client.wait(timeout=10)==0
        rc,meta=d.recover(slot)
        assert bytes(d.disk[other*65536:(other+1)*65536])==preserved
        if mode in ('normal','stream','lost_begin','lost_chunk','lost_commit','duplicate','busy_status','reconcile_commit','reconcile_busy','journal_success','reconcile_restart'):
            assert result['stored'] and not result['activated'] and rc==0 and meta.transaction==tx and meta.generation==1 and bytes(meta.info)==info
            assert result['name']==('IR de pantalla · válvulas 🎸' if slot<3 else 'NAM 5150 · válvulas 🎸')
            if mode=='duplicate':assert not any(op in (2,3,4) for op in ops)
            else:assert result['stages']==['Preparando','Enviando','Verificando']
            if mode=='lost_commit':assert ops.count(4)==1
            if mode.startswith('reconcile_'):assert blocked and resolving and ops.count(4)==1 and ops.count(2)==1 and 6 not in ops
            if journal is not None:
                record=next(journal.glob('*.cfj')).read_bytes();assert struct.unpack_from('<I',record,48)[0]==0
                if mode=='reconcile_restart':assert ops.count(1)==2,'New client did not reconnect from disk journal'
        elif mode in ('all_receipts_lost','wrong_final_crc'):
            assert not result['stored'] and result['error']=='AssetUncertainException' and rc==0 and meta.transaction==tx and ops.count(4)==1
            assert 6 not in ops,'Client canceled an uncertain committed generation'
        else:
            assert not result['stored'] and rc==(store.BUSY if mode=='busy_hello' else store.EMPTY),(mode,result,rc)
            if mode=='busy_hello':assert result['error']=='IOException' and ops==[1] and r.session==77 and d.state.phase==3
            if mode=='cancel':assert result['error']=='OperationCanceledException' and 6 in ops and 4 not in ops
            else:assert bytes(d.disk[slot*65536:(slot+1)*65536])==previous and not any(op in (2,3,4,6) for op in ops)
        cases.append(dict(mode=mode,slot=slot,requests=len(ops),stored=result['stored'],error=result.get('error'),other_slot_preserved=True,uncertain_reupload_blocked=blocked,disk_journal_checked=journal is not None))
    report=dict(passed=True,USB_opened=False,SDK_loaded=False,hardware_accessed=False,actual_C_receiver=True,actual_CSharp_client=True,activation_claimed=False,cases=cases,
        sources={str(p.relative_to(wire.TV)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources+[ROOT/'verify_transfer.py']},
        not_validated=['Native device transport and exclusive SDK handover','Audio activation','Physical flash durability','UI wiring'])
    (out/'result.json').write_text(json.dumps(report,indent=2));(ROOT/'checks/transfer-latest.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':
    try:main()
    except BaseException:
        import traceback
        (ROOT/'checks/transfer-error.txt').write_text(traceback.format_exc());raise
