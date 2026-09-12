package appdata

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestSettingsNeverWritePlaintextSecrets(t *testing.T) {
	dataDir := t.TempDir()
	store := NewSettingsStore(dataDir, testProtector{})
	settings, err := store.LoadOrCreate()
	if err != nil {
		t.Fatal(err)
	}
	if CheckAdminPassword(settings, "not-configured") {
		t.Fatal("unconfigured administrator password was accepted")
	}
	const adminPassword = "TEST-ADMIN-PASSWORD-NOT-REAL"
	if err := store.SetAdminPassword(&settings, adminPassword); err != nil { t.Fatal(err) }
	const moPassword = "TEST-MO-PASSWORD-NOT-REAL"
	if err := store.SetMOPassword(&settings, moPassword); err != nil {
		t.Fatal(err)
	}

	const appKey = "TEST-APP-KEY-NOT-A-REAL-SECRET"
	if err := store.SetProdAppKey(&settings, appKey); err != nil {
		t.Fatal(err)
	}
	settings.ProdInvoice = "12345675"
	settings.Environment = EnvironmentProduction
	if err := store.Save(settings); err != nil {
		t.Fatal(err)
	}

	raw, err := os.ReadFile(filepath.Join(dataDir, "settings.json"))
	if err != nil {
		t.Fatal(err)
	}
	text := string(raw)
	for _, secret := range []string{adminPassword, moPassword, appKey} {
		if strings.Contains(text, secret) {
			t.Fatalf("settings.json contains plaintext secret %q", secret)
		}
	}
	recovered, err := store.ProdAppKey(settings)
	if err != nil || recovered != appKey {
		 t.Fatalf("App Key round trip = %q, %v", recovered, err)
	}
	recoveredMO, err := store.MOPassword(settings)
	if err != nil || recoveredMO != moPassword {
		t.Fatalf("MO password round trip = %q, %v", recoveredMO, err)
	}
}

func TestSettingsRejectProductionWithoutCredentials(t *testing.T) {
	store := NewSettingsStore(t.TempDir(), testProtector{})
	settings, err := store.LoadOrCreate()
	if err != nil {
		t.Fatal(err)
	}
	settings.Environment = EnvironmentProduction
	if err := store.Save(settings); err == nil {
		t.Fatal("production settings without company credentials were accepted")
	}
}

func TestAdminPasswordChange(t *testing.T) {
	store := NewSettingsStore(t.TempDir(), testProtector{})
	settings, err := store.LoadOrCreate()
	if err != nil {
		t.Fatal(err)
	}
	if err := store.SetAdminPassword(&settings, "old-password"); err != nil { t.Fatal(err) }
	if err := store.SetAdminPassword(&settings, "new-password"); err != nil {
		t.Fatal(err)
	}
	if CheckAdminPassword(settings, "old-password") {
		t.Fatal("old password still accepted")
	}
	if !CheckAdminPassword(settings, "new-password") {
		t.Fatal("new password rejected")
	}
}

func TestAdminPasswordUsesPBKDF2AndAcceptsLegacyHash(t *testing.T) {
	store := NewSettingsStore(t.TempDir(), testProtector{})
	settings, err := store.LoadOrCreate()
	if err != nil { t.Fatal(err) }
	if err := store.SetAdminPassword(&settings, "new-password"); err != nil { t.Fatal(err) }
	if !strings.HasPrefix(settings.PasswordHash, passwordHashPrefix) || !CheckAdminPassword(settings, "new-password") {
		t.Fatalf("new password hash=%q", settings.PasswordHash)
	}
	settings.PasswordHash = hashPasswordLegacySaltFirst("legacy-password", settings.PasswordSalt)
	if !CheckAdminPassword(settings, "legacy-password") {
		t.Fatal("legacy administrator password was rejected")
	}
}

func TestInitialSetupRequiresBothPasswords(t *testing.T) {
	store := NewSettingsStore(t.TempDir(), testProtector{})
	settings, err := store.LoadOrCreate()
	if err != nil { t.Fatal(err) }
	if !NeedsInitialSetup(settings) { t.Fatal("new settings did not require initial setup") }
	if required, checkErr := store.InitialSetupRequired(settings); checkErr != nil || !required { t.Fatalf("new settings required=%v err=%v", required, checkErr) }
	if err = store.SetAdminPassword(&settings, "TEST-ADMIN-NOT-REAL"); err != nil { t.Fatal(err) }
	if !NeedsInitialSetup(settings) { t.Fatal("MO password was not required") }
	if err = store.SetMOPassword(&settings, "   "); err == nil { t.Fatal("blank MO password was accepted") }
	if err = store.SetMOPassword(&settings, "TEST-MO-NOT-REAL"); err != nil { t.Fatal(err) }
	if NeedsInitialSetup(settings) { t.Fatal("completed settings still require initial setup") }
	if required, checkErr := store.InitialSetupRequired(settings); checkErr != nil || required { t.Fatalf("completed settings required=%v err=%v", required, checkErr) }
}

func TestInitialSetupDoesNotOverwriteUndecryptableMOPassword(t *testing.T) {
	store := NewSettingsStore(t.TempDir(), testProtector{})
	settings, err := store.LoadOrCreate()
	if err != nil { t.Fatal(err) }
	if err = store.SetAdminPassword(&settings, "TEST-ADMIN-NOT-REAL"); err != nil { t.Fatal(err) }
	settings.MOPasswordEnc = "TEST-UNDECRYPTABLE-NOT-REAL"
	if NeedsInitialSetup(settings) { t.Fatal("structural check should see a stored encrypted value") }
	required, err := store.InitialSetupRequired(settings)
	if err == nil || required { t.Fatalf("required=%v err=%v", required, err) }
}

func TestInitialSetupRequiresNonEmptyDecryptedMOPassword(t *testing.T) {
	store := NewSettingsStore(t.TempDir(), testProtector{})
	settings, err := store.LoadOrCreate()
	if err != nil { t.Fatal(err) }
	if err = store.SetAdminPassword(&settings, "TEST-ADMIN-NOT-REAL"); err != nil { t.Fatal(err) }
	emptyCipher, err := (testProtector{}).Protect([]byte("   "))
	if err != nil { t.Fatal(err) }
	settings.MOPasswordEnc = emptyCipher
	required, err := store.InitialSetupRequired(settings)
	if err != nil || !required { t.Fatalf("required=%v err=%v", required, err) }
}
