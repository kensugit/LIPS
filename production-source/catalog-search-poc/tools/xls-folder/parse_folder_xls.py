"""Bounded read-only adapter for the approved IROHA October 2026 XLS layout."""
import contextlib
import hashlib
import io
import json
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).with_name('vendor')))


def parse(data, supplier):
    if supplier != 'IROHA' or not data.startswith(bytes.fromhex('D0CF11E0A1B11AE1')):
        return dict(ok=False, code='UnsupportedXlsLayout', message='いろはわいんの対応旧Excel形式ではありません。',
                    remedy='いろはわいんの原本.xlsと取引先フォルダーを確認してください。')
    try:
        from iroha_source import iroha
        with tempfile.TemporaryDirectory(prefix='lips-iroha-') as tmp:
            path=Path(tmp)/'source.xls'
            path.write_bytes(data)
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                bundle=iroha(path)
        return dict(ok=True, version=bundle['ParserVersion'], rows=bundle['Rows'],
                    observedAt=bundle['ObservedAt'], hash=hashlib.sha256(data).hexdigest().upper())
    except ImportError:
        return dict(ok=False, code='XlsReaderUnavailable', message='旧Excel読取り環境が不足しています。',
                    remedy='管理担当者が同梱xlrd 2.0.2とPython実行パスを確認してください。')
    except Exception:
        return dict(ok=False, code='XlsExtractionFailed', message='Excelの表題・日付・列・商品行・価格・在庫を安全に読み取れませんでした。登録していません。',
                    remedy='対応原本はStocklist_IROHA_20261001.xlsです。変更された帳票・日付・記号は管理担当者へ確認してください。')


if __name__ == '__main__':
    data=sys.stdin.buffer.read(20_000_001)
    result=parse(data,sys.argv[1]) if len(data)<=20_000_000 else dict(ok=False,code='FileTooLarge',message='20MBを超えています。',remedy='原本のサイズを確認してください。')
    sys.stdout.buffer.write(json.dumps(result,ensure_ascii=False,allow_nan=False).encode('utf-8'))
