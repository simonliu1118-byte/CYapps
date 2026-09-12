package appdata

import (
	"errors"
	"path/filepath"
	"strings"
	"sync"
)

type BuyerNameStore struct {
	mu   sync.Mutex
	path string
}

func NewBuyerNameStore(dataDir string) *BuyerNameStore {
	return &BuyerNameStore{path: filepath.Join(dataDir, "buyer_names.json")}
}

func (store *BuyerNameStore) LoadOrCreate() (map[string]string, error) {
	store.mu.Lock()
	defer store.mu.Unlock()
	names, found, err := store.load()
	if err != nil {
		return nil, err
	}
	if !found {
		if err := writeJSON(store.path, names); err != nil {
			return nil, err
		}
	}
	return cloneNames(names), nil
}

func (store *BuyerNameStore) Lookup(ban string) (string, bool, error) {
	store.mu.Lock()
	defer store.mu.Unlock()
	names, _, err := store.load()
	if err != nil {
		return "", false, err
	}
	name, found := names[strings.TrimSpace(ban)]
	return name, found, nil
}

// RememberAfterSuccessfulInvoice implements the V1.0.0 rule:
// only a successful lookup with an empty API name, followed by manual entry
// and a successfully opened invoice, may be remembered locally.
func (store *BuyerNameStore) RememberAfterSuccessfulInvoice(
	ban string,
	lookupSucceeded bool,
	apiName string,
	manualName string,
	invoiceSucceeded bool,
) (bool, error) {
	ban = strings.TrimSpace(ban)
	manualName = strings.TrimSpace(manualName)
	if !lookupSucceeded || strings.TrimSpace(apiName) != "" ||
		!invoiceSucceeded || manualName == "" {
		return false, nil
	}
	if !validBAN(ban) {
		return false, errors.New("公司統編必須為 8 碼")
	}

	store.mu.Lock()
	defer store.mu.Unlock()
	names, _, err := store.load()
	if err != nil {
		return false, err
	}
	if names[ban] == manualName {
		return false, nil
	}
	names[ban] = manualName
	if err := writeJSON(store.path, names); err != nil {
		return false, err
	}
	return true, nil
}

func (store *BuyerNameStore) load() (map[string]string, bool, error) {
	names := make(map[string]string)
	found, err := readJSON(store.path, &names)
	if err != nil {
		return nil, false, err
	}
	for ban, name := range names {
		if !validBAN(ban) || strings.TrimSpace(name) == "" {
			return nil, found, errors.New("buyer_names.json contains an invalid entry")
		}
	}
	return names, found, nil
}

func cloneNames(names map[string]string) map[string]string {
	copyOfNames := make(map[string]string, len(names))
	for ban, name := range names {
		copyOfNames[ban] = name
	}
	return copyOfNames
}
