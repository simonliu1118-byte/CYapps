package recordview

import (
	"fmt"
	"strconv"
	"strings"

	"cyinvoice/internal/appdata"
	"cyinvoice/internal/displayfmt"
)

type counts struct {
	opened, pending, attention, failed, voided int
}

// Format renders records for the read-only Windows record pane. It deliberately
// uses labeled lines instead of tab alignment because the system UI font is not
// monospaced and long buyer/order names otherwise shift every column.
func Format(records []appdata.InvoiceRecord, query string) string {
	query = strings.ToLower(strings.TrimSpace(query))
	matched := make([]appdata.InvoiceRecord, 0, len(records))
	var summary counts
	for index := len(records) - 1; index >= 0; index-- {
		record := records[index]
		if query != "" && !strings.Contains(searchText(record), query) {
			continue
		}
		matched = append(matched, record)
		addCount(&summary, record)
	}
	if len(matched) == 0 {
		if query == "" {
			return "目前沒有發票紀錄。"
		}
		return "沒有符合查詢條件的紀錄。"
	}

	var text strings.Builder
	fmt.Fprintf(&text, "顯示 %d／共 %d 筆　已開立 %d　處理中 %d　需確認 %d　失敗 %d　作廢 %d\r\n\r\n",
		len(matched), len(records), summary.opened, summary.pending, summary.attention, summary.failed, summary.voided)
	for index, record := range matched {
		category, detail := displayState(record)
		fmt.Fprintf(&text, "【%s｜%s】\r\n", category, detail)
		fmt.Fprintf(&text, "送出：%s　　發票：%s\r\n", shown(record.SentAt, "尚無時間"), shown(record.InvoiceNumber, "尚未取得"))
		fmt.Fprintf(&text, "來源：%s　　原始訂單：%s　　環境：%s\r\n", shown(record.Source, "未記錄"), shown(record.OriginalOrderID, record.OrderID), environmentText(record.Environment))
		fmt.Fprintf(&text, "買受人：%s　　統編：%s　　金額：$%s　　交付：%s\r\n",
			shown(record.BuyerName, "一般消費者"), shown(record.BuyerIdentifier, "無"), displayfmt.Integer(record.Amount), shown(record.Delivery, "未記錄"))
		fmt.Fprintf(&text, "發票狀態：%s　　上傳狀態：%s　　最後查詢：%s\r\n",
			shown(record.InvoiceState, "未記錄"), uploadText(record), shown(record.LastChecked, "尚未查詢"))
		if message := cleanLine(record.ErrorMessage); message != "" {
			fmt.Fprintf(&text, "原因：%s\r\n", message)
		}
		if index != len(matched)-1 {
			text.WriteString("────────────────────────────────────────\r\n")
		}
	}
	return text.String()
}

// FormatDetail renders one local invoice record for the read-only native
// detail window. It never queries AMEGO or changes the stored record.
func FormatDetail(record appdata.InvoiceRecord) string {
	category, safety := displayState(record)
	var text strings.Builder
	fmt.Fprintf(&text, "發票狀態：%s（%s）\r\n", category, safety)
	fmt.Fprintf(&text, "發票號碼：%s\r\n", shown(record.InvoiceNumber, "尚未取得"))
	issuedAt := strings.TrimSpace(strings.TrimSpace(record.InvoiceDate) + " " + strings.TrimSpace(record.InvoiceTime))
	fmt.Fprintf(&text, "開立時間：%s\r\n", shown(issuedAt, shown(record.SentAt, "尚無時間")))
	fmt.Fprintf(&text, "來源：%s\r\n", shown(record.Source, "未記錄"))
	fmt.Fprintf(&text, "訂單編號：%s\r\n", shown(record.OriginalOrderID, record.OrderID))
	if apiOrderID := cleanLine(record.APIOrderID); apiOrderID != "" && apiOrderID != cleanLine(record.OrderID) {
		fmt.Fprintf(&text, "API 訂單編號：%s\r\n", apiOrderID)
	}
	fmt.Fprintf(&text, "使用環境：%s\r\n", environmentText(record.Environment))
	fmt.Fprintf(&text, "買受人：%s\r\n", shown(record.BuyerName, "一般消費者"))
	fmt.Fprintf(&text, "統一編號：%s\r\n", shown(record.BuyerIdentifier, "無"))
	fmt.Fprintf(&text, "發票總額：%s\r\n", displayfmt.Integer(record.Amount))
	fmt.Fprintf(&text, "交付方式：%s\r\n", shown(record.Delivery, "未記錄"))
	fmt.Fprintf(&text, "上傳狀態：%s\r\n", uploadText(record))
	fmt.Fprintf(&text, "最後查詢：%s\r\n", shown(record.LastChecked, "尚未查詢"))
	if message := cleanLine(record.ErrorMessage); message != "" {
		fmt.Fprintf(&text, "原因：%s\r\n", message)
	}
	if remark := cleanLine(record.MainRemark); remark != "" {
		fmt.Fprintf(&text, "發票備註：%s\r\n", remark)
	}

	text.WriteString("\r\n商品明細\r\n")
	if len(record.Items) == 0 {
		text.WriteString("此舊紀錄未保存商品明細。\r\n")
		return text.String()
	}
	for index, item := range record.Items {
		quantity := strings.TrimSpace(item.QuantityDecimal)
		if quantity == "" { quantity = strconv.FormatFloat(item.Quantity, 'f', -1, 64) }
		unitPrice := strings.TrimSpace(item.UnitPriceDecimal)
		if unitPrice == "" { unitPrice = strconv.FormatInt(item.UnitPrice, 10) }
		amount := strings.TrimSpace(item.AmountDecimal)
		if amount == "" { amount = strconv.FormatInt(item.Amount, 10) }
		fmt.Fprintf(&text, "%d. %s\r\n", index+1, shown(item.Description, "未命名商品"))
		fmt.Fprintf(&text, "　數量：%s　單價：%s　金額：%s　課稅別：%s\r\n",
			quantity, displayfmt.Decimal(unitPrice), displayfmt.Decimal(amount), shown(item.TaxType, "應稅"))
		if remark := cleanLine(item.Remark); remark != "" {
			fmt.Fprintf(&text, "　備註：%s\r\n", remark)
		}
	}
	return text.String()
}

func displayState(record appdata.InvoiceRecord) (string, string) {
	switch record.InvoiceState {
	case appdata.InvoiceStateFailed:
		return "失敗", "未開立，可修正後重新嘗試"
	case appdata.InvoiceStateUnknown:
		return "需確認", "開立結果不明，禁止重送"
	case appdata.InvoiceStateChanging:
		return "處理中", "正在確認開立結果，禁止重送"
	case appdata.InvoiceStateVoided:
		return "作廢", "發票已作廢"
	case appdata.InvoiceStateOpened:
		if record.UploadStatus == 91 {
			return "需確認", "發票已開立，但上傳發生錯誤"
		}
		if record.UploadStatus == 99 {
			return "完成", "發票已開立並上傳完成"
		}
		return "已開立", "等待或尚未查詢上傳狀態"
	default:
		return "需確認", "未知的本機狀態"
	}
}

func addCount(summary *counts, record appdata.InvoiceRecord) {
	switch record.InvoiceState {
	case appdata.InvoiceStateFailed:
		summary.failed++
	case appdata.InvoiceStateUnknown:
		summary.attention++
	case appdata.InvoiceStateChanging:
		summary.pending++
	case appdata.InvoiceStateVoided:
		summary.voided++
	case appdata.InvoiceStateOpened:
		if record.UploadStatus == 91 {
			summary.attention++
		} else {
			summary.opened++
		}
	default:
		summary.attention++
	}
}

func uploadText(record appdata.InvoiceRecord) string {
	if value := cleanLine(record.UploadStatusText); value != "" {
		return value
	}
	switch record.UploadStatus {
	case 0:
		return "尚未查詢"
	case 1:
		return "待處理"
	case 2:
		return "上傳中"
	case 3:
		return "已上傳"
	case 31:
		return "處理中"
	case 32:
		return "待確認"
	case 91:
		return "錯誤"
	case 99:
		return "完成"
	default:
		return fmt.Sprintf("狀態 %d", record.UploadStatus)
	}
}

func environmentText(value string) string {
	switch strings.TrimSpace(value) {
	case appdata.EnvironmentTest:
		return "光貿測試"
	case appdata.EnvironmentProduction:
		return "正式公司"
	default:
		return "舊紀錄／未記錄"
	}
}

func searchText(record appdata.InvoiceRecord) string {
	return strings.ToLower(strings.Join([]string{
		record.SentAt, record.InvoiceDate, record.InvoiceTime, record.InvoiceNumber,
		record.Source, record.OriginalOrderID, record.OrderID, record.BuyerIdentifier,
		record.BuyerName, record.Delivery, record.InvoiceState,
		record.UploadStatusText, record.ErrorMessage, environmentText(record.Environment),
	}, " "))
}

func shown(value, fallback string) string {
	if value = cleanLine(value); value != "" {
		return value
	}
	return cleanLine(fallback)
}

func cleanLine(value string) string {
	value = strings.ReplaceAll(value, "\r", " ")
	value = strings.ReplaceAll(value, "\n", " ")
	value = strings.ReplaceAll(value, "\t", " ")
	return strings.Join(strings.Fields(value), " ")
}
