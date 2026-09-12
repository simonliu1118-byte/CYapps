package amego

import (
	"encoding/json"
	"testing"
)

func TestQueryResultAcceptsNumericInvoiceDateAndTimeAliases(t *testing.T) {
	var result QueryResult
	if err := json.Unmarshal([]byte(`{"invoice_number":"AA12345678","order_id":"ORDER-1","invoice_date":20260911,"invoice_time":132501}`), &result); err != nil {
		t.Fatal(err)
	}
	if result.InvoiceDate != "20260911" {
		t.Fatalf("invoice date = %q", result.InvoiceDate)
	}
	if result.InvoiceTime != "132501" {
		t.Fatalf("invoice time = %q", result.InvoiceTime)
	}
}

func TestQueryResultAcceptsNumericDocumentedDateAndTime(t *testing.T) {
	var result QueryResult
	if err := json.Unmarshal([]byte(`{"invoice_number":"AA12345678","order_id":"ORDER-1","date":20260911,"time":93005}`), &result); err != nil {
		t.Fatal(err)
	}
	if result.InvoiceDate != "20260911" {
		t.Fatalf("date = %q", result.InvoiceDate)
	}
	if result.InvoiceTime != "93005" {
		t.Fatalf("time = %q", result.InvoiceTime)
	}
}

func TestQueryResultKeepsStringDateAndTimeCompatibility(t *testing.T) {
	var result QueryResult
	if err := json.Unmarshal([]byte(`{"invoice_number":"AA12345678","order_id":"ORDER-1","date":"2026-09-11","time":"13:25:01"}`), &result); err != nil {
		t.Fatal(err)
	}
	if result.InvoiceDate != "2026-09-11" || result.InvoiceTime != "13:25:01" {
		t.Fatalf("result = %#v", result)
	}
}

func TestQueryResultRejectsNonScalarDate(t *testing.T) {
	var result QueryResult
	if err := json.Unmarshal([]byte(`{"invoice_date":{"value":20260911}}`), &result); err == nil {
		t.Fatal("expected non-scalar invoice_date to be rejected")
	}
}
