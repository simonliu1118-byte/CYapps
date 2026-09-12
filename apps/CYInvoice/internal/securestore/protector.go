package securestore

// Protector encrypts local secrets before they are serialized to settings.json.
// The Windows implementation is bound to the current Windows user through DPAPI.
type Protector interface {
	Protect(plaintext []byte) (string, error)
	Unprotect(ciphertext string) ([]byte, error)
}
