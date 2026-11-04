"""Freshness of the private all-model prototype; never release approval."""
import hashlib,json
from pathlib import Path

def verify(workspace,segment):
    workspace=Path(workspace).resolve();root=workspace/'research/catalyst-nam/all-amps-lab'
    def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
    def read(name):return json.loads((root/name).read_text(encoding='utf-8-sig'))
    audio,actor,boot,native=(read(n) for n in ('build/build.json','actor-build/build.json','boot-build/tests.json','build/native-tests.json'))
    assert audio['static_code_and_tables_fit'] and audio['all_models']==12 and actor['native_owner_loop']
    assert boot['passed'] and not boot['native_reference_collision_requires_resolution']
    assert boot['segment_sha256']==sha(Path(segment)) and boot['audio_elf_sha256']==audio['elf_sha256']==sha(root/'build/combined.elf')
    assert boot['actor_elf_sha256']==actor['elf_sha256']==sha(root/'actor-build/actor.elf')
    assert native['passed'] and len(native['models'])==12 and native['elf_sha256']==audio['elf_sha256']
    assert native['verification_source_sha256']==sha(root/'verify_all_amps.py')
    assert boot['verification_source_sha256']==sha(root/'verify_boot_variant.py')
    for source_map in (audio['sources'],actor['source_sha256']):
        for relative,expected in source_map.items():
            path=(workspace/relative.replace('\\','/')).resolve();assert path.is_relative_to(workspace) and sha(path)==expected,relative
    return dict(passed=True,hardware_accessed=False,hardware_approval_inferred=False,private_all_models_trial=True,segment_sha256=sha(Path(segment)),actor_elf_sha256=actor['elf_sha256'],audio_elf_sha256=audio['elf_sha256'])

def user_trial(workspace,segment):
    result=verify(workspace,segment);workspace=Path(workspace).resolve();root=workspace/'research/catalyst-nam/all-amps-lab'
    owner=json.loads((root/'owner-tests-final/owner-loop-tests.json').read_text(encoding='utf-8'))
    control=json.loads((root/'build/native-controls-final.json').read_text(encoding='utf-8'))
    assert owner['passed'] and owner['packaged_reset_copy_verified'] and owner['actor_elf_sha256']==result['actor_elf_sha256'] and owner['audio_elf_sha256']==result['audio_elf_sha256']
    assert control['passed'] and control['elf_sha256']==result['audio_elf_sha256']
    for relative,expected in owner['host_sources'].items():
        path=workspace/relative.replace('\\','/');assert hashlib.sha256(path.read_bytes()).hexdigest()==expected,relative
    result.update(private_user_trial_products=[2424833],physical_validation_pending=True,public_release_approved=False)
    return result
