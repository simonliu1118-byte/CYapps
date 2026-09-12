package appdata

import (
	"errors"
	"strings"
	"testing"
)

func validDraft() InvoiceDraft {
	return InvoiceDraft{
		OrderID:     " 20260905001 ",
		Items:       []InvoiceItem{{Description: "商品", Quantity: 2, UnitPrice: 50, Amount: 100}},
		TotalAmount: 100,
	}
}

func TestInvoiceDraftValidation(t *testing.T) {
	draft := validDraft()
	if err := draft.Validate(); err != nil {
		t.Fatal(err)
	}
	if draft.OrderID != "20260905001" {
		t.Fatalf("OrderID was not normalized: %q", draft.OrderID)
	}
}

func TestInvoiceDraftValidationRejectsBadTotals(t *testing.T) {
	draft := validDraft()
	draft.TotalAmount = 99
	if err := draft.Validate(); err == nil || !strings.Contains(err.Error(), "金額不一致") {
		t.Fatalf("error = %v", err)
	}

	draft = validDraft()
	draft.Items[0].Amount = 99
	if err := draft.Validate(); err == nil || !strings.Contains(err.Error(), "商品金額不一致") {
		t.Fatalf("error = %v", err)
	}
}

func TestInvoiceDraftValidationRejectsZeroAndNegativeAmounts(t *testing.T) {
	for _, amount := range []int64{0, -1} {
		if err := ValidateAmount(amount); !errors.Is(err, ErrAmountMustPositive) {
			t.Fatalf("ValidateAmount(%d) = %v", amount, err)
		}
	}
}

func TestInvoiceDraftValidationLimits(t *testing.T) {
	draft := validDraft()
	draft.Items = make([]InvoiceItem, MaxInvoiceItems+1)
	if err := draft.Validate(); err == nil {
		t.Fatal("more than 50 items were accepted")
	}

	draft = validDraft()
	draft.MainRemark = strings.Repeat("字", MaxRemarkRunes+1)
	if err := draft.Validate(); err == nil {
		t.Fatal("remark longer than 200 runes was accepted")
	}
}

func TestCompanyBuyerValidation(t *testing.T) {
	draft := validDraft()
	draft.CompanyBuyer = true
	draft.BuyerIdentifier = "123"
	if err := draft.Validate(); err == nil {
		t.Fatal("invalid company identifier was accepted")
	}
	draft.BuyerIdentifier = "12345675"
	if err := draft.Validate(); err == nil {
		t.Fatal("empty company name was accepted")
	}
	draft.BuyerName = "測試公司"
	if err := draft.Validate(); err != nil {
		t.Fatal(err)
	}
}

func TestUntaxedCompanyDraftAddsFivePercentTax(t *testing.T) {
	draft := validDraft()
	draft.CompanyBuyer = true
	draft.BuyerIdentifier = "12345678"
	draft.BuyerName = "測試公司"
	draft.PricesExcludeTax = true
	draft.Items[0].UnitPrice = 100
	draft.Items[0].Amount = 200
	draft.TotalAmount = 210
	if err := draft.Validate(); err != nil {
		t.Fatal(err)
	}
}

func TestUntaxedDraftRequiresCompanyBuyer(t *testing.T) {
	draft := validDraft()
	draft.PricesExcludeTax = true
	draft.TotalAmount = 105
	if err := draft.Validate(); err == nil {
		t.Fatal("expected untaxed B2C draft to fail")
	}
}

func TestSevenDecimalUntaxedValueReturnsExactlyToTwoHundred(t *testing.T) {
	draft := validDraft()
	draft.CompanyBuyer = true
	draft.BuyerIdentifier = "12345678"
	draft.BuyerName = "測試公司"
	draft.PricesExcludeTax = true
	draft.Items = []InvoiceItem{{
		Description: "商品",
		Quantity: 1, QuantityDecimal: "1",
		UnitPrice: 190, UnitPriceDecimal: "190.4761905",
		Amount: 190, AmountDecimal: "190.4761905",
	}}
	draft.TotalAmount = 200
	if err := draft.Validate(); err != nil {
		t.Fatal(err)
	}
	sales, tax, total, err := CalculateInvoiceTotals(draft.Items, true, true)
	if err != nil || sales != 190 || tax != 10 || total != 200 {
		t.Fatalf("totals = sales %d tax %d total %d err %v", sales, tax, total, err)
	}
}

func TestImportedSubtotalRoundingIsAcceptedOnlyWhenExplicitlyMarked(t *testing.T) {
	draft := InvoiceDraft{
		OrderID: "IMPORT-001",
		Items: []InvoiceItem{{
			Description: "商品", Quantity: 3, QuantityDecimal: "3",
			UnitPrice: 33, UnitPriceDecimal: "33.3333333",
			Amount: 100, AmountDecimal: "100", AllowSubtotalRounding: true,
		}},
		TotalAmount: 100,
	}
	if err := draft.Validate(); err != nil { t.Fatal(err) }
	draft.Items[0].AllowSubtotalRounding = false
	if err := draft.Validate(); err == nil {
		t.Fatal("unmarked subtotal mismatch was accepted")
	}
}
