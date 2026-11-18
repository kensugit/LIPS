# ローカルのリリース調整

Python標準ライブラリのみ。展開やネットワーク操作はしません。全担当が同じ既存の絶対パスを `--root` に指定する協調予約です。他PC・非参加タスクへの強制排他、サーバー側inspect・承認・本番ガードの代替ではありません。

```powershell
python tools/release-coordination/coordinate.py reserve --root C:\local-release-state --input reserve.json
python tools/release-coordination/coordinate.py inspect --root C:\local-release-state
python tools/release-coordination/coordinate.py transition --root C:\local-release-state --input phase.json
python tools/release-coordination/coordinate.py handoffvalidate --input handoff.json
python tools/release-coordination/coordinate.py resolve --root C:\local-release-state --input resolve.json
python tools/release-coordination/coordinate.py release --root C:\local-release-state --input release.json
```

`reserve.json` は `session`, `operation`, `expected`, `observed`, `phase` を持ちます。expected/observedはともに `package/source/web/db/schema` の5文字列で、最新の実inspectと一致させます。phaseは `receiver/deploy/active-sidecar`。任意の `ttl_seconds` は警告用で、期限を過ぎても予約を自動取得しません。

`phase.json` は `session/operation/observed/phase/outcome/evidence`。`confirmed` は工程開始前の旧expectedとの一致確認専用です。`unknown` は異なるobservedも記録でき、旧expectedを保持します。受信・適用結果が不明ならunknownを記録し、自動再送しません。unknownのままrelease、またはtransition confirmedへの書換えは禁止です。

不明結果が判明した場合は `resolve.json` に `session/operation/data/observed/evidence` を指定します。data/observedは実配備最新版のhandoff入力と同じです。同じ所有者・unknown状態・解消証拠が必須で、clean sourceと実配備identityを再検証します。成功時は旧expectedを保持したまま `resolvedconfirmed` と実observed、handoff全体・SHA256・解消証拠を履歴へ保存し、release可能になります。手順は **reserve旧版 → unknown新版 → resolve実新版handoffと解消証拠 → release**。旧版observedを偽装して解除しないでください。resolveの再送は拒否します。

正常な展開後の手順は **旧版reserve → 外部の正式展開 → 新実版handoffvalidate → cleanup証拠を添えてrelease** です。新版inspectを旧版confirmedへ渡さないでください。

`handoff.json` は `data` と `observed`。dataには `expectedInspect/sourcePath/sourceCommit/package/nativeIdentity/schemaIdentity/runtimeFeatures/webOnly/verificationEvidence/cleanupEvidence` が必要です。sourcePathはclean Gitルート絶対パス。sourceCommit/package/nativeIdentity/schemaIdentityはそれぞれ実observed.source/package/web/schemaと一致必須です。**実配備済みの最新版引継ぎ専用で、未配備候補の引継ぎは未対応です。** runtimeFeaturesは明示boolean辞書で `aiSidecarActive` 必須。trueとwebOnly=trueの組合せを拒否します。実機から自動取得しないため、提供するinspect・flags・証拠の真正性は別途確認が必要です。

`release.json` は `session/operation/evidence`。所有者のみ解放可能で履歴は残します。I/O失敗やクラッシュで `mutation.lock` が残ったら、状態・履歴を調査してください。自動解除・削除・takeoverコマンドはありません。書込み前の2回目読取で予約競合を検出しただけの場合は、今回取得したmutation lockのみ解除します。

```powershell
python -B -m unittest discover -s tools/release-coordination -v
```
