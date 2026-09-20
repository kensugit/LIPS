"""Integration checks against the self-contained gateway and an isolated backend; no production writes."""
import concurrent.futures
import hashlib
import http.client
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import subprocess
import threading
import time
import unittest

ROOT = Path(__file__).resolve().parents[1]
EXE = ROOT / 'artifacts/lan-package/gateway/LipsLanGateway.exe'
BACKEND_PORT = 55571
GATEWAY_PORT = 55572
ORIGIN = f'http://127.0.0.1:{GATEWAY_PORT}'

class Backend(BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'
    def handle(self):
        try: super().handle()
        except ConnectionResetError: pass  # Gateway process exit closes pooled fixture sockets.
    def log_message(self, *args): pass
    def do_GET(self):
        if self.path.startswith('/redirect'):
            self.send_response(302); self.send_header('Location', '/landing'); self.send_header('Content-Length','0'); self.end_headers(); return
        self.answer()
    def do_POST(self): self.answer()
    def answer(self):
        body = self.rfile.read(int(self.headers.get('Content-Length', '0')))
        valid = self.headers.get('Host') == f'127.0.0.1:{BACKEND_PORT}'
        if self.command == 'POST':
            valid = valid and self.headers.get('Origin') == f'http://127.0.0.1:{BACKEND_PORT}'
        data = json.dumps({'method': self.command, 'path': self.path, 'bytes': len(body),
            'hash': hashlib.sha256(body).hexdigest(), 'type': self.headers.get('Content-Type'),
            'forwarded': self.headers.get('X-Forwarded-For'), 'host': self.headers.get('Host')}).encode()
        self.send_response(200 if valid else 403)
        self.send_header('Content-Type', 'application/json'); self.send_header('Content-Length', str(len(data)))
        self.end_headers(); self.wfile.write(data)

@unittest.skipUnless(EXE.exists(), 'Publish the gateway to artifacts/lan-package/gateway first')
class GatewayTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        class FixtureServer(ThreadingHTTPServer):
            request_queue_size = 128
        cls.backend = FixtureServer(('127.0.0.1', BACKEND_PORT), Backend)
        cls.thread = threading.Thread(target=cls.backend.serve_forever, daemon=True); cls.thread.start()
        env = dict(os.environ, PublicOrigin=ORIGIN, Upstream=f'http://127.0.0.1:{BACKEND_PORT}/')
        cls.log = (ROOT/'artifacts/gateway-test.log').open('w')
        cls.process = subprocess.Popen([str(EXE)], cwd=EXE.parent, env=env, stdout=cls.log, stderr=cls.log)
        for _ in range(60):
            try:
                if cls.request('GET', '/')[0] == 200: break
            except OSError: pass
            time.sleep(0.2)
        else:
            cls.tearDownClass(); raise RuntimeError('Gateway startup failed')
    @classmethod
    def tearDownClass(cls):
        cls.process.terminate(); cls.process.wait(timeout=10)
        cls.backend.shutdown(); cls.backend.server_close(); cls.log.close()
    @staticmethod
    def request(method, path, body=None, headers=None):
        conn = http.client.HTTPConnection('127.0.0.1', GATEWAY_PORT, timeout=15)
        try:
            conn.request(method, path, body=body, headers=headers or {})
            r=conn.getresponse(); return r.status, dict(r.getheaders()), r.read()
        finally: conn.close()
    def test_search_query_and_origin_host_transform(self):
        status, headers, data = self.request('GET', '/api/search?text=FINESSE-&available=true',
            headers={'X-Forwarded-For':'8.8.8.8'})
        self.assertEqual(200,status); parsed=json.loads(data)
        self.assertEqual('/api/search?text=FINESSE-&available=true', parsed['path'])
        self.assertIsNone(parsed['forwarded']); self.assertEqual('lan-v1',headers['X-LIPS-Gateway'])
    def test_json_review_is_forwarded_after_external_origin_validation(self):
        body=b'{"action":"hold","id":"fixture"}'
        status, _, data=self.request('POST','/api/review',body,{'Origin':ORIGIN,'X-Catalog-Action':'local-demo','Content-Type':'application/json'})
        self.assertEqual(200,status); self.assertEqual(hashlib.sha256(body).hexdigest(),json.loads(data)['hash'])
    def test_multipart_file_upload_preserves_all_bytes(self):
        body=b'--boundary\r\nContent-Disposition: form-data; name="file"; filename="fixture.csv"\r\n\r\n'+os.urandom(2_000_000)+b'\r\n--boundary--\r\n'
        status, _, data=self.request('POST','/api/import',body,{'Origin':ORIGIN,'X-Catalog-Action':'local-demo','Content-Type':'multipart/form-data; boundary=boundary'})
        self.assertEqual(200,status); self.assertEqual(hashlib.sha256(body).hexdigest(),json.loads(data)['hash'])
    def test_foreign_missing_origin_and_wrong_host_are_rejected(self):
        for headers in [{}, {'Origin':'http://evil.invalid','X-Catalog-Action':'local-demo'}, {'Origin':ORIGIN},
                        {'Origin':ORIGIN,'X-Catalog-Action':'local-demo','Sec-Fetch-Site':'cross-site'}]:
            self.assertEqual(403,self.request('POST','/api/review',b'{}',headers)[0])
        self.assertEqual(403,self.request('GET','/',headers={'Host':'evil.invalid'})[0])
        self.assertEqual(403,self.request('OPTIONS','/')[0])
    def test_fixture_insertion_is_not_published(self):
        self.assertEqual(404,self.request('POST','/api/fixtures/internal',b'{}',{'Origin':ORIGIN,'X-Catalog-Action':'local-demo'})[0])
    def test_redirect_is_returned_without_following(self):
        status, headers, _=self.request('GET','/redirect'); self.assertEqual(302,status); self.assertEqual('/landing',headers['Location'])
    def test_concurrent_requests(self):
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
            results=list(executor.map(lambda _: self.request('GET','/api/options')[0],range(20)))
        self.assertEqual([200]*20,results)

if __name__ == '__main__': unittest.main()
