# 自由文検索の類似パターン検証とv5

## 本番展開完了（2026-09-21）

以下の準備記録の後、Dell管理者がv5の正確なSHAをapprove。ユーザーの指示に従い専用deployで正常適用し、本番で検証済み。

- パッケージ: `6ab3f8d96b4563d9a32432bfb13b41c76d001f8c159ac708cfa09f5f04d5f820`
- Webイメージ: `sha256:e16969fe25c1b74c86947610f74a3d5847fd3c10da4c1e12374f5e6ff0714db0`
- schema-check: ready=true、pending=0、unknown=0。
- 適用前バックアップ: `/var/lib/catalog-deploy/backups/20260921T091822-525556.dump`。
- statusで新パッケージ・Web/DB healthy確認。DBコンテナ/イメージは維持。
- 本番公開URLで類似表現/API/Edge45項目と既存30項目、計75項目成功。登録4623件を維持。
- 本番の結果: `ブルゴーニュの白で15000円以内` 252件、`イタリアの赤ワインで2500円から4000円` 29件、`容量1500mlの３万円以下のシャンパーニュ` 12件。
- 証跡: artifacts/natural-search-patterns/production-deployment.json、production-pattern-verification.json、production-regression-verification.json。画面画像も同フォルダーへ保存。

v5の本番反映は完了。旧版を前提とするprepare.shの再実行は不要。以下は準備時点の履歴。

ユーザー指示: 似た表現を検証し、未対応を修正して本番へ展開する。会話上の展開承認は取得済み。現行本番は33459fd7（初版）、Web/DBとも正常。v5の反映は、DellでのLinuxイメージ作成・実データ検証・管理者approveが必要。

## 発見と修正

最初の同義表現30ケースで18件失敗を再現し、修正後は全件成功。

- `イタリア産の赤で2,500-4,000円`、`国はイタリア、タイプは赤、価格は2500円から4000円`
- `二千五百円から四千円`、`2千500円から4千円`、`1万5千円以下`、`一万五千円以下`
- `ブルゴーニュ産の白`、`産地はブルゴーニュ、白、予算15000円`、`予算は1.5万円`
- `容量は1500mlのみ`、`容量:1500ml`、`1.5リットル`
- `在庫のある`、`在庫有りのみ`、`在庫があるもの`
- `○円で探してください`、`○円でお願いします`
- `○円未満` / `○円より安い` は上限を含まない。`○円超` / `○円より高い` は下限を含まない。SQLで厳密に比較し、1円差への丸めはしない。
- `泡` / `スパークリングワイン`、色と発泡の組み合わせ。発泡属性は登録タイプの「泡」または「スパークリング」を使う。

価格帯の上下限・詳細欄との積集合・矛盾時エラーを維持。商品名中の白/赤やリテラルの%/_を壊さない回帰テストを維持。主要産地は元の検索索引のキーワードとして照合し、サブ地域の独自推測はしない。

## 検証済み

- 自動140件成功、専用DB/原本依存5件skip。自然文関連110件は全件成功。語順×区切りの24組を含む。
- 新しい実データ/API/Edge45項目と既存30項目、計75項目成功。
- 同義表現32件について、手動の数値/国/タイプ検索を基準に全商品キーを比較。厳密な上限/下限では境界に一致する実商品が除外されることを確認。
- Dell用の同じ32表現の照合スクリプトもローカルで成功。本番データに対するLinuxコンテナ検証はprepare.shで行う。
- 例: ローカルDBではイタリア赤2500〜4000円29件、ブルゴーニュ白15000円以下550件、1500mlシャンパーニュ30000円以下12件。本番DBと件数が違うため、本番受入はその場で手動条件との一致を確認する。

検証スクリプト: tests/verify-natural-search-patterns.cjs、tests/verify-natural-search.cjs、tools/verify-natural-search-production.py。共有する表現データ: tests/natural-search-cases.json。証跡: artifacts/natural-search-patterns/verification.json、regression-verification.json、dell-verifier-local.json。

## 配布

`artifacts/LIPS-natural-search-Dell-v5.zip` がv3/v4を置き換える統合候補。現在の本番パッケージ33459fd7・Webイメージcbfa10b3に限定し、既存DBイメージ/スキーマ/compose/CLIを維持する。DB/原本更新なし。全ファイルSHAと元GitのLFによるschema指紋ab882479を照合。

DellでZIP展開 → `sudo bash prepare.sh` → 検証成功後の表示SHAを管理者が `catalogctl approve` → SHAをCodexへ返して専用deployを実行する。ビルド元ソース/検証結果はZIP内source.tar.gzとproof.jsonで確認可能。prepare.shは本番を切り替えない。

現時点でLinuxコンテナのv5検証と本番展開は未実施。このPCにDocker/WSLディストリビューションがなく、専用SSHではビルド/approveが許可されないため、Dell管理者の操作が必要。サーバーの制限は変更しない。

## 対応範囲

一般的な意味検索・味わいの推論は行わず、未抽出の語はキーワードAND検索。価格は税抜希望小売。税込・仕入価格、OR/除外条件はエラーにする。容量の「マグナム」「ハーフ」からmlを推測しない。任意の日本語文すべてに対応したとは扱わない。
