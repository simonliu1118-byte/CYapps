package main

import "testing"

func TestTransformHeadMapsByHeaderAndClearsCustomerDescription(t *testing.T) {
	src := Sheet{Rows: [][]Cell{
		{{Value: "銷貨單號"}, {Value: "客戶描述"}, {Value: "銷貨單別"}, {Value: "客戶全名"}},
		{{Value: "SO0001"}, {Value: "internal note"}, {Value: "A01"}, {Value: "測試客戶"}},
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
