// Sort all fetched matches before pagination. Missing values stay last in either direction.
const CatalogSort=(()=>{
 const collator=new Intl.Collator('ja',{numeric:true,sensitivity:'base'});
 const stockRank={3:0,2:1,1:2,4:3};
 const values={supplier:r=>r.supplierName,product:r=>r.productNameJa||r.productNameEn,producer:r=>r.producerNameJa||r.producerNameEn,country:r=>r.country,region:r=>r.region,vintage:r=>r.vintageLabel??r.vintageRaw,volume:r=>r.volumeMl,price:r=>r.referenceRetailPrice,stock:r=>stockRank[r.inventory]};
 const missing=v=>v==null||v==='';
 function compareValue(a,b,direction){if(missing(a)||missing(b))return missing(a)===missing(b)?0:missing(a)?1:-1;return (typeof a==='number'&&typeof b==='number'?a-b:collator.compare(String(a),String(b)))*direction}
 function sort(rows,key,direction){if(!values[key])return [...rows];return [...rows].sort((a,b)=>compareValue(values[key](a),values[key](b),direction)||(key==='stock'?compareValue(a.quantity,b.quantity,direction):0)||collator.compare(a.sku,b.sku)||collator.compare(a.sourceDocumentId||'',b.sourceDocumentId||''))}
 return {sort};
})();
if(typeof module!=='undefined')module.exports=CatalogSort;
