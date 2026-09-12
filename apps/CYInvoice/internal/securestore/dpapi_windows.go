//go:build windows

package securestore

import (
	"encoding/base64"
	"errors"
	"fmt"
	"syscall"
	"unsafe"
)

const cryptProtectUIForbidden = 0x1

var (
	crypt32                = syscall.NewLazyDLL("crypt32.dll")
	kernel32               = syscall.NewLazyDLL("kernel32.dll")
	procCryptProtectData   = crypt32.NewProc("CryptProtectData")
	procCryptUnprotectData = crypt32.NewProc("CryptUnprotectData")
	procLocalFree          = kernel32.NewProc("LocalFree")
)

type dataBlob struct {
	Size uint32
	Data *byte
}

type dpapiProtector struct{}

func New() Protector { return dpapiProtector{} }

func (dpapiProtector) Protect(plaintext []byte) (string, error) {
	if len(plaintext) == 0 {
		return "", nil
	}
	input := dataBlob{Size: uint32(len(plaintext)), Data: &plaintext[0]}
	var output dataBlob
	result, _, callErr := procCryptProtectData.Call(
		uintptr(unsafe.Pointer(&input)), 0, 0, 0, 0,
		cryptProtectUIForbidden, uintptr(unsafe.Pointer(&output)),
	)
	if result == 0 {
		return "", fmt.Errorf("CryptProtectData: %w", callErr)
	}
	defer procLocalFree.Call(uintptr(unsafe.Pointer(output.Data)))
	encrypted := append([]byte(nil), unsafe.Slice(output.Data, int(output.Size))...)
	return base64.StdEncoding.EncodeToString(encrypted), nil
}

func (dpapiProtector) Unprotect(ciphertext string) ([]byte, error) {
	if ciphertext == "" {
		return nil, nil
	}
	encrypted, err := base64.StdEncoding.DecodeString(ciphertext)
	if err != nil {
		return nil, fmt.Errorf("decode protected value: %w", err)
	}
	if len(encrypted) == 0 {
		return nil, errors.New("protected value is empty")
	}
	input := dataBlob{Size: uint32(len(encrypted)), Data: &encrypted[0]}
	var output dataBlob
	result, _, callErr := procCryptUnprotectData.Call(
		uintptr(unsafe.Pointer(&input)), 0, 0, 0, 0,
		cryptProtectUIForbidden, uintptr(unsafe.Pointer(&output)),
	)
	if result == 0 {
		return nil, fmt.Errorf("CryptUnprotectData: %w", callErr)
	}
	defer procLocalFree.Call(uintptr(unsafe.Pointer(output.Data)))
	return append([]byte(nil), unsafe.Slice(output.Data, int(output.Size))...), nil
}
