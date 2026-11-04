"""Isolated actor with Main-owned native Boost reconciliation; no device IO."""
import hashlib,importlib.util,json,sys
from pathlib import Path
BASE=Path(__file__).resolve().parent;LAB=BASE/'control-owner-lab';NAM=BASE.parent
EDITOR=NAM.parent/'catalyst-editor';sys.path[:0]=[str(EDITOR/'tools-recovery'),str(NAM)]
import unicorn,capstone
spec=importlib.util.spec_from_file_location('stock_control_offsets',NAM/'dual-lab/verify_lab.py')
fixture=importlib.util.module_from_spec(spec);spec.loader.exec_module(fixture)
audio=json.loads((LAB/'build/build.json').read_text());symbols=audio['symbols']
assert audio['native_setter_from_audio_removed'] and symbols['factory_amp']['address'] and symbols['stock']['address']
source=NAM/'asset-lab/build_recovery_actor.py';text=source.read_text(encoding='utf-8')
sys.path.insert(0,str(source.parent))
def change(old,new):
 global text
 assert text.count(old)==1,(old,text.count(old));text=text.replace(old,new)
change('ROOT=Path(__file__).resolve().parent;TV=ROOT.parents[2]',
       'LAB=Path(__file__).resolve().parent/"control-owner-lab";ROOT=LAB.parent.parent/"asset-lab";TV=ROOT.parents[2]')
start=text.index("    out=ROOT/('actor-owner-lab'");end=text.index('\n',start)
text=text[:start]+'    out=LAB/"actor-build";out.mkdir(parents=True,exist_ok=True)'+text[end:]
text=text.replace("ROOT.parent/'dual-lab/lease-build", "LAB/'build")
change('A.receiver.capabilities|=8u;', 'A.receiver.capabilities|=56u;')
change('extern int cf_owner_bootstrap(void);\\nextern int cf_owner_diagnostic',
       'extern int cf_owner_bootstrap(void);\\nextern int cf_stock_control_diagnostic(CfAssetReceiver*,const CfWireMessage*,CfWireMessage*);\\nextern int cf_owner_diagnostic')
change("text=text.replace('}else if(A.incoming.opcode>=9', '}else if(A.incoming.opcode==13)cf_owner_diagnostic(&A.receiver,&A.incoming,&A.outgoing);else if(A.incoming.opcode>=9')",
       "text=text.replace('}else if(A.incoming.opcode>=9', '}else if(A.incoming.opcode==14)cf_stock_control_diagnostic(&A.receiver,&A.incoming,&A.outgoing);else if(A.incoming.opcode==13)cf_owner_diagnostic(&A.receiver,&A.incoming,&A.outgoing);else if(A.incoming.opcode>=9')")
bindings={'CF_STOCK_STATE_ADDRESS':symbols['stock']['address'],'CF_FACTORY_AMP_ADDRESS':symbols['factory_amp']['address']|1,'CF_TRIAL_NODE_CONTROL_ADDRESS':symbols['trial_node_control']['address']|1,'CF_SEQUENCE_OFFSET':fixture.Combined.sequence.offset,'CF_PENDING_OFFSET':fixture.Combined.pending.offset,'CF_PEDAL_MODE_OFFSET':fixture.Combined.pedal.offset,'CF_BOOST_ENABLED_OFFSET':fixture.Combined.boost_enabled.offset}
switches=json.loads((LAB/'build/OEM-controls.json').read_text())
header='/* Generated from this exact audio ELF; no supplied device addresses. */\n'+''.join('#define %s %du\n'%(k,v) for k,v in bindings.items())+'#define CF_STOCK_SWITCH_IDS {'+','.join(str(x['params']['BoostSwitch']) for x in switches)+'}\n'
marker='    for i,source in enumerate(sources):'
change(marker,'''    (out/'stock_binding.h').write_text('''+repr(header)+''',encoding='utf-8')
    sources.extend([LAB/'stock_control.c',LAB/'control_loader.S'])
'''+marker)
change("dependencies=[original,ROOT/'asset_store.c',ROOT/'build_recovery_actor.py',linker]",
       "dependencies=[original,ROOT/'asset_store.c',ROOT/'build_recovery_actor.py',Path(__file__),LAB/'stock_control.c',out/'stock_binding.h',linker]")
scope={'__name__':'main_control_actor_builder','__file__':str(Path(__file__).resolve())}
sys.argv=[str(Path(__file__)),'--owner-loop'];exec(compile(text,str(source),'exec'),scope);scope['main']()
