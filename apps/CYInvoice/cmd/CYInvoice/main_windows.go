//go:build windows

package main

import (
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"runtime/debug"
	"syscall"
	"unsafe"

	"cyinvoice/internal/appdata"
	"cyinvoice/internal/applog"
	"cyinvoice/internal/securestore"
	"cyinvoice/internal/singleinstance"
	"cyinvoice/internal/version"
)

const (
	className = "CYInvoiceMainWindow"
	csHRedraw = 0x0002
	csVRedraw = 0x0001
	wsOverlappedWindow = 0x00CF0000
	wsVisible = 0x10000000
	cwUseDefault = 0x80000000
	wmCreate = 0x0001
	wmDestroy = 0x0002
	wmCommand = 0x0111
	wmTimer = 0x0113
	wmInitialSetup = 0x8001
	wmAPIHealthResult = 0x8002
	wmBuyerLookupResult = 0x8003
	wmManualIssueLookupResult = 0x8004
	wmManualIssueResult = 0x8005
	wmRecordsRefreshResult = 0x8006
	appIconResourceID = 2
)

var (
	user32 = syscall.NewLazyDLL("user32.dll")
	kernel32 = syscall.NewLazyDLL("kernel32.dll")
	gdi32 = syscall.NewLazyDLL("gdi32.dll")
	procRegisterClassExW = user32.NewProc("RegisterClassExW")
	procCreateWindowExW = user32.NewProc("CreateWindowExW")
	procDefWindowProcW = user32.NewProc("DefWindowProcW")
	procShowWindow = user32.NewProc("ShowWindow")
	procUpdateWindow = user32.NewProc("UpdateWindow")
	procGetMessageW = user32.NewProc("GetMessageW")
	procTranslateMessage = user32.NewProc("TranslateMessage")
	procDispatchMessageW = user32.NewProc("DispatchMessageW")
	procIsDialogMessageW = user32.NewProc("IsDialogMessageW")
	procPostQuitMessage = user32.NewProc("PostQuitMessage")
	procPostMessageW = user32.NewProc("PostMessageW")
	procDestroyWindow = user32.NewProc("DestroyWindow")
	procIsWindow = user32.NewProc("IsWindow")
	procSetFocus = user32.NewProc("SetFocus")
	procLoadCursorW = user32.NewProc("LoadCursorW")
	procLoadIconW = user32.NewProc("LoadIconW")
	procMessageBoxW = user32.NewProc("MessageBoxW")
	procSetProcessDPIAware = user32.NewProc("SetProcessDPIAware")
	procSendMessageW = user32.NewProc("SendMessageW")
	procSetWindowTextW = user32.NewProc("SetWindowTextW")
	procFindWindowW = user32.NewProc("FindWindowW")
	procSetForegroundWindow = user32.NewProc("SetForegroundWindow")
	procIsIconic = user32.NewProc("IsIconic")
	procGetWindowTextW = user32.NewProc("GetWindowTextW")
	procGetWindowTextLengthW = user32.NewProc("GetWindowTextLengthW")
	procEnableWindow = user32.NewProc("EnableWindow")
	procSetTimer = user32.NewProc("SetTimer")
	procKillTimer = user32.NewProc("KillTimer")
	procGetStockObject = gdi32.NewProc("GetStockObject")
	procGetModuleHandleW = kernel32.NewProc("GetModuleHandleW")

	appLogger *applog.Logger
	appRepository *appdata.Repository
	mainWindow uintptr
)

type point struct{ X, Y int32 }
type message struct {
	Window uintptr
	Message uint32
	WParam, LParam uintptr
	Time uint32
	Point point
	Private uint32
}
type windowClassEx struct {
	Size, Style uint32
	WindowProc uintptr
	ClsExtra, WndExtra int32
	Instance, Icon, Cursor, Background uintptr
	MenuName, ClassName *uint16
	IconSmall uintptr
}

func main() {
	runtime.LockOSThread()
	instanceLock, acquired, lockErr := singleinstance.Acquire("Chihyuan.CYInvoice")
	if lockErr != nil {
		showError("CYInvoice 無法建立單一執行鎖：" + lockErr.Error())
		return
	}
	if !acquired {
		activateExistingWindow()
		return
	}
	defer instanceLock.Close()
	baseDir, err := executableDir()
	if err == nil {
		appLogger, err = applog.New(baseDir)
	}
	if err != nil {
		showError("CYInvoice 無法建立 LOG：" + err.Error())
		return
	}
	defer appLogger.Close()
	appRepository, err = appdata.Open(baseDir, securestore.New())
	if err != nil {
		appLogger.Errorf("initialize local data: %v", err)
		showError("CYInvoice 無法讀取本機資料：" + err.Error())
		return
	}
	defer func() {
		if recovered := recover(); recovered != nil {
			appLogger.Errorf("STARTUP PANIC: %v\n%s", recovered, debug.Stack())
			showError(fmt.Sprintf("CYInvoice 發生未預期錯誤：\n%v", recovered))
		}
	}()
	appLogger.Infof("CYInvoice %s 啟動 commit=%s", version.Display(), version.Commit)
	if err := run(); err != nil {
		appLogger.Errorf("startup failed: %v", err)
		showError("CYInvoice 無法啟動：" + err.Error())
		return
	}
	appLogger.Infof("CYInvoice 正常結束")
}

func activateExistingWindow() {
	name := mustUTF16Ptr(className)
	window, _, _ := procFindWindowW.Call(uintptr(unsafe.Pointer(name)), 0)
	if window == 0 {
		showError("CYInvoice 已經在執行中。")
		return
	}
	iconic, _, _ := procIsIconic.Call(window)
	if iconic != 0 { procShowWindow.Call(window, 9) }
	procSetForegroundWindow.Call(window)
}

func executableDir() (string, error) {
	executable, err := os.Executable()
	if err != nil {
		return "", fmt.Errorf("resolve executable path: %w", err)
	}
	return filepath.Dir(executable), nil
}

func run() error {
	procSetProcessDPIAware.Call()
	initNativeUI()
	instance, _, callErr := procGetModuleHandleW.Call(0)
	if instance == 0 {
		return fmt.Errorf("GetModuleHandleW: %v", callErr)
	}
	icon := loadApplicationIcon(instance)
	cursor, _, _ := procLoadCursorW.Call(0, 32512)
	name := mustUTF16Ptr(className)
	class := windowClassEx{
		Size: uint32(unsafe.Sizeof(windowClassEx{})),
		Style: csHRedraw | csVRedraw,
		WindowProc: syscall.NewCallback(windowProc),
		Instance: instance,
		Icon: icon,
		Cursor: cursor,
		Background: 16,
		ClassName: name,
		IconSmall: icon,
	}
	if registered, _, err := procRegisterClassExW.Call(uintptr(unsafe.Pointer(&class))); registered == 0 {
		return fmt.Errorf("RegisterClassExW: %v", err)
	}
	title := mustUTF16Ptr(version.WindowTitle())
	mainWindow, _, callErr = procCreateWindowExW.Call(
		0, uintptr(unsafe.Pointer(name)), uintptr(unsafe.Pointer(title)),
		wsOverlappedWindow|wsVisible, cwUseDefault, cwUseDefault,
		mainWindowInitialWidth, mainWindowInitialHeight, 0, 0, instance, 0,
	)
	if mainWindow == 0 {
		return fmt.Errorf("CreateWindowExW: %v", callErr)
	}
	procShowWindow.Call(mainWindow, 1)
	procUpdateWindow.Call(mainWindow)
	procPostMessageW.Call(mainWindow, wmInitialSetup, 0, 0)
	var msg message
	for {
		result, _, err := procGetMessageW.Call(uintptr(unsafe.Pointer(&msg)), 0, 0, 0)
		if int32(result) == -1 {
			return fmt.Errorf("GetMessageW: %v", err)
		}
		if result == 0 {
			return nil
		}

		// Tab/Shift+Tab use the standard Win32 dialog-navigation rules across
		// ordinary controls. Enter remains explicit so it can never trigger the
		// invoice default button merely because focus is inside an edit control.
		// Product in-place edits handle Tab themselves to follow the same cell
		// sequence as Enter.
		if msg.Message == wmKeyDown && msg.WParam == vkReturn {
			procTranslateMessage.Call(uintptr(unsafe.Pointer(&msg)))
			procDispatchMessageW.Call(uintptr(unsafe.Pointer(&msg)))
			continue
		}
		if productCellEditor != 0 && msg.Window == productCellEditor && msg.Message == wmKeyDown && (msg.WParam == vkTab || msg.WParam == vkEscape) {
			procTranslateMessage.Call(uintptr(unsafe.Pointer(&msg)))
			procDispatchMessageW.Call(uintptr(unsafe.Pointer(&msg)))
			continue
		}
		if mainWindow != 0 {
			if handled, _, _ := procIsDialogMessageW.Call(mainWindow, uintptr(unsafe.Pointer(&msg))); handled != 0 {
				continue
			}
		}
		procTranslateMessage.Call(uintptr(unsafe.Pointer(&msg)))
		procDispatchMessageW.Call(uintptr(unsafe.Pointer(&msg)))
	}
}

func loadApplicationIcon(instance uintptr) uintptr {
	icon, _, _ := procLoadIconW.Call(instance, appIconResourceID)
	return icon
}

func windowProc(window uintptr, msg uint32, wParam uintptr, lParam unsafe.Pointer) (result uintptr) {
	defer func() {
		if recovered := recover(); recovered != nil {
			if appLogger != nil {
				appLogger.Errorf("WNDPROC PANIC hwnd=%d msg=0x%X panic=%v\n%s", window, msg, recovered, debug.Stack())
			}
			result, _, _ = procDefWindowProcW.Call(window, uintptr(msg), wParam, uintptr(lParam))
		}
	}()
	switch msg {
	case wmCreate:
		mainWindow = window
		if err := buildGUI(window); err != nil {
			if appLogger != nil {
				appLogger.Errorf("build GUI: %v", err)
			}
			showError("建立畫面失敗：" + err.Error())
		}
		return 0
	case wmCommand:
		handleCommand(int(wParam & 0xffff))
		return 0
	case wmTimer:
		if wParam == apiRetryTimerID { refreshAPIState() }
		return 0
	case wmNotify:
		if notifyResult, handled := handleNotifyMessage(lParam); handled {
			return notifyResult
		}
	case wmDrawItem:
		if handleTabOwnerDraw(lParam) {
			return 1
		}
	case wmSize:
		relayoutGUI(window)
		return 0
	case wmGetMinMaxInfo:
		setMinimumWindowSize(lParam)
		return 0
	case wmCtlColorStatic:
		if brush := handleStaticColor(wParam, uintptr(lParam)); brush != 0 {
			return brush
		}
	case wmCtlColorEdit:
		return handleStaticColor(wParam, uintptr(lParam))
	case wmCtlColorBtn:
		return handleButtonColor(wParam)
	case wmInitialSetup:
		if err := runInitialSetup(); err != nil {
			appLogger.Errorf("initial setup: %v", err)
			showError("首次設定無法完成：" + err.Error())
		}
		return 0
	case wmAPIHealthResult:
		finishAPIHealthCheck(uint64(wParam))
		return 0
	case wmBuyerLookupResult:
		finishBuyerNameLookup(uint64(wParam))
		return 0
	case wmManualIssueLookupResult:
		finishManualIssueLookup(uint64(wParam))
		return 0
	case wmManualIssueResult:
		finishManualIssue(uint64(wParam))
		return 0
	case wmRecordsRefreshResult:
		finishRecordsRefresh(uint64(wParam))
		return 0
	case wmDestroy:
		procPostQuitMessage.Call(0)
		return 0
	default:
		result, _, _ = procDefWindowProcW.Call(window, uintptr(msg), wParam, uintptr(lParam))
		return result
	}
	result, _, _ = procDefWindowProcW.Call(window, uintptr(msg), wParam, uintptr(lParam))
	return result
}

func showError(text string) { messageBox(text, 0x10) }
func showInfo(text string) { messageBox(text, 0x40) }
func confirmAction(text string) bool { return messageBox(text, 0x20|0x04) == 6 }
func messageBox(text string, icon uintptr) uintptr {
	value := mustUTF16Ptr(text)
	title := mustUTF16Ptr("CYInvoice")
	owner := mainWindow
	if settingsWindow != 0 { owner = settingsWindow }
	if changePasswordWindow != 0 { owner = changePasswordWindow }
	result, _, _ := procMessageBoxW.Call(owner, uintptr(unsafe.Pointer(value)), uintptr(unsafe.Pointer(title)), icon)
	return result
}
func mustUTF16Ptr(value string) *uint16 {
	result, err := syscall.UTF16PtrFromString(value)
	if err != nil {
		panic(err)
	}
	return result
}
