import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest

spec=importlib.util.spec_from_file_location('prepare',Path(__file__).resolve().parents[1]/'tools/remote-deploy/prepare.py')
prepare=importlib.util.module_from_spec(spec);spec.loader.exec_module(prepare)

class PrepareTests(unittest.TestCase):
    def archive(self,path,extra=None,corrupt=False):
        files={n:b'fixture' for n in ['publish/CatalogSearch.Web.dll','publish/CatalogSearch.Web.deps.json','publish/CatalogSearch.Web.runtimeconfig.json','publish/wwwroot/index.html']}
        manifest=dict(version=1,expectedPackage='a'*64,expectedWebImage='sha256:'+'b'*64,expectedDbImage='sha256:'+'c'*64,schemaSha256='d'*64,sourceCommit='e'*40,files={k:hashlib.sha256(v).hexdigest() for k,v in files.items()})
        if corrupt:manifest['files']['publish/CatalogSearch.Web.dll']='0'*64
        files['manifest.json']=json.dumps(manifest).encode()
        with tarfile.open(path,'w') as t:
            for name,data in files.items():
                m=tarfile.TarInfo(name);m.size=len(data);t.addfile(m,io.BytesIO(data))
            if extra is not None:t.addfile(extra,io.BytesIO(b''))
        return manifest
    def test_valid_package(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);manifest=self.archive(root/'payload.tar');out=root/'out';out.mkdir()
            self.assertEqual(manifest,prepare.unpack(root/'payload.tar',out))
            self.assertEqual(b'fixture',(out/'publish/wwwroot/index.html').read_bytes())
    def test_malicious_paths_links_duplicates_and_extra_files(self):
        for name in ['../escape','publish/../escape','/tmp/escape','publish//foo','Dockerfile','prepare.sh','manifest.json','publish/evil\\file','publish/./foo']:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as d:
                root=Path(d);self.archive(root/'payload.tar',tarfile.TarInfo(name));out=root/'out';out.mkdir()
                with self.assertRaises(ValueError):prepare.unpack(root/'payload.tar',out)
        for kind in [tarfile.SYMTYPE,tarfile.LNKTYPE,tarfile.FIFOTYPE,tarfile.CHRTYPE]:
            with tempfile.TemporaryDirectory() as d:
                root=Path(d);m=tarfile.TarInfo('publish/link');m.type=kind;m.linkname='/etc/passwd';self.archive(root/'payload.tar',m);out=root/'out';out.mkdir()
                with self.assertRaises(ValueError):prepare.unpack(root/'payload.tar',out)
    def test_hash_mismatch_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);self.archive(root/'payload.tar',corrupt=True);out=root/'out';out.mkdir()
            with self.assertRaises(ValueError):prepare.unpack(root/'payload.tar',out)

if __name__=='__main__':unittest.main()
