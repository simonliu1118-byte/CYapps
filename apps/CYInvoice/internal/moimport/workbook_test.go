package moimport

import "testing"

func TestParseMORowsGroupsItemsAndAllowsNegativeSubsidy(t *testing.T) {
	header := []string{"訂單編號", "發票類型", "載具", "載具顯碼", "載具隱碼", "捐贈對象", "買方統編", "買方名稱", "買方地址", "買方電話", "買方電子信箱", "品名", "課稅別", "數量", "單價(含稅)", "小計金額(含稅)", "商品備註(最多40個字)", "總備註(最多200個字)", "小計四捨五入"}
	rows := [][]string{
		header,
		{"66090300839412", "B2C", "會員載具", "motmp_66090300839412", "motmp_66090300839412", "", "", "陳*菁", "", "", "", "商品", "應稅", "1", "653", "653", "備註", "總備註", "1"},
		{"66090300839412", "", "", "", "", "", "", "", "", "", "", "運費", "應稅", "1", "45", "45", "", "", ""},
		{"66090300839412", "", "", "", "", "", "", "", "", "", "", "運費補貼", "應稅", "1", "-45", "-45", "", "", ""},
	}
	orders, err := ParseRows(rows)
	if err != nil { t.Fatal(err) }
	if len(orders) != 1 || len(orders[0].Items) != 3 || orders[0].TotalAmount != 653 { t.Fatalf("orders = %#v", orders) }
	if orders[0].CarrierID1 != "motmp_66090300839412" || orders[0].Items[0].Remark != "備註" { t.Fatalf("order = %#v", orders[0]) }
}

func TestParseRowsUsesHeaderNamesNotColumnPositions(t *testing.T) {
	header := []string{"品名", "訂單編號", "發票類型", "載具", "載具顯碼", "載具隱碼", "捐贈對象", "買方統編", "買方名稱", "買方地址", "買方電話", "買方電子信箱", "課稅別", "數量", "單價(含稅)", "小計金額(含稅)", "商品備註(最多40個字)", "總備註(最多200個字)"}
	row := []string{"商品", "ORDER-1", "B2C", "", "", "", "", "", "", "", "", "", "應稅", "2", "50", "100", "", ""}
	orders, err := ParseRows([][]string{header, row})
	if err != nil { t.Fatal(err) }
	if len(orders) != 1 || orders[0].OrderID != "ORDER-1" || orders[0].TotalAmount != 100 { t.Fatalf("orders = %#v", orders) }
}

func TestParseRowsRejectsMissingRequiredColumn(t *testing.T) {
	if _, err := ParseRows([][]string{{"訂單編號", "品名"}, {"1", "商品"}}); err == nil { t.Fatal("missing columns accepted") }
}


func TestParseOrderExportUsesOfficialAmountsInsteadOfProductPrice(t *testing.T) {
	header := []string{
		"訂單編號", "商品名稱", "數量", "商品售價", "應稅(免稅)",
		"客人支付運費", "平台補貼運費", "商品滿額免運費",
		rawItemAmountHeader, rawTotalAmountHeader, "發票開立統編",
	}
	rows := [][]string{
		header,
		{"66090500872566", "神龍鍍金磁珠", "1", "120", "應稅", "65", "0", "-65", "113", "225", "92644802"},
		{"66090500872566", "神龍鍍金磁珠", "1", "120", "應稅", "", "", "", "112", "225", ""},
	}
	orders, err := ParseRows(rows)
	if err != nil {
		t.Fatal(err)
	}
	if len(orders) != 1 {
		t.Fatalf("order count = %d", len(orders))
	}
	order := orders[0]
	if order.TotalAmount != 225 {
		t.Fatalf("total = %d, want official total 225", order.TotalAmount)
	}
	if len(order.Items) != 4 {
		t.Fatalf("item count = %d, want two products plus shipping and free shipping", len(order.Items))
	}
	if order.Items[0].UnitPrice != 113 || order.Items[0].Amount != 113 ||
		order.Items[1].UnitPrice != 112 || order.Items[1].Amount != 112 {
		t.Fatalf("official product amounts were not preserved: %#v", order.Items[:2])
	}
	if order.Items[2].Description != "運費" || order.Items[2].Amount != 65 {
		t.Fatalf("shipping item = %#v", order.Items[2])
	}
	if order.Items[3].Description != "滿額免運費" || order.Items[3].Amount != -65 {
		t.Fatalf("free-shipping item = %#v", order.Items[3])
	}
	if order.BuyerBAN != "92644802" || order.Carrier != "公司戶" {
		t.Fatalf("company invoice fields = %#v", order)
	}
}

func TestParseOrderExportAllowsOfficialRoundedSubtotal(t *testing.T) {
	header := []string{
		"訂單編號", "商品名稱", "數量", "應稅(免稅)",
		"客人支付運費", "平台補貼運費", "商品滿額免運費",
		rawItemAmountHeader, rawTotalAmountHeader, "發票開立統編",
	}
	rows := [][]string{
		header,
		{"ORDER-ROUND", "三入商品", "3", "應稅", "0", "0", "0", "100", "100", ""},
	}
	orders, err := ParseRows(rows)
	if err != nil {
		t.Fatal(err)
	}
	item := orders[0].Items[0]
	if item.Amount != 100 || !item.AllowSubtotalRounding {
		t.Fatalf("official rounded item = %#v", item)
	}
}

func TestParseOrderExportRejectsConflictingOfficialTotals(t *testing.T) {
	header := []string{
		"訂單編號", "商品名稱", "數量", "應稅(免稅)",
		"客人支付運費", "平台補貼運費", "商品滿額免運費",
		rawItemAmountHeader, rawTotalAmountHeader, "發票開立統編",
	}
	rows := [][]string{
		header,
		{"ORDER-BAD", "商品", "1", "應稅", "0", "0", "0", "113", "114", ""},
	}
	if _, err := ParseRows(rows); err == nil {
		t.Fatal("conflicting official item and order totals were accepted")
	}
}

func TestParseOrderExportUsesShippingSubsidyOnlyWhenOfficialTotalRequiresIt(t *testing.T) {
	header := []string{
		"訂單編號", "商品名稱", "數量", "應稅(免稅)",
		"客人支付運費", "平台補貼運費", "商品滿額免運費",
		rawItemAmountHeader, rawTotalAmountHeader, "發票開立統編",
	}
	rows := [][]string{
		header,
		{"ORDER-SUBSIDY", "商品", "1", "應稅", "0", "20", "0", "100", "120", ""},
	}
	orders, err := ParseRows(rows)
	if err != nil {
		t.Fatal(err)
	}
	if len(orders[0].Items) != 2 || orders[0].Items[1].Description != "運費補貼" || orders[0].Items[1].Amount != 20 {
		t.Fatalf("items = %#v", orders[0].Items)
	}
}

func TestParseOrderExportMatchesOfficialBuyerCarrierAndProductDescription(t *testing.T) {
	header := []string{
		"訂單編號", "商品名稱", "規格1", "規格2", "數量", "應稅(免稅)", "收件人姓名",
		"客人支付運費", "平台補貼運費", "商品滿額免運費",
		rawItemAmountHeader, rawTotalAmountHeader, "發票開立統編", "統編",
	}
	rows := [][]string{
		header,
		{"66090700928934", "匿名測試商品一", "18cm", "100碼", "1", "應稅", "測*者", "45", "-45", "0", "500", "898", "", "91442639"},
		{"66090700928934", "匿名測試商品二", "12x15cm", "3包優惠價", "1", "應稅", "測*者", "", "", "", "199", "898", "", "91442639"},
		{"66090700928934", "匿名測試商品三", "12x15cm", "3包優惠價", "1", "應稅", "測*者", "", "", "", "199", "898", "", "91442639"},
	}
	orders, err := ParseRows(rows)
	if err != nil { t.Fatal(err) }
	order := orders[0]
	if order.BuyerBAN != "" || order.BuyerName != "測*者" {
		t.Fatalf("buyer fields = %#v", order)
	}
	if order.Carrier != CarrierMember || order.CarrierID1 != "motmp_66090700928934" || order.CarrierID2 != order.CarrierID1 {
		t.Fatalf("carrier fields = %#v", order)
	}
	if order.Items[0].Description != "匿名測試商品一 18cm 100碼" {
		t.Fatalf("description = %q", order.Items[0].Description)
	}
	if len(order.Items) != 5 || order.Items[3].Description != "運費" || order.Items[3].Amount != 45 ||
		order.Items[4].Description != "運費補貼" || order.Items[4].Amount != -45 || order.TotalAmount != 898 {
		t.Fatalf("official conversion = %#v", order)
	}
}


func TestParseOrderExportMatchesOfficialConvertedWorkbookAmounts(t *testing.T) {
	header := []string{
		"訂單編號", "商品名稱", "數量", "商品售價", "應稅(免稅)",
		"客人支付運費", "平台補貼運費", "商品滿額免運費",
		rawItemAmountHeader, rawTotalAmountHeader, "發票開立統編",
	}
	rows := [][]string{header}
	officialAmounts := []string{"113", "113", "113", "113", "113", "113", "112", "112", "112"}
	for index, amount := range officialAmounts {
		shipping, freeShipping := "", ""
		if index == 0 {
			shipping, freeShipping = "65", "-65"
		}
		rows = append(rows, []string{
			"ORDER-OFFICIAL", "匿名測試商品", "1", "120", "應稅",
			shipping, "0", freeShipping, amount, "1014", "12345675",
		})
	}
	orders, err := ParseRows(rows)
	if err != nil {
		t.Fatal(err)
	}
	order := orders[0]
	if order.TotalAmount != 1014 {
		t.Fatalf("total = %d, want 1014", order.TotalAmount)
	}
	if len(order.Items) != 11 {
		t.Fatalf("item count = %d, want 9 products plus 2 shipping lines", len(order.Items))
	}
	for index, amount := range []int64{113, 113, 113, 113, 113, 113, 112, 112, 112} {
		if order.Items[index].Amount != amount || order.Items[index].UnitPrice != amount {
			t.Fatalf("item %d = %#v, want official amount %d", index+1, order.Items[index], amount)
		}
	}
	if order.Items[9].Description != "運費" || order.Items[9].Amount != 65 ||
		order.Items[10].Description != "滿額免運費" || order.Items[10].Amount != -65 {
		t.Fatalf("shipping lines = %#v", order.Items[9:])
	}
}
