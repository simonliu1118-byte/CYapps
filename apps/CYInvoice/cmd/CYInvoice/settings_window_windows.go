//go:build windows

package main

import (
	"fmt"
	"syscall"
	"unsafe"
)

const (
	settingsWindowClass = "CYInvoiceSettingsWindow"
	settingsWindowWidth = 560
	settingsWindowHeight = 351
)

var (
	settingsWindow uintptr
	settingsLoginEditOriginalProc uintptr
	settingsLoginEditCallback = syscall.NewCallback(settingsLoginEditWindowProc)
)

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
		0x00C00000|0x00080000, cwUseDefault, cwUseDefault, settingsWindowWidth, settingsWindowHeight, mainWindow, 0, instance, 0)
	if settingsWindow == 0 { showError(fmt.Sprintf("無法建立設定視窗：%v", callErr)); return }
	centerWindowOnParent(settingsWindow, mainWindow, settingsWindowWidth, settingsWindowHeight)
	if err := runOwnedModalWindow(mainWindow, settingsWindow, handles[idSettingsLoginPassword]); err != nil {
		showError("設定視窗訊息處理失敗：" + err.Error())
		if settingsWindow != 0 { procDestroyWindow.Call(settingsWindow) }
	}
}

func settingsWindowProc(window uintptr, msg uint32, wParam, lParam uintptr) uintptr {
	switch msg {
	case wmCreate:
		settingsWindow = window
		defaultFont = smallFont
		buildCompactSettingsPage(window)
		defaultFont = contentFont
		for _, group := range [][]uintptr{settingsLoginControls, settingsControls} {
			for _, handle := range group { delete(baseRects, handle) }
		}
		installSettingsLoginEnter()
		lockSettingsPage()
		showSettingsPage(settingsLoginControls)
		return 0
	case wmCommand:
		id := int(wParam & 0xffff)
		switch id {
		case idSettingsSave:
			saveCompactSettings()
			return 0
		case idSettingsUnlock:
			if unlockSettingsWindow() {
				procSetFocus.Call(handles[idEnvironmentTest])
			}
			return 0
		default:
			handleCommand(id)
			return 0
		}
	case wmCtlColorStatic:
		return handleStaticColor(wParam, lParam)
	case wmCtlColorEdit:
		return handleStaticColor(wParam, lParam)
	case wmCtlColorBtn:
		return handleButtonColor(wParam)
	case wmClose:
		procDestroyWindow.Call(window)
		return 0
	case wmDestroy:
		lockSettingsPage()
		forgetControlIDs(
			idSettingsBack, idEnvironmentTest, idEnvironmentProd, idProdBAN, idProdKey,
			idMOPassword, idAdminPassword, idSettingsSave, idSettingsLoginPassword,
			idSettingsUnlock, idSettingsCancel, idSettingsChangePassword,
		)
		settingsControls = nil
		settingsLoginControls = nil
		settingsLoginEditOriginalProc = 0
		settingsWindow = 0
		return 0
	default:
		result, _, _ := procDefWindowProcW.Call(window, uintptr(msg), wParam, lParam)
		return result
	}
}

func installSettingsLoginEnter() {
	handle := handles[idSettingsLoginPassword]
	if handle == 0 { return }
	original, _, _ := procSetWindowLongPtrW.Call(handle, gwlpWndProc, settingsLoginEditCallback)
	if original != 0 { settingsLoginEditOriginalProc = original }
}

func settingsLoginEditWindowProc(window uintptr, msg uint32, wParam, lParam uintptr) uintptr {
	original := settingsLoginEditOriginalProc
	if original == 0 {
		result, _, _ := procDefWindowProcW.Call(window, uintptr(msg), wParam, lParam)
		return result
	}
	if msg == wmKeyDown && wParam == vkReturn {
		if settingsWindow != 0 {
			procSendMessageW.Call(settingsWindow, wmCommand, uintptr(idSettingsUnlock), 0)
		}
		return 0
	}
	result, _, _ := procCallWindowProcW.Call(original, window, uintptr(msg), wParam, lParam)
	return result
}

func showSettingsPage(page []uintptr) {
	for _, group := range [][]uintptr{settingsLoginControls, settingsControls} {
		for _, handle := range group { procShowWindow.Call(handle, swHide) }
	}
	for _, handle := range page { procShowWindow.Call(handle, swShow) }
	if settingsWindow != 0 { procInvalidateRect.Call(settingsWindow, 0, 1) }
}
