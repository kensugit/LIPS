"""Limited LibreOffice renderer candidate for immutable XLSX sources.

This candidate intentionally has a very small formula contract. It preserves
the workbook's single saved print area and hidden columns. A formula is
admitted only when it is a direct same-sheet reference from outside the print
area to an absent cell outside the print area. A dedicated LibreOffice profile
sets OOXML recalc-on-load to "never" before the office process starts, and the
worker compares the value immediately loaded by LibreOffice with the OOXML
cache before exporting anything.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import tempfile
import time
import uuid
import zipfile
import posixpath
import xml.etree.ElementTree as ET


NS = {'s': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
RID = '{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id'
CELL_RE = re.compile(r'^\$?([A-Z]{1,3})\$?([1-9][0-9]*)$')
DIRECT_REF_RE = CELL_RE
CELL_RANGE_RE = re.compile(
    r'^\$?([A-Z]{1,3})\$?([1-9][0-9]*):\$?([A-Z]{1,3})\$?([1-9][0-9]*)$')
COLUMN_RANGE_RE = re.compile(r'^\$?([A-Z]{1,3}):\$?([A-Z]{1,3})$')
MAX_ROW = 1_048_576
CALC_CHAIN_PATH = 'xl/calcChain.xml'
CALC_CHAIN_REL_TYPE = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain'
CALC_CHAIN_CONTENT_TYPE = 'application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml'
REL_NS = 'http://schemas.openxmlformats.org/package/2006/relationships'
CONTENT_TYPE_NS = 'http://schemas.openxmlformats.org/package/2006/content-types'
APPROVED_CHAIN_CELL = 'M941'
APPROVED_CHAIN_SHEET_ID = 1
APPROVED_CHAIN_DEPENDENCY = 'N941'


def column_index(name):
    value = 0
    for char in name:
        value = value * 26 + ord(char) - 64
    if not 1 <= value <= 16384:
        raise ValueError('印刷範囲の列が不正です。')
    return value - 1


def column_name(index):
    value = index + 1
    result = ''
    while value:
        value, rest = divmod(value - 1, 26)
        result = chr(65 + rest) + result
    return result


def parse_cell(value):
    match = CELL_RE.fullmatch(value or '')
    if not match:
        raise ValueError('セル参照が不正です。')
    row = int(match.group(2))
    if not 1 <= row <= MAX_ROW:
        raise ValueError('セル行がExcel上限を超えています。')
    return column_index(match.group(1)), row - 1


def split_sheet_range(value):
    if ',' in value:
        raise ValueError('複数領域の印刷範囲は描画しません。原典で確認してください。')
    match = re.fullmatch(r"('(?:[^']|'')+'|[^!]+)!(.+)", value or '')
    if not match:
        raise ValueError('印刷範囲の参照が不正です。')
    raw_sheet, cell_range = match.groups()
    sheet_name = raw_sheet[1:-1].replace("''", "'") if raw_sheet.startswith("'") else raw_sheet
    return sheet_name, cell_range


def parse_print_area(value, expected_sheet, last_row):
    sheet_name, cell_range = split_sheet_range(value)
    if sheet_name != expected_sheet:
        raise ValueError('印刷範囲のシート参照が対象シートと一致しません。')
    match = COLUMN_RANGE_RE.fullmatch(cell_range)
    if match:
        first_col, last_col = map(column_index, match.groups())
        first_row, end_row = 0, last_row - 1
    else:
        match = CELL_RANGE_RE.fullmatch(cell_range)
        if not match:
            raise ValueError('単一矩形以外の印刷範囲は描画しません。')
        first_col = column_index(match.group(1))
        first_row = int(match.group(2)) - 1
        last_col = column_index(match.group(3))
        end_row = int(match.group(4)) - 1
        if first_row >= MAX_ROW or end_row >= MAX_ROW:
            raise ValueError('印刷範囲の行がExcel上限を超えています。')
    if first_col > last_col or first_row > end_row or last_col >= 200:
        raise ValueError('印刷範囲が上限を超えるか逆順です。')
    return first_col, first_row, last_col, end_row


def parse_simple_range(value, expected_sheet):
    sheet_name, cell_range = split_sheet_range(value)
    if sheet_name != expected_sheet:
        raise ValueError('定義名が対象外シートを参照しています。')
    if not (CELL_RANGE_RE.fullmatch(cell_range) or COLUMN_RANGE_RE.fullmatch(cell_range)):
        raise ValueError('定義名の参照形式が対象外です。')


def sheet_path(archive, workbook, sheet_index):
    relations = {
        element.get('Id'): element.get('Target')
        for element in ET.fromstring(archive.read('xl/_rels/workbook.xml.rels'))
        if element.get('TargetMode') != 'External'
    }
    sheets = workbook.findall('s:sheets/s:sheet', NS)
    if not 1 <= sheet_index <= len(sheets):
        raise ValueError('シート番号が不正です。')
    entry = sheets[sheet_index - 1]
    target = relations.get(entry.get(RID), '')
    name = target.lstrip('/') if target.startswith('/') else 'xl/' + target
    if '..' in name or name not in archive.namelist():
        raise ValueError('シート参照が不正です。')
    return sheets, entry, name


def worksheet_last_row(root):
    rows = [int(row.get('r', '0')) for row in root.findall('s:sheetData/s:row', NS)]
    last = max(rows, default=1)
    if not 1 <= last <= MAX_ROW:
        raise ValueError('ワークシート行がExcel上限を超えています。')
    return last


def inside_area(column, row, area):
    first_col, first_row, last_col, last_row = area
    return first_col <= column <= last_col and first_row <= row <= last_row


def inspect_calc_chain(archive, formula_records, selected_sheet_id):
    """Admit only the exact one-cell calculation-chain shape used by this source."""
    calc_like = [name for name in archive.namelist()
                 if name.lower().endswith('calcchain.xml')]
    if not calc_like:
        return {'present': False, 'count': 0}
    if calc_like != [CALC_CHAIN_PATH] or archive.namelist().count(CALC_CHAIN_PATH) != 1:
        raise ValueError('計算連鎖partが単一の正規位置ではありません。')

    relations = ET.fromstring(archive.read('xl/_rels/workbook.xml.rels'))
    chain_relations = [element for element in relations
                       if element.tag == '{%s}Relationship' % REL_NS and
                       element.get('Type') == CALC_CHAIN_REL_TYPE]
    if len(chain_relations) != 1:
        raise ValueError('計算連鎖relationshipを一意に確認できません。')
    relation = chain_relations[0]
    if (set(relation.attrib) != {'Id', 'Type', 'Target'} or len(relation) or
            (relation.text or '').strip() or (relation.tail or '').strip()):
        raise ValueError('計算連鎖relationshipが限定形式ではありません。')
    target = relation.get('Target', '')
    normalized_target = (target.lstrip('/') if target.startswith('/') else
                         posixpath.normpath(posixpath.join('xl', target)))
    if normalized_target != CALC_CHAIN_PATH or '..' in target.split('/'):
        raise ValueError('計算連鎖relationshipの参照先が不正です。')

    content_types = ET.fromstring(archive.read('[Content_Types].xml'))
    chain_types = [element for element in content_types
                   if element.tag == '{%s}Override' % CONTENT_TYPE_NS and
                   ((element.get('PartName') or '').lower().endswith('calcchain.xml') or
                    element.get('ContentType') == CALC_CHAIN_CONTENT_TYPE)]
    if (len(chain_types) != 1 or
            chain_types[0].get('PartName') != '/' + CALC_CHAIN_PATH or
            chain_types[0].get('ContentType') != CALC_CHAIN_CONTENT_TYPE or
            set(chain_types[0].attrib) != {'PartName', 'ContentType'} or
            len(chain_types[0]) or (chain_types[0].text or '').strip() or
            (chain_types[0].tail or '').strip()):
        raise ValueError('計算連鎖content typeが単一の正規定義ではありません。')

    root = ET.fromstring(
        archive.read(CALC_CHAIN_PATH),
        parser=ET.XMLParser(target=ET.TreeBuilder(insert_comments=True, insert_pis=True)))
    if (root.tag != '{%s}calcChain' % NS['s'] or root.attrib or
            (root.text or '').strip() or (root.tail or '').strip()):
        raise ValueError('計算連鎖rootが限定形式ではありません。')
    children = list(root)
    if (len(children) != 1 or children[0].tag != '{%s}c' % NS['s'] or
            len(children[0]) or (children[0].tail or '').strip()):
        raise ValueError('計算連鎖は単一の式セルだけを許可します。')
    cell = children[0]
    if set(cell.attrib) != {'r', 'i', 'l'} or (cell.text or '').strip():
        raise ValueError('計算連鎖セルの属性または内容が限定形式と一致しません。')
    if not CELL_RE.fullmatch(cell.get('r', '')):
        raise ValueError('計算連鎖セル参照が不正です。')
    try:
        chain_sheet_index = int(cell.get('i', ''))
    except ValueError as error:
        raise ValueError('計算連鎖のシートindexが不正です。') from error
    if (chain_sheet_index != selected_sheet_id or
            chain_sheet_index != APPROVED_CHAIN_SHEET_ID or
            cell.get('l', '').lower() not in ('1', 'true')):
        raise ValueError('計算連鎖のシートまたは依存レベルが限定条件と一致しません。')
    formula_cells = [item['cell'] for item in formula_records]
    if (cell.get('r') != APPROVED_CHAIN_CELL or formula_cells != [APPROVED_CHAIN_CELL] or
            formula_records[0]['dependency'] != APPROVED_CHAIN_DEPENDENCY):
        raise ValueError('計算連鎖と唯一の式セルが一致しません。')
    return {
        'present': True, 'count': 1, 'cell': cell.get('r'),
        'sheetIndex': chain_sheet_index, 'newDependencyLevel': True,
    }


def inspect_workbook(source, sheet_index, selected_row):
    with zipfile.ZipFile(source) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)):
            raise ValueError('同名ZIP entryを含むXLSXは限定描画対象外です。')
        workbook = ET.fromstring(
            archive.read('xl/workbook.xml'),
            parser=ET.XMLParser(target=ET.TreeBuilder(insert_comments=True, insert_pis=True)))
        for protection in workbook.findall('s:workbookProtection', NS):
            if protection.attrib or len(protection) or protection.text not in (None, ''):
                raise ValueError('ブック保護されたXLSXは描画しません。原典で確認してください。')
        sheets, selected_entry, selected_path = sheet_path(archive, workbook, sheet_index)
        selected_name = selected_entry.get('name', '')
        selected_root = ET.fromstring(archive.read(selected_path))
        last_row = worksheet_last_row(selected_root)
        if not 1 <= selected_row <= last_row:
            raise ValueError('行番号が表示範囲外です。')

        print_names = []
        for defined in workbook.findall('s:definedNames/s:definedName', NS):
            name = defined.get('name', '')
            local = defined.get('localSheetId')
            if local is not None and int(local) != sheet_index - 1:
                raise ValueError('他シートの定義名を含むXLSXは限定描画対象外です。')
            if name == '_xlnm.Print_Area':
                print_names.append(defined.text or '')
            elif name == '_xlnm._FilterDatabase':
                parse_simple_range(defined.text or '', selected_name)
            else:
                raise ValueError('印刷範囲以外の定義名を含むXLSXは限定描画対象外です。')
        if len(print_names) != 1:
            raise ValueError('対象シートの単一印刷範囲を確認できません。')
        print_area = parse_print_area(print_names[0], selected_name, last_row)
        if not inside_area(print_area[0], selected_row - 1, print_area):
            raise ValueError('対象行が保存済み印刷範囲外です。')

        unsupported_all_sheets = (
            's:conditionalFormatting', 's:dataValidations', 's:tableParts',
            's:rowBreaks', 's:colBreaks', 's:drawing', 's:legacyDrawing',
            's:legacyDrawingHF', 's:picture')
        formula_records = []
        for position in range(1, len(sheets) + 1):
            _, _, path = sheet_path(archive, workbook, position)
            root = ET.fromstring(archive.read(path))
            if root.find('s:sheetProtection', NS) is not None:
                raise ValueError('シート保護されたXLSXは描画しません。原典で確認してください。')
            if any(root.find(node, NS) is not None for node in unsupported_all_sheets):
                raise ValueError('描画・条件書式・検証・改ページ等を含むXLSXは限定描画対象外です。')
            formulas = root.findall('.//s:f', NS)
            if position != sheet_index and formulas:
                raise ValueError('他シートの数式を含むXLSXは限定描画対象外です。')
            if position == sheet_index:
                for formula in formulas:
                    cell = next((node for node in root.findall('.//s:c', NS)
                                 if node.find('s:f', NS) is formula), None)
                    if cell is None or not CELL_RE.fullmatch(cell.get('r', '')):
                        raise ValueError('数式セル参照が不正です。')
                    if formula.attrib:
                        raise ValueError('shared・array等の属性付き数式は限定描画対象外です。')
                    expression = (formula.text or '').strip().upper()
                    dependency = DIRECT_REF_RE.fullmatch(expression)
                    if not dependency:
                        raise ValueError('単純な同一シート参照以外の数式は限定描画対象外です。')
                    cell_col, cell_row = parse_cell(cell.get('r'))
                    dep_col, dep_row = parse_cell(expression)
                    if inside_area(cell_col, cell_row, print_area) or inside_area(dep_col, dep_row, print_area):
                        raise ValueError('印刷範囲内の数式または依存参照は描画しません。')
                    dependency_ref = dependency.group(1) + dependency.group(2)
                    if root.find(".//s:c[@r='%s']" % dependency_ref, NS) is not None:
                        raise ValueError('実体セルへ依存する数式は限定描画対象外です。')
                    cached = cell.findtext('s:v', default='', namespaces=NS).strip()
                    try:
                        cached_value = float(cached)
                    except ValueError as error:
                        raise ValueError('数式の数値保存値を確認できません。') from error
                    if not math.isfinite(cached_value):
                        raise ValueError('数式の保存値が有限数ではありません。')
                    formula_records.append({
                        'cell': cell.get('r'), 'dependency': dependency_ref,
                        'cached': cached_value, 'sheetIndex': sheet_index})
        if len(formula_records) > 1:
            raise ValueError('複数数式を含むXLSXは限定描画対象外です。')
        try:
            selected_sheet_id = int(selected_entry.get('sheetId', ''))
        except ValueError as error:
            raise ValueError('対象シートのsheetIdが不正です。') from error
        calc_chain = inspect_calc_chain(archive, formula_records, selected_sheet_id)

        hidden_columns = []
        for column in selected_root.findall('s:cols/s:col', NS):
            try:
                first = int(column.get('min')); last = int(column.get('max'))
                width = float(column.get('width', '1'))
            except (TypeError, ValueError) as error:
                raise ValueError('列幅の定義が不正です。') from error
            hidden = column.get('hidden', '0').lower() in ('1', 'true')
            if not (1 <= first <= last <= 16384) or not math.isfinite(width) or width <= 0:
                raise ValueError('幅0の列を含む対象シートは描画しません。')
            if hidden:
                hidden_columns.extend(range(first - 1, last))

        for merged in selected_root.findall('s:mergeCells/s:mergeCell', NS):
            ends = merged.get('ref', '').split(':')
            if len(ends) != 2:
                continue
            _, first_zero = parse_cell(ends[0]); _, last_zero = parse_cell(ends[1])
            first, last = first_zero + 1, last_zero + 1
            if first != last and first <= selected_row <= last:
                raise ValueError('対象行の境界をまたぐ結合セルがあるため行強調できません。原典で確認してください。')

        return {
            'sheetName': selected_name, 'lastRow': last_row, 'printArea': print_area,
            'hiddenColumns': sorted(set(hidden_columns)), 'formulas': formula_records,
            'sheetCount': len(sheets), 'calcChain': calc_chain,
        }


def reject_encrypted_or_invalid_container(source):
    if not zipfile.is_zipfile(source):
        raise ValueError('暗号化・パスワード保護または破損したXLSXは描画しません。原典で確認してください。')


def write_no_recalc_profile(profile):
    user = Path(profile) / 'user'
    user.mkdir(parents=True, exist_ok=True)
    content = '''<?xml version="1.0" encoding="UTF-8"?>
<oor:items xmlns:oor="http://openoffice.org/2001/registry">
 <item oor:path="/org.openoffice.Office.Calc/Formula/Load">
  <prop oor:name="OOXMLRecalcMode" oor:op="fuse"><value>1</value></prop>
 </item>
</oor:items>
'''
    (user / 'registrymodifications.xcu').write_text(content, encoding='utf-8')


def worker(args):
    import uno
    from com.sun.star.beans import PropertyValue

    def prop(name, value):
        result = PropertyValue(); result.Name = name; result.Value = value
        return result

    layout = inspect_workbook(args.source, args.sheet, args.row)
    pipe = 'lips_render_' + uuid.uuid4().hex
    office = None; doc = None
    profile = args.profile
    write_no_recalc_profile(profile)
    try:
            office = subprocess.Popen([
                args.soffice, '-env:UserInstallation=' + Path(profile).as_uri(),
                '--headless', '--invisible', '--nologo', '--nodefault',
                '--nofirststartwizard', '--norestore',
                '--accept=pipe,name=' + pipe + ';urp;StarOffice.ComponentContext'],
                stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            local = uno.getComponentContext()
            resolver = local.ServiceManager.createInstanceWithContext(
                'com.sun.star.bridge.UnoUrlResolver', local)
            deadline = time.monotonic() + 20
            while True:
                if office.poll() is not None:
                    raise ValueError('専用LibreOfficeが起動しませんでした。')
                try:
                    ctx = resolver.resolve('uno:pipe,name=' + pipe + ';urp;StarOffice.ComponentContext')
                    break
                except Exception:
                    if time.monotonic() > deadline:
                        raise ValueError('専用LibreOffice接続が時間切れです。')
                    time.sleep(.1)
            desktop = ctx.ServiceManager.createInstanceWithContext('com.sun.star.frame.Desktop', ctx)
            doc = desktop.loadComponentFromURL(args.source.as_uri(), '_blank', 0, (
                prop('Hidden', True), prop('ReadOnly', True),
                prop('MacroExecutionMode', 0), prop('UpdateDocMode', 0)))
            if doc is None:
                raise ValueError('原典を読取専用で開けませんでした。')
            doc.enableAutomaticCalculation(False)
            if doc.isAutomaticCalculationEnabled():
                raise ValueError('自動計算を無効化できませんでした。')
            sheets = doc.getSheets()
            sheet = sheets.getByIndex(args.sheet - 1)
            if sheet.Name != layout['sheetName'] or not sheet.IsVisible:
                raise ValueError('対象シートの同一性または表示状態を確認できません。')
            def formula_state():
                return [
                    (sheet.getCellRangeByName(item['cell']).Formula,
                     float(sheet.getCellRangeByName(item['cell']).Value))
                    for item in layout['formulas']]
            expected_state = [('=' + item['dependency'], item['cached']) for item in layout['formulas']]
            loaded_state = formula_state()
            if len(loaded_state) != len(expected_state) or any(
                    actual_formula.upper() != expected_formula.upper() or
                    not math.isclose(actual_value, expected_value, rel_tol=0.0, abs_tol=1e-12)
                    for (actual_formula, actual_value), (expected_formula, expected_value)
                    in zip(loaded_state, expected_state)):
                raise ValueError('LibreOffice読込時に数式または保存値が変化したため描画を停止しました。')

            first_col, print_first_row, last_col, print_last_row = layout['printArea']
            for column in range(first_col, last_col + 1):
                visible = bool(sheet.getColumns().getByIndex(column).IsVisible)
                if visible == (column in layout['hiddenColumns']):
                    raise ValueError('原典の列表示状態を保持できませんでした。')
            if not sheet.getRows().getByIndex(args.row - 1).IsVisible:
                raise ValueError('非表示行は描画しません。')
            first = max(print_first_row + 1, args.row - 3)
            end = min(print_last_row + 1, args.row + 3)
            area = sheet.getCellRangeByPosition(first_col, first - 1, last_col, end - 1)
            target = sheet.getCellRangeByPosition(first_col, args.row - 1, last_col, args.row - 1)

            selected_name = sheet.Name
            for name in tuple(sheets.getElementNames()):
                if name != selected_name:
                    sheets.removeByName(name)
            sheet = sheets.getByName(selected_name)
            if formula_state() != loaded_state:
                raise ValueError('他シート除去後に数式保存値が変化しました。')
            area = sheet.getCellRangeByPosition(first_col, first - 1, last_col, end - 1)
            target = sheet.getCellRangeByPosition(first_col, args.row - 1, last_col, args.row - 1)
            sheet.setPrintAreas((area.getRangeAddress(),))
            sheet.setPrintTitleRows(False); sheet.setPrintTitleColumns(False)
            style = doc.getStyleFamilies().getByName('PageStyles').getByName(sheet.PageStyle)
            style.ScaleToPagesX = 1; style.ScaleToPagesY = 1

            def export(name):
                doc.storeToURL((args.out / name).as_uri(), (
                    prop('FilterName', 'calc_pdf_Export'), prop('Overwrite', False),
                    prop('FilterData', (prop('SinglePageSheets', False), prop('ExportBookmarks', False)))))

            export('base.pdf')
            if formula_state() != loaded_state:
                raise ValueError('PDF出力後に数式または保存値が変化しました。')
            shape = doc.createInstance('com.sun.star.drawing.RectangleShape')
            sheet.getDrawPage().add(shape)
            shape.setPosition(target.Position); shape.setSize(target.Size)
            shape.FillColor = 0xFFFF00; shape.FillTransparence = 65
            shape.LineColor = 0xFF0000; shape.LineWidth = 40
            export('highlight.pdf')
            if formula_state() != loaded_state:
                raise ValueError('強調PDF出力後に数式または保存値が変化しました。')
            metadata = {
                'sheet': sheet.Name, 'row': args.row, 'firstRow': first, 'lastRow': end,
                'printColumns': [column_name(first_col), column_name(last_col)],
                'hiddenColumns': [column_name(value) for value in layout['hiddenColumns']],
                'renderer': 'LibreOffice Calc / original XLSX (limited candidate)',
                'policyId': 'limited-saved-print-area-v1',
                'formulaPolicy': 'direct-absent-reference-outside-saved-print-area',
                'calcChainPolicy': 'optional-single-cell-exact-formula-match-v1',
                'loadedCalcChainCellsVerified': layout['calcChain']['count'],
                'preLoadRecalcPolicy': 'dedicated-profile-ooxml-never',
                'loadedFormulaCachesVerified': len(layout['formulas']),
                'formulaStateVerifiedAt': [
                    'post-load', 'post-sheet-removal', 'post-base-export',
                    'post-highlight-export'],
                'automaticCalculationDisabled': True, 'sourceSaved': False,
                'targetPosition': [target.Position.X, target.Position.Y],
                'targetSize': [target.Size.Width, target.Size.Height],
                'printAreaPosition': [area.Position.X, area.Position.Y],
                'printAreaSize': [area.Size.Width, area.Size.Height],
            }
            (args.out / 'excel.json').write_text(
                json.dumps(metadata, ensure_ascii=False, indent=2), encoding='utf-8')
    finally:
        if doc is not None:
            try: doc.close(True)
            except Exception: pass
        if office is not None and office.poll() is None:
            try: desktop.terminate()
            except Exception: pass
            try: office.wait(timeout=5)
            except subprocess.TimeoutExpired: office.terminate()


def render(args):
    if os.name != 'posix': raise ValueError('このbackendは隔離Linux検証専用です。')
    if args.source.suffix.lower() != '.xlsx': raise ValueError('XLSX以外は対象外です。')
    from render import workbook_info, render_pdf
    from PIL import Image, ImageChops
    before = hashlib.sha256(args.source.read_bytes()).hexdigest()
    reject_encrypted_or_invalid_container(args.source)
    info = workbook_info(args.source)
    inspect_workbook(args.source, args.sheet, args.row)
    selected = next((sheet for sheet in info['sheets'] if sheet['index'] == args.sheet), None)
    if not selected or selected['hidden'] or args.row not in selected['contentRows']:
        raise ValueError('非表示または文字・数値のない行は描画しません。')
    args.out.mkdir(parents=True, exist_ok=True)
    if any((args.out / name).exists() for name in ('base.pdf', 'highlight.pdf', 'excel.json')):
        raise ValueError('出力先には既存描画があります。新しい専用ディレクトリを指定してください。')
    with tempfile.TemporaryDirectory(prefix='lips-lo-profile-parent-') as profile:
        command = [args.uno_python, str(Path(__file__).resolve()), str(args.source), str(args.out),
                   '--worker', '--soffice', args.soffice, '--sheet', str(args.sheet),
                   '--row', str(args.row), '--profile', profile]
        process = subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                                   stderr=subprocess.DEVNULL, start_new_session=True)
        try:
            try: exit_code = process.wait(timeout=args.timeout)
            except subprocess.TimeoutExpired:
                raise ValueError('専用LibreOffice描画が時間切れです。自動再試行しません。')
            if exit_code != 0:
                raise ValueError('LibreOffice原典描画に失敗しました。専用環境の互換性確認が必要です。')
        finally:
            try: os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError: pass
            process.wait()
    if before != hashlib.sha256(args.source.read_bytes()).hexdigest():
        raise ValueError('原典SHA256不一致です。')
    result = render_pdf(args.out / 'base.pdf', args.out / 'base.png', 1, False)
    marked = render_pdf(args.out / 'highlight.pdf', args.out / 'highlight.png', 1, False)
    if result['pages'] != 1 or marked['pages'] != 1:
        raise ValueError('対象範囲が1ページに収まりません。')
    base = Image.open(args.out / 'base.png').convert('RGB')
    highlight = Image.open(args.out / 'highlight.png').convert('RGB')
    if base.size != highlight.size: raise ValueError('強調前後のページ寸法が異なります。')
    box = ImageChops.difference(base, highlight).getbbox()
    if not box: raise ValueError('原典上の行強調を確認できません。')
    result.update(json.loads((args.out / 'excel.json').read_text(encoding='utf-8')))
    result.update(kind='xlsx', sourceHash=before, highlightBox=list(box),
                  notice='保存済み印刷範囲と非表示列を保持し、読込時数式保存値を検証した限定候補です。')
    (args.out / 'render.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path); parser.add_argument('out', type=Path)
    parser.add_argument('--soffice', required=True); parser.add_argument('--uno-python', default=sys.executable)
    parser.add_argument('--sheet', type=int, default=1); parser.add_argument('--row', type=int, required=True)
    parser.add_argument('--timeout', type=float, default=60); parser.add_argument('--worker', action='store_true')
    parser.add_argument('--profile', type=Path)
    arguments = parser.parse_args(); arguments.source = arguments.source.resolve(); arguments.out = arguments.out.resolve()
    if arguments.worker:
        if arguments.profile is None: parser.error('--profile is required with --worker')
        arguments.profile = arguments.profile.resolve(); worker(arguments)
    else: render(arguments)
