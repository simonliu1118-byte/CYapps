//go:build !windows

package securestore

import "errors"

type unsupportedProtector struct{}

func New() Protector { return unsupportedProtector{} }

func (unsupportedProtector) Protect([]byte) (string, error) {
	return "", errors.New("Windows DPAPI is unavailable on this platform")
}

func (unsupportedProtector) Unprotect(string) ([]byte, error) {
	return nil, errors.New("Windows DPAPI is unavailable on this platform")
}
