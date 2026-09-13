package main

import (
	"archive/zip"
	"bytes"
	"encoding/xml"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"regexp"
	"strconv"
	"strings"
	"time"
)

type Cell struct {
	Value string
	Type  string
}
type Sheet struct{ Rows [][]Cell }
type Workbook struct{ Sheets map[string]Sheet }

var headHeaders = []string{
	"銷貨單別", "單據名稱", "銷貨單號", "單據日期", "客戶代號", "現銷", "部門代號", "業務人員", "業務人員名稱", "客戶全名", "送貨地址(一)", "送貨地址(二)", "幣別", "匯率", "發票日期", "發票開立時間", "發票號碼", "統一編號", "發票聯數", "課稅別", "營業稅率", "發票作廢", "通關方式", "申報年月", "發票地址(一)", "發票地址(二)", "信用卡末四碼", "連絡人EMAIL", "載具類別", "載具類別名稱", "載具顯碼ID", "載具隱碼ID", "發票捐贈對象", "社福團體名稱", "發票防偽隨機碼", "備註", "傳送次數", "付款條件名稱", "付款條件", "更新碼", "員工代號", "員工名稱", "收款業務員", "L/C NO", "INVOICE NO", "備註一", "備註二", "備註三", "原幣銷貨金額", "原幣銷貨稅額", "原幣合計", "本幣銷貨金額", "本幣銷貨稅額", "本幣合計", "總數量", "簽核狀態碼", "確認碼", "產生分錄碼_收入", "產生分錄碼_成本", "報單號碼", "送貨客戶全名", "連絡人", "收貨人", "TEL_NO", "FAX_NO", "行動電話", "指定日期", "配送時段", "貨運別", "貨運名稱", "代收貨款", "運費", "產生貨運文字檔", "出貨通知單別", "出貨通知單號", "加扣項金額", "交易條件", "交易條件簡稱", "訂單單別", "訂單單號", "預收待抵單別", "預收待抵單號", "訂金分批", "沖抵金額", "沖抵稅額", "總包裝數量", "客戶描述", "作廢日期", "作廢時間", "專案作廢核准文號", "作廢原因", "來源", "發票列印", "買受人簽署適用零稅率註記", "POS單號", "班別", "班別名稱", "POS訂金",
}

var bodyHeaders = []string{
	"序號", "品號", "品名", "規格", "庫別", "庫別名稱", "數量", "類型", "贈/備品量", "單位", "包裝數量", "贈/備品包裝量", "包裝單位", "單價", "折扣率", "金額", "原幣未稅金額", "原幣稅額", "本幣未稅金額", "本幣稅額", "訂單單別", "訂單單號", "訂單序號", "客戶單號", "批號", "客戶品號", "專案代號", "備註", "結帳碼", "結帳單別", "結帳單號", "結帳序號", "暫出單別", "暫出單號", "暫出序號", "生產加工包裝資訊", "網購訂單編號",
}

var requiredHead = []string{"銷貨單別", "銷貨單號"}
var requiredBody = []string{"序號", "品號", "品名", "數量", "單價", "金額"}
var dateFields = map[string]bool{"單據日期": true, "發票日期": true, "指定日期": true, "作廢日期": true}

func readXLSX(path string) (Workbook, error) {
	zr, err := zip.OpenReader(path)
	if err != nil {
		return Workbook{}, err
	}
	defer zr.Close()
	read := func(name string) ([]byte, error) {
		for _, f := range zr.File {
			if f.Name == name {
				r, e := f.Open()
				if e != nil {
					return nil, e
				}
				defer r.Close()
				return io.ReadAll(r)
			}
		}
		return nil, os.ErrNotExist
	}
	shared := []string{}
	if b, e := read("xl/sharedStrings.xml"); e == nil {
		shared = parseShared(b)
	}
	wbxml, err := read("xl/workbook.xml")
	if err != nil {
		return Workbook{}, err
	}
	relxml, err := read("xl/_rels/workbook.xml.rels")
	if err != nil {
		return Workbook{}, err
	}
	sheets := parseWorkbookSheets(wbxml)
	rels := parseRels(relxml)
	out := Workbook{Sheets: map[string]Sheet{}}
	for name, rid := range sheets {
		target := rels[rid]
		if target == "" {
			continue
		}
		target = strings.TrimPrefix(target, "/")
		if !strings.HasPrefix(target, "xl/") {
			target = "xl/" + target
		}
		b, e := read(target)
		if e != nil {
			continue
		}
		out.Sheets[name] = parseSheet(b, shared)
	}
	return out, nil
}

func parseShared(b []byte) []string {
	dec := xml.NewDecoder(bytes.NewReader(b))
	var result []string
	var cur strings.Builder
	inSI := false
	for {
		tok, e := dec.Token()
		if e == io.EOF {
			break
		}
		if e != nil {
			break
		}
		switch t := tok.(type) {
		case xml.StartElement:
			if t.Name.Local == "si" {
				inSI = true
				cur.Reset()
			}
			if inSI && t.Name.Local == "t" {
				var s string
				if dec.DecodeElement(&s, &t) == nil {
					cur.WriteString(s)
				}
			}
		case xml.EndElement:
			if t.Name.Local == "si" && inSI {
				result = append(result, cur.String())
				inSI = false
			}
		}
	}
	return result
}

type wbSheetXML struct {
	Name string `xml:"name,attr"`
	RID  string `xml:"http://schemas.openxmlformats.org/officeDocument/2006/relationships id,attr"`
}
type wbXML struct {
	Sheets []wbSheetXML `xml:"sheets>sheet"`
}

func parseWorkbookSheets(b []byte) map[string]string {
	var w wbXML
	_ = xml.Unmarshal(b, &w)
	m := map[string]string{}
	for _, s := range w.Sheets {
		m[s.Name] = s.RID
	}
	return m
}

type relXML struct {
	ID     string `xml:"Id,attr"`
	Target string `xml:"Target,attr"`
}
type relsXML struct {
	Rels []relXML `xml:"Relationship"`
}

func parseRels(b []byte) map[string]string {
	var r relsXML
	_ = xml.Unmarshal(b, &r)
	m := map[string]string{}
	for _, x := range r.Rels {
		m[x.ID] = x.Target
	}
	return m
}

var cellRefRE = regexp.MustCompile(`^([A-Z]+)`)

func colIndex(ref string) int {
	m := cellRefRE.FindStringSubmatch(ref)
	if len(m) < 2 {
		return 0
	}
	n := 0
	for _, c := range m[1] {
		n = n*26 + int(c-'A'+1)
	}
	return n - 1
}

func parseSheet(b []byte, shared []string) Sheet {
	dec := xml.NewDecoder(bytes.NewReader(b))
	var rows [][]Cell
	var row []Cell
	var inRow bool
	for {
		tok, e := dec.Token()
		if e == io.EOF {
			break
		}
		if e != nil {
			break
		}
		switch t := tok.(type) {
		case xml.StartElement:
			if t.Name.Local == "row" {
				inRow = true
				row = []Cell{}
			}
			if inRow && t.Name.Local == "c" {
				ref, ctype := "", ""
				for _, a := range t.Attr {
					if a.Name.Local == "r" {
						ref = a.Value
					}
					if a.Name.Local == "t" {
						ctype = a.Value
					}
				}
				idx := colIndex(ref)
				for len(row) <= idx {
					row = append(row, Cell{})
				}
				var value string
				for {
					tk, er := dec.Token()
					if er != nil {
						break
					}
					switch q := tk.(type) {
					case xml.StartElement:
						if q.Name.Local == "v" {
							var s string
							_ = dec.DecodeElement(&s, &q)
							value = s
						}
						if q.Name.Local == "t" && ctype == "inlineStr" {
							var s string
							_ = dec.DecodeElement(&s, &q)
							value += s
						}
					case xml.EndElement:
						if q.Name.Local == "c" {
							goto doneCell
						}
					}
				}
			doneCell:
				if ctype == "s" {
					if n, er := strconv.Atoi(value); er == nil && n >= 0 && n < len(shared) {
						value = shared[n]
					}
				}
				row[idx] = Cell{Value: value, Type: ctype}
			}
		case xml.EndElement:
			if t.Name.Local == "row" && inRow {
				rows = append(rows, row)
				inRow = false
			}
		}
	}
	return Sheet{Rows: rows}
}

func headerMap(row []Cell) map[string]int {
	m := map[string]int{}
	for i, c := range row {
		if strings.TrimSpace(c.Value) != "" {
			m[strings.TrimSpace(c.Value)] = i
		}
	}
	return m
}

func cell(row []Cell, idx int) string {
	if idx < 0 || idx >= len(row) {
		return ""
	}
	return row[idx].Value
}

func transformCells(src Sheet, target []string, required []string, clearCustomerDescription bool) ([][]Cell, error) {
	if len(src.Rows) < 2 {
		return nil, errors.New("資料列不足")
	}

	// ERP export has headers on worksheet row 3. XML may omit the blank row 2,
	// therefore find the header row by the required field names instead of a fixed row index.
	hrow := -1
	var hm map[string]int
	for i, r := range src.Rows {
		m := headerMap(r)
		ok := true
		for _, x := range required {
			if _, exists := m[x]; !exists {
				ok = false
				break
			}
		}
		if ok {
			hrow = i
			hm = m
			break
		}
	}
	if hrow < 0 {
		return nil, fmt.Errorf("缺少必要欄位：%s", strings.Join(required, "、"))
	}

	var out [][]Cell
	for _, r := range src.Rows[hrow+1:] {
		nonblank := false
		for _, c := range r {
			if strings.TrimSpace(c.Value) != "" {
				nonblank = true
				break
			}
		}
		if !nonblank {
			continue
		}

		dst := make([]Cell, len(target))
		for i, h := range target {
			if clearCustomerDescription && h == "客戶描述" {
				dst[i] = Cell{}
				continue
			}
			if j, ok := hm[h]; ok && j >= 0 && j < len(r) {
				dst[i] = r[j]
			}
		}
		out = append(out, dst)
	}
	if len(out) == 0 {
		return nil, errors.New("沒有可轉換的資料")
	}
	return out, nil
}

// transformSheet remains as a small value-only wrapper for unit tests and callers that
// only need field mapping. The production writer uses transformCells so Excel cell types
// (especially string-vs-number) are retained from the ERP export.
func transformSheet(src Sheet, target []string, required []string, clearCustomerDescription bool) ([][]string, error) {
	cells, err := transformCells(src, target, required, clearCustomerDescription)
	if err != nil {
		return nil, err
	}
	out := make([][]string, len(cells))
	for i, row := range cells {
		out[i] = make([]string, len(row))
		for j, c := range row {
			out[i][j] = c.Value
		}
	}
	return out, nil
}

type ConvResult struct{ OrderType, OrderNo, Customer, OutputPath string }

func convertFile(srcPath, outDir string) (ConvResult, error) {
	wb, err := readXLSX(srcPath)
	if err != nil {
		return ConvResult{}, fmt.Errorf("讀取 Excel 失敗：%w", err)
	}
	head, ok := wb.Sheets["單頭資料"]
	if !ok {
		return ConvResult{}, errors.New("找不到「單頭資料」工作表")
	}
	body, ok := wb.Sheets["單身資料"]
	if !ok {
		return ConvResult{}, errors.New("找不到「單身資料」工作表")
	}
	hrows, err := transformCells(head, headHeaders, requiredHead, true)
	if err != nil {
		return ConvResult{}, fmt.Errorf("單頭資料：%w", err)
	}
	brows, err := transformCells(body, bodyHeaders, requiredBody, false)
	if err != nil {
		return ConvResult{}, fmt.Errorf("單身資料：%w", err)
	}

	hi := map[string]int{}
	for i, h := range headHeaders {
		hi[h] = i
	}
	orderType := hrows[0][hi["銷貨單別"]].Value
	orderNo := hrows[0][hi["銷貨單號"]].Value
	cust := hrows[0][hi["客戶全名"]].Value
	if strings.TrimSpace(orderType) == "" || strings.TrimSpace(orderNo) == "" {
		return ConvResult{}, errors.New("銷貨單別或銷貨單號為空")
	}
	if err := os.MkdirAll(outDir, 0o755); err != nil {
		return ConvResult{}, fmt.Errorf("建立輸出資料夾失敗：%w", err)
	}
	name := fmt.Sprintf("COPI-%s-%s.xlsx", sanitize(orderType), sanitize(orderNo))
	dest := filepath.Join(outDir, name)
	if _, e := os.Stat(dest); e == nil {
		return ConvResult{}, fmt.Errorf("檔名重複：%s", name)
	}

	// Write to a temporary file in the same destination directory first. Only a
	// completely written workbook is renamed to the final COPI filename, so a
	// conversion failure cannot leave a half-written file for POS to import.
	tmp, err := os.CreateTemp(outDir, ".smartcopi-*.tmp")
	if err != nil {
		return ConvResult{}, fmt.Errorf("建立暫存輸出檔失敗：%w", err)
	}
	tmpPath := tmp.Name()
	if err := tmp.Close(); err != nil {
		_ = os.Remove(tmpPath)
		return ConvResult{}, fmt.Errorf("建立暫存輸出檔失敗：%w", err)
	}
	defer os.Remove(tmpPath)

	if err := writeXLSX(tmpPath, hrows, brows); err != nil {
		return ConvResult{}, fmt.Errorf("寫入 Excel 失敗：%w", err)
	}
	if err := os.Rename(tmpPath, dest); err != nil {
		return ConvResult{}, fmt.Errorf("完成輸出檔失敗：%w", err)
	}
	return ConvResult{OrderType: orderType, OrderNo: orderNo, Customer: cust, OutputPath: dest}, nil
}

func sanitize(s string) string {
	r := strings.NewReplacer("\\", "_", "/", "_", ":", "_", "*", "_", "?", "_", "\"", "_", "<", "_", ">", "_", "|", "_")
	return strings.TrimSpace(r.Replace(s))
}

type sharedStrings struct {
	ids    map[string]int
	values []string
	count  int
}

func newSharedStrings() *sharedStrings {
	return &sharedStrings{ids: map[string]int{}}
}

func (s *sharedStrings) id(v string) int {
	s.count++
	if id, ok := s.ids[v]; ok {
		return id
	}
	id := len(s.values)
	s.ids[v] = id
	s.values = append(s.values, v)
	return id
}

func (s *sharedStrings) xml() string {
	var b strings.Builder
	b.WriteString(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="`)
	b.WriteString(strconv.Itoa(s.count))
	b.WriteString(`" uniqueCount="`)
	b.WriteString(strconv.Itoa(len(s.values)))
	b.WriteString(`">`)
	for _, v := range s.values {
		b.WriteString(`<si><t`)
		if strings.TrimSpace(v) != v {
			b.WriteString(` xml:space="preserve"`)
		}
		b.WriteString(`>`)
		b.WriteString(xmlEsc(v))
		b.WriteString(`</t></si>`)
	}
	b.WriteString(`</sst>`)
	return b.String()
}

func writeXLSX(path string, headRows, bodyRows [][]Cell) error {
	f, err := os.Create(path)
	if err != nil {
		return err
	}
	zw := zip.NewWriter(f)
	closeAll := func(e error) error {
		e2 := zw.Close()
		e3 := f.Close()
		if e != nil {
			return e
		}
		if e2 != nil {
			return e2
		}
		return e3
	}

	ss := newSharedStrings()
	sheet1 := buildSheetXML(headHeaders, headRows, ss)
	sheet2 := buildSheetXML(bodyHeaders, bodyRows, ss)
	files := []struct {
		name string
		data string
	}{
		{"[Content_Types].xml", contentTypesXML},
		{"_rels/.rels", rootRelsXML},
		{"docProps/app.xml", appPropsXML},
		{"docProps/core.xml", corePropsXML()},
		{"xl/workbook.xml", workbookXML},
		{"xl/_rels/workbook.xml.rels", workbookRelsXML},
		{"xl/styles.xml", stylesXML},
		{"xl/theme/theme1.xml", themeXML},
		{"xl/sharedStrings.xml", ss.xml()},
		{"xl/worksheets/sheet1.xml", sheet1},
		{"xl/worksheets/sheet2.xml", sheet2},
	}
	for _, file := range files {
		w, e := zw.Create(file.name)
		if e != nil {
			return closeAll(e)
		}
		if _, e = w.Write([]byte(file.data)); e != nil {
			return closeAll(e)
		}
	}
	return closeAll(nil)
}

func colName(n int) string {
	s := ""
	for n > 0 {
		n--
		s = string(rune('A'+n%26)) + s
		n /= 26
	}
	return s
}

func xmlEsc(s string) string {
	var b bytes.Buffer
	_ = xml.EscapeText(&b, []byte(s))
	return b.String()
}

func numericValue(c Cell) bool {
	if c.Value == "" {
		return false
	}
	// Shared/inline string cells from the ERP source must remain strings even if
	// the visible text looks numeric (e.g. 銷貨單別 "234" or 序號 "0001").
	if c.Type == "s" || c.Type == "inlineStr" || c.Type == "str" {
		return false
	}
	_, err := strconv.ParseFloat(c.Value, 64)
	return err == nil
}

func buildSheetXML(headers []string, rows [][]Cell, ss *sharedStrings) string {
	lastRow := len(rows) + 3
	if lastRow < 4 {
		lastRow = 4
	}
	dim := "A1:" + colName(len(headers)) + strconv.Itoa(lastRow)

	var b strings.Builder
	b.WriteString(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><dimension ref="`)
	b.WriteString(dim)
	b.WriteString(`"/><sheetViews><sheetView workbookViewId="0"/></sheetViews><sheetFormatPr defaultRowHeight="15"/><sheetData>`)

	b.WriteString(`<row r="1"><c r="E1" t="s"><v>`)
	b.WriteString(strconv.Itoa(ss.id("*銷貨單建立作業")))
	b.WriteString(`</v></c></row><row r="2"></row><row r="3">`)
	for i, h := range headers {
		ref := colName(i+1) + "3"
		b.WriteString(`<c r="` + ref + `" t="s"><v>` + strconv.Itoa(ss.id(h)) + `</v></c>`)
	}
	b.WriteString(`</row>`)

	for ri, row := range rows {
		rnum := ri + 4
		b.WriteString(`<row r="` + strconv.Itoa(rnum) + `">`)
		for ci, c := range row {
			if c.Value == "" {
				continue
			}
			ref := colName(ci+1) + strconv.Itoa(rnum)
			h := headers[ci]
			if numericValue(c) {
				b.WriteString(`<c r="` + ref + `"`)
				if dateFields[h] {
					b.WriteString(` s="1"`)
				}
				b.WriteString(`><v>` + xmlEsc(c.Value) + `</v></c>`)
			} else {
				b.WriteString(`<c r="` + ref + `" t="s"><v>` + strconv.Itoa(ss.id(c.Value)) + `</v></c>`)
			}
		}
		b.WriteString(`</row>`)
	}
	b.WriteString(`</sheetData></worksheet>`)
	return b.String()
}

const contentTypesXML = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/theme/theme1.xml" ContentType="application/vnd.openxmlformats-officedocument.theme+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/><Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/><Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/><Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/></Types>`

const rootRelsXML = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/></Relationships>`

const workbookXML = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><workbookPr/><bookViews><workbookView xWindow="0" yWindow="0" windowWidth="12000" windowHeight="8000"/></bookViews><sheets><sheet name="單頭資料" sheetId="1" r:id="rId1"/><sheet name="單身資料" sheetId="2" r:id="rId2"/></sheets><calcPr calcId="124519"/></workbook>`

const workbookRelsXML = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/><Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/><Relationship Id="rId5" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme" Target="theme/theme1.xml"/></Relationships>`

const stylesXML = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="1"><numFmt numFmtId="164" formatCode="yyyy/m/d"/></numFmts><fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>`

const appPropsXML = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes"><Application>Microsoft Excel</Application><DocSecurity>0</DocSecurity><ScaleCrop>false</ScaleCrop><HeadingPairs><vt:vector size="2" baseType="variant"><vt:variant><vt:lpstr>Worksheets</vt:lpstr></vt:variant><vt:variant><vt:i4>2</vt:i4></vt:variant></vt:vector></HeadingPairs><TitlesOfParts><vt:vector size="2" baseType="lpstr"><vt:lpstr>單頭資料</vt:lpstr><vt:lpstr>單身資料</vt:lpstr></vt:vector></TitlesOfParts><Company></Company><LinksUpToDate>false</LinksUpToDate><SharedDoc>false</SharedDoc><HyperlinksChanged>false</HyperlinksChanged><AppVersion>16.0300</AppVersion></Properties>`

const themeXML = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Office Theme"><a:themeElements><a:clrScheme name="Office"><a:dk1><a:sysClr val="windowText" lastClr="000000"/></a:dk1><a:lt1><a:sysClr val="window" lastClr="FFFFFF"/></a:lt1><a:dk2><a:srgbClr val="1F497D"/></a:dk2><a:lt2><a:srgbClr val="EEECE1"/></a:lt2><a:accent1><a:srgbClr val="4F81BD"/></a:accent1><a:accent2><a:srgbClr val="C0504D"/></a:accent2><a:accent3><a:srgbClr val="9BBB59"/></a:accent3><a:accent4><a:srgbClr val="8064A2"/></a:accent4><a:accent5><a:srgbClr val="4BACC6"/></a:accent5><a:accent6><a:srgbClr val="F79646"/></a:accent6><a:hlink><a:srgbClr val="0000FF"/></a:hlink><a:folHlink><a:srgbClr val="800080"/></a:folHlink></a:clrScheme><a:fontScheme name="Office"><a:majorFont><a:latin typeface="Calibri"/></a:majorFont><a:minorFont><a:latin typeface="Calibri"/></a:minorFont></a:fontScheme><a:fmtScheme name="Office"><a:fillStyleLst><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:fillStyleLst><a:lnStyleLst><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln></a:lnStyleLst><a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst><a:bgFillStyleLst><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>`

func corePropsXML() string {
	t := time.Now().UTC().Format(time.RFC3339)
	return `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:dcmitype="http://purl.org/dc/dcmitype/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><dc:creator>SMART COPI Converter</dc:creator><cp:lastModifiedBy>SMART COPI Converter</cp:lastModifiedBy><dcterms:created xsi:type="dcterms:W3CDTF">` + t + `</dcterms:created><dcterms:modified xsi:type="dcterms:W3CDTF">` + t + `</dcterms:modified></cp:coreProperties>`
}
