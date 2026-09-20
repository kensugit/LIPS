import hashlib
import importlib.util
import io
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('dispatch',ROOT/'tools/remote-deploy/dispatch.py')
dispatch=importlib.util.module_from_spec(spec); spec.loader.exec_module(dispatch)

class RemoteDeployTests(unittest.TestCase):
    def test_command_allowlist(self):
        sha='a'*64
        for command, expected in [('status',('status',None)),('rollback',('rollback',None)),('upload '+sha,('upload',sha)),('deploy '+sha,('deploy',sha))]:
            self.assertEqual(expected,dispatch.parse_command(command))
        for command in ['', 'bash', 'status; id', 'status\nwhoami', 'deploy '+sha+';id', 'deploy '+sha+' extra', 'approve '+sha, 'initialize '+sha, 'upload ../../etc/passwd','scp -t /tmp', 'internal-sftp','status ', 'deploy '+'A'*64]:
            with self.subTest(command=command),self.assertRaises(ValueError): dispatch.parse_command(command)
    def test_upload_and_idempotence(self):
        data=b'fixture package\x00\xff'; sha=hashlib.sha256(data).hexdigest()
        with tempfile.TemporaryDirectory() as directory:
            result=dispatch.receive(io.BytesIO(data),directory,sha)
            self.assertFalse(result['alreadyPresent'])
            self.assertEqual(data,(Path(directory)/(sha+'.tar')).read_bytes())
            self.assertTrue(dispatch.receive(io.BytesIO(data),directory,sha)['alreadyPresent'])
            self.assertEqual(1,len(list(Path(directory).iterdir())))
    def test_failed_uploads_leave_no_partial_files(self):
        for data,sha,limit in [(b'abc','0'*64,100),(b'','0'*64,100),(b'1234',hashlib.sha256(b'1234').hexdigest(),3)]:
            with tempfile.TemporaryDirectory() as directory:
                with self.assertRaises(ValueError): dispatch.receive(io.BytesIO(data),directory,sha,limit)
                self.assertEqual([],list(Path(directory).iterdir()))
    def test_conflicting_file_not_overwritten(self):
        data=b'new'; sha=hashlib.sha256(data).hexdigest()
        with tempfile.TemporaryDirectory() as directory:
            target=Path(directory)/(sha+'.tar'); target.write_bytes(b'old')
            with self.assertRaises(ValueError): dispatch.receive(io.BytesIO(data),directory,sha)
            self.assertEqual(b'old',target.read_bytes())
    def test_only_fixed_sudo_arguments(self):
        sha='b'*64
        with patch.dict(os.environ,{'SSH_ORIGINAL_COMMAND':'deploy '+sha}),patch.object(dispatch.subprocess,'run') as run:
            run.return_value.returncode=0; self.assertEqual(0,dispatch.main())
            self.assertEqual(['/usr/bin/sudo','-n','/usr/local/sbin/catalogctl','deploy',sha],run.call_args.args[0])
            self.assertNotIn('shell',run.call_args.kwargs)
        with patch.dict(os.environ,{'SSH_ORIGINAL_COMMAND':'rollback'}),patch.object(dispatch.subprocess,'run') as run:
            dispatch.main(); self.assertEqual(['/usr/bin/sudo','-n','/usr/local/sbin/lips-codex-rollback'],run.call_args.args[0])

if __name__=='__main__': unittest.main()
