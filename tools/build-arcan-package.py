"""Package the locally verified Arcan source and production-compatible bridge."""
from pathlib import Path
import hashlib
import json
import shutil
import tarfile

root = Path(__file__).resolve().parents[1]
stage = root / 'artifacts/arcan-20260921'
assert (stage / 'runtime/CatalogPdfBridge.dll').is_file(), 'Build the compatible bridge first'
(stage / 'data').mkdir(exist_ok=True)
for extension in ('pdf', 'catalog.json'):
    shutil.copy2(root / ('catalogs/アルカン.ボルドー.202609.' + extension), stage / ('data/arcan.' + extension))
shutil.copy2(root / 'tools/Import-Dell-Arcan.sh', stage / 'import.sh')
for name in ['tools/arcan_pdf.py', 'tools/pdf_import.py', 'tools/catalog_export.py',
             'tools/supplier_pdfs.py', 'tools/daiei_pdf.py', 'tools/diony_pdf.py',
             'tools/CatalogPdfBridge/Program.cs', 'tools/CatalogPdfBridge/CatalogPdfBridge.csproj',
             'tests/test_arcan.py', 'tests/verify-arcan-search.cjs', 'requirements.txt']:
    destination = stage / 'source' / name
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(root / name, destination)
files = sorted(p for p in stage.rglob('*') if p.is_file() and p.name != 'SHA256SUMS')
(stage / 'SHA256SUMS').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest() + '  ' + p.relative_to(stage).as_posix() + '\n' for p in files), encoding='utf-8', newline='\n')
package = root / 'artifacts/LIPS-Arcan-20260921.tar'
with tarfile.open(package, 'w', format=tarfile.USTAR_FORMAT) as tar:
    for p in sorted(stage.rglob('*')):
        if p.is_file():
            info = tar.gettarinfo(str(p), p.relative_to(stage).as_posix())
            info.uid = info.gid = 0
            info.uname = info.gname = ''
            info.mode = 0o644
            with p.open('rb') as stream:
                tar.addfile(info, stream)
sha = hashlib.sha256(package.read_bytes()).hexdigest()
script = (root / 'tools/Apply-Dell-Daiei-20260921.sh').read_text(encoding='utf-8')
script = script.replace('f17c680e00dc1c37bf1e8a70cbc02e03fe10b0cd005411b5df057001628c12a0', sha).replace('daiei', 'arcan').replace('list-german-beer', 'arcan')
for destination in ('tools/Apply-Dell-Arcan-20260921.sh', 'artifacts/Apply-Arcan-20260921.sh'):
    (root / destination).write_text(script, encoding='utf-8', newline='\n')
proof = dict(package=str(package), sha256=sha, bytes=package.stat().st_size,
             products=164, productionApplied=False, sourceSha256=hashlib.sha256((stage / 'data/arcan.pdf').read_bytes()).hexdigest())
(root / 'artifacts/arcan-package.json').write_text(json.dumps(proof, indent=2), encoding='utf-8')
print(json.dumps(proof))
