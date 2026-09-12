package moimport

import (
	"errors"
	"fmt"
	"math"
	"math/big"
	"strconv"
	"strings"
	"unicode/utf8"

	"cyinvoice/internal/appdata"
	"cyinvoice/internal/fixeddecimal"
)

const (
	CarrierMember = "會員載具"
	CarrierMobile = "手機條碼"
	CarrierCitizen = "自然人憑證"
)

type Order struct {
	OrderID       string
	InvoiceType   string
	Carrier       string
	CarrierID1    string
	CarrierID2    string
	NPOBAN        string
	BuyerBAN      string
	BuyerName     string
	BuyerAddress  string
	BuyerPhone    string
	BuyerEmail    string
	Items         []appdata.InvoiceItem
	MainRemark    string
	TotalAmount   int64
}

const (
	rawItemAmountHeader = "開立發票金額_依品項(若您是自行開立發票，請依此金額開立予消費者)"
	rawTotalAmountHeader = "開立發票金額加總(若您是自行開立發票，請依此金額開立予消費者)"
)

var convertedRequiredHeaders = []string{"訂單編號", "發票類型", "載具", "載具顯碼", "載具隱碼", "捐贈對象", "買方統編", "買方名稱", "買方地址", "買方電話", "買方電子信箱", "品名", "課稅別", "數量", "單價(含稅)", "小計金額(含稅)", "商品備註(最多40個字)", "總備註(最多200個字)"}

func ParseRows(rows [][]string) ([]Order, error) {
	if findHeaderRow(rows, "訂單編號", "商品名稱", rawItemAmountHeader) >= 0 {
		return parseOrderExportRows(rows)
	}
	return parseConvertedRows(rows)
}

func parseOrderExportRows(rows [][]string) ([]Order, error) {
	if len(rows) < 2 {
		return nil, errors.New("Excel 沒有可匯入的資料")
	}
	headerRow := findHeaderRow(rows, "訂單編號", "商品名稱", rawItemAmountHeader)
	if headerRow < 0 {
		return nil, errors.New("找不到 MO店+ OrderExport 標題列")
	}
	columns := headerMap(rows[headerRow])
	required := []string{
		"訂單編號", "商品名稱", "數量", "應稅(免稅)",
		"客人支付運費", "平台補貼運費", "商品滿額免運費",
		rawItemAmountHeader, rawTotalAmountHeader, "發票開立統編",
	}
	var missing []string
	for _, name := range required {
		if _, ok := columns[name]; !ok {
			missing = append(missing, name)
		}
	}
	if len(missing) > 0 {
		return nil, fmt.Errorf("MO店+ OrderExport 缺少欄位：%s", strings.Join(missing, "、"))
	}

	type rawOrderAmounts struct {
		officialTotal rawAmount
		shipping      rawAmount
		subsidy       rawAmount
		freeShipping  rawAmount
	}
	orders := make([]Order, 0)
	amounts := make([]rawOrderAmounts, 0)
	orderIndexes := map[string]int{}
	currentOrderID := ""
	for rowIndex := headerRow + 1; rowIndex < len(rows); rowIndex++ {
		row := rows[rowIndex]
		if rowBlank(row) {
			continue
		}
		orderID := value(row, columns, "訂單編號")
		if orderID == "" {
			orderID = currentOrderID
		} else {
			currentOrderID = orderID
		}
		if orderID == "" {
			return nil, fmt.Errorf("第 %d 列沒有訂單編號，也沒有可承接的上一筆訂單", rowIndex+1)
		}
		index, found := orderIndexes[orderID]
		if !found {
			buyerBAN := value(row, columns, "發票開立統編")
			order := Order{
				OrderID: orderID,
				BuyerBAN: buyerBAN,
				BuyerName: value(row, columns, "收件人姓名"),
			}
			if buyerBAN != "" {
				order.InvoiceType = "B2B"
				order.Carrier = "公司戶"
			} else {
				order.InvoiceType = "B2C"
				order.Carrier = CarrierMember
				order.CarrierID1 = "motmp_" + orderID
				order.CarrierID2 = "motmp_" + orderID
			}
			orders = append(orders, order)
			amounts = append(amounts, rawOrderAmounts{})
			index = len(orders) - 1
			orderIndexes[orderID] = index
		}
		if buyerBAN := value(row, columns, "發票開立統編"); buyerBAN != "" {
			if orders[index].BuyerBAN != "" && orders[index].BuyerBAN != buyerBAN {
				return nil, fmt.Errorf("訂單 %s 的發票開立統編不一致", orderID)
			}
			orders[index].BuyerBAN = buyerBAN
			orders[index].InvoiceType = "B2B"
			orders[index].Carrier = "公司戶"
			orders[index].CarrierID1 = ""
			orders[index].CarrierID2 = ""
		}
		if orders[index].BuyerName == "" {
			orders[index].BuyerName = value(row, columns, "收件人姓名")
		}
		if tax := value(row, columns, "應稅(免稅)"); tax != "" && tax != "應稅" && tax != "1" {
			return nil, fmt.Errorf("第 %d 列不是應稅商品，目前不可匯入", rowIndex+1)
		}
		productName := value(row, columns, "商品名稱")
		if productName == "" {
			return nil, fmt.Errorf("第 %d 列缺少商品名稱", rowIndex+1)
		}
		descriptionParts := []string{productName}
		for _, header := range []string{"規格1", "規格2"} {
			if spec := value(row, columns, header); spec != "" {
				descriptionParts = append(descriptionParts, spec)
			}
		}
		description := strings.Join(descriptionParts, " ")
		quantityText := cleanNumber(value(row, columns, "數量"))
		itemAmount, err := parseInt(value(row, columns, rawItemAmountHeader))
		if err != nil {
			return nil, fmt.Errorf("第 %d 列官方開立發票金額（依品項）格式錯誤", rowIndex+1)
		}
		item, err := officialInvoiceItem(description, quantityText, itemAmount)
		if err != nil {
			return nil, fmt.Errorf("第 %d 列：%w", rowIndex+1, err)
		}
		orders[index].Items = append(orders[index].Items, item)

		if err := amounts[index].officialTotal.capture(value(row, columns, rawTotalAmountHeader), "官方開立發票金額加總"); err != nil {
			return nil, fmt.Errorf("訂單 %s：%w", orderID, err)
		}
		if err := amounts[index].shipping.capture(value(row, columns, "客人支付運費"), "客人支付運費"); err != nil {
			return nil, fmt.Errorf("訂單 %s：%w", orderID, err)
		}
		if err := amounts[index].subsidy.capture(value(row, columns, "平台補貼運費"), "平台補貼運費"); err != nil {
			return nil, fmt.Errorf("訂單 %s：%w", orderID, err)
		}
		if err := amounts[index].freeShipping.capture(value(row, columns, "商品滿額免運費"), "商品滿額免運費"); err != nil {
			return nil, fmt.Errorf("訂單 %s：%w", orderID, err)
		}
	}

	if len(orders) == 0 {
		return nil, errors.New("Excel 沒有可匯入的訂單")
	}
	for index := range orders {
		order := &orders[index]
		source := &amounts[index]
		if !source.officialTotal.seen {
			return nil, fmt.Errorf("訂單 %s 缺少官方開立發票金額加總", order.OrderID)
		}
		appendAmountItem(order, "運費", source.shipping)
		appendAmountItem(order, "滿額免運費", source.freeShipping)
		detailTotal := itemTotal(order.Items)
		if detailTotal != source.officialTotal.value && source.subsidy.seen && source.subsidy.value != 0 &&
			detailTotal+source.subsidy.value == source.officialTotal.value {
			appendAmountItem(order, "運費補貼", source.subsidy)
			detailTotal = itemTotal(order.Items)
		}
		if detailTotal != source.officialTotal.value {
			return nil, fmt.Errorf(
				"訂單 %s 的官方欄位互相不一致：明細及運費合計 %d，官方開立發票金額加總 %d",
				order.OrderID, detailTotal, source.officialTotal.value,
			)
		}
		order.TotalAmount = source.officialTotal.value
	}
	if err := validateOrders(orders, true); err != nil {
		return nil, err
	}
	return orders, nil
}

type rawAmount struct {
	seen  bool
	value int64
}

func (amount *rawAmount) capture(text, label string) error {
	text = strings.TrimSpace(text)
	if text == "" {
		return nil
	}
	value, err := parseInt(text)
	if err != nil {
		return fmt.Errorf("%s格式錯誤", label)
	}
	if amount.seen && amount.value != value {
		return fmt.Errorf("%s在同一訂單內不一致", label)
	}
	amount.seen = true
	amount.value = value
	return nil
}

func appendAmountItem(order *Order, description string, amount rawAmount) {
	if !amount.seen || amount.value == 0 {
		return
	}
	order.Items = append(order.Items, appdata.InvoiceItem{
		Description: description,
		Quantity: 1, QuantityDecimal: "1",
		UnitPrice: amount.value, UnitPriceDecimal: strconv.FormatInt(amount.value, 10),
		Amount: amount.value, AmountDecimal: strconv.FormatInt(amount.value, 10),
		TaxType: "1",
	})
}

func itemTotal(items []appdata.InvoiceItem) int64 {
	var total int64
	for _, item := range items {
		total += item.Amount
	}
	return total
}

func officialInvoiceItem(description, quantityText string, amount int64) (appdata.InvoiceItem, error) {
	quantity, err := fixeddecimal.Parse(quantityText)
	if err != nil || quantity <= 0 {
		return appdata.InvoiceItem{}, errors.New("數量格式錯誤")
	}
	amountValue, err := fixeddecimal.FromInt64(amount)
	if err != nil {
		return appdata.InvoiceItem{}, errors.New("官方開立發票金額超出範圍")
	}
	numerator := new(big.Int).Mul(big.NewInt(int64(amountValue)), big.NewInt(fixeddecimal.Scale))
	unitPrice, err := roundedBigQuotient(numerator, big.NewInt(int64(quantity)))
	if err != nil {
		return appdata.InvoiceItem{}, err
	}
	expected, err := fixeddecimal.Multiply(quantity, unitPrice)
	if err != nil {
		return appdata.InvoiceItem{}, errors.New("官方開立發票單價換算超出範圍")
	}
	return appdata.InvoiceItem{
		Description: description,
		Quantity: fixeddecimal.Float64(quantity), QuantityDecimal: fixeddecimal.Format(quantity),
		UnitPrice: fixeddecimal.RoundInt64(unitPrice), UnitPriceDecimal: fixeddecimal.Format(unitPrice),
		Amount: amount, AmountDecimal: strconv.FormatInt(amount, 10),
		TaxType: "1",
		AllowSubtotalRounding: expected != amountValue,
	}, nil
}

func roundedBigQuotient(numerator, denominator *big.Int) (fixeddecimal.Value, error) {
	if denominator.Sign() == 0 {
		return 0, errors.New("數量不可為零")
	}
	negative := numerator.Sign()*denominator.Sign() < 0
	absoluteNumerator := new(big.Int).Abs(new(big.Int).Set(numerator))
	absoluteDenominator := new(big.Int).Abs(new(big.Int).Set(denominator))
	quotient, remainder := new(big.Int), new(big.Int)
	quotient.QuoRem(absoluteNumerator, absoluteDenominator, remainder)
	if new(big.Int).Lsh(remainder, 1).Cmp(absoluteDenominator) >= 0 {
		quotient.Add(quotient, big.NewInt(1))
	}
	if negative {
		quotient.Neg(quotient)
	}
	if !quotient.IsInt64() {
		return 0, errors.New("官方開立發票單價換算超出範圍")
	}
	return fixeddecimal.Value(quotient.Int64()), nil
}

func findHeaderRow(rows [][]string, names ...string) int {
	for index, row := range rows {
		columns := headerMap(row)
		found := true
		for _, name := range names {
			if _, ok := columns[name]; !ok {
				found = false
				break
			}
		}
		if found {
			return index
		}
	}
	return -1
}

func parseConvertedRows(rows [][]string) ([]Order, error) {
	if len(rows) < 2 { return nil, errors.New("Excel 沒有可匯入的資料") }
	headerRow := -1
	columns := map[string]int{}
	for index, row := range rows {
		candidate := headerMap(row)
		if _, ok := candidate["訂單編號"]; ok {
			if _, ok := candidate["品名"]; ok { headerRow, columns = index, candidate; break }
		}
	}
	if headerRow < 0 { return nil, errors.New("找不到 MO店+ 標題列（訂單編號、品名）") }
	var missing []string
	for _, name := range convertedRequiredHeaders { if _, ok := columns[name]; !ok { missing = append(missing, name) } }
	if len(missing) > 0 { return nil, fmt.Errorf("MO店+ Excel 缺少欄位：%s", strings.Join(missing, "、")) }

	orders := make([]Order, 0)
	orderIndexes := map[string]int{}
	currentOrderID := ""
	for rowIndex := headerRow + 1; rowIndex < len(rows); rowIndex++ {
		row := rows[rowIndex]
		if rowBlank(row) { continue }
		orderID := value(row, columns, "訂單編號")
		if orderID == "" { orderID = currentOrderID } else { currentOrderID = orderID }
		if orderID == "" { return nil, fmt.Errorf("第 %d 列沒有訂單編號，也沒有可承接的上一筆訂單", rowIndex+1) }
		index, found := orderIndexes[orderID]
		if !found {
			order := Order{
				OrderID: orderID, InvoiceType: value(row, columns, "發票類型"), Carrier: value(row, columns, "載具"),
				CarrierID1: value(row, columns, "載具顯碼"), CarrierID2: value(row, columns, "載具隱碼"), NPOBAN: value(row, columns, "捐贈對象"),
				BuyerBAN: value(row, columns, "買方統編"), BuyerName: value(row, columns, "買方名稱"), BuyerAddress: value(row, columns, "買方地址"),
				BuyerPhone: value(row, columns, "買方電話"), BuyerEmail: value(row, columns, "買方電子信箱"), MainRemark: value(row, columns, "總備註(最多200個字)"),
			}
			orders = append(orders, order)
			index = len(orders)-1
			orderIndexes[orderID] = index
		}
		description := value(row, columns, "品名")
		if description == "" { return nil, fmt.Errorf("第 %d 列缺少品名", rowIndex+1) }
		quantity, err := parseFloat(value(row, columns, "數量"))
		if err != nil || quantity <= 0 { return nil, fmt.Errorf("第 %d 列數量格式錯誤", rowIndex+1) }
		unitPrice, err := parseInt(value(row, columns, "單價(含稅)"))
		if err != nil { return nil, fmt.Errorf("第 %d 列含稅單價格式錯誤", rowIndex+1) }
		amount, err := parseInt(value(row, columns, "小計金額(含稅)"))
		if err != nil { return nil, fmt.Errorf("第 %d 列小計金額格式錯誤", rowIndex+1) }
		expected := int64(math.Round(quantity * float64(unitPrice)))
		if amount != expected { return nil, fmt.Errorf("第 %d 列小計不一致：應為 %d，Excel 為 %d", rowIndex+1, expected, amount) }
		if tax := value(row, columns, "課稅別"); tax != "" && tax != "應稅" && tax != "1" { return nil, fmt.Errorf("第 %d 列不是應稅商品，目前不可匯入", rowIndex+1) }
		remark := value(row, columns, "商品備註(最多40個字)")
		if utf8.RuneCountInString(remark) > 40 { return nil, fmt.Errorf("第 %d 列商品備註超過 40 字", rowIndex+1) }
		orders[index].Items = append(orders[index].Items, appdata.InvoiceItem{Description: description, Quantity: quantity, UnitPrice: unitPrice, Amount: amount, TaxType: "1", Remark: remark})
		orders[index].TotalAmount += amount
	}
	if len(orders) == 0 { return nil, errors.New("Excel 沒有可匯入的訂單") }
	if err := validateOrders(orders, true); err != nil {
		return nil, err
	}
	return orders, nil
}

func validateOrders(orders []Order, allowBuyerNameLookup bool) error {
	for index := range orders {
		order := &orders[index]
		if len(order.Items) > appdata.MaxInvoiceItems {
			return fmt.Errorf("訂單 %s 超過 %d 筆商品", order.OrderID, appdata.MaxInvoiceItems)
		}
		company := order.BuyerBAN != "" && order.BuyerBAN != "0000000000"
		buyerName := order.BuyerName
		if company && allowBuyerNameLookup && strings.TrimSpace(buyerName) == "" {
			buyerName = "待由光貿查詢"
		}
		draft := appdata.InvoiceDraft{
			OrderID: order.OrderID, CompanyBuyer: company,
			BuyerIdentifier: order.BuyerBAN, BuyerName: buyerName,
			Items: order.Items, TotalAmount: order.TotalAmount, MainRemark: order.MainRemark,
		}
		if !company {
			draft.BuyerIdentifier, draft.BuyerName = "", ""
		}
		if err := draft.Validate(); err != nil {
			return fmt.Errorf("訂單 %s：%w", order.OrderID, err)
		}
	}
	return nil
}

func headerMap(row []string) map[string]int {
	result := make(map[string]int, len(row))
	for index, item := range row { result[strings.TrimSpace(strings.TrimPrefix(item, "\ufeff"))] = index }
	return result
}
func value(row []string, columns map[string]int, name string) string { index := columns[name]; if index >= len(row) { return "" }; return strings.TrimSpace(row[index]) }
func rowBlank(row []string) bool { for _, value := range row { if strings.TrimSpace(value) != "" { return false } }; return true }
func parseFloat(value string) (float64, error) { return strconv.ParseFloat(cleanNumber(value), 64) }
func parseInt(value string) (int64, error) { number, err := strconv.ParseFloat(cleanNumber(value), 64); if err != nil { return 0, err }; if math.Trunc(number) != number { return 0, errors.New("not an integer") }; return int64(number), nil }
func cleanNumber(value string) string { return strings.ReplaceAll(strings.TrimSpace(value), ",", "") }
