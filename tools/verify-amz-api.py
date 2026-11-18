import argparse,json,urllib.request
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('base');p.add_argument('output',type=Path);a=p.parse_args()
root=Path(__file__).resolve().parents[1]
b=json.loads((root/'catalogs/AMZ.StockList20260925.catalog.json').read_text('utf-8'))
with urllib.request.urlopen(a.base.rstrip('/')+'/api/search?text=AMZ',timeout=20) as r: result=json.load(r)
assert result['capped'] is False
actual={r['sku']:r for r in result['rows'] if r['supplierName']=='AMZ'}
assert set(actual)=={r['SupplierProductCode'] for r in b['Rows']}
fields={'productNameJa':'ProductNameJa','productNameEn':'ProductNameEn','producerNameJa':'ProducerNameJa',
        'producerNameEn':'ProducerNameEn','vintageRaw':'VintageRaw','vintage':'Vintage','volumeMl':'VolumeMl',
        'referenceRetailPrice':'ReferenceRetailPrice','taxIncluded':'TaxIncluded','sourceSheet':'SourceSheet',
        'sourceRow':'SourceRow','country':'Country','region':'Region','productType':'ProductType'}
for row in b['Rows']:
    got=actual[row['SupplierProductCode']]
    for key,value in fields.items(): assert got[key]==row[value],(row['SupplierProductCode'],key)
    assert got['quantity']==row['Inventory']['Quantity'] and got['inventory']==row['Inventory']['Status']
proof=dict(verified=True,products=len(actual),capped=False,base=a.base,sourceSha256=b['SourceSha256'])
a.output.write_text(json.dumps(proof,indent=2),encoding='utf-8');print(json.dumps(proof))
