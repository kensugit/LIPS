"""Source-specific XLS/XLSX readers. No database or network operations."""
from pathlib import Path
import argparse,collections,hashlib,json,re,unicodedata,io


def text(v):
    if v is None:return ''
    if isinstance(v,float) and v.is_integer():return str(int(v))
    return str(v).strip()

def row(sku,name,producer,vt,volume,price,stock,qty,sheet,n,cells,**extra):
    raw=text(vt)
    return dict(SupplierProductCode=sku,ProducerNameJa=producer,ProducerNameEn=producer,
        ProductNameJa=name,ProductNameEn=name,Vintage=int(raw) if re.fullmatch('[12][0-9]{3}',raw) else None,
        VintageRaw=raw,VolumeMl=int(volume),CaseSize=0,ReferenceRetailPrice=price,PriceRaw=text(price),TaxIncluded=False,
        Inventory=dict(Quantity=qty,Status=stock,RawValue=extra.pop('StockRaw','')),ProductType=extra.pop('ProductType',''),
        Country=extra.pop('Country',''),Region=extra.pop('Region',''),JanCodes=extra.pop('JanCodes',[]),
        Grapes='',AlcoholPercent=None,AppellationJa='',AppellationEn='',OrganicCategory=extra.pop('OrganicCategory',''),
        OrganicCertification='',PriceNote=extra.pop('PriceNote',''),Closure='',LegacySource='',SourceRow=n,
        SourceSheet=sheet,RawCellsJson=json.dumps(cells,ensure_ascii=False,default=str),**extra)

def bundle(p,code,name,date,rows):
    if not rows:raise ValueError('No products')
    codes=[r['SupplierProductCode'] for r in rows]
    if len(codes)!=len(set(codes)):raise ValueError('Duplicate product codes')
    return dict(SchemaVersion=1,SourceFileName=p.name,SourceSha256=hashlib.sha256(p.read_bytes()).hexdigest(),
        ObservedAt=date,SupplierCode=code,SupplierName=name,ParserVersion=code+'/20261002-v1',Rows=rows)

def iroha(p):
    import xlrd
    b=xlrd.open_workbook(p,logfile=io.StringIO());s=b.sheet_by_index(0)
    if text(s.cell_value(1,0))!='㈱いろはわいん　現在庫リスト':raise ValueError('Supplier header changed')
    if b.nsheets != 1 or s.ncols != 14:raise ValueError('Workbook layout changed')
    if s.row_values(7)[:12] != ['生産者','ＪＡＮコード','商品コード','Vt','商品名','容量','色','参考上代','下代','ー','備考','現在庫本数']:raise ValueError('Column headers changed')
    date=xlrd.xldate_as_datetime(s.cell_value(2,10),b.datemode).strftime('%Y-%m-%d')
    if date!='2026-10-01':raise ValueError('Review new source date')
    producer='';rows=[]
    for n in range(s.nrows):
        c=s.row_values(n)
        code=text(c[2])
        if code=='商品コード':continue
        if not code:
            # Producer-description rows are explicit, unlike region/title rows.
            if c[0] and c[3] and not c[4]:producer=text(c[0])
            continue
        if not re.fullmatch('[A-Za-z0-9_-]+',code) or not c[4]:raise ValueError(f'Unrecognized product {n+1}')
        if c[0] and c[0] not in ('定番！',):
            # Do not interpret promotional remarks as producer.
            if c[3] and not c[4]:producer=text(c[0])
        m=re.fullmatch(r'(\d+)(ml|g)',text(c[5]))
        if not m:raise ValueError(f'Volume requires review {n+1}: {c[5]}')
        price=c[7]
        if not isinstance(price,(int,float)):
            mprice=re.fullmatch(r'オープン\s*\([\\¥￥]([\d,]+)\)',text(price))
            if not mprice:raise ValueError(f'Price requires review {n+1}: {price}')
            price=int(mprice[1].replace(',',''))
        if price<0:raise ValueError('Negative price')
        stock=c[11]
        if isinstance(stock,(int,float)):
            if stock<0 or int(stock)!=stock:raise ValueError('Invalid quantity')
            qty=int(stock);status=1 if qty>0 else 3
        elif stock in ('○','◎'):qty=None;status=1
        elif stock=='':qty=None;status=0
        else:raise ValueError(f'Stock requires review {n+1}: {stock}')
        cells={xlrd.formula.colname(i)+str(n+1):v for i,v in enumerate(c)}
        cells.update(参考上代原表記=c[7],価格採用仕様='オープン括弧内金額を税抜参考上代として採用（2026-10-02ユーザー確認）',
            WholesalePrice=c[8],CaseWholesalePrice=c[9],在庫凡例='◎240本以上、○120～239本。記号から正確な本数を補完しない。')
        jan=re.findall(r'(?<!\d)\d{8,14}(?!\d)',text(c[1]))
        nonliquid=m[2]=='g'
        cells.update(原本規格=c[5],数量単位='個' if nonliquid else '本',原本入数=c[3] if nonliquid else None)
        rows.append(row(code,text(c[4]),producer,'' if nonliquid else c[3],0 if nonliquid else int(m[1]),price,status,qty,s.name,n+1,cells,
            StockRaw=text(stock),ProductType=text(c[6]),JanCodes=jan,PriceNote='卸価格（税抜）: '+text(c[8])+'。12本単位: '+text(c[9]),Description=text(c[10])))
    return bundle(p,'IROHA','いろはわいん',date,rows)

