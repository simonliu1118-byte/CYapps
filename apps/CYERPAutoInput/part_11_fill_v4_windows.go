//go:build windows

package main

import (
	"fmt"
	"strings"
	"time"
	"unsafe"
)

func fillHeaderSelectedV4(root uintptr) (ok, fail int) {
	if !selectedInGroup("表頭") {
		return
	}
	rows := buildHeaderRows(root)
	writeHeaderLayoutLog(rows)
	if len(rows) != 2 || len(rows[0]) != 3 || len(rows[1]) != 3 {
		logError("表頭", "欄位結構", "LAYOUT_MISMATCH", fmt.Sprintf("expected=[3,3] actual_rows=%d", len(rows)))
		return 0, 1
	}
	for _, f := range fields {
		if isStopRequested() {
			return ok, fail
		}
		if f.Group != "表頭" || !checked(f.ApplyHwnd) {
			continue
		}
		if f.Row >= len(rows) || f.Col >= len(rows[f.Row]) {
			logError("表頭", f.Label, "FIELD_POSITION_MISSING", fmt.Sprintf("row=%d col=%d", f.Row, f.Col))
			fail++
			continue
		}
		target := rows[f.Row][f.Col]
		value := getWindowText(f.ValueHwnd)
		filled := false
		switch f.Kind {
		case "date":
			filled = setDateControlInteractiveV4(root, target, value)
		default:
			filled = setTextControlInteractiveV4(root, target, value)
			if filled && f.Kind == "lookup" {
				filled = commitLookupFieldV3(root, target, 0)
			} else if filled {
				commitHeaderField(root)
			}
		}
		if filled {
			ok++
			logf("INFO", "filled-v4 表頭/%s hwnd=0x%x class=%q kind=%q", f.Label, target.Hwnd, target.Class, f.Kind)
		} else {
			fail++
			logError("表頭", f.Label, "TEXT_SET_FAILED_V4", fmt.Sprintf("hwnd=0x%x class=%q readonly=%t enabled=%t", target.Hwnd, target.Class, windowStyle(target.Hwnd)&ES_READONLY != 0, target.Enabled))
		}
	}
	return
}

func fillTabGroupSelectedV4(root uintptr, group string) (ok, fail int) {
	if !selectedInGroup(group) {
		return
	}
	if !activateDevExpressTabForFill(root, group) {
		logError(group, "頁籤", "TAB_ACTIVATE_FAILED", "無法自動切換到目標頁籤")
		return 0, 1
	}
	if !interruptibleSleep(180 * time.Millisecond) {
		return 0, 0
	}
	sheet := findTabSheet(root, group)
	if sheet == 0 {
		logError(group, "頁籤", "TAB_SHEET_NOT_FOUND", "找不到 TcxTabSheet")
		return 0, 1
	}
	rows := buildActionRows(sheet)
	writeLayoutLog(group, sheet, rows)
	if !validateGroupLayout(group, rows) {
		logError(group, "欄位結構", "LAYOUT_MISMATCH", fmt.Sprintf("rows=%d；為避免填錯欄位，已停止此頁", len(rows)))
		return 0, 1
	}

	for _, f := range fields {
		if isStopRequested() {
			return ok, fail
		}
		if f.Group != group || !checked(f.ApplyHwnd) {
			continue
		}
		if f.Row < 0 || f.Row >= len(rows) || f.Col < 0 || f.Col >= len(rows[f.Row]) {
			logError(group, f.Label, "FIELD_POSITION_MISSING", fmt.Sprintf("row=%d col=%d", f.Row, f.Col))
			fail++
			continue
		}
		target := rows[f.Row][f.Col]
		filled := false
		switch {
		case f.Kind == "bool":
			filled = setCheckboxControl(target, checked(f.ValueHwnd))
		case f.Kind == "combo" || strings.Contains(strings.ToUpper(target.Class), "IMAGECOMBOBOX"):
			code := strings.TrimSpace(getWindowText(f.ValueHwnd))
			filled = selectDevExpressComboByCode(root, target, code)
			if filled {
				clickBlankArea(sheet)
				interruptibleSleep(180 * time.Millisecond)
			}
		case f.Kind == "date":
			filled = setDateControlInteractiveV4(root, target, getWindowText(f.ValueHwnd))
		case f.Kind == "lookup":
			filled = setTextControlInteractiveV4(root, target, getWindowText(f.ValueHwnd))
			if filled {
				filled = commitLookupFieldV3(root, target, sheet)
			}
		default:
			filled = setTextControlInteractiveV4(root, target, getWindowText(f.ValueHwnd))
			if filled {
				clickBlankArea(sheet)
				interruptibleSleep(180 * time.Millisecond)
			}
		}
		if filled {
			ok++
			logf("INFO", "filled-v4 %s/%s hwnd=0x%x class=%q kind=%q", group, f.Label, target.Hwnd, target.Class, f.Kind)
		} else {
			fail++
			logError(group, f.Label, "FIELD_SET_FAILED_V4", fmt.Sprintf("hwnd=0x%x class=%q", target.Hwnd, target.Class))
		}
		if !interruptibleSleep(90 * time.Millisecond) {
			return ok, fail
		}
	}
	clickBlankArea(sheet)
	interruptibleSleep(120 * time.Millisecond)
	return
}

func fillDetailSelectedV4(root uintptr) (ok, fail int) {
	if !selectedInGroup("明細") {
		return
	}
	grid := findDetailGridSite(root)
	if grid == nil {
		logError("明細", "TcxGrid", "GRID_NOT_FOUND", "找不到可見的標準明細 TcxGridSite")
		return 0, 1
	}
	logf("INFO", "detail-v4 grid hwnd=0x%x rect=%d,%d,%d,%d", grid.Hwnd, grid.Rect.Left, grid.Rect.Top, grid.Rect.Right, grid.Rect.Bottom)

	// COPI08 requires one body click before the first detail row is actually
	// created/activated. This is intentionally separate from cell editing.
	if !activateDetailFirstRowV4(root, *grid) {
		logError("明細", "第一列", "ROW_ACTIVATION_FAILED", "無法完成表身第一列啟用")
		return 0, 1
	}

	for _, f := range fields {
		if isStopRequested() {
			return ok, fail
		}
		if f.Group != "明細" || !checked(f.ApplyHwnd) {
			continue
		}
		if setDetailCellEnterV4(root, *grid, f.Col, getWindowText(f.ValueHwnd)) {
			ok++
			logf("INFO", "filled-v4 明細/%s col=%d", f.Label, f.Col)
		} else {
			fail++
			logError("明細", f.Label, "GRID_CELL_SET_FAILED_V4", fmt.Sprintf("col=%d grid=0x%x", f.Col, grid.Hwnd))
		}
	}
	return
}

func fillAllSelectedV4() {
	root := findERPWindow()
	if root == 0 {
		setStatus("ERP：找不到 COPI08，未執行")
		logError("自動填入", "ERP", "WINDOW_NOT_FOUND", "找不到 COPI08 主視窗")
		return
	}
	if !anySelected() {
		setStatus("ERP：尚未勾選任何欄位")
		return
	}
	if isStopRequested() || !prepareERPWindow(root) {
		if !isStopRequested() {
			setStatus("ERP：已找到但無法移到前景，為避免誤輸入已停止")
		}
		return
	}
	var oldCursor POINT
	pGetCursorPos.Call(uintptr(unsafe.Pointer(&oldCursor)))
	defer pSetCursorPos.Call(uintptr(oldCursor.X), uintptr(oldCursor.Y))

	logf("INFO", "AUTO-FILL V0.0.10 Build 3 start (NO SAVE), target=0x%x", root)
	setStatus("ERP：先確認輸入狀態…")
	if !ensureInputMode(root) {
		if isStopRequested() {
			return
		}
		setStatus("ERP：無法確認輸入狀態，已停止；請提供除錯紀錄")
		logError("自動填入", "ERP", "INPUT_MODE_NOT_CONFIRMED", "未進入或無法判斷輸入狀態")
		return
	}
	setStatus("ERP：Build 3 焦點確認模式，依序填入表頭→交易→送貨→發票→明細（不儲存）…")
	pSetForeground.Call(root)
	if !interruptibleSleep(250 * time.Millisecond) {
		return
	}

	ok, fail := 0, 0
	a, b := fillHeaderSelectedV4(root)
	ok += a
	fail += b
	if isStopRequested() {
		return
	}
	for _, group := range []string{"交易資料", "送貨資料", "發票資料(一)"} {
		if isStopRequested() {
			return
		}
		a, b = fillTabGroupSelectedV4(root, group)
		ok += a
		fail += b
	}
	if isStopRequested() {
		return
	}
	a, b = fillDetailSelectedV4(root)
	ok += a
	fail += b
	if isStopRequested() {
		return
	}
	setStatus(fmt.Sprintf("ERP：Build 3 測試完成，成功 %d，失敗 %d（未儲存）", ok, fail))
	logf("INFO", "AUTO-FILL V0.0.10 Build 3 end success=%d fail=%d (NO SAVE)", ok, fail)
}
