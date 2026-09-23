//go:build windows

package main

import (
	"fmt"
	"strings"
	"time"
	"unsafe"
)

func buildRowsFromControls(items []ControlInfo, tolerance int32) [][]ControlInfo {
	for i := 0; i < len(items); i++ {
		for j := i + 1; j < len(items); j++ {
			iy := (items[i].Rect.Top + items[i].Rect.Bottom) / 2
			jy := (items[j].Rect.Top + items[j].Rect.Bottom) / 2
			if jy < iy || (jy == iy && items[j].Rect.Left < items[i].Rect.Left) {
				items[i], items[j] = items[j], items[i]
			}
		}
	}
	rows := [][]ControlInfo{}
	centers := []int32{}
	for _, c := range items {
		cy := (c.Rect.Top + c.Rect.Bottom) / 2
		idx := -1
		for i, y := range centers {
			d := cy - y
			if d < 0 { d = -d }
			if d <= tolerance { idx = i; break }
		}
		if idx < 0 {
			rows = append(rows, []ControlInfo{c})
			centers = append(centers, cy)
		} else { rows[idx] = append(rows[idx], c) }
	}
	for i := range rows {
		for a := 0; a < len(rows[i]); a++ {
			for b := a + 1; b < len(rows[i]); b++ {
				if rows[i][b].Rect.Left < rows[i][a].Rect.Left { rows[i][a], rows[i][b] = rows[i][b], rows[i][a] }
			}
		}
	}
	return rows
}

func buildHeaderRows(root uintptr) [][]ControlInfo { return buildRowsFromControls(headerStateEdits(root), 9) }

func writeHeaderLayoutLog(rows [][]ControlInfo) {
	logf("INFO", "layout 表頭 rows=%d", len(rows))
	for i, row := range rows {
		parts := []string{}
		for j, c := range row { parts = append(parts, fmt.Sprintf("c%d:0x%x/%s/ro=%t/en=%t", j, c.Hwnd, c.Class, windowStyle(c.Hwnd)&ES_READONLY != 0, c.Enabled)) }
		logf("INFO", "layout 表頭 row%d [%s]", i, strings.Join(parts, ", "))
	}
}

func fillHeaderSelected(root uintptr) (ok, fail int) {
	if !selectedInGroup("表頭") { return }
	rows := buildHeaderRows(root)
	writeHeaderLayoutLog(rows)
	if len(rows) != 2 || len(rows[0]) != 3 || len(rows[1]) != 3 {
		logError("表頭", "欄位結構", "LAYOUT_MISMATCH", fmt.Sprintf("expected=[3,3] actual_rows=%d", len(rows)))
		return 0, 1
	}
	for _, f := range fields {
		if isStopRequested() { return ok, fail }
		if f.Group != "表頭" || !checked(f.ApplyHwnd) { continue }
		if f.Row >= len(rows) || f.Col >= len(rows[f.Row]) {
			logError("表頭", f.Label, "FIELD_POSITION_MISSING", fmt.Sprintf("row=%d col=%d", f.Row, f.Col)); fail++; continue
		}
		target := rows[f.Row][f.Col]
		value := getWindowText(f.ValueHwnd)
		filled := false
		if f.Kind == "date" {
			filled = setDateControlInteractiveV4(root, target, value)
		} else {
			filled = setTextControlInteractiveV4(root, target, value)
			if filled && f.Kind == "lookup" {
				filled = commitLookupFieldV3(root, target, 0)
			} else if filled {
				commitHeaderField(root)
			}
		}
		if filled {
			ok++; logf("INFO", "filled 表頭/%s hwnd=0x%x class=%q kind=%q", f.Label, target.Hwnd, target.Class, f.Kind)
		} else {
			fail++; logError("表頭", f.Label, "TEXT_SET_FAILED", fmt.Sprintf("hwnd=0x%x class=%q readonly=%t enabled=%t", target.Hwnd, target.Class, windowStyle(target.Hwnd)&ES_READONLY != 0, target.Enabled))
		}
	}
	return
}

func fillTabGroupSelected(root uintptr, group string) (ok, fail int) {
	if !selectedInGroup(group) { return }
	if !activateDevExpressTabForFill(root, group) {
		logError(group, "頁籤", "TAB_ACTIVATE_FAILED", "無法自動切換到目標頁籤")
		return 0, 1
	}
	if !interruptibleSleep(180 * time.Millisecond) { return 0, 0 }

	sheet := findTabSheet(root, group)
	if sheet == 0 { logError(group, "頁籤", "TAB_SHEET_NOT_FOUND", "找不到 TcxTabSheet"); return 0, 1 }
	rows := buildActionRows(sheet)
	writeLayoutLog(group, sheet, rows)
	if !validateGroupLayout(group, rows) { logError(group, "欄位結構", "LAYOUT_MISMATCH", fmt.Sprintf("rows=%d；為避免填錯欄位，已停止此頁", len(rows))); return 0, 1 }

	for _, f := range fields {
		if isStopRequested() { return ok, fail }
		if f.Group != group || !checked(f.ApplyHwnd) { continue }
		if f.Row < 0 || f.Row >= len(rows) || f.Col < 0 || f.Col >= len(rows[f.Row]) { logError(group, f.Label, "FIELD_POSITION_MISSING", fmt.Sprintf("row=%d col=%d", f.Row, f.Col)); fail++; continue }
		target := rows[f.Row][f.Col]
		if f.Kind == "bool" {
			if setCheckboxControl(target, checked(f.ValueHwnd)) { ok++; logf("INFO", "filled %s/%s hwnd=0x%x class=%q kind=checkbox", group, f.Label, target.Hwnd, target.Class) } else { fail++; logError(group, f.Label, "CHECKBOX_SET_FAILED", fmt.Sprintf("hwnd=0x%x class=%q", target.Hwnd, target.Class)) }
		} else if f.Kind == "combo" || strings.Contains(strings.ToUpper(target.Class), "IMAGECOMBOBOX") {
			code := strings.TrimSpace(getWindowText(f.ValueHwnd))
			if selectDevExpressComboByCode(root, target, code) { ok++; logf("INFO", "filled %s/%s hwnd=0x%x class=%q kind=combo requested_len=%d", group, f.Label, target.Hwnd, target.Class, len([]rune(code))); clickBlankArea(sheet); time.Sleep(180 * time.Millisecond) } else { fail++; logError(group, f.Label, "COMBO_SELECT_FAILED", fmt.Sprintf("hwnd=0x%x class=%q requested_len=%d", target.Hwnd, target.Class, len([]rune(code)))) }
		} else if f.Kind == "date" {
			if setDateControlInteractiveV4(root, target, getWindowText(f.ValueHwnd)) { ok++; logf("INFO", "filled %s/%s hwnd=0x%x class=%q kind=date", group, f.Label, target.Hwnd, target.Class) } else { fail++; logError(group, f.Label, "DATE_SET_FAILED", fmt.Sprintf("hwnd=0x%x class=%q", target.Hwnd, target.Class)) }
		} else if isSimpleMoneyFieldV13(f.Key) {
			if setSimpleClickInputV13(root, target, getWindowText(f.ValueHwnd), sheet) { ok++; logf("INFO", "filled %s/%s hwnd=0x%x class=%q kind=simple-click-input-leave", group, f.Label, target.Hwnd, target.Class) } else { fail++; logError(group, f.Label, "SIMPLE_INPUT_FAILED", fmt.Sprintf("hwnd=0x%x class=%q", target.Hwnd, target.Class)) }
		} else {
			filled := setTextControlInteractiveV4(root, target, getWindowText(f.ValueHwnd))
			if filled && f.Kind == "lookup" {
				filled = commitLookupFieldV3(root, target, sheet)
			} else if filled {
				clickBlankArea(sheet)
				time.Sleep(180 * time.Millisecond)
			}
			if filled { ok++; logf("INFO", "filled %s/%s hwnd=0x%x class=%q kind=%q", group, f.Label, target.Hwnd, target.Class, f.Kind) } else { fail++; logError(group, f.Label, "TEXT_SET_FAILED", fmt.Sprintf("hwnd=0x%x class=%q readonly=%t enabled=%t", target.Hwnd, target.Class, windowStyle(target.Hwnd)&ES_READONLY != 0, target.Enabled)) }
		}
		time.Sleep(90 * time.Millisecond)
	}
	clickBlankArea(sheet); time.Sleep(120 * time.Millisecond)
	return
}

func findDetailGridSite(root uintptr) *ControlInfo {
	all := enumControls(root)
	var best *ControlInfo; var bestArea int64
	for i := range all {
		c := &all[i]; if !c.Visible || !strings.EqualFold(c.Class, "TcxGridSite") { continue }
		w := int64(c.Rect.Right-c.Rect.Left); h := int64(c.Rect.Bottom-c.Rect.Top); area := w*h
		if w > 700 && h > 120 && area > bestArea { best = c; bestArea = area }
	}
	return best
}

func detailColumnRatio(col int) (float64, bool) {
	ratios := []float64{0.052, 0.339, 0.379, 0.418, 0.450, 0.493, 0.543, 0.626, 0.664, 0.948}
	if col < 0 || col >= len(ratios) { return 0, false }
	return ratios[col], true
}

func setDetailCell(root uintptr, grid ControlInfo, col int, value string) bool {
	return setDetailCellEnterV4(root, grid, col, value)
}

func fillDetailSelected(root uintptr) (ok, fail int) {
	return fillDetailSelectedV11(root)
}

func fillAllSelected() {
	root := findERPWindow(); if root == 0 { setStatus("ERP：找不到 COPI08，未執行"); logError("自動填入","ERP","WINDOW_NOT_FOUND","找不到 COPI08 主視窗"); return }
	if !anySelected() { setStatus("ERP：尚未輸入任何資料"); return }
	if isStopRequested() { return }
	if !prepareERPWindow(root) { setStatus("ERP：已找到但無法移到前景，為避免誤輸入已停止"); return }
	var oldCursor POINT; pGetCursorPos.Call(uintptr(unsafe.Pointer(&oldCursor))); defer pSetCursorPos.Call(uintptr(oldCursor.X), uintptr(oldCursor.Y))
	logf("INFO", "AUTO-FILL V0.0.10 Build 16 start (NO SAVE), target=0x%x", root); setStatus("ERP：先確認輸入狀態…")
	if !ensureInputMode(root) { if isStopRequested(){return}; setStatus("ERP：無法確認輸入狀態，已停止；請提供除錯紀錄"); logError("自動填入","ERP","INPUT_MODE_NOT_CONFIRMED","未進入或無法判斷輸入狀態"); return }
	setStatus("ERP：Build 16 新增模式，依序填入有資料的表頭／交易／送貨／發票欄位→直接表格明細（不儲存）…"); pSetForeground.Call(root); if !interruptibleSleep(250*time.Millisecond){return}
	ok,fail := 0,0; a,b := fillHeaderSelected(root); ok+=a; fail+=b; if isStopRequested(){return}
	for _, group := range []string{"交易資料","送貨資料","發票資料(一)"} { if isStopRequested(){return}; a,b = fillTabGroupSelected(root,group); ok+=a; fail+=b }
	if isStopRequested(){return}; a,b = fillDetailSelected(root); ok+=a; fail+=b; if isStopRequested(){return}
	setStatus(fmt.Sprintf("ERP：Build 16 測試完成，成功 %d，失敗 %d（未儲存）",ok,fail)); logf("INFO", "AUTO-FILL V0.0.10 Build 16 end success=%d fail=%d (NO SAVE)",ok,fail)
}

func findTabSheet(root uintptr, tabName string) uintptr {
	ctrls := enumControls(root); want := normalize(tabName)
	for _, c := range ctrls { if strings.EqualFold(c.Class,"TcxTabSheet") && normalize(c.Text)==want { return c.Hwnd } }
	return 0
}

func directActionControls(sheet uintptr) []ControlInfo {
	all := enumControls(sheet); out := make([]ControlInfo,0,20)
	for _, c := range all {
		if c.Parent != sheet { continue }
		u := strings.ToUpper(c.Class)
		if strings.Contains(u,"TDBEDIT") || strings.Contains(u,"TCXDBIMAGECOMBOBOX") || strings.Contains(u,"TFDBCHECKBOX") { out = append(out,c) }
	}
	return out
}
