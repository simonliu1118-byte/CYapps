package appdata

import (
	"crypto/rand"
	"crypto/hmac"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/binary"
	"encoding/hex"
	"errors"
	"fmt"
	"path/filepath"
	"strings"
	"sync"

	"cyinvoice/internal/securestore"
)

type SettingsStore struct {
	mu        sync.Mutex
	path      string
	protector securestore.Protector
}

func NewSettingsStore(dataDir string, protector securestore.Protector) *SettingsStore {
	return &SettingsStore{
		path:      filepath.Join(dataDir, "settings.json"),
		protector: protector,
	}
}

func (store *SettingsStore) LoadOrCreate() (Settings, error) {
	store.mu.Lock()
	defer store.mu.Unlock()

	var settings Settings
	found, err := readJSON(store.path, &settings)
	if err != nil {
		return Settings{}, err
	}
	if found {
		if err := validateSettings(settings); err != nil {
			return Settings{}, fmt.Errorf("validate settings.json: %w", err)
		}
		return settings, nil
	}
	settings, err = newDefaultSettings(store.protector)
	if err != nil {
		return Settings{}, err
	}
	if err := writeJSON(store.path, settings); err != nil {
		return Settings{}, err
	}
	return settings, nil
}

func (store *SettingsStore) Save(settings Settings) error {
	store.mu.Lock()
	defer store.mu.Unlock()
	if err := validateSettings(settings); err != nil {
		return err
	}
	return writeJSON(store.path, settings)
}

func (store *SettingsStore) SetAdminPassword(settings *Settings, password string) error {
	if settings == nil {
		return errors.New("settings is nil")
	}
	if password == "" {
		return errors.New("管理密碼不可空白")
	}
	salt, err := randomHex(16)
	if err != nil {
		return fmt.Errorf("create password salt: %w", err)
	}
	settings.PasswordSalt = salt
	settings.PasswordHash = hashPassword(password, salt)
	settings.AdminPasswordSet = true
	return nil
}

func CheckAdminPassword(settings Settings, password string) bool {
	if !settings.AdminPasswordSet || settings.PasswordSalt == "" || settings.PasswordHash == "" {
		return false
	}
	if strings.HasPrefix(settings.PasswordHash, passwordHashPrefix) {
		expected, err := hex.DecodeString(strings.TrimPrefix(settings.PasswordHash, passwordHashPrefix))
		if err != nil || len(expected) != sha256.Size { return false }
		actual := derivePasswordKey([]byte(password), []byte(settings.PasswordSalt), passwordHashIterations, sha256.Size)
		return subtle.ConstantTimeCompare(actual, expected) == 1
	}
	expected, err := hex.DecodeString(settings.PasswordHash)
	if err != nil { return false }
	actual, _ := hex.DecodeString(hashPasswordLegacySaltFirst(password, settings.PasswordSalt))
	legacy, _ := hex.DecodeString(hashPasswordLegacy(password, settings.PasswordSalt))
	return len(actual) == len(expected) && (subtle.ConstantTimeCompare(actual, expected)|subtle.ConstantTimeCompare(legacy, expected)) == 1
}

func NeedsInitialSetup(settings Settings) bool {
	return !settings.AdminPasswordSet || strings.TrimSpace(settings.MOPasswordEnc) == ""
}

// InitialSetupRequired verifies both the structural flags and the encrypted MO
// password. A DPAPI value copied from another Windows account must return an
// error instead of being mistaken for a completed setup or overwritten.
func (store *SettingsStore) InitialSetupRequired(settings Settings) (bool, error) {
	if NeedsInitialSetup(settings) {
		return true, nil
	}
	password, err := store.MOPassword(settings)
	if err != nil {
		return false, fmt.Errorf("驗證已加密的 MO店+ Excel 密碼：%w", err)
	}
	return strings.TrimSpace(password) == "", nil
}

func (store *SettingsStore) SetMOPassword(settings *Settings, password string) error {
	if settings == nil {
		return errors.New("settings is nil")
	}
	if strings.TrimSpace(password) == "" {
		return errors.New("MO店+ Excel 密碼不可空白")
	}
	return store.protectInto(&settings.MOPasswordEnc, password)
}

func (store *SettingsStore) MOPassword(settings Settings) (string, error) {
	return store.unprotect(settings.MOPasswordEnc)
}

func (store *SettingsStore) SetProdAppKey(settings *Settings, appKey string) error {
	if settings == nil {
		return errors.New("settings is nil")
	}
	return store.protectInto(&settings.ProdAppKeyEnc, strings.TrimSpace(appKey))
}

func (store *SettingsStore) ProdAppKey(settings Settings) (string, error) {
	return store.unprotect(settings.ProdAppKeyEnc)
}

func (store *SettingsStore) protectInto(destination *string, value string) error {
	if destination == nil {
		return errors.New("settings is nil")
	}
	if value == "" {
		*destination = ""
		return nil
	}
	protected, err := store.protector.Protect([]byte(value))
	if err != nil {
		return err
	}
	*destination = protected
	return nil
}

func (store *SettingsStore) unprotect(value string) (string, error) {
	if value == "" {
		return "", nil
	}
	plaintext, err := store.protector.Unprotect(value)
	if err != nil {
		return "", err
	}
	return string(plaintext), nil
}

func newDefaultSettings(protector securestore.Protector) (Settings, error) {
	return Settings{Environment: EnvironmentTest}, nil
}

func validateSettings(settings Settings) error {
	switch settings.Environment {
	case EnvironmentTest, EnvironmentProduction:
	default:
		return fmt.Errorf("unknown environment %q", settings.Environment)
	}
	if settings.ProdInvoice != "" && !validBAN(settings.ProdInvoice) {
		return errors.New("正式公司統編必須為 8 碼")
	}
	if settings.AdminPasswordSet {
		if _, err := hex.DecodeString(settings.PasswordSalt); err != nil || len(settings.PasswordSalt) < 16 {
			return errors.New("invalid password_salt")
		}
		hashText := strings.TrimPrefix(settings.PasswordHash, passwordHashPrefix)
		if hash, err := hex.DecodeString(hashText); err != nil || len(hash) != sha256.Size { return errors.New("invalid password_hash") }
	}
	if settings.Environment == EnvironmentProduction &&
		(settings.ProdInvoice == "" || settings.ProdAppKeyEnc == "") {
		return errors.New("正式公司請輸入 8 碼公司統編與 App Key")
	}
	return nil
}

func hashPassword(password, salt string) string {
	return passwordHashPrefix + hex.EncodeToString(derivePasswordKey([]byte(password), []byte(salt), passwordHashIterations, sha256.Size))
}

const (
	passwordHashIterations = 210000
	passwordHashPrefix = "pbkdf2-sha256$210000$"
)

// derivePasswordKey implements PBKDF2-HMAC-SHA256 using only the Go standard
// library so the portable Windows build gains no runtime dependency.
func derivePasswordKey(password, salt []byte, iterations, size int) []byte {
	result := make([]byte, 0, size)
	for block := uint32(1); len(result) < size; block++ {
		mac := hmac.New(sha256.New, password)
		mac.Write(salt)
		var counter [4]byte
		binary.BigEndian.PutUint32(counter[:], block)
		mac.Write(counter[:])
		u := mac.Sum(nil)
		t := append([]byte(nil), u...)
		for iteration := 1; iteration < iterations; iteration++ {
			mac = hmac.New(sha256.New, password)
			mac.Write(u)
			u = mac.Sum(nil)
			for index := range t { t[index] ^= u[index] }
		}
		result = append(result, t...)
	}
	return result[:size]
}

func hashPasswordLegacySaltFirst(password, salt string) string {
	sum := sha256.Sum256([]byte(salt + password))
	return hex.EncodeToString(sum[:])
}

// hashPasswordLegacy accepts the alternative concatenation used by some
// early local builds, so an existing administrator is not locked out.
func hashPasswordLegacy(password, salt string) string {
	sum := sha256.Sum256([]byte(password + salt))
	return hex.EncodeToString(sum[:])
}

func randomHex(byteCount int) (string, error) {
	value := make([]byte, byteCount)
	if _, err := rand.Read(value); err != nil {
		return "", err
	}
	return hex.EncodeToString(value), nil
}
