"""Private exact-image trial app. Does not grant a public release approval.

Uses the normal single-EXE sources with a separately pinned all-model engine.
Enables installation only for the explicitly authorized private 60/CX60 trial.
Physical validation and public release approval remain pending.
"""
import hashlib,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent;EDITOR=ROOT.parent.parent/'catalyst-editor'
sys.path[:0]=[str(ROOT),str(EDITOR)]
from verify_variant import user_trial as verify
workspace=EDITOR.parents[1];segment=ROOT/'boot-build/NON-INSTALLABLE-segment.bin'
proof=verify(workspace,segment);digest=hashlib.sha256(segment.read_bytes()).hexdigest()
text=(EDITOR/'build.py').read_text(encoding='utf-8')
text=text.replace("cf_segment=ROOT.parent/'catalyst-nam/asset-lab/boot-owner-lab/NON-INSTALLABLE-segment.bin'",'cf_segment=ROOT.parent/"catalyst-nam/all-amps-lab/boot-build/NON-INSTALLABLE-segment.bin"')
text=text.replace('ff8bbdc15b631b5a1f1909fa477c06837ab6181406acab2b0061fdd55538595a',digest)
text=text.replace('from initial_build_proof import verify as verify_initial_build','from verify_variant import verify as verify_initial_build')
text=text.replace('from verify_variant import verify as verify_initial_build','from verify_variant import user_trial as verify_initial_build')
text=text.replace('approved=[];physically_verified=[];family_unverified=[]',
                  'approved=[2424833];physically_verified=[];family_unverified=[2424833] # Explicit private user trial, not public/physical approval')
marker="    executable=OUT/('OfflineManagedChecks.exe' if MANAGED_CHECKS else 'CatalystCFEditor.exe')"
assert text.count(marker)==1
text=text.replace(marker,'''    patched_builder=OUT/'CFPackageBuilder.cs'
    patched_builder.write_text((ROOT/'CFPackageBuilder.cs').read_text(encoding='utf-8').replace('ff8bbdc15b631b5a1f1909fa477c06837ab6181406acab2b0061fdd55538595a','''+repr(digest)+'''),encoding='utf-8')
    sources=[patched_builder if path==ROOT/'CFPackageBuilder.cs' else path for path in sources]
'''+marker)
scope={'__name__':'private_all_models_app','__file__':str((EDITOR/'build.py').resolve())}
exec(compile(text,str(EDITOR/'build.py'),'exec'),scope)
scope['OUT']=(workspace/'CatalystEditor/Development/update-20261104-r103-user-test').resolve()
scope['BOOTSTRAP_DEMOS']=True
scope['main']()
