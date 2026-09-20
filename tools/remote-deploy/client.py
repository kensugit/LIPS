"""Codex-side restricted deployment client. Always verifies a separately pinned SSH host key."""
import argparse
import hashlib
from pathlib import Path
import re
import subprocess
import sys

def digest_file(path):
    h=hashlib.sha256()
    with Path(path).open('rb') as source:
        for block in iter(lambda: source.read(1024*1024),b''): h.update(block)
    return h.hexdigest()

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action',choices=['status','upload','deploy','rollback'])
    parser.add_argument('--package',type=Path)
    parser.add_argument('--sha256')
    parser.add_argument('--key',type=Path,default=Path.home()/'.ssh/lips_codex_ed25519')
    parser.add_argument('--known-hosts',type=Path,default=Path.home()/'.ssh/lips_known_hosts')
    args=parser.parse_args()
    if not args.key.is_file() or not args.known_hosts.is_file(): parser.error('Dedicated key and out-of-band verified host key are required')
    command=args.action
    if args.action=='upload':
        if not args.package or not args.package.is_file(): parser.error('--package required')
        sha=digest_file(args.package)
        if args.sha256 and args.sha256!=sha: parser.error('Local package SHA256 mismatch')
        command+=' '+sha
    elif args.action=='deploy':
        if not re.fullmatch('[0-9a-f]{64}',args.sha256 or ''): parser.error('--sha256 required')
        command+=' '+args.sha256
    elif args.package or args.sha256: parser.error('Package arguments are not valid for this action')
    ssh=['ssh','-T','-p','22222','-i',str(args.key),'-o','IdentitiesOnly=yes','-o','BatchMode=yes',
        '-o','StrictHostKeyChecking=yes','-o','UserKnownHostsFile='+str(args.known_hosts),
        '-o','ConnectTimeout=10','-o','ServerAliveInterval=15','-o','ServerAliveCountMax=3',
        'catalogdeploy@192.168.1.5',command]
    if args.action=='upload':
        with args.package.open('rb') as data: return subprocess.run(ssh,stdin=data,check=False).returncode
    return subprocess.run(ssh,stdin=subprocess.DEVNULL,check=False).returncode

if __name__=='__main__': sys.exit(main())
