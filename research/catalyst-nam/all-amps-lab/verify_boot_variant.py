"""Check the new actor/audio reset copies without changing observed artifacts."""
import hashlib,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent
source=ROOT.parent/'asset-lab/verify_boot_layout.py'
sys.path.insert(0,str(ROOT.parent.parent/'catalyst-editor/tools-recovery'))
import unicorn,capstone
sys.path.insert(0,str(source.parent))
text=source.read_text(encoding='utf-8')
def change(old,new):
    global text
    assert text.count(old)==1,(old,text.count(old));text=text.replace(old,new)
change('ROOT=Path(__file__).resolve().parent;NAM=ROOT.parent;IR=NAM.parent/\'catalyst-ir\';EDITOR=NAM.parent/\'catalyst-editor\'',
       'LAB=Path(__file__).resolve().parent;ROOT=LAB.parent/"asset-lab";NAM=ROOT.parent;IR=NAM.parent/"catalyst-ir";EDITOR=NAM.parent/"catalyst-editor"')
change("actor_folder=ROOT/('actor-owner-lab/actor-build' if options.owner_loop else 'actor-build')",'actor_folder=LAB/"actor-build"')
change("audio_folder=NAM/'dual-lab'/('lease-build' if options.owner_loop else 'build')",'audio_folder=LAB/"build"')
change("out=ROOT/('boot-owner-lab' if options.owner_loop else 'boot-build');out.mkdir(exist_ok=True)", 'out=LAB/"boot-build";out.mkdir(exist_ok=True)')
sys.argv=[str(Path(__file__)),'--owner-loop']
scope={'__name__':'all_model_boot_verifier','__file__':str(Path(__file__).resolve())}
exec(compile(text,str(source),'exec'),scope);scope['main']()
