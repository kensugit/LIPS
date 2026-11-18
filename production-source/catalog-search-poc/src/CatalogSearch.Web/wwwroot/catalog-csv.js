// Pure CSV serialization: callers supply the complete, already sorted result set.
const CatalogCsv = (() => {
 const states = ['不明', '在庫あり', '少量', '在庫なし', '入荷待ち'];
 const columns = [
  ['取引先', row => row.supplierName],
  ['仕入先SKU', row => row.codeKind==='InternalIdentity'?null:(row.originalSupplierProductCode??row.sku)],
  ['商品名', row => row.productNameJa],
  ['商品名（欧文）', row => row.productNameEn],
  ['生産者', row => row.producerNameJa],
  ['生産者（欧文）', row => row.producerNameEn],
  ['国', row => row.country],
  ['産地', row => row.region],
  ['タイプ', row => row.productType],
  ['年号', row => row.vintageLabel ?? row.vintageRaw],
  ['容量(ml)', row => row.volumeMl || null],
  ['希望小売価格', row => row.referenceRetailPrice],
  ['税区分', row => row.taxIncluded ? '税込' : '税抜'],
  ['特価仕入価格（税抜）', row => row.specialPurchasePrice],
  ['在庫状態', row => row.abundantInventory ? '潤沢' : (states[row.inventory] || '不明')],
  ['在庫数量(本)', row => row.quantity],
  ['価格・規格の注意', row => row.priceNote],
  ['取込元シート', row => row.sourceSheet],
  ['取込元行', row => row.sourceRow],
  ['取込元ID', row => row.sourceDocumentId]
 ];

 function cell(value) {
  let text = value == null ? '' : String(value);
  // Neutralize formulas in supplier text before escaping CSV quotes.
  if (typeof value === 'string' && (/^[\s\uFEFF]*[=+\-@]/u.test(text) || /^[\t\r\n]/.test(text))) {
   text = "'" + text;
  }
  return '"' + text.replace(/"/g, '""') + '"';
 }

 const record = values => values.map(cell).join(',');
 const header = record(columns.map(([label]) => label));

 function serialize(rows) {
  const records = rows.map(row => record(columns.map(([, read]) => read(row))));
  return '\uFEFF' + [header, ...records].join('\r\n') + '\r\n';
 }

 return {serialize};
})();
if (typeof module !== 'undefined') module.exports = CatalogCsv;
