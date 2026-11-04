"""Data-only reuse of a pinned, physically observed engine; no device access."""
import hashlib
import json
from pathlib import Path

REFERENCE = '5ee8770e3d5afe84d424b8b65a8d94433ba541ec5b1727b727a5e41055bff610'
def digest(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()

def verify(workspace, permit, segment, transport, demos):
    workspace = Path(workspace).resolve()
    def need(ok, message):
        if not ok:
            raise ValueError('Initial release evidence: ' + message)
    def path(name):
        p = (workspace / name.replace('\\', '/')).resolve()
        need(p.is_relative_to(workspace), 'path outside workspace')
        return p
    def read(name):
        return json.loads(path(name).read_text(encoding='utf-8-sig'))
    need(permit['schema'] == 2 and permit['basis'] == 'observed-engine-data-only', 'unknown approval basis')
    # No physical support is inferred for another power/model variant.
    need(permit['products'] == permit['physically_verified_products'] == [2424833]
         and permit['family_unverified_products'] == [], 'model exceeds physical reference')
    need(permit['segment_sha256'] == digest(segment) and permit['transport_sha256'] == digest(transport), 'engine or transport changed')
    for name, expected in permit['evidence'].items():
        need(digest(path(name)) == expected, 'changed evidence: ' + name)
    for name, expected in permit['validation_sources'].items():
        need(digest(path(name)) == expected, 'changed validator: ' + name)
    need(digest(path(permit['reference_image'])) == REFERENCE, 'wrong physical reference image')
    review = read(permit['review'])
    need(review['passed'] and review['reference_sha256'] == REFERENCE, 'offline reference comparison')
    need(review['protected_boot_code_native_images_and_metadata_match_reference']
         and review['only_seed_manifest_edition_and_resources_can_vary'], 'code changes not allowed')
    need(review['demo_bundle_sha256'] == digest(demos) and review['NAM_input_db'] == -5 and review['NAM_output_db'] == -3 and not review['NAM_automatic']
         and review['initial_slots'] == 5 and review['NAM_demos_identical'], 'demo package changed')
    need(digest(path(permit['demo_candidate'])) == review['demo_candidate_sha256'], 'reviewed demo image changed')
    owner = read(permit['owner_test'])
    need(owner['passed'] and owner['packaged_reset_copy_verified']
         and owner['package_sha256'] == review['demo_candidate_sha256'], 'packaged owner/reset emulation missing')
    boot = read(permit['physical_boot'])
    need(boot['boot_confirmed'] and boot['user_confirmed_lights_and_sound']
         and boot['identity_matches_edition'] and boot['shutdown_confirmed']
         and boot['candidate_sha256'] == REFERENCE, 'physical boot not confirmed')
    receiver = read(permit['physical_receiver'])
    need(receiver['passed'] and receiver['shutdown_confirmed']
         and receiver['identity'].split(':')[-1] == review['reference_edition'], 'physical receiver edition mismatch')
    observed = read(permit['physical_audio'])
    need(observed['candidate_sha256'] == REFERENCE and observed['user_confirmed_normal_boot']
         and observed['all_owned_connections_closed'] and observed['measured_model'] == 'Catalyst 60', 'physical observation mismatch')
    need(all(x['added_late_periods'] == 0 and x['NAM_faults'] == [0] and x['preamp_faults'] == [0]
             for x in observed['pedal_observations']), 'reference audio fault recorded')
    journal = read(permit['installation_journal'])
    need(journal['Phase'] == 'active' and journal['Product'] == 2424833
         and journal['Hash'] == REFERENCE and journal['Edition'] == review['reference_edition'], 'installed reference not active')
    return dict(passed=True, basis=permit['basis'], product=2424833,
                reference_image_sha256=REFERENCE, data_only_comparison=True,
                hardware_accessed_now=False, physical_demo_installation_claimed=False,
                general_memory_IRQ_certification_claimed=False,
                owner_emulation_for_demo_package=True)
