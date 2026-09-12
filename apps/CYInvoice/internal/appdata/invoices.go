package appdata

import (
	"encoding/json"
	"errors"
	"fmt"
	"path/filepath"
	"strings"
	"sync"
)

type InvoiceStore struct {
	mu   sync.Mutex
	path string
}

func NewInvoiceStore(dataDir string) *InvoiceStore {
	return &InvoiceStore{path: filepath.Join(dataDir, "invoices.json")}
}

func (store *InvoiceStore) LoadOrCreate() ([]InvoiceRecord, error) {
	store.mu.Lock()
	defer store.mu.Unlock()
	records, changed, found, err := store.load()
	if err != nil {
		return nil, err
	}
	if !found || changed {
		if err := writeJSON(store.path, records); err != nil {
			return nil, err
		}
	}
	return cloneRecords(records), nil
}

func (store *InvoiceStore) Save(records []InvoiceRecord) error {
	store.mu.Lock()
	defer store.mu.Unlock()
	if err := validateRecords(records); err != nil {
		return err
	}
	return writeJSON(store.path, records)
}

func (store *InvoiceStore) Append(record InvoiceRecord) error {
	store.mu.Lock()
	defer store.mu.Unlock()
	records, _, _, err := store.load()
	if err != nil {
		return err
	}
	if err := validateRecord(record); err != nil {
		return err
	}
	for _, existing := range records {
		if record.ID != "" && existing.ID == record.ID {
			return fmt.Errorf("invoice record ID %q already exists", record.ID)
		}
	}
	records = append(records, record)
	return writeJSON(store.path, records)
}

// UpdateStatus deliberately does not accept or modify SentAt. Status refreshes
// may update official invoice time and LastChecked, but the send time is immutable.
func (store *InvoiceStore) UpdateStatus(id string, update StatusUpdate) error {
	store.mu.Lock()
	defer store.mu.Unlock()
	records, _, _, err := store.load()
	if err != nil {
		return err
	}
	for index := range records {
		if records[index].ID != id {
			continue
		}
		records[index].InvoiceNumber = update.InvoiceNumber
		records[index].InvoiceState = update.InvoiceState
		records[index].UploadStatus = update.UploadStatus
		records[index].UploadStatusText = update.UploadStatusText
		records[index].ErrorMessage = update.ErrorMessage
		records[index].InvoiceDate = update.InvoiceDate
		records[index].InvoiceTime = update.InvoiceTime
		records[index].LastChecked = update.LastChecked
		return writeJSON(store.path, records)
	}
	return fmt.Errorf("invoice record %q not found", id)
}

func (store *InvoiceStore) load() ([]InvoiceRecord, bool, bool, error) {
	records := make([]InvoiceRecord, 0)
	found, err := readJSON(store.path, &records)
	if err != nil {
		return nil, false, false, err
	}
	changed := false
	for index := range records {
		if strings.TrimSpace(records[index].SentAt) == "" {
			records[index].SentAt = legacySentAt(records[index])
			if records[index].SentAt != "" {
				changed = true
			}
		}
	}
	if err := validateRecords(records); err != nil {
		return nil, false, found, err
	}
	return records, changed, found, nil
}

func legacySentAt(record InvoiceRecord) string {
	date := strings.TrimSpace(record.InvoiceDate)
	clock := strings.TrimSpace(record.InvoiceTime)
	if date != "" && clock != "" {
		return date + " " + clock
	}
	if date != "" {
		return date
	}
	return strings.TrimSpace(record.LastChecked)
}

func validateRecords(records []InvoiceRecord) error {
	ids := make(map[string]struct{}, len(records))
	for index, record := range records {
		if err := validateRecord(record); err != nil {
			return fmt.Errorf("invoice record %d: %w", index+1, err)
		}
		if record.ID != "" {
			if _, exists := ids[record.ID]; exists {
				return fmt.Errorf("invoice record ID %q is duplicated", record.ID)
			}
			ids[record.ID] = struct{}{}
		}
	}
	return nil
}

func validateRecord(record InvoiceRecord) error {
	if strings.TrimSpace(record.OrderID) == "" {
		return ErrOrderIDRequired
	}
	if record.Amount < 0 {
		return errors.New("invoice amount cannot be negative")
	}
	if strings.TrimSpace(record.InvoiceState) == "" {
		return errors.New("invoice state is required")
	}
	return nil
}

func cloneRecords(records []InvoiceRecord) []InvoiceRecord {
	copyOfRecords := append([]InvoiceRecord(nil), records...)
	for index := range copyOfRecords {
		copyOfRecords[index].Items = append([]InvoiceItem(nil), copyOfRecords[index].Items...)
		if copyOfRecords[index].Extra != nil {
			extra := make(map[string]json.RawMessage, len(copyOfRecords[index].Extra))
			for name, value := range copyOfRecords[index].Extra {
				extra[name] = append(json.RawMessage(nil), value...)
			}
			copyOfRecords[index].Extra = extra
		}
	}
	return copyOfRecords
}
