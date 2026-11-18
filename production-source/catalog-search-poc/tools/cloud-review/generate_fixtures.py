"""Generate synthetic-only files; no supplier document is read."""
import json
from pathlib import Path
import openpyxl
from openpyxl.styles import Font, PatternFill

root=Path(__file__).parent/'fixtures'
(root/'ENEOS').mkdir(parents=True,exist_ok=True)
headers=['品番','2026-10-01','ﾘﾏｰｸ','商品名','色','VT','原産国','地域/産地（格付）','生産者','入数','JANコード単品','容量(ml)','参考上代（税別/単品）','お見積価格（税別/単品）']
def book(name,rows):
    w=openpyxl.Workbook();s=w.active;s.title='ENEOS合成'
    s.append(['合成試験専用・実在庫ではありません']);s.append([]);s.append(headers)
    for sku,qty,price in rows:s.append([sku,qty,'','合成商品'+sku,'赤','2020','日本','合成産地','合成生産者',6,'',750,price,700])
    for c in s[3]:c.font=Font(bold=True);c.fill=PatternFill('solid',fgColor='DDEEFF')
    for col in s.columns:s.column_dimensions[col[0].column_letter].width=18
    w.save(root/'ENEOS'/name)
book('01_2026-10-01.xlsx',[('990001',12,1000),('990001',12,1000)])
book('02_2026-10-01.xlsx',[('990001',8,1000)])
book('03_2026-10-01.xlsx',[('990001',12,1000)])
book('04_2026-10-01.xlsx',[('990001',9,1100),('990002',6,1500)])
(root/'LUC').mkdir(exist_ok=True)
(root/'LUC'/'date-question.csv').write_text('SKU,ProductNameJa,Vintage,VolumeMl,Price,Tax,Inventory\nSYNTH-DATE,合成日付確認,2020,750,1000,税抜,12\n',encoding='utf-8-sig')
request={'schema':'lips-date-proposal/v1','requestId':'92aa25d9-60fb-4886-8b54-330ebd3e7a31','sourceHash':'A'*64,'reviewVersion':3,'promptVersion':'date-evidence/1','excerpts':[{'id':'e1','location':'合成原典!A1','text':'在庫日2026年10月1日'}]}
response={k:request[k] for k in ['schema','requestId','sourceHash','reviewVersion','promptVersion']}
response.update(provider='offline-fixture',model='synthetic',outcome='Candidates',candidates=[{'date':'2026-10-01','reason':'原典に記載された在庫日','evidenceIds':['e1']}])
for name,data in [('proposal-request.json',request),('proposal-response.json',response)]:
    (root/name).write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
print('Created synthetic ENEOS/LUC and offline proposal fixtures.')
