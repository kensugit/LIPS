(() => {
 const section=document.createElement('section');section.id='intake-reviews';section.hidden=true;
 section.innerHTML='<h2>確認が必要な資料</h2><p>資料を選ぶと、原典確認から回答まで順番に進められます（最新100件）。</p><button type="button">再取得</button><p role="status"></p><div></div>';
 document.getElementById('import-panel').prepend(section);
 const states={Partial:'一部保存・保留あり',Held:'要確認',Ready:'回答確定・再開可能',Running:'処理中・結果照合が必要',Unknown:'保存結果不明・再開禁止',Completed:'取込成功',Created:'受付済み'};
 async function load(){try{const res=await fetch('/api/import/reviews');if(res.status===404)return;if(!res.ok)throw Error();const rows=await res.json();section.hidden=false;const list=section.querySelector('div');list.replaceChildren();const done=document.createElement('details');const summary=document.createElement('summary');summary.textContent='完了した資料';done.append(summary);
 for(const r of rows){const item=document.createElement('p'),a=document.createElement('a');a.href='/intake-review.html?id='+encodeURIComponent(r.id);a.textContent=r.path+' — '+(states[r.state]||r.state);item.append(a);const g=window.importGuidance(r.result);if(r.state!=='Completed'){const detail=document.createElement('span');detail.textContent=' ／ 原因：'+g.cause+' ／ 対応方法：'+g.remedy;item.append(detail);}else if(r.result?.alreadyImported){item.append(document.createTextNode(' ／ 重複・スキップ'));}(r.state==='Completed'?done:list).append(item);}list.append(done);section.querySelector('[role=status]').textContent=rows.length?'':'確認対象はありません。';}catch{section.hidden=false;section.querySelector('[role=status]').textContent='確認対象を取得できません。';}}
 section.querySelector('button').onclick=load;window.addEventListener('folder-import-finished',load);load();
})();
