"""Build the same serialized owner against the new all-model audio symbols.

Keeps the observed actor, source generator and build artifacts untouched.
No firmware image, peripheral access or physical approval is produced.
"""
import hashlib,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent
source=ROOT.parent/'asset-lab/build_recovery_actor.py'
sys.path.insert(0,str(source.parent))
before=hashlib.sha256(source.read_bytes()).hexdigest()
text=source.read_text(encoding='utf-8')
def change(old,new):
    global text
    assert text.count(old)==1,(old,text.count(old))
    text=text.replace(old,new)
change('ROOT=Path(__file__).resolve().parent;TV=ROOT.parents[2]',
       'LAB=Path(__file__).resolve().parent;ROOT=LAB.parent/"asset-lab";TV=ROOT.parents[2]')
start=text.index("    out=ROOT/('actor-owner-lab'");end=text.index('\n',start)
text=text[:start]+'    out=LAB/"actor-build";out.mkdir(parents=True,exist_ok=True)'+text[end:]
text=text.replace("ROOT.parent/'dual-lab/lease-build", "LAB/'build")
change('A.receiver.capabilities|=8u;', 'A.receiver.capabilities|=24u;')
change("dependencies=[original,ROOT/'asset_store.c',ROOT/'build_recovery_actor.py',linker]",
       "dependencies=[original,ROOT/'asset_store.c',ROOT/'build_recovery_actor.py',Path(__file__),linker]")
scope={'__name__':'all_model_actor_builder','__file__':str(Path(__file__).resolve())}
sys.argv=[str(Path(__file__)),'--owner-loop']
exec(compile(text,str(source),'exec'),scope)
scope['main']()
assert hashlib.sha256(source.read_bytes()).hexdigest()==before
