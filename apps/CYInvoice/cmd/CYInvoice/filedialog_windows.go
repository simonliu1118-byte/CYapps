//go:build windows

package main

import (
	"syscall"
	"unicode/utf16"
	"unsafe"
)

var (
	comdlg32 = syscall.NewLazyDLL("comdlg32.dll")
	procGetOpenFileNameW = comdlg32.NewProc("GetOpenFileNameW")
)

type openFileName struct {
	StructSize uint32
	Owner, Instance uintptr
	Filter, CustomFilter *uint16
	MaxCustomFilter, FilterIndex uint32
	File *uint16
	MaxFile uint32
	FileTitle *uint16
	MaxFileTitle uint32
	InitialDir, Title *uint16
	Flags uint32
	FileOffset, FileExtension uint16
	DefaultExtension *uint16
	CustomData, Hook uintptr
	TemplateName *uint16
	ReservedPtr uintptr
	Reserved, FlagsEx uint32
}

func chooseExcelFile() string {
	buffer := make([]uint16, 4096)
	filter := utf16.Encode([]rune("Excel 檔案 (*.xls;*.xlsx)\x00*.xls;*.xlsx\x00Excel 97-2003 (*.xls)\x00*.xls\x00Excel 活頁簿 (*.xlsx)\x00*.xlsx\x00所有檔案 (*.*)\x00*.*\x00\x00"))
	title := mustUTF16Ptr("選擇 Excel 檔案")
	configuration := openFileName{
		Owner: mainWindow, Filter: &filter[0], FilterIndex: 1,
		File: &buffer[0], MaxFile: uint32(len(buffer)), Title: title,
		Flags: 0x00080000 | 0x00001000 | 0x00000800,
	}
	configuration.StructSize = uint32(unsafe.Sizeof(configuration))
	result, _, _ := procGetOpenFileNameW.Call(uintptr(unsafe.Pointer(&configuration)))
	if result == 0 { return "" }
	return syscall.UTF16ToString(buffer)
}
