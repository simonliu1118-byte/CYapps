package appdata

import (
	"errors"
	"fmt"
	"strconv"
	"strings"
	"time"
)

var ErrDailyOrderSequenceExhausted = errors.New("今日自動訂單編號已達 999 筆")

func NextManualOrderID(now time.Time, records []InvoiceRecord) (string, error) {
	prefix := now.Format("20060102")
	highest := 0
	for _, record := range records {
		orderID := strings.TrimSpace(record.OrderID)
		if len(orderID) != len(prefix)+3 || !strings.HasPrefix(orderID, prefix) {
			continue
		}
		sequence, err := strconv.Atoi(orderID[len(prefix):])
		if err == nil && sequence > highest {
			highest = sequence
		}
	}
	if highest >= 999 {
		return "", ErrDailyOrderSequenceExhausted
	}
	return fmt.Sprintf("%s%03d", prefix, highest+1), nil
}

// DuplicateBlockReason applies the conservative V1.0.0 safety rule. Only an
// explicitly failed or voided prior attempt can be opened again. Unknown and
// future statuses remain blocked until they are positively resolved.
func DuplicateBlockReason(records []InvoiceRecord, source, originalOrderID string, environment ...string) string {
	source = normalizeSource(source)
	originalOrderID = strings.TrimSpace(originalOrderID)
	currentEnvironment := ""
	if len(environment) > 0 { currentEnvironment = strings.TrimSpace(environment[0]) }
	if source == "" || originalOrderID == "" {
		return ""
	}
	for _, record := range records {
		if normalizeSource(record.Source) != source ||
			strings.TrimSpace(record.OriginalOrderID) != originalOrderID {
			continue
		}
		// Legacy records without an environment stay conservative and block in
		// either mode. New test records must not block a later production issue.
		if currentEnvironment != "" && strings.TrimSpace(record.Environment) != "" &&
			strings.TrimSpace(record.Environment) != currentEnvironment {
			continue
		}
		switch strings.TrimSpace(record.InvoiceState) {
		case InvoiceStateFailed, InvoiceStateVoided:
			continue
		case InvoiceStateUnknown:
			return "此原始訂單先前開立結果不明，為避免重複開票已擋下"
		default:
			return "此原始訂單已有尚未確認為可安全重開的發票紀錄，為避免重複開票已擋下"
		}
	}
	return ""
}
