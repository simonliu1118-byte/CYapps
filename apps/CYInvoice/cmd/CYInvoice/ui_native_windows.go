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
	baseClientWidth  = 1164
	baseClientHeight = 811

	wmSize           = 0x0005
	wmGetMinMaxInfo  = 0x0024
	wmNotify         = 0x004E
	wmDrawItem       = 0x002B
	wmCtlColorBtn    = 0x0135
	wmCtlColorStatic = 0x0138
	wmSetFocus       = 0x0007
	wmKillFocus      = 0x0008
	wmKeyDown        = 0x0100
	wmLButtonDown    = 0x0201
	emSetSel         = 0x00B1
	vkReturn         = 0x000D
	vkEscape         = 0x001B

	ssCenter      = 0x0001
	ssRight       = 0x0002
	ssEtchedHorizontal = 0x0010
	ssCenterImage = 0x0200

	odtButton    = 4
	odtTab       = 101
	odtListView  = 102
	odsSelected  = 0x0001
	odsDisabled  = 0x0004
	dtCenter     = 0x0001
	dtRight      = 0x0002
	dtVCenter    = 0x0004
	dtSingleLine = 0x0020
	dtNoPrefix   = 0x0800
	dtEndEllipsis = 0x8000

	tcmFirst       = 0x1300
	tcmGetCurSel   = tcmFirst + 11
	tcmSetCurSel   = tcmFirst + 12
	tcmSetItemSize = tcmFirst + 41
	tcmInsertItemW = tcmFirst + 62
	tcifText       = 0x0001
	tcnSelChange   = -551
	tcsFixedWidth  = 0x0400
	tcsOwnerDrawFixed = 0x2000

	lvsReport                = 0x0001
	lvsOwnerDrawFixed        = 0x0400
	lvsSingleSel             = 0x0004
	lvsShowSelAlways         = 0x0008
	lvsNoColumnHeader        = 0x4000
	lvsExGridLines           = 0x00000001
	lvsExCheckboxes          = 0x00000004
	lvsExFullRowSelect       = 0x00000020
	lvsExDoubleBuffer        = 0x00010000
	lvmFirst                 = 0x1000
	lvmDeleteAllItems        = lvmFirst + 9
	lvmSetExtendedListStyle  = lvmFirst + 54
	lvmInsertColumnW         = lvmFirst + 97
	lvmSetColumnWidth        = lvmFirst + 30
	lvmGetColumnWidth        = lvmFirst + 29
	lvmSetColumnW            = lvmFirst + 96
	lvmInsertItemW           = lvmFirst + 77
	lvmSetItemTextW          = lvmFirst + 116
	lvmGetItemTextW          = lvmFirst + 115
	lvmGetNextItem           = lvmFirst + 12
	lvmSetItemState          = lvmFirst + 43
	lvmGetItemState          = lvmFirst + 44
	lvmEnsureVisible         = lvmFirst + 19
	lvmGetSubItemRect        = lvmFirst + 56
	lvifText                 = 0x0001
	lvifState                = 0x0008
	lvisFocused              = 0x0001
	lvisSelected             = 0x0002
	lvisStateImageMask       = 0xF000
	lvniSelected             = 0x0002
	nmClick                  = -2
	nmCustomDraw             = -12
	lvnItemChanged           = -101
	cddsPrePaint             = 0x00000001
	cddsItemPrePaint         = 0x00010001
	cddsSubItem              = 0x00020000
	cdrfNewFont              = 0x00000002
	cdrfSkipDefault          = 0x00000004
	cdrfNotifyItemDraw       = 0x00000020
	cdrfNotifySubItemDraw    = 0x00000020
	cdisSelected             = 0x0001
	lvcfFmt                  = 0x0001
	lvcfWidth                = 0x0002
	lvcfText                 = 0x0004
	lvcfmtLeft               = 0x0000
	lvcfmtRight              = 0x0001
	lvirBounds               = 0

	cbsDropDownList = 0x0003
	cbAddString     = 0x0143
	cbGetCurSel     = 0x0147
	cbSetCurSel     = 0x014E
)

var (
	comctl32 = syscall.NewLazyDLL("comctl32.dll")
	procInitCommonControlsEx = comctl32.NewProc("InitCommonControlsEx")
	procMoveWindow = user32.NewProc("MoveWindow")
	procGetClientRect = user32.NewProc("GetClientRect")
	procGetWindowRect = user32.NewProc("GetWindowRect")
	procInvalidateRect = user32.NewProc("InvalidateRect")
	procFillRect = user32.NewProc("FillRect")
	procFrameRect = user32.NewProc("FrameRect")
	procDrawTextW = user32.NewProc("DrawTextW")
	procSetTextColor = gdi32.NewProc("SetTextColor")
	procSetBkColor = gdi32.NewProc("SetBkColor")
	procSetBkMode = gdi32.NewProc("SetBkMode")
	procCreateSolidBrush = gdi32.NewProc("CreateSolidBrush")
	procCreatePen = gdi32.NewProc("CreatePen")
	procRoundRect = gdi32.NewProc("RoundRect")
	procCreateFontW = gdi32.NewProc("CreateFontW")
	procSelectObject = gdi32.NewProc("SelectObject")
	procSetWindowLongPtrW = user32.NewProc("SetWindowLongPtrW")
	procCallWindowProcW = user32.NewProc("CallWindowProcW")
	procGetFocus = user32.NewProc("GetFocus")

	baseRects = map[uintptr]uiRect{}
	colorStyles = map[uintptr]staticColor{}
	bannerBrush uintptr
	whiteBrush uintptr
	hollowBrush uintptr
	alternateRowBrush uintptr
	disabledEditBrush uintptr
	panelTitleBrush uintptr
	readonlyCellBrush uintptr
	readonlyAlternateCellBrush uintptr
	selectedRowBrush uintptr
	gridLineBrush uintptr
	redBorderBrush uintptr
	redPressedBrush uintptr
	taxSelectedBrush uintptr
	importButtonBrush uintptr
	importButtonPressedBrush uintptr
	importButtonPen uintptr
	blueBorderBrush uintptr
	uiFont uintptr
	contentFont uintptr
	smallFont uintptr
	apiReasonFont uintptr
	strikeFont uintptr
	contentStrikeFont uintptr
	boldFont uintptr
	bannerFont uintptr
	tabHandle uintptr
	recordsList uintptr
	invoiceItemsList uintptr
	recordColumnWidths []int
	productColumnWidths []int
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
type drawItem struct {
	ControlType uint32
	ControlID uint32
	ItemID uint32
	ItemAction uint32
	ItemState uint32
	ItemWindow uintptr
	DC uintptr
	Rect winRect
	ItemData uintptr
}
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
	hollowBrush, _, _ = procGetStockObject.Call(5)
	alternateRowBrush, _, _ = procCreateSolidBrush.Call(uintptr(rgb(224, 238, 250)))
	disabledEditBrush, _, _ = procCreateSolidBrush.Call(uintptr(rgb(238, 238, 238)))
	panelTitleBrush, _, _ = procCreateSolidBrush.Call(uintptr(rgb(240, 240, 240)))
	readonlyCellBrush, _, _ = procCreateSolidBrush.Call(uintptr(rgb(236, 236, 236)))
	readonlyAlternateCellBrush, _, _ = procCreateSolidBrush.Call(uintptr(rgb(211, 223, 233)))
	selectedRowBrush, _, _ = procCreateSolidBrush.Call(uintptr(rgb(204, 228, 247)))
	gridLineBrush, _, _ = procCreateSolidBrush.Call(uintptr(rgb(224, 224, 224)))
	redBorderBrush, _, _ = procCreateSolidBrush.Call(uintptr(rgb(255, 72, 72)))
	redPressedBrush, _, _ = procCreateSolidBrush.Call(uintptr(rgb(255, 238, 238)))
	taxSelectedBrush, _, _ = procCreateSolidBrush.Call(uintptr(rgb(226, 241, 255)))
	importButtonBrush, _, _ = procCreateSolidBrush.Call(uintptr(rgb(32, 126, 205)))
	importButtonPressedBrush, _, _ = procCreateSolidBrush.Call(uintptr(rgb(20, 95, 162)))
	importButtonPen, _, _ = procCreatePen.Call(0, 1, uintptr(rgb(0, 82, 155)))
	blueBorderBrush, _, _ = procCreateSolidBrush.Call(uintptr(rgb(0, 120, 215)))
	uiFont = createUIFont(-17, 400)
	contentFont = createUIFont(-16, 400)
	smallFont = createUIFont(-14, 400)
	apiReasonFont = createUIFont(-11, 400)
	strikeFont = createUIFontWithStrike(-17, 400, true)
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
	if message == wmKeyDown && wParam == vkReturn {
		row, column := productCellEditorRow, productCellEditorColumn
		commitProductCellEdit(false)
		refreshManualRows()
		refreshTotals()
		if column == 1 { beginProductCellEdit(row, 3) }
		if column == 3 { beginProductCellEdit(row, 4) }
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
		wsChild|wsVisible|wsTabStop|wsBorder|esAutoHScroll,
		uintptr(rect.Left+2), uintptr(rect.Top+2), uintptr(rect.Right-rect.Left-4), uintptr(rect.Bottom-rect.Top-4),
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
	procMoveWindow.Call(productCellEditor, uintptr(rect.Left+2), uintptr(rect.Top+2), uintptr(rect.Right-rect.Left-4), uintptr(rect.Bottom-rect.Top-4), 1)
}

func productDeleteButtonRect(row int) (winRect, bool) {
	rect, ok := productSubItemRect(row, 6)
	if !ok { return rect, false }
	rect.Left += 9
	rect.Right -= 9
	rect.Top += 3
	rect.Bottom -= 3
	return rect, true
}

func pointInside(rect winRect, point point) bool {
	return point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom
}

func handleOwnerDraw(lParam unsafe.Pointer) bool {
	if lParam == nil {
		return false
	}
	item := (*drawItem)(lParam)
	if item.ControlType == odtListView {
		return handleListViewOwnerDraw(item)
	}
	if item.ControlType == odtTab && item.ItemWindow == tabHandle {
		selected, _, _ := procSendMessageW.Call(tabHandle, tcmGetCurSel, 0, 0)
		isSelected := uintptr(item.ItemID) == selected
		background := disabledEditBrush
		textColor := rgb(72, 72, 72)
		font := contentFont
		if isSelected {
			background = taxSelectedBrush
			textColor = rgb(0, 70, 140)
			font = boldFont
		}
		procFillRect.Call(item.DC, uintptr(unsafe.Pointer(&item.Rect)), background)
		if isSelected { procFrameRect.Call(item.DC, uintptr(unsafe.Pointer(&item.Rect)), blueBorderBrush) }
		procSetTextColor.Call(item.DC, uintptr(textColor))
		procSetBkMode.Call(item.DC, 1)
		oldFont, _, _ := procSelectObject.Call(item.DC, font)
		label := "開立發票"
		if item.ItemID == 1 { label = "已開立發票清單" }
		text := mustUTF16Ptr(label)
		rect := item.Rect
		procDrawTextW.Call(item.DC, uintptr(unsafe.Pointer(text)), ^uintptr(0), uintptr(unsafe.Pointer(&rect)), dtCenter|dtVCenter|dtSingleLine|dtNoPrefix)
		procSelectObject.Call(item.DC, oldFont)
		return true
	}
	if item.ControlType != odtButton {
		return false
	}
	if item.ControlID == idTaxInclusive || item.ControlID == idTaxExclusive {
		selected := item.ControlID == idTaxInclusive && pricesAreInclusive ||
			item.ControlID == idTaxExclusive && !pricesAreInclusive
		background := whiteBrush
		if selected { background = taxSelectedBrush }
		procFillRect.Call(item.DC, uintptr(unsafe.Pointer(&item.Rect)), background)
		if selected { procFrameRect.Call(item.DC, uintptr(unsafe.Pointer(&item.Rect)), blueBorderBrush) }
		procSetTextColor.Call(item.DC, uintptr(rgb(0, 51, 102)))
		procSetBkMode.Call(item.DC, 1)
		label := "○ 以未稅輸入"
		if item.ControlID == idTaxInclusive { label = "○ 以含稅輸入" }
		if selected { label = strings.Replace(label, "○", "●", 1) }
		text := mustUTF16Ptr(label)
		rect := item.Rect
		procDrawTextW.Call(item.DC, uintptr(unsafe.Pointer(text)), ^uintptr(0), uintptr(unsafe.Pointer(&rect)), dtCenter|dtVCenter|dtSingleLine)
		return true
	}
	if item.ControlID == idImportERP || item.ControlID == idImportMO || item.ControlID == idImportCoupang {
		background := importButtonBrush
		if item.ItemState&odsSelected != 0 {
			background = importButtonPressedBrush
		}
		oldBrush, _, _ := procSelectObject.Call(item.DC, background)
		oldPen, _, _ := procSelectObject.Call(item.DC, importButtonPen)
		procRoundRect.Call(item.DC, uintptr(item.Rect.Left), uintptr(item.Rect.Top), uintptr(item.Rect.Right), uintptr(item.Rect.Bottom), 12, 12)
		procSelectObject.Call(item.DC, oldPen)
		procSelectObject.Call(item.DC, oldBrush)
		color := rgb(255, 255, 255)
		if item.ItemState&odsDisabled != 0 {
			color = rgb(128, 128, 128)
		}
		procSetTextColor.Call(item.DC, uintptr(color))
		procSetBkMode.Call(item.DC, 1)
		label := "匯入鼎新 ERP 銷貨單"
		if item.ControlID == idImportMO { label = "匯入 MO店+" }
		if item.ControlID == idImportCoupang { label = "匯入酷澎" }
		text := mustUTF16Ptr(label)
		rect := item.Rect
		procDrawTextW.Call(item.DC, uintptr(unsafe.Pointer(text)), ^uintptr(0), uintptr(unsafe.Pointer(&rect)), dtCenter|dtVCenter|dtSingleLine)
		return true
	}
	if item.ControlID != idItemDelete { return false }
	background := whiteBrush
	if item.ItemState&odsSelected != 0 {
		background = redPressedBrush
	}
	procFillRect.Call(item.DC, uintptr(unsafe.Pointer(&item.Rect)), background)
	procFrameRect.Call(item.DC, uintptr(unsafe.Pointer(&item.Rect)), redBorderBrush)
	procSetTextColor.Call(item.DC, uintptr(rgb(255, 45, 45)))
	procSetBkMode.Call(item.DC, 1)
	text := mustUTF16Ptr("刪除")
	rect := item.Rect
	procDrawTextW.Call(item.DC, uintptr(unsafe.Pointer(text)), ^uintptr(0), uintptr(unsafe.Pointer(&rect)), dtCenter|dtVCenter|dtSingleLine)
	return true
}

func handleListViewOwnerDraw(item *drawItem) bool {
	if item == nil || item.ItemID == ^uint32(0) { return false }
	handle := item.ItemWindow
	if handle != invoiceItemsList && handle != recordsList { return false }
	row := int(item.ItemID)
	columns := len(productColumnWidths)
	if handle == recordsList { columns = len(recordColumnWidths) }
	if columns == 0 { return true }

	x := item.Rect.Left
	selected := item.ItemState&odsSelected != 0
	for column := 0; column < columns; column++ {
		widthValue, _, _ := procSendMessageW.Call(handle, lvmGetColumnWidth, uintptr(column), 0)
		width := int32(widthValue)
		if width <= 0 { continue }
		cell := winRect{Left: x, Top: item.Rect.Top, Right: x + width, Bottom: item.Rect.Bottom}
		if cell.Right > item.Rect.Right { cell.Right = item.Rect.Right }
		x += width

		background := zebraBrush(row)
		if selected { background = selectedRowBrush }
		if handle == invoiceItemsList && column == 5 {
			background = readonlyCellBrush
			if row%2 == 0 { background = readonlyAlternateCellBrush }
		}
		procFillRect.Call(item.DC, uintptr(unsafe.Pointer(&cell)), background)
		procFrameRect.Call(item.DC, uintptr(unsafe.Pointer(&cell)), gridLineBrush)

		if handle == invoiceItemsList && column == 6 && row >= 0 && row < len(manualRows) {
			button := cell
			button.Left += 9; button.Right -= 9; button.Top += 3; button.Bottom -= 3
			procFrameRect.Call(item.DC, uintptr(unsafe.Pointer(&button)), redBorderBrush)
			procSetTextColor.Call(item.DC, uintptr(rgb(255, 45, 45)))
			procSetBkMode.Call(item.DC, 1)
			text := mustUTF16Ptr("刪除")
			procDrawTextW.Call(item.DC, uintptr(unsafe.Pointer(text)), ^uintptr(0), uintptr(unsafe.Pointer(&button)), dtCenter|dtVCenter|dtSingleLine|dtNoPrefix)
			continue
		}

		textColor, font := listViewCellAppearance(handle, row, column)
		procSetTextColor.Call(item.DC, uintptr(textColor))
		procSetBkMode.Call(item.DC, 1)
		oldFont, _, _ := procSelectObject.Call(item.DC, font)
		textRect := cell
		textRect.Left += 6; textRect.Right -= 6
		format := uintptr(dtVCenter | dtSingleLine | dtNoPrefix | dtEndEllipsis)
		if listViewColumnRightAligned(handle, column) { format |= dtRight }
		text := mustUTF16Ptr(listViewCellText(handle, row, column))
		procDrawTextW.Call(item.DC, uintptr(unsafe.Pointer(text)), ^uintptr(0), uintptr(unsafe.Pointer(&textRect)), format)
		procSelectObject.Call(item.DC, oldFont)
	}
	return true
}

func listViewColumnRightAligned(handle uintptr, column int) bool {
	if handle == invoiceItemsList { return column == 3 || column == 4 || column == 5 }
	return handle == recordsList && column == 6
}

func listViewCellAppearance(handle uintptr, row, column int) (uint32, uintptr) {
	if handle == invoiceItemsList {
		if column == 5 { return rgb(96, 96, 96), contentFont }
		return rgb(0, 0, 0), contentFont
	}
	color, font := rgb(0, 0, 0), contentFont
	if row < 0 || row >= len(recordRowStyles) { return color, font }
	style := recordRowStyles[row]
	switch style.State {
	case appdata.InvoiceStateFailed:
		color = rgb(210, 0, 0)
	case appdata.InvoiceStateUnknown, appdata.InvoiceStateChanging:
		color = rgb(215, 116, 0)
	case appdata.InvoiceStateVoided:
		color, font = rgb(128, 128, 128), contentStrikeFont
	}
	if column == 9 && style.Upload != 0 {
		switch style.Upload {
		case 99:
			color = rgb(0, 170, 65)
		case 91:
			color = rgb(210, 0, 0)
		default:
			color = rgb(220, 160, 0)
		}
	}
	return color, font
}

func createUIFont(height, weight int32) uintptr {
	return createUIFontWithStrike(height, weight, false)
}

func createUIFontWithStrike(height, weight int32, strikeout bool) uintptr {
	face := mustUTF16Ptr("Microsoft JhengHei UI")
	strike := uintptr(0)
	if strikeout {
		strike = 1
	}
	font, _, _ := procCreateFontW.Call(
		uintptr(height), 0, 0, 0, uintptr(weight),
		0, 0, strike, 1, 0, 0, 5, 0,
		uintptr(unsafe.Pointer(face)),
	)
	return font
}

func handleRecordsCustomDraw(lParam unsafe.Pointer) (uintptr, bool) {
	if lParam == nil || recordsList == 0 {
		return 0, false
	}
	draw := (*nmListViewCustomDraw)(lParam)
	if draw.Draw.Header.WindowFrom != recordsList || int32(draw.Draw.Header.Code) != nmCustomDraw {
		return 0, false
	}
	switch draw.Draw.DrawStage {
	case cddsPrePaint:
		return cdrfNotifyItemDraw, true
	case cddsItemPrePaint:
		return cdrfNotifySubItemDraw, true
	case cddsItemPrePaint | cddsSubItem:
		applyRecordRowStyle(draw)
		if draw.Draw.ItemState&cdisSelected != 0 { return cdrfNewFont, true }
		drawListViewCell(recordsList, draw, draw.SubItem == 6)
		return cdrfSkipDefault, true
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
		if draw.Draw.ItemState&cdisSelected != 0 { return cdrfNewFont, true }
		background := zebraBrush(row)
		if draw.SubItem == 5 {
			background = readonlyCellBrush
			if row%2 == 0 { background = readonlyAlternateCellBrush }
			procSetTextColor.Call(draw.Draw.DC, uintptr(rgb(112, 112, 112)))
		} else {
			procSetTextColor.Call(draw.Draw.DC, uintptr(rgb(0, 0, 0)))
		}
		procFillRect.Call(draw.Draw.DC, uintptr(unsafe.Pointer(&draw.Draw.Rect)), background)
		if draw.SubItem != 6 || row < 0 || row >= len(manualRows) {
			drawListViewCell(invoiceItemsList, draw, draw.SubItem == 3 || draw.SubItem == 4 || draw.SubItem == 5)
			return cdrfSkipDefault, true
		}
		button := draw.Draw.Rect
		button.Left += 9; button.Right -= 9; button.Top += 3; button.Bottom -= 3
		procFrameRect.Call(draw.Draw.DC, uintptr(unsafe.Pointer(&button)), redBorderBrush)
		procSetTextColor.Call(draw.Draw.DC, uintptr(rgb(255, 45, 45)))
		procSetBkMode.Call(draw.Draw.DC, 1)
		text := mustUTF16Ptr("刪除")
		procDrawTextW.Call(draw.Draw.DC, uintptr(unsafe.Pointer(text)), ^uintptr(0), uintptr(unsafe.Pointer(&button)), dtCenter|dtVCenter|dtSingleLine)
		return cdrfSkipDefault, true
	default:
		return 0, true
	}
}

func applyRecordRowStyle(draw *nmListViewCustomDraw) {
	row := int(draw.Draw.ItemSpec)
	if row < 0 || row >= len(recordRowStyles) {
		return
	}
	style := recordRowStyles[row]
	applyZebraBackground(draw)
	draw.TextColor = rgb(0, 0, 0)
	font := contentFont
	switch style.State {
	case appdata.InvoiceStateFailed:
		draw.TextColor = rgb(210, 0, 0)
	case appdata.InvoiceStateUnknown, appdata.InvoiceStateChanging:
		draw.TextColor = rgb(215, 116, 0)
	case appdata.InvoiceStateVoided:
		draw.TextColor = rgb(128, 128, 128)
		font = contentStrikeFont
	}
	if draw.SubItem == 9 && style.Upload != 0 {
		switch style.Upload {
		case 99:
			draw.TextColor = rgb(0, 170, 65)
		case 91:
			draw.TextColor = rgb(210, 0, 0)
		default:
			draw.TextColor = rgb(220, 160, 0)
		}
	}
	procSetTextColor.Call(draw.Draw.DC, uintptr(draw.TextColor))
	procSelectObject.Call(draw.Draw.DC, font)
}

func applyZebraBackground(draw *nmListViewCustomDraw) {
	if draw == nil || draw.Draw.ItemState&cdisSelected != 0 { return }
	row := int(draw.Draw.ItemSpec)
	background := zebraBrush(row)
	draw.TextBackground = rgb(255, 255, 255)
	if row%2 == 0 { draw.TextBackground = rgb(224, 238, 250) }
	procFillRect.Call(draw.Draw.DC, uintptr(unsafe.Pointer(&draw.Draw.Rect)), background)
}

func zebraBrush(row int) uintptr {
	if row%2 == 0 { return alternateRowBrush }
	return whiteBrush
}

func listViewCellText(handle uintptr, row, column int) string {
	buffer := make([]uint16, 1024)
	item := lvItem{Item: int32(row), SubItem: int32(column), Text: &buffer[0], TextMax: int32(len(buffer))}
	procSendMessageW.Call(handle, lvmGetItemTextW, uintptr(row), uintptr(unsafe.Pointer(&item)))
	return syscall.UTF16ToString(buffer)
}

func drawListViewCell(handle uintptr, draw *nmListViewCustomDraw, right bool) {
	if handle == 0 || draw == nil { return }
	row, column := int(draw.Draw.ItemSpec), int(draw.SubItem)
	rect := draw.Draw.Rect
	rect.Left += 6
	rect.Right -= 6
	format := uintptr(dtVCenter | dtSingleLine | dtNoPrefix | dtEndEllipsis)
	if right { format |= dtRight }
	procSetBkMode.Call(draw.Draw.DC, 1)
	text := mustUTF16Ptr(listViewCellText(handle, row, column))
	procDrawTextW.Call(draw.Draw.DC, uintptr(unsafe.Pointer(text)), ^uintptr(0), uintptr(unsafe.Pointer(&rect)), format)
}

func rgb(red, green, blue byte) uint32 {
	return uint32(red) | uint32(green)<<8 | uint32(blue)<<16
}

func rememberRect(handle uintptr, x, y, width, height int) {
	if handle != 0 {
		baseRects[handle] = uiRect{X: x, Y: y, W: width, H: height}
	}
}

func relayoutGUI(window uintptr) {
	if window == 0 || len(baseRects) == 0 {
		return
	}
	var client winRect
	procGetClientRect.Call(window, uintptr(unsafe.Pointer(&client)))
	width := int(client.Right - client.Left)
	height := int(client.Bottom - client.Top)
	if width <= 0 || height <= 0 {
		return
	}
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
	if recordsList != 0 {
		for index, width := range recordColumnWidths {
			procSendMessageW.Call(recordsList, lvmSetColumnWidth, uintptr(index), uintptr(int(float64(width)*sx)))
		}
	}
	if invoiceItemsList != 0 {
		for index, width := range productColumnWidths {
			procSendMessageW.Call(invoiceItemsList, lvmSetColumnWidth, uintptr(index), uintptr(int(float64(width)*sx)))
		}
		positionProductCellEditor()
	}
	procInvalidateRect.Call(window, 0, 1)
}

func centerWindowOnParent(window, parent uintptr, width, height int) {
	if window == 0 {
		return
	}
	var bounds winRect
	if parent != 0 {
		if result, _, _ := procGetWindowRect.Call(parent, uintptr(unsafe.Pointer(&bounds))); result == 0 {
			parent = 0
		}
	}
	if parent == 0 {
		bounds = winRect{Left: 0, Top: 0, Right: int32(baseClientWidth), Bottom: int32(baseClientHeight)}
	}
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
	if !ok {
		return handlePlainControlColor(dc)
	}
	procSetTextColor.Call(dc, uintptr(style.Text))
	if style.Transparent {
		procSetBkMode.Call(dc, 1)
	} else {
		procSetBkMode.Call(dc, 2)
		procSetBkColor.Call(dc, uintptr(style.Background))
	}
	return style.Brush
}

func handlePlainControlColor(dc uintptr) uintptr {
	procSetTextColor.Call(dc, uintptr(rgb(0, 0, 0)))
	procSetBkMode.Call(dc, 2)
	procSetBkColor.Call(dc, uintptr(rgb(240, 240, 240)))
	return panelTitleBrush
}

func addTab(parent uintptr, x, y, width, height int) uintptr {
	handle := addControl("SysTabControl32", "", parent, x, y, width, height, wsChild|wsVisible|wsTabStop|tcsFixedWidth|tcsOwnerDrawFixed, 0, nil)
	for index, title := range []string{"開立發票", "已開立發票清單"} {
		value := mustUTF16Ptr(title)
		item := tcItem{Mask: tcifText, Text: value}
		procSendMessageW.Call(handle, tcmInsertItemW, uintptr(index), uintptr(unsafe.Pointer(&item)))
	}
	itemSize := uintptr(uint32(190) | uint32(36)<<16)
	procSendMessageW.Call(handle, tcmSetItemSize, 0, itemSize)
	return handle
}

func handleNotifyMessage(lParam unsafe.Pointer) bool {
	if lParam == nil { return false }
	header := (*nmHdr)(lParam)
	if header.WindowFrom == tabHandle && int32(header.Code) == tcnSelChange {
		selection, _, _ := procSendMessageW.Call(tabHandle, tcmGetCurSel, 0, 0)
		if selection == 1 {
			refreshRecords()
			showPage(recordControls)
		} else {
			showPage(invoiceControls)
		}
		return true
	}
	if header.WindowFrom == invoiceItemsList && int32(header.Code) == nmClick {
		handleProductListClick((*nmItemActivate)(lParam))
		return true
	}
	return false
}

func handleProductListClick(activate *nmItemActivate) {
	if activate == nil { return }
	row, column := int(activate.Item), int(activate.SubItem)
	if row < 0 || row >= len(manualRows) { return }
	commitProductCellEdit(false)
	activeManualRow = row
	selectListViewRow(invoiceItemsList, row)
	if column == 1 || column == 3 || column == 4 {
		beginProductCellEdit(row, column)
		return
	}
	if column == 6 {
		if rect, ok := productDeleteButtonRect(row); ok && pointInside(rect, activate.Point) {
			deleteManualItem()
		}
	}
}

func addListView(parent uintptr, x, y, width, height int, id int, list *[]uintptr) uintptr {
	handle := addControl("SysListView32", "", parent, x, y, width, height,
		wsChild|wsVisible|wsTabStop|wsBorder|lvsReport|lvsOwnerDrawFixed|lvsSingleSel|lvsShowSelAlways, id, list)
	procSendMessageW.Call(handle, lvmSetExtendedListStyle, 0, lvsExGridLines|lvsExFullRowSelect|lvsExDoubleBuffer)
	return handle
}

func addListViewColumns(handle uintptr, columns []struct{ Title string; Width int; Right bool }) {
	if handle == recordsList {
		recordColumnWidths = recordColumnWidths[:0]
	}
	if handle == invoiceItemsList { productColumnWidths = productColumnWidths[:0] }
	for index, spec := range columns {
		text := mustUTF16Ptr(spec.Title)
		format := int32(lvcfmtLeft)
		if spec.Right { format = lvcfmtRight }
		column := lvColumn{Mask: lvcfFmt|lvcfWidth|lvcfText, Fmt: format, CX: int32(spec.Width), Text: text}
		procSendMessageW.Call(handle, lvmInsertColumnW, uintptr(index), uintptr(unsafe.Pointer(&column)))
		if handle == recordsList { recordColumnWidths = append(recordColumnWidths, spec.Width) }
		if handle == invoiceItemsList { productColumnWidths = append(productColumnWidths, spec.Width) }
	}
}

func setListViewColumnTitle(handle uintptr, index int, title string) {
	if handle == 0 { return }
	text := mustUTF16Ptr(title)
	column := lvColumn{Mask: lvcfText, Text: text}
	procSendMessageW.Call(handle, lvmSetColumnW, uintptr(index), uintptr(unsafe.Pointer(&column)))
}

func clearListView(handle uintptr) {
	if handle != 0 { procSendMessageW.Call(handle, lvmDeleteAllItems, 0, 0) }
}

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
	for _, item := range values {
		text := mustUTF16Ptr(item)
		procSendMessageW.Call(handle, cbAddString, 0, uintptr(unsafe.Pointer(text)))
	}
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

func containsFold(value, query string) bool {
	return strings.Contains(strings.ToLower(value), strings.ToLower(strings.TrimSpace(query)))
}
