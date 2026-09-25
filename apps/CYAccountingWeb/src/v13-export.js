const encoder = new TextEncoder();
const CRC_TABLE = buildCrcTable();

export async function handleV13Api(request, env) {
  const url = new URL(request.url);
  if (url.pathname !== '/api/export/month.xlsx' || request.method !== 'GET') return null;

  const month = String(url.searchParams.get('month') || currentMonth()).trim();
  if (!isMonth(month)) return json({ ok: false, error: '月份格式錯誤。' }, 400);

  const [transactionsResult, accountsResult, openingsResult, lockedRow] = await Promise.all([
    env.DB.prepare(`
      SELECT id, tx_date, account_name, kind, category_name, summary, amount, created_at
      FROM transactions
      WHERE substr(tx_date, 1, 7) = ?
      ORDER BY tx_date ASC, created_at ASC, id ASC
    `).bind(month).all(),
    env.DB.prepare('SELECT name FROM accounts ORDER BY sort_order, id').all(),
    env.DB.prepare('SELECT account_name, amount FROM opening_balances WHERE month = ? ORDER BY account_name').bind(month).all(),
    env.DB.prepare("SELECT value FROM app_settings WHERE key = 'locked_through'").first()
  ]);

  const transactions = transactionsResult.results || [];
  const currentAccounts = (accountsResult.results || []).map(row => String(row.name || '')).filter(Boolean);
  const openingRows = openingsResult.results || [];
  const openingMap = new Map(openingRows.map(row => [String(row.account_name || ''), numberOrZero(row.amount)]));

  const accountNames = [...currentAccounts];
  const seen = new Set(accountNames);
  for (const row of openingRows) {
    const name = String(row.account_name || '');
    if (name && !seen.has(name)) {
      seen.add(name);
      accountNames.push(name);
    }
  }
  for (const tx of transactions) {
    const name = String(tx.account_name || '');
    if (name && !seen.has(name)) {
      seen.add(name);
      accountNames.push(name);
    }
  }

  const lockedThrough = isMonth(String(lockedRow?.value || '')) ? String(lockedRow.value) : null;
  const workbook = buildMonthlyWorkbook({
    month,
    transactions,
    accountNames,
    openingMap,
    locked: Boolean(lockedThrough && month <= lockedThrough),
    generatedAt: new Date()
  });

  const filename = `CYAccounting_${month}.xlsx`;
  return new Response(workbook, {
    status: 200,
    headers: {
      'content-type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      'content-disposition': `attachment; filename="${filename}"`,
      'cache-control': 'no-store',
      'x-content-type-options': 'nosniff'
    }
  });
}

export function buildMonthlyWorkbook({ month, transactions, accountNames, openingMap, locked = false, generatedAt = new Date() }) {
  const openingBalances = openingMap instanceof Map ? openingMap : new Map(Object.entries(openingMap || {}));
  const txs = [...(transactions || [])].sort(compareChronological);
  const openingTotal = [...openingBalances.values()].reduce((sum, value) => sum + numberOrZero(value), 0);
  const income = txs.reduce((sum, tx) => sum + (tx.kind === 'income' ? numberOrZero(tx.amount) : 0), 0);
  const expense = txs.reduce((sum, tx) => sum + (tx.kind === 'expense' ? numberOrZero(tx.amount) : 0), 0);
  const ending = openingTotal + income - expense;
  const net = income - expense;

  let running = openingTotal;
  const detail = txs.map(tx => {
    running += (tx.kind === 'income' ? 1 : -1) * numberOrZero(tx.amount);
    return { ...tx, running };
  });

  const generatedIso = generatedAt.toISOString();
  const files = [
    { name: '[Content_Types].xml', text: contentTypesXml() },
    { name: '_rels/.rels', text: rootRelsXml() },
    { name: 'docProps/core.xml', text: corePropsXml(generatedIso) },
    { name: 'docProps/app.xml', text: appPropsXml() },
    { name: 'xl/workbook.xml', text: workbookXml() },
    { name: 'xl/_rels/workbook.xml.rels', text: workbookRelsXml() },
    { name: 'xl/styles.xml', text: stylesXml() },
    {
      name: 'xl/worksheets/sheet1.xml',
      text: ledgerSheetXml({ month, locked, generatedAt, openingTotal, income, expense, net, ending, detail })
    },
    {
      name: 'xl/worksheets/sheet2.xml',
      text: openingSheetXml({ month, accountNames: accountNames || [], openingBalances })
    }
  ];

  return zipStore(files.map(file => ({ name: file.name, data: encoder.encode(file.text) })));
}

function ledgerSheetXml({ month, locked, generatedAt, openingTotal, income, expense, net, ending, detail }) {
  const [year, monthNumber] = month.split('-');
  const title = `志遠記帳系統｜${year}年${monthNumber}月帳簿`;
  const status = locked ? '已鎖帳' : '未鎖帳';
  const generated = formatDateTime(generatedAt);
  const rows = [];

  rows.push(rowXml(1, [inlineCell('A1', title, 1)]));
  rows.push(rowXml(2, [inlineCell('A2', `月份 ${month}　｜　${status}　｜　匯出時間 ${generated}`, 2)]));
  rows.push(rowXml(3, [
    inlineCell('A3', '期初', 3), numberCell('B3', openingTotal, 4),
    inlineCell('C3', '收入', 3), numberCell('D3', income, 4),
    inlineCell('E3', '支出', 3), numberCell('F3', expense, 4),
    inlineCell('G3', '淨利損', 3), formulaNumberCell('H3', 'D3-F3', net, 4)
  ]));
  rows.push(rowXml(4, [inlineCell('A4', '期末', 3), formulaNumberCell('B4', 'B3+D3-F3', ending, 4)]));
  rows.push(rowXml(6, [
    inlineCell('A6', '日期', 5), inlineCell('B6', '帳戶', 5), inlineCell('C6', '收支', 5),
    inlineCell('D6', '科目', 5), inlineCell('E6', '摘要', 5), inlineCell('F6', '金額', 5), inlineCell('G6', '餘額', 5)
  ]));

  let lastRow = 6;
  if (!detail.length) {
    rows.push(rowXml(7, [inlineCell('A7', '本月無記帳資料', 7)]));
    lastRow = 7;
  } else {
    detail.forEach((tx, index) => {
      const row = 7 + index;
      const kindLabel = tx.kind === 'income' ? '收入' : '支出';
      const kindStyle = tx.kind === 'income' ? 9 : 10;
      const balanceFormula = row === 7
        ? `$B$3+IF(C${row}="收入",F${row},-F${row})`
        : `G${row - 1}+IF(C${row}="收入",F${row},-F${row})`;
      rows.push(rowXml(row, [
        numberCell(`A${row}`, excelDateSerial(String(tx.tx_date || '')), 6),
        inlineCell(`B${row}`, tx.account_name || '', 7),
        inlineCell(`C${row}`, kindLabel, kindStyle),
        inlineCell(`D${row}`, tx.category_name || '', 7),
        inlineCell(`E${row}`, tx.summary || '', 11),
        numberCell(`F${row}`, numberOrZero(tx.amount), 8),
        formulaNumberCell(`G${row}`, balanceFormula, tx.running, 8)
      ]));
      lastRow = row;
    });
  }

  const mergeCells = detail.length
    ? '<mergeCells count="2"><mergeCell ref="A1:H1"/><mergeCell ref="A2:H2"/></mergeCells>'
    : '<mergeCells count="3"><mergeCell ref="A1:H1"/><mergeCell ref="A2:H2"/><mergeCell ref="A7:G7"/></mergeCells>';
  const autoFilter = detail.length ? `<autoFilter ref="A6:G${lastRow}"/>` : '<autoFilter ref="A6:G6"/>';

  return xmlHeader() + `<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <sheetPr><pageSetUpPr fitToPage="1"/></sheetPr>
  <dimension ref="A1:H${lastRow}"/>
  <sheetViews><sheetView workbookViewId="0"><pane ySplit="6" topLeftCell="A7" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>
  <sheetFormatPr defaultRowHeight="18"/>
  <cols>
    <col min="1" max="1" width="13" customWidth="1"/><col min="2" max="2" width="16" customWidth="1"/>
    <col min="3" max="3" width="9" customWidth="1"/><col min="4" max="4" width="18" customWidth="1"/>
    <col min="5" max="5" width="36" customWidth="1"/><col min="6" max="7" width="15" customWidth="1"/>
    <col min="8" max="8" width="15" customWidth="1"/>
  </cols>
  <sheetData>${rows.join('')}</sheetData>
  ${mergeCells}
  ${autoFilter}
  <pageMargins left="0.25" right="0.25" top="0.5" bottom="0.5" header="0.2" footer="0.2"/>
  <pageSetup orientation="landscape" fitToWidth="1" fitToHeight="0"/>
</worksheet>`;
}

function openingSheetXml({ month, accountNames, openingBalances }) {
  const rows = [
    rowXml(1, [inlineCell('A1', `期初餘額｜${month}`, 1)]),
    rowXml(3, [inlineCell('A3', '帳戶', 5), inlineCell('B3', '期初餘額', 5), inlineCell('C3', '狀態', 5)])
  ];

  const names = [...new Set(accountNames.map(name => String(name || '')).filter(Boolean))];
  if (!names.length) {
    rows.push(rowXml(4, [inlineCell('A4', '無帳戶資料', 7)]));
  } else {
    names.forEach((name, index) => {
      const row = 4 + index;
      const hasExplicitOpening = openingBalances.has(name);
      rows.push(rowXml(row, [
        inlineCell(`A${row}`, name, 7),
        numberCell(`B${row}`, numberOrZero(openingBalances.get(name)), 8),
        inlineCell(`C${row}`, hasExplicitOpening ? '已設定' : '未設定（視為 0）', 12)
      ]));
    });
  }

  const lastRow = Math.max(4, 3 + names.length);
  return xmlHeader() + `<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <dimension ref="A1:C${lastRow}"/>
  <sheetViews><sheetView workbookViewId="0"><pane ySplit="3" topLeftCell="A4" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>
  <sheetFormatPr defaultRowHeight="18"/>
  <cols><col min="1" max="1" width="24" customWidth="1"/><col min="2" max="2" width="16" customWidth="1"/><col min="3" max="3" width="20" customWidth="1"/></cols>
  <sheetData>${rows.join('')}</sheetData>
  <mergeCells count="1"><mergeCell ref="A1:C1"/></mergeCells>
  <autoFilter ref="A3:C${lastRow}"/>
  <pageMargins left="0.4" right="0.4" top="0.5" bottom="0.5" header="0.2" footer="0.2"/>
</worksheet>`;
}

function stylesXml() {
  return xmlHeader() + `<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <numFmts count="2"><numFmt numFmtId="164" formatCode="yyyy/mm/dd"/><numFmt numFmtId="165" formatCode="#,##0;[Red]-#,##0"/></numFmts>
  <fonts count="7">
    <font><sz val="11"/><name val="Calibri"/><family val="2"/></font>
    <font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Calibri"/></font>
    <font><b/><color rgb="FF17202A"/><sz val="16"/><name val="Calibri"/></font>
    <font><b/><color rgb="FF243746"/><sz val="11"/><name val="Calibri"/></font>
    <font><b/><color rgb="FF287A45"/><sz val="11"/><name val="Calibri"/></font>
    <font><b/><color rgb="FFB94A48"/><sz val="11"/><name val="Calibri"/></font>
    <font><i/><color rgb="FF667788"/><sz val="10"/><name val="Calibri"/></font>
  </fonts>
  <fills count="5">
    <fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FF3A7398"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FFEAF2F8"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FFF8FAFC"/><bgColor indexed="64"/></patternFill></fill>
  </fills>
  <borders count="2">
    <border><left/><right/><top/><bottom/><diagonal/></border>
    <border><left style="thin"><color rgb="FFD9E1E8"/></left><right style="thin"><color rgb="FFD9E1E8"/></right><top style="thin"><color rgb="FFD9E1E8"/></top><bottom style="thin"><color rgb="FFD9E1E8"/></bottom><diagonal/></border>
  </borders>
  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
  <cellXfs count="13">
    <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
    <xf numFmtId="0" fontId="2" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
    <xf numFmtId="0" fontId="6" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
    <xf numFmtId="0" fontId="3" fillId="3" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
    <xf numFmtId="165" fontId="3" fillId="4" borderId="1" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
    <xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
    <xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
    <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyAlignment="1"><alignment vertical="center"/></xf>
    <xf numFmtId="165" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
    <xf numFmtId="0" fontId="4" fillId="0" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
    <xf numFmtId="0" fontId="5" fillId="0" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
    <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyAlignment="1"><alignment vertical="center" wrapText="1"/></xf>
    <xf numFmtId="0" fontId="6" fillId="0" borderId="1" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
  </cellXfs>
  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
  <dxfs count="0"/><tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
</styleSheet>`;
}

function contentTypesXml() {
  return xmlHeader() + `<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
  <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
  <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
</Types>`;
}

function rootRelsXml() {
  return xmlHeader() + `<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
</Relationships>`;
}

function workbookXml() {
  return xmlHeader() + `<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <workbookPr date1904="0"/><bookViews><workbookView xWindow="120" yWindow="120" windowWidth="22000" windowHeight="14000"/></bookViews>
  <sheets><sheet name="月帳簿" sheetId="1" r:id="rId2"/><sheet name="期初餘額" sheetId="2" r:id="rId3"/></sheets>
  <calcPr calcId="191029" calcMode="auto" fullCalcOnLoad="1" forceFullCalc="1"/>
</workbook>`;
}

function workbookRelsXml() {
  return xmlHeader() + `<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
</Relationships>`;
}

function corePropsXml(generatedIso) {
  return xmlHeader() + `<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:dcmitype="http://purl.org/dc/dcmitype/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <dc:title>CYAccounting 月帳簿</dc:title><dc:creator>CYAccountingWeb</dc:creator><cp:lastModifiedBy>CYAccountingWeb</cp:lastModifiedBy>
  <dcterms:created xsi:type="dcterms:W3CDTF">${xmlEscape(generatedIso)}</dcterms:created><dcterms:modified xsi:type="dcterms:W3CDTF">${xmlEscape(generatedIso)}</dcterms:modified>
</cp:coreProperties>`;
}

function appPropsXml() {
  return xmlHeader() + `<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
  <Application>CYAccountingWeb</Application><DocSecurity>0</DocSecurity><ScaleCrop>false</ScaleCrop>
  <HeadingPairs><vt:vector size="2" baseType="variant"><vt:variant><vt:lpstr>工作表</vt:lpstr></vt:variant><vt:variant><vt:i4>2</vt:i4></vt:variant></vt:vector></HeadingPairs>
  <TitlesOfParts><vt:vector size="2" baseType="lpstr"><vt:lpstr>月帳簿</vt:lpstr><vt:lpstr>期初餘額</vt:lpstr></vt:vector></TitlesOfParts><Company>Chihyuan</Company><AppVersion>1.0</AppVersion>
</Properties>`;
}

function rowXml(row, cells) {
  return `<row r="${row}">${cells.join('')}</row>`;
}

function inlineCell(ref, value, style = 0) {
  const text = String(value ?? '');
  const preserve = /^\s|\s$|\n/.test(text) ? ' xml:space="preserve"' : '';
  return `<c r="${ref}" t="inlineStr" s="${style}"><is><t${preserve}>${xmlEscape(text)}</t></is></c>`;
}

function numberCell(ref, value, style = 0) {
  const safe = Number.isFinite(Number(value)) ? Number(value) : 0;
  return `<c r="${ref}" s="${style}"><v>${safe}</v></c>`;
}

function formulaNumberCell(ref, formula, value, style = 0) {
  const safe = Number.isFinite(Number(value)) ? Number(value) : 0;
  return `<c r="${ref}" s="${style}"><f>${xmlEscape(formula)}</f><v>${safe}</v></c>`;
}

function excelDateSerial(value) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return 0;
  const utc = Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
  return Math.floor(utc / 86400000) + 25569;
}

function compareChronological(left, right) {
  const dateCompare = String(left.tx_date || '').localeCompare(String(right.tx_date || ''));
  if (dateCompare) return dateCompare;
  const kindCompare = (left.kind === 'income' ? 0 : 1) - (right.kind === 'income' ? 0 : 1);
  if (kindCompare) return kindCompare;
  const createdCompare = String(left.created_at || '').localeCompare(String(right.created_at || ''));
  if (createdCompare) return createdCompare;
  return Number(left.id || 0) - Number(right.id || 0);
}

function formatDateTime(date) {
  const pad = value => String(value).padStart(2, '0');
  return `${date.getFullYear()}/${pad(date.getMonth() + 1)}/${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function numberOrZero(value) {
  const number = Number(value);
  return Number.isFinite(number) ? number : 0;
}

function isMonth(value) {
  return /^\d{4}-(0[1-9]|1[0-2])$/.test(String(value || ''));
}

function currentMonth() {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
}

function xmlHeader() {
  return '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>';
}

function xmlEscape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&apos;');
}

function zipStore(files) {
  const localParts = [];
  const centralParts = [];
  let offset = 0;

  for (const file of files) {
    const name = encoder.encode(file.name);
    const data = file.data instanceof Uint8Array ? file.data : new Uint8Array(file.data);
    const crc = crc32(data);
    const localHeader = concatBytes([
      u32(0x04034b50), u16(20), u16(0x0800), u16(0), u16(0), u16(0x0021),
      u32(crc), u32(data.length), u32(data.length), u16(name.length), u16(0), name
    ]);
    localParts.push(localHeader, data);

    centralParts.push(concatBytes([
      u32(0x02014b50), u16(20), u16(20), u16(0x0800), u16(0), u16(0), u16(0x0021),
      u32(crc), u32(data.length), u32(data.length), u16(name.length), u16(0), u16(0),
      u16(0), u16(0), u32(0), u32(offset), name
    ]));
    offset += localHeader.length + data.length;
  }

  const central = concatBytes(centralParts);
  const end = concatBytes([
    u32(0x06054b50), u16(0), u16(0), u16(files.length), u16(files.length),
    u32(central.length), u32(offset), u16(0)
  ]);
  return concatBytes([...localParts, central, end]);
}

function crc32(data) {
  let crc = 0xFFFFFFFF;
  for (const byte of data) crc = CRC_TABLE[(crc ^ byte) & 0xFF] ^ (crc >>> 8);
  return (crc ^ 0xFFFFFFFF) >>> 0;
}

function buildCrcTable() {
  const table = new Uint32Array(256);
  for (let index = 0; index < 256; index += 1) {
    let value = index;
    for (let bit = 0; bit < 8; bit += 1) value = (value & 1) ? (0xEDB88320 ^ (value >>> 1)) : (value >>> 1);
    table[index] = value >>> 0;
  }
  return table;
}

function u16(value) {
  const bytes = new Uint8Array(2);
  new DataView(bytes.buffer).setUint16(0, value & 0xFFFF, true);
  return bytes;
}

function u32(value) {
  const bytes = new Uint8Array(4);
  new DataView(bytes.buffer).setUint32(0, value >>> 0, true);
  return bytes;
}

function concatBytes(parts) {
  const length = parts.reduce((sum, part) => sum + part.length, 0);
  const result = new Uint8Array(length);
  let offset = 0;
  for (const part of parts) {
    result.set(part, offset);
    offset += part.length;
  }
  return result;
}

function json(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' }
  });
}
