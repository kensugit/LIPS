#!/usr/bin/python3 -I
"""Root-owned, fixed Web-only builder. Never execute uploaded scripts or Dockerfiles."""
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import subprocess
import sys
import tarfile
import tempfile
import time
from urllib.request import urlopen

STATE = Path('/var/lib/catalog-deploy')
INCOMING = Path('/home/catalogdeploy/incoming')
LIMIT = 256 * 1024**2

def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for chunk in iter(lambda: f.read(1024**2), b''): h.update(chunk)
    return h.hexdigest()

def unpack(archive, destination):
    with tarfile.open(archive, 'r:') as tar:
        members = tar.getmembers()
        names = [m.name for m in members]
        if not 2 <= len(names) <= 1000 or len(set(names)) != len(names): raise ValueError('Invalid member count/duplicates')
        if sum(m.size for m in members) > LIMIT: raise ValueError('Payload too large')
        for m in members:
            parts = PurePosixPath(m.name).parts
            if not m.isfile() or m.size < 0 or m.size > 80*1024**2: raise ValueError('Regular bounded files only')
            if not re.fullmatch(r'[A-Za-z0-9_./-]+', m.name) or any(p in ('.','..') for p in m.name.split('/')) or m.name.startswith('/'): raise ValueError('Invalid path')
            if m.name != 'manifest.json' and not (len(parts) >= 2 and parts[0] == 'publish'): raise ValueError('Unexpected path')
        if 'manifest.json' not in names: raise ValueError('Missing manifest')
        manifest_member = tar.getmember('manifest.json')
        if manifest_member.size > 200000: raise ValueError('Manifest too large')
        manifest = json.load(tar.extractfile(manifest_member))
        fields = {'version','expectedPackage','expectedWebImage','expectedDbImage','schemaSha256','sourceCommit','files'}
        if set(manifest) != fields or manifest['version'] != 1: raise ValueError('Manifest schema')
        for key in ('expectedPackage','schemaSha256'):
            if not re.fullmatch('[a-f0-9]{64}', str(manifest[key])): raise ValueError(key)
        for key in ('expectedWebImage','expectedDbImage'):
            if not re.fullmatch('sha256:[a-f0-9]{64}', str(manifest[key])): raise ValueError(key)
        if not re.fullmatch('[a-f0-9]{40}', str(manifest['sourceCommit'])): raise ValueError('sourceCommit')
        if not isinstance(manifest['files'], dict) or set(manifest['files']) != set(names)-{'manifest.json'}: raise ValueError('File manifest mismatch')
        required={'publish/CatalogSearch.Web.dll','publish/CatalogSearch.Web.deps.json','publish/CatalogSearch.Web.runtimeconfig.json','publish/wwwroot/index.html'}
        if not required <= set(names): raise ValueError('Missing Web files')
        for m in members:
            target = destination / m.name
            target.parent.mkdir(parents=True, exist_ok=True)
            with tar.extractfile(m) as src, target.open('xb') as dst: shutil.copyfileobj(src, dst)
            target.chmod(0o644)
            if m.name != 'manifest.json' and digest(target) != manifest['files'][m.name]: raise ValueError('File hash mismatch')
        for directory in (destination/'publish').rglob('*'):
            if directory.is_dir(): directory.chmod(0o755)
        (destination/'publish').chmod(0o755)
    return manifest

def run(*args):
    return subprocess.check_output(args, text=True, stderr=sys.stderr).strip()

def get(port, path):
    with urlopen(f'http://127.0.0.1:{port}{path}', timeout=10) as response: return json.load(response)

def main(sha):
    import fcntl
    if os.geteuid() != 0 or (sha!='inspect' and not re.fullmatch('[a-f0-9]{64}', sha)): raise ValueError('Root and SHA256 required')
    os.umask(0o077)
    with (STATE/'deployment.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        if sha=='inspect':
            current=(STATE/'current').read_text().strip()
            if not re.fullmatch('[a-f0-9]{64}',current): raise ValueError('Invalid current package')
            values=dict(line.split('=',1) for line in (Path('/opt/catalog/releases')/current/'release.env').read_text().splitlines())
            print(json.dumps({'currentPackage':current, 'webImage':values['CATALOG_IMAGE'],'dbImage':values['POSTGRES_IMAGE'],'schemaSha256':values['SCHEMA_SHA256'],'sourceCommit':values['CATALOG_COMMIT'],'webState':run('docker','inspect','--format','{{.Image}} {{.State.Health.Status}}','catalog-web-1'),'dbState':run('docker','inspect','--format','{{.Image}} {{.State.Health.Status}}','catalog-postgres-1')}));return
        cache=STATE/'codex-prepared'/f'{sha}.json'
        if cache.exists():
            proof=json.loads(cache.read_text())
            approved=STATE/'approved'/(proof['packageSha256']+'.tar')
            current=(STATE/'current').read_text().strip()
            if current not in (proof['expectedPackage'],proof['packageSha256']): raise ValueError('Stale prepared payload')
            if not approved.is_file() or digest(approved)!=proof['packageSha256']: raise ValueError('Prepared package integrity')
            print(json.dumps(proof)); return
        workbase=STATE/'codex-builds';workbase.mkdir(mode=0o700,exist_ok=True)
        with tempfile.TemporaryDirectory(prefix=sha[:12]+'-',dir=workbase) as folder:
            work=Path(folder); incoming=INCOMING/(sha+'.tar')
            fd=os.open(incoming, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
            if not stat.S_ISREG(os.fstat(fd).st_mode):
                os.close(fd);raise ValueError('Regular upload required')
            with os.fdopen(fd,'rb') as src, (work/'payload.tar').open('xb') as dst:
                count=0
                while chunk:=src.read(1024**2):
                    count+=len(chunk)
                    if count>LIMIT: raise ValueError('Upload too large')
                    dst.write(chunk)
            if digest(work/'payload.tar')!=sha: raise ValueError('Upload hash mismatch')
            payload=work/'payload';payload.mkdir()
            m=unpack(work/'payload.tar',payload)
            if (STATE/'current').read_text().strip()!=m['expectedPackage']: raise ValueError('Production changed; rebuild against current release')
            release=Path('/opt/catalog/releases')/m['expectedPackage']
            values=dict(line.split('=',1) for line in (release/'release.env').read_text().splitlines())
            for key, field in [('CATALOG_IMAGE','expectedWebImage'),('POSTGRES_IMAGE','expectedDbImage'),('SCHEMA_SHA256','schemaSha256')]:
                if values[key]!=m[field]: raise ValueError('Production '+key+' differs')
            for container, field in [('catalog-web-1','expectedWebImage'),('catalog-postgres-1','expectedDbImage')]:
                if run('docker','inspect','--format','{{.Image}} {{.State.Health.Status}}',container)!=m[field]+' healthy': raise ValueError(container+' not expected/healthy')
            if run('docker','info','--format','{{.OSType}}/{{.Architecture}}')!='linux/x86_64': raise ValueError('Linux amd64 required')
            # Preserve the base runtime, entrypoint, CLI, health policy and DB image.
            tag='lips-codex-base:'+sha[:16]; candidate='lips-codex-web:'+sha
            run('docker','tag',m['expectedWebImage'],tag)
            (payload/'Dockerfile').write_text(f'FROM {tag}\nCOPY publish/ /app/web/\n')
            run('docker','build','--pull=false','--network=none','-t',candidate,str(payload))
            image=run('docker','image','inspect','--format','{{.Id}}',candidate)
            if run('ss','-ltnH','sport = :55443'): raise ValueError('Preview port occupied')
            flags=['--network','host','--read-only','--cap-drop','ALL','--security-opt','no-new-privileges','--mount','type=bind,src=/etc/catalog/secrets/db_password,dst=/run/secrets/db_password,readonly']
            run('docker','run','--rm',*flags,candidate,'schema-check')
            preview='lips-codex-preview-'+sha[:16]
            # Fixed command, never from the upload. Password is read inside the container.
            command='set -eu; password=$(cat /run/secrets/db_password); case "$password" in ""|*[!a-f0-9]*) exit 2;; esac; export ConnectionStrings__CatalogSearch="Host=127.0.0.1;Port=55439;Database=catalog_search;Username=catalog;Password=$password;Options=-c default_transaction_read_only=on"; unset password; exec dotnet /app/web/CatalogSearch.Web.dll --urls http://127.0.0.1:55443'
            run('docker','run','-d','--name',preview,*flags,'--tmpfs','/tmp:rw,noexec,nosuid,size=64m','-e','CatalogDemo__Enabled=true','-e','CatalogDemo__WatchEnabled=false','--entrypoint','/bin/sh',candidate,'-c',command)
            try:
                ready=False
                for _ in range(30):
                    try:
                        if get(55443,'/health')['status']=='ok': ready=True; break
                    except (OSError, ValueError): pass
                    time.sleep(1)
                if not ready: raise ValueError('Preview did not start')
                options=get(55440,'/api/options')
                if get(55443,'/api/options')!=options: raise ValueError('Candidate options/count mismatch')
                results=get(55443,'/api/search?volumeMl=750&priceMax=6000&available=true')
                if 'rows' not in results: raise ValueError('Search response missing rows')
            finally: run('docker','rm','-f',preview)
            out=work/'package';out.mkdir()
            run('docker','save','-o',str(out/'images.tar'),candidate)
            shutil.copyfile(release/'compose.yaml',out/'compose.yaml')
            (out/'release.env').write_text(f'CATALOG_IMAGE={image}\nPOSTGRES_IMAGE={m["expectedDbImage"]}\nCATALOG_COMMIT={m["sourceCommit"]}\nSCHEMA_SHA256={m["schemaSha256"]}\n')
            run('docker','compose','--env-file',str(out/'release.env'),'-f',str(out/'compose.yaml'),'config','--quiet')
            archive=work/'package.tar'
            with tarfile.open(archive,'w',format=tarfile.USTAR_FORMAT) as tar:
                for name in ('images.tar','compose.yaml','release.env'): tar.add(out/name,arcname=name,recursive=False)
            resultsha=digest(archive)
            # Root-owned registration is granted only for this fixed builder's verified output.
            approved=STATE/'approved'/(resultsha+'.tar')
            temp=STATE/'approved'/('.codex-'+sha+'.partial')
            with archive.open('rb') as src, temp.open('wb') as dst: shutil.copyfileobj(src,dst)
            if digest(temp)!=resultsha: raise ValueError('Registered hash mismatch')
            os.replace(temp,approved)
            proof={'payloadSha256':sha,'packageSha256':resultsha,'expectedPackage':m['expectedPackage'],'webImage':image,'sourceCommit':m['sourceCommit'],'products':options['total'],'prepared':True,'deployed':False}
            cache.parent.mkdir(mode=0o700,exist_ok=True)
            cache.write_text(json.dumps(proof,indent=2))
            print(json.dumps(proof))

if __name__=='__main__':
    try:
        if len(sys.argv)!=2: raise ValueError('Usage: lips-codex-prepare SHA256')
        main(sys.argv[1])
    except (ValueError,OSError,subprocess.CalledProcessError) as error:
        print(str(error),file=sys.stderr);sys.exit(1)
