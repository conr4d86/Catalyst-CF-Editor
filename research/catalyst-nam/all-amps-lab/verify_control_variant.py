"""Freshness gate for the corrected DSP-owner engine; no device operations."""
import hashlib,json
from pathlib import Path

def basic(workspace,segment):
    workspace=Path(workspace).resolve()
    base=workspace/'research/catalyst-nam/all-amps-lab'
    root=base/'control-owner-lab'
    def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
    def read(path): return json.loads((root/path).read_text(encoding='utf-8-sig'))
    audio,actor,boot=(read(p) for p in ('build/build.json','actor-build/build.json','boot-build/tests.json'))
    assert audio['compiled'] and audio['static_code_and_tables_fit'] and audio['all_models']==12
    assert audio['native_setter_from_audio_removed'] and actor['native_owner_loop']
    assert boot['passed'] and not boot['native_reference_collision_requires_resolution']
    assert boot['dsp_control_owner_hook'] and boot['no_native_graph_setter_in_audio'] and boot['HELLO_capability_32_requires_DSP_hook']
    assert boot['segment_sha256']==sha(segment)
    assert boot['audio_elf_sha256']==audio['elf_sha256']==sha(root/'build/combined.elf')
    assert boot['actor_elf_sha256']==actor['elf_sha256']==sha(root/'actor-build/actor.elf')
    assert boot['verification_source_sha256']==sha(base/'build_control_boot.py')
    assert 'main_wait=0x8000b9b0' in (base/'build_control_boot.py').read_text()
    for sources in (audio['sources'],actor['source_sha256']):
        for relative,digest in sources.items():
            path=(workspace/relative.replace('\\','/')).resolve()
            assert path.is_relative_to(workspace) and sha(path)==digest,relative
    return dict(passed=True,hardware_accessed=False,physical_validation_pending=True,
                segment_sha256=sha(segment),audio_elf_sha256=audio['elf_sha256'],
                actor_elf_sha256=actor['elf_sha256'],DSP_control_wait='0x8000b9b0')

def user_trial(workspace,segment):
    result=basic(workspace,segment)
    workspace=Path(workspace).resolve()
    base=workspace/'research/catalyst-nam/all-amps-lab'
    root=base/'control-owner-lab'
    def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
    def read(path): return json.loads((root/path).read_text(encoding='utf-8-sig'))
    native=read('build/native-tests.json')
    assert native['passed'] and len(native['models'])==12
    assert native['elf_sha256']==result['audio_elf_sha256'] and native['actor_sha256']==result['actor_elf_sha256'] and native['segment_sha256']==result['segment_sha256']
    assert native['verification_source_sha256']==sha(base/'verify_control_native.py')
    assert native['old_unsafe_binary']['rejected_from_audio']
    assert native['old_unsafe_binary']['elf_sha256']==sha(base/'build/combined.elf')
    assert native['queue_zero_unexpected_only_returns_real_message'] and native['queue_finite_foreign_preserved']
    assert native['unexpected_receive_yields_one_tick'] and native['negative_loader_preserves_rc'] and native['release_failure_fail_closed']
    assert native['ABA_native_reload']['same_addresses']
    assert native['unrelated_FX_load_rebinds'] and native['inactive_graph_controls_rejected']
    assert native['new_generation_during_setter_reconciled'] and native['opcode14_fixed_read_only']
    for model in native['models']:
        assert model['native_off_on'] and model['custom_bypasses_OEM_before_audio'] and model['restores_factory_Boost']
        assert model['outer_mutex_busy_skips'] and model['audio_FS_IRQ_rejected'] and model['finite_nonzero_native_audio']
        assert model['DSP_stack_reserve']>=256
    controls=read('build/native-controls-final.json')
    assert controls['passed'] and controls['elf_sha256']==result['audio_elf_sha256']
    assert controls['verification_source_sha256']==sha(base.parent/'dual-lab/verify_native_controls.py')
    for relative,digest in controls['OEM_sources_sha256'].items():
        assert sha(workspace/relative.replace('\\','/'))==digest
    owner=read('owner-tests-r106/owner-loop-tests.json')
    assert owner['passed'] and owner['packaged_reset_copy_verified']
    assert owner['actor_elf_sha256']==result['actor_elf_sha256'] and owner['audio_elf_sha256']==result['audio_elf_sha256']
    assert owner['verification_sha256']==sha(base/'verify_control_owner.py')
    package=workspace/'research/catalyst-editor/checks/editor-r105-bootstrap/firmware/demos-offline-NOT-INSTALLED.ctf'
    assert owner['package_sha256']==sha(package)
    for relative,digest in owner['host_sources'].items():
        assert sha(workspace/relative.replace('\\','/'))==digest,relative
    result.update(private_user_trial_products=[2424833],physical_validation_pending=True,
                  old_r103_binary_regression_detected=True,native_models_tested=12,
                  same_capture_NAM_control_parity=True,exact_package_owner_verified=True,
                  public_release_approved=False)
    return result
