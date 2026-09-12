package appdata

import (
	"fmt"
	"os"
	"path/filepath"

	"cyinvoice/internal/securestore"
)

type Repository struct {
	DataDir    string
	Settings   *SettingsStore
	Invoices   *InvoiceStore
	BuyerNames *BuyerNameStore
}

func Open(baseDir string, protector securestore.Protector) (*Repository, error) {
	dataDir := filepath.Join(baseDir, "Data")
	if err := os.MkdirAll(dataDir, 0o700); err != nil {
		return nil, fmt.Errorf("create Data directory: %w", err)
	}
	if err := os.MkdirAll(filepath.Join(baseDir, "Cache", "InvoicePDF"), 0o700); err != nil {
		return nil, fmt.Errorf("create Cache directory: %w", err)
	}

	repository := &Repository{
		DataDir:    dataDir,
		Settings:   NewSettingsStore(dataDir, protector),
		Invoices:   NewInvoiceStore(dataDir),
		BuyerNames: NewBuyerNameStore(dataDir),
	}
	if _, err := repository.Settings.LoadOrCreate(); err != nil {
		return nil, err
	}
	if _, err := repository.Invoices.LoadOrCreate(); err != nil {
		return nil, err
	}
	if _, err := repository.BuyerNames.LoadOrCreate(); err != nil {
		return nil, err
	}
	return repository, nil
}
