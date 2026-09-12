package appdata

import (
	"errors"
	"fmt"
	"strconv"
	"strings"
	"unicode"
	"unicode/utf8"

	"cyinvoice/internal/fixeddecimal"
)

const (
	MaxInvoiceItems = 50
	MaxRemarkRunes  = 200
)

var (
	ErrOrderIDRequired    = errors.New("請輸入訂單編號")
	ErrOrderIDInvalid     = errors.New("訂單編號含有不可使用的控制字元")
	ErrAmountMustPositive = errors.New("0元不開立發票")
)

type InvoiceDraft struct {
	OrderID         string
	CompanyBuyer    bool
	BuyerIdentifier string
	BuyerName       string
	Items           []InvoiceItem
	TotalAmount     int64
	MainRemark      string
	PricesExcludeTax bool
}

func NormalizeOrderID(value string) (string, error) {
	value = strings.TrimSpace(value)
	if value == "" {
		return "", ErrOrderIDRequired
	}
	if !utf8.ValidString(value) {
		return "", ErrOrderIDInvalid
	}
	for _, char := range value {
		if unicode.IsControl(char) {
			return "", ErrOrderIDInvalid
		}
	}
	return value, nil
}

func ValidateAmount(amount int64) error {
	if amount <= 0 {
		return ErrAmountMustPositive
	}
	return nil
}

func (draft *InvoiceDraft) Validate() error {
	orderID, err := NormalizeOrderID(draft.OrderID)
	if err != nil {
		return err
	}
	draft.OrderID = orderID

	if draft.CompanyBuyer {
		if !validBAN(draft.BuyerIdentifier) {
			return errors.New("公司統編必須為 8 碼")
		}
		if strings.TrimSpace(draft.BuyerName) == "" {
			return errors.New("請輸入買方名稱")
		}
	}
	if len(draft.Items) == 0 {
		return errors.New("請至少輸入一筆商品")
	}
	if len(draft.Items) > MaxInvoiceItems {
		return fmt.Errorf("商品明細最多 %d 筆", MaxInvoiceItems)
	}
	if utf8.RuneCountInString(draft.MainRemark) > MaxRemarkRunes {
		return fmt.Errorf("發票總備註最多 %d 字", MaxRemarkRunes)
	}

	for index := range draft.Items {
		item := &draft.Items[index]
		item.Description = strings.TrimSpace(item.Description)
		quantity, unitPrice, amount, decimalErr := InvoiceItemDecimals(*item)
		if item.Description == "" || decimalErr != nil || quantity <= 0 {
			return fmt.Errorf("第 %d 筆商品明細的品名與數量不可空白", index+1)
		}
		expected, multiplyErr := fixeddecimal.Multiply(quantity, unitPrice)
		if multiplyErr != nil {
			return fmt.Errorf("第 %d 筆商品金額超出範圍", index+1)
		}
		if amount != expected {
			roundedMatch := item.AllowSubtotalRounding &&
				fixeddecimal.RoundInt64(expected) == item.Amount &&
				fixeddecimal.RoundInt64(amount) == item.Amount
			if roundedMatch {
				continue
			}
			return fmt.Errorf("第 %d 筆商品金額不一致：應為 %s，實際為 %s", index+1, fixeddecimal.Format(expected), fixeddecimal.Format(amount))
		}
		if item.Amount != fixeddecimal.RoundInt64(amount) {
			return fmt.Errorf("第 %d 筆商品整數金額與 7 位小數資料不一致", index+1)
		}
	}
	_, _, calculated, err := CalculateInvoiceTotals(draft.Items, draft.CompanyBuyer, draft.PricesExcludeTax)
	if err != nil {
		return err
	}
	if err := ValidateAmount(draft.TotalAmount); err != nil {
		return err
	}
	if calculated != draft.TotalAmount {
		return fmt.Errorf("發票金額不一致：明細加總 %d，發票總額 %d", calculated, draft.TotalAmount)
	}
	return nil
}

func InvoiceItemDecimals(item InvoiceItem) (fixeddecimal.Value, fixeddecimal.Value, fixeddecimal.Value, error) {
	quantityText := strings.TrimSpace(item.QuantityDecimal)
	if quantityText == "" {
		quantityText = strconv.FormatFloat(item.Quantity, 'f', 7, 64)
	}
	quantity, err := fixeddecimal.Parse(quantityText)
	if err != nil {
		return 0, 0, 0, err
	}
	unitPrice, err := decimalOrInteger(item.UnitPriceDecimal, item.UnitPrice)
	if err != nil {
		return 0, 0, 0, err
	}
	amount, err := decimalOrInteger(item.AmountDecimal, item.Amount)
	if err != nil {
		return 0, 0, 0, err
	}
	return quantity, unitPrice, amount, nil
}

func CalculateInvoiceTotals(items []InvoiceItem, companyBuyer, pricesExcludeTax bool) (int64, int64, int64, error) {
	if pricesExcludeTax && !companyBuyer {
		return 0, 0, 0, errors.New("未稅輸入只適用於公司統編發票")
	}
	subtotal := fixeddecimal.Value(0)
	for _, item := range items {
		_, _, amount, err := InvoiceItemDecimals(item)
		if err != nil {
			return 0, 0, 0, err
		}
		subtotal, err = fixeddecimal.Add(subtotal, amount)
		if err != nil {
			return 0, 0, 0, errors.New("發票總額超出範圍")
		}
	}
	if pricesExcludeTax {
		sales := fixeddecimal.RoundInt64(subtotal)
		salesDecimal, err := fixeddecimal.FromInt64(sales)
		if err != nil {
			return 0, 0, 0, err
		}
		taxDecimal, err := fixeddecimal.MultiplyRatio(salesDecimal, 1, 20)
		if err != nil {
			return 0, 0, 0, err
		}
		tax := fixeddecimal.RoundInt64(taxDecimal)
		return sales, tax, sales + tax, nil
	}
	total := fixeddecimal.RoundInt64(subtotal)
	totalDecimal, err := fixeddecimal.FromInt64(total)
	if err != nil {
		return 0, 0, 0, err
	}
	salesDecimal, err := fixeddecimal.MultiplyRatio(totalDecimal, 20, 21)
	if err != nil {
		return 0, 0, 0, err
	}
	sales := fixeddecimal.RoundInt64(salesDecimal)
	return sales, total - sales, total, nil
}

func decimalOrInteger(decimal string, integer int64) (fixeddecimal.Value, error) {
	if strings.TrimSpace(decimal) != "" {
		return fixeddecimal.Parse(decimal)
	}
	return fixeddecimal.FromInt64(integer)
}

func validBAN(value string) bool {
	if len(value) != 8 {
		return false
	}
	for _, char := range value {
		if char < '0' || char > '9' {
			return false
		}
	}
	return true
}
