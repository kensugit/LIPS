"""Create a Web-only payload from a clean source, publish directory and live inspect JSON."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tarfile
import tempfile

def schema_fingerprint(source):
    """Match deployment/build-release.sh: recursive, sorted, exact-byte SHA256."""
    paths=[]
    for name in ('src/CatalogSearch.Infrastructure/Migrations','DB/Migrations'):
        directory=source/name
        if not directory.is_dir() or directory.is_symlink():
            raise ValueError('Missing or linked migration directory: '+name)
        for file in directory.rglob('*'):
            if file.is_symlink():raise ValueError('Migration links prohibited')
            if file.is_file():paths.append(file)
    if not paths:raise ValueError('Empty migration inventory')
    lines=[]
    for file in sorted(paths,key=lambda f:f.relative_to(source).as_posix().encode('utf-8')):
        name=file.relative_to(source).as_posix()
        if any(c in name for c in ('\\','\n','\r')):raise ValueError('Unsupported migration filename')
        lines.append(hashlib.sha256(file.read_bytes()).hexdigest()+'  '+name+'\n')
    return hashlib.sha256(''.join(lines).encode('utf-8')).hexdigest()

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source',type=Path,required=True,help='Git root containing catalog-search-poc')
    p.add_argument('--publish',type=Path,required=True)
    p.add_argument('--state',type=Path,required=True,help='Saved client.py inspect JSON')
    p.add_argument('--output',type=Path,required=True)
    a=p.parse_args()
    if subprocess.check_output(['git','-C',str(a.source),'status','--porcelain']).strip():raise ValueError('Commit and verify source before packaging')
    commit=subprocess.check_output(['git','-C',str(a.source),'rev-parse','HEAD'],text=True).strip()
    schema=schema_fingerprint(a.source/'catalog-search-poc')
    state=json.loads(a.state.read_text(encoding='utf-8-sig'))
    if schema!=state['schemaSha256']:raise ValueError('Schema changed: this route does not apply migrations')
    files=sorted(f for f in a.publish.rglob('*') if f.is_file())
    manifest=dict(version=1,expectedPackage=state['currentPackage'],expectedWebImage=state['webImage'],expectedDbImage=state['dbImage'],schemaSha256=schema,sourceCommit=commit,files={'publish/'+f.relative_to(a.publish).as_posix():hashlib.sha256(f.read_bytes()).hexdigest() for f in files})
    a.output.parent.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory() as d:
        m=Path(d)/'manifest.json';m.write_text(json.dumps(manifest),encoding='utf-8')
        with tarfile.open(a.output,'w',format=tarfile.USTAR_FORMAT) as t:
            t.add(m,arcname='manifest.json')
            for f in files:t.add(f,arcname='publish/'+f.relative_to(a.publish).as_posix())
    print(json.dumps({'payload':str(a.output),'sha256':hashlib.sha256(a.output.read_bytes()).hexdigest(),'sourceCommit':commit,'schemaSha256':schema}))

if __name__=='__main__':main()
