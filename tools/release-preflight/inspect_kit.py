"""Offline wrapper/control audit. No receiver execution, network or file writes."""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import re

def digest(value):return hashlib.sha256(value).hexdigest().upper()
def validate_activation_pins(directory):
    activation=(directory/'Activate-Receive.ps1').read_text('utf-8-sig')
    pins=re.findall(r"@\{name='([^']+)';sha='([0-9A-F]{64})'\}",activation)
    required={'control-hashes.json','Read-TransferReadiness.ps1','Start-LipsR4.ps1'}
    if len(pins)!=3 or {name for name,_ in pins}!=required:
        raise ValueError('Activation pin format or required controls differ')
    for name,sha in pins:
        if digest((directory/name).read_bytes())!=sha:
            raise ValueError('Activation control pin differs: '+name)

def validate_bundle(bundle, directory):
    seen=set()
    for item in bundle['files']:
        name=item['name']
        if Path(name).name!=name or any(c in name for c in ('/','\\',':')) or name in seen:
            raise ValueError('Unsafe or duplicate control name')
        seen.add(name)
        value=base64.b64decode(item['base64'],validate=True)
        if len(value)!=item['length'] or digest(value)!=item['sha256']:
            raise ValueError('Embedded control bytes/hash differ: '+name)
        if value!=(directory/name).read_bytes():raise ValueError('Wrapper/kit bytes differ: '+name)
    for required in ('Activate-Receive.ps1','ReceiverBase.ps1','BulkTransfer.cs','candidate-manifest.json','control-hashes.json','Start-LipsR4.ps1','Read-TransferReadiness.ps1','Receive-LipsR4.ps1','Stage-Payload.ps1'):
        if required not in seen:raise ValueError('Missing control: '+required)
    pins=json.loads((directory/'control-hashes.json').read_text('utf-8-sig'))
    for item in pins['files']:
        if item['name'] not in seen:raise ValueError('Pinned file not embedded')
        value=(directory/item['name']).read_bytes()
        if len(value)!=item['length'] or digest(value)!=item['sha256']:raise ValueError('Control pin differs')
    validate_activation_pins(directory)
    return len(seen)

def audit(wrapper, directory, sources):
    text=wrapper.read_text('utf-8-sig')
    encoded=re.findall(r"FromBase64String\('([^']+)'\)",text)
    if len(encoded)!=1:raise ValueError('Expected one wrapper bundle')
    bundle=json.loads(base64.b64decode(encoded[0],validate=True))
    count=validate_bundle(bundle,directory)
    manifest=json.loads((directory/'candidate-manifest.json').read_text('utf-8-sig'))
    approved=json.loads((directory/'approved-transfer.json').read_text('utf-8-sig'))
    if manifest!=approved:raise ValueError('Receiver/sender manifests differ')
    names=set();kinds=set()
    for f in manifest['files']:
        name=f['name']
        if Path(name).name!=name or any(c in name for c in ('/','\\',':')) or name in names:raise ValueError('Invalid target name')
        names.add(name);kinds.add(f['kind'])
        raw=(sources/name).read_bytes()
        if len(raw)!=f['length'] or digest(raw)!=f['sha256']:raise ValueError('Transfer source differs: '+name)
    if len(names)!=2 or kinds!={'zip','proof'}:raise ValueError('Expected fixed ZIP/proof pair')
    return dict(status='WrapperAndSourcesVerified',packageId=manifest['packageId'],controls=count,wrapperSha256=digest(wrapper.read_bytes()),receiverExecuted=False)

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--wrapper',required=True,type=Path);p.add_argument('--kit',required=True,type=Path);p.add_argument('--sources',required=True,type=Path)
    a=p.parse_args();print(json.dumps(audit(a.wrapper,a.kit,a.sources)))
if __name__=='__main__':main()
