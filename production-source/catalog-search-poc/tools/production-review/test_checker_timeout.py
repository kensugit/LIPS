"""Offline regression: bounded subprocess, copied environment, sanitized timeout."""
import os, subprocess, unittest
from unittest.mock import patch
from check_intake_schema import inspect
class TimeoutTests(unittest.TestCase):
    def test_timeout_is_bounded_without_mutating_parent_or_disclosing_command(self):
        before=os.environ.get('PGCONNECT_TIMEOUT')
        with patch('check_intake_schema.subprocess.run',side_effect=subprocess.TimeoutExpired(['psql','private-connection-marker'],25)) as runner:
            with self.assertRaisesRegex(RuntimeError,'Read-only catalog query timed out') as raised:
                inspect('psql','127.0.0.1',55993,'postgres','synthetic_only')
            kwargs=runner.call_args.kwargs
            self.assertEqual(kwargs['timeout'],25)
            self.assertEqual(kwargs['env']['PGCONNECT_TIMEOUT'],'5')
            self.assertIsNot(kwargs['env'],os.environ)
            self.assertEqual(os.environ.get('PGCONNECT_TIMEOUT'),before)
            self.assertNotIn('private-connection-marker',str(raised.exception))
if __name__=='__main__':unittest.main()
