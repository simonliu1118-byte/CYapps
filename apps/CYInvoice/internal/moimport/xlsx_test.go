package moimport

import (
	"archive/zip"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestReadXLSXAndParseMOWorkbook(t *testing.T) {
	path := filepath.Join(t.TempDir(), "mo-test.xlsx")
	file, err := os.Create(path)
	if err != nil {
		t.Fatal(err)
	}
	book := zip.NewWriter(file)

	shared := append([]string(nil), convertedRequiredHeaders...)
	var sharedXML strings.Builder
	sharedXML.WriteString(`<?xml version="1.0" encoding="UTF-8"?><sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">`)
	for _, value := range shared {
		sharedXML.WriteString("<si><t>")
		writeXMLText(&sharedXML, value)
		sharedXML.WriteString("</t></si>")
	}
	sharedXML.WriteString("</sst>")

	var sheetXML strings.Builder
	sheetXML.WriteString(`<?xml version="1.0" encoding="UTF-8"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1">`)
	for index := range shared {
		fmt.Fprintf(&sheetXML, `<c r="%s1" t="s"><v>%d</v></c>`, testColumnName(index), index)
	}
	sheetXML.WriteString(`</row><row r="2">`)
	values := []string{
		"66090300839412", "B2C", "會員載具", "motmp_66090300839412", "motmp_66090300839412", "", "", "王*明", "", "", "",
		"測試商品", "應稅", "2", "50", "100", "商品備註", "總備註",
	}
	for index, value := range values {
		if value == "" {
			continue // Exercise sparse-cell references used by real workbooks.
		}
		fmt.Fprintf(&sheetXML, `<c r="%s2" t="inlineStr"><is><t>`, testColumnName(index))
		writeXMLText(&sheetXML, value)
		sheetXML.WriteString(`</t></is></c>`)
	}
	sheetXML.WriteString(`</row></sheetData></worksheet>`)

	writeZipPart(t, book, "xl/sharedStrings.xml", sharedXML.String())
	writeZipPart(t, book, "xl/worksheets/sheet1.xml", sheetXML.String())
	if err := book.Close(); err != nil {
		t.Fatal(err)
	}
	if err := file.Close(); err != nil {
		t.Fatal(err)
	}

	rows, err := ReadXLSX(path)
	if err != nil {
		t.Fatal(err)
	}
	orders, err := ParseRows(rows)
	if err != nil {
		t.Fatal(err)
	}
	if len(orders) != 1 || orders[0].OrderID != "66090300839412" || orders[0].TotalAmount != 100 {
		t.Fatalf("orders = %#v", orders)
	}
	if orders[0].CarrierID1 != "motmp_66090300839412" || orders[0].Items[0].Remark != "商品備註" {
		t.Fatalf("order = %#v", orders[0])
	}
}

func TestColumnIndexSupportsColumnsAfterZ(t *testing.T) {
	tests := map[string]int{"A1": 0, "Z2": 25, "AA3": 26, "AZ4": 51, "BA5": 52}
	for reference, want := range tests {
		if got := columnIndex(reference); got != want {
			t.Fatalf("columnIndex(%q) = %d, want %d", reference, got, want)
		}
	}
}

func TestReadXLSXUsesWorkbookRelationshipForFirstLogicalSheet(t *testing.T) {
	filePath := filepath.Join(t.TempDir(), "relationship.xlsx")
	file, err := os.Create(filePath)
	if err != nil { t.Fatal(err) }
	book := zip.NewWriter(file)
	writeZipPart(t, book, "xl/workbook.xml", `<?xml version="1.0"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Orders" sheetId="1" r:id="rId7"/></sheets></workbook>`)
	writeZipPart(t, book, "xl/_rels/workbook.xml.rels", `<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId7" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/orders.xml"/></Relationships>`)
	writeZipPart(t, book, "xl/worksheets/sheet1.xml", `<?xml version="1.0"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row><c r="A1" t="inlineStr"><is><t>WRONG</t></is></c></row></sheetData></worksheet>`)
	writeZipPart(t, book, "xl/worksheets/orders.xml", `<?xml version="1.0"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row><c r="A1" t="inlineStr"><is><t>RIGHT</t></is></c></row></sheetData></worksheet>`)
	if err := book.Close(); err != nil { t.Fatal(err) }
	if err := file.Close(); err != nil { t.Fatal(err) }
	rows, err := ReadXLSX(filePath)
	if err != nil { t.Fatal(err) }
	if len(rows) != 1 || len(rows[0]) != 1 || rows[0][0] != "RIGHT" {
		t.Fatalf("rows=%#v", rows)
	}
}

func writeZipPart(t *testing.T, book *zip.Writer, name, content string) {
	t.Helper()
	part, err := book.Create(name)
	if err != nil {
		t.Fatal(err)
	}
	if _, err := part.Write([]byte(content)); err != nil {
		t.Fatal(err)
	}
}

func writeXMLText(builder *strings.Builder, value string) {
	value = strings.ReplaceAll(value, "&", "&amp;")
	value = strings.ReplaceAll(value, "<", "&lt;")
	value = strings.ReplaceAll(value, ">", "&gt;")
	builder.WriteString(value)
}

func testColumnName(index int) string {
	var result string
	for index++; index > 0; index = (index - 1) / 26 {
		result = string(rune('A'+(index-1)%26)) + result
	}
	return result
}
