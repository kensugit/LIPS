import copy
import json
import socket
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from types import SimpleNamespace
import import_answers as cli

class WorkflowTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.original = b'synthetic-original-only'
        self.posts = 0
        self.mode = 'ok'
        self.body = {'schema':'lips-source-review-answers/v1', 'items':{}}
        for key, text in ((cli.VOLUME_ID, cli.VOLUME_TEXT), (cli.REGION_ID, cli.REGION_TEXT)):
            self.body['items'][key] = dict(binding=key[7:]+'0'*48, choice='回答', version=1, text=text, history=[])
        self.review = dict(hash=cli.sha(self.original),version=3,state='Held',mappingResolutions=[],history=[],
            result=dict(questionKind='Mapping',questions=[self.question('v','VolumeUnit','H/容量','0.75',['L']),
                self.question('r','RegionAbbreviation','C/AL','AL',['アルザス'])]))
        owner = self
        class Handler(BaseHTTPRequestHandler):
            def log_message(self,*args): pass
            def do_GET(self):
                self.send_response(200); self.end_headers()
                self.wfile.write(owner.original if self.path.endswith('/original') else json.dumps(owner.review).encode())
            def do_POST(self):
                owner.posts += 1
                assert self.path.endswith('/answer')
                assert self.headers['X-Catalog-Action']=='local-demo'
                assert self.headers['Origin']==owner.args.base_url
                payload=json.loads(self.rfile.read(int(self.headers['Content-Length'])))
                if owner.mode == 'conflict':
                    self.send_response(409);self.end_headers();return
                if owner.mode != 'lost-before':
                    assert payload['version']==owner.review['version']
                    resolutions=[dict(question=copy.deepcopy(next(q for q in owner.review['result']['questions']
                        if q['id']==a['questionId'])),meaning=a['meaning'],evidence=payload['reason']) for a in payload['mappingAnswers']]
                    owner.review['mappingResolutions'] += resolutions
                    owner.review['version']+=1
                    owner.review['history'].append(dict(version=owner.review['version'],method='Human',action='mapping',
                        reason=payload['reason'],mappingResolutions=resolutions))
                if owner.mode.startswith('lost'):
                    self.connection.shutdown(socket.SHUT_RDWR);self.connection.close();return
                self.send_response(200);self.end_headers();self.wfile.write(json.dumps(owner.review).encode())
        self.server=ThreadingHTTPServer(('127.0.0.1',0),Handler)
        self.thread=threading.Thread(target=self.server.serve_forever,daemon=True);self.thread.start()
        self.args=SimpleNamespace(answers=str(self.root/'answers.json'),pending=str(self.root/'pending.json'),
            base_url='http://127.0.0.1:'+str(self.server.server_port),review_id='11111111-1111-1111-1111-111111111111',
            expected_source_sha=cli.sha(self.original),expected_columns_fingerprint='abc',save=False,
            volume_binding=self.body['items'][cli.VOLUME_ID]['binding'],region_binding=self.body['items'][cli.REGION_ID]['binding'])
        Path(self.args.pending).write_text(json.dumps([dict(supplier='ラヴィネ',sheet='202610LaVineeリスト',sha256=cli.sha(self.original))]),encoding='utf8')
        self.write_answers()
    def question(self,id,kind,key,raw,choices):
        return dict(id=id,scope=dict(supplierId='LA_VINEE',documentKind='LaVinee/1',columnsFingerprint='abc'),
            kind=kind,sourceKey=key,rawValue=raw,choices=choices,row=2,sheet='202610LaVineeリスト',sku='1')
    def write_answers(self):
        data=json.dumps(self.body).encode();Path(self.args.answers).write_bytes(data);self.args.expected_answer_sha=cli.sha(data)
    def tearDown(self):
        self.server.shutdown();self.server.server_close();self.thread.join();self.temp.cleanup()
    def test_preview_save_and_rerun(self):
        self.assertEqual(cli.run(self.args)['toSave'],2);self.assertEqual(self.posts,0)
        self.args.save=True
        self.assertEqual(cli.run(self.args)['status'],'saved_verified')
        self.assertEqual(cli.run(self.args)['status'],'already_saved');self.assertEqual(self.posts,1)
    def test_binding_tamper_even_rehashed(self):
        self.body['items'][cli.VOLUME_ID]['binding']='changed';self.write_answers()
        with self.assertRaisesRegex(cli.Stop,'received_item'):cli.run(self.args)
    def test_answer_hash(self):
        self.args.expected_answer_sha='0'*64
        with self.assertRaisesRegex(cli.Stop,'answer_hash'):cli.run(self.args)
    def test_original_hash(self):
        self.original=b'changed'
        with self.assertRaisesRegex(cli.Stop,'original_hash'):cli.run(self.args)
    def test_unknown_state(self):
        self.review['state']='Unknown';self.args.save=True
        with self.assertRaisesRegex(cli.Stop,'state'):cli.run(self.args)
        self.assertEqual(self.posts,0)
    def test_conflict(self):
        self.mode='conflict';self.args.save=True
        with self.assertRaisesRegex(cli.Stop,'not_confirmed'):cli.run(self.args)
        self.assertEqual(self.posts,1)
    def test_lost_committed_response(self):
        self.mode='lost-after';self.args.save=True
        self.assertEqual(cli.run(self.args)['status'],'saved_reconciled');self.assertEqual(self.posts,1)
    def test_lost_uncommitted_response(self):
        self.mode='lost-before';self.args.save=True
        with self.assertRaisesRegex(cli.Stop,'not_confirmed'):cli.run(self.args)
        self.assertEqual(self.posts,1)
    def test_unknown_region_and_bad_volume_remain_unanswered(self):
        self.review['result']['questions'] += [self.question('u','RegionAbbreviation','C/XX','XX',[]),self.question('z','VolumeUnit','H/容量','0',['L'])]
        result=cli.run(self.args)
        self.assertEqual(result['toSave'],2);self.assertEqual(result['excluded'],dict(unknownRegion=1,invalidVolume=1,unansweredPriceUnit=0))
    def test_price_unit_is_not_in_received_answers(self):
        self.review['result']['questions'].append(self.question('p','ColumnMeaning','J/納価単位','1200',['PerBottle']))
        result=cli.run(self.args)
        self.assertEqual(result['toSave'],2);self.assertEqual(result['excluded']['unansweredPriceUnit'],1);self.assertEqual(self.posts,0)
    def test_existing_different_answer(self):
        self.review['mappingResolutions']=[dict(question=self.review['result']['questions'][0],meaning='mL')]
        with self.assertRaisesRegex(cli.Stop,'saved_answer_conflict'):cli.run(self.args)
    def test_other_scope(self):
        self.review['result']['questions'][0]['scope']['supplierId']='LUC'
        with self.assertRaisesRegex(cli.Stop,'scope'):cli.run(self.args)
    def test_region_key_cannot_relabel_raw(self):
        self.review['result']['questions'][1]['sourceKey']='C/BG'
        with self.assertRaisesRegex(cli.Stop,'region_source'):cli.run(self.args)

if __name__=='__main__':unittest.main()
