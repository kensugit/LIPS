// Also interprets older persisted receipts: deployment does not rewrite their history.
window.importGuidance = result => {
  const r = result || {}, message = r.message || '取込結果を確認できません。';
  if (r.remedy) return {cause:message, remedy:r.remedy};
  if (r.stopQueue) return {cause:message, remedy:'保存結果が不明です。原典・操作履歴で保存結果を照合するまで再送・再開しないでください。'};
  if (r.alreadyImported) return {cause:message, remedy:'同じ原本は再登録せずスキップしました。追加操作は不要です。'};
  if (r.status === 'Success') return {cause:message, remedy:''};
  if (r.questionKind === 'OrveauxNewArrivals') return {cause:message, remedy:'保存済みの確定商品は保持しています。保留行の原典で、生産者・商品名・年・容量・タイプと既存商品の対応を確認してください。新着の容量は推測せず保留します。'};
  if (r.questionKind === 'Date') return {cause:message, remedy:'原典の発行日・在庫基準日を確認して回答してください。不明な日付は推測せず保留してください。'};
  if (r.questionKind === 'Mapping' || r.questionKind === 'Rows') return {cause:message, remedy:'原典の対象行と質問を確認し、回答を保存してから処理を再開してください。'};
  if (r.status === 'Unsupported' && message.includes('xlsx・標準CSV以外は未対応')) return {cause:'前回の処理ではPDFが未対応のため停止しました。', remedy:'「この原典を再読取りして取込」で、保存済み原本を現在の読取り処理で再判定してください。対応外の帳票の場合は、その原因と対応方法を表示します。'};
  if (r.status === 'Unsupported') return {cause:message, remedy:'この形式・帳票は回答メモだけでは取り込めません。対応するExcel・PDFまたは標準CSVを用意するか、原本を添えて読取り対応を依頼してください。'};
  if (/取引先フォルダー/.test(message)) return {cause:message, remedy:'「取引先別」を親として、その下に取引先名、その下に資料を置いてください。未対応の取引先名は管理担当者へ確認してください。'};
  if (/20MB|空ファイル/.test(message)) return {cause:message, remedy:'空でない20MB以下のファイルを選択してください。'};
  if (/同日別内容|新しい資料/.test(message)) return {cause:message, remedy:'登録済み資料と今回の基準日・内容を比較してください。重複を回避するためだけに日付を変えないでください。'};
  return {cause:message, remedy:'原典の列・必須項目・値と取込履歴を確認してください。解消しない場合は原本と表示された原因を添えて読取り対応を依頼してください。'};
};
