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
	initialSetupClass = "CYInvoiceInitialSetupWindow"
	idSetupAdminPassword = 9001
	idSetupMOPassword = 9002
	idSetupSave = 9003
	wmClose = 0x0010
)

var initialSetupWindow uintptr

var (
	initialSetupFont uintptr
	initialSetupEditOriginalProcs = map[uintptr]uintptr{}
	initialSetupEditNext = map[uintptr]uintptr{}
	initialSetupEditCallback = syscall.NewCallback(initialSetupEditWindowProc)
)

func runInitialSetup() error {
	settings, err := appRepository.Settings.LoadOrCreate()
	if err != nil { return err }
	required, err := appRepository.Settings.InitialSetupRequired(settings)
	if err != nil { return err }
	if !required { return nil }

	instance, _, callErr := procGetModuleHandleW.Call(0)
	if instance == 0 { return fmt.Errorf("GetModuleHandleW: %v", callErr) }
	className := mustUTF16Ptr(initialSetupClass)
	cursor, _, _ := procLoadCursorW.Call(0, 32512)
	class := windowClassEx{
		Size: uint32(unsafe.Sizeof(windowClassEx{})),
		Style: csHRedraw | csVRedraw,
		WindowProc: syscall.NewCallback(initialSetupProc),
		Instance: instance, Icon: loadApplicationIcon(instance), Cursor: cursor, Background: 16, ClassName: className,
		IconSmall: loadApplicationIcon(instance),
	}
	if registered, _, registerErr := procRegisterClassExW.Call(uintptr(unsafe.Pointer(&class))); registered == 0 {
		if errno, ok := registerErr.(syscall.Errno); !ok || errno != 1410 { return fmt.Errorf("RegisterClassExW: %v", registerErr) }
	}
	title := mustUTF16Ptr("CYInvoice 首次安全設定")
	const setupWidth, setupHeight = 500, 300
	initialSetupWindow, _, callErr = procCreateWindowExW.Call(0, uintptr(unsafe.Pointer(className)), uintptr(unsafe.Pointer(title)),
		0x00C00000|0x00080000, cwUseDefault, cwUseDefault, setupWidth, setupHeight, mainWindow, 0, instance, 0)
	if initialSetupWindow == 0 { return fmt.Errorf("CreateWindowExW: %v", callErr) }
	centerWindowOnParent(initialSetupWindow, mainWindow, setupWidth, setupHeight)
	if err := runOwnedModalWindow(mainWindow, initialSetupWindow, handles[idSetupAdminPassword]); err != nil {
		if initialSetupWindow != 0 { procDestroyWindow.Call(initialSetupWindow) }
		return err
	}
	refreshAPIState()
	return nil
}

func initialSetupProc(window uintptr, msg uint32, wParam, lParam uintptr) uintptr {
	switch msg {
	case wmCreate:
		initialSetupWindow = window
		buildInitialSetupControls(window)
		return 0
	case wmCommand:
		if int(wParam&0xffff) == idSetupSave { saveInitialSetup() }
		return 0
	case wmCtlColorStatic:
		return handlePlainControlColor(wParam)
	case wmCtlColorEdit:
		return handleStaticColor(wParam, lParam)
	case wmCtlColorBtn:
		return handleButtonColor(wParam)
	case wmClose:
		setupMessage("首次使用必須設定管理密碼與 MO店+ Excel 密碼，完成後才能進入程式。", 0x30)
		return 0
	case wmDestroy:
		for handle := range initialSetupEditOriginalProcs {
			delete(initialSetupEditOriginalProcs, handle)
			delete(initialSetupEditNext, handle)
		}
		forgetControlIDs(idSetupAdminPassword, idSetupMOPassword, idSetupSave)
		initialSetupWindow = 0
		return 0
	default:
		result, _, _ := procDefWindowProcW.Call(window, uintptr(msg), wParam, lParam)
		return result
	}
}

func buildInitialSetupControls(parent uintptr) {
	initialSetupFont = createUIFont(-14, 400)
	controls := []uintptr{
		addStatic(parent, "首次安全設定", 24, 14, 300, 24, nil),
		addStatic(parent, "以下兩組密碼都必須設定，且只會安全保存在這台 Windows 電腦。", 24, 42, 440, 38, nil),
		addStatic(parent, "管理密碼", 24, 94, 120, 24, nil),
		addEdit(parent, "", 150, 93, 314, 26, idSetupAdminPassword, esPassword|esAutoHScroll, nil),
		addStatic(parent, "MO店+ Excel 密碼", 24, 133, 138, 24, nil),
		addEdit(parent, "", 166, 132, 298, 26, idSetupMOPassword, esPassword|esAutoHScroll, nil),
		addStatic(parent, "首次啟動固定使用光貿測試環境；正式資料仍需稍後在設定視窗輸入。", 24, 174, 440, 32, nil),
		addButtonStyle(parent, "完成首次設定", 304, 216, 160, 34, idSetupSave, bsDefaultPushButton, nil),
	}
	for _, handle := range controls { procSendMessageW.Call(handle, wmSetFont, initialSetupFont, 1) }
	enableInitialSetupEnterNavigation()
}

func enableInitialSetupEnterNavigation() {
	admin := handles[idSetupAdminPassword]
	mo := handles[idSetupMOPassword]
	initialSetupEditNext[admin] = mo
	initialSetupEditNext[mo] = 0
	for _, handle := range []uintptr{admin, mo} {
		original, _, _ := procSetWindowLongPtrW.Call(handle, gwlpWndProc, initialSetupEditCallback)
		if original != 0 { initialSetupEditOriginalProcs[handle] = original }
	}
}

func initialSetupEditWindowProc(window uintptr, message uint32, wParam, lParam uintptr) uintptr {
	original := initialSetupEditOriginalProcs[window]
	if original == 0 {
		result, _, _ := procDefWindowProcW.Call(window, uintptr(message), wParam, lParam)
		return result
	}
	if message == wmKeyDown && wParam == vkReturn {
		if window == handles[idSetupMOPassword] {
			saveInitialSetup()
			return 0
		}
		if next := initialSetupEditNext[window]; next != 0 {
			procSetFocus.Call(next)
			return 0
		}
	}
	result, _, _ := procCallWindowProcW.Call(original, window, uintptr(message), wParam, lParam)
	return result
}

func saveInitialSetup() {
	adminPassword := controlText(handles[idSetupAdminPassword])
	moPassword := controlText(handles[idSetupMOPassword])
	if strings.TrimSpace(adminPassword) == "" || strings.TrimSpace(moPassword) == "" {
		setupMessage("管理密碼與 MO店+ Excel 密碼都不可空白。", 0x10)
		return
	}
	settings, err := appRepository.Settings.LoadOrCreate()
	if err == nil { settings.Environment = appdata.EnvironmentTest }
	if err == nil { err = appRepository.Settings.SetAdminPassword(&settings, adminPassword) }
	if err == nil { err = appRepository.Settings.SetMOPassword(&settings, moPassword) }
	if err == nil { err = appRepository.Settings.Save(settings) }
	if err != nil {
		appLogger.Errorf("save initial setup: %v", err)
		setupMessage("首次設定儲存失敗："+err.Error(), 0x10)
		return
	}
	appLogger.Infof("initial security setup completed")
	setupMessage("首次設定已完成。\n\n目前使用光貿測試環境；正式公司統編與 App Key 請稍後到設定視窗輸入。", 0x40)
	procDestroyWindow.Call(initialSetupWindow)
}

func setupMessage(text string, icon uintptr) {
	if initialSetupWindow == 0 { showError(text); return }
	value := mustUTF16Ptr(text)
	title := mustUTF16Ptr("CYInvoice 首次安全設定")
	procMessageBoxW.Call(initialSetupWindow, uintptr(unsafe.Pointer(value)), uintptr(unsafe.Pointer(title)), icon)
}
