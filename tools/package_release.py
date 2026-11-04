"""Package a reviewed executable and curated source tree; never opens a device."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import zipfile


def digest(data):
    return hashlib.sha256(data).hexdigest()


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--root', type=Path, required=True)
    p.add_argument('--executable', type=Path, required=True)
    p.add_argument('--official-recovery', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--version', default='1.0.0')
    p.add_argument('--commit', default=None)
    a = p.parse_args()
    root, out = a.root.resolve(), a.output.resolve()
    if out == root or root in out.parents:
        raise ValueError('Release output must be outside the source tree')
    exe, official = a.executable.read_bytes(), a.official_recovery.read_bytes()
    if digest(exe) != '096841c174e846d4db7b6ee640fde3380cddf452909a73c9b77a1043583acea8':
        raise ValueError('Executable differs from the reviewed v1.0 artifact')
    if digest(official) != 'f9fe2482e561bef8a04128a38af89cadee4f8d06071b06a9053e36c206471ae8':
        raise ValueError('Official recovery firmware differs')
    sources = []
    banned_dirs = {'.git', '__pycache__', 'node_modules', 'backups', 'hardware-trials'}
    banned_ext = {'.exe', '.dll', '.bin', '.ctf', '.hxf', '.dfu', '.nam', '.wav', '.catl6a', '.log', '.cfj', '.lock', '.pyc'}
    secrets = re.compile(rb'(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[A-Z0-9]{16})')
    private_prefixes = [str(Path.home()).lower().encode(), Path.home().as_posix().lower().encode()]
    for path in sorted(root.rglob('*')):
        relative = path.relative_to(root)
        if any(part in banned_dirs for part in relative.parts):
            continue
        if not path.is_file():
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
        'Catalyst CF Editor ' + a.version + '\n\n'
        'ES: Abre CatalystCFEditor.exe. Lee docs/INICIO_RAPIDO.md y docs/RECUPERACION.md.\n'
        'EN: Open CatalystCFEditor.exe. Read docs/QUICKSTART.md and docs/RECUPERACION.md.\n\n'
        'Primera instalacion / First installation: FIRMWARE CF.\n'
        'Despues / Afterwards: CAMBIAR NAM / IR / CHANGE NAM / IR.\n'
        'La reinstalacion repone los ejemplos / Reinstall restores the examples.\n'
        'Windows + .NET Framework + controlador oficial / official Line 6 driver.\n'
        'https://github.com/conr4d86/Catalyst-CF-Editor\n'
    )
    with zipfile.ZipFile(windows, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        z.writestr('CatalystCFEditor.exe', exe)
        z.writestr('README.txt', readme)
        z.writestr('recovery/Catalyst-OFICIAL-recuperacion.ctf', official)
        for name, data in sources:
            if name in ('README.md', 'README.es.md', 'LICENSE', 'NOTICE') or name.startswith(('docs/', 'models/', 'assets/')):
                z.writestr(name, data)
    inventory = [{'path': name, 'sha256': digest(data), 'bytes': len(data)} for name, data in sources]
    with zipfile.ZipFile(source, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for name, data in sources:
            z.writestr(name, data)
        z.writestr('SOURCE-SHA256.json', json.dumps(inventory, indent=2, ensure_ascii=False))
    artifacts = []
    for path in (out / 'CatalystCFEditor.exe', windows, source):
        data = path.read_bytes()
        artifacts.append({'name': path.name, 'bytes': len(data), 'sha256': digest(data)})
    manifest = {
        'schema': 1, 'application': 'Catalyst CF Editor', 'version': a.version,
        'repository': 'https://github.com/conr4d86/Catalyst-CF-Editor',
        'source_commit': a.commit, 'artifacts': artifacts,
        'initial_install_enabled_models': ['Catalyst 60', 'Catalyst CX60'],
        '100_200_initial_install_enabled': False,
        'NAM': {'positions': 2, 'architecture': 'A2 Lite', 'sample_rate': 48000,
                'default_input_db': -5, 'input_adjustable': True, 'default_output_db': -3, 'output_automatic': False},
        'IR': {'positions': 3, 'sample_rate': 48000, 'samples': 2048, 'demo_cuts_hz': [80, 8000]},
        'firmware_segment_sha256': '4a81169b0cee9b8ed384936fad14161b8fd42169c5a5dcc317337d9dec15bf34',
        'official_recovery_sha256': digest(official),
        'validation': {'offline_groups_passed': 11, 'hardware_read_only_connection_passed': True,
                       'all_models_hardware_matrix_complete': False, 'unsigned_executable': True},
        'source_files': len(sources), 'device_access_during_packaging': False,
    }
    manifest_path = out / 'RELEASE-MANIFEST.json'
    manifest_path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    sums = [row['sha256'] + '  ' + row['name'] for row in artifacts]
    sums.append(digest(manifest_path.read_bytes()) + '  RELEASE-MANIFEST.json')
    (out / 'SHA256SUMS.txt').write_text('\n'.join(sums) + '\n', encoding='ascii')
    print(json.dumps({'passed': True, 'source_files': len(sources), 'artifacts': artifacts}, indent=2))


if __name__ == '__main__':
    main()
