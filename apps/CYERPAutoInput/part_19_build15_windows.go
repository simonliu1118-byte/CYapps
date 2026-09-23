//go:build windows

package main

import (
	"fmt"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

const (
	wmKeyDownV15          = 0x0100
	wmKillFocusV15        = 0x0008
	wmMouseWheelV15       = 0x020A
	wmVScrollV15          = 0x0115
	wmHScrollV15          = 0x0114
	lvmSubItemHitTestV15  = lvmFirstV14 + 57
	lvmGetSubItemRectV15  = lvmFirstV14 + 56
	lvirBoundsV15         = 0
	initialDetailRowsV15  = 12
)

type lvHitTestInfoV15 struct {
	Pt       POINT
	Flags    uint32
	IItem    int32
	ISubItem int32
	IGroup   int32
}

var (
	pGetDlgItemV15 = user32.NewProc("GetDlgItem")

	detailGridV15          uintptr
	detailGridOldProcV15   uintptr
	detailGridWndProcV15CB uintptr
	detailCellEditV15      uintptr
	detailCellOldProcV15   uintptr
	detailCellWndProcV15CB uintptr
	detailCellRowV15       = -1
	detailCellSubV15       = -1
	detailCellClosingV15   bool

	startButtonOldProcV15   uintptr
	startButtonWndProcV15CB uintptr
	coupangOldProcV15       uintptr
	coupangWndProcV15CB     uintptr
)

// setupBuild15UIV15 is called after the Build 14 controls have been created.
// It intentionally reuses the validated ERP automation code while replacing
// only the data-entry surface and selector semantics.
func setupBuild15UIV15(parent uintptr) {
	setWindowText(parent, "CYERPAutoInput V0.0.10 Build 15 — SMART ERP 自動輸入工具")

	for _, c := range enumControls(parent) {
		if strings.Contains(c.Text, "V0.0.10 Build 14") {
			setWindowText(c.Hwnd, strings.ReplaceAll(c.Text, "V0.0.10 Build 14", "V0.0.10 Build 15"))
		}
	}

	// Field selector checkboxes are no longer user-facing. Their HWNDs remain as
	// hidden compatibility state so the proven fill routines can continue to use
	// checked(f.ApplyHwnd). Build 15 synchronizes them from entered values just
	// before automation starts.
	for _, f := range fields {
		if f == nil || f.Group == "明細" || f.ApplyHwnd == 0 {
			continue
		}
		pShowWindow.Call(f.ApplyHwnd, swHideV12)
		if widget, ok := fieldWidgetsV12[f.Key]; ok {
			widget.apply = 0
			fieldWidgetsV12[f.Key] = widget
		}
	}

	// The old select-all buttons no longer have a meaning once field inclusion is
	// driven by content.
	for _, id := range []uintptr{1210, 1211} {
		if h, _, _ := pGetDlgItemV15.Call(parent, id); h != 0 {
			pShowWindow.Call(h, swHideV12)
		}
	}
	if statusHwnd != 0 {
		pSetWindowPos.Call(statusHwnd, 0, 514, 77, 960, 24, 0x0004|SWP_SHOWWINDOW)
	}

	setupDirectDetailGridV15(parent, 30, detailBaseYV12+24)
	setupStartPreflightV15(parent)
	setupCoupangBrandV15(parent)

	if detailBoxV12 != 0 {
		setWindowText(detailBoxV12, "商品明細（直接在表格輸入；有資料的列必須填品號＋數量）")
	}
	if footerV12 != 0 {
		setWindowText(footerV12, "明細：直接點表格儲存格輸入；空白列忽略。只要該列有任何資料，品號與數量都必填。單位有填時先 F2 選單位，再輸入數量；批號點選後續處理。")
	}
}

func setupDirectDetailGridV15(parent uintptr, x, y int32) {
	// Hide Build 14 ListView/buttons. The original fixed edit matrix was already
	// hidden by setupDetailListViewV14.
	for _, d := range detailWidgetsV12 {
		if d.hwnd != 0 {
			pShowWindow.Call(d.hwnd, swHideV12)
		}
	}
	for _, c := range enumControls(parent) {
		if strings.Contains(c.Text, "單位有指定時：品號") {
			pShowWindow.Call(c.Hwnd, swHideV12)
		}
	}
	detailWidgetsV12 = nil

	style := uint32(WS_CHILD | WS_VISIBLE | WS_BORDER | WS_TABSTOP | lvsReportV14 | lvsSingleSelV14)
	detailGridV15 = createUIControlV12("SysListView32", "", style, x, y, 1470, 246, parent, 1500)
	// Reuse the V14 handle/model so its stable ERP fill path still has one source
	// of detail-row truth. Checkbox state is retained only as a hidden internal
	// marker: the first ListView column is 1 px wide.
	detailListViewV14 = detailGridV15
	registerDetailWidgetV12(detailGridV15, x, y, 1470, 246)
	pSendMessageW.Call(detailGridV15, lvmSetExtStyleV14,
		lvsExGridLinesV14|lvsExCheckboxesV14|lvsExFullRowV14|lvsExDoubleBufV14,
		lvsExGridLinesV14|lvsExCheckboxesV14|lvsExFullRowV14|lvsExDoubleBufV14)

	columns := []struct {
		text  string
		width int32
	}{
		{"", 1},
		{"品號", 260},
		{"單位", 130},
		{"數量", 130},
		{"贈/備品量", 160},
		{"批號", 230},
		{"庫別", 190},
		{"單價", 190},
	}
	for i, col := range columns {
		text := wstr(col.text)
		c := lvColumnV14{Mask: lvcfFmtV14 | lvcfWidthV14 | lvcfTextV14 | lvcfSubItemV14, Cx: col.width, PszText: text, ISubItem: int32(i)}
		pSendMessageW.Call(detailGridV15, lvmInsertColumnWV14, uintptr(i), uintptr(unsafe.Pointer(&c)))
	}

	detailRowsV14 = make([]detailRowV14, initialDetailRowsV15)
	refreshDirectDetailGridV15()
	syncDetailAutomationMarkersV15()

	if detailGridWndProcV15CB == 0 {
		detailGridWndProcV15CB = syscall.NewCallback(detailGridWndProcV15)
	}
	old, _, _ := pSetWindowLongPtrWV12.Call(detailGridV15, gwlpWndProcV12, detailGridWndProcV15CB)
	detailGridOldProcV15 = old
}

func refreshDirectDetailGridV15() {
	if detailGridV15 == 0 {
		return
	}
	pSendMessageW.Call(detailGridV15, lvmDeleteAllV14, 0, 0)
	for rowIndex, row := range detailRowsV14 {
		texts := []string{"", row.ItemCode, row.Unit, row.Qty, row.GiftQty, row.Batch, row.Warehouse, row.UnitPrice}
		for sub, textValue := range texts {
			text := wstr(textValue)
			item := lvItemV14{Mask: lvifTextV14, IItem: int32(rowIndex), ISubItem: int32(sub), PszText: text}
			msg := uintptr(lvmSetItemWV14)
			if sub == 0 {
				msg = lvmInsertItemWV14
			}
			pSendMessageW.Call(detailGridV15, msg, 0, uintptr(unsafe.Pointer(&item)))
		}
		setDetailRowCheckedV14(rowIndex, row.Enabled)
	}
}

func detailGridWndProcV15(hwnd uintptr, msg uint32, wParam, lParam uintptr) uintptr {
	if detailGridOldProcV15 == 0 {
		r, _, _ := pDefWindowProcW.Call(hwnd, uintptr(msg), wParam, lParam)
		return r
	}
	if msg == wmVScrollV15 || msg == wmHScrollV15 || msg == wmMouseWheelV15 {
		commitDetailCellEditorV15(false)
	}

	// Let the native ListView update row selection first, then put an EDIT over
	// the clicked subitem. A single click is enough; no add/edit dialog is used.
	if msg == WM_LBUTTONDOWN {
		r, _, _ := pCallWindowProcWV12.Call(detailGridOldProcV15, hwnd, uintptr(msg), wParam, lParam)
		x := int32(int16(uint16(lParam & 0xffff)))
		y := int32(int16(uint16((lParam >> 16) & 0xffff)))
		hit := lvHitTestInfoV15{Pt: POINT{X: x, Y: y}, IItem: -1, ISubItem: -1}
		pSendMessageW.Call(hwnd, lvmSubItemHitTestV15, 0, uintptr(unsafe.Pointer(&hit)))
		if hit.IItem >= 0 && hit.ISubItem >= 1 && hit.ISubItem <= 7 {
			startDetailCellEditorV15(int(hit.IItem), int(hit.ISubItem))
		}
		return r
	}
	r, _, _ := pCallWindowProcWV12.Call(detailGridOldProcV15, hwnd, uintptr(msg), wParam, lParam)
	return r
}

func startDetailCellEditorV15(row, sub int) {
	if detailGridV15 == 0 || row < 0 || row >= len(detailRowsV14) || sub < 1 || sub > 7 {
		return
	}
	commitDetailCellEditorV15(false)

	rc := RECT{Left: lvirBoundsV15, Top: int32(sub)}
	r, _, _ := pSendMessageW.Call(detailGridV15, lvmGetSubItemRectV15, uintptr(row), uintptr(unsafe.Pointer(&rc)))
	if r == 0 || rc.Right <= rc.Left || rc.Bottom <= rc.Top {
		return
	}
	value := detailCellValueV15(row, sub)
	edit := createUIControlV12("EDIT", value, WS_CHILD|WS_VISIBLE|WS_BORDER|ES_AUTOHSCROLL|WS_TABSTOP,
		rc.Left+1, rc.Top+1, rc.Right-rc.Left-2, rc.Bottom-rc.Top-2, detailGridV15, 1590)
	if edit == 0 {
		return
	}
	detailCellEditV15 = edit
	detailCellRowV15 = row
	detailCellSubV15 = sub
	if detailCellWndProcV15CB == 0 {
		detailCellWndProcV15CB = syscall.NewCallback(detailCellEditWndProcV15)
	}
	old, _, _ := pSetWindowLongPtrWV12.Call(edit, gwlpWndProcV12, detailCellWndProcV15CB)
	detailCellOldProcV15 = old
	pSetFocusV14.Call(edit)
	pSendMessageW.Call(edit, EM_SETSEL, 0, ^uintptr(0))
}

func detailCellEditWndProcV15(hwnd uintptr, msg uint32, wParam, lParam uintptr) uintptr {
	if msg == wmKeyDownV15 {
		switch wParam {
		case VK_RETURN, VK_TAB:
			commitDetailCellEditorV15(true)
			return 0
		case VK_ESCAPE:
			cancelDetailCellEditorV15()
			return 0
		}
	}
	if msg == wmKillFocusV15 && !detailCellClosingV15 {
		commitDetailCellEditorV15(false)
		return 0
	}
	if detailCellOldProcV15 != 0 {
		r, _, _ := pCallWindowProcWV12.Call(detailCellOldProcV15, hwnd, uintptr(msg), wParam, lParam)
		return r
	}
	r, _, _ := pDefWindowProcW.Call(hwnd, uintptr(msg), wParam, lParam)
	return r
}

func commitDetailCellEditorV15(moveNext bool) {
	if detailCellEditV15 == 0 || detailCellClosingV15 {
		return
	}
	hwnd := detailCellEditV15
	row := detailCellRowV15
	sub := detailCellSubV15
	value := strings.TrimSpace(getWindowText(hwnd))
	if row >= 0 && row < len(detailRowsV14) && sub >= 1 && sub <= 7 {
		setDetailCellValueV15(row, sub, value)
		setDetailGridCellTextV15(row, sub, value)
	}

	detailCellClosingV15 = true
	detailCellEditV15 = 0
	detailCellRowV15 = -1
	detailCellSubV15 = -1
	pDestroyWindowV12.Call(hwnd)
	detailCellClosingV15 = false
	detailCellOldProcV15 = 0

	ensureTrailingDetailRowV15()
	syncDetailAutomationMarkersV15()

	if moveNext && row >= 0 {
		nextRow, nextSub := row, sub+1
		if nextSub > 7 {
			nextSub = 1
			nextRow++
		}
		if nextRow >= len(detailRowsV14) {
			appendBlankDetailRowV15()
		}
		startDetailCellEditorV15(nextRow, nextSub)
	}
}

func cancelDetailCellEditorV15() {
	if detailCellEditV15 == 0 {
		return
	}
	hwnd := detailCellEditV15
	detailCellClosingV15 = true
	detailCellEditV15 = 0
	detailCellRowV15 = -1
	detailCellSubV15 = -1
	pDestroyWindowV12.Call(hwnd)
	detailCellClosingV15 = false
	detailCellOldProcV15 = 0
}

func detailCellValueV15(row, sub int) string {
	if row < 0 || row >= len(detailRowsV14) {
		return ""
	}
	r := detailRowsV14[row]
	switch sub {
	case 1:
		return r.ItemCode
	case 2:
		return r.Unit
	case 3:
		return r.Qty
	case 4:
		return r.GiftQty
	case 5:
		return r.Batch
	case 6:
		return r.Warehouse
	case 7:
		return r.UnitPrice
	}
	return ""
}

func setDetailCellValueV15(row, sub int, value string) {
	if row < 0 || row >= len(detailRowsV14) {
		return
	}
	switch sub {
	case 1:
		detailRowsV14[row].ItemCode = value
	case 2:
		detailRowsV14[row].Unit = value
	case 3:
		detailRowsV14[row].Qty = value
	case 4:
		detailRowsV14[row].GiftQty = value
	case 5:
		detailRowsV14[row].Batch = value
	case 6:
		detailRowsV14[row].Warehouse = value
	case 7:
		detailRowsV14[row].UnitPrice = value
	}
}

func setDetailGridCellTextV15(row, sub int, value string) {
	if detailGridV15 == 0 {
		return
	}
	text := wstr(value)
	item := lvItemV14{Mask: lvifTextV14, IItem: int32(row), ISubItem: int32(sub), PszText: text}
	pSendMessageW.Call(detailGridV15, lvmSetItemWV14, 0, uintptr(unsafe.Pointer(&item)))
}

func appendBlankDetailRowV15() {
	row := len(detailRowsV14)
	detailRowsV14 = append(detailRowsV14, detailRowV14{})
	text := wstr("")
	item := lvItemV14{Mask: lvifTextV14, IItem: int32(row), ISubItem: 0, PszText: text}
	pSendMessageW.Call(detailGridV15, lvmInsertItemWV14, uintptr(row), uintptr(unsafe.Pointer(&item)))
	for sub := 1; sub <= 7; sub++ {
		setDetailGridCellTextV15(row, sub, "")
	}
	setDetailRowCheckedV14(row, false)
}

func ensureTrailingDetailRowV15() {
	if len(detailRowsV14) == 0 {
		appendBlankDetailRowV15()
		return
	}
	if detailRowHasDataV15(detailRowsV14[len(detailRowsV14)-1]) {
		appendBlankDetailRowV15()
	}
}

func detailRowHasDataV15(row detailRowV14) bool {
	return strings.TrimSpace(row.ItemCode) != "" ||
		strings.TrimSpace(row.Unit) != "" ||
		strings.TrimSpace(row.Qty) != "" ||
		strings.TrimSpace(row.GiftQty) != "" ||
		strings.TrimSpace(row.Batch) != "" ||
		strings.TrimSpace(row.Warehouse) != "" ||
		strings.TrimSpace(row.UnitPrice) != ""
}

func syncDetailAutomationMarkersV15() bool {
	any := false
	for i := range detailRowsV14 {
		has := detailRowHasDataV15(detailRowsV14[i])
		detailRowsV14[i].Enabled = has
		setDetailRowCheckedV14(i, has)
		any = any || has
	}
	if detailMirrorV14 != 0 {
		state := uintptr(BST_UNCHECKED)
		if any {
			state = BST_CHECKED
		}
		pSendMessageW.Call(detailMirrorV14, BM_SETCHECK, state, 0)
	}
	return any
}

func syncUpperApplyFromValuesV15() bool {
	any := false
	for _, f := range fields {
		if f == nil || f.Group == "明細" || f.ApplyHwnd == 0 {
			continue
		}
		include := false
		if currentUIModeV12 == uiModeAdvancedV12 || standardFieldKeysV12[f.Key] {
			if f.Kind == "bool" {
				include = checked(f.ValueHwnd)
			} else {
				include = strings.TrimSpace(getWindowText(f.ValueHwnd)) != ""
			}
		}
		state := uintptr(BST_UNCHECKED)
		if include {
			state = BST_CHECKED
			any = true
		}
		pSendMessageW.Call(f.ApplyHwnd, BM_SETCHECK, state, 0)
	}
	return any
}

func validateDetailRowsV15() bool {
	for i, row := range detailRowsV14 {
		if !detailRowHasDataV15(row) {
			continue
		}
		missing := []string{}
		if strings.TrimSpace(row.ItemCode) == "" {
			missing = append(missing, "品號")
		}
		if strings.TrimSpace(row.Qty) == "" {
			missing = append(missing, "數量")
		}
		if len(missing) == 0 {
			continue
		}
		message := fmt.Sprintf("第 %d 列商品明細已有資料，但缺少%s。\n\n有資料的明細列必須同時填寫品號與數量。", i+1, strings.Join(missing, "、"))
		pMessageBoxW.Call(mainHwnd, uintptr(unsafe.Pointer(wstr(message))), uintptr(unsafe.Pointer(wstr("商品明細資料不完整"))), MB_OK|MB_ICONINFORMATION)
		setStatus(fmt.Sprintf("第 %d 列明細缺少%s，尚未開始輸入 ERP", i+1, strings.Join(missing, "、")))
		return false
	}
	return true
}

func preflightBuild15V15() bool {
	commitDetailCellEditorV15(false)
	upper := syncUpperApplyFromValuesV15()
	detail := syncDetailAutomationMarkersV15()
	if !validateDetailRowsV15() {
		return false
	}
	if !upper && !detail {
		setStatus("尚未輸入任何要送入 ERP 的資料")
		return false
	}
	return true
}

func setupStartPreflightV15(parent uintptr) {
	h, _, _ := pGetDlgItemV15.Call(parent, 1005)
	if h == 0 {
		return
	}
	if startButtonWndProcV15CB == 0 {
		startButtonWndProcV15CB = syscall.NewCallback(startButtonWndProcV15)
	}
	old, _, _ := pSetWindowLongPtrWV12.Call(h, gwlpWndProcV12, startButtonWndProcV15CB)
	startButtonOldProcV15 = old
}

func startButtonWndProcV15(hwnd uintptr, msg uint32, wParam, lParam uintptr) uintptr {
	if msg == WM_LBUTTONUP || (msg == wmKeyUpV12 && (wParam == VK_RETURN || wParam == vkSpaceV12)) {
		if !preflightBuild15V15() {
			pInvalidateRectV12.Call(hwnd, 0, 1)
			return 0
		}
	}
	if startButtonOldProcV15 != 0 {
		r, _, _ := pCallWindowProcWV12.Call(startButtonOldProcV15, hwnd, uintptr(msg), wParam, lParam)
		return r
	}
	r, _, _ := pDefWindowProcW.Call(hwnd, uintptr(msg), wParam, lParam)
	return r
}

func setupCoupangBrandV15(parent uintptr) {
	h, _, _ := pGetDlgItemV15.Call(parent, 1222)
	if h == 0 {
		return
	}
	setWindowText(h, "酷澎商城")
	if coupangWndProcV15CB == 0 {
		coupangWndProcV15CB = syscall.NewCallback(coupangWndProcV15)
	}
	old, _, _ := pSetWindowLongPtrWV12.Call(h, gwlpWndProcV12, coupangWndProcV15CB)
	coupangOldProcV15 = old
	pInvalidateRectV12.Call(h, 0, 1)
}

func coupangWndProcV15(hwnd uintptr, msg uint32, wParam, lParam uintptr) uintptr {
	if msg == wmPaintV12 {
		paintCoupangV15(hwnd)
		return 0
	}
	if coupangOldProcV15 != 0 {
		r, _, _ := pCallWindowProcWV12.Call(coupangOldProcV15, hwnd, uintptr(msg), wParam, lParam)
		return r
	}
	r, _, _ := pDefWindowProcW.Call(hwnd, uintptr(msg), wParam, lParam)
	return r
}

func paintCoupangV15(hwnd uintptr) {
	var ps paintStructV12
	hdc, _, _ := pBeginPaintV12.Call(hwnd, uintptr(unsafe.Pointer(&ps)))
	if hdc == 0 {
		return
	}
	defer pEndPaintV12.Call(hwnd, uintptr(unsafe.Pointer(&ps)))
	var r RECT
	pGetClientRect.Call(hwnd, uintptr(unsafe.Pointer(&r)))
	brush, _, _ := pCreateSolidBrushV12.Call(rgbV12(255, 255, 255))
	pen, _, _ := pCreatePenV12.Call(psSolidV12, 1, rgbV12(188, 188, 188))
	oldBrush, _, _ := pSelectObjectV12.Call(hdc, brush)
	oldPen, _, _ := pSelectObjectV12.Call(hdc, pen)
	pRoundRectV12.Call(hdc, uintptr(r.Left+1), uintptr(r.Top+1), uintptr(r.Right-1), uintptr(r.Bottom-1), 12, 12)
	pSelectObjectV12.Call(hdc, oldBrush)
	pSelectObjectV12.Call(hdc, oldPen)
	pDeleteObjectV12.Call(brush)
	pDeleteObjectV12.Call(pen)

	font, _, _ := pSendMessageW.Call(hwnd, 0x0031, 0, 0)
	if font != 0 {
		oldFont, _, _ := pSelectObjectV12.Call(hdc, font)
		defer pSelectObjectV12.Call(hdc, oldFont)
	}
	pSetBkModeV12.Call(hdc, transparentV12)
	chars := []string{"酷", "澎", "商", "城"}
	colors := []uintptr{
		rgbV12(145, 69, 18),
		rgbV12(238, 126, 0),
		rgbV12(112, 176, 0),
		rgbV12(0, 142, 196),
	}
	charW := int32(22)
	totalW := charW * int32(len(chars))
	left := (r.Right - r.Left - totalW) / 2
	for i, ch := range chars {
		pSetTextColorV12.Call(hdc, colors[i])
		rc := RECT{Left: left + int32(i)*charW, Top: r.Top, Right: left + int32(i+1)*charW, Bottom: r.Bottom}
		pDrawTextWV12.Call(hdc, uintptr(unsafe.Pointer(wstr(ch))), ^uintptr(0), uintptr(unsafe.Pointer(&rc)), dtCenterV12|dtVCenterV12|dtSingleLineV12|dtNoPrefixV12)
	}
}

// Build 15 detail fill is the Build 14 sequence with two changes: rows are
// included by content rather than a visible checkbox, and confirmed F2 unit
// selections use Enter first (the ERP-native action) before falling back to the
// visible 確定 button.
func fillDetailSelectedV15(root uintptr) (ok, fail int) {
	commitDetailCellEditorV15(false)
	syncDetailAutomationMarkersV15()
	selected := make([]detailRowV14, 0, len(detailRowsV14))
	for _, row := range detailRowsV14 {
		if detailRowHasDataV15(row) {
			selected = append(selected, row)
		}
	}
	if len(selected) == 0 {
		return 0, 0
	}
	if !validateDetailRowsV15() {
		return 0, 1
	}

	grid := findDetailGridSite(root)
	if grid == nil {
		logError("明細", "TcxGrid", "GRID_NOT_FOUND", "找不到可見的標準明細 TcxGridSite")
		return 0, 1
	}
	logf("INFO", "detail V15 direct-grid start rows=%d grid=0x%x", len(selected), grid.Hwnd)
	if !activateDetailFirstRowV4(root, *grid) {
		logError("明細", "第一列", "ROW_ACTIVATION_FAILED", "表頭完成後無法啟用表身第一列")
		return 0, 1
	}

	for rowIndex, row := range selected {
		if isStopRequested() {
			return ok, fail
		}
		if rowIndex > 0 {
			if !openNextDetailRowV8(root, *grid) {
				logError("明細", fmt.Sprintf("第%d列", rowIndex+1), "NEXT_ROW_OPEN_FAILED", "上一列完成後按 Down 無法進入下一列")
				return ok, fail + 1
			}
		}

		itemOK := false
		if rowIndex == 0 {
			itemOK = setDetailCell(root, *grid, 0, row.ItemCode)
		} else {
			itemOK = setDetailCellAtRowV11(root, *grid, rowIndex, 0, row.ItemCode)
		}
		if !itemOK {
			logError("明細", fmt.Sprintf("第%d列品號", rowIndex+1), "GRID_CELL_SET_FAILED", "品號輸入失敗")
			return ok, fail + 1
		}
		ok++

		if strings.TrimSpace(row.Unit) != "" {
			if !selectDetailUnitV15(root, *grid, rowIndex, row.Unit) {
				logError("明細", fmt.Sprintf("第%d列單位", rowIndex+1), "UNIT_LOOKUP_FAILED", "F2 單位查詢無法可靠確認指定列；已停止本列後續輸入")
				return ok, fail + 1
			}
			ok++
		}

		ordered := []struct {
			col   int
			label string
			value string
		}{
			{1, "數量", row.Qty},
			{3, "贈/備品量", row.GiftQty},
			{6, "庫別", row.Warehouse},
			{7, "單價", row.UnitPrice},
		}
		for _, f := range ordered {
			if strings.TrimSpace(f.value) == "" {
				continue
			}
			if !setDetailCellAtRowV11(root, *grid, rowIndex, f.col, f.value) {
				logError("明細", fmt.Sprintf("第%d列%s", rowIndex+1, f.label), "GRID_CELL_SET_FAILED", fmt.Sprintf("row=%d col=%d", rowIndex+1, f.col))
				return ok, fail + 1
			}
			ok++
		}
		if strings.TrimSpace(row.Batch) != "" {
			logf("INFO", "detail V15 batch deferred row=%d requested_len=%d", rowIndex+1, len([]rune(strings.TrimSpace(row.Batch))))
		}
	}
	logf("INFO", "detail V15 direct-grid end rows=%d", len(selected))
	return ok, fail
}

func selectDetailUnitV15(root uintptr, grid ControlInfo, row int, wanted string) bool {
	wanted = strings.TrimSpace(wanted)
	if wanted == "" || isStopRequested() {
		return wanted == ""
	}
	if !prepareERPWindow(root) {
		return false
	}
	ratio, ok := detailColumnRatio(4)
	if !ok {
		return false
	}
	visibleRow := detailVisibleRowIndexV11(grid, row)
	w := float64(grid.Rect.Right - grid.Rect.Left)
	x := grid.Rect.Left + int32(w*ratio)
	y := grid.Rect.Top + 33 + int32(visibleRow)*detailRowHeightV8
	clickScreenPoint(x, y)
	if !interruptibleSleep(180 * time.Millisecond) {
		return false
	}
	pressVK(VK_F2)
	lookup := waitUnitLookupWindowV14(root, 2200*time.Millisecond)
	if lookup == 0 {
		logf("WARN", "unit lookup V15 window not found row=%d requested_len=%d", row+1, len([]rune(wanted)))
		return false
	}
	prepareLookupWindowV14(lookup)
	if !selectLookupUnitRowV14(lookup, wanted) {
		setStatus("ERP：單位 F2 查詢已開啟，但無法可靠讀取指定單位；視窗保留供診斷")
		return false
	}

	// The F2 grid's natural commit action is Enter. Use it first rather than
	// relying on the dialog button being exposed as a normal child HWND.
	pressVK(VK_RETURN)
	if !interruptibleSleep(320 * time.Millisecond) {
		return false
	}
	if lookupWindowStillVisibleV15(lookup) {
		if !clickLookupButtonV14(lookup, "確定") {
			logf("WARN", "unit lookup V15 Enter did not close dialog and confirm button not found")
			return false
		}
		if !interruptibleSleep(320 * time.Millisecond) {
			return false
		}
	}
	if lookupWindowStillVisibleV15(lookup) {
		logf("WARN", "unit lookup V15 dialog still visible after Enter/fallback")
		return false
	}
	prepareERPWindow(root)
	logf("INFO", "unit lookup V15 selected row=%d requested_len=%d confirm=enter", row+1, len([]rune(wanted)))
	return true
}

func lookupWindowStillVisibleV15(hwnd uintptr) bool {
	if hwnd == 0 {
		return false
	}
	vis, _, _ := pIsWindowVisible.Call(hwnd)
	return vis != 0
}
