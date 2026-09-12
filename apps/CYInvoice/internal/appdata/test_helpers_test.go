package appdata

import (
	"encoding/base64"
	"errors"
	"strings"
)

type testProtector struct{}

func (testProtector) Protect(plaintext []byte) (string, error) {
	if len(plaintext) == 0 {
		return "", nil
	}
	return "test-protected:" + base64.StdEncoding.EncodeToString(plaintext), nil
}

func (testProtector) Unprotect(ciphertext string) ([]byte, error) {
	const prefix = "test-protected:"
	if !strings.HasPrefix(ciphertext, prefix) {
		return nil, errors.New("invalid protected test value")
	}
	return base64.StdEncoding.DecodeString(strings.TrimPrefix(ciphertext, prefix))
}
