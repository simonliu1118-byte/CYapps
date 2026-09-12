//go:build windows

package main

import (
	"fmt"
	"strings"
	"syscall"
	"unsafe"

	"cyinvoice/internal/appdata"
)

const (
	changePasswordWindowClass = "CYInvoiceChangePasswordWindow"
	idCurrentAdminPassword = 9201
	idChangedAdminPassword = 9202
	idConfirmAdminPassword = 9203
	idConfirmPasswordChange = 9204
	idCancelPasswordChange = 9205
)

var changePasswordWindow uintptr

func showChangePasswordWindow() {
	if !settingsUnlocked { showError("請先通過設定管理密碼驗證"); return }
	instance, _, callErr := procGetModuleHandleW.Call(0)
	if instance == 0 { showError(fmt.Sprintf("無法開啟管理密碼設定：%v", callErr)); return }
	className := mustUTF16Ptr(changePasswordWindowClass)
	cursor, _, _ := procLoadCursorW.Call(0, 32512)
	class := windowClassEx{
		Size: uint32(unsafe.Sizeof(windowClassEx{})), Style: csHRedraw | csVRedraw,
		WindowProc: syscall.NewCallback(changePasswordWindowProc), Instance: instance,
		Icon: loadApplicationIcon(instance), Cursor: cursor, Background: 16, ClassName: className,
		IconSmall: loadApplicationIcon(instance),
	}
	if registered, _, registerErr := procRegisterClassExW.Call(uintptr(unsafe.Pointer(&class))); registered == 0 {
		if errno, ok := registerErr.(syscall.Errno); !ok || errno != 1410 {
			showError(fmt.Sprintf("無法註冊管理密碼設定視窗：%v", registerErr))
			return
		}
	}
	title := mustUTF16Ptr("CYInvoice 設定管理密碼")
	changePasswordWindow, _, callErr = procCreateWindowExW.Call(0, uintptr(unsafe.Pointer(className)), uintptr(unsafe.Pointer(title)),
		0x00C00000|0x00080000, cwUseDefault, cwUseDefault, 520, 330, settingsWindow, 0, instance, 0)
	if changePasswordWindow == 0 { showError(fmt.Sprintf("無法建立管理密碼設定視窗：%v", callErr)); return }
	centerWindowOnParent(changePasswordWindow, settingsWindow, 520, 330)
	procEnableWindow.Call(settingsWindow, 0)
	procShowWindow.Call(changePasswordWindow, swShow)
	procUpdateWindow.Call(changePasswordWindow)
	procSetFocus.Call(handles[idCurrentAdminPassword])

	var msg message
	for {
		alive, _, _ := procIsWindow.Call(changePasswordWindow)
		if alive == 0 { break }
		result, _, messageErr := procGetMessageW.Call(uintptr(unsafe.Pointer(&msg)), 0, 0, 0)
		if int32(result) == -1 { showError(fmt.Sprintf("管理密碼視窗訊息處理失敗：%v", messageErr)); break }
		if result == 0 { procPostQuitMessage.Call(0); break }
		procTranslateMessage.Call(uintptr(unsafe.Pointer(&msg)))
		procDispatchMessageW.Call(uintptr(unsafe.Pointer(&msg)))
	}
	procEnableWindow.Call(settingsWindow, 1)
	procSetFocus.Call(settingsWindow)
}

func changePasswordWindowProc(window uintptr, msg uint32, wParam, lParam uintptr) uintptr {
	switch msg {
	case wmCreate:
		changePasswordWindow = window
		buildChangePasswordControls(window)
		return 0
	case wmCommand:
		switch int(wParam & 0xffff) {
		case idConfirmPasswordChange:
			saveChangedAdminPassword()
		case idCancelPasswordChange:
			procDestroyWindow.Call(window)
		}
		return 0
	case wmCtlColorStatic, wmCtlColorBtn:
		return handlePlainControlColor(wParam)
	case wmClose:
		procDestroyWindow.Call(window)
		return 0
	case wmDestroy:
		changePasswordWindow = 0
		return 0
	default:
		result, _, _ := procDefWindowProcW.Call(window, uintptr(msg), wParam, lParam)
		return result
	}
}

func buildChangePasswordControls(parent uintptr) {
	controls := []uintptr{
		addStatic(parent, "原管理密碼", 48, 52, 130, 28, nil),
		addEdit(parent, "", 184, 49, 280, 28, idCurrentAdminPassword, esPassword|esAutoHScroll, nil),
		addStatic(parent, "新管理密碼", 48, 103, 130, 28, nil),
		addEdit(parent, "", 184, 100, 280, 28, idChangedAdminPassword, esPassword|esAutoHScroll, nil),
		addStatic(parent, "再次輸入新管理密碼", 48, 154, 130, 48, nil),
		addEdit(parent, "", 184, 163, 280, 28, idConfirmAdminPassword, esPassword|esAutoHScroll, nil),
		addButtonStyle(parent, "確定", 236, 226, 110, 38, idConfirmPasswordChange, bsDefaultPushButton, nil),
		addButton(parent, "取消", 354, 226, 110, 38, idCancelPasswordChange, nil),
	}
	for _, handle := range controls { delete(baseRects, handle) }
}

func saveChangedAdminPassword() {
	currentPassword := controlText(handles[idCurrentAdminPassword])
	newPassword := controlText(handles[idChangedAdminPassword])
	confirmation := controlText(handles[idConfirmAdminPassword])
	if strings.TrimSpace(currentPassword) == "" || strings.TrimSpace(newPassword) == "" || strings.TrimSpace(confirmation) == "" {
		showError("三個密碼欄位都必須輸入")
		return
	}
	settings, err := appRepository.Settings.LoadOrCreate()
	if err != nil { showError("讀取設定失敗：" + err.Error()); return }
	if !appdata.CheckAdminPassword(settings, currentPassword) {
		showError("原管理密碼錯誤")
		return
	}
	if newPassword != confirmation {
		showError("新管理密碼與再次輸入的密碼不相同")
		return
	}
	if err = appRepository.Settings.SetAdminPassword(&settings, newPassword); err != nil {
		showError("設定新管理密碼失敗：" + err.Error())
		return
	}
	if err = appRepository.Settings.Save(settings); err != nil {
		showError("儲存新管理密碼失敗：" + err.Error())
		return
	}
	showInfo("管理密碼已更新")
	procDestroyWindow.Call(changePasswordWindow)
}
