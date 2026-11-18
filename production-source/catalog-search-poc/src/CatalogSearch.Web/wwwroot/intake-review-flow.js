(() => {
'use strict';
const $=id=>document.getElementById(id),id=new URLSearchParams(location.search).get('id'),base='/api/import/reviews/'+encodeURIComponent(id||'');
const imageOnly=new URLSearchParams(location.search).get('view')==='original';
let review,draft=null,busy=false,answerInitialized=false;
let edited=false,unconfirmed=false;
const mappingInputs=new Map();
let reuseRules=null,reuseRulesError=false,reuseLoadVersion=0;
let mappingActiveId=null,mappingPositionId=null,mappingNavigating=false,mappingReady=false,mappingBulkKey=null;
function nextState(){ updateMappingNavigation();$('partial-import-panel').hidden=imageOnly||!review?.canResumeResolved;$('resume-resolved').disabled=busy||edited||!!draft||unconfirmed||mappingNavigating||!review?.canResumeResolved;const refreshAllowed=!imageOnly&&review?.result?.questionKind==='Mapping'&&['Held','Ready'].includes(review?.state);$('refresh-questions-panel').hidden=!refreshAllowed;$('refresh-questions').disabled=!refreshAllowed||busy||edited||!!draft||unconfirmed||mappingNavigating; $('save').disabled=busy||unconfirmed||['Unknown','Running'].includes(review?.state);$('resume').disabled=busy||unconfirmed||['Unknown','Running'].includes(review?.state);document.querySelectorAll('#reuse-options button').forEach(b=>b.disabled=busy||edited||!!draft||unconfirmed||['Unknown','Running'].includes(review?.state)); $('next-review-panel').hidden=imageOnly||!review;$('next-review').disabled=busy||edited||!!draft||unconfirmed||['Unknown','Running'].includes(review?.state); }
$('form').addEventListener('input',event=>{if(event.target.id==='mapping-question')return;edited=true;nextState();$('next-review-status').textContent='未保存の入力があります。回答を保存してから次へ進んでください。';});
$('next-review').onclick=async()=>{if(busy||edited||draft||unconfirmed||['Unknown','Running'].includes(review?.state))return;busy=true;nextState();try{
 const current=await get(base);if(current.version!==review.version||current.hash!==review.hash||['Unknown','Running'].includes(current.state)){unconfirmed=true;throw Error('保存状態が変わりました。再読込して照合してください。');}
 const reviews=await get('/api/import/reviews');const next=reviews.find(r=>r.id!==id&&['Held','Partial'].includes(r.state)&&r.canAnswer!==false);
 if(!next){$('next-review-status').textContent='最新100件の範囲に次の未解決資料はありません。一覧から全体の処理状況を確認してください。';return;}
 busy=false;location.assign('/intake-review.html?id='+encodeURIComponent(next.id));
 }catch(e){$('next-review-status').textContent=e.message+' 移動・取込は行っていません。';}finally{busy=false;nextState();}};
const steps=['source','answer','confirm','result'];
function show(step){for(const s of steps)$(s).hidden=s!==step&&!(s==='source'&&!imageOnly);document.querySelectorAll('nav li').forEach((e,i)=>{e.removeAttribute('aria-current');if(steps[i]===step)e.setAttribute('aria-current','step');});$(step).querySelector('h2').focus();}
async function get(url){const r=await fetch(url);if(!r.ok)throw Error('情報を取得できません（'+r.status+'）。');return r.json();}
function history(){ renderMappingsHistory();void loadReuseRules();nextState(); $('file').textContent=review.path;$('identity').textContent='SHA256: '+review.hash+' ／回答版: '+review.version;$('history').replaceChildren(...review.history.map(e=>{const li=document.createElement('li');li.textContent=e.at+' ｜ '+e.method+' ｜ '+e.action+' ｜ '+(e.date||'')+' ｜ '+e.reason;return li;}));}
async function source(){
show('source');if(!imageOnly&&review.canAnswer)answer();
const dateQuestion=review.result?.questionKind==='Date',mappingQuestion=review.result?.questionKind==='Mapping';
const guidance=window.importGuidance(review.result);let diagnostic=$('import-diagnostic');if(!diagnostic){diagnostic=document.createElement('section');diagnostic.id='import-diagnostic';diagnostic.setAttribute('role','status');$('source').querySelector('h2').after(diagnostic);}diagnostic.replaceChildren();diagnostic.hidden=imageOnly||review.state==='Completed';if(!diagnostic.hidden){for(const [label,value] of [['取込できない原因',guidance.cause],['対応方法',guidance.remedy]]){const p=document.createElement('p'),strong=document.createElement('strong');strong.textContent=label+'：';p.append(strong,document.createTextNode(value));diagnostic.append(p);}if(review.result?.status==='Unsupported'){$('answer').hidden=true;document.querySelectorAll('nav li').forEach(e=>e.removeAttribute('aria-current'));document.querySelector('nav li').setAttribute('aria-current','step');}}
const priorityRepair=review.hash==='901FAB1EF4940521203D3CBAB3A4EBDA1E82F48C835669D867109DF3D62B4527'&&review.state==='Partial'&&review.result?.supplierCode==='ORVEAUX'&&review.result?.status==='Partial'&&review.result?.products===160&&!!review.result?.sourceDocumentId&&!review.result?.stopQueue;
if(!imageOnly&&(priorityRepair||review.state==='Held'&&(review.result?.status==='Unsupported'||review.hash==='901FAB1EF4940521203D3CBAB3A4EBDA1E82F48C835669D867109DF3D62B4527'&&review.result?.supplierCode==='ORVEAUX'&&review.result?.status==='Held'&&review.result?.message?.includes('同日別内容'))&&!review.result?.stopQueue&&!review.result?.products&&!review.result?.sourceDocumentId)){const button=document.createElement('button');button.type='button';button.textContent='この原典を再読取りして取込';button.disabled=busy||unconfirmed;button.onclick=async()=>{if(busy||unconfirmed)return;busy=true;button.disabled=true;nextState();let sent=false;try{const expected={version:review.version,hash:review.hash};sent=true;const response=await fetch(base+'/recheck',{method:'POST',headers:{'Content-Type':'application/json','X-Catalog-Action':'local-demo'},body:JSON.stringify(expected)});if(!response.ok)throw Error('再読取りの結果を確認できません（'+response.status+'）。');const receipt=await response.json();if(receipt.id!==review.id||receipt.hash!==review.hash)throw Error('原本の照合に失敗しました。');review=receipt;edited=false;draft=null;answerInitialized=false;history();await source();$('notice').textContent=review.result?.message||'';window.dispatchEvent(new CustomEvent('intake-review-updated',{detail:{...review.result,reviewId:review.id,hash:review.hash}}));}catch(e){if(sent)unconfirmed=true;$('notice').textContent=e.message+' 自動再送せず、再読込して履歴を確認してください。';}finally{busy=false;nextState();}};diagnostic.append(button);const help=document.createElement('p');help.textContent='保存済みの同一原本を再判定します。条件が揃った場合は商品を登録します。';diagnostic.append(help);}
$('review-layout').classList.toggle('mapping-review',mappingQuestion&&!imageOnly);
$('row-holds').replaceChildren(...(review.result?.holds||[]).map(h=>{const li=document.createElement('li'),button=document.createElement('button');li.textContent=h.sku+'：'+h.reason+' ';button.textContent='この行の原典を見る';button.onclick=()=>window.originalRenderer.selectReference(h.sources[0]);li.append(button);return li;}));
$('source').querySelector('h2').textContent=imageOnly?'元ファイルの画像を確認する':dateQuestion?'この在庫リストは、いつ時点の情報ですか？':mappingQuestion?mappingKindsLabel()+'を確認してください':'資料の内容を確認してください';
$('question').textContent=imageOnly?'画像表示の確認画面です。ファイルの内容と、強調された行を確認してください。ここでは日付の回答や商品の取込は行いません。':review.state==='Completed'?'この資料の取込は完了しています。原典と保存済みの回答・履歴を確認できます。次回への再利用設定は別途保存してください。':dateQuestion?'取込に使う基準日を確定できないため、処理を保留しています。資料に記載された「発行日」や「在庫日」を確認してください。':mappingQuestion?'原典を見ながら、各行の'+mappingKindsLabel()+'について確認してください。原略号・対象列ごとに回答を区別します。':guidance.cause;
$('location').textContent=imageOnly?'内容のある位置から表示します。別の行を見る場合は行番号を変更してください。':review.state==='Completed'?'取込済みの原典を表示しています。表示や再利用設定の保存だけで商品を再登録することはありません。':dateQuestion?'原典を見ながら回答欄に日付を入力してください。分からない場合は「保留を継続する」を選んでください。':guidance.remedy;
if(review.result?.status==='Unsupported'&&!imageOnly){$('source').querySelector('h2').textContent='取込できない原因と対応方法';$('question').hidden=true;$('location').hidden=true;}else{$('question').hidden=false;$('location').hidden=false;}
$('original').href=base+'/original';$('to-answer').hidden=imageOnly||!review.canAnswer||review.result?.status==='Unsupported';
$('to-answer').textContent=dateQuestion?'日付の回答へ':'確認結果を記録する';
await window.originalRenderer.load(base,review.result?.questions?.[0]||review.result?.holds?.[0]?.sources?.[0]);mappingReady=true;syncMappingReference();if(!imageOnly)$('to-answer').hidden=true;
}
function answer(){ renderMappingInputs();const mappingAllowed=review.result?.questionKind==='Mapping';$('action').querySelector('[value="mapping"]').disabled=!mappingAllowed;const selected=answerInitialized?[...$('row-options').querySelectorAll('input:checked')].map(i=>i.value):(review.selectedSkus||[]);const rowsAllowed=review.result?.questionKind==='Rows';$('action').querySelector('[value="recheck-rows"]').disabled=!rowsAllowed;$('row-options').replaceChildren(...(review.result?.holds||[]).map(h=>{const label=document.createElement('label'),input=document.createElement('input');input.type='checkbox';input.value=h.sku;input.checked=selected.includes(h.sku);label.append(input,document.createTextNode(h.sku+' / '+h.reason));return label;}));const dateAllowed=review.result?.questionKind==='Date';$('action').options[0].disabled=!dateAllowed;if(!answerInitialized){answerInitialized=true;$('action').value=dateAllowed?'date':rowsAllowed?'recheck-rows':mappingAllowed?'mapping':'hold';$('date').value=review.date||'';}$('date-label').hidden=$('action').value!=='date';$('date').required=$('action').value==='date';$('row-choices').hidden=$('action').value!=='recheck-rows';$('mapping-choices').hidden=$('action').value!=='mapping';show('answer');syncMappingReference();}
function summary(){const pending=!!draft;$('summary').textContent=pending?review.path+'\n対応：'+(draft.action==='date'?'資料日付を確定する':draft.action==='recheck-rows'?'選択商品の再判定：'+draft.selectedSkus.join(', '):draft.action==='mapping'?'確認項目の回答：\n'+draft.mappingAnswers.map(a=>{const q=review.result.questions.find(q=>q.id===a.questionId);return q.sheet+' 行'+q.row+' / '+q.sku+' / '+mappingTarget(q)+' / 原文 '+q.rawValue+' → '+mappingMeaning(a.meaning);}).join('\n'):'保留を継続する')+'\n日付：'+(draft.date||'—')+'\n根拠：'+draft.reason:review.path+'\n確定済みの資料日付：'+(review.date||'—')+'\n再開対象商品：'+(review.selectedSkus?.join(', ')||'資料単位')+'\n判断根拠：'+([...review.history].reverse().find(e=>e.method==='Human')?.reason||'—');$('save').hidden=!pending;$('resume').hidden=pending||!review.canResume;nextState();$('edit').hidden=!review.canAnswer;$('saved').textContent=unconfirmed?'保存結果の照合が必要です。再開せず担当者に確認してください。':pending?'この段階ではまだ保存していません。': '回答は保存されています。再開は下のボタンで明示的に実行します。';show('confirm');}
function result(){const labels={Completed:'取込が完了しました。',Partial:'一部保存・保留あり。資料全体の取込は未完了です。',Held:'保留しました。',Unknown:'保存結果が不明です。再開せず担当者が保存結果を照合してください。',Running:'処理中、または保存結果の照合が必要です。再開はできません。',Created:'受付済みです。担当者が処理状況を確認してください。'};$('result-text').textContent=(labels[review.state]||review.state)+' '+(review.result?.message||'')+(review.result?.supplierCode==='LA_VINEE'&&review.result?.products>0?' 保存済み合計 '+review.result.products+'件／今回追加 '+(review.result.addedProducts||0)+'件。':'');$('again').textContent=review.canAnswer?'原典・回答を確認する':'原典を確認する';show('result');}
async function post(path,body){if(imageOnly||busy||unconfirmed||['Unknown','Running'].includes(review?.state))return;busy=true;$('notice').textContent='保存・処理中です。';document.querySelectorAll('button').forEach(b=>b.disabled=true);try{const r=await fetch(base+path,{method:'POST',headers:{'Content-Type':'application/json','X-Catalog-Action':'local-demo'},body:JSON.stringify(body)});const data=await r.json();if(!r.ok)throw Error(data.error||'処理を確定できませんでした。');review=data;if(path==='/answer'&&body.action==='mapping'){reuseRules=null;$('reuse-status').textContent='回答を保存しました。次回への再利用は、下のボタンで別途保存してください。';}mappingInputs.clear();draft=null;edited=false;unconfirmed=false;history();$('next-review-status').textContent='回答を保存しました。次へ進んでも取込は実行しません。';$('notice').textContent='保存しました。';review.canResume?summary():result();}catch(e){unconfirmed=true;$('notice').textContent=e.message+' 自動再送はしません。';try{review=await get(base);history();draft=null;review.canResume?summary():result();}catch{$('notice').textContent+=' 保存状態の再取得にも失敗しました。一覧に戻り接続回復後に照合してください。';$('save').hidden=true;$('resume').hidden=true;}}finally{busy=false;document.querySelectorAll('button').forEach(b=>b.disabled=false);nextState();}}
$('to-answer').onclick=answer;$('back-source').onclick=source;$('action').onchange=()=>{$('date-label').hidden=$('action').value!=='date';$('date').required=$('action').value==='date';$('row-choices').hidden=$('action').value!=='recheck-rows';$('mapping-choices').hidden=$('action').value!=='mapping';};
$('form').onsubmit=e=>{e.preventDefault();const sourceLocation=window.originalRenderer.location();draft={action:$('action').value,date:$('action').value==='date'?$('date').value:null,reason:$('reason').value+(sourceLocation?'\n参照位置：'+sourceLocation:''),sourceReference:window.originalRenderer.reference(),mappingAnswers:$('action').value==='mapping'?[...mappingInputs].filter(([id,value])=>value).map(([questionId,meaning])=>({questionId,meaning})):null,selectedSkus:$('action').value==='recheck-rows'?[...$('row-options').querySelectorAll('input:checked')].map(i=>i.value):null};if(draft.action==='mapping'&&!draft.mappingAnswers.length){draft=null;$('notice').textContent='少なくとも1つの確認項目を回答してください。';return;}if(draft.action==='recheck-rows'&&!draft.selectedSkus.length){$('notice').textContent='未保存商品を選択してください。';return;}summary();};
$('refresh-questions').onclick=async()=>{
 if(imageOnly||busy||edited||draft||unconfirmed||mappingNavigating||review?.result?.questionKind!=='Mapping'||!['Held','Ready'].includes(review.state))return;
 const frozen=[...$('form').querySelectorAll('input,textarea,select,button')].map(control=>[control,control.disabled]);
 busy=true;nextState();for(const [control] of frozen)control.disabled=true;const expected={version:review.version,hash:review.hash,operationId:crypto.randomUUID()};
 $('refresh-questions-status').textContent='同じ原典の質問を更新しています…';
 try{
  const response=await fetch(base+'/refresh-questions',{method:'POST',headers:{'Content-Type':'application/json','X-Catalog-Action':'local-demo'},body:JSON.stringify(expected)});
  const result=await response.json();if(!response.ok)throw Error(result.error||'質問の更新を確定できませんでした。');
  const latest=await get(base);
  if(latest.id!==review.id||latest.hash!==expected.hash||latest.version<result.version)throw Error('更新後の原典・回答版を照合できませんでした。');
  review=latest;mappingInputs.clear();reuseRules=null;history();await source();
  $('refresh-questions-status').textContent=(latest.version===expected.version?'確認項目に変更はありません。':'質問を更新しました。一致する保存済み回答を保持し、未回答の確認項目を表示しています。')+' 商品取込は実行していません。';
 }catch(e){
  unconfirmed=true;let reconciled=false;
  try{
   const latest=await get(base);
   if(latest.id===review.id&&latest.hash===expected.hash){
    reconciled=latest.history?.some(event=>event.action==='RefreshQuestions'&&event.method==='Rule'&&event.operationId===expected.operationId&&event.version===expected.version+1)===true;
    review=latest;unconfirmed=!reconciled;history();await source();
   }
  }catch{unconfirmed=true;reconciled=false;}
  $('refresh-questions-status').textContent=reconciled
   ?'操作記録を照合し、質問の更新を確認しました。一致する保存済み回答を保持しています。商品取込は実行していません。'
   :e.message+' 更新結果は未確認です。自動再送せず、回答保存・取込再開・次の資料への移動を停止しています。操作ID：'+expected.operationId;

 }finally{for(const [control,disabled] of frozen)if(control.isConnected)control.disabled=disabled;busy=false;nextState();}
};
$('resume-resolved').onclick=()=>{if(imageOnly||busy||edited||draft||unconfirmed||mappingNavigating||!review?.canResumeResolved)return;post('/resume-resolved',{version:review.version,hash:review.hash});};
$('edit').onclick=answer;$('save').onclick=()=>post('/answer',{version:review.version,hash:review.hash,...draft});$('resume').onclick=()=>post('/resume',{version:review.version,hash:review.hash});$('again').onclick=source;

function mappingKey(q){return JSON.stringify([q.scope.supplierId,q.scope.documentKind,q.scope.columnsFingerprint,q.kind,q.sourceKey,q.scope.productIdentity||null,q.scope.vintage||null]);}
function mappingMeaning(value){return value==='PerBottle'?'各行の商品1本あたり':value;}
function mappingLabel(q){if(q.kind==='ColumnMeaning'&&q.sourceKey==='J/納価単位')return '納価の単位';return ({VolumeUnit:'容量単位',RegionAbbreviation:'地域略号',ColumnMeaning:'列の意味',ProductAlias:'商品別名'})[q.kind]||'確認項目';}
function mappingTarget(q){return mappingLabel(q)+'（'+q.sourceKey+'）';}
function mappingKindsLabel(){return [...new Set(mappingQuestions().map(mappingLabel))].join('・')||'確認項目';}
function renderMappingsHistory(){
 $('mapping-history').replaceChildren(...(review.mappingResolutions||[]).map(r=>{const li=document.createElement('li');li.textContent=r.question.sheet+' 行'+r.question.row+' / '+r.question.sku+' / '+mappingTarget(r.question)+' / '+r.question.rawValue+' → '+mappingMeaning(r.meaning)+' / '+r.evidence;return li;}));
}
function renderMappingInputs(){
 $('mapping-legend').textContent='各行の'+mappingKindsLabel()+'を確認';$('action').querySelector('[value="mapping"]').textContent='原典の'+mappingKindsLabel()+'を回答する';
 $('mapping-options').replaceChildren(...(review.result?.questions||[]).map(q=>{
  const box=document.createElement('div'),label=document.createElement('label'),select=document.createElement('select'),button=document.createElement('button');box.className='mapping-row';
  label.textContent=q.sheet+' 行'+q.row+' / '+q.sku+' / '+mappingTarget(q)+' / 原文「'+q.rawValue+'」：'+q.prompt.replaceAll('PerBottle','「各行の商品1本あたり」');
  select.setAttribute('aria-label',q.sheet+' 行'+q.row+' '+q.sku+' '+mappingTarget(q));
  for(const meaning of ['',...q.choices]){const option=document.createElement('option');option.value=meaning;option.textContent=mappingMeaning(meaning)||'未回答（保留）';select.append(option);}
  if(!mappingInputs.has(q.id))mappingInputs.set(q.id,(review.mappingResolutions||[]).find(r=>r.question.id===q.id)?.meaning||'');select.value=mappingInputs.get(q.id);
  select.onchange=()=>{mappingInputs.set(q.id,select.value);edited=true;updateMappingLabels();syncMappingReference();nextState();if(mappingActiveId!==q.id)focusMapping(q.id,false);};
  select.onfocus=()=>{if(mappingActiveId!==q.id)focusMapping(q.id,false);};
  button.type='button';button.textContent='この行の原典を見る';button.onclick=()=>focusMapping(q.id,true);
  box.dataset.questionId=q.id;const status=document.createElement('p');status.className='mapping-status';status.setAttribute('aria-live','polite');label.append(select);box.append(label,status,button);return box;
 }));
 updateMappingLabels();if(mappingReady)syncMappingReference();else updateMappingNavigation();
}
$('mapping-fill').onclick=()=>{if(mappingBlocked())return;const current=mappingQuestions().find(q=>q.id===mappingActiveId),meaning=$('mapping-bulk').value;if(!current||!meaning||!current.choices.includes(meaning))return;const key=mappingKey(current);let count=0;for(const q of mappingQuestions())if(mappingKey(q)===key&&!mappingValue(q)&&q.choices.includes(meaning)){mappingInputs.set(q.id,meaning);count++;}if(!count)return;edited=true;renderMappingInputs();nextState();$('notice').textContent=mappingTarget(current)+'の同じ確認対象の空欄'+count+'件に入力しました。他の原略号・列・種類へは入力していません。各行の原典を確認してから回答を保存してください。';};

function mappingQuestions(){return review?.result?.questions||[];}
function mappingBlocked(){return busy||mappingNavigating||$('render-original').disabled||unconfirmed||['Unknown','Running'].includes(review?.state);}
function mappingValue(q){return mappingInputs.has(q.id)?mappingInputs.get(q.id):(review.mappingResolutions||[]).find(r=>r.question.id===q.id)?.meaning||'';}
function mappingStatus(q){
 const value=mappingValue(q),saved=(review.mappingResolutions||[]).find(r=>r.question.id===q.id)?.meaning;
 if(value&&value===saved)return '保存済み：'+value;
 if(value)return '入力済み・未保存：'+value;
 return saved?'入力欄は空欄（保存済み回答 '+saved+' は削除されません）':'未回答';
}
function updateMappingLabels(){
 const questions=mappingQuestions(),picker=$('mapping-question');
 picker.replaceChildren(...questions.map((q,i)=>{const option=document.createElement('option');option.value=q.id;option.textContent=(i+1)+' / '+questions.length+' ｜ '+q.sheet+' 行'+q.row+' ｜ '+q.sku+' ｜ '+mappingTarget(q)+' ｜ '+mappingStatus(q);return option;}));
 picker.value=mappingPositionId||'';
 for(const box of $('mapping-options').children){const q=questions.find(q=>q.id===box.dataset.questionId);if(q)box.querySelector('.mapping-status').textContent=mappingStatus(q);}
}
function updateMappingBulk(){
 const q=mappingQuestions().find(q=>q.id===mappingActiveId),picker=$('mapping-bulk'),key=q?mappingKey(q):null;
 const value=key&&key===mappingBulkKey?picker.value:'';mappingBulkKey=key;
 picker.replaceChildren(...['',...(q?.choices||[])].map(meaning=>{const option=document.createElement('option');option.value=meaning;option.textContent=mappingMeaning(meaning)||(q?'回答を選択':'質問を選択してください');return option;}));picker.value=(q?.choices||[]).includes(value)?value:'';
 $('mapping-bulk-label').textContent=q?mappingTarget(q)+'：同じ確認対象の未回答行への入力補助':'同じ確認対象の未回答行への入力補助';
 $('mapping-bulk-target').textContent=q?'対象：'+q.scope.supplierId+' / '+q.scope.documentKind+' / '+mappingTarget(q)+'（同じ列構成・同じ種類・同じ対象の質問だけ）':'原典が表示された質問を選択すると、その質問の選択肢を表示します。';
 picker.disabled=mappingBlocked()||!mappingReady||!q;
}
function updateMappingNavigation(){
 updateMappingBulk();
 const questions=mappingQuestions(),index=questions.findIndex(q=>q.id===mappingPositionId),locked=mappingBlocked()||!mappingReady;
 $('mapping-question').disabled=locked||!questions.length;
 $('mapping-prev').disabled=locked||index<=0;
 $('mapping-next').disabled=locked||index>=questions.length-1||!questions.length;
 $('mapping-unanswered').disabled=locked||!questions.some(q=>!mappingValue(q));
 $('mapping-fill').disabled=locked||!mappingActiveId;
 for(const box of $('mapping-options').children){
  const selected=box.dataset.questionId===mappingActiveId;box.classList.toggle('mapping-current',selected);
  if(selected)box.setAttribute('aria-current','true');else box.removeAttribute('aria-current');
  box.querySelector('select').disabled=locked;box.querySelector('button').disabled=locked;
 }
}
function syncMappingReference(){
 if(!mappingQuestions().length)return;
 const reference=window.originalRenderer.reference();
 const matches=q=>q.sheet===reference?.sheet&&q.row===reference?.row;
 const match=mappingQuestions().find(q=>q.id===mappingPositionId&&matches(q))||mappingQuestions().find(matches);
 mappingActiveId=match?.id||null;
 if(match){mappingPositionId=match.id;$('mapping-question').value=match.id;$('mapping-position').textContent='原典と質問 '+(mappingQuestions().indexOf(match)+1)+' / '+mappingQuestions().length+'：'+match.sheet+' 行'+match.row+' / '+match.sku+' ／ '+mappingTarget(match)+' ／ '+mappingStatus(match);}
 else if(!mappingNavigating)$('mapping-position').textContent=$('render-original').disabled?'原典を描画中です。表示完了後に質問を選択できます。':'疑義の行の原典が未表示です。質問を選んで原典を表示してください。';
 updateMappingNavigation();
}
async function focusMapping(questionId,focusInput=true){
 if(mappingBlocked()||!mappingReady)return;
 const q=mappingQuestions().find(q=>q.id===questionId);if(!q)return;
 mappingNavigating=true;mappingActiveId=null;mappingPositionId=q.id;$('mapping-question').value=q.id;
 $('mapping-position').textContent=q.sheet+' 行'+q.row+' の原典を表示しています…';updateMappingNavigation();
 try{
  await window.originalRenderer.selectReference({sheet:q.sheet,row:q.row});
  const reference=window.originalRenderer.reference();
  if(reference?.sheet!==q.sheet||reference?.row!==q.row)throw Error('選択した質問の原典を表示できませんでした。原典の状態を確認し、質問を選び直してください。');
  syncMappingReference();
  const box=[...$('mapping-options').children].find(box=>box.dataset.questionId===q.id);
  box?.scrollIntoView({block:'nearest',behavior:'instant'});
  // Enable first: focusing a disabled select does not move keyboard focus.
  mappingNavigating=false;updateMappingNavigation();
  if(focusInput)box?.querySelector('select').focus({preventScroll:true});
 }catch(e){mappingActiveId=null;$('mapping-position').textContent=e.message;}
 finally{mappingNavigating=false;updateMappingNavigation();}
}
$('mapping-question').onchange=()=>focusMapping($('mapping-question').value);
$('mapping-prev').onclick=()=>{const rows=mappingQuestions(),index=rows.findIndex(q=>q.id===mappingPositionId);if(index>0)focusMapping(rows[index-1].id);};
$('mapping-next').onclick=()=>{const rows=mappingQuestions(),index=rows.findIndex(q=>q.id===mappingPositionId);if(index<rows.length-1)focusMapping(rows[index+1].id);};
$('mapping-unanswered').onclick=()=>{const rows=mappingQuestions(),index=rows.findIndex(q=>q.id===mappingPositionId);for(let offset=1;offset<=rows.length;offset++){const q=rows[(index+offset)%rows.length];if(!mappingValue(q)){focusMapping(q.id);break;}}};
new MutationObserver(()=>{if(mappingReady)syncMappingReference();}).observe($('render-location'),{childList:true,characterData:true,subtree:true});
new MutationObserver(()=>updateMappingNavigation()).observe($('render-original'),{attributes:true,attributeFilter:['disabled']});

async function loadReuseRules(){
 const version=++reuseLoadVersion;reuseRules=null;reuseRulesError=false;renderReuse();nextState();
 try{const rules=await get('/api/import/rules');if(!Array.isArray(rules))throw Error('再利用状態を確認できません。');if(version!==reuseLoadVersion)return;reuseRules=rules;}
 catch{if(version!==reuseLoadVersion)return;reuseRulesError=true;}
 renderReuse();nextState();
}
function renderReuse(){
 const groups=new Map();const questionsForReuse=review.result?.questions?.length?review.result.questions:(review.mappingResolutions||[]).map(r=>r.question);for(const q of questionsForReuse){const key=mappingKey(q);if(!groups.has(key))groups.set(key,[]);groups.get(key).push(q);}
 $('reuse-panel').hidden=imageOnly||!groups.size;
 $('reuse-options').replaceChildren(...[...groups].map(([key,questions])=>{
  const row=document.createElement('div'),text=document.createElement('p'),button=document.createElement('button');
  const resolutions=questions.map(q=>(review.mappingResolutions||[]).find(r=>r.question.id===q.id));
  const ready=resolutions.every(Boolean)&&new Set(resolutions.map(r=>r?.meaning)).size===1;
  text.textContent=mappingTarget(questions[0])+' / '+questions.length+'行：'+(ready?'保存済み回答 '+mappingMeaning(resolutions[0].meaning):'同じ確認対象の全行を同じ回答で保存すると再利用できます。');row.append(text);
  if(!ready)return row;
  if(!reuseRules){text.textContent+=reuseRulesError?' ／再利用状態は未確認です。再読込して確認してください。登録は行いません。':' ／再利用状態を確認中です。';return row;}
  const matches=reuseRules.filter(r=>mappingKey(r)===key),rule=matches[0];
  if(matches.length>1){text.textContent+=' ／同じ確認対象に複数のルールがあります。状態未確認のため登録できません。';return row;}
  if(rule&&!rule.revoked&&rule.meaning===resolutions[0].meaning){text.textContent+=' ／再利用保存済み（有効・同じ回答）';return row;}
  text.textContent+=rule?.revoked?' ／再利用設定は取消済みです。':rule?' ／有効な設定との相違：'+mappingMeaning(rule.meaning):' ／再利用設定は未登録です。';
  button.type='button';button.textContent=mappingTarget(questions[0])+(rule?.revoked?'の保存済み回答を明示的に再登録する':rule?'の設定を保存済み回答に更新する':'の保存済み回答を次回も使う');button.onclick=()=>publishReuse(key,resolutions,rule);row.append(button);return row;
 }));
}
async function publishReuse(key,resolutions,displayedRule){
 if(busy||edited||draft||unconfirmed||['Unknown','Running'].includes(review.state))return;
 busy=true;nextState();let request=null;
 const matchesReceipt=receipt=>receipt?.operationId===request.operationId&&receipt.expectedVersion===request.expectedVersion&&receipt.rule?.id===request.ruleId&&receipt.rule?.version===request.expectedVersion+1&&receipt.rule?.answerId===id&&receipt.rule?.answerVersion===request.answerVersion&&receipt.rule?.sourceHash===request.hash&&receipt.rule?.meaning===resolutions[0].meaning&&mappingKey(receipt.rule)===key;
 try{
  const rules=await get('/api/import/rules');const matches=rules.filter(r=>mappingKey(r)===key);
  reuseRules=rules;reuseRulesError=false;renderReuse();nextState();
  if(matches.length>1)throw Error('同じ確認対象に複数の再利用ルールがあります。照合するまで保存しません。');
  const current=matches[0];
  if(current&&!current.revoked&&current.meaning===resolutions[0].meaning){$('reuse-status').textContent='有効な同じ回答の再利用設定を確認しました。重複登録は行いません。';return;}
  if(current?.id!==displayedRule?.id||current?.version!==displayedRule?.version||current?.revoked!==displayedRule?.revoked||current?.meaning!==displayedRule?.meaning){$('reuse-status').textContent='再利用状態が変わりました。最新の表示を確認してから操作してください。登録は行っていません。';return;}
  const resolution=resolutions[0],answer=[...review.history].reverse().find(e=>e.method==='Human'&&e.action==='mapping'&&e.mappingResolutions?.some(r=>r.question.id===resolution.question.id));
  if(!answer)throw Error('再利用できる本人の保存済み回答がありません。');
  request={operationId:crypto.randomUUID(),version:review.version,hash:review.hash,answerVersion:answer.version,questionId:resolution.question.id,ruleId:matches[0]?.id||crypto.randomUUID(),expectedVersion:matches[0]?.version||0};
  $('reuse-status').textContent='再利用ルールを保存しています。';
  const response=await fetch(base+'/reuse',{method:'POST',headers:{'Content-Type':'application/json','X-Catalog-Action':'local-demo'},body:JSON.stringify(request)});
  const receipt=await response.json();if(!response.ok)throw Error(receipt.error||'再利用保存を確定できません。');if(!matchesReceipt(receipt))throw Error('保存結果が要求と一致しません。');
  const confirmed=await get('/api/import/rules/operations/'+encodeURIComponent(request.operationId));if(!matchesReceipt(confirmed))throw Error('操作記録が要求と一致しません。');
  await loadReuseRules();$('reuse-status').textContent='この仕入先・同じ帳票の'+mappingTarget(resolutions[0].question)+'を次回も使う設定を保存しました。';renderReuse();
 }catch(e){
  $('reuse-status').textContent=e.message;
  if(!request){reuseRules=null;reuseRulesError=true;renderReuse();}
  if(request){try{const receipt=await get('/api/import/rules/operations/'+encodeURIComponent(request.operationId));if(!matchesReceipt(receipt))throw Error('結果の不一致');await loadReuseRules();$('reuse-status').textContent='操作記録を照合し、再利用ルールの保存を確認しました。';}
   catch{unconfirmed=true;$('reuse-status').textContent+=' 保存結果は未確認です。自動再送はせず、取込再開・次の資料への移動を停止しました。操作ID：'+request.operationId;}}
 }finally{busy=false;nextState();}
}

window.addEventListener('beforeunload',e=>{if(busy||edited||draft||unconfirmed){e.preventDefault();e.returnValue='';}});
(async()=>{try{if(!id)throw Error('対象資料が指定されていません。一覧から選択してください。');review=await get(base);history();if(imageOnly){document.querySelector('h1').textContent='原典画像の確認';document.querySelector('nav').hidden=true;$('review-layout').classList.add('image-only');await source();}else if(review.canResume){await source();summary();}else if(review.canAnswer)await source();else {await source();result();}}catch(e){$('notice').textContent=e.message;}})();
})();
