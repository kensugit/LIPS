#!/usr/bin/python3
"""Forced SSH command. No shell, arbitrary paths, forwarding, or database commands."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile

MAX_UPLOAD = 4 * 1024**3
INCOMING = Path('/home/catalogdeploy/incoming')

def file_digest(path):
    digest = hashlib.sha256()
    with path.open('rb') as source:
        for chunk in iter(lambda: source.read(1024*1024), b''): digest.update(chunk)
    return digest.hexdigest()

def parse_command(command):
    if command in ('status', 'rollback'):
        return command, None
    match = re.fullmatch(r'(upload|deploy) ([0-9a-f]{64})', command)
    if not match:
        raise ValueError('Allowed: status, upload <sha256>, deploy <approved-sha256>, rollback')
    return match.group(1), match.group(2)

def receive(stream, directory, expected, limit=MAX_UPLOAD):
    directory = Path(directory)
    if directory.is_symlink() or not directory.is_dir():
        raise ValueError('Incoming directory is not available')
    digest = hashlib.sha256(); count = 0
    fd, temp = tempfile.mkstemp(prefix='.upload-', dir=directory)
    try:
        with os.fdopen(fd, 'wb') as output:
            while True:
                chunk = stream.read(1024*1024)
                if not chunk: break
                count += len(chunk)
                if count > limit: raise ValueError('Upload exceeds limit')
                digest.update(chunk); output.write(chunk)
            output.flush(); os.fsync(output.fileno())
        if count == 0 or digest.hexdigest() != expected:
            raise ValueError('Upload empty or SHA256 mismatch')
        target = directory / (expected + '.tar')
        if target.exists() or target.is_symlink():
            if target.is_symlink() or not target.is_file() or file_digest(target) != expected:
                raise ValueError('Existing upload conflicts with SHA256')
            return {'uploaded':True,'sha256':expected,'bytes':count,'alreadyPresent':True}
        os.rename(temp, target)
        return {'uploaded':True,'sha256':expected,'bytes':count,'alreadyPresent':False}
    finally:
        if os.path.exists(temp): os.unlink(temp)

def main():
    action, sha = parse_command(os.environ.get('SSH_ORIGINAL_COMMAND',''))
    os.umask(0o077)
    if action == 'upload':
        print(json.dumps(receive(sys.stdin.buffer, INCOMING, sha)))
        return 0
    args = ['/usr/bin/sudo','-n','/usr/local/sbin/catalogctl',action]
    if action == 'rollback': args = ['/usr/bin/sudo','-n','/usr/local/sbin/lips-codex-rollback']
    elif action == 'deploy': args.append(sha)
    return subprocess.run(args, stdin=subprocess.DEVNULL, env={'PATH':'/usr/sbin:/usr/bin:/sbin:/bin','LANG':'C.UTF-8'}, check=False).returncode

if __name__ == '__main__':
    try: sys.exit(main())
    except (ValueError,OSError) as error:
        print(str(error),file=sys.stderr); sys.exit(2)
