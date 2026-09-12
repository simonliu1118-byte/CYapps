package coupangimport

import (
	"errors"
	"fmt"
	"strings"

	"cyinvoice/internal/appdata"
	"cyinvoice/internal/fixeddecimal"
)

const DeliveryPaper = "紙本"

type Order struct {
	OrderID      string
	BuyerBAN     string
	BuyerName    string
	Items        []appdata.InvoiceItem
	TotalAmount  int64
}

var requiredHeaders = []string{
	"訂單編號",
	"顯示產品名稱",
	"數量",
	"訂購人姓名",
	"應開立予買家之發票金額",
	"應開立予酷澎之發票金額",
	"統一編號",
}

func ParseRows(rows [][]string) ([]Order, error) {
	if len(rows) < 2 {
		return nil, errors.New("Excel 沒有可匯入的酷澎資料")
	}
	headerRow := -1
	columns := map[string]int{}
	for index, row := range rows {
		candidate := headerMap(row)
		if _, hasOrderID := candidate["訂單編號"]; hasOrderID {
			if _, hasAmount := candidate["應開立予買家之發票金額"]; hasAmount {
				headerRow, columns = index, candidate
				break
			}
		}
	}
	if headerRow < 0 {
		return nil, errors.New("找不到酷澎標題列（訂單編號、應開立予買家之發票金額）")
	}
	var missing []string
	for _, name := range requiredHeaders {
		if _, ok := columns[name]; !ok {
			missing = append(missing, name)
		}
	}
	if len(missing) > 0 {
		return nil, fmt.Errorf("酷澎 Excel 缺少欄位：%s", strings.Join(missing, "、"))
	}

	orders := make([]Order, 0)
	orderIndexes := map[string]int{}
	for rowIndex := headerRow + 1; rowIndex < len(rows); rowIndex++ {
		row := rows[rowIndex]
		if rowBlank(row) {
			continue
		}
		orderID := value(row, columns, "訂單編號")
		if orderID == "" {
			return nil, fmt.Errorf("第 %d 列沒有訂單編號", rowIndex+1)
		}
		quantityText := cleanNumber(value(row, columns, "數量"))
		quantityDecimal, err := fixeddecimal.Parse(quantityText)
		quantity := fixeddecimal.Float64(quantityDecimal)
		if err != nil || quantityDecimal <= 0 {
			return nil, fmt.Errorf("第 %d 列數量格式錯誤", rowIndex+1)
		}
		invoiceAmount, err := parseInteger(value(row, columns, "應開立予買家之發票金額"))
		if err != nil || invoiceAmount <= 0 {
			return nil, fmt.Errorf("第 %d 列應開立予買家之發票金額格式錯誤", rowIndex+1)
		}
		amountDecimal, _ := fixeddecimal.FromInt64(invoiceAmount)
		unitPriceDecimal, divideErr := fixeddecimal.Divide(amountDecimal, quantityDecimal)
		if divideErr != nil { return nil, fmt.Errorf("第 %d 列無法計算固定小數單價：%w", rowIndex+1, divideErr) }
		description := value(row, columns, "顯示產品名稱")
		if description == "" {
			return nil, fmt.Errorf("第 %d 列缺少顯示產品名稱", rowIndex+1)
		}
		buyerBAN := normalizeBAN(value(row, columns, "統一編號"))
		buyerName := value(row, columns, "訂購人姓名")
		index, found := orderIndexes[orderID]
		if !found {
			orders = append(orders, Order{OrderID: orderID, BuyerBAN: buyerBAN, BuyerName: buyerName})
			index = len(orders) - 1
			orderIndexes[orderID] = index
		} else if orders[index].BuyerBAN != buyerBAN || orders[index].BuyerName != buyerName {
			return nil, fmt.Errorf("訂單 %s 的買方資料在不同列不一致", orderID)
		}
		item := appdata.InvoiceItem{
			Description: description,
			Quantity:    quantity,
			QuantityDecimal: fixeddecimal.Format(quantityDecimal),
			UnitPrice:   fixeddecimal.RoundInt64(unitPriceDecimal),
			UnitPriceDecimal: fixeddecimal.Format(unitPriceDecimal),
			TaxType:     "1",
			Amount:      invoiceAmount,
			AmountDecimal: fixeddecimal.Format(amountDecimal),
		}
		expected, multiplyErr := fixeddecimal.Multiply(quantityDecimal, unitPriceDecimal)
		item.AllowSubtotalRounding = multiplyErr == nil && expected != amountDecimal
		orders[index].Items = append(orders[index].Items, item)
		orders[index].TotalAmount += invoiceAmount
	}
	if len(orders) == 0 {
		return nil, errors.New("Excel 沒有可匯入的酷澎訂單")
	}
	for index := range orders {
		order := &orders[index]
		if len(order.Items) > appdata.MaxInvoiceItems {
			return nil, fmt.Errorf("訂單 %s 超過 %d 筆商品", order.OrderID, appdata.MaxInvoiceItems)
		}
		company := order.BuyerBAN != ""
		draft := appdata.InvoiceDraft{
			OrderID: order.OrderID, CompanyBuyer: company,
			BuyerIdentifier: order.BuyerBAN, BuyerName: order.BuyerName,
			Items: order.Items, TotalAmount: order.TotalAmount,
		}
		if !company {
			draft.BuyerName = ""
		}
		if err := draft.Validate(); err != nil {
			return nil, fmt.Errorf("訂單 %s：%w", order.OrderID, err)
		}
	}
	return orders, nil
}

func headerMap(row []string) map[string]int {
	result := make(map[string]int, len(row))
	for index, item := range row {
		result[strings.TrimSpace(strings.TrimPrefix(item, "\ufeff"))] = index
	}
	return result
}

func value(row []string, columns map[string]int, name string) string {
	index := columns[name]
	if index >= len(row) {
		return ""
	}
	return strings.TrimSpace(row[index])
}

func rowBlank(row []string) bool {
	for _, item := range row {
		if strings.TrimSpace(item) != "" {
			return false
		}
	}
	return true
}

func cleanNumber(value string) string {
	return strings.ReplaceAll(strings.TrimSpace(value), ",", "")
}

func parseInteger(value string) (int64, error) {
	number, err := fixeddecimal.Parse(cleanNumber(value))
	if err != nil { return 0, errors.New("invalid integer") }
	integer := fixeddecimal.RoundInt64(number)
	converted, conversionErr := fixeddecimal.FromInt64(integer)
	if conversionErr != nil || converted != number { return 0, errors.New("invalid integer") }
	return integer, nil
}

func normalizeBAN(value string) string {
	value = strings.TrimSpace(value)
	if value == "0000000000" {
		return ""
	}
	return value
}
