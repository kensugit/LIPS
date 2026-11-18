import base64
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[1]

def powershell(arguments):
    # Python inherits PS7 module paths unchanged; give the PS5 child its native
    # modules, as a fresh Dell powershell.exe process would receive.
    env=os.environ.copy()
    env['PSMODULEPATH']=str(Path(env['SYSTEMROOT'])/'System32/WindowsPowerShell/v1.0/Modules')
    return subprocess.run(['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',*map(str,arguments)],capture_output=True,env=env)
def module(name,path):
    spec=importlib.util.spec_from_file_location(name,ROOT/path)
    m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m
builder=module('payload_builder','tools/remote-deploy/build-payload.py')
waiter=module('wait_intake','tools/release-preflight/wait_intake.py')
kit=module('inspect_kit','tools/release-preflight/inspect_kit.py')

class SchemaTests(unittest.TestCase):
    def test_raw_crlf_and_recursive_inventory_match_linux_contract(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d); names={'DB/Migrations/a.sql':b'one\r\ntwo\r\n','src/CatalogSearch.Infrastructure/Migrations/nested/a.cs':b'class A {}\n'}
            for n,b in names.items():p=root/n;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b)
            stream=''.join(hashlib.sha256(names[n]).hexdigest()+'  '+n+'\n' for n in sorted(names))
            self.assertEqual(hashlib.sha256(stream.encode()).hexdigest(),builder.schema_fingerprint(root))
            first=builder.schema_fingerprint(root)
            (root/'DB/Migrations/a.sql').write_bytes(b'one\ntwo\n')
            self.assertNotEqual(first,builder.schema_fingerprint(root))
    def test_missing_migrations_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaises(ValueError):builder.schema_fingerprint(Path(d))
    def test_known_production_source_exact_bytes(self):
        source=ROOT/'outputs/orveaux-202610/source-latest/catalog-search-poc'
        if not source.exists():self.skipTest('Local production fixture not present')
        self.assertEqual('e6f8c5cc5263392e87a4383248fa6bd7278ad1784f18a410f25e2dbd62970267',builder.schema_fingerprint(source))

class WaitTests(unittest.TestCase):
    def row(self,state,version=9):return dict(id='id',hash='hash',state=state,version=version)
    def run_wait(self,rows,timeout=3):
        seq=iter(rows);last=[None];now=[0]
        def fetch():
            last[0]=next(seq,last[0]);return last[0]
        def sleep(n):now[0]+=n
        return waiter.wait_result(fetch,'id','hash',6,timeout,1,lambda:now[0],sleep)
    def test_running_never_treated_as_finished(self):
        result=self.run_wait([self.row('Running',8),self.row('Partial')]);self.assertEqual('Partial',result['state'])
    def test_completed_review_returns_nested_success_receipt(self):
        # Production /api/import/reviews uses Completed at the review level,
        # while the nested receipt uses Success. Preserve both contracts.
        row=self.row('Completed');row['result']={'status':'Success','products':48}
        result=self.run_wait([self.row('Running',8),row])
        self.assertEqual('Completed',result['state'])
        self.assertEqual({'status':'Success','products':48},result['result'])
    def test_stale_saved_result_requires_new_version(self):
        self.assertEqual(9,self.run_wait([self.row('Partial',6),self.row('Running',8),self.row('Partial')])['version'])
    def test_timeout_has_no_import_retry(self):
        with self.assertRaises(TimeoutError):self.run_wait([self.row('Running')])
    def test_unknown_stops_immediately(self):
        with self.assertRaises(ValueError):self.run_wait([self.row('Unknown')])
    def test_wrong_original_rejected(self):
        row=self.row('Partial');row['hash']='other'
        with self.assertRaises(ValueError):self.run_wait([row])
    def test_failed_and_held_remain_explicit(self):
        for state in ['Failed','Held']:
            self.assertEqual(state,self.run_wait([self.row(state)])['state'])

class WrapperTests(unittest.TestCase):
    def test_stale_activation_start_pin_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);pins=[]
            for name in ['control-hashes.json','Read-TransferReadiness.ps1','Start-LipsR4.ps1']:
                (root/name).write_bytes(b'old')
                pins.append("@{name='"+name+"';sha='"+kit.digest(b'old')+"'}")
            (root/'Activate-Receive.ps1').write_text(';'.join(pins))
            kit.validate_activation_pins(root)
            (root/'Start-LipsR4.ps1').write_bytes(b'new with error display')
            with self.assertRaisesRegex(ValueError,'Activation control pin differs'):kit.validate_activation_pins(root)
    def test_missing_activation_pins_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);(root/'Activate-Receive.ps1').write_text('Write-Host ready')
            with self.assertRaisesRegex(ValueError,'Activation pin format'):kit.validate_activation_pins(root)
    def test_bad_embedded_hash_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            item=dict(name='a',base64=base64.b64encode(b'a').decode(),length=1,sha256='0'*64)
            with self.assertRaisesRegex(ValueError,'hash differ'):kit.validate_bundle({'files':[item]},Path(d))
    def test_path_traversal_rejected(self):
        for name in ['../a','C:a','a\\b']:
            with self.assertRaises(ValueError):kit.validate_bundle({'files':[{'name':name}]},ROOT)
    def test_iroha_and_fixed_resume_artifacts(self):
        cases=[('artifacts/iroha-20261017/latest','Receive-Iroha-20261017.ps1','receiver-kit'),('outputs/orveaux-202610/index-fix','Resume-OrveauxIndex-20261017-r2.ps1','resume-kit')]
        for folder,wrapper,directory in cases:
            root=ROOT/folder
            if not root.exists():continue
            self.assertEqual('WrapperAndSourcesVerified',kit.audit(root/wrapper,root/directory,root)['status'])

class LifecycleTests(unittest.TestCase):
    def run_check(self,directory):
        return powershell([ROOT/'tools/release-preflight/Test-ReceiverLifecycle.ps1','-KitDirectory',directory])
    def test_original_duplicate_initialization_regression(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'Activate-Receive.ps1';p.write_text('Initialize-CandidateEngine (Get-CandidatePlan)')
            self.assertNotEqual(0,self.run_check(d).returncode)
    def test_dot_source_preload_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'Activate-Receive.ps1';p.write_text(". (Join-Path $dir 'Receive-LipsR4.ps1') -DefinitionsOnly")
            self.assertNotEqual(0,self.run_check(d).returncode)
    def test_fixed_resume_and_iroha_parse_without_execution(self):
        for path in ['outputs/orveaux-202610/index-fix/resume-kit','artifacts/iroha-20261017/latest/receiver-kit']:
            if not (ROOT/path).exists():continue
            r=self.run_check(ROOT/path);self.assertEqual(0,r.returncode,r.stderr)

class DiagnosticTests(unittest.TestCase):
    def test_partial_proof_and_cleanup_are_returned_together(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d); (root/'payload.zip').write_bytes(b'zip');(root/'proof.json.upload').write_bytes(b'')
            manifest=dict(packageId='test',destination=str(root),files=[dict(name=n,kind=k,length=len(b),sha256=hashlib.sha256(b).hexdigest().upper()) for n,k,b in [('payload.zip','zip',b'zip'),('proof.json','proof',b'proof')]])
            (root/'manifest.json').write_text(json.dumps(manifest))
            (root/'result.json').write_text(json.dumps(dict(status='failedNeedsInspection',error='Transfer interrupted; partial retained.')))
            (root/'cleanup-test.json').write_text(json.dumps(dict(cleanupError=None,portObservedClear=True,ruleRemovalVerified=True,listenersAfterCleanup=[])))
            r=powershell([ROOT/'tools/release-preflight/Read-TransferOutcome.ps1','-ResultPath',root/'result.json','-ManifestPath',root/'manifest.json'])
            self.assertEqual(0,r.returncode,r.stderr)
            result=json.loads(r.stdout.decode('utf-8-sig'))
            self.assertIn('partial retained',result['error'])
            self.assertFalse(result['allCompletedFilesMatch']);self.assertFalse(result['automaticRetry'])
            self.assertTrue(result['files'][0]['matchesCompletedFile'])
            self.assertEqual(0,result['files'][3]['bytes'])
            self.assertTrue(result['cleanupRecords'][0]['ruleRemovalVerified'])
    def test_new_summary_surfaces_error_without_second_read(self):
        # Execute the exact summary expression only, never the receiver/template.
        text=(ROOT/'tools/release-preflight/templates/Start-LipsR4.ps1').read_text('utf-8-sig')
        summary=next(line for line in text.splitlines() if line.startswith('[pscustomobject]@{status=$read.status;'))
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);result=root/'result.json';result.write_text('{}')
            script=root/'test.ps1'
            script.write_text("$ErrorActionPreference='Stop'\nSet-StrictMode -Version Latest\n$resultPath='"+str(result).replace("'","''")+"'\n$read=[pscustomobject]@{status='failedNeedsInspection';error='Engine type already loaded'}\n"+summary,encoding='utf-8-sig')
            r=powershell([script])
            self.assertEqual(0,r.returncode,r.stderr)
            self.assertEqual('Engine type already loaded',json.loads(r.stdout)['error'])
            script.write_text(script.read_text('utf-8-sig').replace("status='failedNeedsInspection';error='Engine type already loaded'","status='completed'"),encoding='utf-8-sig')
            r=powershell([script])
            self.assertEqual(0,r.returncode,r.stderr);self.assertIsNone(json.loads(r.stdout)['error'])

if __name__=='__main__':unittest.main()
