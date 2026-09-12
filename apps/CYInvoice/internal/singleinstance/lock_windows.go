//go:build windows

package singleinstance

import (
	"fmt"
	"syscall"
)

const errorAlreadyExists syscall.Errno = 183

var (
	kernel32 = syscall.NewLazyDLL("kernel32.dll")
	procCreateMutexW = kernel32.NewProc("CreateMutexW")
	procCloseHandle = kernel32.NewProc("CloseHandle")
)

type Lock struct{ handle uintptr }

func Acquire(name string) (*Lock, bool, error) {
	wide, err := syscall.UTF16PtrFromString(`Local\` + name)
	if err != nil { return nil, false, err }
	handle, _, callErr := procCreateMutexW.Call(0, 1, uintptr(unsafePointer(wide)))
	if handle == 0 { return nil, false, fmt.Errorf("CreateMutexW: %w", callErr) }
	if callErr == errorAlreadyExists {
		procCloseHandle.Call(handle)
		return nil, false, nil
	}
	return &Lock{handle: handle}, true, nil
}

func (lock *Lock) Close() {
	if lock == nil || lock.handle == 0 { return }
	procCloseHandle.Call(lock.handle)
	lock.handle = 0
}

