//go:build windows

package main

import (
	"unsafe"

	"cyinvoice/internal/appdata"
)

var (
	procGetDC              = user32.NewProc("GetDC")
	procReleaseDC          = user32.NewProc("ReleaseDC")
	procGetTextExtentPoint = gdi32.NewProc("GetTextExtentPoint32W")
)

type textExtent struct{ CX, CY int32 }

func measuredColumnWidth(handle, font uintptr, values ...string) int {
	if handle == 0 { return 0 }
	dc, _, _ := procGetDC.Call(handle)
	if dc == 0 { return 0 }
	defer procReleaseDC.Call(handle, dc)
	oldFont, _, _ := procSelectObject.Call(dc, font)
	defer procSelectObject.Call(dc, oldFont)
	maximum := 0
	for _, value := range values {
		text := mustUTF16Ptr(value)
		var extent textExtent
		procGetTextExtentPoint.Call(dc, uintptr(unsafe.Pointer(text)), uintptr(len([]rune(value))), uintptr(unsafe.Pointer(&extent)))
		if int(extent.CX) > maximum { maximum = int(extent.CX) }
	}
	return maximum + 18
}

// layoutRecordColumnsToClient is the only records-list width calculation.
// Fields that must never be ellipsized get measured minimums; every remaining
// pixel belongs to the buyer-name column. The total equals the actual visible
// content width, so 上傳 ends exactly at the reserved scrollbar gutter.
func layoutRecordColumnsToClient() {
	if recordsList == 0 { return }
	available := layoutInvoiceListChrome(recordsList)
	if available <= 0 { return }

	timeWidth := measuredColumnWidth(recordsList, contentFont, "開立時間", "2026/12/31 23:59:59")
	invoiceWidth := measuredColumnWidth(recordsList, contentFont, "發票號碼", "AA12345678")
	sourceWidth := measuredColumnWidth(recordsList, contentFont, "來源", "MO店+", "好賣+", "iOPEN")
	orderWidth := measuredColumnWidth(recordsList, contentFont, "訂單編號", "115102696774269")
	banWidth := measuredColumnWidth(recordsList, contentFont, "統編", "12345678", "00000000", "0000000000")
	if timeWidth < 142 { timeWidth = 142 }
	if invoiceWidth < 96 { invoiceWidth = 96 }
	if sourceWidth < 84 { sourceWidth = 84 }
	if orderWidth < 158 { orderWidth = 158 }
	if banWidth < 112 { banWidth = 112 }

	widths := []int{
		timeWidth, invoiceWidth, sourceWidth, orderWidth, banWidth, 0,
		82, 86, 88, 58,
	}
	minimums := []int{
		timeWidth, invoiceWidth, sourceWidth, orderWidth, banWidth, 0,
		70, 72, 72, 52,
	}
	buyerMinimum := 120

	sumFixed := func() int {
		total := 0
		for index, width := range widths {
			if index != 5 { total += width }
		}
		return total
	}
	buyerWidth := available - sumFixed()
	if buyerWidth < buyerMinimum {
		deficit := buyerMinimum - buyerWidth
		// Shrink only secondary columns. Time, invoice number, source, the
		// 15-digit order ID and BAN keep their fixed full-value widths.
		for _, index := range []int{7, 8, 6, 9} {
			if deficit <= 0 { break }
			room := widths[index] - minimums[index]
			if room <= 0 { continue }
			shrink := room
			if shrink > deficit { shrink = deficit }
			widths[index] -= shrink
			deficit -= shrink
		}
		buyerWidth = available - sumFixed()
	}
	if buyerWidth < 80 { buyerWidth = 80 }
	widths[5] = buyerWidth

	// Correct any rounding/guard difference by assigning it to Buyer.
	total := 0
	for _, width := range widths { total += width }
	widths[5] += available - total
	if widths[5] < 80 { widths[5] = 80 }

	for index, width := range widths {
		procSendMessageW.Call(recordsList, lvmSetColumnWidth, uintptr(index), uintptr(width))
	}
	procShowScrollBar.Call(recordsList, sbHorz, 0)
	layoutInvoiceListChrome(recordsList)
}

func syncRecordPlaceholderRows() {
	if recordsList == 0 { return }
	// Remove any stale horizontal bar before asking how many complete rows fit.
	// This also settles the vertical slot after a clear or resize.
	layoutInvoiceListChrome(recordsList)
	perPage, _, _ := procSendMessageW.Call(recordsList, lvmGetCountPerPage, 0, 0)
	if perPage == 0 { return }
	realCount := len(visibleRecordRows)
	desired := realCount
	if desired < int(perPage) { desired = int(perPage) }
	current, _, _ := procSendMessageW.Call(recordsList, lvmGetItemCount, 0, 0)
	for int(current) < desired {
		addListViewRow(recordsList, int(current), []string{"", "", "", "", "", "", "", "", "", ""})
		current++
	}
	for int(current) > desired {
		current--
		procSendMessageW.Call(recordsList, lvmDeleteItem, current, 0)
	}
}

func layoutSettingsButtonToTab(sx, sy float64) {
	button := handles[idSettings]
	base, ok := baseRects[tabHandle]
	if button == 0 || !ok { return }
	// The native Tab header keeps its system metric height when the main window
	// is maximized. Keep the button at its final control size and center it in
	// the real first-item rectangle instead of stretching it with sx/sy.
	buttonWidth := settingsButtonWidth
	buttonHeight := settingsButtonHeight
	tabX := int(float64(base.X) * sx)
	tabY := int(float64(base.Y) * sy)
	tabWidth := int(float64(base.W) * sx)
	headerTop := 2
	headerHeight := buttonHeight
	var itemRect winRect
	selected, _, _ := procSendMessageW.Call(tabHandle, tcmGetCurSel, 0, 0)
	if ok, _, _ := procSendMessageW.Call(tabHandle, tcmGetItemRect, selected, uintptr(unsafe.Pointer(&itemRect))); ok != 0 {
		headerTop = int(itemRect.Top)
		headerHeight = int(itemRect.Bottom - itemRect.Top)
	}
	x := tabX + tabWidth - buttonWidth - 8
	y := tabY + headerTop
	if headerHeight > buttonHeight { y += (headerHeight - buttonHeight) / 2 }
	procMoveWindow.Call(button, uintptr(x), uintptr(y), uintptr(buttonWidth), uintptr(buttonHeight), 1)
}

func recordHasUploadIndicator(style recordRowStyle) bool {
	if style.Upload != 0 { return true }
	switch style.State {
	case appdata.InvoiceStateOpened, appdata.InvoiceStateVoided, appdata.InvoiceStateUnknown, appdata.InvoiceStateChanging:
		return true
	default:
		return false
	}
}

func drawRecordUploadIndicator(draw *nmListViewCustomDraw, style recordRowStyle) {
	background := zebraColor(int(draw.Draw.ItemSpec))
	if draw.Draw.ItemState&cdisSelected != 0 { background = rgb(224, 232, 240) }
	brush, _, _ := procCreateSolidBrush.Call(uintptr(background))
	procFillRect.Call(draw.Draw.DC, uintptr(unsafe.Pointer(&draw.Draw.Rect)), brush)
	procDeleteObject.Call(brush)

	color := rgb(220, 160, 0)
	switch style.Upload {
	case 99:
		color = rgb(0, 170, 65)
	case 91:
		color = rgb(210, 0, 0)
	}
	procSetTextColor.Call(draw.Draw.DC, uintptr(color))
	procSetBkMode.Call(draw.Draw.DC, 1)
	oldFont, _, _ := procSelectObject.Call(draw.Draw.DC, contentFont)
	text := mustUTF16Ptr("●")
	rect := draw.Draw.Rect
	procDrawTextW.Call(draw.Draw.DC, uintptr(unsafe.Pointer(text)), ^uintptr(0), uintptr(unsafe.Pointer(&rect)), dtCenter|dtVCenter|dtSingleLine)
	procSelectObject.Call(draw.Draw.DC, oldFont)
}
