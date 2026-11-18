"""Allowlist source + generated synthetic fixtures; never walk input/data/runtime trees."""
import hashlib,json,subprocess,sys,zipfile
from pathlib import Path
root=Path(__file__).resolve().parents[2]
output=Path(sys.argv[1]).resolve()
files=[]
for folder in ['src','tests']:
    for p in (root/folder).rglob('*'):
        relative=p.relative_to(root)
        if not p.is_file() or any(part in ('bin','obj','.git') for part in relative.parts):continue
        if p.suffix in ('.cs','.csproj','.html','.js','.css'):
            files.append(p)
for name in ['Directory.Build.props','Directory.Packages.props','CatalogSearch.slnx','DB/Migrations/20261012010000_IntakeReview.sql']:
    files.append(root/name)
for name in ['render.py','excel-render.ps1']:
    files.append(root/'tools/original-render'/name)
for name in ['BASE-DATE-PROPOSAL-CONTRACT.md','IMAGE-ZOOM-REVIEW.md','ROW-RESUME-REVIEW.md','ROW-INTAKE-RELEASE-GATES.md']:
    files.append(root/'docs'/name)
for p in (root/'tools/cloud-review').rglob('*'):
    if p.is_file() and '__pycache__' not in p.parts and p.suffix in ('.py','.sh','.md','.json','.csv','.xlsx','.pdf'):files.append(p)
commit=subprocess.check_output(['git','-C',str(root),'rev-parse','HEAD'],text=True).strip()
manifest={'sourceCommit':commit,'scope':'source and synthetic fixtures only','files':[]}
with zipfile.ZipFile(output,'x',zipfile.ZIP_DEFLATED) as z:
    for p in sorted(set(files)):
        data=p.read_bytes();name=p.relative_to(root).as_posix()
        z.writestr(name,data);manifest['files'].append({'path':name,'sha256':hashlib.sha256(data).hexdigest(),'bytes':len(data)})
    # No host-specific connection strings; runtime requires explicit dedicated environment.
    z.writestr('src/CatalogSearch.Web/appsettings.json','{"AI":{"Provider":"Disabled"},"Shudoraku":{"Mode":"Fixture"}}')
    z.writestr('manifest.json',json.dumps(manifest,ensure_ascii=False,indent=2))
print(json.dumps({'file':output.name,'commit':commit,'bytes':output.stat().st_size,'sha256':hashlib.sha256(output.read_bytes()).hexdigest(),'files':len(files)+2}))
