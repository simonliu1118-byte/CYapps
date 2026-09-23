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
	lvsReportV14        = 0x0001
	lvsSingleSelV14     = 0x0004
	lvmFirstV14         = 0x1000
	lvmInsertItemWV14   = lvmFirstV14 + 77
	lvmSetItemWV14      = lvmFirstV14 + 76
	lvmInsertColumnWV14 = lvmFirstV14 + 97
	lvmDeleteAllV14     = lvmFirstV14 + 9
	lvmGetNextItemV14   = lvmFirstV14 + 12
	lvmGetItemStateV14  = lvmFirstV14 + 44
	lvmSetItemStateV14  = lvmFirstV14 + 43
	lvmSetExtStyleV14   = lvmFirstV14 + 54
	lvisStateImageV14   = 0xF000
	lvniSelectedV14     = 0x0002
	lvifTextV14         = 0x0001
	lvcfFmtV14          = 0x0001
	lvcfWidthV14        = 0x0002
	lvcfTextV14         = 0x0004
	lvcfSubItemV14      = 0x0008
	lvsExGridLinesV14   = 0x00000001
	lvsExCheckboxesV14  = 0x00000004
	lvsExFullRowV14     = 0x00000020
	lvsExDoubleBufV14   = 0x00010000
	iccListViewV14      = 0x00000001
)

type initCommonControlsExV14 struct {
	DwSize uint32
	DwICC  uint32
}

type lvColumnV14 struct {
	Mask       uint32
	Fmt        int32
	Cx         int32
	PszText    *uint16
	CchTextMax int32
	ISubItem   int32
	IImage     int32
	IOrder     int32
	CxMin      int32
	CxDefault  int32
	CxIdeal    int32
}

type lvItemV14 struct {
	Mask       uint32
	IItem      int32
	ISubItem   int32
	State      uint32
	StateMask  uint32
	PszText    *uint16
	CchTextMax int32
	IImage     int32
	LParam     uintptr
	IIndent    int32
	IGroupID   int32
	CColumns   uint32
	PuColumns  *uint32
	PiColFmt   *int32
	IGroup     int32
}

type detailRowV14 struct {
	Enabled   bool
	ItemCode  string
	Unit      string
	Qty       string
	GiftQty   string
	Batch     string
	Warehouse string
	UnitPrice string
}

var (
	comctl32V14 = syscall.NewLazyDLL("comctl32.dll")
	pInitCommonControlsExV14 = comctl32V14.NewProc("InitCommonControlsEx")
	pSetFocusV14             = user32.NewProc("SetFocus")

	detailListViewV14 uintptr
	detailRowsV14     []detailRowV14
	detailMirrorV14   uintptr

	detailEditorV14          uintptr
	detailEditorIndexV14     = -1
	detailEditorFieldsV14    = map[string]uintptr{}
	detailEditorClassV14     bool
	detailEditorWndProcV14CB uintptr
)

func setupDetailListViewV14(parent uintptr, x, y int32) {
	icc := initCommonControlsExV14{DwSize: uint32(unsafe.Sizeof(initCommonControlsExV14{})), DwICC: iccListViewV14}
	pInitCommonControlsExV14.Call(uintptr(unsafe.Pointer(&icc)))

	// Build 12/13 created a fixed 8-row edit matrix. Build 14 replaces it with
	// a real ListView but keeps the outer group box and mode-dependent positioning.
	for _, d := range detailWidgetsV12 {
		if d.hwnd != 0 {
			pShowWindow.Call(d.hwnd, swHideV12)
		}
	}
	detailWidgetsV12 = nil

	for _, f := range fields {
		if f != nil && f.Group == "明細" && f.ApplyHwnd != 0 {
			pSendMessageW.Call(f.ApplyHwnd, BM_SETCHECK, BST_UNCHECKED, 0)
		}
	}

	style := uint32(WS_CHILD | WS_VISIBLE | WS_BORDER | WS_TABSTOP | lvsReportV14 | lvsSingleSelV14)
	detailListViewV14 = createUIControlV12("SysListView32", "", style, x, y, 1250, 246, parent, 1400)
	registerDetailWidgetV12(detailListViewV14, x, y, 1250, 246)
	pSendMessageW.Call(detailListViewV14, lvmSetExtStyleV14,
		lvsExGridLinesV14|lvsExCheckboxesV14|lvsExFullRowV14|lvsExDoubleBufV14,
		lvsExGridLinesV14|lvsExCheckboxesV14|lvsExFullRowV14|lvsExDoubleBufV14)

	columns := []struct {
		text  string
		width int32
	}{
		{"列", 52}, {"品號", 220}, {"單位", 105}, {"數量", 110},
		{"贈/備品量", 130}, {"批號", 190}, {"庫別", 145}, {"單價", 145},
	}
	for i, col := range columns {
		text := wstr(col.text)
		c := lvColumnV14{Mask: lvcfFmtV14 | lvcfWidthV14 | lvcfTextV14 | lvcfSubItemV14, Cx: col.width, PszText: text, ISubItem: int32(i)}
		pSendMessageW.Call(detailListViewV14, lvmInsertColumnWV14, uintptr(i), uintptr(unsafe.Pointer(&c)))
	}

	add := createUIControlV12("BUTTON", "新增明細", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, x+1270, y+4, 145, 34, parent, 1401)
	edit := createUIControlV12("BUTTON", "編輯明細", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, x+1270, y+48, 145, 34, parent, 1402)
	remove := createUIControlV12("BUTTON", "刪除明細", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, x+1270, y+92, 145, 34, parent, 1403)
	registerDetailWidgetV12(add, x+1270, y+4, 145, 34)
	registerDetailWidgetV12(edit, x+1270, y+48, 145, 34)
	registerDetailWidgetV12(remove, x+1270, y+92, 145, 34)
	registerButtonActionV12(add, func() { openDetailEditorV14(-1) }, "primary")
	registerButtonActionV12(edit, editSelectedDetailV14, "")
	registerButtonActionV12(remove, deleteSelectedDetailV14, "")

	createUIControlV12("STATIC", "單位有指定時：品號 → F2選單位 → 數量。批號點選機制後續處理。", WS_CHILD|WS_VISIBLE|SS_LEFT, x+1270, y+144, 205, 64, parent, 0)

	// Hidden marker keeps legacy anySelected() compatible when the user only
	// wants to input detail rows. It carries no ERP value.
	detailMirrorV14 = createCtrl("BUTTON", "", WS_CHILD|BS_AUTOCHECKBOX, 0, 0, 1, 1, parent, nextID)
	fields = append(fields, &Field{Key: "detail_rv14_active", Group: "明細", Label: "ListView", ApplyHwnd: detailMirrorV14})
	nextID++
	refreshDetailListV14()
}

func refreshDetailListV14() {
	if detailListViewV14 == 0 {
		return
	}
	pSendMessageW.Call(detailListViewV14, lvmDeleteAllV14, 0, 0)
	for i, row := range detailRowsV14 {
		texts := []string{fmt.Sprintf("%d", i+1), row.ItemCode, row.Unit, row.Qty, row.GiftQty, row.Batch, row.Warehouse, row.UnitPrice}
		for sub, s := range texts {
			text := wstr(s)
			item := lvItemV14{Mask: lvifTextV14, IItem: int32(i), ISubItem: int32(sub), PszText: text}
			msg := uintptr(lvmSetItemWV14)
			if sub == 0 {
				msg = lvmInsertItemWV14
			}
			pSendMessageW.Call(detailListViewV14, msg, 0, uintptr(unsafe.Pointer(&item)))
		}
		setDetailRowCheckedV14(i, row.Enabled)
	}
	if detailMirrorV14 != 0 {
		state := uintptr(BST_UNCHECKED)
		if len(detailRowsV14) > 0 {
			state = BST_CHECKED
		}
		pSendMessageW.Call(detailMirrorV14, BM_SETCHECK, state, 0)
	}
}

func detailRowCheckedV14(index int) bool {
	if detailListViewV14 == 0 || index < 0 || index >= len(detailRowsV14) {
		return false
	}
	state, _, _ := pSendMessageW.Call(detailListViewV14, lvmGetItemStateV14, uintptr(index), lvisStateImageV14)
	return uint32(state)&lvisStateImageV14 == uint32(2<<12)
}

func setDetailRowCheckedV14(index int, checked bool) {
	if detailListViewV14 == 0 || index < 0 {
		return
	}
	image := uint32(1 << 12)
	if checked {
		image = uint32(2 << 12)
	}
	item := lvItemV14{State: image, StateMask: lvisStateImageV14}
	pSendMessageW.Call(detailListViewV14, lvmSetItemStateV14, uintptr(index), uintptr(unsafe.Pointer(&item)))
}

func syncDetailChecksV14() {
	for i := range detailRowsV14 {
		detailRowsV14[i].Enabled = detailRowCheckedV14(i)
	}
}

func setAllDetailListChecksV14(selected bool) {
	if detailListViewV14 == 0 {
		return
	}
	for i := range detailRowsV14 {
		detailRowsV14[i].Enabled = selected
		setDetailRowCheckedV14(i, selected)
	}
	if detailMirrorV14 != 0 {
		state := uintptr(BST_UNCHECKED)
		if len(detailRowsV14) > 0 {
			state = BST_CHECKED
		}
		pSendMessageW.Call(detailMirrorV14, BM_SETCHECK, state, 0)
	}
}

func selectedDetailIndexV14() int {
	if detailListViewV14 == 0 {
		return -1
	}
	r, _, _ := pSendMessageW.Call(detailListViewV14, lvmGetNextItemV14, ^uintptr(0), lvniSelectedV14)
	if r == ^uintptr(0) {
		return -1
	}
	idx := int(r)
	if idx < 0 || idx >= len(detailRowsV14) {
		return -1
	}
	return idx
}

func editSelectedDetailV14() {
	idx := selectedDetailIndexV14()
	if idx < 0 {
		setStatus("請先在商品明細 ListView 選取一列")
		return
	}
	openDetailEditorV14(idx)
}

func deleteSelectedDetailV14() {
	idx := selectedDetailIndexV14()
	if idx < 0 {
		setStatus("請先在商品明細 ListView 選取一列")
		return
	}
	syncDetailChecksV14()
	detailRowsV14 = append(detailRowsV14[:idx], detailRowsV14[idx+1:]...)
	refreshDetailListV14()
	setStatus("已刪除商品明細")
}

func ensureDetailEditorClassV14() {
	if detailEditorClassV14 {
		return
	}
	detailEditorWndProcV14CB = syscall.NewCallback(detailEditorWndProcV14)
	hInst, _, _ := pGetModuleHandleW.Call(0)
	className := wstr("CYERPAutoInputDetailEditorV14")
	wc := WNDCLASSEX{CbSize: uint32(unsafe.Sizeof(WNDCLASSEX{})), LpfnWndProc: detailEditorWndProcV14CB, HInstance: hInst, HbrBackground: COLOR_WINDOW + 1, LpszClassName: className}
	pRegisterClassExW.Call(uintptr(unsafe.Pointer(&wc)))
	detailEditorClassV14 = true
}

func openDetailEditorV14(index int) {
	if detailEditorV14 != 0 {
		pSetForeground.Call(detailEditorV14)
		return
	}
	if index >= len(detailRowsV14) {
		return
	}
	ensureDetailEditorClassV14()
	detailEditorIndexV14 = index
	detailEditorFieldsV14 = map[string]uintptr{}
	hInst, _, _ := pGetModuleHandleW.Call(0)
	title := "新增商品明細"
	if index >= 0 {
		title = "編輯商品明細"
	}
	hwnd, _, _ := pCreateWindowExW.Call(0, uintptr(unsafe.Pointer(wstr("CYERPAutoInputDetailEditorV14"))), uintptr(unsafe.Pointer(wstr(title))), WS_OVERLAPPEDWINDOW|WS_VISIBLE, 260, 180, 610, 430, mainHwnd, 0, hInst, 0)
	detailEditorV14 = hwnd

	labels := []struct{ key, label string }{
		{"item_code", "品號"}, {"unit", "單位"}, {"qty", "數量"}, {"gift_qty", "贈/備品量"},
		{"batch", "批號"}, {"warehouse", "庫別"}, {"unit_price", "單價"},
	}
	values := map[string]string{}
	if index >= 0 {
		r := detailRowsV14[index]
		values = map[string]string{"item_code": r.ItemCode, "unit": r.Unit, "qty": r.Qty, "gift_qty": r.GiftQty, "batch": r.Batch, "warehouse": r.Warehouse, "unit_price": r.UnitPrice}
	}
	for i, row := range labels {
		y := int32(28 + i*42)
		createUIControlV12("STATIC", row.label, WS_CHILD|WS_VISIBLE|SS_LEFT, 28, y+4, 105, 22, hwnd, 0)
		detailEditorFieldsV14[row.key] = createUIControlV12("EDIT", values[row.key], WS_CHILD|WS_VISIBLE|WS_BORDER|ES_AUTOHSCROLL|WS_TABSTOP, 140, y, 390, 26, hwnd, uint16(1450+i))
	}
	createUIControlV12("STATIC", "單位有填時，ERP 會先選單位再輸入數量；批號目前只保存資料，尚未自動點選。", WS_CHILD|WS_VISIBLE|SS_LEFT, 28, 326, 510, 22, hwnd, 0)
	save := createUIControlV12("BUTTON", "儲存", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 330, 354, 96, 34, hwnd, 1491)
	cancel := createUIControlV12("BUTTON", "取消", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 438, 354, 96, 34, hwnd, 1492)
	registerButtonActionV12(save, saveDetailEditorV14, "primary")
	registerButtonActionV12(cancel, func() { if detailEditorV14 != 0 { pDestroyWindowV12.Call(detailEditorV14) } }, "")
	if h := detailEditorFieldsV14["item_code"]; h != 0 {
		pSetFocusV14.Call(h)
	}
}

func saveDetailEditorV14() {
	item := strings.TrimSpace(getWindowText(detailEditorFieldsV14["item_code"]))
	if item == "" {
		pMessageBoxW.Call(detailEditorV14, uintptr(unsafe.Pointer(wstr("品號為必填欄位。"))), uintptr(unsafe.Pointer(wstr("商品明細"))), MB_OK|MB_ICONINFORMATION)
		return
	}
	row := detailRowV14{
		Enabled:   true,
		ItemCode:  item,
		Unit:      strings.TrimSpace(getWindowText(detailEditorFieldsV14["unit"])),
		Qty:       strings.TrimSpace(getWindowText(detailEditorFieldsV14["qty"])),
		GiftQty:   strings.TrimSpace(getWindowText(detailEditorFieldsV14["gift_qty"])),
		Batch:     strings.TrimSpace(getWindowText(detailEditorFieldsV14["batch"])),
		Warehouse: strings.TrimSpace(getWindowText(detailEditorFieldsV14["warehouse"])),
		UnitPrice: strings.TrimSpace(getWindowText(detailEditorFieldsV14["unit_price"])),
	}
	if detailEditorIndexV14 >= 0 && detailEditorIndexV14 < len(detailRowsV14) {
		syncDetailChecksV14()
		row.Enabled = detailRowsV14[detailEditorIndexV14].Enabled
		detailRowsV14[detailEditorIndexV14] = row
	} else {
		detailRowsV14 = append(detailRowsV14, row)
	}
	refreshDetailListV14()
	setStatus("商品明細已更新")
	if detailEditorV14 != 0 {
		pDestroyWindowV12.Call(detailEditorV14)
	}
}

func detailEditorWndProcV14(hwnd uintptr, msg uint32, wParam, lParam uintptr) uintptr {
	if msg == wmCloseV12 {
		pDestroyWindowV12.Call(hwnd)
		return 0
	}
	if msg == WM_DESTROY {
		if hwnd == detailEditorV14 {
			detailEditorV14 = 0
			detailEditorIndexV14 = -1
			detailEditorFieldsV14 = map[string]uintptr{}
		}
		return 0
	}
	r, _, _ := pDefWindowProcW.Call(hwnd, uintptr(msg), wParam, lParam)
	return r
}

func fillDetailSelectedV14(root uintptr) (ok, fail int) {
	if detailListViewV14 == 0 {
		return 0, 0
	}
	syncDetailChecksV14()
	selected := make([]detailRowV14, 0, len(detailRowsV14))
	for _, row := range detailRowsV14 {
		if row.Enabled {
			if strings.TrimSpace(row.ItemCode) == "" {
				logError("明細", "品號", "DETAIL_ITEM_REQUIRED", "ListView 已勾選列的品號不可空白")
				return 0, 1
			}
			selected = append(selected, row)
		}
	}
	if len(selected) == 0 {
		return 0, 0
	}

	grid := findDetailGridSite(root)
	if grid == nil {
		logError("明細", "TcxGrid", "GRID_NOT_FOUND", "找不到可見的標準明細 TcxGridSite")
		return 0, 1
	}
	logf("INFO", "detail V14 ListView start selected_rows=%d grid=0x%x", len(selected), grid.Hwnd)
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

		// Unit is intentionally before quantity. ERP quantity meaning depends on
		// the selected conversion unit (for example, inventory-unit conversion).
		if strings.TrimSpace(row.Unit) != "" {
			if !selectDetailUnitV14(root, *grid, rowIndex, row.Unit) {
				logError("明細", fmt.Sprintf("第%d列單位", rowIndex+1), "UNIT_LOOKUP_FAILED", "F2 單位查詢無法確認指定列；已停止本列後續輸入")
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
			logf("INFO", "detail V14 batch deferred row=%d requested_len=%d", rowIndex+1, len([]rune(strings.TrimSpace(row.Batch))))
		}
	}
	logf("INFO", "detail V14 ListView end selected_rows=%d", len(selected))
	return ok, fail
}

func selectDetailUnitV14(root uintptr, grid ControlInfo, row int, wanted string) bool {
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
		logf("WARN", "unit lookup V14 window not found row=%d requested_len=%d", row+1, len([]rune(wanted)))
		return false
	}
	prepareLookupWindowV14(lookup)
	if !selectLookupUnitRowV14(lookup, wanted) {
		setStatus("ERP：單位 F2 查詢已開啟，但目前無法可靠讀取指定單位；視窗保留供診斷")
		return false
	}
	if !clickLookupButtonV14(lookup, "確定") {
		logf("WARN", "unit lookup V14 confirm button not found")
		return false
	}
	if !interruptibleSleep(260 * time.Millisecond) {
		return false
	}
	prepareERPWindow(root)
	logf("INFO", "unit lookup V14 selected row=%d requested_len=%d", row+1, len([]rune(wanted)))
	return true
}

func waitUnitLookupWindowV14(root uintptr, timeout time.Duration) uintptr {
	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		if isStopRequested() {
			return 0
		}
		var found uintptr
		cb := syscall.NewCallback(func(hwnd, lParam uintptr) uintptr {
			if hwnd == root {
				return 1
			}
			vis, _, _ := pIsWindowVisible.Call(hwnd)
			if vis == 0 {
				return 1
			}
			title := strings.TrimSpace(getWindowText(hwnd))
			if strings.Contains(title, "F2開窗查詢") {
				found = hwnd
				return 0
			}
			return 1
		})
		pEnumWindows.Call(cb, 0)
		if found != 0 {
			return found
		}
		time.Sleep(45 * time.Millisecond)
	}
	return 0
}

func prepareLookupWindowV14(hwnd uintptr) {
	pBringWindowToTop.Call(hwnd)
	pSetForeground.Call(hwnd)
	pUpdateWindow.Call(hwnd)
	time.Sleep(120 * time.Millisecond)
}

func lookupGridV14(lookup uintptr) *ControlInfo {
	all := enumControls(lookup)
	var best *ControlInfo
	var bestArea int64
	for i := range all {
		c := &all[i]
		if !c.Visible || !strings.EqualFold(c.Class, "TcxGridSite") {
			continue
		}
		area := int64(c.Rect.Right-c.Rect.Left) * int64(c.Rect.Bottom-c.Rect.Top)
		if area > bestArea {
			bestArea = area
			best = c
		}
	}
	return best
}

func selectLookupUnitRowV14(lookup uintptr, wanted string) bool {
	grid := lookupGridV14(lookup)
	if grid == nil {
		controls := enumControls(lookup)
		logf("WARN", "unit lookup V14 grid not found controls=%d", len(controls))
		return false
	}
	gw := grid.Rect.Right - grid.Rect.Left
	x := grid.Rect.Left + int32(float64(gw)*0.29)
	y := grid.Rect.Top + 62
	if y > grid.Rect.Bottom-10 {
		y = grid.Rect.Top + 20
	}
	clickScreenPoint(x, y)
	if !interruptibleSleep(120 * time.Millisecond) {
		return false
	}

	const maxProbe = 40
	for probe := 0; probe < maxProbe; probe++ {
		if isStopRequested() {
			return false
		}
		if lookupHasExactReadableTextV14(lookup, wanted) {
			logf("INFO", "unit lookup V14 readable exact match probe=%d requested_len=%d", probe+1, len([]rune(wanted)))
			return true
		}
		pressVK(VK_DOWN)
		if !interruptibleSleep(85 * time.Millisecond) {
			return false
		}
	}
	controls := enumControls(lookup)
	readable := 0
	for _, c := range controls {
		if c.Visible && strings.TrimSpace(c.Text) != "" {
			readable++
		}
	}
	focus := focusedControlOfForeground(lookup)
	logf("WARN", "unit lookup V14 no readable exact match probes=%d controls=%d readable_controls=%d focus_class=%q grid_class=%q", maxProbe, len(controls), readable, className(focus), grid.Class)
	return false
}

func lookupHasExactReadableTextV14(lookup uintptr, wanted string) bool {
	want := strings.TrimSpace(wanted)
	focus := focusedControlOfForeground(lookup)
	if focus != 0 && strings.TrimSpace(getWindowText(focus)) == want {
		return true
	}
	for _, c := range enumControls(lookup) {
		if !c.Visible {
			continue
		}
		if strings.TrimSpace(c.Text) == want {
			// If the text is an actual child HWND, click it so that the row/cell is
			// active before pressing the dialog's 確定 button.
			r := c.Rect
			if r.Right > r.Left && r.Bottom > r.Top {
				clickScreenPoint((r.Left+r.Right)/2, (r.Top+r.Bottom)/2)
				time.Sleep(90 * time.Millisecond)
			}
			return true
		}
	}
	return false
}

func clickLookupButtonV14(lookup uintptr, text string) bool {
	want := normalize(text)
	for _, c := range enumControls(lookup) {
		if !c.Visible || normalize(c.Text) != want {
			continue
		}
		r := c.Rect
		if r.Right <= r.Left || r.Bottom <= r.Top {
			continue
		}
		clickScreenPoint((r.Left+r.Right)/2, (r.Top+r.Bottom)/2)
		return true
	}
	return false
}
