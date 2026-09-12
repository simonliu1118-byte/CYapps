//go:build windows

package main

import (
	"fmt"
	"syscall"
	"unsafe"

	"cyinvoice/internal/appdata"
)

const (
	baseClientWidth  = 1164
	baseClientHeight = 811

	wmSize           = 0x0005
	wmSetRedraw      = 0x000B
	wmGetMinMaxInfo  = 0x0024
	wmNotify         = 0x004E
	wmCtlColorBtn    = 0x0135
	wmCtlColorEdit   = 0x0133
	wmCtlColorStatic = 0x0138
	wmSetFocus       = 0x0007
	wmKillFocus      = 0x0008
	wmKeyDown        = 0x0100
	emSetSel         = 0x00B1
	vkReturn         = 0x000D
	vkEscape         = 0x001B

	ssCenter           = 0x0001
	ssRight            = 0x0002
	ssEtchedHorizontal = 0x0010
	ssCenterImage      = 0x0200

	dtCenter     = 0x0001
	dtVCenter    = 0x0004
	dtSingleLine = 0x0020

	tcmFirst       = 0x1300
	tcmGetItemRect = tcmFirst + 10
	tcmGetCurSel   = tcmFirst + 11
	tcmSetCurSel   = tcmFirst + 12
	tcmSetPadding  = tcmFirst + 43
	tcmInsertItemW = tcmFirst + 62
	tcifText       = 0x0001
	tcnSelChange   = -551
	tcsFocusNever  = 0x8000

	lvsReport               = 0x0001
	lvsSingleSel            = 0x0004
	lvsShowSelAlways        = 0x0008
	lvsNoSortHeader          = 0x8000
	lvsExGridLines          = 0x00000001
	lvsExCheckboxes         = 0x00000004
	lvsExFullRowSelect      = 0x00000020
	lvsExDoubleBuffer       = 0x00010000
	lvmFirst                = 0x1000
	lvmGetItemCount         = lvmFirst + 4
	lvmDeleteItem           = lvmFirst + 8
	lvmDeleteAllItems       = lvmFirst + 9
	lvmGetNextItem          = lvmFirst + 12
	lvmEnsureVisible        = lvmFirst + 19
	lvmSetColumnWidth       = lvmFirst + 30
	lvmGetHeader            = lvmFirst + 31
	lvmGetCountPerPage      = lvmFirst + 40
	lvmSetItemState         = lvmFirst + 43
	lvmGetItemState         = lvmFirst + 44
	lvmSetExtendedListStyle = lvmFirst + 54
	lvmGetSubItemRect       = lvmFirst + 56
	lvmInsertItemW          = lvmFirst + 77
	lvmSetColumnW           = lvmFirst + 96
	lvmInsertColumnW        = lvmFirst + 97
	lvmSetItemTextW         = lvmFirst + 116
	lvifText                = 0x0001
	lvifState               = 0x0008
	lvisFocused             = 0x0001
	lvisSelected            = 0x0002
	lvisStateImageMask      = 0xF000
	lvniSelected            = 0x0002
	nmClick                 = -2
	nmDblClk                = -3
	nmCustomDraw            = -12
	lvnItemChanged          = -101
	cddsPrePaint            = 0x00000001
	cddsItemPrePaint        = 0x00010001
	cddsSubItem             = 0x00020000
	cdrfNewFont             = 0x00000002
	cdrfSkipDefault         = 0x00000004
	cdrfNotifyItemDraw      = 0x00000020
	cdrfNotifySubItemDraw   = 0x00000020
	cdisSelected            = 0x0001
	cdisFocus               = 0x0010
	lvcfFmt                 = 0x0001
	lvcfWidth               = 0x0002
	lvcfText                = 0x0004
	lvcfmtLeft              = 0x0000
	lvcfmtRight             = 0x0001
	lvirBounds              = 0

	cbsDropDownList = 0x0003
	cbAddString     = 0x0143
	cbGetCurSel     = 0x0147
	cbSetCurSel     = 0x014E

	dfcButton      = 4
	dfcsButtonPush = 0x0010
	hdsButtons      = 0x0002
	hdsHotTrack     = 0x0004
	hdsDragDrop     = 0x0040
	hdsFullDrag     = 0x0080
	hdsNoSizing     = 0x0800
	sbHorz      = 0
	sbVert      = 1
	sbsVert     = 0x0001
	smCxVScroll = 2
)

var (
	comctl32 = syscall.NewLazyDLL("comctl32.dll")
	uxtheme  = syscall.NewLazyDLL("uxtheme.dll")
	procInitCommonControlsEx = comctl32.NewProc("InitCommonControlsEx")
	procSetWindowTheme = uxtheme.NewProc("SetWindowTheme")
	procMoveWindow = user32.NewProc("MoveWindow")
	procGetClientRect = user32.NewProc("GetClientRect")
	procGetWindowRect = user32.NewProc("GetWindowRect")
	procInvalidateRect = user32.NewProc("InvalidateRect")
	procFillRect = user32.NewProc("FillRect")
	procDrawFrameControl = user32.NewProc("DrawFrameControl")
	procDrawTextW = user32.NewProc("DrawTextW")
	procShowScrollBar = user32.NewProc("ShowScrollBar")
	procGetSystemMetrics = user32.NewProc("GetSystemMetrics")
	procGetWindowLongPtrW = user32.NewProc("GetWindowLongPtrW")
	procSetTextColor = gdi32.NewProc("SetTextColor")
	procSetBkColor = gdi32.NewProc("SetBkColor")
	procSetBkMode = gdi32.NewProc("SetBkMode")
	procCreateSolidBrush = gdi32.NewProc("CreateSolidBrush")
	procCreateFontW = gdi32.NewProc("CreateFontW")
	procSelectObject = gdi32.NewProc("SelectObject")
	procSetWindowLongPtrW = user32.NewProc("SetWindowLongPtrW")
	procCallWindowProcW = user32.NewProc("CallWindowProcW")

	baseRects = map[uintptr]uiRect{}
	colorStyles = map[uintptr]staticColor{}
	bannerBrush uintptr
	whiteBrush uintptr
	disabledEditBrush uintptr
	panelTitleBrush uintptr
	uiFont uintptr
	contentFont uintptr
	smallFont uintptr
	apiReasonFont uintptr
	contentStrikeFont uintptr
	boldFont uintptr
	bannerFont uintptr
	tabHandle uintptr
	recordsList uintptr
	invoiceItemsList uintptr
	recordColumnWidths []int
	invoiceListScrollPlaceholders = map[uintptr]uintptr{}
	productCellEditor uintptr
	productCellEditorOriginalProc uintptr
	productCellEditorRow = -1
	productCellEditorColumn = -1
	productEditCallback uintptr
)

var gwlpWndProc = ^uintptr(3)

type uiRect struct{ X, Y, W, H int }
type winRect struct{ Left, Top, Right, Bottom int32 }
type minMaxInfo struct {
	Reserved point
	MaxSize point
	MaxPosition point
	MinTrackSize point
	MaxTrackSize point
}
type initCommonControlsEx struct{ Size, ICC uint32 }
type nmHdr struct{ WindowFrom, IDFrom uintptr; Code uint32 }
type nmItemActivate struct {
	Header nmHdr
	Item, SubItem int32
	NewState, OldState, Changed uint32
	Point point
	Param uintptr
	KeyFlags uint32
}
type tcItem struct {
	Mask, State, StateMask uint32
	Text *uint16
	TextMax int32
	Image int32
	Param uintptr
}
type lvColumn struct {
	Mask int32
	Fmt int32
	CX int32
	Text *uint16
	TextMax int32
	SubItem int32
	Image int32
	Order int32
	MinWidth int32
	DefaultWidth int32
	IdealWidth int32
}
type lvItem struct {
	Mask uint32
	Item, SubItem int32
	State, StateMask uint32
	Text *uint16
	TextMax int32
	Image int32
	Param uintptr
	Indent int32
	GroupID int32
	Columns uint32
	ColumnPointer *uint32
	ColumnFormatPointer *int32
	Group int32
}
type staticColor struct{ Text, Background uint32; Brush uintptr; Transparent bool }
type nmCustomDrawInfo struct {
	Header nmHdr
	DrawStage uint32
	DC uintptr
	Rect winRect
	ItemSpec uintptr
	ItemState uint32
	ItemParam uintptr
}
type nmListViewCustomDraw struct {
	Draw nmCustomDrawInfo
	TextColor uint32
	TextBackground uint32
	SubItem int32
}
func initNativeUI() {
	productEditCallback = syscall.NewCallback(productEditWindowProc)
	controls := initCommonControlsEx{Size: uint32(unsafe.Sizeof(initCommonControlsEx{})), ICC: 0x00000001 | 0x00004000}
	procInitCommonControlsEx.Call(uintptr(unsafe.Pointer(&controls)))
	bannerBrush, _, _ = procCreateSolidBrush.Call(uintptr(rgb(238, 246, 255)))
	whiteBrush, _, _ = procCreateSolidBrush.Call(uintptr(rgb(255, 255, 255)))
	disabledEditBrush, _, _ = procCreateSolidBrush.Call(uintptr(rgb(238, 238, 238)))
	panelTitleBrush, _, _ = procCreateSolidBrush.Call(uintptr(rgb(240, 240, 240)))
	uiFont = createUIFont(-17, 400)
	contentFont = createUIFont(-16, 400)
	smallFont = createUIFont(-14, 400)
	apiReasonFont = createUIFont(-11, 400)
	contentStrikeFont = createUIFontWithStrike(-16, 400, true)
	boldFont = createUIFont(-17, 700)
	bannerFont = createUIFont(-19, 700)
}

func productEditWindowProc(window uintptr, message uint32, wParam, lParam uintptr) uintptr {
	original := productCellEditorOriginalProc
	if original == 0 {
		result, _, _ := procDefWindowProcW.Call(window, uintptr(message), wParam, lParam)
		return result
	}
	if message == wmSetFocus {
		result, _, _ := procCallWindowProcW.Call(original, window, uintptr(message), wParam, lParam)
		procSendMessageW.Call(window, emSetSel, 0, ^uintptr(0))
		return result
	}
	if message == wmKeyDown && (wParam == vkReturn || wParam == vkTab) {
		row, column := productCellEditorRow, productCellEditorColumn
		commitProductCellEdit(false)
		refreshManualRows()
		refreshTotals()
		switch column {
		case 1:
			beginProductCellEdit(row, 3)
		case 3:
			beginProductCellEdit(row, 4)
		case 4:
			if wParam == vkTab { procSetFocus.Call(handles[idRemark]) }
		}
		return 0
	}
	if message == wmKeyDown && wParam == vkEscape {
		commitProductCellEdit(true)
		procSetFocus.Call(invoiceItemsList)
		return 0
	}
	if message == wmKillFocus {
		result, _, _ := procCallWindowProcW.Call(original, window, uintptr(message), wParam, lParam)
		commitProductCellEdit(false)
		refreshManualRows()
		refreshTotals()
		return result
	}
	result, _, _ := procCallWindowProcW.Call(original, window, uintptr(message), wParam, lParam)
	return result
}

func beginProductCellEdit(row, column int) {
	if row < 0 || row >= len(manualRows) || (column != 1 && column != 3 && column != 4) { return }
	commitProductCellEdit(false)
	activeManualRow = row
	rect, ok := productSubItemRect(row, column)
	if !ok { return }
	text := manualRows[row].Description
	if column == 3 { text = manualRows[row].Quantity }
	if column == 4 { text = manualRows[row].UnitPrice }
	className := mustUTF16Ptr("EDIT")
	value := mustUTF16Ptr(text)
	handle, _, _ := procCreateWindowExW.Call(0, uintptr(unsafe.Pointer(className)), uintptr(unsafe.Pointer(value)),
		wsChild|wsVisible|wsTabStop|esAutoHScroll,
		uintptr(rect.Left+3), uintptr(rect.Top+1), uintptr(rect.Right-rect.Left-6), uintptr(rect.Bottom-rect.Top-2),
		invoiceItemsList, 0, 0, 0)
	if handle == 0 { return }
	productCellEditor = handle
	productCellEditorRow = row
	productCellEditorColumn = column
	procSendMessageW.Call(handle, wmSetFont, contentFont, 1)
	limit := uintptr(64)
	if column == 1 { limit = 256 }
	procSendMessageW.Call(handle, 0x00C5, limit, 0)
	original, _, _ := procSetWindowLongPtrW.Call(handle, gwlpWndProc, productEditCallback)
	productCellEditorOriginalProc = original
	procSetFocus.Call(handle)
	procSendMessageW.Call(handle, emSetSel, 0, ^uintptr(0))
}

func commitProductCellEdit(cancel bool) {
	if productCellEditor == 0 { return }
	handle := productCellEditor
	row, column := productCellEditorRow, productCellEditorColumn
	value := controlText(handle)
	productCellEditor = 0
	productCellEditorRow = -1
	productCellEditorColumn = -1
	productCellEditorOriginalProc = 0
	if !cancel && row >= 0 && row < len(manualRows) {
		switch column {
		case 1:
			manualRows[row].Description = value
		case 3:
			manualRows[row].Quantity = value
		case 4:
			manualRows[row].UnitPrice = value
		}
		setListViewCellText(invoiceItemsList, row, column, value)
		if column == 3 || column == 4 { setListViewCellText(invoiceItemsList, row, 5, manualRows[row].amountText()) }
	}
	procDestroyWindow.Call(handle)
}

func productSubItemRect(row, column int) (winRect, bool) {
	rect := winRect{Top: int32(column), Left: lvirBounds}
	result, _, _ := procSendMessageW.Call(invoiceItemsList, lvmGetSubItemRect, uintptr(row), uintptr(unsafe.Pointer(&rect)))
	return rect, result != 0 && rect.Right > rect.Left && rect.Bottom > rect.Top
}

func positionProductCellEditor() {
	if productCellEditor == 0 { return }
	rect, ok := productSubItemRect(productCellEditorRow, productCellEditorColumn)
	if !ok { return }
	procMoveWindow.Call(productCellEditor, uintptr(rect.Left+3), uintptr(rect.Top+1), uintptr(rect.Right-rect.Left-6), uintptr(rect.Bottom-rect.Top-2), 1)
}

func productDeleteButtonRect(row int) (winRect, bool) {
	rect, ok := productSubItemRect(row, 6)
	if !ok { return rect, false }
	rect.Left += 6
	rect.Right -= 6
	rect.Top += 1
	rect.Bottom -= 1
	return rect, true
}

func pointInside(rect winRect, point point) bool {
	return point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom
}

func drawProductDeleteButton(draw *nmListViewCustomDraw, row int) {
	cell, ok := productSubItemRect(row, 6)
	if !ok { return }
	brush, _, _ := procCreateSolidBrush.Call(uintptr(zebraColor(row)))
	procFillRect.Call(draw.Draw.DC, uintptr(unsafe.Pointer(&cell)), brush)
	procDeleteObject.Call(brush)
	rect, ok := productDeleteButtonRect(row)
	if !ok { return }
	procDrawFrameControl.Call(draw.Draw.DC, uintptr(unsafe.Pointer(&rect)), dfcButton, dfcsButtonPush)
	procSetTextColor.Call(draw.Draw.DC, uintptr(rgb(176, 32, 32)))
	procSetBkMode.Call(draw.Draw.DC, 1)
	oldFont, _, _ := procSelectObject.Call(draw.Draw.DC, contentFont)
	text := mustUTF16Ptr("刪除")
	procDrawTextW.Call(draw.Draw.DC, uintptr(unsafe.Pointer(text)), ^uintptr(0), uintptr(unsafe.Pointer(&rect)), dtCenter|dtVCenter|dtSingleLine)
	procSelectObject.Call(draw.Draw.DC, oldFont)
}

func createUIFont(height, weight int32) uintptr { return createUIFontWithStrike(height, weight, false) }

func createUIFontWithStrike(height, weight int32, strikeout bool) uintptr {
	face := mustUTF16Ptr("Microsoft JhengHei UI")
	strike := uintptr(0)
	if strikeout { strike = 1 }
	font, _, _ := procCreateFontW.Call(uintptr(height), 0, 0, 0, uintptr(weight), 0, 0, strike, 1, 0, 0, 5, 0, uintptr(unsafe.Pointer(face)))
	return font
}

func handleRecordsCustomDraw(lParam unsafe.Pointer) (uintptr, bool) {
	if lParam == nil || recordsList == 0 { return 0, false }
	draw := (*nmListViewCustomDraw)(lParam)
	if draw.Draw.Header.WindowFrom != recordsList || int32(draw.Draw.Header.Code) != nmCustomDraw { return 0, false }
	switch draw.Draw.DrawStage {
	case cddsPrePaint:
		return cdrfNotifyItemDraw, true
	case cddsItemPrePaint:
		return cdrfNotifySubItemDraw, true
	case cddsItemPrePaint | cddsSubItem:
		row := int(draw.Draw.ItemSpec)
		draw.Draw.ItemState &^= cdisSelected | cdisFocus
		if row >= 0 && row < len(recordRowStyles) && draw.SubItem == 9 && recordHasUploadIndicator(recordRowStyles[row]) {
			drawRecordUploadIndicator(draw, recordRowStyles[row])
			return cdrfSkipDefault, true
		}
		applyRecordRowStyle(draw)
		return cdrfNewFont, true
	default:
		return 0, true
	}
}

func handleProductCustomDraw(lParam unsafe.Pointer) (uintptr, bool) {
	if lParam == nil || invoiceItemsList == 0 { return 0, false }
	draw := (*nmListViewCustomDraw)(lParam)
	if draw.Draw.Header.WindowFrom != invoiceItemsList || int32(draw.Draw.Header.Code) != nmCustomDraw { return 0, false }
	switch draw.Draw.DrawStage {
	case cddsPrePaint:
		return cdrfNotifyItemDraw, true
	case cddsItemPrePaint:
		return cdrfNotifySubItemDraw, true
	case cddsItemPrePaint | cddsSubItem:
		row := int(draw.Draw.ItemSpec)
		draw.Draw.ItemState &^= cdisSelected | cdisFocus
		if draw.SubItem == 6 && row >= 0 && row < len(manualRows) {
			drawProductDeleteButton(draw, row)
			return cdrfSkipDefault, true
		}
		draw.TextBackground = zebraColor(row)
		draw.TextColor = rgb(0, 0, 0)
		if draw.SubItem == 5 {
			draw.TextBackground = readonlyZebraColor(row)
			draw.TextColor = rgb(96, 96, 96)
		}
		procSelectObject.Call(draw.Draw.DC, contentFont)
		return cdrfNewFont, true
	default:
		return 0, true
	}
}

func applyRecordRowStyle(draw *nmListViewCustomDraw) {
	row := int(draw.Draw.ItemSpec)
	draw.TextBackground = zebraColor(row)
	draw.TextColor = rgb(0, 0, 0)
	font := contentFont
	if row < 0 || row >= len(recordRowStyles) {
		procSelectObject.Call(draw.Draw.DC, font)
		return
	}
	style := recordRowStyles[row]
	switch style.State {
	case appdata.InvoiceStateFailed:
		draw.TextColor = rgb(210, 0, 0)
	case appdata.InvoiceStateUnknown, appdata.InvoiceStateChanging:
		draw.TextColor = rgb(215, 116, 0)
	case appdata.InvoiceStateVoided:
		draw.TextColor = rgb(128, 128, 128)
		font = contentStrikeFont
	}
	procSelectObject.Call(draw.Draw.DC, font)
}

func zebraColor(row int) uint32 {
	if row%2 == 0 { return rgb(255, 255, 255) }
	return rgb(246, 246, 246)
}

func readonlyZebraColor(row int) uint32 {
	if row%2 == 0 { return rgb(241, 241, 241) }
	return rgb(234, 234, 234)
}

func rgb(red, green, blue byte) uint32 { return uint32(red) | uint32(green)<<8 | uint32(blue)<<16 }

func rememberRect(handle uintptr, x, y, width, height int) {
	if handle != 0 { baseRects[handle] = uiRect{X: x, Y: y, W: width, H: height} }
}

// relayoutGUI is the single main-window layout path. It owns control sizing and
// all list-column sizing. No refine/normalize pass may run after it.
func relayoutGUI(window uintptr) {
	if window == 0 || len(baseRects) == 0 { return }
	var client winRect
	procGetClientRect.Call(window, uintptr(unsafe.Pointer(&client)))
	width := int(client.Right - client.Left)
	height := int(client.Bottom - client.Top)
	if width <= 0 || height <= 0 { return }
	sx := float64(width) / float64(baseClientWidth)
	sy := float64(height) / float64(baseClientHeight)
	for handle, base := range baseRects {
		x := int(float64(base.X) * sx)
		y := int(float64(base.Y) * sy)
		w := int(float64(base.W) * sx)
		h := int(float64(base.H) * sy)
		if w < 1 { w = 1 }
		if h < 1 { h = 1 }
		procMoveWindow.Call(handle, uintptr(x), uintptr(y), uintptr(w), uintptr(h), 1)
	}
	layoutSettingsButtonToTab(sx, sy)
	syncRecordPlaceholderRows()
	layoutRecordColumnsToClient()
	layoutProductColumnsToClient()
	positionProductCellEditor()
	procInvalidateRect.Call(window, 0, 1)
}

func layoutProductColumnsToClient() {
	if invoiceItemsList == 0 { return }
	available := layoutInvoiceListChrome(invoiceItemsList)
	if available <= 0 { return }

	// There are exactly seven real columns. Their sum is the actual visible
	// SysHeader32 width, so 操作 ends at the permanently reserved scrollbar
	// gutter instead of leaving an unnamed header surface on its right.
	fixed := []int{50, 78, 82, 140, 148, 80}
	fixedTotal := 0
	for _, width := range fixed { fixedTotal += width }
	nameWidth := available - fixedTotal
	if nameWidth < 180 {
		// At supported CYInvoice window sizes this branch should not be needed,
		// but preserve the no-horizontal-scroll invariant if Windows metrics vary.
		deficit := 180 - nameWidth
		mins := []int{44, 68, 70, 118, 124, 68}
		for _, index := range []int{4, 3, 2, 1, 5, 0} {
			if deficit <= 0 { break }
			room := fixed[index] - mins[index]
			if room <= 0 { continue }
			shrink := room
			if shrink > deficit { shrink = deficit }
			fixed[index] -= shrink
			deficit -= shrink
		}
		fixedTotal = 0
		for _, width := range fixed { fixedTotal += width }
		nameWidth = available - fixedTotal
	}
	if nameWidth < 80 { nameWidth = 80 }
	widths := []int{fixed[0], nameWidth, fixed[1], fixed[2], fixed[3], fixed[4], fixed[5]}
	total := 0
	for _, width := range widths { total += width }
	widths[1] += available - total
	for index, columnWidth := range widths {
		procSendMessageW.Call(invoiceItemsList, lvmSetColumnWidth, uintptr(index), uintptr(columnWidth))
	}
	procShowScrollBar.Call(invoiceItemsList, sbHorz, 0)
	layoutInvoiceListChrome(invoiceItemsList)
}

var gwlStyle = ^uintptr(15)

func configureInvoiceListView(handle uintptr) {
	if handle == 0 { return }
	header, _, _ := procSendMessageW.Call(handle, lvmGetHeader, 0, 0)
	if header == 0 { return }
	// Keep the ListView and its scrollbar on the active Windows theme, but use
	// the native classic SysHeader32 renderer so the header bottom/divider lines
	// visually join the ListView grid.
	empty := mustUTF16Ptr("")
	procSetWindowTheme.Call(header, uintptr(unsafe.Pointer(empty)), uintptr(unsafe.Pointer(empty)))
	style, _, _ := procGetWindowLongPtrW.Call(header, gwlStyle)
	style &^= hdsButtons | hdsHotTrack | hdsDragDrop | hdsFullDrag
	style |= hdsNoSizing
	procSetWindowLongPtrW.Call(header, gwlStyle, style)
	procInvalidateRect.Call(header, 0, 1)
	layoutInvoiceListChrome(handle)
}

func createInvoiceListScrollPlaceholder(handle uintptr) uintptr {
	className := mustUTF16Ptr("SCROLLBAR")
	placeholder, _, err := procCreateWindowExW.Call(0, uintptr(unsafe.Pointer(className)), 0,
		wsChild|wsVisible|sbsVert, 0, 0, 1, 1, handle, 0, 0, 0)
	if placeholder == 0 { panic(fmt.Sprintf("create ListView scrollbar placeholder: %v", err)) }
	procEnableWindow.Call(placeholder, 0)
	invoiceListScrollPlaceholders[handle] = placeholder
	return placeholder
}

func invoiceListNeedsVerticalScroll(handle uintptr) bool {
	itemCount, _, _ := procSendMessageW.Call(handle, lvmGetItemCount, 0, 0)
	perPage, _, _ := procSendMessageW.Call(handle, lvmGetCountPerPage, 0, 0)
	return perPage > 0 && itemCount > perPage
}

// layoutInvoiceListChrome is the single native scrollbar-slot calculation for
// CYInvoice ListViews. When rows fit, a disabled themed SCROLLBAR child occupies
// the exact system-width slot. When rows overflow, that placeholder is hidden
// and the ListView's own scrollbar appears at the same position.
func layoutInvoiceListChrome(handle uintptr) int {
	if handle == 0 { return 0 }
	procShowScrollBar.Call(handle, sbHorz, 0)
	needsScroll := invoiceListNeedsVerticalScroll(handle)
	procShowScrollBar.Call(handle, sbVert, boolValue(needsScroll))

	var client winRect
	if result, _, _ := procGetClientRect.Call(handle, uintptr(unsafe.Pointer(&client))); result == 0 { return 0 }
	width := int(client.Right - client.Left)
	height := int(client.Bottom - client.Top)
	placeholder := invoiceListScrollPlaceholders[handle]
	if needsScroll {
		if placeholder != 0 { procShowWindow.Call(placeholder, swHide) }
		return width
	}
	scrollWidth, _, _ := procGetSystemMetrics.Call(smCxVScroll)
	if int(scrollWidth) <= 0 || int(scrollWidth) >= width { return width }
	contentWidth := width - int(scrollWidth)
	if placeholder != 0 {
		procMoveWindow.Call(placeholder, uintptr(contentWidth), 0, scrollWidth, uintptr(height), 1)
		procEnableWindow.Call(placeholder, 0)
		procShowWindow.Call(placeholder, swShow)
	}
	return contentWidth
}

func centerWindowOnParent(window, parent uintptr, width, height int) {
	if window == 0 { return }
	var bounds winRect
	if parent != 0 {
		if result, _, _ := procGetWindowRect.Call(parent, uintptr(unsafe.Pointer(&bounds))); result == 0 { parent = 0 }
	}
	if parent == 0 { bounds = winRect{Left: 0, Top: 0, Right: int32(baseClientWidth), Bottom: int32(baseClientHeight)} }
	x := int(bounds.Left) + (int(bounds.Right-bounds.Left)-width)/2
	y := int(bounds.Top) + (int(bounds.Bottom-bounds.Top)-height)/2
	if x < 0 { x = 0 }
	if y < 0 { y = 0 }
	procMoveWindow.Call(window, uintptr(x), uintptr(y), uintptr(width), uintptr(height), 1)
}

func setMinimumWindowSize(lParam unsafe.Pointer) {
	if lParam == nil { return }
	info := (*minMaxInfo)(lParam)
	info.MinTrackSize = point{X: mainWindowMinimumWidth, Y: mainWindowMinimumHeight}
}

func setStaticStyle(handle uintptr, text, background uint32, brush uintptr, transparent bool) {
	colorStyles[handle] = staticColor{Text: text, Background: background, Brush: brush, Transparent: transparent}
}

func handleStaticColor(dc, control uintptr) uintptr {
	style, ok := colorStyles[control]
	if !ok { return handlePlainControlColor(dc) }
	procSetTextColor.Call(dc, uintptr(style.Text))
	if style.Transparent { procSetBkMode.Call(dc, 1) } else { procSetBkMode.Call(dc, 2); procSetBkColor.Call(dc, uintptr(style.Background)) }
	return style.Brush
}

func handlePlainControlColor(dc uintptr) uintptr {
	procSetTextColor.Call(dc, uintptr(rgb(0, 0, 0)))
	procSetBkMode.Call(dc, 2)
	procSetBkColor.Call(dc, uintptr(rgb(240, 240, 240)))
	return panelTitleBrush
}

// Paint handlers are paint-only. They must never EnableWindow, change radio
// state, tax mode, buyer mode, or business data.
func handleButtonColor(dc uintptr) uintptr {
	procSetTextColor.Call(dc, uintptr(rgb(0, 0, 0)))
	procSetBkMode.Call(dc, 2)
	procSetBkColor.Call(dc, uintptr(rgb(240, 240, 240)))
	return panelTitleBrush
}

func addTab(parent uintptr, x, y, width, height int) uintptr {
	// The native tab control owns page selection, frame and notifications. Only
	// the two header items are owner-drawn from creation time for the requested
	// blue-underline appearance; there is no later style conversion or patch.
	handle := addControl("SysTabControl32", "", parent, x, y, width, height, wsChild|wsVisible|wsTabStop|tcsFocusNever|tcsOwnerDrawFixed, 0, nil)
	empty := mustUTF16Ptr("")
	procSetWindowTheme.Call(handle, uintptr(unsafe.Pointer(empty)), uintptr(unsafe.Pointer(empty)))
	// Apply the final sizing after the theme choice. The selected label uses
	// boldFont, so the native Tab reserves enough space before item insertion.
	procSendMessageW.Call(handle, wmSetFont, boldFont, 1)
	procSendMessageW.Call(handle, tcmSetPadding, 0, uintptr(10|(3<<16)))
	for index, title := range cyInvoiceTabTitles {
		value := mustUTF16Ptr(title)
		item := tcItem{Mask: tcifText, Text: value}
		procSendMessageW.Call(handle, tcmInsertItemW, uintptr(index), uintptr(unsafe.Pointer(&item)))
	}
	procSendMessageW.Call(handle, tcmSetCurSel, 0, 0)
	return handle
}

func handleNotifyMessage(lParam unsafe.Pointer) (uintptr, bool) {
	if lParam == nil { return 0, false }
	if result, handled := handleProductCustomDraw(lParam); handled { return result, true }
	if result, handled := handleRecordsCustomDraw(lParam); handled { return result, true }
	header := (*nmHdr)(lParam)
	if header.WindowFrom == tabHandle && int32(header.Code) == tcnSelChange {
		selection, _, _ := procSendMessageW.Call(tabHandle, tcmGetCurSel, 0, 0)
		if selection == 1 { refreshRecords(); showPage(recordControls) } else { showPage(invoiceControls) }
		return 0, true
	}
	if header.WindowFrom == invoiceItemsList && int32(header.Code) == nmClick {
		handleProductListClick((*nmItemActivate)(lParam))
		clearListViewSelection(invoiceItemsList)
		return 0, true
	}
	if header.WindowFrom == recordsList && int32(header.Code) == nmClick {
		clearListViewSelection(recordsList)
		return 0, true
	}
	if header.WindowFrom == recordsList && int32(header.Code) == nmDblClk {
		activate := (*nmItemActivate)(lParam)
		row := int(activate.Item)
		clearListViewSelection(recordsList)
		if row >= 0 && row < len(visibleRecordRows) {
			showRecordDetail(visibleRecordRows[row])
		}
		return 0, true
	}
	return 0, false
}

func clearListViewSelection(handle uintptr) {
	if handle == 0 { return }
	item := lvItem{Mask: lvifState, State: 0, StateMask: lvisSelected | lvisFocused}
	procSendMessageW.Call(handle, lvmSetItemState, ^uintptr(0), uintptr(unsafe.Pointer(&item)))
}

func clearProductSelection() { clearListViewSelection(invoiceItemsList) }

func handleProductListClick(activate *nmItemActivate) {
	if activate == nil { return }
	row, column := int(activate.Item), int(activate.SubItem)
	if row < 0 || row >= len(manualRows) { return }
	commitProductCellEdit(false)
	activeManualRow = row
	if column == 1 || column == 3 || column == 4 { beginProductCellEdit(row, column); return }
	if column == 6 {
		if rect, ok := productDeleteButtonRect(row); ok && pointInside(rect, activate.Point) { deleteManualItem() }
	}
}

func addListView(parent uintptr, x, y, width, height int, id int, list *[]uintptr) uintptr {
	handle := addControl("SysListView32", "", parent, x, y, width, height, wsChild|wsVisible|wsTabStop|wsBorder|lvsReport|lvsSingleSel|lvsShowSelAlways, id, list)
	procSendMessageW.Call(handle, lvmSetExtendedListStyle, 0, lvsExGridLines|lvsExFullRowSelect|lvsExDoubleBuffer)
	return handle
}

func addInvoiceListView(parent uintptr, x, y, width, height int, id int, list *[]uintptr) uintptr {
	handle := addControl("SysListView32", "", parent, x, y, width, height,
		wsChild|wsVisible|wsTabStop|wsBorder|wsVScroll|lvsReport|lvsSingleSel|lvsShowSelAlways|lvsNoSortHeader, id, list)
	procSendMessageW.Call(handle, lvmSetExtendedListStyle, 0, lvsExGridLines|lvsExFullRowSelect|lvsExDoubleBuffer)
	createInvoiceListScrollPlaceholder(handle)
	return handle
}

func addListViewColumns(handle uintptr, columns []struct{ Title string; Width int; Right bool }) {
	if handle == recordsList { recordColumnWidths = recordColumnWidths[:0] }
	for index, spec := range columns {
		text := mustUTF16Ptr(spec.Title)
		format := int32(lvcfmtLeft)
		if spec.Right { format = lvcfmtRight }
		column := lvColumn{Mask: lvcfFmt|lvcfWidth|lvcfText, Fmt: format, CX: int32(spec.Width), Text: text}
		procSendMessageW.Call(handle, lvmInsertColumnW, uintptr(index), uintptr(unsafe.Pointer(&column)))
		if handle == recordsList { recordColumnWidths = append(recordColumnWidths, spec.Width) }
	}
	if handle == invoiceItemsList || handle == recordsList {
		configureInvoiceListView(handle)
	}
}

func setListViewColumnTitle(handle uintptr, index int, title string) {
	if handle == 0 { return }
	text := mustUTF16Ptr(title)
	column := lvColumn{Mask: lvcfText, Text: text}
	procSendMessageW.Call(handle, lvmSetColumnW, uintptr(index), uintptr(unsafe.Pointer(&column)))
}

func clearListView(handle uintptr) { if handle != 0 { procSendMessageW.Call(handle, lvmDeleteAllItems, 0, 0) } }

func setListViewChecked(handle uintptr, row int, checked bool) {
	if handle == 0 || row < 0 { return }
	state := uint32(0x1000)
	if checked { state = 0x2000 }
	item := lvItem{State: state, StateMask: lvisStateImageMask}
	procSendMessageW.Call(handle, lvmSetItemState, uintptr(row), uintptr(unsafe.Pointer(&item)))
}

func isListViewChecked(handle uintptr, row int) bool {
	if handle == 0 || row < 0 { return false }
	state, _, _ := procSendMessageW.Call(handle, lvmGetItemState, uintptr(row), lvisStateImageMask)
	return uint32(state)&lvisStateImageMask == 0x2000
}

func addListViewRow(handle uintptr, row int, values []string) {
	if handle == 0 || len(values) == 0 { return }
	first := mustUTF16Ptr(values[0])
	item := lvItem{Mask: lvifText, Item: int32(row), Text: first}
	procSendMessageW.Call(handle, lvmInsertItemW, 0, uintptr(unsafe.Pointer(&item)))
	for column := 1; column < len(values); column++ {
		text := mustUTF16Ptr(values[column])
		subItem := lvItem{Item: int32(row), SubItem: int32(column), Text: text}
		procSendMessageW.Call(handle, lvmSetItemTextW, uintptr(row), uintptr(unsafe.Pointer(&subItem)))
	}
}

func setListViewCellText(handle uintptr, row, column int, value string) {
	if handle == 0 || row < 0 || column < 0 { return }
	text := mustUTF16Ptr(value)
	item := lvItem{Item: int32(row), SubItem: int32(column), Text: text}
	procSendMessageW.Call(handle, lvmSetItemTextW, uintptr(row), uintptr(unsafe.Pointer(&item)))
}

func selectedListViewRow(handle uintptr) int {
	result, _, _ := procSendMessageW.Call(handle, lvmGetNextItem, ^uintptr(0), lvniSelected)
	return int(int32(result))
}

func selectListViewRow(handle uintptr, row int) {
	item := lvItem{Mask: lvifState, State: lvisFocused | lvisSelected, StateMask: lvisFocused | lvisSelected}
	procSendMessageW.Call(handle, lvmSetItemState, uintptr(row), uintptr(unsafe.Pointer(&item)))
	procSendMessageW.Call(handle, lvmEnsureVisible, uintptr(row), 0)
}

func addCombo(parent uintptr, x, y, width, height, id int, values []string, list *[]uintptr) uintptr {
	handle := addControl("COMBOBOX", "", parent, x, y, width, height, wsChild|wsVisible|wsTabStop|cbsDropDownList, id, list)
	for _, item := range values { text := mustUTF16Ptr(item); procSendMessageW.Call(handle, cbAddString, 0, uintptr(unsafe.Pointer(text))) }
	procSendMessageW.Call(handle, cbSetCurSel, 0, 0)
	return handle
}

func comboText(handle uintptr) string {
	if handle == 0 { return "" }
	selection, _, _ := procSendMessageW.Call(handle, cbGetCurSel, 0, 0)
	if int32(selection) < 0 { return "" }
	return controlText(handle)
}

func clientSize(window uintptr) (int, int, error) {
	var client winRect
	result, _, err := procGetClientRect.Call(window, uintptr(unsafe.Pointer(&client)))
	if result == 0 { return 0, 0, fmt.Errorf("GetClientRect: %v", err) }
	return int(client.Right-client.Left), int(client.Bottom-client.Top), nil
}
