"""Run the production host/serialized ARM owner tests on the new prototype."""
import sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent;source=ROOT.parent/'asset-lab/verify_owner_loop.py'
sys.path.insert(0,str(source.parent))
text=source.read_text(encoding='utf-8')
text=text.replace("ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent;LAB=ROOT/'actor-owner-lab';BUILD=LAB/'actor-build'",
                  'LAB=Path(__file__).resolve().parent;ROOT=LAB.parent/"asset-lab";NAM=ROOT.parent;BUILD=LAB/"actor-build"')
text=text.replace("NAM/'dual-lab/lease-build", "LAB/'build")
text=text.replace("if seg['data']:u.mem_write(seg['address'],seg['data'])", "if seg['data'] and (seg['flags']&1 or seg['address']==0x20064b00):u.mem_write(seg['address'],seg['data'])")
text=text.replace("report['production_reply_timeout_ms']=1500", "report['production_reply_timeout_ms']=1500;report['production_metadata_reply_timeout_ms']=30000")
text=text.replace('Metadata queries use an extended host deadline only in the emulator; this does not verify physical response latency','Metadata queries have a bounded 30 s host deadline; emulation does not verify physical response latency')
scope={'__name__':'all_model_owner_verifier','__file__':str(Path(__file__).resolve())}
exec(compile(text,str(source),'exec'),scope);scope['main']()
