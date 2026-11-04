"""Original source snapshot for the unchanged embedded legacy components.

The main editor compiles the current IRImport.cs. Its decoder can evolve without
rebuilding a vendor USB/updater component. Legacy validation still checks the
exact original source bytes and the exact previously reviewed DLL hashes.
"""
from pathlib import Path
import hashlib

LEGACY_IR_HASH = '4b3707583bfb90005a243485aa4e9717a04d4a10deff89653cb0c1a9f62b65f9'

def legacy_input(root, name, expected=None):
    root = Path(root)
    if name == 'IRImport.cs' and (expected is None or expected == LEGACY_IR_HASH):
        snapshot = root / 'legacy-pinned-sources' / name
        assert hashlib.sha256(snapshot.read_bytes()).hexdigest() == LEGACY_IR_HASH, 'Legacy IR source snapshot changed'
        return snapshot
    return root / name
