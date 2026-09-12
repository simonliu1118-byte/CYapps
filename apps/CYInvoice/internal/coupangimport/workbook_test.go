package coupangimport

import "testing"

func TestParseRowsMatchesVerifiedCoupangSample(t *testing.T) {
	rows := [][]string{
		{"編號", "訂單編號", "顯示產品名稱", "數量", "訂購人姓名", "應開立予買家之發票金額", "應開立予酷澎之發票金額", "統一編號"},
		{"1", "TEST-ORDER-001", "測試商品（測試規格）", "1", "測試買家", "500", "0.0", ""},
	}
	orders, err := ParseRows(rows)
	if err != nil {
		t.Fatal(err)
	}
	if len(orders) != 1 {
		t.Fatalf("orders=%d", len(orders))
	}
	order := orders[0]
	if order.OrderID != "TEST-ORDER-001" || order.BuyerName != "測試買家" || order.BuyerBAN != "" || order.TotalAmount != 500 {
		t.Fatalf("unexpected order: %+v", order)
	}
	if len(order.Items) != 1 || order.Items[0].Quantity != 1 || order.Items[0].UnitPrice != 500 || order.Items[0].Amount != 500 {
		t.Fatalf("unexpected item: %+v", order.Items)
	}
}

func TestParseRowsUsesHeadersNotColumnPositions(t *testing.T) {
	rows := [][]string{
		{"統一編號", "應開立予酷澎之發票金額", "訂購人姓名", "數量", "顯示產品名稱", "應開立予買家之發票金額", "訂單編號"},
		{"", "125", "買家", "2", "商品", "600", "ORDER-1"},
	}
	orders, err := ParseRows(rows)
	if err != nil {
		t.Fatal(err)
	}
	if len(orders) != 1 || orders[0].Items[0].UnitPrice != 300 || orders[0].TotalAmount != 600 {
		t.Fatalf("unexpected conversion: %+v", orders)
	}
}

func TestParseRowsGroupsItemsByOrder(t *testing.T) {
	headers := []string{"訂單編號", "顯示產品名稱", "數量", "訂購人姓名", "應開立予買家之發票金額", "應開立予酷澎之發票金額", "統一編號"}
	rows := [][]string{
		headers,
		{"ORDER-1", "商品甲", "1", "買家", "100", "0", ""},
		{"ORDER-1", "商品乙", "2", "買家", "400", "0", ""},
	}
	orders, err := ParseRows(rows)
	if err != nil {
		t.Fatal(err)
	}
	if len(orders) != 1 || len(orders[0].Items) != 2 || orders[0].TotalAmount != 500 {
		t.Fatalf("unexpected grouping: %+v", orders)
	}
}

func TestParseRowsKeepsFractionalUnitPriceAtSevenPlaces(t *testing.T) {
	rows := [][]string{
		{"訂單編號", "顯示產品名稱", "數量", "訂購人姓名", "應開立予買家之發票金額", "應開立予酷澎之發票金額", "統一編號"},
		{"ORDER-1", "商品", "3", "買家", "100", "0", ""},
	}
	orders, err := ParseRows(rows)
	if err != nil { t.Fatal(err) }
	item := orders[0].Items[0]
	if item.UnitPriceDecimal != "33.3333333" || item.AmountDecimal != "100" || !item.AllowSubtotalRounding {
		t.Fatalf("item=%#v", item)
	}
}
