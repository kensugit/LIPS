"""Import only explicitly received LaVinee meanings; never resume or publish rules."""
import argparse
import hashlib
import json
import re
import unicodedata
import urllib.error
import urllib.parse
import urllib.request
import uuid
from decimal import Decimal, InvalidOperation
from pathlib import Path

VOLUME_ID = 'source-2f51f54e2e9e798b'
REGION_ID = 'source-597a0d41f23d74aa'
REGIONS = dict(zip('AL BG BX CP JS LO LR PC RH SO'.split(),
    'アルザス ブルゴーニュ ボルドー シャンパーニュ ジュラサヴォワ ロワール ラングドックルーション プロヴァンス ローヌ スッドウェスト'.split()))
VOLUME_TEXT = '容量単位は「ｌ」(リットル)です。'
REGION_TEXT = '\n'.join(k + '　' + v for k, v in REGIONS.items())

class Stop(Exception):
    pass

def require(condition, code):
    if not condition:
        raise Stop(code)

def sha(data):
    return hashlib.sha256(data).hexdigest()

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        raise Stop('redirect_refused')

class Client:
    def __init__(self, base, review):
        parsed = urllib.parse.urlsplit(base)
        require(parsed.scheme == 'http' and parsed.hostname in ('127.0.0.1', 'localhost', '::1')
                and not parsed.username and not parsed.password and parsed.path in ('', '/')
                and not parsed.query and not parsed.fragment, 'loopback_url_required')
        self.base = base.rstrip('/')
        self.url = self.base + '/api/import/reviews/' + str(uuid.UUID(review))
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())

    def request(self, suffix='', payload=None, raw=False):
        data = None if payload is None else json.dumps(payload).encode()
        request = urllib.request.Request(self.url + suffix, data=data, headers={
            'Origin': self.base, 'X-Catalog-Action': 'local-demo', 'Content-Type': 'application/json'})
        with self.opener.open(request, timeout=30) as response:
            body = response.read(65 * 1024 * 1024)
        return body if raw else json.loads(body)

def run(args):
    answer_bytes = Path(args.answers).read_bytes()
    require(sha(answer_bytes) == args.expected_answer_sha.lower(), 'answer_hash_mismatch')
    received = json.loads(answer_bytes)
    require(received.get('schema') == 'lips-source-review-answers/v1', 'answer_schema_mismatch')
    for key, binding, text in ((VOLUME_ID, args.volume_binding, VOLUME_TEXT),
                               (REGION_ID, args.region_binding, REGION_TEXT)):
        item = received['items'].get(key, {})
        require(item.get('binding') == binding and binding.startswith(key.removeprefix('source-'))
                and item.get('choice') == '回答' and item.get('version') == 1
                and item.get('text') == text and item.get('history') == [], 'received_item_mismatch')
    pending = json.loads(Path(args.pending).read_text(encoding='utf-8-sig'))
    records = [r for r in pending if r.get('supplier') in ('ラヴィネ', 'LA_VINEE')]
    require(len(records) == 1 and records[0].get('sha256', '').lower() == args.expected_source_sha.lower(),
            'pending_source_mismatch')
    client = Client(args.base_url, args.review_id)
    review = client.request()
    require(review.get('hash', '').lower() == args.expected_source_sha.lower(), 'review_hash_mismatch')
    require(sha(client.request('/original', raw=True)) == args.expected_source_sha.lower(), 'original_hash_mismatch')
    require(review.get('state') in ('Held', 'Ready', 'Partial'), 'review_state_refused')
    questions = (review.get('result') or {}).get('questions') or []
    require(questions and (review.get('result') or {}).get('questionKind') == 'Mapping', 'mapping_questions_required')
    desired = {}
    by_id = {}
    seen = set()
    excluded = {'unknownRegion': 0, 'invalidVolume': 0, 'unansweredPriceUnit': 0}
    for q in questions:
        scope = q.get('scope', {})
        require(scope.get('supplierId') == 'LA_VINEE' and scope.get('documentKind') == 'LaVinee/1'
                and scope.get('columnsFingerprint', '').lower() == args.expected_columns_fingerprint.lower()
                and not scope.get('productIdentity') and not scope.get('vintage'), 'question_scope_mismatch')
        require(q.get('id') and q['id'] not in seen and isinstance(q.get('row'), int)
                and q['row'] > 1 and q.get('sheet') == records[0].get('sheet'), 'question_identity_mismatch')
        seen.add(q['id'])
        if q.get('kind') == 'VolumeUnit' and q.get('sourceKey') == 'H/容量':
            meaning = 'L'
            raw = unicodedata.normalize('NFKC', q.get('rawValue', '')).strip()
            try:
                volume = Decimal(raw) * 1000 if re.fullmatch(r'[0-9]+(?:\.[0-9]+)?', raw) else Decimal(0)
                valid = volume > 0 and volume <= 2147483647 and volume == volume.to_integral_value()
            except InvalidOperation:
                valid = False
            if not valid:
                excluded['invalidVolume'] += 1
                continue
        elif q.get('kind') == 'RegionAbbreviation':
            code = re.sub(r'\s+', '', unicodedata.normalize('NFKC', q.get('rawValue', '')))
            require(q.get('sourceKey') == 'C/' + code, 'region_source_mismatch')
            if code not in REGIONS:
                require(q.get('choices') == [], 'unapproved_region_choices')
                excluded['unknownRegion'] += 1
                continue
            meaning = REGIONS[code]
        elif q.get('kind') == 'ColumnMeaning' and q.get('sourceKey') == 'J/納価単位':
            require(q.get('choices') == ['PerBottle'], 'price_unit_choices_mismatch')
            # The supplied JSON confirms capacity/regions, not the price unit.
            excluded['unansweredPriceUnit'] += 1
            continue
        else:
            raise Stop('question_kind_not_approved')
        require(q.get('choices') == [meaning], 'question_choices_mismatch')
        desired[q['id']] = meaning
        by_id[q['id']] = q
    saved = {}
    for resolution in review.get('mappingResolutions', []):
        q = resolution.get('question', {})
        require(q.get('id') in desired and q == by_id[q['id']] and q['id'] not in saved
                and resolution.get('meaning') == desired[q['id']], 'saved_answer_conflict')
        saved[q['id']] = resolution['meaning']
    answers = [{'questionId': k, 'meaning': v} for k, v in desired.items() if k not in saved]
    reason = 'Received answers SHA256=' + args.expected_answer_sha.lower() + '; items=' + VOLUME_ID + '@1,' + REGION_ID + '@1'
    payload = {'version': review['version'], 'hash': review['hash'], 'action': 'mapping',
               'date': None, 'reason': reason, 'mappingAnswers': answers}
    proof = {'answerSha256': sha(answer_bytes), 'sourceSha256': args.expected_source_sha.lower(),
             'questions': len(questions), 'eligible': len(desired), 'excluded': excluded,
             'alreadySaved': len(saved), 'toSave': len(answers),
             'payloadSha256': sha(json.dumps(payload, sort_keys=True).encode()), 'status': 'preview'}
    if not answers:
        proof['status'] = 'already_saved'
        return proof
    if not args.save:
        return proof
    uncertain = False
    try:
        client.request('/answer', payload)
    except Exception:
        # No POST retry. Even HTTP errors are reconciled, never interpreted as a save.
        uncertain = True
    try:
        after = client.request()
    except Exception:
        raise Stop('save_outcome_unknown_stop_no_retry') from None
    actual = {r['question']['id']: r for r in after.get('mappingResolutions', [])}
    matching_event = any(e.get('version') == review['version'] + 1 and e.get('method') == 'Human'
        and e.get('action') == 'mapping' and e.get('reason') == reason
        and {r['question']['id']: r['meaning'] for r in e.get('mappingResolutions') or []}
            == {a['questionId']: a['meaning'] for a in answers} for e in after.get('history', []))
    require(after.get('hash') == review['hash'] and after.get('version') == review['version'] + 1
            and after.get('state') in ('Held', 'Ready', 'Partial') and matching_event
            and all(k in actual and actual[k].get('question') == by_id[k]
                    and actual[k].get('meaning') == v for k, v in desired.items()),
            'save_not_confirmed_stop_no_retry')
    proof['status'] = 'saved_reconciled' if uncertain else 'saved_verified'
    proof['savedVersion'] = after['version']
    return proof

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('answers', 'pending', 'base-url', 'review-id', 'expected-answer-sha',
                 'expected-source-sha', 'expected-columns-fingerprint', 'volume-binding', 'region-binding'):
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--save', action='store_true')
    args = parser.parse_args()
    try:
        print(json.dumps(run(args), ensure_ascii=False))
    except Exception as error:
        # Do not expose request bodies, local paths, response bodies, or credentials.
        print(json.dumps({'status': 'stopped', 'code': str(error) if isinstance(error, Stop) else 'validation_or_transport_failure'}))
        return 2
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
