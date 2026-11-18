(() => {
 'use strict';
 const key='lips-source-review-answers/v1', rows=[...document.querySelectorAll('tbody tr[data-item-id]')];
 const notice=document.getElementById('answer-notice');
 let loaded=null, state={schema:key,items:{}};
 const controls=document.createElement('div');
 controls.innerHTML='<label>表示 <select id="answers-filter"><option value="pending">未回答・保留</option><option value="all">全件（回答済み・履歴を含む）</option></select></label><p id="answers-count" role="status"></p>';
 notice.after(controls);
 const filter=controls.querySelector('select');
 function dirty(r){const a=state.items[r.dataset.itemId];return r.querySelector('textarea').value!==(a?.text||'')||r.querySelector('select').value!==(a?.choice||'未入力');}
 function filterRows(){let answered=0,held=0,drafts=0,visible=0;
  for(const r of rows){const a=state.items[r.dataset.itemId],draft=dirty(r);if(a?.choice==='回答')answered++;if(a?.choice==='保留')held++;if(draft)drafts++;
   r.hidden=filter.value==='pending'&&a?.choice==='回答'&&!draft;if(!r.hidden)visible++;
   const history=r.querySelector('.answer-history');history.textContent='';
   const summary=document.createElement('summary');summary.textContent='保存履歴';history.append(summary);
   for(const h of [...(a?.history||[]),...(a?[a]:[])]){const p=document.createElement('p');p.textContent=`第${h.version}版 ${h.at} ${h.choice}：${h.text}`;history.append(p);}
  }
  controls.querySelector('#answers-count').textContent=`全${rows.length}件／未回答${rows.length-answered-held}件／保留${held}件／回答済み${answered}件／未保存変更${drafts}件／表示${visible}件`;
 }
 filter.onchange=filterRows;
 const snapshot=()=>localStorage.getItem(key);
 function read(){const raw=snapshot();const value=raw===null?{schema:key,items:{}}:JSON.parse(raw);
  if(value.schema!==key||!value.items||typeof value.items!=='object'||Array.isArray(value.items))throw Error('保存形式が一致しません。');
  for(const r of rows){const a=value.items[r.dataset.itemId];if(a&&(a.binding!==r.dataset.binding||typeof a.text!=='string'||!['未入力','回答','保留'].includes(a.choice)||!Number.isInteger(a.version)))throw Error('項目と保存内容の対応が一致しません。');}
  return {raw,value};
 }
 function render(){for(const r of rows){const a=state.items[r.dataset.itemId];r.querySelector('textarea').value=a?.text||'';r.querySelector('select').value=a?.choice||'未入力';r.querySelector('[role=status]').textContent=a?`保存済み 第${a.version}版 ${a.at}`:'未入力・未保存';}filterRows();}
 function load(){try{const data=read();loaded=data.raw;state=data.value;render();notice.textContent='再読込しました。保存先はこのPC・このブラウザーです。';}catch(e){notice.textContent='読込失敗：'+e.message+' 保存内容は上書きしません。';loaded=undefined;}}
 for(const r of rows){const cell=document.createElement('td');cell.innerHTML='<label>回答状態<select><option>未入力</option><option>回答</option><option>保留</option></select></label><label>回答・判断根拠<textarea rows="4" maxlength="1800"></textarea></label><button type="button">この回答を保存</button><p role="status"></p>';
  r.append(cell);const button=cell.querySelector('button'),select=cell.querySelector('select'),input=cell.querySelector('textarea'),status=cell.querySelector('[role=status]');
  const history=document.createElement('details');history.className='answer-history';cell.append(history);
  select.setAttribute('aria-label',r.dataset.itemId+' 回答状態');input.setAttribute('aria-label',r.dataset.itemId+' 回答・判断根拠');
  for(const e of [select,input])e.addEventListener('input',()=>{status.textContent='未保存の変更があります。';filterRows();});
  button.onclick=()=>{if(button.disabled)return;button.disabled=true;
   try{if(loaded===undefined)throw Error('先に保存データを再読込してください。');const choice=select.value,text=input.value.trim();
    if(choice==='未入力'&&text)throw Error('回答または保留を選択してください。');
    if(choice!=='未入力'&&!text)throw Error('回答・判断根拠を入力してください。');
    if(snapshot()!==loaded)throw Error('別画面で保存が更新されました。入力を控えて再読込してください。');
    const old=state.items[r.dataset.itemId];
    if((!old&&choice==='未入力'&&!text)||(old?.choice===choice&&old?.text===text)){status.textContent=old?'保存済み（変更なし・重複保存なし）':'未入力・保存対象なし';return;}
    const next=JSON.parse(JSON.stringify(state));next.items[r.dataset.itemId]={binding:r.dataset.binding,choice,text,version:(old?.version||0)+1,at:new Date().toISOString(),history:[...(old?.history||[]),...(old?[{choice:old.choice,text:old.text,version:old.version,at:old.at}]:[])]};
    const encoded=JSON.stringify(next);localStorage.setItem(key,encoded);
    if(snapshot()!==encoded){loaded=undefined;throw Error('保存結果を照合できません。再保存せず再読込してください。');}
    state=next;loaded=encoded;status.textContent=`保存済み 第${next.items[r.dataset.itemId].version}版（再取得一致）`;
    filterRows();notice.textContent='回答を保存・再取得しました。回答済みは「全件」で再確認・編集できます。';
   }catch(e){status.textContent='保存失敗／未確認：'+e.message;}finally{button.disabled=false;}
  };
 }
 document.getElementById('answers-reload').onclick=()=>{if(rows.some(r=>r.querySelector('textarea').value!==(state.items[r.dataset.itemId]?.text||'')||r.querySelector('select').value!==(state.items[r.dataset.itemId]?.choice||'未入力'))&&!confirm('未保存の入力を破棄して、保存済み回答を再読込しますか？'))return;load();};
 document.getElementById('answers-export').onclick=()=>{try{const data=read();const blob=new Blob([JSON.stringify(data.value,null,2)],{type:'application/json'}),url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download='LIPS-source-review-answers.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);notice.textContent='保存済み回答を書き出しました。未保存の入力は含みません。';}catch(e){notice.textContent='書出し失敗：'+e.message;}};
 window.addEventListener('beforeunload',e=>{if(rows.some(r=>r.querySelector('textarea').value!==(state.items[r.dataset.itemId]?.text||'')||r.querySelector('select').value!==(state.items[r.dataset.itemId]?.choice||'未入力'))){e.preventDefault();e.returnValue='';}});
 load();
})();
