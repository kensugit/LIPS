"""Package the isolated, verified CSV release for guarded Dell-side preparation."""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import zipfile

root = Path(__file__).resolve().parents[1]
stage = root / 'artifacts/search-csv-production'
bundle = stage / 'bundle'
source = stage / 'source'
assert (bundle / 'publish/CatalogSearch.Web.dll').is_file()
assert not subprocess.check_output(['git', '-C', str(source), 'status', '--porcelain']).strip()
commit = subprocess.check_output(['git', '-C', str(source), 'rev-parse', 'HEAD'], text=True).strip()
previous = root / 'artifacts/natural-search-patterns/bundle'
package = '6ab3f8d96b4563d9a32432bfb13b41c76d001f8c159ac708cfa09f5f04d5f820'
web = 'sha256:e16969fe25c1b74c86947610f74a3d5847fd3c10da4c1e12374f5e6ff0714db0'
script = (previous / 'prepare.sh').read_text(encoding='utf-8')
script = script.replace('33459fd747b89872af1622fc1ba8f61de3fad9947e7dc1c6f13e34b65f9ee30c', package)
script = script.replace('sha256:cbfa10b30124e407baf7c58bfdfe1b3d780b8e1ab81752c0a458e99066c66b95', web)
script = script.replace('lips-natural-base:cbfa10b30124', 'lips-csv-base:e16969fe25c1')
script = script.replace('lips-natural-search:', 'lips-search-csv:').replace('lips-natural-preview-', 'lips-csv-preview-')
(bundle / 'prepare.sh').write_text(script, encoding='utf-8', newline='\n')
(bundle / 'Dockerfile').write_text('FROM lips-csv-base:e16969fe25c1\nCOPY publish/ /app/web/\n', encoding='utf-8', newline='\n')
shutil.copy2(previous / 'schema.sha256', bundle / 'schema.sha256')
shutil.copy2(root / 'tools/verify-search-csv-candidate.py', bundle / 'verify-production.py')
shutil.copy2(stage / 'release.patch', bundle / 'release.patch')
for name in ['verify-search-csv.cjs', 'verify-search-csv-live.cjs']:
    shutil.copy2(root / 'tests' / name, bundle / name)
shutil.copy2(root / 'artifacts/search-csv/live-proof.json', bundle / 'local-download-proof.json')
(bundle / 'source-commit.txt').write_text(commit + '\n', encoding='utf-8', newline='\n')
subprocess.run(['git', '-C', str(source), 'archive', '--format=tar.gz', '-o', str(bundle / 'source.tar.gz'), 'HEAD'], check=True)
proof = dict(sourceCommit=commit, expectedCurrentPackage=package, expectedWebImage=web,
             expectedState='Last recorded deployment; prepare.sh must verify current state before building.',
             testsPassed=140, testsSkipped=5, browserCsvChecks='passed', localReadOnlyComparisonQueries=6,
             actualDownloadedRows=2000, linuxContainerTest='pending on Dell', productionApplied=False,
             productionHttpHealth='ok', productionObservedProducts=7526, sshStatus='TCP22222 timed out twice',
             scope='SupplierName projection and CSV/search UI. Existing parsers, controllers, migrations and DB image preserved.')
(bundle / 'proof.json').write_text(json.dumps(proof, ensure_ascii=False, indent=2), encoding='utf-8')
(bundle / 'README.txt').write_text('''LIPS 取引先表示・検索結果CSV / Dell準備用

本番適用のユーザー指示は取得済みです。このZIP自体はdeploy用package.tarではありません。
本番Webの切替前にDellのUbuntuでイメージ作成・検証・管理者登録が必要です。

1. DellでこのZIPを展開し、Ubuntuで展開先へ移動します。
2. sudo bash prepare.sh
3. 検証が成功したら、verification.jsonと表示SHAを確認して、表示された
   sudo /usr/local/sbin/catalogctl approve <SHA256>
   を実行します。
4. package/package.sha256の値をCodexへ返してください。
   専用SSHが復旧していればCodexが承認済み版をdeployし、公開画面を検証します。

prepare.shは既知の本番パッケージ6ab3f8d9、Web e16969fe、既存DBイメージと
schema指紋を固定照合します。最新SSH状態は接続タイムアウトで未確認です。
本番が更新されていれば停止します。ガード値を手動で書き換えず、差分再確認が必要です。
プレビューのDB接続はread-only、監視OFF。準備スクリプトは本番を切り替えません。
本番deploy時は既存catalogctlがバックアップ・schema確認・異常時Web復旧を担当します。
DB構造、データ、取り込み処理、認証、SSH制限は変更しません。
''', encoding='utf-8', newline='\n')
files = sorted(p for p in bundle.rglob('*') if p.is_file() and p.name != 'SHA256SUMS')
(bundle / 'SHA256SUMS').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.relative_to(bundle).as_posix()+'\n' for p in files), encoding='utf-8', newline='\n')
archive = root / 'artifacts/LIPS-search-csv-Dell-20260922.zip'
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
    for p in sorted(bundle.rglob('*')):
        if p.is_file(): z.write(p, 'LIPS-search-csv-Dell/' + p.relative_to(bundle).as_posix())
result = dict(path=str(archive), sha256=hashlib.sha256(archive.read_bytes()).hexdigest(), bytes=archive.stat().st_size, sourceCommit=commit, productionApplied=False)
(stage / 'package-proof.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result))
