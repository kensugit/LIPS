(() => {
  'use strict';
  const byId = id => document.getElementById(id);
  const picker = byId('folder-files'), start = byId('folder-start'), stop = byId('folder-stop');
  const selectAll=byId('folder-select-all'), clearAll=byId('folder-clear-all'), selection=byId('folder-selection');
  const download = byId('folder-download'), summary = byId('folder-summary'), tbody = byId('folder-rows');
  const labels = {Pending:'未処理', Processing:'処理中', Success:'成功', Duplicate:'重複・スキップ', Partial:'一部保存・保留あり', Held:'保留', Unsupported:'未対応', Failed:'失敗'};
  let rows = [], running = false, stopping = false, unresolvedSave = false;
  const unresolvedNotice = '保存結果が不明なため、このページの一括取込を停止しました。原典・回答履歴で保存結果を照合し、確認後にページを再読み込みしてください。未処理ファイルも確認前に取り込まないでください。';
  function blockUnresolvedSave() { unresolvedSave=true; stopping=true; }
  function text(tag, value) { const e = document.createElement(tag); e.textContent = value; return e; }
  function refresh() {
    const counts = Object.keys(labels).map(k => `${labels[k]} ${rows.filter(r=>r.status===k).length}件`).join(' / ');
    summary.textContent = `${rows.length}ファイル：${counts}${unresolvedSave?' / '+unresolvedNotice:''}`;
    const pending=rows.filter(r=>r.status==='Pending'), selected=pending.filter(r=>r.selected);
    selection.textContent=`取込対象 ${selected.length}件 / 未処理 ${pending.length}件`;
    start.disabled = unresolvedSave || running || !selected.length; picker.disabled = unresolvedSave || running;
    selectAll.disabled=unresolvedSave||running||!pending.some(r=>!r.selected);
    clearAll.disabled=unresolvedSave||running||!selected.length;
    for(const row of rows){row.selectionInput.checked=row.selected;row.selectionInput.disabled=unresolvedSave||running||row.status!=='Pending';}
    stop.disabled = !running || stopping; download.disabled = !rows.length || running;
  }
  function update(row, result) {
    Object.assign(row, result);if(row.status==='Success'&&row.alreadyImported)row.status='Duplicate'; row.statusCell.textContent = labels[row.status] || row.status;
    const guidance=window.importGuidance(row);row.messageCell.textContent = guidance.cause+(guidance.remedy?' 対応方法：'+guidance.remedy:'');
    row.hashCell.replaceChildren();if(row.hash){row.hashCell.append(text('div',row.alreadyImported?'登録済み原本と一致・スキップ':'アップロード後に計算済み'));const details=document.createElement('details');details.append(text('summary','SHA-256 '+row.hash.slice(0,12)+'…'+row.hash.slice(-8)),text('code',row.hash));row.hashCell.append(details);}else row.hashCell.textContent=row.status==='Pending'?'アップロード後に確認':'未確認';
    row.hashCell.style.overflowWrap='anywhere';row.hashCell.style.maxWidth='18rem'; row.supplierCell.textContent = row.supplierCode || '未確定';
    if(row.reviewId){const link=text('a','原典・回答履歴を確認');link.href='/intake-review.html?id='+encodeURIComponent(row.reviewId);row.messageCell.append(document.createElement('br'),link);}
    row.countCell.textContent = row.products == null ? '' : `${row.products}件${row.alreadyImported?'（取込済み）':''}`;
    if (row.observedAt) row.dateInput.value = row.observedAt;
    refresh();
  }
  picker.addEventListener('change', () => {
    if (running || unresolvedSave) return;
    rows = []; tbody.replaceChildren();
    const files = [...picker.files].sort((a,b)=>(a.webkitRelativePath||a.name).localeCompare(b.webkitRelativePath||b.name,'ja'));
    if (files.length > 1000 || files.reduce((n,f)=>n+f.size,0) > 500_000_000) {
      summary.textContent = '1回に1,000ファイル・合計500MBまで選択できます。フォルダーを分けてください。'; refreshButtons(); return;
    }
    for (const file of files) {
      const path = file.webkitRelativePath || file.name, tr = document.createElement('tr');
      const dateCell = document.createElement('td'), dateInput = document.createElement('input');
      dateInput.type='date'; dateInput.setAttribute('aria-label',`${path} の資料日付`); dateCell.append(dateInput);
      const selectionCell=document.createElement('td'), selectionInput=document.createElement('input');
      selectionInput.type='checkbox';selectionInput.checked=true;selectionInput.setAttribute('aria-label',`${path} を取り込む`);selectionCell.append(selectionInput);
      const row = {file, path, selected:true, selectionInput, status:'Pending', dateInput, statusCell:text('td','未処理'), messageCell:text('td',''), supplierCell:text('td','未確定'), countCell:text('td',''), hashCell:text('td','アップロード後に確認')};
      selectionInput.addEventListener('change',()=>{if(running||unresolvedSave||row.status!=='Pending'){refresh();return;}row.selected=selectionInput.checked;refresh();});
      tr.append(selectionCell,text('td',path), row.supplierCell, dateCell, row.statusCell, row.countCell, row.hashCell, row.messageCell); tbody.append(tr); rows.push(row);
      if (file.size===0 || file.size>20_000_000) update(row,{selected:false,status:'Failed',message:'空ファイル、または20MBを超えています。送信しません。'});
    }
    refresh();
  });
  function refreshButtons() { start.disabled=true; download.disabled=true; stop.disabled=true; selectAll.disabled=true;clearAll.disabled=true;selection.textContent='取込対象 0件'; }
  function setSelection(value){if(running||unresolvedSave)return;for(const row of rows)if(row.status==='Pending')row.selected=value;refresh();}
  selectAll.addEventListener('click',()=>setSelection(true));clearAll.addEventListener('click',()=>setSelection(false));
  stop.addEventListener('click',()=>{stopping=true;stop.disabled=true;summary.textContent='現在のファイルの結果を待って停止します。';});
  start.addEventListener('click', async () => {
    if (running || unresolvedSave) return;
    const queue=rows.filter(r=>r.status==='Pending'&&r.selected);if(!queue.length)return;
    running=true; stopping=false;
    rows.forEach(r=>r.dateInput.disabled=true); refresh();
    try {
      for (const row of queue) {
        if (stopping) break;
        if (row.status!=='Pending') continue;
        update(row,{status:'Processing',message:'サーバーで確認しています。'});
        const form = new FormData(); form.append('file',row.file,row.file.name); form.append('relativePath',row.path); form.append('observedAt',row.dateInput.value);
        try {
          const response = await fetch('/api/import/folder',{method:'POST',headers:{'X-Catalog-Action':'local-demo'},body:form});
          if (!response.ok) {
            if (response.status===403 || response.status===404) {update(row,{status:'Failed',message:'取込が許可されていないか、このサーバーは未対応です。'});stopping=true;}
            else if (response.status===413) update(row,{status:'Failed',message:'送信上限を超えました。'});
            else {blockUnresolvedSave();update(row,{status:'Held',message:'サーバーの結果が不明です。履歴を確認してください。自動再試行しません。'});}
          } else {
            const result = await response.json();
            if (!['Success','Partial','Held','Unsupported','Failed'].includes(result.status) || result.path!==row.path) throw new Error('Invalid receipt');
            if (result.stopQueue === true) blockUnresolvedSave();
            update(row,result);
          }
        } catch {
          blockUnresolvedSave();update(row,{status:'Held',message:'通信が中断し結果が不明です。取込履歴を確認してください。自動再試行しません。'});
        }
      }
    } finally {
      window.dispatchEvent(new Event('folder-import-finished')); running=false; rows.forEach(r=>r.dateInput.disabled=r.status!=='Pending'); refresh();
    }
  });
  download.addEventListener('click',()=>{
    const report=rows.map(({path,status,message,supplierCode,observedAt,products,alreadyImported,sourceDocumentId,stopQueue,hash,failureCode,remedy,dateInput})=>({path,status,message,supplierCode,observedAt:observedAt||dateInput.value,products,alreadyImported,sourceDocumentId,stopQueue,hash,failureCode,remedy}));
    const url=URL.createObjectURL(new Blob([JSON.stringify(report,null,2)],{type:'application/json'}));
    const a=document.createElement('a');a.href=url;a.download='folder-import-results.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
  });
  window.addEventListener('beforeunload',event=>{if(running){event.preventDefault();event.returnValue='';}});
  window.addEventListener('intake-review-updated',event=>{const result=event.detail;for(const row of rows)if(row.reviewId===result.reviewId)update(row,result);});
  refresh();
})();
