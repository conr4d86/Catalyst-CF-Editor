"""Build the corrected engine app; bootstrap cannot install firmware.

No SDK, USB or device API is opened by this build. Final hardware-trial builds
require the separate complete proof gate and explicit existing user authority.
"""
import argparse,hashlib,sys
from pathlib import Path
BASE=Path(__file__).resolve().parent
EDITOR=BASE.parent.parent/'catalyst-editor'
sys.path[:0]=[str(BASE),str(EDITOR)]
from verify_control_variant import basic,user_trial

parser=argparse.ArgumentParser()
parser.add_argument('--bootstrap',action='store_true')
args=parser.parse_args()
workspace=EDITOR.parents[1]
segment=BASE/'control-owner-lab/boot-build/NON-INSTALLABLE-segment.bin'
(basic if args.bootstrap else user_trial)(workspace,segment)
digest=hashlib.sha256(segment.read_bytes()).hexdigest()
text=(EDITOR/'build.py').read_text(encoding='utf-8')
old="cf_segment=ROOT.parent/'catalyst-nam/asset-lab/boot-owner-lab/NON-INSTALLABLE-segment.bin'"
assert text.count(old)==1
text=text.replace(old,'cf_segment=ROOT.parent/"catalyst-nam/all-amps-lab/control-owner-lab/boot-build/NON-INSTALLABLE-segment.bin"')
text=text.replace('ff8bbdc15b631b5a1f1909fa477c06837ab6181406acab2b0061fdd55538595a',digest)
old='from initial_build_proof import verify as verify_initial_build'
assert text.count(old)==1
text=text.replace(old,'from verify_control_variant import '+('basic' if args.bootstrap else 'user_trial')+' as verify_initial_build')
if not args.bootstrap:
    old='approved=[];physically_verified=[];family_unverified=[]'
    assert text.count(old)==1
    text=text.replace(old,'approved=[2424833];physically_verified=[];family_unverified=[2424833] # Existing explicit user trial authority, not a claim of physical validation')
marker="    executable=OUT/('OfflineManagedChecks.exe' if MANAGED_CHECKS else 'CatalystCFEditor.exe')"
assert text.count(marker)==1
text=text.replace(marker,"""    patched_builder=OUT/'CFPackageBuilder.cs'
    patched_builder.write_text((ROOT/'CFPackageBuilder.cs').read_text(encoding='utf-8').replace('ff8bbdc15b631b5a1f1909fa477c06837ab6181406acab2b0061fdd55538595a',"""+repr(digest)+"""),encoding='utf-8')
    sources=[patched_builder if path==ROOT/'CFPackageBuilder.cs' else path for path in sources]
"""+marker)
scope={'__name__':'corrected_control_owner_app','__file__':str(EDITOR/'build.py')}
exec(compile(text,str(EDITOR/'build.py'),'exec'),scope)
scope['OUT']=(workspace/('CatalystEditor/Development/update-20261104-r105-control-owner-'+('bootstrap' if args.bootstrap else 'verified'))).resolve()
scope['BOOTSTRAP_DEMOS']=True
scope['main']()
