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
	uiModeStandardV12 = "standard"
	uiModeAdvancedV12 = "advanced"

	swHideV12 = 0
	wmPaintV12 = 0x000F
	wmKeyUpV12 = 0x0101
	wmCloseV12 = 0x0010
	wmSetIconV12 = 0x0080
	iconSmallV12 = 0
	iconBigV12 = 1
	vkSpaceV12 = 0x20
	bsAutoRadioButtonV12 = 0x00000009
	wsGroupV12 = 0x00020000
	bsOwnerDrawV12 = 0x0000000B
	transparentV12 = 1
	dtCenterV12 = 0x00000001
	dtVCenterV12 = 0x00000004
	dtSingleLineV12 = 0x00000020
	dtNoPrefixV12 = 0x00000800
	psSolidV12 = 0
	gwlpWndProcV12 = ^uintptr(3) // -4
)

var (
	pDestroyWindowV12 = user32.NewProc("DestroyWindow")
	pSetWindowLongPtrWV12 = user32.NewProc("SetWindowLongPtrW")
	pCallWindowProcWV12 = user32.NewProc("CallWindowProcW")
	pBeginPaintV12 = user32.NewProc("BeginPaint")
	pEndPaintV12 = user32.NewProc("EndPaint")
	pFillRectV12 = user32.NewProc("FillRect")
	pDrawTextWV12 = user32.NewProc("DrawTextW")
	pSetTextColorV12 = gdi32.NewProc("SetTextColor")
	pSetBkModeV12 = gdi32.NewProc("SetBkMode")
	pCreateSolidBrushV12 = gdi32.NewProc("CreateSolidBrush")
	pCreatePenV12 = gdi32.NewProc("CreatePen")
	pSelectObjectV12 = gdi32.NewProc("SelectObject")
	pDeleteObjectV12 = gdi32.NewProc("DeleteObject")
	pRoundRectV12 = gdi32.NewProc("RoundRect")
	pCreateFontWV12 = gdi32.NewProc("CreateFontW")
	pCreateBitmapV12 = gdi32.NewProc("CreateBitmap")
	pCreateIconIndirectV12 = user32.NewProc("CreateIconIndirect")
	pInvalidateRectV12 = user32.NewProc("InvalidateRect")
)

type fieldWidgetV12 struct {
	apply uintptr
	label uintptr
	value uintptr
	group string
}

type groupWidgetV12 struct {
	hwnd uintptr
	x int32
	y int32
	w int32
}

type detailWidgetV12 struct {
	hwnd uintptr
	x int32
	y int32
	w int32
	h int32
}

type buttonActionV12 struct {
	oldProc uintptr
	action func()
	style string
}

type paintStructV12 struct {
	Hdc uintptr
	FErase int32
	RcPaint RECT
	FRestore int32
	FIncUpdate int32
	RgbReserved [32]byte
}

type iconInfoV12 struct {
	FIcon int32
	XHotspot uint32
	YHotspot uint32
	HbmMask uintptr
	HbmColor uintptr
}

var (
	fieldWidgetsV12 = map[string]fieldWidgetV12{}
	groupWidgetsV12 = map[string]groupWidgetV12{}
	detailWidgetsV12 []detailWidgetV12
	detailBoxV12 uintptr
	detailBaseYV12 int32 = 610
	footerV12 uintptr
	modeStandardButtonV12 uintptr
	modeAdvancedButtonV12 uintptr
	currentUIModeV12 = uiModeStandardV12
	uiFontV12 uintptr
	buttonActionsV12 = map[uintptr]*buttonActionV12{}
	buttonWndProcV12 uintptr
	settingsWindowV12 uintptr
	settingsModeStandardV12 uintptr
	settingsModeAdvancedV12 uintptr
	settingsValueV12 = map[string]uintptr{}
	settingsWndProcCallbackV12 uintptr
	settingsClassRegisteredV12 bool
	appIconBigV12 uintptr
	appIconSmallV12 uintptr
)

var standardFieldKeysV12 = map[string]bool{
	"order_type": true,
	"order_date": true,
	"customer_code": true,
	"trade_note": true,
	"employee_code": true,
	"freight_type": true,
	"cod": true,
	"freight_fee": true,
	"inv_date": true,
	"inv_time": true,
	"inv_copies": true,
	"inv_no": true,
	"tax_type": true,
	"tax_id": true,
	"inv_name": true,
}

func resetUIRegistrationV12() {
	fieldWidgetsV12 = map[string]fieldWidgetV12{}
	groupWidgetsV12 = map[string]groupWidgetV12{}
	detailWidgetsV12 = nil
	detailBoxV12 = 0
	footerV12 = 0
	modeStandardButtonV12 = 0
	modeAdvancedButtonV12 = 0
}

func ensureUIFontV12() uintptr {
	if uiFontV12 != 0 {
		return uiFontV12
	}
	fontName := wstr("Microsoft JhengHei UI")
	h, _, _ := pCreateFontWV12.Call(
		uintptr(int32ToUintptrV12(-16)), 0, 0, 0, 400,
		0, 0, 0, 1, 0, 0, 5, 0,
		uintptr(unsafe.Pointer(fontName)),
	)
	if h != 0 {
		uiFontV12 = h
		return h
	}
	font, _, _ := pGetStockObject.Call(DEFAULT_GUI_FONT)
	return font
}

func int32ToUintptrV12(v int32) uintptr { return uintptr(uint32(v)) }

func createUIControlV12(class, text string, style uint32, x, y, w, h int32, parent uintptr, id uint16) uintptr {
	hwnd := createCtrl(class, text, style, x, y, w, h, parent, id)
	pSendMessageW.Call(hwnd, WM_SETFONT, ensureUIFontV12(), 1)
	return hwnd
}

func registerFieldWidgetV12(key, group string, apply, label, value uintptr) {
	fieldWidgetsV12[key] = fieldWidgetV12{apply: apply, label: label, value: value, group: group}
}

func registerGroupWidgetV12(group string, hwnd uintptr, x, y, w int32) {
	groupWidgetsV12[group] = groupWidgetV12{hwnd: hwnd, x: x, y: y, w: w}
}

func registerDetailWidgetV12(hwnd uintptr, x, y, w, h int32) {
	detailWidgetsV12 = append(detailWidgetsV12, detailWidgetV12{hwnd: hwnd, x: x, y: y, w: w, h: h})
}

func loadUIModeV12() string {
	if settings.Combos != nil {
		mode := strings.ToLower(strings.TrimSpace(settings.Combos["__ui_mode"].Selected))
		if mode == uiModeAdvancedV12 {
			return uiModeAdvancedV12
		}
	}
	return uiModeStandardV12
}

func setUIModeV12(mode string) {
	if mode != uiModeAdvancedV12 {
		mode = uiModeStandardV12
	}
	currentUIModeV12 = mode
	if settings.Combos == nil {
		settings.Combos = map[string]ComboSetting{}
	}
	cs := settings.Combos["__ui_mode"]
	cs.Selected = mode
	settings.Combos["__ui_mode"] = cs
	_ = writeSettings()
	applyMainModeV12(mode)
}

func applyMainModeV12(mode string) {
	if mainHwnd == 0 {
		return
	}
	advanced := mode == uiModeAdvancedV12
	currentUIModeV12 = mode
	if modeStandardButtonV12 != 0 {
		state := uintptr(BST_UNCHECKED)
		if !advanced { state = BST_CHECKED }
		pSendMessageW.Call(modeStandardButtonV12, BM_SETCHECK, state, 0)
	}
	if modeAdvancedButtonV12 != 0 {
		state := uintptr(BST_UNCHECKED)
		if advanced { state = BST_CHECKED }
		pSendMessageW.Call(modeAdvancedButtonV12, BM_SETCHECK, state, 0)
	}

	groupH := int32(220)
	if advanced { groupH = 398 }
	for group, gw := range groupWidgetsV12 {
		pSetWindowPos.Call(gw.hwnd, 0, uintptr(gw.x), uintptr(gw.y), uintptr(gw.w), uintptr(groupH), 0x0004|SWP_SHOWWINDOW)
		row := int32(0)
		for _, f := range fields {
			if f.Group != group || strings.HasPrefix(f.Key, "detail_r") {
				continue
			}
			widget, ok := fieldWidgetsV12[f.Key]
			if !ok { continue }
			visible := advanced || standardFieldKeysV12[f.Key]
			if !visible {
				pShowWindow.Call(widget.apply, swHideV12)
				pShowWindow.Call(widget.label, swHideV12)
				pShowWindow.Call(widget.value, swHideV12)
				pSendMessageW.Call(widget.apply, BM_SETCHECK, BST_UNCHECKED, 0)
				continue
			}
			y := gw.y + 26 + row*26
			x := gw.x + 10
			pSetWindowPos.Call(widget.apply, 0, uintptr(x), uintptr(y), 18, 22, 0x0004|SWP_SHOWWINDOW)
			pSetWindowPos.Call(widget.label, 0, uintptr(x+22), uintptr(y+2), 105, 20, 0x0004|SWP_SHOWWINDOW)
			valueW := gw.w - 160
			if valueW < 120 { valueW = 120 }
			pSetWindowPos.Call(widget.value, 0, uintptr(x+130), uintptr(y), uintptr(valueW), 23, 0x0004|SWP_SHOWWINDOW)
			row++
		}
	}

	detailY := int32(432)
	windowH := int32(820)
	if advanced {
		detailY = 610
		windowH = 998
	}
	if detailBoxV12 != 0 {
		pSetWindowPos.Call(detailBoxV12, 0, 16, uintptr(detailY), 1518, 310, 0x0004|SWP_SHOWWINDOW)
	}
	delta := detailY - detailBaseYV12
	for _, d := range detailWidgetsV12 {
		pSetWindowPos.Call(d.hwnd, 0, uintptr(d.x), uintptr(d.y+delta), uintptr(d.w), uintptr(d.h), 0x0004|SWP_SHOWWINDOW)
	}
	if footerV12 != 0 {
		pSetWindowPos.Call(footerV12, 0, 16, uintptr(detailY+320), 1510, 22, 0x0004|SWP_SHOWWINDOW)
	}
	pSetWindowPos.Call(mainHwnd, HWND_TOP, 0, 0, 1580, uintptr(windowH), SWP_NOMOVE|SWP_SHOWWINDOW)
	setStatus(fmt.Sprintf("ERP：%s模式；尚未自動儲存", map[bool]string{true:"進階", false:"標準"}[advanced]))
}

func registerButtonActionV12(hwnd uintptr, action func(), style string) {
	if hwnd == 0 { return }
	if buttonWndProcV12 == 0 {
		buttonWndProcV12 = syscall.NewCallback(buttonWndProcHandlerV12)
	}
	old, _, _ := pSetWindowLongPtrWV12.Call(hwnd, gwlpWndProcV12, buttonWndProcV12)
	buttonActionsV12[hwnd] = &buttonActionV12{oldProc: old, action: action, style: style}
	if style != "" {
		pInvalidateRectV12.Call(hwnd, 0, 1)
	}
}

func buttonWndProcHandlerV12(hwnd uintptr, msg uint32, wParam, lParam uintptr) uintptr {
	info := buttonActionsV12[hwnd]
	if info == nil {
		r, _, _ := pDefWindowProcW.Call(hwnd, uintptr(msg), wParam, lParam)
		return r
	}
	if msg == wmPaintV12 && info.style != "" {
		paintStyledButtonV12(hwnd, info.style)
		return 0
	}
	old := info.oldProc
	r, _, _ := pCallWindowProcWV12.Call(old, hwnd, uintptr(msg), wParam, lParam)
	if msg == WM_LBUTTONUP || (msg == wmKeyUpV12 && (wParam == VK_RETURN || wParam == vkSpaceV12)) {
		if info.action != nil {
			info.action()
		}
	}
	return r
}

func rgbV12(r, g, b byte) uintptr { return uintptr(uint32(r) | uint32(g)<<8 | uint32(b)<<16) }

func paintStyledButtonV12(hwnd uintptr, style string) {
	var ps paintStructV12
	hdc, _, _ := pBeginPaintV12.Call(hwnd, uintptr(unsafe.Pointer(&ps)))
	if hdc == 0 { return }
	defer pEndPaintV12.Call(hwnd, uintptr(unsafe.Pointer(&ps)))
	var r RECT
	pGetClientRect.Call(hwnd, uintptr(unsafe.Pointer(&r)))

	bg := rgbV12(3, 155, 229)
	fg := rgbV12(255, 255, 255)
	border := rgbV12(2, 119, 189)
	if style == "shopee" { bg = rgbV12(238, 77, 45); border = rgbV12(205, 62, 35) }
	if style == "mo" { bg = rgbV12(229, 0, 170); border = rgbV12(75, 75, 75) }
	if style == "coupang" { bg = rgbV12(255,255,255); fg = rgbV12(80,80,80); border = rgbV12(188,188,188) }
	if style == "primary" { bg = rgbV12(3,155,229); border = rgbV12(2,119,189) }

	brush, _, _ := pCreateSolidBrushV12.Call(bg)
	pen, _, _ := pCreatePenV12.Call(psSolidV12, 1, border)
	oldBrush, _, _ := pSelectObjectV12.Call(hdc, brush)
	oldPen, _, _ := pSelectObjectV12.Call(hdc, pen)
	pRoundRectV12.Call(hdc, uintptr(r.Left+1), uintptr(r.Top+1), uintptr(r.Right-1), uintptr(r.Bottom-1), 12, 12)
	pSelectObjectV12.Call(hdc, oldBrush)
	pSelectObjectV12.Call(hdc, oldPen)
	pDeleteObjectV12.Call(brush)
	pDeleteObjectV12.Call(pen)

	if style == "mo" {
		mid := (r.Left+r.Right)/2
		rightBrush, _, _ := pCreateSolidBrushV12.Call(rgbV12(45,62,117))
		rr := RECT{Left:mid, Top:r.Top+2, Right:r.Right-2, Bottom:r.Bottom-2}
		pFillRectV12.Call(hdc, uintptr(unsafe.Pointer(&rr)), rightBrush)
		pDeleteObjectV12.Call(rightBrush)
	}

	font, _, _ := pSendMessageW.Call(hwnd, 0x0031, 0, 0)
	if font != 0 {
		oldFont, _, _ := pSelectObjectV12.Call(hdc, font)
		defer pSelectObjectV12.Call(hdc, oldFont)
	}
	pSetBkModeV12.Call(hdc, transparentV12)

	text := getWindowText(hwnd)
	if style == "coupang" {
		chars := []rune(text)
		colors := []uintptr{rgbV12(145,69,18), rgbV12(238,126,0), rgbV12(112,176,0), rgbV12(0,142,196)}
		if len(chars) > 0 {
			seg := (r.Right-r.Left)/int32(len(chars))
			for i, ch := range chars {
				pSetTextColorV12.Call(hdc, colors[i%len(colors)])
				rc := RECT{Left:r.Left+int32(i)*seg, Top:r.Top, Right:r.Left+int32(i+1)*seg, Bottom:r.Bottom}
				s := string(ch)
				pDrawTextWV12.Call(hdc, uintptr(unsafe.Pointer(wstr(s))), uintptr(len([]rune(s))), uintptr(unsafe.Pointer(&rc)), dtCenterV12|dtVCenterV12|dtSingleLineV12|dtNoPrefixV12)
			}
			return
		}
	}
	pSetTextColorV12.Call(hdc, fg)
	pDrawTextWV12.Call(hdc, uintptr(unsafe.Pointer(wstr(text))), uintptr(len([]rune(text))), uintptr(unsafe.Pointer(&r)), dtCenterV12|dtVCenterV12|dtSingleLineV12|dtNoPrefixV12)
}

func showImportPlaceholderV12(source string) {
	setStatus(fmt.Sprintf("匯入：%s 按鈕已就緒；格式解析將於後續版本串接", source))
	logf("INFO", "import button invoked source=%s parser=not-yet-connected", source)
}

func showSettingsWindowV12() {
	if settingsWindowV12 != 0 {
		pBringWindowToTop.Call(settingsWindowV12)
		pSetForeground.Call(settingsWindowV12)
		return
	}
	hInst, _, _ := pGetModuleHandleW.Call(0)
	if !settingsClassRegisteredV12 {
		settingsWndProcCallbackV12 = syscall.NewCallback(settingsWndProcV12)
		class := wstr("CYERPAutoInputSettingsV12")
		wc := WNDCLASSEX{CbSize:uint32(unsafe.Sizeof(WNDCLASSEX{})), LpfnWndProc:settingsWndProcCallbackV12, HInstance:hInst, HbrBackground:COLOR_WINDOW+1, LpszClassName:class}
		pRegisterClassExW.Call(uintptr(unsafe.Pointer(&wc)))
		settingsClassRegisteredV12 = true
	}
	mr := rectOf(mainHwnd)
	x := mr.Left + (mr.Right-mr.Left-560)/2
	y := mr.Top + 100
	style := uintptr(0x00C00000 | 0x00080000 | 0x00020000) // caption + sysmenu + minimize
	hwnd, _, _ := pCreateWindowExW.Call(0, uintptr(unsafe.Pointer(wstr("CYERPAutoInputSettingsV12"))), uintptr(unsafe.Pointer(wstr("CYERPAutoInput 設定"))), style|WS_VISIBLE, uintptr(x), uintptr(y), 560, 430, mainHwnd, 0, hInst, 0)
	settingsWindowV12 = hwnd
	buildSettingsControlsV12(hwnd)
	pShowWindow.Call(hwnd, SW_SHOW)
	pUpdateWindow.Call(hwnd)
}

func buildSettingsControlsV12(parent uintptr) {
	settingsValueV12 = map[string]uintptr{}
	createUIControlV12("STATIC", "啟動模式", WS_CHILD|WS_VISIBLE|SS_LEFT, 24, 24, 110, 22, parent, 0)
	settingsModeStandardV12 = createUIControlV12("BUTTON", "標準模式", WS_CHILD|WS_VISIBLE|bsAutoRadioButtonV12|wsGroupV12, 142, 20, 120, 26, parent, 1301)
	settingsModeAdvancedV12 = createUIControlV12("BUTTON", "進階模式", WS_CHILD|WS_VISIBLE|bsAutoRadioButtonV12, 272, 20, 120, 26, parent, 1302)
	if loadUIModeV12() == uiModeAdvancedV12 {
		pSendMessageW.Call(settingsModeAdvancedV12, BM_SETCHECK, BST_CHECKED, 0)
	} else {
		pSendMessageW.Call(settingsModeStandardV12, BM_SETCHECK, BST_CHECKED, 0)
	}
	createUIControlV12("STATIC", "下拉欄位預設值", WS_CHILD|WS_VISIBLE|SS_LEFT, 24, 70, 200, 22, parent, 0)
	rows := []struct{key,label string}{
		{"delivery_slot","配送時段"},
		{"inv_copies","發票聯數"},
		{"tax_type","課稅別"},
		{"customs","通關方式"},
	}
	for i, row := range rows {
		y := int32(104 + i*44)
		createUIControlV12("STATIC", row.label, WS_CHILD|WS_VISIBLE|SS_LEFT, 36, y+4, 120, 22, parent, 0)
		value := ""
		if settings.Combos != nil { value = settings.Combos[row.key].Selected }
		settingsValueV12[row.key] = createUIControlV12("EDIT", value, WS_CHILD|WS_VISIBLE|WS_BORDER|ES_AUTOHSCROLL|WS_TABSTOP, 160, y, 330, 26, parent, uint16(1320+i))
	}
	createUIControlV12("STATIC", "設定會存於本機 config\\settings.json，不會寫入 Git。", WS_CHILD|WS_VISIBLE|SS_LEFT, 36, 292, 455, 22, parent, 0)
	save := createUIControlV12("BUTTON", "儲存", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 292, 334, 96, 34, parent, 1391)
	cancel := createUIControlV12("BUTTON", "取消", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 398, 334, 96, 34, parent, 1392)
	registerButtonActionV12(save, saveSettingsWindowV12, "primary")
	registerButtonActionV12(cancel, func(){ if settingsWindowV12!=0 { pDestroyWindowV12.Call(settingsWindowV12) } }, "")
}

func saveSettingsWindowV12() {
	if settings.Combos == nil { settings.Combos = map[string]ComboSetting{} }
	for key, hwnd := range settingsValueV12 {
		cs := settings.Combos[key]
		cs.Selected = strings.TrimSpace(getWindowText(hwnd))
		settings.Combos[key] = cs
	}
	mode := uiModeStandardV12
	if checked(settingsModeAdvancedV12) { mode = uiModeAdvancedV12 }
	cs := settings.Combos["__ui_mode"]
	cs.Selected = mode
	settings.Combos["__ui_mode"] = cs
	if err := writeSettings(); err != nil {
		setStatus("設定儲存失敗，請稍後再試")
		logError("設定","settings.json","SAVE_FAILED",err.Error())
		return
	}
	applySettingsToUI()
	applyMainModeV12(mode)
	setStatus("設定已儲存")
	if settingsWindowV12 != 0 { pDestroyWindowV12.Call(settingsWindowV12) }
}

func settingsWndProcV12(hwnd uintptr, msg uint32, wParam, lParam uintptr) uintptr {
	if msg == WM_DESTROY {
		settingsWindowV12 = 0
		return 0
	}
	if msg == wmCloseV12 {
		pDestroyWindowV12.Call(hwnd)
		return 0
	}
	r, _, _ := pDefWindowProcW.Call(hwnd, uintptr(msg), wParam, lParam)
	return r
}

func makeAppIconV12(size int) uintptr {
	if size < 16 { size = 16 }
	pixels := make([]uint32, size*size)
	blue := uint32(0x00AA7200) // BGR byte order in little-endian DDB data
	white := uint32(0x00FFFFFF)
	orange := uint32(0x002D4DEE)
	for y:=0; y<size; y++ {
		for x:=0; x<size; x++ {
			pixels[y*size+x] = blue
		}
	}
	pad := size/5
	if pad < 3 { pad = 3 }
	for y:=pad; y<size-pad; y++ {
		for x:=pad; x<size-pad; x++ {
			pixels[y*size+x] = white
		}
	}
	lineH := size/12
	if lineH < 1 { lineH = 1 }
	for row:=0; row<3; row++ {
		y0 := pad + size/6 + row*(size/6)
		for y:=y0; y<y0+lineH && y<size-pad; y++ {
			for x:=pad+size/7; x<size-pad-size/7; x++ {
				pixels[y*size+x] = blue
			}
		}
	}
	// Small orange import arrow at lower right.
	for i:=0; i<size/4; i++ {
		x := size-pad-i
		y := size-pad-size/5
		if x>=0 && x<size && y>=0 && y<size { pixels[y*size+x] = orange }
		if y+i<size { pixels[(y+i)*size+(size-pad-size/5)] = orange }
	}
	color, _, _ := pCreateBitmapV12.Call(uintptr(size), uintptr(size), 1, 32, uintptr(unsafe.Pointer(&pixels[0])))
	maskStride := ((size + 15) / 16) * 2
	mask := make([]byte, maskStride*size)
	maskBmp, _, _ := pCreateBitmapV12.Call(uintptr(size), uintptr(size), 1, 1, uintptr(unsafe.Pointer(&mask[0])))
	info := iconInfoV12{FIcon:1, HbmMask:maskBmp, HbmColor:color}
	icon, _, _ := pCreateIconIndirectV12.Call(uintptr(unsafe.Pointer(&info)))
	if color!=0 { pDeleteObjectV12.Call(color) }
	if maskBmp!=0 { pDeleteObjectV12.Call(maskBmp) }
	return icon
}

func applyAppIconV12(hwnd uintptr) {
	if appIconBigV12 == 0 { appIconBigV12 = makeAppIconV12(32) }
	if appIconSmallV12 == 0 { appIconSmallV12 = makeAppIconV12(16) }
	if appIconBigV12 != 0 { pSendMessageW.Call(hwnd, wmSetIconV12, iconBigV12, appIconBigV12) }
	if appIconSmallV12 != 0 { pSendMessageW.Call(hwnd, wmSetIconV12, iconSmallV12, appIconSmallV12) }
}

func waitForUIV12() { time.Sleep(1 * time.Millisecond) }
