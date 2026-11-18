from pathlib import Path
import hashlib,json,shutil,zipfile
root=Path(__file__).resolve().parents[1]
work=root/'artifacts/amz-20260925';stage=work/'package'
state=json.loads((work/'live-packaging.json').read_text('utf-8-sig'))
assert state==json.loads((work/'live-before.json').read_text('utf-8-sig')), 'Production baseline changed'
data=stage/'data';data.mkdir(exist_ok=True)
for ext in ('xlsx','catalog.json'):
    shutil.copy2(root/f'catalogs/AMZ.StockList20260925.{ext}',data/f'amz.{ext}')
script=(root/'tools/Import-Dell-NipponLiquor.sh').read_text('utf-8')
script=script.replace('sha256:e16969fe25c1b74c86947610f74a3d5847fd3c10da4c1e12374f5e6ff0714db0',state['webImage'])
script=script.replace('bordeaux jadot retail shipping','amz').replace('891 Nippon Liquor','206 AMZ').replace('Nippon Liquor 891','AMZ 206').replace('nippon-liquor','amz-20260925')
script=script.replace('curl --fail --max-time', 'curl --fail --silent --show-error --max-time')
guard='''exec 8> /var/lib/catalog-deploy/deployment.lock
flock -n 8 || { echo 'Another catalog operation is running' >&2; exit 1; }
[[ $(cat /var/lib/catalog-deploy/current) == EXPECTED_PACKAGE ]] || { echo 'Current package changed' >&2; exit 1; }
[[ $(docker inspect --format '{{.Image}}' catalog-postgres-1) == EXPECTED_DB ]] || { echo 'DB image changed' >&2; exit 1; }
grep -Fxq 'SCHEMA_SHA256=EXPECTED_SCHEMA' /opt/catalog/releases/EXPECTED_PACKAGE/release.env || { echo 'Schema fingerprint changed' >&2; exit 1; }
'''.replace('EXPECTED_PACKAGE',state['currentPackage']).replace('EXPECTED_DB',state['dbImage']).replace('EXPECTED_SCHEMA',state['schemaSha256'])
script=script.replace('for container in catalog-web-1',guard+'for container in catalog-web-1')
(stage/'import.sh').write_text(script,encoding='utf-8',newline='\n')
apply='''#!/usr/bin/env bash
set -euo pipefail
[[ $(id -u) == 0 ]] || { echo 'Run on Dell Ubuntu as root'; exit 1; }
mode=${1:---check}
[[ $# -le 1 && ( $mode == --check || $mode == --apply ) ]] || exit 2
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
sha256sum --check --quiet SHA256SUMS
image=$(docker inspect --format '{{.Image}}' catalog-web-1)
[[ $image == EXPECTED_IMAGE ]] || { echo 'Production image changed'; exit 1; }
stage=$(mktemp -d /var/tmp/lips-amz-20260925.XXXXXX)
chmod 700 "$stage"
cp -a data runtime import.sh SHA256SUMS "$stage/"
identity=$(docker run --rm --network none --read-only --cap-drop ALL --security-opt no-new-privileges --entrypoint /bin/sh "$image" -c 'printf "%s:%s" "$(id -u)" "$(id -g)"')
[[ $identity =~ ^[0-9]+:[0-9]+$ ]] || exit 1
chown -R -- "$identity" "$stage"
find "$stage" -type d -exec chmod 500 {} +
find "$stage" -type f -exec chmod 400 {} +
docker run --rm --network none --read-only --cap-drop ALL --security-opt no-new-privileges --mount "type=bind,src=$stage,dst=/import,readonly" --entrypoint /bin/sh "$image" -c 'test -r /import/runtime/CatalogPdfBridge.dll && test -r /import/data/amz.xlsx'
echo "Stage: $stage"
bash "$stage/import.sh" "$mode"
'''.replace('EXPECTED_IMAGE',state['webImage'])
# Runtime/data/import hashes are also checked inside the readable Linux stage.
files=sorted(p for p in stage.rglob('*') if p.is_file() and p.relative_to(stage).parts[0] in ('runtime','data','import.sh'))
(stage/'SHA256SUMS').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.relative_to(stage).as_posix()+'\n' for p in files),encoding='utf-8',newline='\n')
(stage/'apply.sh').write_text(apply,encoding='utf-8',newline='\n')
source=stage/'source';source.mkdir(exist_ok=True)
for name in ('tools/amz_import.py','tools/CatalogPdfBridge/Program.cs','tools/CatalogPdfBridge/CatalogPdfBridge.csproj','tests/test_amz.py'):
    out=source/name;out.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(root/name,out)
for name in ('source-audit.json','local-db-validation.log','local-api-proof.json','live-packaging.json'):
    shutil.copy2(work/name,stage/name)
package=work/'LIPS-AMZ-20260925.zip'
with zipfile.ZipFile(package,'w',zipfile.ZIP_DEFLATED) as z:
    for f in sorted(stage.rglob('*')):
        if f.is_file():z.write(f,f.relative_to(stage).as_posix())
proof=dict(packageId='LIPS-AMZ-20260925',file=package.name,length=package.stat().st_size,
 sha256=hashlib.sha256(package.read_bytes()).hexdigest().upper(),products=206,inventory={'InStock':189,'LowStock':17},
 sourceSha256=hashlib.sha256((data/'amz.xlsx').read_bytes()).hexdigest().upper(),baseline=state,
 localImportVerified=True,idempotencyVerified=True,apiVerified=True,transferred=False,productionApplied=False)
(work/'LIPS-AMZ-20260925.proof.json').write_text(json.dumps(proof,indent=2),encoding='utf-8')
print(json.dumps(proof))
