(() => {
 let candidates=[],reviewIds=[],version=0;
 function add(){
  const ids=new Set([...reviewIds,...candidates.flatMap(c=>[c.excel.id,c.pdf.id])]);
  for(const anchor of document.querySelectorAll('a[href*="/intake-review.html?id="]')){
   const id=new URL(anchor.href).searchParams.get('id');if(!ids.has(id)||anchor.parentElement.querySelector('.exact-pair-link'))continue;
   const a=document.createElement('a');a.className='exact-pair-link';a.href='/orveaux-pair.html?review='+encodeURIComponent(id);a.textContent=' AOC 2024の固定1ペアを確認';anchor.parentElement.append(document.createElement('br'),a);
  }
  const current=new URLSearchParams(location.search).get('id');
  if(ids.has(current)&&!document.getElementById('exact-pair-current')){const p=document.createElement('p');p.id='exact-pair-current';const a=document.createElement('a');a.href='/orveaux-pair.html?review='+encodeURIComponent(current);a.textContent='AOC 2024の固定1ペアを確認（資料全体の取込ではありません）';p.append(a);document.querySelector('main').prepend(p);}
 }
 async function load(){const token=++version;try{const r=await fetch('/api/import/orveaux-pair/review-candidates');if(!r.ok)return;const data=await r.json();if(token!==version)return;candidates=data.candidates||[];reviewIds=[...(data.excelReviews||[]),...(data.pdfReviews||[])].map(r=>r.id);document.querySelectorAll('.exact-pair-link,#exact-pair-current').forEach(e=>e.remove());add();}catch{}}
 new MutationObserver(add).observe(document.body,{childList:true,subtree:true});window.addEventListener('folder-import-finished',load);load();
})();
