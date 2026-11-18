# LibreOffice native renderer experiment

This is an opt-in backend connected to `render.py` only by explicit Linux environment configuration; this change does not enable any Web instance.
Native Linux execution is required before claiming this backend works. Source
is opened through Calc; no extracted values are used to redraw a workbook.
Initial scope rejects every formula instead of claiming cached formula fidelity.
This conservative restriction must be disclosed for real source acceptance.

## 2026-10-12 native verification

LIQUOR-BASE received the four approved files with matching size/SHA256.
Native verification found that hiding a non-selected sheet still exported its
page in Calc. The backend now removes non-selected sheets from the unsaved
private document; the preserved XLSX is never saved. Formula workbooks remain
rejected before launch. Candidate backend SHA256:
`13051ec3e441bfdde202a3b804364512f04fab4f09859e7b674100754cf60f6c`.

Remote synthetic acceptance passed: one selected page, expected text, no other
sheet text, highlight pixel difference, unchanged original SHA, Japanese and
numeric-format visual check, no leftover soffice process. Remote guards used a
standard-library fixture harness because openpyxl was unavailable there; local
bundled GuardTests also passed (2 tests). Proof is in
`/home/openai/liquor-work/lips-linux-render-20261012/output/lips-backend-2997555-candidate-proof-001`.
This proves the standalone backend only, not Web integration or real-document
acceptance. Remaining native gates below are still required.

Dependencies: LibreOffice Calc, a Python interpreter able to `import uno`, and
the outer Python's pypdfium2, Pillow, openpyxl (fixture only). Paths may point to
the isolated unprivileged deb extraction. No installation or network listener is
performed by these scripts. UNO uses a unique local named pipe and user profile.

Run from this directory with the already prepared runtimes:

```sh
OUTER_PYTHON test_libreoffice_backend.py
OUTER_PYTHON test_libreoffice_backend.py --accept /absolute/new/isolated-proof-dir --soffice /absolute/isolated/soffice --uno-python /absolute/uno-python
```

Use a fresh output directory. Inspect `output/base.png`, `output/highlight.png`
and `proof.json`. Check native text, formatting, Japanese glyphs, numeric format,
row 6 highlight, and that the second sheet never appears. Automated checks do not
replace visual review. The original SHA must remain unchanged.

Additional native gates before integration: hidden row/sheet, merged cells across
target boundaries, zero-height columns, page breaks, protected files, font
substitution, timeout cleanup, concurrent independent process, drawing objects,
and the existing active-content rejection tests. The Windows renderer remains
unchanged. No claim of Excel-identical pixel output is made.

Official API references checked:
- https://api.libreoffice.org/docs/idl/ref/interfacecom_1_1sun_1_1star_1_1sheet_1_1XPrintAreas.html
- https://api.libreoffice.org/docs/idl/ref/servicecom_1_1sun_1_1star_1_1sheet_1_1SheetCellRange.html
- https://api.libreoffice.org/docs/idl/ref/servicecom_1_1sun_1_1star_1_1sheet_1_1TablePageStyle.html
- https://api.libreoffice.org/docs/idl/ref/servicecom_1_1sun_1_1star_1_1document_1_1MediaDescriptor.html
- https://api.libreoffice.org/docs/idl/ref/namespacecom_1_1sun_1_1star_1_1document_1_1MacroExecMode.html

`ReadOnly=true`, `MacroExecutionMode=0`, `UpdateDocMode=0` are set before load;
automatic calculation is disabled after load, but formula sources are already
rejected because that alone does not prove no import-time recalculation.
Only the wrapper's newly created worker process group is terminated on timeout.

Boundary follow-up: backend SHA256
2a385f8cef3df52cd7cc93a830504621aec7c4ad91a5dd4e94209069dd6d4d40
matches the remote v3 candidate. Reacquire ranges after deleting preceding
sheets. Protected/non-ZIP containers, drawings, manual page breaks, hidden/zero
width columns and merges crossing the target row are rejected before launch.
Remote native tests passed second-sheet selection and adjacent merges in two
parallel processes, plus timeout cleanup preserving an unrelated sentinel.
Use the isolated soffice binary with this backend's unique profile. A wrapper
that injects a fixed UserInstallation caused concurrency failures; it was not
modified. Real encrypted Office input remains untested (non-ZIP stand-in only).
Standalone proof: output/lips-backend-2997555-boundary-003/boundary-proof.json
on LIQUOR-BASE. Web integration remains pending.
# render.py 統合（隔離実証待ち）

Linux では明示設定時だけ `render.py` がこの backend を呼びます。Windows Excel と PDFium の既存分岐は維持します。環境変数の設定自体はこの実装では実施していません。

- `LIPS_ORIGINAL_XLSX_BACKEND=libreoffice`
- `LIPS_ORIGINAL_SOFFICE`: 専用 LibreOffice 実行ファイルの絶対パス
- `LIPS_ORIGINAL_UNO_PYTHON`: UNO が利用可能な専用 Python の絶対パス

既存 `OriginalRender:Python` は PDFium/Pillow を持つ外側 Python の設定です。UNO Python と同一である必要はありません。専用 prefix の依存環境は検証済みの起動環境から継承させてください。新しい権限・ネットワーク公開は不要です。

外側 `render.py source out --mode info|render --sheet N --row N` の引数・JSON ファイル名は不変です。Linux `info` の notice は LibreOffice と制約を明記します。`render` は native backend の width/height/pages/page/lines/sheet/row/firstRow/lastRow/highlightBox/sourceHash/notice を返します。Windows の crop フィールドは従来どおりで、Linux は全ページ画像とその座標です。

専用 worker の制限は 60 秒（Web の既存 85 秒以内に終了処理と PDFium の余裕を確保）。数式等の拒否と原本 SHA 不変を維持し、失敗時に保存値による代替描画や自動再試行をしません。ローカル mock 分岐 9 項目合格。実 Linux `render.py`→backend→画像 JSON の統合はリモート担当の検証が必要であり、この時点で完了扱いしません。

