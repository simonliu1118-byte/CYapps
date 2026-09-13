package main

import (
	"strings"
	"testing"
)

func TestTransformHeadMapsByHeaderAndClearsCustomerDescription(t *testing.T) {
	src := Sheet{Rows: [][]Cell{
		{{Value: "銷貨單號"}, {Value: "客戶描述"}, {Value: "銷貨單別"}, {Value: "客戶全名"}},
		{{Value: "SO0001", Type: "s"}, {Value: "internal note", Type: "s"}, {Value: "A01", Type: "s"}, {Value: "測試客戶", Type: "s"}},
	}}
	rows, err := transformSheet(src, headHeaders, requiredHead, true)
	if err != nil {
		t.Fatal(err)
	}
	idx := map[string]int{}
	for i, h := range headHeaders {
		idx[h] = i
	}
	if rows[0][idx["銷貨單別"]] != "A01" || rows[0][idx["銷貨單號"]] != "SO0001" {
		t.Fatalf("header mapping failed: %#v", rows[0])
	}
	if rows[0][idx["客戶描述"]] != "" {
		t.Fatalf("customer description must be blank")
	}
}

func TestTransformCellsPreservesERPCellType(t *testing.T) {
	src := Sheet{Rows: [][]Cell{
		{{Value: "銷貨單別"}, {Value: "銷貨單號"}, {Value: "單據日期"}},
		{{Value: "234", Type: "s"}, {Value: "20260710002", Type: "s"}, {Value: "46213", Type: ""}},
	}}
	rows, err := transformCells(src, headHeaders, requiredHead, true)
	if err != nil {
		t.Fatal(err)
	}
	idx := map[string]int{}
	for i, h := range headHeaders {
		idx[h] = i
	}
	if rows[0][idx["銷貨單別"]].Type != "s" || rows[0][idx["銷貨單號"]].Type != "s" {
		t.Fatalf("numeric-looking ERP strings must stay strings: %#v", rows[0])
	}
	if rows[0][idx["單據日期"]].Type != "" {
		t.Fatalf("numeric ERP date must stay numeric")
	}
}

func TestBuildSheetUsesSharedStringsAndDateStyle(t *testing.T) {
	ss := newSharedStrings()
	rows := [][]Cell{{
		{Value: "234", Type: "s"},
		{},
		{Value: "20260710002", Type: "s"},
		{Value: "46213", Type: ""},
	}}
	xml := buildSheetXML(headHeaders, rows, ss)
	if !strings.Contains(xml, `<c r="A4" t="s"><v>`) {
		t.Fatalf("sales type should be emitted as a shared string: %s", xml)
	}
	if !strings.Contains(xml, `<c r="C4" t="s"><v>`) {
		t.Fatalf("sales number should be emitted as a shared string: %s", xml)
	}
	if !strings.Contains(xml, `<c r="D4" s="1"><v>46213</v></c>`) {
		t.Fatalf("date should be numeric with the date style: %s", xml)
	}
	if ss.count == 0 || len(ss.values) == 0 {
		t.Fatal("shared string table was not populated")
	}
}

func TestTransformBodyRequiresFields(t *testing.T) {
	src := Sheet{Rows: [][]Cell{
		{{Value: "序號"}, {Value: "品號"}, {Value: "品名"}, {Value: "數量"}, {Value: "單價"}},
		{{Value: "0001"}, {Value: "P001"}, {Value: "商品"}, {Value: "1"}, {Value: "100"}},
	}}
	if _, err := transformSheet(src, bodyHeaders, requiredBody, false); err == nil {
		t.Fatal("expected missing 金額 error")
	}
}

func TestSanitizeFilename(t *testing.T) {
	got := sanitize(`A/B:C*D?E"F<G>H|I`)
	if got != "A_B_C_D_E_F_G_H_I" {
		t.Fatalf("unexpected sanitize result: %q", got)
	}
}
