"""Package verified data + Web payload, with fixed-hash mutual TLS receiver registration."""
from pathlib import Path
import hashlib,json,shutil,subprocess,zipfile

root=Path(__file__).resolve().parents[1]
work=root/'artifacts/fines-special-20260924';stage=work/'package';stage.mkdir(exist_ok=True)
state=json.loads((work/'live-packaging.json').read_text('utf-8-sig'))
assert state==json.loads((work/'live-before.json').read_text('utf-8-sig')),'Production changed; rebase and verify'
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
data=stage/'data';data.mkdir(exist_ok=True)
for label,name in [('main','特価リスト260924 (2).xlsx'),('extra','追加特価 (1).xlsx')]:
    shutil.copy2(root/'catalogs/fines-special-20260924'/name,data/(label+'.xlsx'))
    shutil.copy2(root/f'catalogs/fines-special-20260924/{label}.catalog.json',data/(label+'.catalog.json'))
shutil.copytree(work/'runtime',stage/'runtime',dirs_exist_ok=True)
shutil.copy2(work/'web-payload.tar',stage/'web-payload.tar')
payload_sha=digest(stage/'web-payload.tar')
script=(root/'tools/Import-Dell-NipponLiquor.sh').read_text('utf-8')
script=script.replace('sha256:e16969fe25c1b74c86947610f74a3d5847fd3c10da4c1e12374f5e6ff0714db0',state['webImage'])
script=script.replace('bordeaux jadot retail shipping','main extra').replace('891 Nippon Liquor','331 Fines special').replace('Nippon Liquor 891','Fines special 331').replace('nippon-liquor','fines-special-20260924')
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
[[ $(id -u) == 0 ]] || { echo 'Run as root on Dell Ubuntu'; exit 1; }
mode=${1:---check}
[[ $# -le 1 && ( $mode == --check || $mode == --apply ) ]] || exit 2
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
sha256sum --check --quiet SHA256SUMS
root=$(pwd -P)
image=$(docker inspect --format '{{.Image}}' catalog-web-1)
[[ $image == EXPECTED_IMAGE ]] || { echo 'Production image changed; do not bypass the guard'; exit 1; }
stage=$(mktemp -d /var/tmp/lips-fines-special-20260924.XXXXXX)
chmod 700 "$stage"
cp -a data runtime import.sh SHA256SUMS web-payload.tar "$stage/"
identity=$(docker run --rm --network none --read-only --cap-drop ALL --security-opt no-new-privileges --entrypoint /bin/sh "$image" -c 'printf "%s:%s" "$(id -u)" "$(id -g)"')
[[ $identity =~ ^[0-9]+:[0-9]+$ ]] || exit 2
chown -R -- "$identity" "$stage"
find "$stage" -type d -exec chmod 500 {} +
find "$stage" -type f -exec chmod 400 {} +
bash "$stage/import.sh" --check
if [[ $mode == --check ]]; then echo 'Preflight complete. No data import or Web deployment.'; exit 0; fi
sha=PAYLOAD_SHA
incoming=/home/catalogdeploy/incoming/$sha.tar
if [[ -e $incoming || -L $incoming ]]; then
  [[ -f $incoming && ! -L $incoming ]] || exit 2
  printf '%s  %s\n' "$sha" "$incoming" | sha256sum --check --quiet
else
  install -o catalogdeploy -g catalogdeploy -m 0600 "$root/web-payload.tar" "$incoming"
  printf '%s  %s\n' "$sha" "$incoming" | sha256sum --check --quiet
fi
# Existing fixed builder performs read-only preview and root-protected registration.
# Prepare completes before data import, so preview failure cannot update stock.
/usr/local/sbin/lips-codex-prepare "$sha" | tee "$root/web-prepare.log"
package=$(python3 -c 'import json,sys; j=json.load(open(sys.argv[1])); assert j["prepared"] and j["payloadSha256"]==sys.argv[2]; print(j["packageSha256"])' "$root/web-prepare.log" "$sha")
[[ $package =~ ^[0-9a-f]{64}$ ]] || exit 2
bash "$stage/import.sh" --apply
/usr/local/sbin/lips-codex-deploy "$package"
/usr/local/sbin/lips-codex-prepare inspect
curl --fail --max-time 15 'http://127.0.0.1:55440/api/search?text=ALDMR21A'
printf '\nFines special 331 imported; Web deployed. Confirm the public API with Codex.\n'
'''.replace('EXPECTED_IMAGE',state['webImage']).replace('PAYLOAD_SHA',payload_sha)
(stage/'apply.sh').write_text(apply,encoding='utf-8',newline='\n')
# SHA256SUMS is also checked inside the Linux import stage; it covers just those files.
files=sorted(p for p in stage.rglob('*') if p.is_file() and p.relative_to(stage).parts[0] in ('runtime','data','import.sh','web-payload.tar'))
(stage/'SHA256SUMS').write_text(''.join(digest(p)+'  '+p.relative_to(stage).as_posix()+'\n' for p in files),encoding='utf-8',newline='\n')
evidence=stage/'verification';evidence.mkdir(exist_ok=True)
for name in ('local-db-proof.json','local-api-ui-proof.json','live-packaging.json'):
    shutil.copy2(work/name,evidence/name)
source=stage/'source';source.mkdir(exist_ok=True)
for path in (root/'tools/fines_special_import.py',root/'tests/test_fines_special.py',root/'tools/patches/fines-special-price.patch',work/'bridge-source/Program.cs',work/'bridge-source/CatalogPdfBridge.csproj'):
    shutil.copy2(path,source/path.name)
readme='''# ファインズ特価 本番反映

全リスト322商品と追加特価9商品を登録し、既存参考上代は保持、通常在庫を更新。重複3商品は追加優先。オープンブル除外。特価は別列で表示しCSVにも保持。DBスキーマは変更しません。

Dellの既存受信先に転送済みZIPのサイズ・SHA256が一致してからApply-FinesSpecialOnDell.ps1を実行。既定は事前確認のみ。-Applyで、旧本番版一致検査、read-only Webプレビュー、DBバックアップと可読性検証、2原本の登録と全件照合、標準LIPS Web展開を順に実行します。

失敗時はログとバックアップを保持。自動再試行やガード解除をしないでください。原本単位でトランザクションが分かれ、2番目の原本で失敗した場合は先の322商品の変更が残る場合があります。同じ原本の再登録は重複しません。Web更新失敗時は標準展開の復旧処理に従います。WebロールバックはDB在庫を戻しません。データを戻す場合はバックアップを別DBに復元し、後続更新を確認してから管理者が判断してください。

prepare、Dockerプレビュー、本番転送、本番適用の実機結果は未確認です。全て成功した後にCodexから331商品の特価・在庫・参考上代・原表表示を再検証します。
'''
(stage/'README.md').write_text(readme,encoding='utf-8')
package_id='LIPS-FinesSpecial-20260924'
package=work/(package_id+'.zip')
with zipfile.ZipFile(package,'w',zipfile.ZIP_DEFLATED) as z:
    for p in sorted(stage.rglob('*')):
        if p.is_file():z.write(p,p.relative_to(stage).as_posix())
proof=dict(packageId=package_id,file=package.name,length=package.stat().st_size,sha256=digest(package).upper(),
    sourceCommit=subprocess.check_output(['git','-C',str(work/'source'),'rev-parse','HEAD'],text=True).strip(),
    products=331,schemaChanged=False,stockUpdated=True,specialPriceSeparate=True,baseline=state,
    payloadSha256=payload_sha,localVerified=True,transferred=False,productionApplied=False)
proof_path=work/(package_id+'.proof.json');proof_path.write_text(json.dumps(proof,indent=2),encoding='utf-8')
kit=work/'receiver-kit';kit.mkdir(exist_ok=True)
engine=Path('C:/Users/kensu/OpenAI/Codex/iWSET/iWSET/本番一括転送/quotation-toolbar-bulk-20260925/BulkTransfer.cs')
assert digest(engine).upper()=='6FC34FEF08282788FAB8A0941A92BA5C90886FB44609C952331A0A82715D0F65'
shutil.copy2(engine,kit/'BulkTransfer.cs')
manifest=dict(packageId=package_id,destination='C:\\ProgramData\\LIPS-Codex\\Inbox\\'+package_id,
    files=[dict(name=p.name,kind=kind,length=p.stat().st_size,sha256=digest(p).upper()) for p,kind in [(package,'zip'),(proof_path,'proof')]])
mp=kit/'approved-transfer.json';mp.write_text(json.dumps(manifest,indent=2),encoding='utf-8')
for old,new in [('Receive-AmzBulk.ps1','Receive-FinesSpecialBulk.ps1'),('Send-AmzBulk.ps1','Send-FinesSpecialBulk.ps1'),('Apply-AmzOnDell.ps1','Apply-FinesSpecialOnDell.ps1')]:
    text=(root/'tools'/old).read_text('utf-8').replace('LIPS-AMZ-20260925',package_id).replace('AMZ Dell','Fines special Dell')
    text=text.replace('MANIFEST_HASH',digest(mp).upper()).replace('PACKAGE_LENGTH',str(proof['length'])).replace('PACKAGE_HASH',proof['sha256'])
    text=text.replace('10.10.10.0/24','10.10.10.1')
    (kit/new).write_text(text,encoding='utf-8-sig',newline='\r\n')
(kit/'README.md').write_text('''# Dell受信準備（本番適用とは別）

今回のZIP/proof専用の固定ファイル名・サイズ・SHA256を登録します。既存相互TLS証明書を使用し、既存iWSET JEAや配置登録を変更しません。
Dell管理者PowerShellで展開フォルダーへ移動し実行:

```powershell
.\\Receive-FinesSpecialBulk.ps1 -Receive -ApproveTemporaryFirewall
```

一時的にTCP5987を192.168.1.110/10.10.10.1だけに許可します。受信対象は今回のZIP/proofのみ、アプリ・DB変更なし。転送終了/失敗時に一時規則を削除します。ポート使用中なら所有者を停止せず確認してください。待受は120秒です。実行後Codexへ「受信開始」と連絡してください。

受信完了後、Codexの転送検証報告を確認し、同フォルダーで事前検査と手動適用:

```powershell
.\\Apply-FinesSpecialOnDell.ps1
.\\Apply-FinesSpecialOnDell.ps1 -Apply
```

事前検査が失敗したら-Applyを実行しないでください。手動適用はバックアップ・DB更新・Web更新を含みます。結果はCodexへ返してください。受信準備自体はこの適用を実行しません。
''',encoding='utf-8-sig')
kit_zip=work/(package_id+'-ReceiverKit.zip')
with zipfile.ZipFile(kit_zip,'w',zipfile.ZIP_DEFLATED) as z:
    for p in sorted(kit.iterdir()):z.write(p,p.name)
print(json.dumps(proof));print(kit_zip)
