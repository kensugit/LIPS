"""Read-only validation of manually applied intake DDL. Does not read business rows."""
import argparse, hashlib, json, os, pathlib, re, subprocess, sys
HERE = pathlib.Path(__file__).resolve().parent
SQL = HERE / 'check-intake-schema.sql'
EXPECTED_DDL_TEXT_SHA256 = {'20261012010000_IntakeReview.sql': 'c9bc39cd08715ca46595396af5b46e87db1accd4cf2b81b727f7b1179bc7baa9', '20261012070000_IntakeReusableRules.sql': 'e0254622645fd8c666ba6ccb0b27610809f36aac526a81cbd2f78b11f394817f'}
MIGRATIONS = HERE.parent.parent / 'DB' / 'Migrations'

def column(name, kind, default=None):
    return dict(name=name, type=kind, notNull=True, default=default, identity='', generated='')

def constraint(definition):
    return dict(definition=definition, validated=True, deferrable=False, deferred=False)

def index(columns, primary=False):
    return dict(method='btree', unique=True, primary=primary, valid=True, ready=True, live=True,
        immediate=True, exclusion=False, nullsNotDistinct=False, keyCount=len(columns),
        columns=['"'+c+'"' for c in columns], predicate=None, expressions=None)

EXPECTED = {
 'IntakeReviews': dict(columns=[column('Id','uuid'),column('Hash','text'),column('Path','text'),column('Content','bytea'),column('Version','integer'),column('Body','jsonb'),column('UpdatedAt','timestamp with time zone','now()')],
  constraints=[constraint('PRIMARY KEY ("Id")'),constraint('UNIQUE ("Hash", "Path")')],indexes=[index(['Id'],True),index(['Hash','Path'])]),
 'IntakeReusableRuleVersions': dict(columns=[column('RuleId','uuid'),column('Version','integer'),column('OperationId','uuid'),column('RequestHash','text'),column('ExpectedVersion','integer'),column('Body','jsonb'),column('CreatedAt','timestamp with time zone','now()')],
  constraints=[constraint('PRIMARY KEY ("RuleId", "Version")'),constraint('UNIQUE ("OperationId")'),constraint('CHECK (("Version" > 0))'),constraint('CHECK ((length("RequestHash") = 64))'),constraint('CHECK (("ExpectedVersion" >= 0))')],
  indexes=[index(['RuleId','Version'],True),index(['OperationId'])])}

def canonical(values):
    return sorted(json.dumps(v,sort_keys=True) for v in values)

def validate(data):
    errors=[]
    if data.get('transactionReadOnly')!='on': errors.append('Read-only transaction was not confirmed')
    found={t['name']:t for t in data['tables']}
    for name,expected in EXPECTED.items():
        actual=found.get(name)
        if actual is None:
            errors.append(name+': missing relation'); continue
        if any(actual.get(k)!=v for k,v in dict(kind='r',persistence='p',rls=False,forceRls=False,inherits=0,userTriggers=0).items()):
            errors.append(name+': relation properties mismatch')
        if actual['columns']!=expected['columns']: errors.append(name+': columns/type/nullability/default/identity/order mismatch')
        for kind in ('constraints','indexes'):
            if canonical(actual[kind])!=canonical(expected[kind]): errors.append(name+': '+kind+' mismatch')
    return errors

def inspect(psql, host, port, user, database, schema='public'):
    for label,value in [('database',database),('schema',schema),('user',user)]:
        if not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]*',value): raise ValueError('Invalid '+label+' identifier')
    command=[str(psql),'-X','-w','-q','-A','-t','-h',host,'-p',str(port),'-U',user,'-d',database,
        '-v','ON_ERROR_STOP=1','-v','schema='+schema,'-f',str(SQL)]
    child_env=os.environ.copy()
    child_env['PGCONNECT_TIMEOUT']='5'
    try:
        result=subprocess.run(command,capture_output=True,text=True,encoding='utf-8',errors='replace',env=child_env,timeout=25)
    except subprocess.TimeoutExpired:
        raise RuntimeError('Read-only catalog query timed out; connection details withheld') from None
    if result.returncode: raise RuntimeError('Read-only catalog query failed; psql exit '+str(result.returncode)+' (connection/error details withheld)')
    data=json.loads(result.stdout)
    if data.get('database')!=database or data.get('schema')!=schema: raise RuntimeError('Query destination mismatch')
    return data

def report(data):
    errors=validate(data)
    for name,expected_hash in EXPECTED_DDL_TEXT_SHA256.items():
        actual_hash=hashlib.sha256((MIGRATIONS/name).read_text(encoding='utf-8-sig').encode()).hexdigest()
        if actual_hash!=expected_hash: errors.append(name+': DDL contract changed; validator review required')
    return dict(status='Failed' if errors else 'Passed',readOnly=True,businessRowsRead=False,
        database=data['database'],schema=data['schema'],expectedTableCount=2,errors=errors,
        ddlSha256={name:hashlib.sha256((MIGRATIONS/name).read_bytes()).hexdigest() for name in
            ['20261012010000_IntakeReview.sql','20261012070000_IntakeReusableRules.sql']})

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--psql',default='psql');parser.add_argument('--host',required=True)
    parser.add_argument('--port',required=True,type=int);parser.add_argument('--user',required=True)
    parser.add_argument('--database',required=True);parser.add_argument('--schema',default='public')
    args=parser.parse_args()
    try:
        result=report(inspect(args.psql,args.host,args.port,args.user,args.database,args.schema))
        print(json.dumps(result,ensure_ascii=False));return 0 if result['status']=='Passed' else 1
    except Exception as exc:
        message=str(exc) if isinstance(exc,(ValueError,RuntimeError)) else type(exc).__name__
        print(json.dumps(dict(status='Failed',readOnly=True,error=message),ensure_ascii=False));return 2

if __name__=='__main__':sys.exit(main())
