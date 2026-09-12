package appdata

import "testing"

func TestBuyerNameMemorySafetyRules(t *testing.T) {
	store := NewBuyerNameStore(t.TempDir())
	if _, err := store.LoadOrCreate(); err != nil {
		t.Fatal(err)
	}

	tests := []struct {
		name             string
		lookupSucceeded  bool
		apiName          string
		manualName       string
		invoiceSucceeded bool
		wantSaved        bool
	}{
		{"API returned a name", true, "API 公司名稱", "人工名稱", true, false},
		{"lookup failed", false, "", "人工名稱", true, false},
		{"invoice failed", true, "", "人工名稱", false, false},
		{"manual name empty", true, "", "", true, false},
		{"eligible", true, "", "人工名稱", true, true},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			saved, err := store.RememberAfterSuccessfulInvoice(
				"12345675",
				test.lookupSucceeded,
				test.apiName,
				test.manualName,
				test.invoiceSucceeded,
			)
			if err != nil {
				t.Fatal(err)
			}
			if saved != test.wantSaved {
				t.Fatalf("saved = %v, want %v", saved, test.wantSaved)
			}
		})
	}
	name, found, err := store.Lookup("12345675")
	if err != nil {
		t.Fatal(err)
	}
	if !found || name != "人工名稱" {
		t.Fatalf("remembered name = %q, %v", name, found)
	}
}

func TestBuyerNameMemoryRejectsInvalidBAN(t *testing.T) {
	store := NewBuyerNameStore(t.TempDir())
	if _, err := store.RememberAfterSuccessfulInvoice("123", true, "", "人工名稱", true); err == nil {
		t.Fatal("invalid BAN was accepted")
	}
}
