"""Package reviewed r106 artifacts locally; never opens a device or publishes."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import zipfile

EXE_SHA = 'a2595487dded2c2df297781b7009458e42a7365a1876dcce6bb2a9f7d5b79a16'
OFFICIAL_SHA = 'f9fe2482e561bef8a04128a38af89cadee4f8d06071b06a9053e36c206471ae8'
ENGINE = {
    'audio_elf_sha256': 'a652621cc772759d5a0912cff1fd4d9c383410d4c3737f13a3e2a3bb98237c91',
    'actor_elf_sha256': '329cb0dfb8164200040ccc8092a583e81c9eb4e63ca4e1850f4b988452080654',
    'segment_sha256': 'a4f5c1038787b91a2c0272d1f64cb6d55bfba3bc58d0b67bcba875aa9f7c5ff8',
}
GROUPS = {'capability', 'preset-selection', 'journal', 'midi', 'data-session',
          'access', 'firmware', 'data', 'system', 'native-preamp',
          'install-fixture', 'interaction', 'self'}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def entry(archive, name, data):
    # Fixed metadata gives identical ZIPs for an identical reviewed input tree.
    info = zipfile.ZipInfo(name, date_time=(2026, 11, 4, 0, 0, 0))
    info.compress_type = zipfile.ZIP_DEFLATED
    info.external_attr = 0o100644 << 16
    archive.writestr(info, data, compresslevel=9)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--root', type=Path, required=True)
    p.add_argument('--executable', type=Path, required=True)
    p.add_argument('--official-recovery', type=Path, required=True)
    p.add_argument('--validation-suite', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--version', default='1.0.0')
    p.add_argument('--commit', default=None)
    p.add_argument('--physical-preset-check', choices=('pending', 'passed'), default='pending')
    p.add_argument('--gui-preset-check', choices=('pending', 'passed'), default='pending')
    p.add_argument('--publication-status', choices=('local-review', 'reviewed'), default='local-review')
    a = p.parse_args()
    root, out = a.root.resolve(), a.output.resolve()
    if out == root or root in out.parents:
        raise ValueError('Release output must be outside the source tree')
    if not re.fullmatch(r'[0-9A-Za-z][0-9A-Za-z._-]{0,63}', a.version):
        raise ValueError('Invalid version filename')
    if a.commit is not None and not re.fullmatch(r'[0-9a-fA-F]{40}', a.commit):
        raise ValueError('Use a full source commit, or omit it for an uncommitted local review')
    if a.publication_status == 'reviewed' and (a.commit is None or a.physical_preset_check != 'passed' or a.gui_preset_check != 'passed'):
        raise ValueError('Reviewed packaging needs the final source commit and physical plus GUI preset confirmations')
    exe, official = a.executable.read_bytes(), a.official_recovery.read_bytes()
    if digest(exe) != EXE_SHA:
        raise ValueError('Executable differs from the reviewed r106 artifact')
    if digest(official) != OFFICIAL_SHA:
        raise ValueError('Official recovery firmware differs')
    suite_bytes = a.validation_suite.read_bytes()
    suite = json.loads(suite_bytes.decode('utf-8-sig'))
    checks = suite.get('checks', [])
    if not (suite.get('passed') is True and suite.get('hardware_accessed') is False
            and suite.get('exe_sha256') == EXE_SHA and len(checks) == 13
            and {c.get('name') for c in checks} == GROUPS
            and all(c.get('exit_code') == 0 for c in checks)):
        raise ValueError('The exact executable needs all thirteen offline test groups')
    inventory_bytes = (root / 'SOURCE-INVENTORY.json').read_bytes()
    curated = json.loads(inventory_bytes.decode('utf-8'))
    if any(curated.get('selected_engine', {}).get(k) != v for k, v in ENGINE.items()):
        raise ValueError('Source snapshot identifies another engine')
    for row in curated['source_files']:
        path = (root / row['path']).resolve()
        if not path.is_relative_to(root) or digest(path.read_bytes()) != row['sha256']:
            raise ValueError('Curated source hash mismatch: ' + row['path'])
    sources = []
    banned_dirs = {'.git', '__pycache__', 'node_modules', 'backups', 'hardware-trials', 'local-cache', 'global-cache'}
    banned_ext = {'.exe', '.dll', '.bin', '.ctf', '.hxf', '.dfu', '.elf', '.o', '.obj', '.pdb', '.lib',
                  '.nam', '.wav', '.f32', '.catl6a', '.catl6p', '.log', '.cfj', '.lock', '.pyc'}
    secrets = re.compile(rb'(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[A-Z0-9]{16}|sk-proj-[A-Za-z0-9_-]{20,})')
    private_prefixes = [str(Path.home()).lower().encode(), Path.home().as_posix().lower().encode()]
    for path in sorted(root.rglob('*')):
        relative = path.relative_to(root)
        if any(part in banned_dirs for part in relative.parts) or not path.is_file():
            continue
        if path.suffix.lower() in banned_ext:
            raise ValueError('Unexpected private/binary source entry: ' + relative.as_posix())
        data = path.read_bytes()
        if secrets.search(data) or any(prefix in data.lower() for prefix in private_prefixes):
            raise ValueError('Private path or credential pattern: ' + relative.as_posix())
        sources.append((relative.as_posix(), data))
    out.mkdir(parents=True, exist_ok=True)
    (out / 'CatalystCFEditor.exe').write_bytes(exe)
    windows = out / ('CatalystCFEditor-' + a.version + '-windows.zip')
    source = out / ('CatalystCFEditor-' + a.version + '-source.zip')
    readme = (
        'Catalyst CF Editor ' + a.version + ' / host r106\n\n'
        'ES: Abre CatalystCFEditor.exe. Lee docs/INICIO_RAPIDO.md y docs/RECUPERACION.md.\n'
        'EN: Open CatalystCFEditor.exe. Read docs/QUICKSTART.md and docs/RECUPERACION.md.\n\n'
        'Primera instalacion / First installation: FIRMWARE CF.\n'
        'Despues / Afterwards: CAMBIAR NAM / IR / CHANGE NAM / IR.\n'
        'Reinstalar activa los ejemplos en una nueva edicion; no migra recursos anteriores.\n'
        'Reinstall activates demos in a new edition; earlier resources are not migrated.\n'
        'Si la app pide actualizar, necesita la copia verificada de esa edicion.\n'
        'If the app requires an update, it needs the verified backup for that edition.\n'
        'Las copias nativas de presets no incluyen NAM/IR ni ajustes adicionales CF.\n'
        'Native preset backups do not include NAM/IR or additional CF settings.\n'
        'Windows + .NET Framework + controlador oficial / official Line 6 driver.\n'
        'https://github.com/conr4d86/Catalyst-CF-Editor\n'
    )
    with zipfile.ZipFile(windows, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        entry(z, 'CatalystCFEditor.exe', exe)
        entry(z, 'README.txt', readme.encode('utf-8'))
        entry(z, 'recovery/Catalyst-OFICIAL-recuperacion.ctf', official)
        for name, data in sources:
            if name in ('README.md', 'README.es.md', 'LICENSE', 'NOTICE') or name.startswith(('docs/', 'models/', 'assets/')):
                entry(z, name, data)
    inventory = [{'path': name, 'sha256': digest(data), 'bytes': len(data)} for name, data in sources]
    with zipfile.ZipFile(source, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for name, data in sources:
            entry(z, name, data)
        entry(z, 'SOURCE-SHA256.json', json.dumps(inventory, indent=2, ensure_ascii=False).encode('utf-8'))
    artifacts = []
    for path in (out / 'CatalystCFEditor.exe', windows, source):
        data = path.read_bytes()
        artifacts.append({'name': path.name, 'bytes': len(data), 'sha256': digest(data)})
    manifest = {
        'schema': 2, 'application': 'Catalyst CF Editor', 'version': a.version, 'host_revision': 'r106',
        'repository': 'https://github.com/conr4d86/Catalyst-CF-Editor',
        'publication_status': a.publication_status, 'source_commit': a.commit, 'artifacts': artifacts,
        'source_inventory_sha256': digest(inventory_bytes), 'curated_source_files': len(curated['source_files']),
        'initial_install_enabled_models': ['Catalyst 60', 'Catalyst CX60'],
        '100_200_initial_install_enabled': False,
        'NAM': {'positions': 2, 'architecture': 'A2 Lite', 'sample_rate': 48000,
                'default_input_db': -5, 'input_adjustable': True, 'default_output_db': -3, 'output_automatic': False},
        'IR': {'positions': 3, 'sample_rate': 48000, 'samples': 2048, 'demo_cuts_hz': [80, 8000],
               'max_source_bytes': 64 * 1024 * 1024, 'formats': ['WAV', 'AIFF', 'AIFC', 'FLAC']},
        'engine': ENGINE, 'firmware_segment_sha256': ENGINE['segment_sha256'],
        'official_recovery_sha256': digest(official),
        'upgrade': {'when_app_requires_update': 'Reuse only a verified local preset archive for the exact installed edition; no repeated preset scan',
                    'missing_or_changed_archive': 'Stop before installation connection',
                    'reinstall_resources': 'New edition with two demo NAMs and three demo IRs active; no automatic migration',
                    'native_preset_backup_includes_NAM_IR_CF_sidecars': False},
        'validation': {'offline_groups_passed': 13, 'offline_group_names': sorted(GROUPS),
                       'offline_suite_sha256': digest(suite_bytes), 'offline_hardware_accessed': False,
                       'corrected_engine_transfer_and_normal_boot_confirmed': True,
                       'read_only_runtime_diagnostics_clean': True,
                       'physical_CH_A_B_preset_change': a.physical_preset_check,
                       'GUI_A_B_A_preset_change': a.gui_preset_check,
                       'preamp_save_and_cold_boot_persistence': 'pending',
                       'physical_scope': 'One development Catalyst 60/CX60; no device identity included',
                       'all_models_hardware_matrix_complete': False, 'unsigned_executable': True},
        'source_files': len(sources), 'device_access_during_packaging': False, 'published_by_packaging': False,
    }
    manifest_path = out / 'RELEASE-MANIFEST.json'
    manifest_path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    sums = [row['sha256'] + '  ' + row['name'] for row in artifacts]
    sums.append(digest(manifest_path.read_bytes()) + '  RELEASE-MANIFEST.json')
    (out / 'SHA256SUMS.txt').write_text('\n'.join(sums) + '\n', encoding='ascii')
    print(json.dumps({'passed': True, 'publication_status': a.publication_status,
                      'physical_CH_A_B_preset_change': a.physical_preset_check,
                      'source_files': len(sources), 'artifacts': artifacts}, indent=2))


if __name__ == '__main__':
    main()
