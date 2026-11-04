"""Derive explicit source eviction, preserving the original actor source."""
import hashlib,json
from pathlib import Path
ROOT=Path(__file__).resolve().parent
def derive():
    p=ROOT/'ir_handoff.c';s=p.read_text();pin=json.loads((ROOT/'actor-build/build.json').read_text())['source_sha256']['ir_handoff.c']
    assert hashlib.sha256(p.read_bytes()).hexdigest()==pin
    marker='unsigned cf_ir_boundary(void){';assert s.count(marker)==1
    s=s.replace(marker,'''int cf_ir_evict_request(unsigned tx,unsigned source){
    if(!source||(source&3u))return CF_BAD_INPUT;
    return request(tx,3,0,source);
}
'''+marker)
    a='''        if(I.kind==1){I.source[I.slot]=I.pointer;I.mask|=1u<<I.slot;'''
    b='''        if(I.kind==3){
            unsigned selected=I.selected<3&&I.source[I.selected]==I.pointer;
            for(unsigned slot=0;slot<3;++slot)if(I.source[slot]==I.pointer){I.source[slot]=0;I.mask&=~(1u<<slot);}
            if(selected){IR_RAM[0]=0;IR_RAM[7]=0;IR_RAM[3]=0;IR_RAM[4]=0;}
            I.copy_source=0;barrier();I.state=2;return 0;
        }
        if(I.kind==1){I.source[I.slot]=I.pointer;I.mask|=1u<<I.slot;'''
    assert s.count(a)==1;s=s.replace(a,b)
    # A new selection may interrupt the FS producer halfway through publishing
    # its command. The audio owner must defer, not clear that in-flight slot.
    a='''    I.state=3;barrier();I.transaction=tx;'''
    b='''    unsigned expected=__atomic_load_n(&I.state,__ATOMIC_ACQUIRE);
    if(expected==1||expected==3||!__atomic_compare_exchange_n(&I.state,&expected,3,0,__ATOMIC_ACQ_REL,__ATOMIC_ACQUIRE))return CF_BUSY;
    barrier();I.transaction=tx;'''
    assert s.count(a)==1;s=s.replace(a,b)
    s+='''
/* Only the outer audio owner may supersede its own preparation. A native
 * selector event never writes the audio history from the control task. */
int cf_ir_supersede(unsigned slot,unsigned graph){
    if(I.magic!=CTX_MAGIC||slot>=3||(graph&3u)||graph<0x20000000u||graph>0x20050000u-24u)return CF_BAD_INPUT;
    unsigned expected=__atomic_load_n(&I.state,__ATOMIC_ACQUIRE);
    if(expected==3||!__atomic_compare_exchange_n(&I.state,&expected,3,0,__ATOMIC_ACQ_REL,__ATOMIC_ACQUIRE))return CF_BUSY;
    I.phase=0;I.transaction=0;I.copy_source=0;I.selected=3;
    IR_RAM[0]=IR_RAM[3]=IR_RAM[4]=IR_RAM[7]=0;IR_RAM[1]=slot;IR_RAM[6]=graph;barrier();I.state=0;return CF_OK;
}
'''
    return s
