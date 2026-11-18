(() => {
    'use strict';
    const $ = id => document.getElementById(id);
    let base, info, rendered, query, selection, start, pendingReference, loading = false;
    let zoom = 1, manualZoom = false, previousHorizontal = 0;
    function keepTargetRowVisible() {
        const box = rendered?.kind === 'xlsx' ? rendered.highlightBox : null;
        if (!Array.isArray(box) || box.length !== 4 || !box.every(Number.isFinite)
            || box[0] < 0 || box[1] < 0 || box[2] > rendered.width || box[3] > rendered.height
            || box[2] <= box[0] || box[3] <= box[1]) return;
        const wrap = $('native-image-wrap'), top = box[1] * zoom, bottom = box[3] * zoom;
        // Only explicit zoom/render actions bring the row back. Manual scrolling is free.
        if (top < wrap.scrollTop || bottom > wrap.scrollTop + wrap.clientHeight)
            wrap.scrollTop = Math.max(0, top - Math.min(32, wrap.clientHeight / 4));
    }
    function setZoom(value, keepCenter = true) {
        if (!rendered) return;
        const wrap = $('native-image-wrap'), stage = $('native-stage');
        const cx = (wrap.scrollLeft + wrap.clientWidth / 2) / zoom;
        const cy = (wrap.scrollTop + wrap.clientHeight / 2) / zoom;
        zoom = Math.max(.1, Math.min(4, value));
        stage.style.width = `${rendered.width * zoom}px`;
        $('zoom-value').textContent = `${Math.round(zoom * 100)}%`;
        if (keepCenter) { wrap.scrollLeft = cx * zoom - wrap.clientWidth / 2; wrap.scrollTop = cy * zoom - wrap.clientHeight / 2; }
        else { wrap.scrollLeft = 0; wrap.scrollTop = 0; }
        keepTargetRowVisible();
    }
    $('zoom-in').onclick = () => { manualZoom = true; setZoom(zoom * 1.25); };
    $('zoom-out').onclick = () => { manualZoom = true; setZoom(zoom / 1.25); };
    $('zoom-actual').onclick = () => { manualZoom = true; setZoom(1); };
    $('zoom-fit').onclick = () => { manualZoom = false; setZoom($('native-image-wrap').clientWidth / rendered.width, false); };
    async function json(url) {
        const response = await fetch(url);
        const data = await response.json();
        if (!response.ok) throw Error(data.error || '原典画像の取得に失敗しました。');
        return data;
    }
    function location() {
        if (!rendered) return '';
        if (rendered.kind === 'xlsx') return `${rendered.sheet} / 行 ${rendered.row}（Excel原典画像）`;
        const box = selection ? ` / 範囲 ${[selection.x,selection.y,selection.right,selection.bottom].map(Math.round).join(',')} / ${selection.method}` : '';
        return `PDF ページ ${rendered.page}${box}（画像 ${rendered.width}×${rendered.height}）`;
    }
    function highlight() {
        const enabled = $('show-highlight').checked;
        if (!rendered) return;
        if (rendered.kind === 'xlsx') {
            $('native-image').src = `${base}/render-image?${query}&highlight=${enabled}`;
            $('native-overlay').hidden = true;
        } else {
            $('native-overlay').hidden = false;
            $('native-box').toggleAttribute('hidden', !enabled || !selection);
            if (selection) {
                const box = $('native-box');
                box.setAttribute('x', selection.x); box.setAttribute('y', selection.y);
                box.setAttribute('width', selection.right-selection.x);
                box.setAttribute('height', selection.bottom-selection.y);
            }
        }
        $('render-location').textContent = location();
    }
    async function render() {
        if (loading || !info) return;
        const sheet = Number($('native-sheet').value || 1), row = Number($('native-row').value), page = Number($('native-page').value);
        if (info.kind === 'xlsx' && !$('native-row').reportValidity()) return;
        if (info.kind === 'xlsx') {
            const s=info.sheets.find(s=>s.index===sheet);
            if (s.contentRows && !s.contentRows.includes(row)) {
                invalidate();
                $('preview-notice').textContent=`${row}行目は空白です。内容のある${s.firstContentRow}行目以降を選択してください。`;
                return;
            }
        }
        if (info.kind === 'pdf' && !$('native-page').reportValidity()) return;
        const previousLeft = rendered ? $('native-image-wrap').scrollLeft : previousHorizontal;
        loading = true; $('render-original').disabled = true;
        for (const id of ['native-sheet','native-row','native-page']) $(id).disabled=true;
        $('to-answer').disabled = true;
        $('native-image-wrap').hidden = true; rendered = null; selection = null;
        $('image-zoom').hidden = true;
        $('render-location').textContent = ''; $('preview-notice').textContent = '原典を描画しています…';
        const began = performance.now();
        const progress = setInterval(() => {
            $('preview-notice').textContent = `原典を描画しています（${Math.floor((performance.now()-began)/1000)}秒経過）。初めて表示する範囲は時間がかかります。そのままお待ちください。`;
        }, 1000);
        query = new URLSearchParams({sheet, row, page}).toString();
        try {
            const result = await json(`${base}/render?${query}`);
            const image = $('native-image');
            image.src = `${base}/render-image?${query}&highlight=${result.kind==='xlsx' && $('show-highlight').checked}`;
            await image.decode();
            rendered = result;
            $('native-overlay').setAttribute('viewBox', `0 0 ${result.width} ${result.height}`);
            $('native-image-wrap').hidden = false;
            $('image-zoom').hidden = false;
            setZoom(manualZoom && result.kind === 'xlsx' ? zoom : Math.min(1, $('native-image-wrap').clientWidth / result.width), false);
            if (manualZoom && result.kind === 'xlsx') $('native-image-wrap').scrollLeft = previousLeft;
            $('preview-notice').textContent = result.kind==='xlsx'
                ? ($('show-highlight').checked ? '枠が、指定した行です。前後の行も表示しています。' : '文字や数値のある位置を開きました。確認したい行が別にある場合は、行番号を変更してください。')
                : '元のPDFページを表示しています。行候補を選ぶか、画像上をドラッグして確認箇所を指定してください。';
            $('pdf-line-label').hidden = result.kind !== 'pdf';
            $('pdf-line').replaceChildren(new Option('画像上で対象範囲を指定', ''),
                ...result.lines.map((line,i) => new Option(`${i+1}: ${line.text.slice(0,120)}`, i)));
            $('native-help').textContent = result.kind === 'pdf'
                ? '行候補はPDFの文字位置から作成しています。業務上の対象行を自動確定していません。候補を選ぶか画像上をドラッグしてください。スキャンPDFも範囲指定できます。'
                : '画像内の文字・罫線は元ファイルのものです。画像部分をスクロールして確認できます。';
            highlight();
        } catch (e) { $('preview-notice').textContent = e.message + ' 自動再試行はしません。'; }
        finally {
            clearInterval(progress);
            loading = false; $('render-original').disabled = false; $('to-answer').disabled = false;
            for (const id of ['native-sheet','native-row','native-page']) $(id).disabled=false;
            if (pendingReference) { const next=pendingReference;pendingReference=null;await selectReference(next); }
        }
    }
    async function load(url, initialReference = null) {
        if (base === url && info) return;
        base = url;
        try {
            info = await json(base+'/render-info');
            const excel = info.kind === 'xlsx';
            $('native-controls').hidden = false;
            $('sheet-label').hidden = $('row-label').hidden = !excel;
            $('page-label').hidden = excel;
            $('preview-notice').textContent = info.notice;
            $('native-sheet').replaceChildren(...(info.sheets || []).filter(s=>!s.hidden).map(s=>new Option(s.name,s.index)));
            const chooseSheet = () => {
                const sheet = info.sheets.find(s=>s.index===Number($('native-sheet').value));
                $('native-row').max = sheet?.maxRow || 1;
                $('native-row').value = sheet?.firstContentRow || 1;
                $('show-highlight').checked=false;
                invalidate();
            };
            $('native-sheet').onchange=async()=>{chooseSheet();await render();};
            if (excel) {
                chooseSheet();
                const initialSheet=info.sheets.find(s=>s.name===initialReference?.sheet&&!s.hidden&&s.contentRows?.includes(initialReference.row));
                if(initialSheet){$('native-sheet').value=initialSheet.index;chooseSheet();$('native-row').value=initialReference.row;$('show-highlight').checked=true;}
                const requested=new URLSearchParams(window.location.search);
                const sheet=Number(requested.get('sheet')),row=Number(requested.get('row'));
                if (info.sheets.some(s=>s.index===sheet && !s.hidden && s.contentRows?.includes(row))) {
                    $('native-sheet').value=sheet;chooseSheet();$('native-row').value=row;$('show-highlight').checked=true;
                }
                await render();
            }
            else { $('native-page').max = info.pages; await render(); }
        } catch(e) { $('preview-notice').textContent = e.message; }
    }
    function invalidate() {
        if (rendered) previousHorizontal = $('native-image-wrap').scrollLeft;
        rendered=null;selection=null;
        $('native-image-wrap').hidden=true;$('pdf-line-label').hidden=true;
        $('image-zoom').hidden=true;
        $('render-location').textContent='';$('native-help').textContent='';
        $('preview-notice').textContent='シート・行またはページを指定し、「原典画像を表示」を押してください。';
    }
    $('native-row').oninput=()=>{$('show-highlight').checked=true;invalidate();};$('native-page').oninput=invalidate;
    $('render-original').onclick = render;
    $('show-highlight').onchange = highlight;
    $('pdf-line').onchange = () => {
        const value = $('pdf-line').value;
        selection = value === '' ? null : {...rendered.lines[Number(value)], method:'PDF文字位置候補を本人選択'};
        highlight();
        if (selection) {
            const wrap=$('native-image-wrap'), scale=$('native-image').clientWidth/rendered.width;
            wrap.scrollTop=Math.max(0,(selection.y+selection.bottom)*scale/2-wrap.clientHeight/2);
        }
    };
    function point(e) {
        const r=$('native-overlay').getBoundingClientRect();
        return {x:Math.max(0,Math.min(rendered.width,(e.clientX-r.left)*rendered.width/r.width)),
            y:Math.max(0,Math.min(rendered.height,(e.clientY-r.top)*rendered.height/r.height))};
    }
    $('native-overlay').onpointerdown = e => {
        if (!rendered || rendered.kind !== 'pdf') return;
        start=point(e);$('native-overlay').setPointerCapture(e.pointerId);
    };
    $('native-overlay').onpointermove = e => {
        if (!start) return;
        const end=point(e); selection={x:Math.min(start.x,end.x),y:Math.min(start.y,end.y),right:Math.max(start.x,end.x),bottom:Math.max(start.y,end.y),method:'本人の範囲指定'};
        $('show-highlight').checked=true;$('pdf-line').value='';highlight();
    };
    $('native-overlay').onpointerup = () => {start=null;};
    $('native-overlay').onpointercancel = () => {start=null;};
    function reference() {
        if (!rendered) return null;
        if (rendered.kind==='xlsx') return {kind:'xlsx',sheet:rendered.sheet,row:rendered.row};
        if (!selection) return null;
        return {kind:'pdf',page:rendered.page,x:selection.x,y:selection.y,right:selection.right,bottom:selection.bottom,width:rendered.width,height:rendered.height};
    }
    async function selectReference(source) {
        if (loading) { pendingReference=source;return; }
        if (!info || info.kind!=='xlsx' || !source) return;
        const sheet=info.sheets.find(s=>s.name===source.sheet&&!s.hidden);
        if (!sheet) return;
        $('native-sheet').value=sheet.index;$('native-row').max=sheet.maxRow;$('native-row').value=source.row;
        $('show-highlight').checked=true;invalidate();await render();
    }
    window.originalRenderer = {load, location, reference, selectReference};
})();
