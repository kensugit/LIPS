"""Prepare a reviewable patch against the catalog application's current checkout."""
from pathlib import Path
import difflib

root = Path(__file__).resolve().parents[1]
source = Path('C:/Users/kensu/OpenAI/Codex/iWSET/iWSET/商品情報DB/product-catalog-poc-20260917/catalog-search-poc')
stage = root / 'artifacts/search-csv/catalog-search-poc'
changes = {
 'src/CatalogSearch.Web/wwwroot/catalog.css': [('#search-results{min-width:1160px}#search-results td:first-child{min-width:230px;max-width:340px}#search-results td:nth-child(2){min-width:140px;max-width:200px}#search-results td:nth-child(4){min-width:120px}', '#search-results{min-width:1300px}#search-results td:first-child{min-width:130px;max-width:180px;overflow-wrap:anywhere}#search-results td:nth-child(2){min-width:230px;max-width:340px}#search-results td:nth-child(3){min-width:140px;max-width:200px}#search-results td:nth-child(5){min-width:120px}')],
 'tests/CatalogSearch.Domain.Tests/SearchReviewTests.cs': [('Assert.NotEmpty(await query.SearchAsync(new(Text:"特別　pINot",PriceMin:4000,PriceMax:4500)));', 'var matches = await query.SearchAsync(new(Text:"特別　pINot",PriceMin:4000,PriceMax:4500));\n        Assert.NotEmpty(matches);\n        Assert.All(matches.Where(x => x.SourceDocumentId == first.SourceDocumentId), x => Assert.Equal(code, x.SupplierName));')],
 'CODEX-HANDOFF.md': [('## Current status (2026-09-21, publication review)', '## Current status (2026-09-22, search CSV)\n\n検索一覧に取引先名と並べ替えを追加。CSVは現在取得済みの検索結果全体（最大2,000件）を並び順どおり出力。UTF-8 BOM、引用符・改行エスケープ、数式文字列対策、空欄と0の区別に対応。上限到達を明示し、取得中・0件・取得失敗時は出力不可。DB/schema変更なし。LIPS tests/verify-search-csv.cjsでブラウザの実ダウンロードを検証。本番未反映。\n\n## Previous status (2026-09-21, publication review)'), ('## Next work — execute in this order', '## Next work — execute in this order\n\n取引先表示・CSVの変更を次の配布候補に含め、本番適用前に実データで検索・CSV件数を照合し、パッケージ承認を取得する。')],
 'src/CatalogSearch.Application/ImportService.cs': [('public string PriceNote', 'public string SupplierName { get; init; } = "";\n    public string PriceNote')],
 'src/CatalogSearch.Infrastructure/CatalogQuery.cs': [('join e in db.SourceEvidence', 'join supplier in db.Suppliers.AsNoTracking() on p.SupplierId equals supplier.Id\n            join e in db.SourceEvidence'), ('p.Country, p.Region, p.ProductType), e.ExtractedRowJson', 'p.Country, p.Region, p.ProductType) { SupplierName = supplier.Name }, e.ExtractedRowJson')],
 'src/CatalogSearch.Web/wwwroot/index.html': [('<script defer src="/catalog.js">', '<script defer src="/catalog-csv.js"></script><script defer src="/catalog.js">'), ('<span>希望小売価格 / 在庫表の記載時点</span>', '<div><span>希望小売価格 / 在庫表の記載時点</span> <button type="button" id="download-csv" disabled>検索結果をCSVダウンロード</button></div>'), ('<p id="interpreted"', '<p id="csv-status" class="hint" role="status">検索結果全体を現在の並び順でCSV出力します（最大2,000件）。</p><p id="interpreted"'), ('<thead><tr>', '<thead><tr><th scope="col" aria-sort="none"><button type="button" class="sort-heading" data-sort="supplier">取引先<span aria-hidden="true"> ↕</span></button></th>')],
 'src/CatalogSearch.Web/wwwroot/catalog-sort.js': [('const values={', 'const values={supplier:r=>r.supplierName,')],
 'src/CatalogSearch.Web/wwwroot/catalog.js': [('const size=50;', 'const size=50;let searchPending=false;'), ('tr.append(name,producer', "tr.append(el('td',r.supplierName||'記載なし'),name,producer"), ("const body=$('rows');", "$('download-csv').disabled=searchPending||data.length===0;const body=$('rows');"), ("async function search(){error('');", "async function search(){searchPending=true;$('download-csv').disabled=true;$('csv-status').textContent='検索結果全体を現在の並び順でCSV出力します（最大2,000件）。';error('');"), ("error('価格の下限は上限以下にしてください。');return;", "error('価格の下限は上限以下にしてください。');searchPending=false;return;"), ("data=result.rows;", "data=result.rows;$('csv-status').textContent=result.capped?'検索上限に達したため、CSVも先頭2,000件のみです。条件を絞ると残りの商品も検索できます。':'検索結果 '+data.length.toLocaleString()+'件を現在の並び順でCSV出力します。';"), ("finally{$('busy').hidden=true;$('search').inert=false}", "finally{searchPending=false;$('download-csv').disabled=data.length===0;$('busy').hidden=true;$('search').inert=false}"), ("$('search').addEventListener('submit'", "$('download-csv').addEventListener('click',()=>{if(searchPending||!data.length)return;const blob=new Blob([CatalogCsv.serialize(data)],{type:'text/csv;charset=utf-8'});const url=URL.createObjectURL(blob);const a=el('a');a.href=url;a.download='検索結果_'+new Date().toISOString().replace(/[:.]/g,'-')+'.csv';document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),1000)});\n$('search').addEventListener('submit'")],
}
patch=[]
for name, replacements in changes.items():
    old=(source/name).read_text(encoding='utf-8')
    new=old
    for before,after in replacements:
        assert new.count(before)==1, (name,before,new.count(before))
        new=new.replace(before,after)
    (stage/name).write_text(new,encoding='utf-8',newline='\n')
    patch.extend(difflib.unified_diff(old.splitlines(True),new.splitlines(True),fromfile='a/catalog-search-poc/'+name,tofile='b/catalog-search-poc/'+name))
name='src/CatalogSearch.Web/wwwroot/catalog-csv.js'
new=(root/'tools/catalog-csv.js').read_text(encoding='utf-8')
(stage/name).write_text(new,encoding='utf-8',newline='\n')
patch.extend(difflib.unified_diff([],new.splitlines(True),fromfile='/dev/null',tofile='b/catalog-search-poc/'+name))
(root/'tools/patches/search-csv.patch').write_text(''.join(patch),encoding='utf-8',newline='\n')
print('Prepared search-csv.patch and staged application.')
