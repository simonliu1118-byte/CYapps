//go:build windows

package main

import (
	"fmt"
	"syscall"
	"unsafe"
)

const settingsWindowClass = "CYInvoiceSettingsWindow"

var settingsWindow uintptr

func showSettingsWindow() {
	if settingsWindow != 0 {
		if alive, _, _ := procIsWindow.Call(settingsWindow); alive != 0 {
			procShowWindow.Call(settingsWindow, swShow)
			procSetFocus.Call(settingsWindow)
			return
		}
	}
	instance, _, callErr := procGetModuleHandleW.Call(0)
	if instance == 0 { showError(fmt.Sprintf("無法開啟設定視窗：%v", callErr)); return }
	className := mustUTF16Ptr(settingsWindowClass)
	cursor, _, _ := procLoadCursorW.Call(0, 32512)
	class := windowClassEx{
		Size: uint32(unsafe.Sizeof(windowClassEx{})), Style: csHRedraw | csVRedraw,
		WindowProc: syscall.NewCallback(settingsWindowProc), Instance: instance,
		Icon: loadApplicationIcon(instance), Cursor: cursor, Background: 16, ClassName: className,
		IconSmall: loadApplicationIcon(instance),
	}
	if registered, _, registerErr := procRegisterClassExW.Call(uintptr(unsafe.Pointer(&class))); registered == 0 {
		if errno, ok := registerErr.(syscall.Errno); !ok || errno != 1410 {
			showError(fmt.Sprintf("無法註冊設定視窗：%v", registerErr))
			return
		}
	}
	title := mustUTF16Ptr("CYInvoice 設定")
	settingsWindow, _, callErr = procCreateWindowExW.Call(0, uintptr(unsafe.Pointer(className)), uintptr(unsafe.Pointer(title)),
		0x00C00000|0x00080000|wsVisible, cwUseDefault, cwUseDefault, 780, 500, mainWindow, 0, instance, 0)
	if settingsWindow == 0 { showError(fmt.Sprintf("無法建立設定視窗：%v", callErr)); return }
	centerWindowOnParent(settingsWindow, mainWindow, 780, 500)
	procEnableWindow.Call(mainWindow, 0)
	procShowWindow.Call(settingsWindow, swShow)
	procUpdateWindow.Call(settingsWindow)

	var msg message
	for {
		alive, _, _ := procIsWindow.Call(settingsWindow)
		if alive == 0 { break }
		result, _, messageErr := procGetMessageW.Call(uintptr(unsafe.Pointer(&msg)), 0, 0, 0)
		if int32(result) == -1 { showError(fmt.Sprintf("設定視窗訊息處理失敗：%v", messageErr)); break }
		if result == 0 { procPostQuitMessage.Call(0); break }
		procTranslateMessage.Call(uintptr(unsafe.Pointer(&msg)))
		procDispatchMessageW.Call(uintptr(unsafe.Pointer(&msg)))
	}
	procEnableWindow.Call(mainWindow, 1)
	procSetFocus.Call(mainWindow)
}

func settingsWindowProc(window uintptr, msg uint32, wParam, lParam uintptr) uintptr {
	switch msg {
	case wmCreate:
		settingsWindow = window
		defaultFont = uiFont
		buildSettingsPage(window)
		defaultFont = contentFont
		for _, group := range [][]uintptr{settingsLoginControls, settingsControls} {
			for _, handle := range group { delete(baseRects, handle) }
		}
		lockSettingsPage()
		showSettingsPage(settingsLoginControls)
		procSetFocus.Call(handles[idSettingsLoginPassword])
		return 0
	case wmCommand:
		handleCommand(int(wParam & 0xffff))
		return 0
	case wmCtlColorStatic:
		return handleStaticColor(wParam, lParam)
	case wmCtlColorBtn:
		return handlePlainControlColor(wParam)
	case wmClose:
		procDestroyWindow.Call(window)
		return 0
	case wmDestroy:
		lockSettingsPage()
		settingsWindow = 0
		return 0
	default:
		result, _, _ := procDefWindowProcW.Call(window, uintptr(msg), wParam, lParam)
		return result
	}
}

func showSettingsPage(page []uintptr) {
	for _, group := range [][]uintptr{settingsLoginControls, settingsControls} {
		for _, handle := range group { procShowWindow.Call(handle, swHide) }
	}
	for _, handle := range page { procShowWindow.Call(handle, swShow) }
	if settingsWindow != 0 { procInvalidateRect.Call(settingsWindow, 0, 1) }
}
