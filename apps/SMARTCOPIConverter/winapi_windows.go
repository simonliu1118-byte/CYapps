//go:build windows

package main

import (
	"syscall"
	"unsafe"
)

const (
	WS_OVERLAPPEDWINDOW = 0x00CF0000
	WS_VISIBLE          = 0x10000000
	WS_CHILD            = 0x40000000
	WS_BORDER           = 0x00800000
	WS_TABSTOP          = 0x00010000

	BS_PUSHBUTTON   = 0x00000000
	BS_AUTOCHECKBOX = 0x00000003
	SS_LEFT         = 0x00000000

	LVS_REPORT           = 0x0001
	LVS_SINGLESEL        = 0x0004
	LVS_SHOWSELALWAYS    = 0x0008
	LVS_NOCOLUMNHEADER   = 0x4000
	LVS_EX_GRIDLINES     = 0x00000001
	LVS_EX_FULLROWSELECT = 0x00000020

	SW_SHOW = 5

	WM_CREATE         = 0x0001
	WM_DESTROY        = 0x0002
	WM_CLOSE          = 0x0010
	WM_COMMAND        = 0x0111
	WM_NOTIFY         = 0x004E
	WM_SETFONT        = 0x0030
	WM_CTLCOLORBTN    = 0x0135
	WM_CTLCOLORSTATIC = 0x0138
	WM_APP            = 0x8000
	WM_UI_EVENT       = WM_APP + 1
	WM_FIRST_RUN      = WM_APP + 2

	BM_GETCHECK = 0x00F0
	BST_CHECKED = 1

	MB_OK              = 0x00000000
	MB_ICONINFORMATION = 0x00000040
	MB_ICONWARNING     = 0x00000030
	MB_ICONERROR       = 0x00000010

	OFN_ALLOWMULTISELECT = 0x00000200
	OFN_EXPLORER         = 0x00080000
	OFN_FILEMUSTEXIST    = 0x00001000
	OFN_PATHMUSTEXIST    = 0x00000800

	PROGRESS_CLASS = "msctls_progress32"
	PBM_SETRANGE32 = 0x0406
	PBM_SETPOS     = 0x0402

	LVM_FIRST                    = 0x1000
	LVM_DELETEALLITEMS           = LVM_FIRST + 9
	LVM_INSERTITEMW              = LVM_FIRST + 77
	LVM_SETITEMW                 = LVM_FIRST + 76
	LVM_INSERTCOLUMNW            = LVM_FIRST + 97
	LVM_SETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 54

	LVIF_TEXT   = 0x0001
	LVCF_FMT    = 0x0001
	LVCF_WIDTH  = 0x0002
	LVCF_TEXT   = 0x0004
	LVCFMT_LEFT = 0
	NM_CLICK    = -2

	COINIT_APARTMENTTHREADED = 0x2
	CLSCTX_INPROC_SERVER     = 0x1
	FOS_PICKFOLDERS          = 0x00000020
	FOS_FORCEFILESYSTEM      = 0x00000040
	FOS_PATHMUSTEXIST        = 0x00000800
	SIGDN_FILESYSPATH        = 0x80058000

	COLOR_WINDOW = 5
	TRANSPARENT  = 1
)

const (
	ID_SELECT  = 1001
	ID_CLEAR   = 1002
	ID_START   = 1003
	ID_OPEN    = 1004
	ID_HELP    = 1005
	ID_LOG     = 1006
	ID_FAIL    = 1007
	ID_CHANGE  = 1008
	ID_FILES   = 1010
	ID_HISTORY = 1011
)

type WNDCLASSEXW struct {
	CbSize        uint32
	Style         uint32
	LpfnWndProc   uintptr
	CbClsExtra    int32
	CbWndExtra    int32
	HInstance     uintptr
	HIcon         uintptr
	HCursor       uintptr
	HbrBackground uintptr
	LpszMenuName  *uint16
	LpszClassName *uint16
	HIconSm       uintptr
}

type POINT struct{ X, Y int32 }
type MSG struct {
	Hwnd     uintptr
	Message  uint32
	WParam   uintptr
	LParam   uintptr
	Time     uint32
	Pt       POINT
	LPrivate uint32
}

type OPENFILENAMEW struct {
	LStructSize       uint32
	HwndOwner         uintptr
	HInstance         uintptr
	LpstrFilter       *uint16
	LpstrCustomFilter *uint16
	NMaxCustFilter    uint32
	NFilterIndex      uint32
	LpstrFile         *uint16
	NMaxFile          uint32
	LpstrFileTitle    *uint16
	NMaxFileTitle     uint32
	LpstrInitialDir   *uint16
	LpstrTitle        *uint16
	Flags             uint32
	NFileOffset       uint16
	NFileExtension    uint16
	LpstrDefExt       *uint16
	LCustData         uintptr
	LpfnHook          uintptr
	LpTemplateName    *uint16
	PvReserved        uintptr
	DwReserved        uint32
	FlagsEx           uint32
}

type NMHDR struct {
	HwndFrom uintptr
	IDFrom   uintptr
	Code     int32
	_        uint32
}

type NMITEMACTIVATE struct {
	Hdr       NMHDR
	IItem     int32
	ISubItem  int32
	UNewState uint32
	UOldState uint32
	UChanged  uint32
	PtAction  POINT
	LParam    uintptr
	UKeyFlags uint32
}

type LVCOLUMNW struct {
	Mask       uint32
	Fmt        int32
	Cx         int32
	PszText    *uint16
	CchTextMax int32
	ISubItem   int32
	IImage     int32
	IOrder     int32
	CxMin      int32
	CxDefault  int32
	CxIdeal    int32
}

type LVITEMW struct {
	Mask       uint32
	IItem      int32
	ISubItem   int32
	State      uint32
	StateMask  uint32
	PszText    *uint16
	CchTextMax int32
	IImage     int32
	LParam     uintptr
	IIndent    int32
	IGroupId   int32
	CColumns   uint32
	PuColumns  *uint32
	PiColFmt   *int32
	IGroup     int32
}

type GUID struct {
	Data1 uint32
	Data2 uint16
	Data3 uint16
	Data4 [8]byte
}

var (
	user32   = syscall.NewLazyDLL("user32.dll")
	kernel32 = syscall.NewLazyDLL("kernel32.dll")
	comdlg32 = syscall.NewLazyDLL("comdlg32.dll")
	shell32  = syscall.NewLazyDLL("shell32.dll")
	gdi32    = syscall.NewLazyDLL("gdi32.dll")
	comctl32 = syscall.NewLazyDLL("comctl32.dll")
	ole32    = syscall.NewLazyDLL("ole32.dll")

	pRegisterClassExW = user32.NewProc("RegisterClassExW")
	pCreateWindowExW  = user32.NewProc("CreateWindowExW")
	pDefWindowProcW   = user32.NewProc("DefWindowProcW")
	pShowWindow       = user32.NewProc("ShowWindow")
	pUpdateWindow     = user32.NewProc("UpdateWindow")
	pGetMessageW      = user32.NewProc("GetMessageW")
	pTranslateMessage = user32.NewProc("TranslateMessage")
	pDispatchMessageW = user32.NewProc("DispatchMessageW")
	pPostQuitMessage  = user32.NewProc("PostQuitMessage")
	pPostMessageW     = user32.NewProc("PostMessageW")
	pMessageBoxW      = user32.NewProc("MessageBoxW")
	pSendMessageW     = user32.NewProc("SendMessageW")
	pSetWindowTextW   = user32.NewProc("SetWindowTextW")
	pEnableWindow     = user32.NewProc("EnableWindow")
	pLoadCursorW      = user32.NewProc("LoadCursorW")
	pLoadIconW        = user32.NewProc("LoadIconW")
	pGetSysColorBrush = user32.NewProc("GetSysColorBrush")

	pGetModuleHandleW   = kernel32.NewProc("GetModuleHandleW")
	pGetOpenFileNameW   = comdlg32.NewProc("GetOpenFileNameW")
	pShellExecuteW      = shell32.NewProc("ShellExecuteW")
	pCreateFontW        = gdi32.NewProc("CreateFontW")
	pSetBkMode          = gdi32.NewProc("SetBkMode")
	pInitCommonControls = comctl32.NewProc("InitCommonControls")

	pCoInitializeEx   = ole32.NewProc("CoInitializeEx")
	pCoUninitialize   = ole32.NewProc("CoUninitialize")
	pCoCreateInstance = ole32.NewProc("CoCreateInstance")
	pCoTaskMemFree    = ole32.NewProc("CoTaskMemFree")
)

func wstr(s string) *uint16   { p, _ := syscall.UTF16PtrFromString(s); return p }
func loword(v uintptr) uint16 { return uint16(v & 0xffff) }

func createFont(size, weight int) uintptr {
	h, _, _ := pCreateFontW.Call(
		uintptr(-size), 0, 0, 0, uintptr(weight), 0, 0, 0,
		1, 0, 0, 5, 0, uintptr(unsafe.Pointer(wstr("Microsoft JhengHei UI"))),
	)
	return h
}

func createControl(parent uintptr, cls, text string, style uintptr, x, y, w, h, id int) uintptr {
	hwnd, _, _ := pCreateWindowExW.Call(0, uintptr(unsafe.Pointer(wstr(cls))), uintptr(unsafe.Pointer(wstr(text))), style, uintptr(x), uintptr(y), uintptr(w), uintptr(h), parent, uintptr(id), 0, 0)
	if hwnd != 0 && hFont != 0 {
		pSendMessageW.Call(hwnd, WM_SETFONT, hFont, 1)
	}
	return hwnd
}
func createStatic(parent uintptr, text string, x, y, w, h int) uintptr {
	return createControl(parent, "STATIC", text, WS_CHILD|WS_VISIBLE|SS_LEFT, x, y, w, h, 0)
}
func createButton(parent uintptr, text string, id, x, y, w, h int) uintptr {
	return createControl(parent, "BUTTON", text, WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_PUSHBUTTON, x, y, w, h, id)
}
func createCheck(parent uintptr, text string, id, x, y, w, h int) uintptr {
	return createControl(parent, "BUTTON", text, WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_AUTOCHECKBOX, x, y, w, h, id)
}
func setText(hwnd uintptr, text string) {
	pSetWindowTextW.Call(hwnd, uintptr(unsafe.Pointer(wstr(text))))
}
func enable(hwnd uintptr, on bool) {
	v := uintptr(0)
	if on {
		v = 1
	}
	pEnableWindow.Call(hwnd, v)
}
func message(title, text string, flags uintptr) {
	pMessageBoxW.Call(hwndMain, uintptr(unsafe.Pointer(wstr(text))), uintptr(unsafe.Pointer(wstr(title))), flags)
}

func comCall(obj uintptr, index int, args ...uintptr) uintptr {
	vtbl := *(*uintptr)(unsafe.Pointer(obj))
	fn := *(*uintptr)(unsafe.Pointer(vtbl + uintptr(index)*unsafe.Sizeof(uintptr(0))))
	a := append([]uintptr{obj}, args...)
	r, _, _ := syscall.SyscallN(fn, a...)
	return r
}
func comRelease(obj uintptr) {
	if obj != 0 {
		comCall(obj, 2)
	}
}
