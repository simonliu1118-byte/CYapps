//go:build windows

package main

import (
	"os"
	"sync"
	"syscall"
)

const appTitle = "三聯式發票開立計算機"

var appVersion = "dev"

const (
	WM_CREATE       = 0x0001
	WM_DESTROY      = 0x0002
	WM_SETFOCUS     = 0x0007
	WM_KILLFOCUS    = 0x0008
	WM_PAINT        = 0x000F
	WM_CLOSE        = 0x0010
	WM_COMMAND      = 0x0111
	WM_CHAR         = 0x0102
	WM_KEYDOWN      = 0x0100
	WM_PASTE        = 0x0302
	WM_SETFONT      = 0x0030
	WM_SETICON      = 0x0080
	WM_GETDLGCODE   = 0x0087
	WM_ERASEBKGND   = 0x0014
	WM_CTLCOLORBTN  = 0x0135
	WM_CTLCOLOREDIT = 0x0133
	WM_APP          = 0x8000
	WM_APP_READY    = WM_APP + 1
	WM_APP_RECALC   = WM_APP + 2
	WM_APP_FOCUS    = WM_APP + 3

	VK_TAB    = 0x09
	VK_RETURN = 0x0D

	WS_OVERLAPPED    = 0x00000000
	WS_CAPTION       = 0x00C00000
	WS_SYSMENU       = 0x00080000
	WS_MINIMIZEBOX   = 0x00020000
	WS_VISIBLE       = 0x10000000
	WS_CHILD         = 0x40000000
	WS_BORDER        = 0x00800000
	WS_TABSTOP       = 0x00010000
	ES_AUTOHSCROLL   = 0x0080
	BS_PUSHBUTTON    = 0x00000000
	WS_EX_CLIENTEDGE = 0x00000200

	SW_SHOW = 5

	CS_HREDRAW = 0x0002
	CS_VREDRAW = 0x0001

	IDC_ARROW  = 32512
	IDI_APP    = 2
	ICON_SMALL = 0
	ICON_BIG   = 1

	COLOR_WINDOW = 5

	DT_LEFT         = 0x00000000
	DT_CENTER       = 0x00000001
	DT_RIGHT        = 0x00000002
	DT_VCENTER      = 0x00000004
	DT_SINGLELINE   = 0x00000020
	DT_WORDBREAK    = 0x00000010
	DT_END_ELLIPSIS = 0x00008000

	TRANSPARENT = 1

	PS_SOLID = 0

	FW_NORMAL   = 400
	FW_SEMIBOLD = 600
	FW_BOLD     = 700

	EN_KILLFOCUS = 0x0200
	BN_CLICKED   = 0

	EM_SETLIMITTEXT = 0x00C5
	EM_GETSEL       = 0x00B0

	MB_OK          = 0x00000000
	MB_ICONWARNING = 0x00000030
	MB_ICONERROR   = 0x00000010

	CF_UNICODETEXT = 13

	ID_CLEAR    = 2002
	ID_DISCOUNT = 1100

	DLGC_WANTCHARS   = 0x0080
	DLGC_WANTTAB     = 0x0002
	DLGC_WANTALLKEYS = 0x0004
)

type POINT struct{ X, Y int32 }
type RECT struct{ Left, Top, Right, Bottom int32 }
type SIZE struct{ Cx, Cy int32 }
type PAINTSTRUCT struct {
	Hdc         uintptr
	FErase      int32
	RcPaint     RECT
	FRestore    int32
	FIncUpdate  int32
	RgbReserved [32]byte
}
type MSG struct {
	Hwnd     uintptr
	Message  uint32
	WParam   uintptr
	LParam   uintptr
	Time     uint32
	Pt       POINT
	LPrivate uint32
}
type WNDCLASSEX struct {
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

type fieldKind int

const (
	fieldName fieldKind = iota
	fieldQty
	fieldMoney
	fieldDiscount
)

type editMeta struct {
	kind  fieldKind
	order int
}

type rowInput struct {
	NameText   string
	QtyText    string
	PriceText  string
	Qty        int64
	PriceCents int64
	Active     bool
}

type rowResult struct {
	DisplayName         string
	Qty                 int64
	NetUnitScaled       int64
	NetUnitDecimals     int
	DisplayUnitReady    bool
	GrossUnitCents      int64
	GrossLineCents      int64
	InitialNetCents     int64
	AdjustedNetCents    int64
	TailAdjustmentCents int64
	Active              bool
	ValidForTail        bool
}

type calcResult struct {
	Rows                [5]rowResult
	DiscountCents       int64
	NetDiscountCents    int64
	GrossItemsCents     int64
	GrossTotalCents     int64
	TaxDollars          int64
	TaxCents            int64
	NetSalesCents       int64
	InitialNetSumCents  int64
	TailCents           int64
	TailRows            []int
	HasData             bool
	Valid               bool
	ErrorKind           string
	ErrorMessage        string
	VerificationDetails string
}

var (
	user32   = syscall.NewLazyDLL("user32.dll")
	gdi32    = syscall.NewLazyDLL("gdi32.dll")
	kernel32 = syscall.NewLazyDLL("kernel32.dll")

	procRegisterClassExW     = user32.NewProc("RegisterClassExW")
	procCreateWindowExW      = user32.NewProc("CreateWindowExW")
	procDefWindowProcW       = user32.NewProc("DefWindowProcW")
	procShowWindow           = user32.NewProc("ShowWindow")
	procUpdateWindow         = user32.NewProc("UpdateWindow")
	procGetMessageW          = user32.NewProc("GetMessageW")
	procTranslateMessage     = user32.NewProc("TranslateMessage")
	procDispatchMessageW     = user32.NewProc("DispatchMessageW")
	procPostQuitMessage      = user32.NewProc("PostQuitMessage")
	procBeginPaint           = user32.NewProc("BeginPaint")
	procEndPaint             = user32.NewProc("EndPaint")
	procGetClientRect        = user32.NewProc("GetClientRect")
	procInvalidateRect       = user32.NewProc("InvalidateRect")
	procLoadCursorW          = user32.NewProc("LoadCursorW")
	procLoadIconW            = user32.NewProc("LoadIconW")
	procSendMessageW         = user32.NewProc("SendMessageW")
	procGetWindowTextLengthW = user32.NewProc("GetWindowTextLengthW")
	procGetWindowTextW       = user32.NewProc("GetWindowTextW")
	procSetWindowTextW       = user32.NewProc("SetWindowTextW")
	procGetDlgCtrlID         = user32.NewProc("GetDlgCtrlID")
	procSetFocus             = user32.NewProc("SetFocus")
	procGetFocus             = user32.NewProc("GetFocus")
	procSetWindowLongPtrW    = user32.NewProc("SetWindowLongPtrW")
	procCallWindowProcW      = user32.NewProc("CallWindowProcW")
	procIsDialogMessageW     = user32.NewProc("IsDialogMessageW")
	procPostMessageW         = user32.NewProc("PostMessageW")
	procMessageBoxW          = user32.NewProc("MessageBoxW")
	procGetSystemMetrics     = user32.NewProc("GetSystemMetrics")
	procOpenClipboard        = user32.NewProc("OpenClipboard")
	procCloseClipboard       = user32.NewProc("CloseClipboard")
	procGetClipboardData     = user32.NewProc("GetClipboardData")
	procMessageBeep          = user32.NewProc("MessageBeep")

	procCreateSolidBrush      = gdi32.NewProc("CreateSolidBrush")
	procCreatePen             = gdi32.NewProc("CreatePen")
	procSelectObject          = gdi32.NewProc("SelectObject")
	procDeleteObject          = gdi32.NewProc("DeleteObject")
	procMoveToEx              = gdi32.NewProc("MoveToEx")
	procLineTo                = gdi32.NewProc("LineTo")
	procRectangle             = gdi32.NewProc("Rectangle")
	procRoundRect             = gdi32.NewProc("RoundRect")
	procSetBkMode             = gdi32.NewProc("SetBkMode")
	procSetTextColor          = gdi32.NewProc("SetTextColor")
	procCreateFontW           = gdi32.NewProc("CreateFontW")
	procGetTextExtentPoint32W = gdi32.NewProc("GetTextExtentPoint32W")

	procFillRect  = user32.NewProc("FillRect")
	procDrawTextW = user32.NewProc("DrawTextW")

	procGetModuleHandleW   = kernel32.NewProc("GetModuleHandleW")
	procGetCurrentThreadId = kernel32.NewProc("GetCurrentThreadId")
	procGetModuleFileNameW = kernel32.NewProc("GetModuleFileNameW")
	procGlobalLock         = kernel32.NewProc("GlobalLock")
	procGlobalUnlock       = kernel32.NewProc("GlobalUnlock")
	procGlobalSize         = kernel32.NewProc("GlobalSize")

	mainHwnd     uintptr
	hInstance    uintptr
	mainBrush    uintptr
	panelBrush   uintptr
	invoiceBrush uintptr
	editBrush    uintptr
	headerBrush  uintptr
	accentBrush  uintptr
	errorBrush   uintptr

	fontTitle             uintptr
	fontSection           uintptr
	fontNormal            uintptr
	fontSmall             uintptr
	fontInvoiceTitle      uintptr
	fontInvoiceHeader     uintptr
	fontInvoiceText       uintptr
	fontInvoiceSmall      uintptr
	fontInvoiceDigit      uintptr
	fontInvoiceAmountUnit uintptr

	nameEdits    [5]uintptr
	qtyEdits     [5]uintptr
	priceEdits   [5]uintptr
	discountEdit uintptr
	buttonClear  uintptr

	editMetas        = map[uintptr]editMeta{}
	orderedEdits     []uintptr
	originalEditProc uintptr
	editCallback     uintptr

	currentResult  calcResult
	lastWarningKey string

	appReady          bool
	pendingRecalc     bool
	pendingFocus      bool
	inRecalc          bool
	suppressKillFocus bool
	pendingFocusOrder = -1

	traceMu        sync.Mutex
	traceFile      *os.File
	tracePath      string
	traceSequence  uint64
	traceLineCount int
	traceNormal    bool
)
