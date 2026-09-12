//go:build windows

package main

var procDeleteObject = gdi32.NewProc("DeleteObject")
