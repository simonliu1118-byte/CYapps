package recordview

import (
	"strings"
	"testing"

	"cyinvoice/internal/appdata"
)

func TestFormatMakesUnsafeStatesExplicit(t *testing.T) {
	records := []appdata.InvoiceRecord{
		{OrderID: "FAILED-1", OriginalOrderID: "FAILED-1", InvoiceState: appdata.InvoiceStateFailed, ErrorMessage: "API rejected", Amount: 10},
		{OrderID: "UNKNOWN-1", OriginalOrderID: "UNKNOWN-1", InvoiceState: appdata.InvoiceStateUnknown, ErrorMessage: "timeout\r\ndo not resend", Amount: 20},
		{OrderID: "OPEN-1", OriginalOrderID: "OPEN-1", InvoiceNumber: "AA12345678", InvoiceState: appdata.InvoiceStateOpened, UploadStatus: 99, Amount: 30, Environment: appdata.EnvironmentTest},
	}
	text := Format(records, "")
	for _, required := range []string{
		"【需確認｜開立結果不明，禁止重送】",
		"【失敗｜未開立，可修正後重新嘗試】",
		"【完成｜發票已開立並上傳完成】",
		"原因：timeout do not resend",
		"已開立 1", "需確認 1", "失敗 1",
	} {
		if !strings.Contains(text, required) {
			t.Fatalf("missing %q in:\n%s", required, text)
		}
	}
	if strings.Contains(text, "timeout\r\n") {
		t.Fatal("error message injected an extra record line")
	}
}

func TestFormatFiltersAllUsefulFields(t *testing.T) {
	records := []appdata.InvoiceRecord{
		{OrderID: "A", OriginalOrderID: "MO-100", BuyerName: "第一公司", InvoiceState: appdata.InvoiceStateOpened, Amount: 1},
		{OrderID: "B", OriginalOrderID: "MO-200", BuyerName: "第二公司", InvoiceState: appdata.InvoiceStateOpened, Amount: 2},
	}
	text := Format(records, "第二公司")
	if strings.Contains(text, "MO-100") || !strings.Contains(text, "MO-200") || !strings.Contains(text, "顯示 1／共 2 筆") {
		t.Fatalf("unexpected filtered output:\n%s", text)
	}
}

func TestFormatMarksUploadErrorForOpenedInvoice(t *testing.T) {
	record := appdata.InvoiceRecord{OrderID: "A", InvoiceState: appdata.InvoiceStateOpened, UploadStatus: 91}
	text := Format([]appdata.InvoiceRecord{record}, "")
	if !strings.Contains(text, "【需確認｜發票已開立，但上傳發生錯誤】") || !strings.Contains(text, "上傳狀態：錯誤") {
		t.Fatalf("output:\n%s", text)
	}
}

func TestFormatDetailIncludesItemsAndKeepsUnsafeStateReadOnly(t *testing.T) {
	record := appdata.InvoiceRecord{
		OrderID: "ORDER-1", OriginalOrderID: "115102696774269",
		InvoiceState: appdata.InvoiceStateUnknown, Amount: 1234567,
		Items: []appdata.InvoiceItem{{
			Description: "測試商品", QuantityDecimal: "2.5",
			UnitPriceDecimal: "12345.67", AmountDecimal: "30864.175", TaxType: "應稅",
		}},
	}
	text := FormatDetail(record)
	for _, required := range []string{
		"開立結果不明，禁止重送", "訂單編號：115102696774269",
		"發票總額：1,234,567", "單價：12,345.67", "金額：30,864.175",
	} {
		if !strings.Contains(text, required) { t.Fatalf("missing %q in:\n%s", required, text) }
	}
}

func TestFormatDetailExplainsMissingLegacyItems(t *testing.T) {
	text := FormatDetail(appdata.InvoiceRecord{OrderID: "OLD-1", InvoiceState: appdata.InvoiceStateOpened})
	if !strings.Contains(text, "此舊紀錄未保存商品明細") { t.Fatalf("output:\n%s", text) }
}
