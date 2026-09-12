//go:build windows

package main

import "unsafe"

const (
	wmDrawItem        = 0x002B
	vkTab             = 0x0009
	tcsOwnerDrawFixed = 0x2000
	odtTab            = 101
	odsSelected       = 0x0001
	tabAccentHeight   = 3
)

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

var cyInvoiceTabTitles = []string{"開立發票", "已開立發票清單"}

// handleTabOwnerDraw is intentionally limited to the visual header of the
// native SysTabControl32. Selection, keyboard navigation, TCN_SELCHANGE and the
// page frame remain owned by the Win32 tab control. No business state is ever
// changed from this paint path.
func handleTabOwnerDraw(lParam unsafe.Pointer) bool {
	if lParam == nil || tabHandle == 0 { return false }
	item := (*drawItem)(lParam)
	if item.ControlType != odtTab || item.ItemWindow != tabHandle { return false }
	index := int(item.ItemID)
	if index < 0 || index >= len(cyInvoiceTabTitles) { return false }

	rect := item.Rect
	if rect.Right <= rect.Left || rect.Bottom <= rect.Top { return true }
	selected := item.ItemState&odsSelected != 0

	// Modern underline profile: the native Tab still owns the actual selection
	// and page frame; this paint-only path gives both headers a clean rectangular
	// background and marks only the selected header with a blue underline.
	background, _, _ := procCreateSolidBrush.Call(uintptr(rgb(240, 240, 240)))
	procFillRect.Call(item.DC, uintptr(unsafe.Pointer(&rect)), background)
	procDeleteObject.Call(background)
	if selected {
		accentRect := rect
		accentRect.Top = accentRect.Bottom - tabAccentHeight
		accent, _, _ := procCreateSolidBrush.Call(uintptr(rgb(0, 102, 204)))
		procFillRect.Call(item.DC, uintptr(unsafe.Pointer(&accentRect)), accent)
		procDeleteObject.Call(accent)
	}

	procSetBkMode.Call(item.DC, 1)
	textColor := rgb(72, 72, 72)
	font := contentFont
	if selected {
		textColor = rgb(0, 82, 180)
		font = boldFont
	}
	procSetTextColor.Call(item.DC, uintptr(textColor))
	oldFont, _, _ := procSelectObject.Call(item.DC, font)
	textRect := rect
	textRect.Left += 4
	textRect.Right -= 4
	textRect.Bottom -= tabAccentHeight
	text := mustUTF16Ptr(cyInvoiceTabTitles[index])
	procDrawTextW.Call(item.DC, uintptr(unsafe.Pointer(text)), ^uintptr(0), uintptr(unsafe.Pointer(&textRect)), dtCenter|dtVCenter|dtSingleLine)
	procSelectObject.Call(item.DC, oldFont)
	return true
}
