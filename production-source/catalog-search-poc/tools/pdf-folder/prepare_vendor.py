"""Build-time only: verify pinned wheels and materialize private Web dependencies."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import stat
import zipfile


def prepare(wheel_directory, destination, lock_path):
    lock = json.loads(lock_path.read_text(encoding="utf-8"))
    if lock["schema"] != "lips-pdf-vendor/v1" or lock["platform"] != "linux-x86_64" or lock["python"] != "3.12":
        raise ValueError("Unexpected dependency contract")
    actual = {p.name: p for p in wheel_directory.glob("*.whl")}
    if set(actual) != set(lock["wheels"]):
        raise ValueError("Wheel inventory differs from lock")
    entries = []
    names = set()
    for name, expected in lock["wheels"].items():
        wheel = actual[name]
        if hashlib.sha256(wheel.read_bytes()).hexdigest() != expected["sha256"]:
            raise ValueError("Wheel hash mismatch")
        with zipfile.ZipFile(wheel) as archive:
            files = [i for i in archive.infolist() if not i.is_dir()]
            if len(files) != expected["files"]:
                raise ValueError("Wheel entry count differs")
            for item in files:
                path = PurePosixPath(item.filename)
                if (path.is_absolute() or ".." in path.parts or not re.fullmatch(r"[A-Za-z0-9_./-]+", item.filename)
                    or stat.S_ISLNK(item.external_attr >> 16) or item.filename in names):
                    raise ValueError("Unexpected wheel path or duplicate")
                if item.file_size > 80_000_000:
                    raise ValueError("Wheel file too large")
                names.add(item.filename)
                entries.append((wheel, item.filename, item.file_size))
    if len(entries) > 800 or sum(n for _, _, n in entries) > 100_000_000:
        raise ValueError("Dependency size limit")
    # Validate every entry before any filesystem mutation. Existing files must match.
    root = destination.resolve()
    proof = {}
    for wheel in actual.values():
        with zipfile.ZipFile(wheel) as archive:
            for item in archive.infolist():
                if item.is_dir(): continue
                target = (root / item.filename).resolve()
                if not target.is_relative_to(root): raise ValueError("Path outside destination")
                data = archive.read(item)
                digest = hashlib.sha256(data).hexdigest()
                if target.exists() and (not target.is_file() or hashlib.sha256(target.read_bytes()).hexdigest() != digest):
                    raise ValueError("Existing vendor content differs")
                target.parent.mkdir(parents=True, exist_ok=True)
                if not target.exists(): target.write_bytes(data)
                proof[item.filename] = {"size": len(data), "sha256": digest}
    if {p.relative_to(root).as_posix() for p in root.rglob("*") if p.is_file()} != set(proof):
        raise ValueError("Unexpected existing vendor files")
    return {"schema": lock["schema"], "platform": lock["platform"], "python": lock["python"], "files": proof}


if __name__ == "__main__":
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--wheels", type=Path, required=True)
    cli.add_argument("--destination", type=Path, required=True)
    cli.add_argument("--proof", type=Path, required=True)
    args = cli.parse_args()
    proof = prepare(args.wheels, args.destination, Path(__file__).with_name("linux-dependencies.lock.json"))
    args.proof.write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"filesVerified": len(proof["files"]), "existingRendererModified": False}))
