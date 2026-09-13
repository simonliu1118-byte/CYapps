//go:build windows

package main

import (
	"runtime"
	"strings"
	"syscall"
	"unsafe"
)

var version = "dev"

var (
	hwndMain     uintptr
	hwndPath     uintptr
	hwndLog      uintptr
	hwndSelect   uintptr
	hwndClear    uintptr
	hwndStart    uintptr
	hwndOpen     uintptr
	hwndHelp     uintptr
	hwndCount    uintptr
	hwndFiles    uintptr
	hwndProgress uintptr
	hwndStatus   uintptr
	hwndFail     uintptr
	hwndHistory  uintptr
	hwndChange   uintptr
	hFont        uintptr

	state      AppState
	files      []fileEntry
	failures   []string
	converting bool
	batchDone  bool

	uiEvents = make(chan batchEvent, 128)
)

func main() {
	runtime.LockOSThread()
	initStartupLog()
	defer closeStartupLog()
	diag("START version=%s", version)
	defer func() {
		if r := recover(); r != nil {
			diag("PANIC: %v", r)
			panic(r)
		}
	}()

	hr, _, _ := pCoInitializeEx.Call(0, COINIT_APARTMENTTHREADED)
	diag("CoInitializeEx hr=0x%X", uint32(hr))
	defer pCoUninitialize.Call()

	diag("loadState begin")
	state = loadState()
	diag("loadState done output=%q history=%d", state.Settings.POSOutputDir, len(state.History))

	pInitCommonControls.Call()
	hInst, _, _ := pGetModuleHandleW.Call(0)
	hCursor, _, _ := pLoadCursorW.Call(0, 32512)
	hIcon, _, _ := pLoadIconW.Call(hInst, 1)

	cls := wstr("SMARTCOPIConverterWindow")
	wc := WNDCLASSEXW{
		CbSize:        uint32(unsafe.Sizeof(WNDCLASSEXW{})),
		LpfnWndProc:   syscall.NewCallback(wndProc),
		HInstance:     hInst,
		HIcon:         hIcon,
		HCursor:       hCursor,
		HbrBackground: 6,
		LpszClassName: cls,
		HIconSm:       hIcon,
	}
	if r, _, e := pRegisterClassExW.Call(uintptr(unsafe.Pointer(&wc))); r == 0 {
		diag("RegisterClassExW failed: %v", e)
	}

	title := "SMART 銷貨單格式轉換工具"
	if version != "" && version != "dev" {
		title += " V" + version
	}
	hwndMain, _, _ = pCreateWindowExW.Call(
		0,
		uintptr(unsafe.Pointer(cls)), uintptr(unsafe.Pointer(wstr(title))),
		WS_OVERLAPPEDWINDOW|WS_VISIBLE,
		100, 80, 760, 610,
		0, 0, hInst, 0,
	)
	if hwndMain == 0 {
		diag("CreateWindowExW failed")
		return
	}
	diag("main window created hwnd=0x%X", hwndMain)
	pShowWindow.Call(hwndMain, SW_SHOW)
	pUpdateWindow.Call(hwndMain)

	if strings.TrimSpace(state.Settings.POSOutputDir) == "" {
		pPostMessageW.Call(hwndMain, WM_FIRST_RUN, 0, 0)
	}

	diag("message loop start")
	var msg MSG
	for {
		r, _, _ := pGetMessageW.Call(uintptr(unsafe.Pointer(&msg)), 0, 0, 0)
		if int32(r) <= 0 {
			break
		}
		pTranslateMessage.Call(uintptr(unsafe.Pointer(&msg)))
		pDispatchMessageW.Call(uintptr(unsafe.Pointer(&msg)))
	}
	diag("message loop end")
}
