//go:build windows

package main

import (
	"fmt"
	"syscall"
	"unsafe"

	"cyinvoice/internal/appdata"
	"cyinvoice/internal/recordview"
)

const (
	recordDetailWindowClass = "CYInvoiceRecordDetailWindow"
	recordDetailWindowWidth = 780
	recordDetailWindowHeight = 660
	idRecordDetailClose = 9401
)

var (
	recordDetailWindow uintptr
	recordDetailControls []uintptr
	recordDetailRecord appdata.InvoiceRecord
)

func showRecordDetail(record appdata.InvoiceRecord) {
	if recordDetailWindow != 0 {
		if alive, _, _ := procIsWindow.Call(recordDetailWindow); alive != 0 {
			procShowWindow.Call(recordDetailWindow, swShow)
			procSetFocus.Call(recordDetailWindow)
			return
		}
	}
	recordDetailRecord = record
	instance, _, callErr := procGetModuleHandleW.Call(0)
	if instance == 0 { showError(fmt.Sprintf("無法開啟發票詳細資訊：%v", callErr)); return }
	className := mustUTF16Ptr(recordDetailWindowClass)
	cursor, _, _ := procLoadCursorW.Call(0, 32512)
	class := windowClassEx{
		Size: uint32(unsafe.Sizeof(windowClassEx{})), Style: csHRedraw | csVRedraw,
		WindowProc: syscall.NewCallback(recordDetailWindowProc), Instance: instance,
		Icon: loadApplicationIcon(instance), Cursor: cursor, Background: 16, ClassName: className,
		IconSmall: loadApplicationIcon(instance),
	}
	if registered, _, registerErr := procRegisterClassExW.Call(uintptr(unsafe.Pointer(&class))); registered == 0 {
		if errno, ok := registerErr.(syscall.Errno); !ok || errno != 1410 {
			showError(fmt.Sprintf("無法註冊發票詳細資訊視窗：%v", registerErr))
			return
		}
	}
	title := mustUTF16Ptr("CYInvoice 發票詳細資訊")
	recordDetailWindow, _, callErr = procCreateWindowExW.Call(0, uintptr(unsafe.Pointer(className)), uintptr(unsafe.Pointer(title)),
		0x00C00000|0x00080000, cwUseDefault, cwUseDefault, recordDetailWindowWidth, recordDetailWindowHeight,
		mainWindow, 0, instance, 0)
	if recordDetailWindow == 0 { showError(fmt.Sprintf("無法建立發票詳細資訊視窗：%v", callErr)); return }
	centerWindowOnParent(recordDetailWindow, mainWindow, recordDetailWindowWidth, recordDetailWindowHeight)
	if err := runOwnedModalWindow(mainWindow, recordDetailWindow, handles[idRecordDetailClose]); err != nil {
		showError("發票詳細資訊視窗訊息處理失敗：" + err.Error())
		if recordDetailWindow != 0 { procDestroyWindow.Call(recordDetailWindow) }
	}
}

func recordDetailWindowProc(window uintptr, msg uint32, wParam, lParam uintptr) uintptr {
	switch msg {
	case wmCreate:
		recordDetailWindow = window
		recordDetailControls = nil
		defaultFont = contentFont
		addStatic(window, "以下內容只讀取本機紀錄，不會查詢或變更光貿發票。", 20, 16, 720, 24, &recordDetailControls)
		text := addControl("EDIT", recordview.FormatDetail(recordDetailRecord), window, 20, 44, 724, 520,
			wsChild|wsVisible|wsTabStop|wsBorder|wsVScroll|esMultiLine|esAutoVScroll|esReadOnly, 0, &recordDetailControls)
		setStaticStyle(text, rgb(0, 0, 0), rgb(255, 255, 255), whiteBrush, false)
		procSendMessageW.Call(text, emSetSel, 0, 0)
		addButtonStyle(window, "關閉", 330, 577, 120, 34, idRecordDetailClose, bsDefaultPushButton, &recordDetailControls)
		for _, handle := range recordDetailControls { delete(baseRects, handle) }
		return 0
	case wmCommand:
		id := int(wParam & 0xffff)
		if id == idRecordDetailClose || id == 2 {
			procDestroyWindow.Call(window)
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
		for _, handle := range recordDetailControls {
			delete(baseRects, handle)
			delete(colorStyles, handle)
		}
		delete(handles, idRecordDetailClose)
		recordDetailControls = nil
		recordDetailRecord = appdata.InvoiceRecord{}
		recordDetailWindow = 0
		return 0
	default:
		result, _, _ := procDefWindowProcW.Call(window, uintptr(msg), wParam, lParam)
		return result
	}
	result, _, _ := procDefWindowProcW.Call(window, uintptr(msg), wParam, lParam)
	return result
}
