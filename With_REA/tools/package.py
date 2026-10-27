#!/usr/bin/env python3
"""Package only this fresh PoC, excluding runtime test data and original inputs."""
import hashlib
from pathlib import Path
import zipfile

root = Path(__file__).resolve().parents[1]
package = root / 'StickyMemo-PoC.zip'
files = sorted(p for p in root.rglob('*') if p.is_file()
               and p != package and p.name != 'SHA256SUMS.txt'
               and 'test-runs' not in p.relative_to(root).parts
               and '__pycache__' not in p.parts)
manifest = ''.join(f'{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.relative_to(root).as_posix()}\n'
                   for p in files)
(root / 'SHA256SUMS.txt').write_text(manifest, encoding='utf-8')
with zipfile.ZipFile(package, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for path in files + [root / 'SHA256SUMS.txt']:
        archive.write(path, 'StickyMemo-PoC/' + path.relative_to(root).as_posix())
with zipfile.ZipFile(package) as archive:
    assert archive.testzip() is None
    for line in manifest.splitlines():
        digest, path = line.split('  ', 1)
        assert hashlib.sha256(archive.read('StickyMemo-PoC/' + path)).hexdigest() == digest
print(f'{package.name}: {package.stat().st_size} bytes, {len(files) + 1} files, CRC/SHA-256 verified')
