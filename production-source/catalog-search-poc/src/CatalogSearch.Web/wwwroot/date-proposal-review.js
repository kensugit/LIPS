(()=>{'use strict';const $=id=>document.getElementById(id);let busy=false;
async function request(){const r=await fetch('/api/date-proposal-review/request');if(!r.ok)throw Error('確認機能が有効な隔離環境で開いてください。');return r.json();}
function reset(){ $('result').hidden=true;$('prepare').hidden=false;$('candidates').replaceChildren();}
function line(parent,text){const p=document.createElement('p');p.textContent=text;parent.append(p);}
async function run(getPayload){if(busy)return;busy=true;reset();$('status').textContent='応答を検証しています…';for(const b of document.querySelectorAll('button'))b.disabled=true;try{
const payload=await getPayload();const r=await fetch('/api/date-proposal-review/validate',{method:'POST',headers:{'Content-Type':'application/json','X-Catalog-Action':'local-demo'},body:payload});const v=await r.json();if(!r.ok)throw Error(v.error||'検証できませんでした。');
line($('candidates'),`生成元：${v.response.provider} / ${v.response.model}　回答版：${v.response.reviewVersion}`);
if(v.response.outcome==='Unknown')line($('candidates'),'候補なし：原典を確認し、本人による判断が必要です。');
for(const c of v.response.candidates){const a=document.createElement('article');line(a,`日付候補：${c.date}`);line(a,`理由：${c.reason}`);for(const id of c.evidenceIds){const e=v.excerpts.find(e=>e.id===id);line(a,`原典の位置：${e.location}`);line(a,`原典の記載：${e.text}`);} $('candidates').append(a);}
$('prepare').hidden=true;$('result').hidden=false;$('status').textContent='形式・原典識別子・版・根拠の照合に合格。回答・商品は未保存です。';
}catch(e){reset();$('status').textContent=e.message;}finally{busy=false;for(const b of document.querySelectorAll('button'))b.disabled=false;}}
$('sample').onclick=()=>run(async()=>{const q=await request();const {excerpts,...identity}=q;return JSON.stringify({...identity,provider:'offline-fixture',model:'synthetic',outcome:'Candidates',candidates:[{date:'2026-10-01',reason:'原典に記載された在庫日',evidenceIds:['e1']}]});});
$('validate').onclick=()=>run(async()=>{const file=$('response').files[0];if(!file)throw Error('応答JSONを選択してください。');if(file.size>50000)throw Error('応答ファイルが大きすぎます。');return file.text();});
$('response').onchange=()=>{reset();$('status').textContent='選択した応答はまだ検証していません。';};
$('request').onclick=async()=>{try{const q=await request(),url=URL.createObjectURL(new Blob([JSON.stringify(q,null,2)],{type:'application/json'})),a=document.createElement('a');a.href=url;a.download='proposal-request-synthetic.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}catch(e){$('status').textContent=e.message;}};
$('back').onclick=()=>{reset();$('status').textContent='次の応答を選択してください。';};
})();
