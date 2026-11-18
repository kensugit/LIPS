import json
from pathlib import Path
import tempfile
import subprocess
import sys
import unittest
from unittest.mock import patch
import coordinate as c


class CoordinationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(dir=Path(__file__).resolve().parent)
        self.root = Path(self.temp.name)
        self.ids = dict(package="p", source="s", web="w", db="d", schema="z")
    def tearDown(self): self.temp.cleanup()
    def reserve(self): return c.reserve(self.root, "session1", "op1", self.ids, self.ids, "receiver")
    def test_reserve_and_inspect(self):
        self.reserve()
        self.assertEqual(c.inspect(self.root)["reservation"]["operationId"], "op1")
        self.assertFalse(c.inspect(self.root)["mutationPending"])
    def test_second_owner_and_expired_reservation_never_take_over(self):
        self.reserve()
        p = self.root / "reservation.json"
        value = c.load(p); value["createdUtc"] = "2000-01-01T00:00:00+00:00"
        p.write_text(json.dumps(value))
        self.assertTrue(c.inspect(self.root)["ttlElapsed"])
        with self.assertRaisesRegex(c.Rejected, "NO_AUTOMATIC_TAKEOVER"):
            c.reserve(self.root, "other", "other", self.ids, self.ids, "deploy")
        self.assertEqual(c.load(p), value)
    def test_stale_inspect_writes_nothing(self):
        with self.assertRaisesRegex(c.Rejected, "STALE_INSPECT"):
            c.reserve(self.root, "a", "b", self.ids, dict(self.ids, web="new"), "deploy")
        self.assertEqual(list(self.root.iterdir()), [])
    def test_owner_release_refused(self):
        self.reserve()
        with self.assertRaisesRegex(c.Rejected, "OWNER_MISMATCH"):
            c.release(self.root, "other", "op1", "cleanup")
        self.assertTrue((self.root / "reservation.json").exists())
    def test_unknown_preserved_then_explicit_resolution(self):
        self.reserve()
        c.transition(self.root, "session1", "op1", self.ids, "receiver", "unknown", "timeout receipt")
        with self.assertRaisesRegex(c.Rejected, "UNKNOWN_REQUIRES_RESOLUTION"):
            c.release(self.root, "session1", "op1", "cleanup")
        with self.assertRaisesRegex(c.Rejected, "UNKNOWN_USE_RESOLVE"):
            c.transition(self.root, "session1", "op1", self.ids, "receiver", "confirmed", "operation readback hash")
        c.resolve(self.root, "session1", "op1", self.handoff(), self.ids, "operation readback hash", self.reader)
        c.release(self.root, "session1", "op1", "listener zero proof")
        self.assertFalse((self.root / "reservation.json").exists())
        events = [c.load(p) for p in self.root.glob("event-*.json")]
        self.assertEqual(len(events), 4)
        self.assertTrue(any(e["reservation"]["outcome"] == "unknown" for e in events))
    def test_io_failure_retains_guard_no_replay(self):
        with patch.object(c, "event", side_effect=OSError("disk")):
            with self.assertRaises(OSError): self.reserve()
        self.assertTrue(c.inspect(self.root)["mutationPending"])
        with self.assertRaises(c.Rejected): c.release(self.root, "session1", "op1", "cleanup")
    def test_transition_stale_preserves_state(self):
        old = self.reserve()
        with self.assertRaisesRegex(c.Rejected, "STALE_INSPECT"):
            c.transition(self.root, "session1", "op1", dict(self.ids, db="new"), "deploy", "confirmed", "x")
        self.assertEqual(c.load(self.root / "reservation.json"), old)
    def handoff(self):
        return dict(expectedInspect=self.ids, sourcePath=str(self.root), sourceCommit="s",
                    package="p", nativeIdentity="w", schemaIdentity="z",
                    verificationEvidence="tests hash", cleanupEvidence="owned zero",
                    runtimeFeatures={"aiSidecarActive":False, "pdf":True}, webOnly=True)
    def reader(self, source, *args):
        return str(self.root) if args[-1] == "--show-toplevel" else "s" if args[-1] == "HEAD" else ""
    def test_clean_handoff_and_active_sidecar_guard(self):
        data = self.handoff()
        self.assertEqual(c.validate_handoff(data, self.ids, self.reader)["status"], "DeployedHandoffLocallyValidated")
        data["runtimeFeatures"]["aiSidecarActive"] = True
        with self.assertRaisesRegex(c.Rejected, "ACTIVE_SIDECAR"):
            c.validate_handoff(data, self.ids, self.reader)
    def test_dirty_commit_and_missing_evidence_rejected(self):
        data = self.handoff()
        with self.assertRaisesRegex(c.Rejected, "SOURCE_DIRTY"):
            c.validate_handoff(data, self.ids, lambda s,*a: " M file" if a[0] == "status" else self.reader(s,*a))
        data["sourceCommit"] = "wrong"
        with self.assertRaisesRegex(c.Rejected, "DEPLOYED_IDENTITY_sourceCommit"):
            c.validate_handoff(data, self.ids, self.reader)
        data = self.handoff(); data["verificationEvidence"] = ""
        with self.assertRaises(c.Rejected): c.validate_handoff(data, self.ids, self.reader)
    def test_missing_schema_identity_and_mutation_lock_refused(self):
        with self.assertRaisesRegex(c.Rejected, "INSPECT_FIELDS"):
            c.reserve(self.root, "a", "b", {"package":"p"}, self.ids, "receiver")
        c.write_new(self.root / "mutation.lock", {"interrupted":True})
        with self.assertRaisesRegex(c.Rejected, "MUTATION_BUSY_OR_UNKNOWN"):
            self.reserve()
    def test_cli_reserve_inspect_and_owner_rejection(self):
        script = str(Path(c.__file__).resolve())
        arguments = self.root / "input.json"
        arguments.write_text(json.dumps(dict(session="cli", operation="op", expected=self.ids,
                                            observed=self.ids, phase="receiver")))
        run = lambda command: subprocess.run([sys.executable, "-B", script, command,
                    "--root", str(self.root), "--input", str(arguments)], capture_output=True, text=True)
        first = run("reserve")
        self.assertEqual(first.returncode, 0, first.stderr)
        self.assertEqual(json.loads(run("inspect").stdout)["reservation"]["sessionRef"], "cli")
        arguments.write_text(json.dumps(dict(session="wrong", operation="op", evidence="cleanup")))
        denied = run("release")
        self.assertEqual(denied.returncode, 1)
        self.assertEqual(json.loads(denied.stdout)["reason"], "OWNER_MISMATCH")
    def test_reviewed_deploy_flow_drift_and_prewrite_race(self):
        old = self.reserve()
        real_load = c.load
        reads = 0
        def changed_on_second(path):
            nonlocal reads
            reads += 1
            return dict(old, operationId="changed") if reads == 2 else real_load(path)
        with patch.object(c, "load", side_effect=changed_on_second):
            with self.assertRaisesRegex(c.Rejected, "RESERVATION_CHANGED"):
                c.transition(self.root, "session1", "op1", self.ids, "deploy", "confirmed", "before")
        self.assertFalse((self.root / "mutation.lock").exists())
        self.assertEqual(real_load(self.root / "reservation.json"), old)
        deployed = dict(self.ids, package="new-package", source="new-source", web="new-web")
        data = self.handoff()
        data.update(expectedInspect=deployed, sourceCommit=deployed["source"], package=deployed["package"], nativeIdentity=deployed["web"])
        reader = lambda s,*a: deployed["source"] if a[-1] == "HEAD" else self.reader(s,*a)
        for key in ("sourceCommit", "package", "nativeIdentity", "schemaIdentity"):
            with self.subTest(key=key):
                with self.assertRaisesRegex(c.Rejected, "DEPLOYED_IDENTITY"):
                    c.validate_handoff(dict(data, **{key:"candidate-mismatch"}), deployed, reader)
        self.assertEqual(c.validate_handoff(data, deployed, reader)["package"], "new-package")
        c.release(self.root, "session1", "op1", "new deployed handoff + cleanup evidence")
        self.reserve()
        result = c.transition(self.root, "session1", "op1", deployed, "deploy", "unknown", "changed runtime, outcome unconfirmed")
        self.assertEqual(result["expectedInspect"], self.ids)
        self.assertEqual(result["observedInspect"], deployed)
        with self.assertRaisesRegex(c.Rejected, "UNKNOWN_REQUIRES_RESOLUTION"):
            c.release(self.root, "session1", "op1", "not resolved")
        before = c.load(self.root / "reservation.json")
        with self.assertRaisesRegex(c.Rejected, "OWNER_MISMATCH"):
            c.resolve(self.root, "wrong", "op1", data, deployed, "resolved", reader)
        with self.assertRaisesRegex(c.Rejected, "RESOLUTION_EVIDENCE_REQUIRED"):
            c.resolve(self.root, "session1", "op1", data, deployed, "", reader)
        with self.assertRaisesRegex(c.Rejected, "DEPLOYED_IDENTITY"):
            c.resolve(self.root, "session1", "op1", dict(data, package="wrong"), deployed, "resolved", reader)
        with self.assertRaisesRegex(c.Rejected, "SOURCE_DIRTY"):
            c.resolve(self.root, "session1", "op1", data, deployed, "resolved",
                      lambda s,*a: " M file" if a[0] == "status" else reader(s,*a))
        self.assertEqual(c.load(self.root / "reservation.json"), before)
        resolved = c.resolve(self.root, "session1", "op1", data, deployed, "actual operation and cleanup verified", reader)
        self.assertEqual(resolved["expectedInspect"], self.ids)
        self.assertEqual(resolved["observedInspect"], deployed)
        self.assertEqual(resolved["outcome"], "resolvedconfirmed")
        self.assertEqual(len(resolved["resolvedHandoffSha256"]), 64)
        with self.assertRaisesRegex(c.Rejected, "RESOLVE_REQUIRES_UNKNOWN"):
            c.resolve(self.root, "session1", "op1", data, deployed, "repeat", reader)
        c.release(self.root, "session1", "op1", "actual new handoff and cleanup complete")
        self.assertFalse((self.root / "reservation.json").exists())


if __name__ == "__main__": unittest.main()
