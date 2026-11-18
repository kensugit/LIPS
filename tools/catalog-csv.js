// Export the displayed result set before pagination, preserving the current sort.
const CatalogCsv=(()=>{
 const states=['不明','在庫あり','少量','在庫なし','入荷待ち'];
 const columns=[['取引先',r=>r.supplierName],['仕入先SKU',r=>r.sku],['商品名',r=>r.productNameJa],['商品名（欧文）',r=>r.productNameEn],['生産者',r=>r.producerNameJa],['生産者（欧文）',r=>r.producerNameEn],['国',r=>r.country],['産地',r=>r.region],['タイプ',r=>r.productType],['年号',r=>r.vintageRaw],['容量(ml)',r=>r.volumeMl||null],['希望小売価格',r=>r.referenceRetailPrice],['税区分',r=>r.taxIncluded?'税込':'税抜'],['在庫状態',r=>states[r.inventory]||'不明'],['在庫数量(本)',r=>r.quantity],['価格・規格の注意',r=>r.priceNote],['取込元シート',r=>r.sourceSheet],['取込元行',r=>r.sourceRow],['取込元ID',r=>r.sourceDocumentId]];
 function cell(value){
  let text=value==null?'':String(value);
  // Quote CSV delimiters and neutralize spreadsheet formulas in supplier text.
  if(typeof value==='string'&&(/^[\s\uFEFF]*[=+\-@]/u.test(text)||/^[\t\r\n]/.test(text)))text="'"+text;
  return '"'+text.replace(/"/g,'""')+'"';
 }
 function serialize(rows){return '\uFEFF'+[columns.map(([label])=>cell(label)).join(','),...rows.map(row=>columns.map(([,read])=>cell(read(row))).join(','))].join('\r\n')+'\r\n'}
 return {serialize};
})();
if(typeof module!=='undefined')module.exports=CatalogCsv;
