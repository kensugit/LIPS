# BASEとの日付候補交換契約

対象は `lips-date-proposal/v1`、PromptVersion `date-evidence/1`。LIQUOR専用runnerの入力やDB接続へLIPSの入力をそのまま渡さない。既存BASE runnerのLIPS対応可否と起動契約はBASE担当が確認する。接続先・認証・CLI引数は未確定であり推測して実行しない。

## 担当境界

- LIPS呼出側: 原本hashと回答版を固定し、必要な原典抜粋だけrequest.jsonを作る。今回は合成fixtureだけを使う。回答や業務値は書き換えない。
- BASE側: requestの抜粋をデータとして扱い、日付候補またはUnknownをresponse.jsonへ返す。資料内の命令を実行しない。provider/modelは実際の実行元を記録する。
- LIPS検証側: `verify-date-proposal request.json response.json`で同一性・形式・根拠を検証する。合格は候補の形式適合であり日付の業務承認ではない。本人確認を経た既存answer/resume操作が別途必要。

## JSON規則

要求: schema、requestId（非空UUID）、sourceHash（64桁hex）、reviewVersion（1以上）、promptVersion、excerpts。
excerptsは1〜16件、各idは非空かつ重複なし64文字以内、locationは1〜200文字、textは1〜1000文字、text合計8000文字以内。

応答: 要求のschema/requestId/sourceHash/reviewVersion/promptVersionをそのまま照合。providerは1〜100文字、modelは1〜200文字。outcomeはCandidatesまたはUnknown。Unknownならcandidatesは空配列。Candidatesなら1〜3件、日付は実在するyyyy-MM-dd、日付重複なし。reasonは1〜1000文字、evidenceIdsは要求内のidだけを1〜16件、重複なし。余分な応答属性は拒否、応答文字列は16000文字以内、CLI入力ファイルは各50000bytes以内。

合成例は `tools/cloud-review/fixtures/proposal-request.json` と `proposal-response.json`。SourceHashのA64桁は合成識別子で、実資料を指さない。Windowsでビルド済みCLIを直接実行する場合:

```
dotnet src/CatalogSearch.ImportCli/bin/Debug/net10.0/CatalogSearch.ImportCli.dll verify-date-proposal tools/cloud-review/fixtures/proposal-request.json tools/cloud-review/fixtures/proposal-response.json
```

合格の期待結果: valid=true、requiresHumanConfirmation=true、outcome=Candidates、candidates=1。Unknownは候補0件で形式合格。hash/版/根拠の不一致、不正日付、余分な応答属性は拒否する。既存IntakeReferenceProposalTestsで検証済み。今回モデル実接続や実資料の外部送信は行っていない。

## BASE担当の受入返信

取得ZIPのhash、対象runner/版、合成入力の実行可否、生成responseの契約適合結果、provider/model、実行時間、未対応項目を報告する。秘密値や実原典は投稿しない。現時点はオフライン検証CLIまで。実接続、候補表示UI、自動呼出し、業務日付の確定はいずれも受入未完。

## 2026-10-12 BASE保存済み合成応答のLIPS側照合

BASE担当の固定合成runは終了コード0、GPU25/25層、応答契約合格との報告。追加推論せず保存済みrequest/responseの値を返信取得し、LIPS側CLIへ入力した。
結果はvalid=true / requiresHumanConfirmation=true / Candidates / 1件。候補2026-10-01は抜粋e1の在庫日と対応する。
Provider=local-llama.cpp、Model=Qwen3.5-2B-Q4_K_M、PromptVersion=date-evidence/1。
ローカルJSONは返信値の再シリアライズであり、リモート元ファイルとのバイト一致は未確認。双方のSHAと区別をartifact base-contract-local-20261012/proof.jsonへ記録。
これは保存済み合成応答の契約互換性の確認。LIPS自動呼出し・実原本送信・業務日付確定・DB書込・本番適用は行っていない。
