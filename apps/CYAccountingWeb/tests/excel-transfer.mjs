import readExcelFile, { readSheet } from 'read-excel-file/universal';
import { buildMonthlyWorkbook, buildImportTemplateWorkbook, handleExcelExportApi } from '../src/excel-export.js';
import { analyzeImportRows, handleExcelImportApi } from '../src/excel-import.js';
import { unzipSync, zipSync, strFromU8 } from 'fflate';

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

const workbook = buildMonthlyWorkbook({
  month: '2026-09',
  locked: false,
  generatedAt: new Date('2026-09-25T04:00:00Z'),
  accountNames: ['現金'],
  openingMap: new Map([['現金', 1000]]),
  transactions: [
    { id: 1, tx_date: '2026-09-24', account_name: '現金', kind: 'expense', category_name: '一般支出', summary: '文具', amount: 20, created_at: '2026-09-24T01:00:00Z' }
  ]
});
const arrayBuffer = workbook.buffer.slice(workbook.byteOffset, workbook.byteOffset + workbook.byteLength);
const sheets = await readExcelFile(arrayBuffer);
assert(sheets.length === 2, 'expected two exported sheets');
assert(sheets[0].sheet === '月帳簿', 'expected monthly ledger as first sheet');
const sheetByName = await readSheet(arrayBuffer, '月帳簿');
const header = sheetByName.find(row => Array.isArray(row) && row[0] === '日期');
assert(header?.[5] === '金額', 'expected monthly ledger header');

// ISO/IEC 29500 CT_Worksheet is a sequence: autoFilter precedes mergeCells.
// A tolerant data reader does not detect the Excel repair dialog caused by this.
function validateWorksheetOrder(bytes) {
  const files = unzipSync(new Uint8Array(bytes));
  const order = ['sheetPr', 'dimension', 'sheetViews', 'sheetFormatPr', 'cols', 'sheetData', 'autoFilter', 'mergeCells', 'dataValidations', 'pageMargins', 'pageSetup'];
  for (const [name, data] of Object.entries(files)) {
    if (!/^xl\/worksheets\/sheet\d+\.xml$/.test(name)) continue;
    const xml = strFromU8(data);
    let previous = -1;
    for (const element of xml.matchAll(/<(sheetPr|dimension|sheetViews|sheetFormatPr|cols|sheetData|autoFilter|mergeCells|dataValidations|pageMargins|pageSetup)(?=[\s/>])/g)) {
      const index = order.indexOf(element[1]);
      assert(index > previous, `${name}: invalid worksheet element order at ${element[1]}`);
      previous = index;
    }
  }
}
validateWorksheetOrder(arrayBuffer);
validateWorksheetOrder(buildMonthlyWorkbook({ month: '2026-02', transactions: [], accountNames: [], openingMap: new Map() }));
const historical = buildMonthlyWorkbook({
  month: '2026-02', accountNames: ['測試現金', '測試銀行'], openingMap: new Map([['測試現金', 1000], ['測試銀行', 2000]]),
  transactions: Array.from({ length: 180 }, (_, i) => ({ id: i + 1, tx_date: `2026-02-${String(i % 28 + 1).padStart(2, '0')}`, account_name: i % 2 ? '測試銀行' : '測試現金', kind: i % 3 ? 'expense' : 'income', category_name: '測試科目', summary: '摘要 & <測試> "引號"\n第二行', amount: 100, created_at: '2026-02-01T00:00:00Z' }))
});
validateWorksheetOrder(historical);
const historicalRows = await readSheet(historical.buffer.slice(historical.byteOffset, historical.byteOffset + historical.byteLength), '月帳簿');
assert(historicalRows.length === 186, 'historical export retains all 180 records');
assert(historicalRows[2][3] === 6000 && historicalRows[2][5] === 12000 && historicalRows[3][1] === -3000, 'historical export totals include both accounts');
assert(historicalRows.slice(6).filter(row => row[1] === '測試現金').length === 90 && historicalRows.slice(6).filter(row => row[1] === '測試銀行').length === 90, 'historical export preserves every account');

// Compressed worksheet XML > 512 KiB used to start an unsupported nested
// worker inside read-excel-file. Exercise the actual canonical inspect route.
const largeWorkbook = buildMonthlyWorkbook({ month:'2026-02', accountNames:['測試現金'], openingMap:new Map(),
  transactions:Array.from({length:4000},(_,i)=>({id:i+1,tx_date:'2026-02-01',account_name:'測試現金',kind:'income',category_name:'測試收入',summary:`大量工作表測試 ${i}`,amount:1,created_at:'2026-02-01T00:00:00Z'})) });
const xmlEntries = unzipSync(largeWorkbook);
assert(Object.entries(xmlEntries).some(([name,bytes])=>name.startsWith('xl/worksheets/') && bytes.byteLength>512*1024),'large compressed XML fixture must hit the nested worker threshold');
const compressed = zipSync(xmlEntries,{level:6});
const inspected = await handleExcelImportApi(new Request('https://test.invalid/api/import/xlsx/inspect',{method:'POST',body:compressed}),{});
assert(inspected.status===200,'large compressed workbook parses in Worker-compatible path');
assert((await inspected.json()).sheets[0].rowCount===4006,'large workbook inspect retains all records');
const parsedLarge = await handleExcelImportApi(new Request('https://test.invalid/api/import/xlsx/inspect?mode=sheet&sheet=1',{method:'POST',body:compressed}),{});
assert(parsedLarge.status===200 && (await parsedLarge.json()).rows.length===4006,'large worksheet parses in canonical sheet route');

class MockStatement {
  constructor(sql) {
    this.sql = sql;
    this.args = [];
  }
  bind(...args) {
    this.args = args;
    return this;
  }
  async all() {
    if (this.sql.includes('FROM accounts')) return { results: [{ name: '現金' }] };
    if (this.sql.includes('FROM categories')) {
      return { results: [{ kind: 'expense', name: '一般支出' }, { kind: 'income', name: '一般收入' }] };
    }
    if (this.sql.includes('FROM transactions')) {
      return {
        results: [
          { tx_date: '2026-09-24', account_name: '現金', kind: 'expense', category_name: '一般支出', summary: '文具', amount: 20 }
        ]
      };
    }
    return { results: [] };
  }
  async first() {
    if (this.sql.includes("key = 'locked_through'")) return { value: '2026-08' };
    return null;
  }
}

const db = { prepare(sql) { return new MockStatement(sql); } };
const preview = await analyzeImportRows([
  { sourceRow: 2, txDate: '2026-09-24', accountName: '現金', kind: 'expense', categoryName: '一般支出', summary: '文具', amount: 20 },
  { sourceRow: 3, txDate: '2026-09-24', accountName: '現金', kind: 'expense', categoryName: '一般支出', summary: '文具', amount: 20 },
  { sourceRow: 4, txDate: '2026-09-25', accountName: '現金', kind: 'income', categoryName: '一般收入', summary: '現金收入', amount: 100 },
  { sourceRow: 5, txDate: '2026-08-20', accountName: '現金', kind: 'expense', categoryName: '一般支出', summary: '鎖帳', amount: 30 },
  { sourceRow: 6, txDate: '2026-09-25', accountName: '不存在', kind: 'expense', categoryName: '一般支出', summary: '錯誤', amount: 40 }
], db);

assert(preview.summary.duplicates === 1, `expected one duplicate: ${JSON.stringify(preview.summary)}`);
assert(preview.summary.ready === 2, `expected two ready rows including second identical occurrence: ${JSON.stringify(preview.summary)}`);
assert(preview.summary.locked === 1, `expected one locked row: ${JSON.stringify(preview.summary)}`);
assert(preview.summary.errors === 1, `expected one invalid row: ${JSON.stringify(preview.summary)}`);
assert(preview.canCommit === false, 'preview with locked/error rows must not commit');

console.log('Excel import/export tests passed');

const templateResponse = await handleExcelExportApi(new Request('https://acc.example.com/api/import/template.xlsx'), { DB: db });
assert(templateResponse.status === 200, 'template download succeeds');
assert(templateResponse.headers.get('content-disposition').includes('CYAccounting_import_template.xlsx'), 'template has a download filename');
const template = await templateResponse.arrayBuffer();
validateWorksheetOrder(template);
const templateSheets = await readExcelFile(template);
assert(templateSheets[0].sheet === '記帳匯入' && templateSheets[1].sheet === '填寫說明', 'template separates writable records from instructions');
const templateRows = await readSheet(template, '記帳匯入');
assert(JSON.stringify(templateRows[0]) === JSON.stringify(['日期', '帳戶', '收支', '科目', '摘要', '金額']), 'template headers match importer aliases');
assert(templateRows.slice(1).every(row => row.every(cell => cell == null || cell === '')), 'template has no example transactions that could accidentally import');
const templateNotes = await readSheet(template, '填寫說明');
assert(templateNotes.some(row => row.includes('現金')) && templateNotes.some(row => row.includes('一般支出')), 'template contains currently valid names');
