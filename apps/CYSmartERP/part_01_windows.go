//go:build windows

package main

import (
	"encoding/csv"
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"runtime/debug"
	"sync"
	"sync/atomic"
	"syscall"
	"time"
	"unsafe"
)

var (
	user32   = syscall.NewLazyDLL("user32.dll")
	kernel32 = syscall.NewLazyDLL("kernel32.dll")
	shell32  = syscall.NewLazyDLL("shell32.dll")
	gdi32    = syscall.NewLazyDLL("gdi32.dll")

	pRegisterClassExW         = user32.NewProc("RegisterClassExW")
	pCreateWindowExW          = user32.NewProc("CreateWindowExW")
	pDefWindowProcW           = user32.NewProc("DefWindowProcW")
	pShowWindow               = user32.NewProc("ShowWindow")
	pUpdateWindow             = user32.NewProc("UpdateWindow")
	pGetMessageW              = user32.NewProc("GetMessageW")
	pTranslateMessage         = user32.NewProc("TranslateMessage")
	pDispatchMessageW         = user32.NewProc("DispatchMessageW")
	pPostQuitMessage          = user32.NewProc("PostQuitMessage")
	pSendMessageW             = user32.NewProc("SendMessageW")
	pSetWindowTextW           = user32.NewProc("SetWindowTextW")
	pGetWindowTextW           = user32.NewProc("GetWindowTextW")
	pGetWindowTextLenW        = user32.NewProc("GetWindowTextLengthW")
	pGetClassNameW            = user32.NewProc("GetClassNameW")
	pEnumWindows              = user32.NewProc("EnumWindows")
	pEnumChildWindows         = user32.NewProc("EnumChildWindows")
	pIsWindowVisible          = user32.NewProc("IsWindowVisible")
	pIsWindowEnabled          = user32.NewProc("IsWindowEnabled")
	pGetWindowRect            = user32.NewProc("GetWindowRect")
	pGetClientRect            = user32.NewProc("GetClientRect")
	pScreenToClient           = user32.NewProc("ScreenToClient")
	pGetParent                = user32.NewProc("GetParent")
	pSetForeground            = user32.NewProc("SetForegroundWindow")
	pBringWindowToTop         = user32.NewProc("BringWindowToTop")
	pIsIconic                 = user32.NewProc("IsIconic")
	pSetWindowPos             = user32.NewProc("SetWindowPos")
	pMessageBoxW              = user32.NewProc("MessageBoxW")
	pGetForeground            = user32.NewProc("GetForegroundWindow")
	pGetWindowThreadProcessId = user32.NewProc("GetWindowThreadProcessId")
	pGetWindowLongW           = user32.NewProc("GetWindowLongW")
	pGetGUIThreadInfo         = user32.NewProc("GetGUIThreadInfo")
	pGetCursorPos             = user32.NewProc("GetCursorPos")
	pGetAsyncKeyState         = user32.NewProc("GetAsyncKeyState")
	pSetCursorPos             = user32.NewProc("SetCursorPos")
	pMouseEvent               = user32.NewProc("mouse_event")
	pSendInput                = user32.NewProc("SendInput")
	pGetStockObject           = gdi32.NewProc("GetStockObject")
	pShellExecuteW            = shell32.NewProc("ShellExecuteW")
	pGetModuleHandleW         = kernel32.NewProc("GetModuleHandleW")
)

const (
	WS_OVERLAPPEDWINDOW  = 0x00CF0000
	WS_VISIBLE           = 0x10000000
	WS_CHILD             = 0x40000000
	WS_BORDER            = 0x00800000
	WS_TABSTOP           = 0x00010000
	WS_VSCROLL           = 0x00200000
	ES_AUTOHSCROLL       = 0x0080
	ES_MULTILINE         = 0x0004
	ES_AUTOVSCROLL       = 0x0040
	ES_READONLY          = 0x0800
	GWL_STYLE            = -16
	BS_PUSHBUTTON        = 0x00000000
	BS_AUTOCHECKBOX      = 0x00000003
	BS_GROUPBOX          = 0x00000007
	SS_LEFT              = 0x00000000
	SW_SHOW              = 5
	SW_RESTORE           = 9
	SWP_NOSIZE           = 0x0001
	SWP_NOMOVE           = 0x0002
	SWP_SHOWWINDOW       = 0x0040
	HWND_TOP             = 0
	MB_OK                = 0x00000000
	MB_ICONINFORMATION   = 0x00000040
	WM_DESTROY           = 0x0002
	WM_COMMAND           = 0x0111
	WM_SETFONT           = 0x0030
	WM_SETTEXT           = 0x000C
	WM_CHAR              = 0x0102
	EM_SETSEL            = 0x00B1
	BM_GETCHECK          = 0x00F0
	BM_SETCHECK          = 0x00F1
	BST_CHECKED          = 1
	BST_UNCHECKED        = 0
	CB_SELECTSTRING      = 0x014D
	TCM_FIRST            = 0x1300
	TCM_GETITEMCOUNT     = TCM_FIRST + 4
	TCM_SETCURSEL        = TCM_FIRST + 12
	TCM_GETITEMRECT      = TCM_FIRST + 10
	WM_LBUTTONDOWN       = 0x0201
	WM_LBUTTONUP         = 0x0202
	MK_LBUTTON           = 0x0001
	MOUSEEVENTF_LEFTDOWN = 0x0002
	MOUSEEVENTF_LEFTUP   = 0x0004
	INPUT_KEYBOARD       = 1
	KEYEVENTF_KEYUP      = 0x0002
	KEYEVENTF_UNICODE    = 0x0004
	VK_MENU              = 0x12
	VK_RETURN            = 0x0D
	VK_ESCAPE            = 0x1B
	VK_TAB               = 0x09
	VK_F2                = 0x71
	VK_F4                = 0x73
	VK_HOME              = 0x24
	VK_END               = 0x23
	VK_BACK              = 0x08
	VK_DOWN              = 0x28
	DEFAULT_GUI_FONT     = 17
	COLOR_WINDOW         = 5
)

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

type MSG struct {
	Hwnd    uintptr
	Message uint32
	WParam  uintptr
	LParam  uintptr
	Time    uint32
	Pt      POINT
}

type POINT struct{ X, Y int32 }

type RECT struct{ Left, Top, Right, Bottom int32 }

type GUITHREADINFO struct {
	CbSize        uint32
	Flags         uint32
	HwndActive    uintptr
	HwndFocus     uintptr
	HwndCapture   uintptr
	HwndMenuOwner uintptr
	HwndMoveSize  uintptr
	HwndCaret     uintptr
	RcCaret       RECT
}

type KEYBDINPUT struct {
	WVk         uint16
	WScan       uint16
	DwFlags     uint32
	Time        uint32
	_           uint32
	DwExtraInfo uintptr
}

type INPUT struct {
	Type uint32
	_    uint32
	Ki   KEYBDINPUT
	_pad [8]byte
}

type Field struct {
	Key       string
	Group     string
	Label     string
	Aliases   []string
	Kind      string // text / combo / bool
	ApplyHwnd uintptr
	ValueHwnd uintptr
	Default   string
	Row       int
	Col       int
}

type ControlInfo struct {
	Hwnd    uintptr
	Parent  uintptr
	Class   string
	Text    string
	Rect    RECT
	Visible bool
	Enabled bool
}

type ComboSetting struct {
	Selected string   `json:"selected"`
	Options  []string `json:"options"`
}

type AppSettings struct {
	Version int                     `json:"version"`
	Combos  map[string]ComboSetting `json:"combos"`
}

type ERPMode string

const (
	ERPModeBrowse  ERPMode = "BROWSE"
	ERPModeInput   ERPMode = "INPUT"
	ERPModeUnknown ERPMode = "UNKNOWN"
)

var (
	mainHwnd          uintptr
	statusHwnd        uintptr
	logHwnd           uintptr
	focusText         uintptr
	fields            []*Field
	fieldByID                = map[uint16]*Field{}
	nextID            uint16 = 2000
	logMu             sync.Mutex
	logFile           *os.File
	logPath           string
	errorPath         string
	settingsPath      string
	settings          AppSettings
	automationRunning atomic.Bool
	stopRequested     atomic.Bool
	stopLogged        atomic.Bool
)

func wstr(s string) *uint16 { p, _ := syscall.UTF16PtrFromString(s); return p }

func main() {

	runtime.LockOSThread()
	defer runtime.UnlockOSThread()
	initLogging()
	initSettings()
	defer func() {
		r := recover()
		if r != nil {
			logf("FATAL", "panic: %v", r)
			_ = os.WriteFile(filepath.Join(filepath.Dir(logPath), "crash_"+time.Now().Format("20060102_150405")+".txt"), []byte(fmt.Sprintf("panic: %v\n\n%s", r, debug.Stack())), 0644)
		} else {
			logf("INFO", "CY SMART ERP Prototype normal exit")
		}
		if logFile != nil {
			logFile.Close()
		}
	}()
	logf("INFO", "CY SMART ERP Prototype V0.0.10 starting")
	startEscapeWatcher()
	runGUI()
}

func initLogging() {
	exe, _ := os.Executable()
	base := filepath.Dir(exe)
	dir := filepath.Join(base, "logs")
	if err := os.MkdirAll(dir, 0755); err != nil {
		dir = os.TempDir()
	}
	logPath = filepath.Join(dir, "CYSmartERPPrototype_"+time.Now().Format("20060102")+".log")
	errorPath = filepath.Join(dir, "errors_"+time.Now().Format("20060102")+".csv")
	f, err := os.OpenFile(logPath, os.O_CREATE|os.O_APPEND|os.O_WRONLY, 0644)
	if err == nil {
		logFile = f
	}
}

func logf(level, format string, args ...any) {
	line := fmt.Sprintf("%s [%s] %s", time.Now().Format("2006-01-02 15:04:05.000"), level, fmt.Sprintf(format, args...))
	logMu.Lock()
	defer logMu.Unlock()
	if logFile != nil {
		fmt.Fprintln(logFile, line)
		logFile.Sync()
	}
	if logHwnd != 0 {
		old := getWindowText(logHwnd)
		if len(old) > 18000 {
			old = old[len(old)-12000:]
		}
		setWindowText(logHwnd, old+line+"\r\n")
	}
}

func logError(group, field, code, detail string) {
	logf("ERROR", "%s/%s %s: %s", group, field, code, detail)
	f, err := os.OpenFile(errorPath, os.O_CREATE|os.O_APPEND|os.O_WRONLY, 0644)
	if err != nil {
		return
	}
	defer f.Close()
	w := csv.NewWriter(f)
	_ = w.Write([]string{time.Now().Format(time.RFC3339), group, field, code, detail})
	w.Flush()
}

func runGUI() {
	hInst, _, _ := pGetModuleHandleW.Call(0)
	className := wstr("CYSmartERPPrototypeWindow")
	wc := WNDCLASSEX{CbSize: uint32(unsafe.Sizeof(WNDCLASSEX{})), LpfnWndProc: syscall.NewCallback(wndProc), HInstance: hInst, HbrBackground: COLOR_WINDOW + 1, LpszClassName: className}
	pRegisterClassExW.Call(uintptr(unsafe.Pointer(&wc)))
	hwnd, _, _ := pCreateWindowExW.Call(0, uintptr(unsafe.Pointer(className)), uintptr(unsafe.Pointer(wstr("CY SMART ERP 自動打單原型 V0.0.10 — Esc 緊急停止／下拉安全診斷（不儲存）"))), WS_OVERLAPPEDWINDOW|WS_VISIBLE, 10, 10, 1580, 800, 0, 0, hInst, 0)
	mainHwnd = hwnd
	createControls(hwnd)
	pShowWindow.Call(hwnd, SW_SHOW)
	pUpdateWindow.Call(hwnd)
	var msg MSG
	for {
		r, _, _ := pGetMessageW.Call(uintptr(unsafe.Pointer(&msg)), 0, 0, 0)
		if int32(r) <= 0 {
			break
		}
		pTranslateMessage.Call(uintptr(unsafe.Pointer(&msg)))
		pDispatchMessageW.Call(uintptr(unsafe.Pointer(&msg)))
	}
}

func wndProc(hwnd uintptr, msg uint32, wParam, lParam uintptr) uintptr {
	switch msg {
	case WM_COMMAND:
		id := uint16(wParam & 0xffff)
		safeHandleCommand(id)
		return 0
	case WM_DESTROY:
		pPostQuitMessage.Call(0)
		return 0
	}
	r, _, _ := pDefWindowProcW.Call(hwnd, uintptr(msg), wParam, lParam)
	return r
}

func safeHandleCommand(id uint16) {
	defer func() {
		if r := recover(); r != nil {
			logf("ERROR", "command %d panic: %v", id, r)
			crashPath := filepath.Join(filepath.Dir(logPath), "command-crash_"+time.Now().Format("20060102_150405")+".txt")
			_ = os.WriteFile(crashPath, []byte(fmt.Sprintf("command=%d\npanic=%v\n\n%s", id, r, debug.Stack())), 0644)
			setStatus("原型發生錯誤，已寫入 command-crash 紀錄；程式會繼續保留")
		}
	}()
	handleCommand(id)
}

func createCtrl(class, text string, style uint32, x, y, w, h int32, parent uintptr, id uint16) uintptr {
	hwnd, _, _ := pCreateWindowExW.Call(0, uintptr(unsafe.Pointer(wstr(class))), uintptr(unsafe.Pointer(wstr(text))), uintptr(style), uintptr(x), uintptr(y), uintptr(w), uintptr(h), parent, uintptr(id), 0, 0)
	font, _, _ := pGetStockObject.Call(DEFAULT_GUI_FONT)
	pSendMessageW.Call(hwnd, WM_SETFONT, font, 1)
	return hwnd
}
