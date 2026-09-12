//go:build windows

package singleinstance

import "unsafe"

func unsafePointer(value *uint16) unsafe.Pointer { return unsafe.Pointer(value) }

