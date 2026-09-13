//go:build windows

package main

import (
	"fmt"
	"runtime/debug"
	"syscall"
	"unsafe"
)

func utf16Ptr(s string) *uint16 { return syscall.StringToUTF16Ptr(s) }

func rgb(r, g, b byte) uintptr { return uintptr(r) | uintptr(g)<<8 | uintptr(b)<<16 }

func loword(v uintptr) uint16 { return uint16(v & 0xFFFF) }
func hiword(v uintptr) uint16 { return uint16((v >> 16) & 0xFFFF) }

func getText(hwnd uintptr) string {
	n, _, _ := procGetWindowTextLengthW.Call(hwnd)
	buf := make([]uint16, n+1)
	procGetWindowTextW.Call(hwnd, uintptr(unsafe.Pointer(&buf[0])), n+1)
	return syscall.UTF16ToString(buf)
}

func setText(hwnd uintptr, s string) {
	procSetWindowTextW.Call(hwnd, uintptr(unsafe.Pointer(utf16Ptr(s))))
}

func createFont(height int32, weight int32, face string) uintptr {
	h, _, _ := procCreateFontW.Call(
		uintptr(height), 0, 0, 0, uintptr(weight),
		0, 0, 0, 1, 0, 0, 5, 0,
		uintptr(unsafe.Pointer(utf16Ptr(face))),
	)
	return h
}

func setControlFont(hwnd, font uintptr) {
	procSendMessageW.Call(hwnd, WM_SETFONT, font, 1)
}

func createChild(exStyle uint32, class, text string, style uint32, x, y, w, h int32, id int) uintptr {
	hwnd, _, _ := procCreateWindowExW.Call(
		uintptr(exStyle),
		uintptr(unsafe.Pointer(utf16Ptr(class))),
		uintptr(unsafe.Pointer(utf16Ptr(text))),
		uintptr(style),
		uintptr(x), uintptr(y), uintptr(w), uintptr(h),
		mainHwnd, uintptr(id), hInstance, 0,
	)
	return hwnd
}

func createControls() {
	// Left input grid
	const leftX int32 = 38
	const gridY int32 = 134
	const rowH int32 = 52
	nameX, nameW := leftX, int32(205)
	qtyX, qtyW := leftX+214, int32(72)
	priceX, priceW := leftX+295, int32(112)

	for i := 0; i < 5; i++ {
		y := gridY + int32(i)*rowH + 6
		nameEdits[i] = createChild(WS_EX_CLIENTEDGE, "EDIT", "", WS_CHILD|WS_VISIBLE|WS_TABSTOP|ES_AUTOHSCROLL, nameX, y, nameW, 34, 1000+i*3)
		qtyEdits[i] = createChild(WS_EX_CLIENTEDGE, "EDIT", "", WS_CHILD|WS_VISIBLE|WS_TABSTOP|ES_AUTOHSCROLL, qtyX, y, qtyW, 34, 1001+i*3)
		priceEdits[i] = createChild(WS_EX_CLIENTEDGE, "EDIT", "", WS_CHILD|WS_VISIBLE|WS_TABSTOP|ES_AUTOHSCROLL, priceX, y, priceW, 34, 1002+i*3)

		setControlFont(nameEdits[i], fontNormal)
		setControlFont(qtyEdits[i], fontNormal)
		setControlFont(priceEdits[i], fontNormal)
		procSendMessageW.Call(nameEdits[i], EM_SETLIMITTEXT, 40, 0)
		procSendMessageW.Call(qtyEdits[i], EM_SETLIMITTEXT, 3, 0)
		procSendMessageW.Call(priceEdits[i], EM_SETLIMITTEXT, 7, 0)

		editMetas[nameEdits[i]] = editMeta{kind: fieldName}
		editMetas[qtyEdits[i]] = editMeta{kind: fieldQty}
		editMetas[priceEdits[i]] = editMeta{kind: fieldMoney}
		orderedEdits = append(orderedEdits, nameEdits[i], qtyEdits[i], priceEdits[i])
	}

	discountEdit = createChild(WS_EX_CLIENTEDGE, "EDIT", "", WS_CHILD|WS_VISIBLE|WS_TABSTOP|ES_AUTOHSCROLL, 196, 424, 150, 36, ID_DISCOUNT)
	setControlFont(discountEdit, fontNormal)
	procSendMessageW.Call(discountEdit, EM_SETLIMITTEXT, 8, 0)
	editMetas[discountEdit] = editMeta{kind: fieldDiscount}
	orderedEdits = append(orderedEdits, discountEdit)

	// 重新計算按鈕移除；離開欄位時即自動換算。清空按鈕靠右放置。
	buttonClear = createChild(0, "BUTTON", "清空全部", WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_PUSHBUTTON, 313, 482, 132, 42, ID_CLEAR)
	setControlFont(buttonClear, fontNormal)

	// Edit subclass for Enter navigation and input filtering.
	editCallback = syscall.NewCallback(editWndProc)
	for idx, hwnd := range orderedEdits {
		meta := editMetas[hwnd]
		meta.order = idx
		editMetas[hwnd] = meta
		old, _, _ := procSetWindowLongPtrW.Call(hwnd, ^uintptr(3), editCallback) // GWL_WNDPROC = -4
		if originalEditProc == 0 {
			originalEditProc = old
		}
	}
}

func validQtyText(s string) bool {
	if s == "" {
		return true
	}
	for _, r := range s {
		if r < '0' || r > '9' {
			return false
		}
	}
	return true
}

func validMoneyText(s string) bool {
	if s == "" {
		return true
	}
	dots := 0
	decimalDigits := 0
	seenDot := false
	for _, r := range s {
		if r == '.' {
			dots++
			if dots > 1 {
				return false
			}
			seenDot = true
			continue
		}
		if r < '0' || r > '9' {
			return false
		}
		if seenDot {
			decimalDigits++
			if decimalDigits > 2 {
				return false
			}
		}
	}
	return true
}

func validDiscountText(s string) bool {
	return validQtyText(s)
}

func getSelection(hwnd uintptr) (int, int) {
	var start, end uint32
	procSendMessageW.Call(hwnd, EM_GETSEL, uintptr(unsafe.Pointer(&start)), uintptr(unsafe.Pointer(&end)))
	return int(start), int(end)
}

func proposedText(hwnd uintptr, insert string) string {
	current := []rune(getText(hwnd))
	start, end := getSelection(hwnd)
	if start < 0 {
		start = 0
	}
	if end < start {
		end = start
	}
	if start > len(current) {
		start = len(current)
	}
	if end > len(current) {
		end = len(current)
	}
	return string(current[:start]) + insert + string(current[end:])
}

func readClipboardText() string {
	ok, _, _ := procOpenClipboard.Call(mainHwnd)
	if ok == 0 {
		return ""
	}
	defer procCloseClipboard.Call()
	h, _, _ := procGetClipboardData.Call(CF_UNICODETEXT)
	if h == 0 {
		return ""
	}
	p, _, _ := procGlobalLock.Call(h)
	if p == 0 {
		return ""
	}
	defer procGlobalUnlock.Call(h)
	sizeBytes, _, _ := procGlobalSize.Call(h)
	if sizeBytes < 2 {
		return ""
	}
	// Clipboard strings are NUL-terminated UTF-16. Use the actual allocation
	// size instead of probing an arbitrary memory range.
	clipboardUnits := unsafe.Slice((*uint16)(unsafe.Pointer(p)), int(sizeBytes/2))
	end := 0
	for end < len(clipboardUnits) && clipboardUnits[end] != 0 {
		end++
	}
	return syscall.UTF16ToString(clipboardUnits[:end])
}

func editWndProc(hwnd uintptr, msg uint32, wParam, lParam uintptr) (ret uintptr) {
	defer func() {
		if r := recover(); r != nil {
			panicText := fmt.Sprintf("editWndProc panic: %v\n%s", r, debug.Stack())
			traceLog("EDIT_WNDPROC_PANIC", "%s", panicText)
			traceLog("EDIT_WNDPROC_STACK", "%s", panicText)
			ret = 0
		}
	}()

	meta, ok := editMetas[hwnd]
	if !ok || originalEditProc == 0 {
		r, _, _ := procDefWindowProcW.Call(hwnd, uintptr(msg), wParam, lParam)
		return r
	}

	switch msg {
	case WM_SETFOCUS:
		traceLog("EDIT_FOCUS_GAINED", "field=%s", describeEdit(hwnd))

	case WM_KILLFOCUS:
		traceLog("EDIT_FOCUS_LOST", "field=%s next_hwnd=%d", describeEdit(hwnd), wParam)

	case WM_GETDLGCODE:
		r, _, _ := procCallWindowProcW.Call(originalEditProc, hwnd, uintptr(msg), wParam, lParam)
		return r | DLGC_WANTCHARS | DLGC_WANTTAB | DLGC_WANTALLKEYS

	case WM_KEYDOWN:
		if wParam == VK_RETURN || wParam == VK_TAB {
			keyName := "ENTER"
			if wParam == VK_TAB {
				keyName = "TAB"
			}
			traceLog("NAVIGATION_KEY", "key=%s field=%s order=%d ready=%t pending=%t", keyName, describeEdit(hwnd), meta.order, appReady, pendingFocus)
			if appReady {
				pendingFocusOrder = meta.order
				if !pendingFocus {
					pendingFocus = true
					okPost, _, _ := procPostMessageW.Call(mainHwnd, WM_APP_FOCUS, 0, 0)
					traceLog("FOCUS_CHANGE_QUEUED", "from=%s post_result=%d", describeEdit(hwnd), okPost)
				} else {
					traceLog("FOCUS_CHANGE_COALESCED", "from=%s", describeEdit(hwnd))
				}
			}
			return 0
		}

	case WM_CHAR:
		if wParam == VK_RETURN || wParam == VK_TAB {
			// 部分鍵盤／輸入法環境可能只可靠地送出 WM_CHAR；若
			// WM_KEYDOWN 尚未排入焦點切換，這裡補排一次。重複事件會合併。
			if appReady {
				pendingFocusOrder = meta.order
				if !pendingFocus {
					pendingFocus = true
					posted, _, _ := procPostMessageW.Call(mainHwnd, WM_APP_FOCUS, 0, 0)
					traceLog("FOCUS_CHANGE_QUEUED_CHAR", "field=%s post_result=%d", describeEdit(hwnd), posted)
				}
			}
			return 0
		}
		ch := rune(wParam)
		if ch < 32 {
			break
		}
		candidate := proposedText(hwnd, string(ch))
		valid := true
		switch meta.kind {
		case fieldQty:
			valid = validQtyText(candidate)
		case fieldMoney:
			valid = validMoneyText(candidate)
		case fieldDiscount:
			valid = validDiscountText(candidate)
		}
		if !valid {
			traceLog("INPUT_REJECTED", "field=%s char=%q candidate=%q", describeEdit(hwnd), string(ch), candidate)
			procMessageBeep.Call(0xFFFFFFFF)
			return 0
		}

	case WM_PASTE:
		if meta.kind == fieldQty || meta.kind == fieldMoney || meta.kind == fieldDiscount {
			clip := readClipboardText()
			candidate := proposedText(hwnd, clip)
			valid := true
			switch meta.kind {
			case fieldQty:
				valid = validQtyText(candidate)
			case fieldMoney:
				valid = validMoneyText(candidate)
			case fieldDiscount:
				valid = validDiscountText(candidate)
			}
			if !valid {
				traceLog("PASTE_REJECTED", "field=%s clipboard=%q", describeEdit(hwnd), clip)
				procMessageBeep.Call(0xFFFFFFFF)
				return 0
			}
		}
	}

	r, _, _ := procCallWindowProcW.Call(originalEditProc, hwnd, uintptr(msg), wParam, lParam)
	return r
}
