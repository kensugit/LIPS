"""Synthetic schema guard tests. Existing restore DB is metadata-read-only; fixture DB must be new."""
import argparse, datetime, hashlib, json, pathlib, subprocess, uuid
from check_intake_schema import inspect, report, MIGRATIONS
HERE=pathlib.Path(__file__).resolve().parent

def main():
    ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--pg-bin',required=True);ap.add_argument('--output',required=True)
    args=ap.parse_args();bin_dir=pathlib.Path(args.pg_bin)
    psql=bin_dir/'psql.exe' if (bin_dir/'psql.exe').exists() else bin_dir/'psql'
    base=[str(psql),'-X','-w','-q','-A','-t','-h','127.0.0.1','-p','55993','-U','postgres','-v','ON_ERROR_STOP=1']
    target='lips_migration_restore_20261012';fixture='lips_schema_guard_'+uuid.uuid4().hex[:12]
    checks=[];proof=dict(startedAt=datetime.datetime.now(datetime.timezone.utc).isoformat(),productionTouched=False,existingDatabaseReadOnly=True,restoreDatabase=target,fixtureDatabase=fixture)
    output=pathlib.Path(args.output)
    if output.exists():raise RuntimeError('Refusing to overwrite existing proof')
    def sql(db,text):
        p=subprocess.run([*base,'-d',db],input=text,text=True,capture_output=True,encoding='utf-8')
        if p.returncode:raise RuntimeError('Synthetic setup failed; details withheld, exit='+str(p.returncode))
        return p.stdout.strip()
    def check(schema,expected):
        result=report(inspect(psql,'127.0.0.1',55993,'postgres',fixture,schema))
        assert result['status']==expected,(schema,result)
        checks.append(dict(case=schema,expected=expected,status=result['status'],errors=result['errors']))
    before=inspect(psql,'127.0.0.1',55993,'postgres',target)
    assert report(before)['status']=='Passed';checks.append(dict(case='existing_restore_db_read_only',status='Passed'))
    # Creation is exclusive: CREATE DATABASE fails if the fresh name happens to exist.
    sql('postgres','CREATE DATABASE "'+fixture+'" TEMPLATE template0;')
    names=['20261012010000_IntakeReview.sql','20261012070000_IntakeReusableRules.sql']
    ddl='\n'.join((MIGRATIONS/n).read_text(encoding='utf-8-sig') for n in names)
    cases={
      'valid':ddl,
      'missing':'',
      'wrong_type':ddl.replace('"Content" bytea','"Content" text'),
      'nullable':ddl.replace('"Hash" text NOT NULL','"Hash" text'),
      'wrong_default':ddl.replace('DEFAULT now()','DEFAULT clock_timestamp()'),
      'missing_unique':ddl.replace('"OperationId" uuid NOT NULL UNIQUE','"OperationId" uuid NOT NULL'),
      'missing_check':ddl.replace(' CHECK ("Version" > 0)',''),
      'weaker_check':ddl.replace('CHECK ("ExpectedVersion" >= 0)','CHECK ("ExpectedVersion" >= -1)'),
      'wrong_primary_order':ddl.replace('PRIMARY KEY ("RuleId", "Version")','PRIMARY KEY ("Version", "RuleId")'),
      'extra_index':ddl+'\nCREATE INDEX extra_index ON "IntakeReviews" ("Path");',
      'deferred_unique':ddl.replace('UNIQUE ("Hash", "Path")','UNIQUE ("Hash", "Path") DEFERRABLE INITIALLY DEFERRED'),
      'unvalidated_check':ddl.replace(' CHECK ("Version" > 0)','')+'\nALTER TABLE "IntakeReusableRuleVersions" ADD CHECK ("Version" > 0) NOT VALID;',
      'row_security':ddl+'\nALTER TABLE "IntakeReviews" ENABLE ROW LEVEL SECURITY;',
    }
    for schema,text in cases.items():
        sql(fixture,'BEGIN; CREATE SCHEMA "'+schema+'"; SET LOCAL search_path="'+schema+'";\n'+text+'\nCOMMIT;')
        check(schema,'Passed' if schema=='valid' else 'Failed')
    after=inspect(psql,'127.0.0.1',55993,'postgres',target)
    assert before==after,'Existing restore metadata changed';checks.append(dict(case='existing_restore_schema_unchanged',status='Passed'))
    proof.update(status='Passed',checks=checks,checkCount=len(checks),finishedAt=datetime.datetime.now(datetime.timezone.utc).isoformat(),fixtureRetained=True,restoreMetadataSha256=hashlib.sha256(json.dumps(before,sort_keys=True).encode()).hexdigest())
    output.write_text(json.dumps(proof,indent=2),encoding='utf-8');print(json.dumps(dict(status='Passed',checks=len(checks),fixtureDatabase=fixture,proof=str(output))))
if __name__=='__main__':main()
