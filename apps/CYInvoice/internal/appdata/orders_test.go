package appdata

import (
	"errors"
	"testing"
	"time"
)

func TestNextManualOrderID(t *testing.T) {
	now := time.Date(2026, 9, 5, 7, 0, 0, 0, time.FixedZone("Asia/Taipei", 8*60*60))
	records := []InvoiceRecord{
		{OrderID: "20260905001"},
		{OrderID: "20260905009"},
		{OrderID: "20260904099"},
		{OrderID: "external-order"},
	}
	got, err := NextManualOrderID(now, records)
	if err != nil {
		t.Fatal(err)
	}
	if got != "20260905010" {
		t.Fatalf("NextManualOrderID = %q", got)
	}
}

func TestNextManualOrderIDExhausted(t *testing.T) {
	now := time.Date(2026, 9, 5, 0, 0, 0, 0, time.UTC)
	_, err := NextManualOrderID(now, []InvoiceRecord{{OrderID: "20260905999"}})
	if !errors.Is(err, ErrDailyOrderSequenceExhausted) {
		t.Fatalf("error = %v", err)
	}
}

func TestDuplicateBlockReason(t *testing.T) {
	base := InvoiceRecord{Source: "MO店+", OriginalOrderID: "MO-001"}
	tests := []struct {
		name    string
		state   string
		blocked bool
	}{
		{"opened", InvoiceStateOpened, true},
		{"unknown", InvoiceStateUnknown, true},
		{"changing", InvoiceStateChanging, true},
		{"unrecognized is safe-blocked", "未來新增狀態", true},
		{"failed", InvoiceStateFailed, false},
		{"voided", InvoiceStateVoided, false},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			record := base
			record.InvoiceState = test.state
			reason := DuplicateBlockReason([]InvoiceRecord{record}, " mo店+ ", "MO-001")
			if (reason != "") != test.blocked {
				t.Fatalf("reason = %q", reason)
			}
		})
	}
}

func TestDuplicateBlockReasonSeparatesTestAndProduction(t *testing.T) {
	record := InvoiceRecord{
		Source: SourceMO, OriginalOrderID: "MO-001",
		InvoiceState: InvoiceStateOpened, Environment: EnvironmentTest,
	}
	if reason := DuplicateBlockReason([]InvoiceRecord{record}, SourceMO, "MO-001", EnvironmentProduction); reason != "" {
		t.Fatalf("test record blocked production: %q", reason)
	}
	if reason := DuplicateBlockReason([]InvoiceRecord{record}, SourceMO, "MO-001", EnvironmentTest); reason == "" {
		t.Fatal("test record did not block a second test issue")
	}
}

func TestLegacyRecordStillBlocksBothEnvironments(t *testing.T) {
	record := InvoiceRecord{Source: SourceMO, OriginalOrderID: "MO-001", InvoiceState: InvoiceStateOpened}
	for _, environment := range []string{EnvironmentTest, EnvironmentProduction} {
		if reason := DuplicateBlockReason([]InvoiceRecord{record}, SourceMO, "MO-001", environment); reason == "" {
			t.Fatalf("legacy record did not block %s", environment)
		}
	}
}
