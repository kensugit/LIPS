"""Wait for a persisted intake result using GET only; never retry an import."""
import argparse
import json
import time
import urllib.request
import uuid
from pathlib import Path

IN_FLIGHT={'Pending','Queued','Running','Processing'}
FINISHED={'Completed','Success','Partial','Held','Unsupported','Failed','Cancelled'}

def wait_result(fetch, review_id, source_hash, after_version, timeout=120,
                interval=2, clock=time.monotonic, sleep=time.sleep):
    if timeout<=0 or not 0<interval<=60:raise ValueError('Invalid wait bounds')
    deadline=clock()+timeout
    while True:
        row=fetch()
        if row.get('id')!=review_id or row.get('hash')!=source_hash:
            raise ValueError('Review identity or original hash changed; inspect only')
        state=row.get('state')
        if state=='Unknown':raise ValueError('Unknown operation result; no automatic retry')
        if state in FINISHED and row.get('version',-1)>after_version:
            return row  # Held/Partial are results, not full-workbook success.
        if state not in IN_FLIGHT|FINISHED:raise ValueError('Unrecognized state; inspect only')
        remaining=deadline-clock()
        if remaining<=0:raise TimeoutError('Result not confirmed; do not resend/recheck automatically')
        sleep(min(interval,remaining))

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--base',required=True);p.add_argument('--id',required=True,type=uuid.UUID)
    p.add_argument('--hash',required=True);p.add_argument('--after-version',required=True,type=int)
    p.add_argument('--timeout',type=float,default=120);p.add_argument('--output',type=Path,required=True)
    a=p.parse_args()
    def fetch():
        with urllib.request.urlopen(a.base.rstrip('/')+'/api/import/reviews/'+str(a.id),timeout=15) as r:
            return json.load(r)
    row=wait_result(fetch,str(a.id),a.hash,a.after_version,a.timeout)
    # Exclusive create prevents overwriting a previous accepted proof.
    with a.output.open('x',encoding='utf-8') as f:json.dump(row,f,ensure_ascii=False,indent=2)
    print(json.dumps({'state':row['state'],'version':row['version'],'resultSaved':str(a.output),'importTriggered':False}))
if __name__=='__main__':main()
