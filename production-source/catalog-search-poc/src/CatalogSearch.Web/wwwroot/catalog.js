const $=id=>document.getElementById(id);let data=[],page=0,sortKey=null,sortDirection=1;const size=50;let searchPending=false;
const money=n=>n==null?'金額記載なし／要確認':new Intl.NumberFormat('ja-JP').format(n)+'円';
const selectedProducts=new Map();
const supplierNames=new Map();
const productKey=r=>r.supplierProductId;
const sourceSku=r=>r.codeKind==='InternalIdentity'?'':(r.originalSupplierProductCode??r.sku);
const codeLabel=r=>r.codeKind==='InternalIdentity'?'仕入先SKU記載なし（内部IDで管理）':'SKU '+sourceSku(r);
const selectedRows=()=>CatalogSort.sort([...selectedProducts.values()],sortKey,sortDirection);
const states=['不明','在庫あり','少量','在庫なし','入荷待ち'];
function el(tag,text,cls){const e=document.createElement(tag);if(text!==undefined)e.textContent=text;if(cls)e.className=cls;return e}
function error(message){$('error').textContent=message;$('error').hidden=!message}
async function json(url){const res=await fetch(url);if(!res.ok){let message='読み込みに失敗しました。もう一度お試しください。';try{message=(await res.json()).error||message}catch{}throw new Error(message)}return res.json()}
function applySort(){data=CatalogSort.sort(data,sortKey,sortDirection)}
document.querySelectorAll('[data-sort]').forEach(button=>button.addEventListener('click',()=>{
 sortDirection=sortKey===button.dataset.sort?-sortDirection:1;sortKey=button.dataset.sort;page=0;applySort();
 document.querySelectorAll('[data-sort]').forEach(b=>{const selected=b.dataset.sort===sortKey;b.closest('th').setAttribute('aria-sort',selected?(sortDirection===1?'ascending':'descending'):'none');b.querySelector('span').textContent=selected?(sortDirection===1?' ▲':' ▼'):' ↕'});
 $('sort-status').textContent=button.childNodes[0].textContent+'：'+(sortDirection===1?'昇順':'降順')+'（検索結果全体。未設定は末尾）'+(sortKey==='stock'?'。昇順は在庫なし→少量→在庫あり→入荷待ち、同じ状態では本数順。':'');render();
}));
function render(){
 updateDownloadButton();const body=$('rows');body.replaceChildren();$('empty').hidden=data.length!==0;
 for(const r of data.slice(page*size,(page+1)*size)){
  const tr=el('tr');tr.dataset.sku=r.sku;
  const selection=el('td',undefined,'selection-cell');const checkbox=el('input');
  checkbox.type='checkbox';checkbox.className='product-select';checkbox.checked=selectedProducts.has(productKey(r));
  checkbox.setAttribute('aria-label',`${r.supplierName} ${r.productNameJa||r.productNameEn} ${r.vintageLabel??r.vintageRaw} ${codeLabel(r)} を選択`);
  tr.classList.toggle('is-selected',checkbox.checked);
  checkbox.addEventListener('change',()=>{if(checkbox.checked)selectedProducts.set(productKey(r),r);else selectedProducts.delete(productKey(r));tr.classList.toggle('is-selected',checkbox.checked);updateDownloadButton()});
  selection.append(checkbox);
  const name=el('td');if(r.duplicateMinimumSelected)name.append(el('span','重複あり・少ない在庫を採用','badge'));name.append(el('div',r.productNameJa,'name'),el('div',r.productNameEn,'secondary'),el('div',codeLabel(r),'secondary'));
  const producer=el('td');producer.append(el('div',r.producerNameJa||r.producerNameEn||'記載なし'),el('div',r.producerNameJa?r.producerNameEn:'','secondary'));
  const country=el('td',r.country||'記載なし');const region=el('td');region.append(el('div',r.region||'記載なし'),el('div',r.productType,'secondary'));
  const vintage=el('td',(r.vintageLabel??r.vintageRaw)||'年不明','nowrap');const volume=el('td',!r.volumeMl?'規格を確認':r.volumeMl+' ml','nowrap');
  const price=el('td',undefined,'nowrap');price.append(el('div',money(r.referenceRetailPrice)),el('div',r.taxIncluded?'税込':'税抜','secondary'));
  if(r.priceNote){const note=el('div',r.priceNote,'secondary');note.style.whiteSpace='normal';price.append(note)}
  const special=el('td',undefined,'nowrap');special.append(el('div',r.specialPurchasePrice==null?'—':money(r.specialPurchasePrice)));if(r.specialPurchasePrice!=null)special.append(el('div','税抜・取込資料時点','secondary'));
  const stock=el('td');stock.append(el('span',r.abundantInventory?'潤沢':(states[r.inventory]||'不明'),'stock '+(r.inventory===2?'low':r.inventory===0?'unknown':'')));if(r.quantity!=null)stock.append(el('div',r.quantity+'本','secondary'));
  const source=el('td');const b=el('button',r.sourceSheet+'!'+r.sourceRow);b.addEventListener('click',()=>detail(r,b));source.append(b);
  tr.append(selection,el('td',r.supplierName||'記載なし'),name,producer,country,region,vintage,volume,price,special,stock,source);body.append(tr);
 }
 $('page').textContent=data.length?`${page*size+1}–${Math.min((page+1)*size,data.length)} / ${data.length}件`:'0件';$('prev').disabled=page===0;$('next').disabled=(page+1)*size>=data.length;
}
function updateDownloadButton() {
 $('download-csv').disabled = searchPending || selectedProducts.size === 0;
 $('download-csv').textContent = `選択商品 ${selectedProducts.size.toLocaleString()}件を確認・CSVダウンロード`;
}

function setSearchPending(pending) {
 searchPending = pending;
 $('busy').hidden = !pending;
 $('search').inert = pending;
 updateDownloadButton();
}

function searchRequest() {
 const explicitUi={};
 for(const [id,key]of [['supplier','supplier'],['country','country'],['region','region'],['type','productType'],['vintage','vintage']])if($(id).value)explicitUi[key]=$(id).value;
 for(const id of ['priceMin','priceMax','volumeMl'])if($(id).value!=='')explicitUi[id]=Number($(id).value);
 if($('available').checked)explicitUi.availableOnly=true;
 return {query:$('text').value,explicitUi};
}
const fallbackLabels={model_disabled:'AI解釈は無効です。',query_empty:'自由文が未入力のため、指定した条件で検索しました。',catalog_changed:'商品情報が更新されたため、最新情報で規則検索しました。',bridge_configuration_invalid:'AI解釈の接続設定を利用できません。',bridge_http_failure:'AI解釈サービスから正常な応答を得られませんでした。',bridge_response_too_large:'AI解釈の応答が許容サイズを超えました。',bridge_timeout:'AI解釈が制限時間内に完了しませんでした。',bridge_unavailable:'AI解釈サービスへ接続できませんでした。',bridge_invalid_response:'AI解釈の応答を検証できませんでした。',bridge_request_invalid:'AI解釈への依頼を検証できませんでした。',merged_conditions_invalid:'AI解釈と明示条件の組合せを採用できませんでした。'};
function adoptedConditions(conditions) {
 const c=conditions||{},f=c.filter||{},parts=[];
 if(c.supplier)parts.push('取引先：'+(supplierNames.get(c.supplier)||c.supplier));
 for(const [key,label]of [['country','国'],['region','産地'],['productType','タイプ'],['vintageRaw','年']])if(f[key])parts.push(label+'：'+f[key]);
 if(f.volumeMl!=null)parts.push('容量：'+f.volumeMl+' ml');
 if(f.priceMin!=null)parts.push('希望小売価格の下限（税抜）：'+money(f.priceMin));
 if(f.priceMax!=null)parts.push('希望小売価格の上限（税抜）：'+money(f.priceMax));
 if(f.availableOnly===true)parts.push('在庫あり・少量のみ');
 if(c.keywords?.length)parts.push('キーワード：'+c.keywords.join(' '));
 if(c.regions?.length)parts.push('産地（いずれか）：'+c.regions.join(' / '));
 if(c.geographyCoverage)parts.push(c.geographyCoverage);
 if(c.grape)parts.push('品種：'+c.grape.name+(c.grape.mode==='predominant'?'（過半または主体の根拠あり）':'（含有の根拠あり）'));
 if(c.taste)parts.push('味わい：'+c.taste+'（原本の記載）');
 if(c.sparkling)parts.push('発泡タイプ：スパークリング');
 if(c.parsedConditions?.length)parts.push(...c.parsedConditions);
 return [...new Set(parts)];
}
function appendImporterEvidence(parent,evidence) {
 if(!evidence)return;
 const section=el('section');section.append(el('h3','インポーター公式商品ページの補完根拠'),el('p','品種条件の判定に使用した情報です。原本の価格・在庫・商品記述は変更していません。'));
 const dl=el('dl');
 const fetched=typeof evidence.fetchedAtUtc==='string'?new Date(evidence.fetchedAtUtc):null;
 const fetchedLabel=fetched&&!Number.isNaN(fetched.getTime())?fetched.toLocaleString('ja-JP',{timeZone:'Asia/Tokyo',year:'numeric',month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit',second:'2-digit',hourCycle:'h23'})+'（日本時間）':'情報なし';
 for(const [label,value]of [['品種',evidence.grape],['取得日時',fetchedLabel],['出典の記述',evidence.sourceExcerpt]])
  dl.append(el('dt',label),el('dd',typeof value==='string'?value:'情報なし'));
 section.append(dl);
 try {
  const url=new URL(evidence.sourceUrl);
  if(url.protocol!=='https:'||url.username||url.password)throw Error('invalid_source');
  const link=el('a','公式商品ページを開く');link.href=url.href;link.target='_blank';link.rel='noopener noreferrer';section.append(link);
 }catch{section.append(el('p','出典URLを確認できません。'));}
 parent.append(section);
}
async function freeTextSearch() {
 const response=await fetch('/api/free-text-search/search',{method:'POST',headers:{'Content-Type':'application/json','X-Catalog-Action':'local-demo'},body:JSON.stringify(searchRequest())});
 if(!response.ok)throw Error('検索結果を取得できません（'+response.status+'）。条件を確認して再検索してください。');
 return response.json();
}

function validPriceRange() {
 const min = $('priceMin').value;
 const max = $('priceMax').value;
 return min === '' || max === '' || Number(min) <= Number(max);
}

function showSearchResults(result) {
 const awaitingConditions=result.mode==='needsClarification';
 data = result.rows;
 for(const row of data)if(selectedProducts.has(productKey(row)))selectedProducts.set(productKey(row),row);
 $('csv-status').textContent = result.capped
  ? '検索結果は先頭2,000件です。条件を絞って再検索し、必要な商品を追加してください。選択は保持されます。'
  : 'チェックした商品だけをCSV出力します。再検索・ページ切り替え後も選択は保持され、追加できます。';
 const conditions=adoptedConditions(result.conditions);
 $('interpreted').textContent=(awaitingConditions?'確認中の検索条件：':'採用した検索条件：')+(conditions.length?conditions.join(' ／ '):awaitingConditions?'未確定':'すべて');
 const clarificationLabels={explicit_red_not_adopted:'赤の条件',explicit_white_not_adopted:'白の条件',explicit_stock_not_adopted:'在庫条件',explicit_country_not_adopted:'国の条件',explicit_grape_not_adopted:'品種の条件',explicit_predominance_not_adopted:'主体の条件',geography_scope_unverified:'地理的な範囲',explicit_price_min_not_adopted:'価格下限',explicit_price_max_not_adopted:'価格上限',explicit_volume_not_adopted:'容量',explicit_vintage_not_adopted:'年',price_scope_unverified:'価格の意味',explicit_conditions_unverified:'明示条件',unparsed_terms:'未解釈の条件'};
 $('search-mode').textContent=result.mode==='needsClarification'
  ? '条件を確定できないため検索していません。'+(result.clarificationReasons||[]).map(x=>clarificationLabels[x]||fallbackLabels[x]||'AI解釈を確認できません').join(' ／ ')+(result.unparsedTerms?.length?'。未解釈：'+result.unparsedTerms.join(' ／ '):'')+'。文章を具体化するか、画面で条件を指定してください。'
  : result.fallbackReason==='dictionary_match'?'辞書による検索（画面の明示条件を優先）'
  : result.mode==='model'?'AI解釈による検索（画面の明示条件を優先）':'規則検索：'+(fallbackLabels[result.fallbackReason]||'AI解釈を採用せず、規則で検索しました。');
 if(result.evidenceUnavailableCount||result.evidenceConflictCount||result.complete===false&&result.mode==='model')
  $('search-mode').textContent+=' 確認した候補中、根拠不足 '+(result.evidenceUnavailableCount||0)+'件、根拠の不一致 '+(result.evidenceConflictCount||0)+'件。条件を確認できた商品だけを表示しています。'+(result.complete===false?'全件の確認は完了していません。':'');
 applySort();
 page = 0;
 $('count').textContent = awaitingConditions?'未検索':`${data.length.toLocaleString()}件${result.capped ? '（先頭2,000件）' : ''}`;
 $('empty').textContent = awaitingConditions?'条件の確認待ちのため、まだ検索していません。':'該当する商品はありません。条件を減らして再検索してください。';
 render();
 if(awaitingConditions)$('page').textContent='未検索';
}

function showSearchFailure(message) {
 error(message);
 data = [];
 page = 0;
 $('count').textContent = '取得できません';
 render();
 $('empty').hidden = true;
}

async function search() {
 error('');
 // A rejected input does not change the last successful results or their export.
 if (!validPriceRange()) {
  error('価格の下限は上限以下にしてください。');
  return;
 }
 $('interpreted').textContent = '';
 $('search-mode').textContent = '';
 $('csv-status').textContent = '選択は再検索後も保持されます。別の検索結果から商品を追加できます。';
 setSearchPending(true);
 try {
  showSearchResults(await freeTextSearch());
 } catch (e) {
  showSearchFailure(e.message);
 } finally {
  setSearchPending(false);
 }
}

function downloadResults() {
 if (searchPending || selectedProducts.size === 0) return;
 $('selection-error').hidden=true;
 let url;
 const link = el('a');
 try {
  const blob = new Blob([CatalogCsv.serialize(selectedRows())], {type: 'text/csv;charset=utf-8'});
  url = URL.createObjectURL(blob);
  link.href = url;
  link.download = '選択商品_' + new Date().toISOString().replace(/[:.]/g, '-') + '.csv';
  document.body.append(link);
  link.click();
 } catch {
  $('selection-error').textContent='CSVのダウンロードに失敗しました。選択は保持されています。もう一度お試しください。';
  $('selection-error').hidden=false;
 } finally {
  link.remove();
  if (url) setTimeout(() => URL.revokeObjectURL(url), 1000);
 }
}

function appendSourcePrices(row,unadopted){
 const section=el('section');section.append(el('h3',unadopted?'原典に記載された価格（未採用）':'原典の価格成分'));
 section.append(el('p',unadopted?'単位・条件が未確定のため、検索価格には使用していません。':'原典に記録された価格・税区分・単位です。未確認の項目は確定した価格条件として扱わないでください。'));
 const labels={Wholesale:'納価（納入価格）',ReferenceRetail:'参考上代',TaxInclusiveRetail:'税込上代',ConditionalPrice:'条件付き価格'};
 const units={PerBottle:'各行の商品1本あたり',PerCase:'ケース単位',Unknown:'単位未確認'};
 for(const price of row.PriceComponents||[]){const unit=units[price.Unit]||price.Unit||'単位未確認',tax=price.TaxIncluded===true?'税込':price.TaxIncluded===false?'税抜':'税区分未確認';section.append(el('p',(labels[price.Kind]||price.Kind||'種類未確認')+'：'+money(price.Amount)+'（'+tax+'・'+unit+'） 原表記：'+(price.Raw??'記載なし')));}
 $('detail-body').append(section);
}
async function detail(r,button){error('');$('busy').hidden=false;try{const d=await json(`/api/evidence/${r.sourceDocumentId}/${encodeURIComponent(r.sku)}`);const row=JSON.parse(d.extractedRowJson);$('detail-body').replaceChildren();$('detail-title').textContent=row.ProductNameJa;const dl=el('dl');for(const [k,v] of [['仕入先SKU',sourceSku(r)||'記載なし（内部IDで管理）'],['元ファイル',d.fileName],['在庫表の日付',d.observedAt.slice(0,10)],['シート / 行',d.sheetName+'!'+d.rowNumber],['年号の原表記',row.VintageRaw||'記載なし'],['希望小売の原表記',r.codeKind==='InternalIdentity'?'単位確認中（下記の原典価格を参照）':row.PriceRaw],['特価仕入価格（税抜）',r.specialPurchasePrice==null?'記載なし':money(r.specialPurchasePrice)],['在庫の原表記',row.Inventory.RawValue||'空欄（不明）'],['JAN',row.JanCodes.join(' / ')||'記載なし'],['品種',row.Grapes||'記載なし'],['原本の商品コード',r.codeKind==='InternalIdentity'?'記載なし':(row.OriginalSupplierProductCode||row.SupplierProductCode)],['価格・規格の注意',row.PriceNote||'記載なし'],['商品コメント',r.codeKind==='InternalIdentity'?'原典の商品表記を保持し、内部IDで対応付けています。':(row.Description||'記載なし')]])dl.append(el('dt',k),el('dd',v));const details=el('details');details.append(el('summary','元のセル内容を見る'));const table=el('table');for(const [cell,value]of Object.entries(JSON.parse(d.rawCellsJson))){const tr=el('tr');tr.append(el('th',cell),el('td',value));table.append(tr)}details.append(table);$('detail-body').append(dl);if(r.codeKind==='InternalIdentity'||row.PriceComponents?.length)appendSourcePrices(row,r.codeKind==='InternalIdentity');if(row.DuplicateMinimumSelected){const dup=el('section');dup.append(el('h3','重複あり・少ない在庫を採用'),el('p','数量は加算していません。両方の原典行を保持しています。'));for(const occurrence of row.Occurrences||[]){const item=el('details');item.append(el('summary',occurrence.Sheet+'!'+occurrence.Row+'：'+occurrence.Inventory.Quantity+'本'+(occurrence.Row===row.SourceRow?'（採用）':'')));const cells=el('table');for(const[cell,value]of Object.entries(JSON.parse(occurrence.RawCellsJson))){const tr=el('tr');tr.append(el('th',cell),el('td',value));cells.append(tr)}item.append(cells);dup.append(item)}$('detail-body').append(dup);}$('detail-body').append(details);appendImporterEvidence($('detail-body'),r.importerEvidence);if(r.supplierProductId&&(r.codeKind==='InternalIdentity'||r.supplierName.includes('ENEOS'))){const sources=await json('/api/products/'+r.supplierProductId+'/source-inventory');const section=el('section');section.append(el('h3','同じ基準日の原本別在庫（数量は加算しません）'),el('p','一覧の数量：'+(sources.quantity??'不明')));for(const source of sources.sources)for(const evidence of source.rows)section.append(el('p',source.fileName+' / '+evidence.sheet+'!'+evidence.row+'：'+(evidence.inventory.quantity??'不明')+' / 原表記 '+(evidence.inventory.rawValue||'空欄')));$('detail-body').append(section);}$('detail').showModal();$('detail').addEventListener('close',()=>button.focus(),{once:true})}catch(e){error(e.message)}finally{$('busy').hidden=true}}
function renderSelection() {
 const body=$('selected-rows');body.replaceChildren();
 $('selected-count').textContent=`${selectedProducts.size.toLocaleString()}件`;
 $('selected-empty').hidden=selectedProducts.size!==0;
 $('download-selected-csv').disabled=selectedProducts.size===0;
 for(const [index,r] of selectedRows().entries()){
  const tr=el('tr');const action=el('td');const remove=el('button','選択解除');remove.type='button';
  remove.setAttribute('aria-label',`${r.supplierName} ${r.productNameJa||r.productNameEn} ${codeLabel(r)} の選択解除`);
  remove.addEventListener('click',()=>{selectedProducts.delete(productKey(r));renderSelection();render();const buttons=$('selected-rows').querySelectorAll('button');(buttons[Math.min(index,buttons.length-1)]||$('back-to-results')).focus()});
  action.append(remove);const product=el('td');product.append(el('div',r.productNameJa||r.productNameEn,'name'),el('div',`${codeLabel(r)} ／ ${r.producerNameJa||r.producerNameEn||'生産者記載なし'}`,'secondary'));
  tr.append(action,el('td',r.supplierName),product,el('td',(r.vintageLabel??r.vintageRaw)||'年不明'),el('td',r.volumeMl?r.volumeMl+' ml':'規格を確認'),el('td',money(r.referenceRetailPrice)));body.append(tr);
 }
}
$('download-csv').addEventListener('click',()=>{if(searchPending||!selectedProducts.size)return;$('selection-error').hidden=true;renderSelection();$('selected-products').showModal()});
$('download-selected-csv').addEventListener('click',downloadResults);
$('close-selected').onclick=$('back-to-results').onclick=()=>$('selected-products').close();
$('selected-products').addEventListener('close',()=>{if(!$('download-csv').disabled)$('download-csv').focus();else $('text').focus()});
$('search').addEventListener('submit',e=>{e.preventDefault();search()});$('search').addEventListener('reset',()=>setTimeout(search,0));$('example').addEventListener('click',()=>{$('text').value='';$('priceMin').value='';$('volumeMl').value='';$('vintage').value='';$('country').value='フランス';$('region').value='ブルゴーニュ';$('type').value='赤';$('priceMax').value=10000;$('available').checked=true;search()});$('prev').onclick=()=>{page--;render()};$('next').onclick=()=>{page++;render()};$('close').onclick=()=>$('detail').close();
(async()=>{try{
 const response=await json('/api/free-text-search/options'),options=response.options;
 for(const [id,key]of [['country','countries'],['region','regions'],['type','productTypes'],['volumeMl','volumesMl'],['vintage','vintages']])for(const value of options[key])if(value){const o=el('option',id==='volumeMl'?value+' ml':value);o.value=value;$(id).append(o)}
 for(const supplier of response.suppliers){supplierNames.set(supplier.code,supplier.name);const option=el('option',supplier.name+'（'+supplier.code+'）');option.value=supplier.code;$('supplier').append(option)}
 try{const summary=await json('/api/options?vintageNotation=canonical');$('total').textContent=`登録商品 ${summary.total.toLocaleString()}件`;}catch{$('total').textContent='登録商品数を取得できません';}
 await search();
}catch(e){$('total').textContent='接続できません';error(e.message)}})();
