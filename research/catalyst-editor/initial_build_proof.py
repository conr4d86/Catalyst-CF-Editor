"""Read-only freshness checks for the embedded initial CF, never hardware approval."""
import hashlib
import json
from pathlib import Path


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify(workspace, segment):
    workspace = Path(workspace).resolve()
    nam = workspace / 'research/catalyst-nam'
    actor = nam / 'asset-lab/actor-owner-lab/actor-build'
    audio = nam / 'dual-lab/lease-build'
    boot = nam / 'asset-lab/boot-owner-lab/tests.json'
    proofs = {}

    def need(ok, message):
        if not ok:
            raise ValueError('Initial CF proof is stale: ' + message)

    def read(path):
        proofs[str(path.relative_to(workspace))] = digest(path)
        return json.loads(path.read_text(encoding='utf-8-sig'))

    def sources(items):
        need(bool(items), 'empty source fingerprint list')
        for relative, expected in items.items():
            path = (workspace / relative.replace('\\', '/')).resolve()
            need(path.is_relative_to(workspace), 'source outside workspace')
            need(digest(path) == expected, relative)

    ai, di, bi = read(actor / 'build.json'), read(audio / 'build.json'), read(boot)
    need(ai['compiled'] and ai['native_owner_loop'], 'wrong actor variant')
    need(di['compiled'] and di['leases_enabled'], 'wrong audio variant')
    sources(ai['source_sha256'])
    sources(di['sources'])
    need(digest(actor / 'actor.elf') == ai['elf_sha256'], 'actor ELF')
    need(digest(audio / 'combined.elf') == di['elf_sha256'], 'audio ELF')
    need(bi['passed'] and bi['owner_loop_included'], 'boot checks failed')
    need(bi['actor_elf_sha256'] == ai['elf_sha256'], 'boot uses another actor')
    need(bi['audio_elf_sha256'] == di['elf_sha256'], 'boot uses another audio engine')
    need(bi['segment_sha256'] == digest(Path(segment)), 'boot uses another segment')
    need(bi['verification_source_sha256'] == digest(nam / 'asset-lab/verify_boot_layout.py'), 'boot verification changed')
    li = read(audio / 'lease-tests.json')
    need(li['passed'] and li['elf_sha256'] == di['elf_sha256'], 'audio checks use another ELF')
    need(li['verification_source_sha256'] == digest(nam / 'dual-lab/verify_leases.py'), 'audio verification changed')
    ci = read(audio / 'native-controls.json')
    need(ci['passed'] and ci['elf_sha256'] == di['elf_sha256'], 'native control checks use another ELF')
    need(ci['verification_source_sha256'] == digest(nam / 'dual-lab/verify_native_controls.py'), 'native control verification changed')
    sources(ci['OEM_sources_sha256'])
    return dict(passed=True, hardware_accessed=False, hardware_approval_inferred=False,
                segment_sha256=bi['segment_sha256'], actor_elf_sha256=ai['elf_sha256'],
                audio_elf_sha256=di['elf_sha256'], evidence=proofs)
