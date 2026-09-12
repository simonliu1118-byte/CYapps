package appdata

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestInvoiceStoreCreatesEmptyArray(t *testing.T) {
	dataDir := t.TempDir()
	store := NewInvoiceStore(dataDir)
	records, err := store.LoadOrCreate()
	if err != nil {
		t.Fatal(err)
	}
	if len(records) != 0 {
		t.Fatalf("records = %#v", records)
	}
	raw, err := os.ReadFile(filepath.Join(dataDir, "invoices.json"))
	if err != nil {
		t.Fatal(err)
	}
	if strings.TrimSpace(string(raw)) != "[]" {
		t.Fatalf("empty history JSON = %q", raw)
	}
}

func TestInvoiceStatusRefreshKeepsSentAt(t *testing.T) {
	store := NewInvoiceStore(t.TempDir())
	record := InvoiceRecord{
		ID:           "record-1",
		OrderID:      "20260905001",
		Amount:       100,
		InvoiceState: InvoiceStateOpened,
		SentAt:       "2026/09/05 07:10:00",
		InvoiceDate:  "2026/09/05",
		InvoiceTime:  "07:10:01",
	}
	if err := store.Append(record); err != nil {
		t.Fatal(err)
	}
	if err := store.UpdateStatus(record.ID, StatusUpdate{
		InvoiceNumber: "AB12345678",
		InvoiceState:  InvoiceStateOpened,
		InvoiceDate:   "2026/09/05",
		InvoiceTime:   "07:11:00",
		LastChecked:   "2026/09/05 07:12:00",
	}); err != nil {
		t.Fatal(err)
	}
	records, err := store.LoadOrCreate()
	if err != nil {
		t.Fatal(err)
	}
	if records[0].SentAt != record.SentAt {
		t.Fatalf("SentAt changed to %q", records[0].SentAt)
	}
	if records[0].InvoiceTime != "07:11:00" {
		t.Fatalf("official invoice time = %q", records[0].InvoiceTime)
	}
}

func TestLegacyInvoiceFreezesSentAtOnLoad(t *testing.T) {
	dataDir := t.TempDir()
	path := filepath.Join(dataDir, "invoices.json")
	legacy := `[{"id":"legacy","order_id":"old","amount":1,"invoice_state":"已開立","invoice_date":"2026/09/04","invoice_time":"12:34:56","legacy_field":"keep-me"}]`
	if err := os.WriteFile(path, []byte(legacy), 0o600); err != nil {
		t.Fatal(err)
	}
	store := NewInvoiceStore(dataDir)
	records, err := store.LoadOrCreate()
	if err != nil {
		t.Fatal(err)
	}
	if records[0].SentAt != "2026/09/04 12:34:56" {
		t.Fatalf("migrated SentAt = %q", records[0].SentAt)
	}
	raw, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(raw), `"sent_at": "2026/09/04 12:34:56"`) {
		t.Fatalf("migration was not persisted: %s", raw)
	}
	if !strings.Contains(string(raw), `"legacy_field": "keep-me"`) {
		t.Fatalf("unknown V1.0.0 field was discarded: %s", raw)
	}
}

func TestCorruptInvoiceJSONIsNotOverwritten(t *testing.T) {
	dataDir := t.TempDir()
	path := filepath.Join(dataDir, "invoices.json")
	const corrupt = "{not-json"
	if err := os.WriteFile(path, []byte(corrupt), 0o600); err != nil {
		t.Fatal(err)
	}
	store := NewInvoiceStore(dataDir)
	if _, err := store.LoadOrCreate(); err == nil {
		t.Fatal("corrupt JSON was accepted")
	}
	raw, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	if string(raw) != corrupt {
		t.Fatalf("corrupt source was overwritten: %q", raw)
	}
}
