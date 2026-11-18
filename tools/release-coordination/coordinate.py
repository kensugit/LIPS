"""Local cooperative release reservation. No network or deployment operations.

All cooperating sessions must use the same local directory. This is not a
cross-PC lock and does not replace production inspect/approval/server guards.
Expired reservations are never taken over automatically. Unknown outcomes must
be resolved through evidence before release.
"""
import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import json
import hashlib
import os
from pathlib import Path
import subprocess
import uuid

IDENTITY = {"package", "source", "web", "db", "schema"}
PHASES = {"receiver", "deploy", "active-sidecar"}


class Rejected(ValueError):
    pass


class UnmodifiedReservationChanged(Rejected):
    """Only raised by the pre-write second read, never by a write operation."""


def same_reservation(path, record):
    if load(path) != record:
        raise UnmodifiedReservationChanged("RESERVATION_CHANGED")


def require(condition, code):
    if not condition:
        raise Rejected(code)


def now():
    return datetime.now(timezone.utc).isoformat()


def identity(value):
    require(isinstance(value, dict) and set(value) == IDENTITY, "INSPECT_FIELDS")
    require(all(isinstance(v, str) and v.strip() for v in value.values()), "INSPECT_VALUES")
    return value


def write_new(path, value):
    with path.open("x", encoding="utf-8", newline="\n") as f:
        json.dump(value, f, ensure_ascii=False, indent=2)
        f.flush()
        os.fsync(f.fileno())


def load(path):
    require(not path.is_symlink(), "SYMLINK")
    with path.open(encoding="utf-8-sig") as f:
        return json.load(f)


@contextmanager
def mutation(root):
    root = Path(root)
    require(root.is_absolute() and root.is_dir(), "EXISTING_ABSOLUTE_ROOT_REQUIRED")
    require(not any(p.is_symlink() for p in [root, *root.parents]), "SYMLINK_ROOT")
    guard = root / "mutation.lock"
    # A failed/crashed mutation leaves this lock for explicit investigation.
    try:
        write_new(guard, {"createdUtc": now()})
    except FileExistsError as e:
        raise Rejected("MUTATION_BUSY_OR_UNKNOWN") from e
    try:
        yield root
    except UnmodifiedReservationChanged:
        guard.unlink()
        raise
    except BaseException:
        # Expected validation failures have not modified state; I/O failures may
        # have, and deliberately retain the guard instead of allowing replay.
        raise
    else:
        guard.unlink()


def owner(record, session, operation):
    require(record["sessionRef"] == session and record["operationId"] == operation, "OWNER_MISMATCH")


def event(root, kind, record, evidence=None):
    write_new(root / ("event-" + uuid.uuid4().hex + ".json"),
              {"kind": kind, "utc": now(), "reservation": record, "evidence": evidence})


def reserve(root, session, operation, expected, observed, phase, ttl_seconds=1800):
    identity(expected); identity(observed)
    require(expected == observed, "STALE_INSPECT")
    require(phase in PHASES and session.strip() and operation.strip(), "RESERVATION_FIELDS")
    require(type(ttl_seconds) is int and ttl_seconds > 0, "TTL")
    path = Path(root) / "reservation.json"
    require(not path.exists() and not path.is_symlink(), "RESERVED_NO_AUTOMATIC_TAKEOVER")
    with mutation(root) as directory:
        # Exclusive creation also protects against a race after the first read.
        record = {"sessionRef": session, "operationId": operation, "expectedInspect": expected,
                  "phase": phase, "createdUtc": now(), "ttlSeconds": ttl_seconds,
                  "outcome": "reserved", "automaticTakeover": False}
        write_new(path, record)
        event(directory, "reserved", record)
    return record


def inspect(root):
    record = load(Path(root) / "reservation.json")
    elapsed = (datetime.now(timezone.utc) - datetime.fromisoformat(record["createdUtc"])).total_seconds()
    return {"reservation": record, "ttlElapsed": elapsed > record["ttlSeconds"],
            "mutationPending": (Path(root) / "mutation.lock").exists(), "automaticTakeover": False}


def transition(root, session, operation, observed, phase, outcome, evidence):
    require(phase in PHASES and outcome in {"confirmed", "unknown"}, "TRANSITION_FIELDS")
    require(isinstance(evidence, str) and evidence.strip(), "EVIDENCE_REQUIRED")
    record = load(Path(root) / "reservation.json")
    owner(record, session, operation)
    identity(observed)
    # confirmed checks a phase's starting baseline, not deployment completion.
    if outcome == "confirmed":
        require(record["outcome"] != "unknown", "UNKNOWN_USE_RESOLVE")
        require(observed == record["expectedInspect"], "STALE_INSPECT")
    with mutation(root) as directory:
        same_reservation(directory / "reservation.json", record)
        updated = dict(record, phase=phase, outcome=outcome, evidence=evidence, observedInspect=observed)
        event(directory, "transition", updated, evidence)
        temp = directory / ("state-" + uuid.uuid4().hex + ".json")
        write_new(temp, updated)
        os.replace(temp, directory / "reservation.json")
    return updated


def resolve(root, session, operation, data, observed, evidence, read_git=None):
    """Resolve uncertainty only against a validated deployed handoff, not an old baseline."""
    record = load(Path(root) / "reservation.json")
    owner(record, session, operation)
    require(record["outcome"] == "unknown", "RESOLVE_REQUIRES_UNKNOWN")
    require(isinstance(evidence, str) and evidence.strip(), "RESOLUTION_EVIDENCE_REQUIRED")
    validation = validate_handoff(data, observed, read_git or git)
    handoff_hash = hashlib.sha256(json.dumps({"data": data, "observed": observed},
                          sort_keys=True, ensure_ascii=False, separators=(",", ":")).encode()).hexdigest()
    with mutation(root) as directory:
        same_reservation(directory / "reservation.json", record)
        updated = dict(record, outcome="resolvedconfirmed", observedInspect=observed,
                       resolutionEvidence=evidence, resolvedHandoffSha256=handoff_hash,
                       resolvedHandoff=data, handoffValidation=validation)
        event(directory, "resolved", updated, evidence)
        temp = directory / ("state-" + uuid.uuid4().hex + ".json")
        write_new(temp, updated)
        os.replace(temp, directory / "reservation.json")
    return updated


def release(root, session, operation, evidence):
    record = load(Path(root) / "reservation.json")
    owner(record, session, operation)
    require(record["outcome"] != "unknown", "UNKNOWN_REQUIRES_RESOLUTION")
    require(isinstance(evidence, str) and evidence.strip(), "CLEANUP_EVIDENCE_REQUIRED")
    with mutation(root) as directory:
        same_reservation(directory / "reservation.json", record)
        event(directory, "released", record, evidence)
        (directory / "reservation.json").unlink()
    return {"released": True, "historyPreserved": True}


def git(source, *args):
    return subprocess.check_output(["git", "-C", str(source), *args], text=True, encoding="utf-8").strip()


def validate_handoff(data, observed, read_git=git):
    """Validate deployed latest-state handoff only; candidate handoff unsupported."""
    require(data["expectedInspect"] == identity(observed), "STALE_INSPECT")
    for key, actual in (("sourceCommit", "source"), ("package", "package"),
                        ("nativeIdentity", "web"), ("schemaIdentity", "schema")):
        require(data.get(key) == observed[actual], "DEPLOYED_IDENTITY_" + key)
    source = Path(data["sourcePath"])
    require(source.is_absolute() and source.is_dir(), "SOURCE_PATH")
    require(not any(p.is_symlink() for p in [source, *source.parents]), "SOURCE_SYMLINK")
    require(Path(read_git(source, "rev-parse", "--show-toplevel")).resolve() == source.resolve(), "SOURCE_ROOT")
    require(read_git(source, "rev-parse", "HEAD") == data["sourceCommit"], "SOURCE_COMMIT")
    require(not read_git(source, "status", "--porcelain", "--untracked-files=all"), "SOURCE_DIRTY")
    for key in ("package", "nativeIdentity", "schemaIdentity", "verificationEvidence", "cleanupEvidence"):
        require(isinstance(data.get(key), str) and data[key].strip(), "HANDOFF_" + key)
    require(data["schemaIdentity"] == observed["schema"], "SCHEMA_CHANGED_WEB_ONLY")
    flags = data.get("runtimeFeatures")
    require(isinstance(flags, dict) and flags and all(type(v) is bool for v in flags.values()), "FEATURE_FLAGS")
    require(type(data.get("webOnly")) is bool, "WEB_ONLY_REQUIRED")
    require("aiSidecarActive" in flags, "SIDECAR_STATE_REQUIRED")
    require(not (flags["aiSidecarActive"] and data["webOnly"]), "ACTIVE_SIDECAR_REQUIRES_COORDINATED_RELEASE")
    return {"status": "DeployedHandoffLocallyValidated", "package": data["package"],
            "serverGuardsStillRequired": True, "crossPcExclusionGuaranteed": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["reserve", "inspect", "transition", "resolve", "release", "handoffvalidate"])
    parser.add_argument("--root", type=Path)
    parser.add_argument("--input", type=Path, help="JSON arguments; no credentials")
    args = parser.parse_args()
    try:
        values = load(args.input) if args.input else {}
        if args.command == "handoffvalidate": result = validate_handoff(**values)
        elif args.command == "inspect": result = inspect(args.root)
        else: result = globals()[args.command](args.root, **values)
        print(json.dumps(result, ensure_ascii=False))
    except Exception as e:
        print(json.dumps({"status": "StoppedNoAutomaticRetry", "reason": str(e) if isinstance(e, Rejected) else type(e).__name__}))
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
