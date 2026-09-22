//go:build windows

package main

import (
	"strings"
	"time"
	"unsafe"
)

// hwndInsideV4 reports whether child is target itself or one of its descendants.
func hwndInsideV4(child, target uintptr) bool {
	if child == 0 || target == 0 {
		return false
	}
	for cur := child; cur != 0; {
		if cur == target {
			return true
		}
		parent, _, _ := pGetParent.Call(cur)
		if parent == cur {
			break
		}
		cur = parent
	}
	return false
}

func pointInsideRectV4(r RECT, x, y int32) bool {
	return x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom
}

// waitFocusedEditForTargetV4 is the safety gate for normal ERP fields.
// No character may be sent until the actual keyboard focus is confirmed to be
// the target TDBEdit (or an edit descendant of the target control).
func waitFocusedEditForTargetV4(root uintptr, target ControlInfo, timeout time.Duration) uintptr {
	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		if isStopRequested() {
			return 0
		}
		focus := focusedControlOfForeground(root)
		if focus != 0 {
			cls := strings.ToUpper(className(focus))
			if strings.Contains(cls, "EDIT") && (focus == target.Hwnd || hwndInsideV4(focus, target.Hwnd)) {
				if windowStyle(focus)&ES_READONLY == 0 {
					return focus
				}
			}
		}
		if !interruptibleSleep(35 * time.Millisecond) {
			return 0
		}
	}
	return 0
}

func focusTargetEditV4(root uintptr, target ControlInfo) uintptr {
	if isStopRequested() || !prepareERPWindow(root) {
		return 0
	}
	r := target.Rect
	x := (r.Left + r.Right) / 2
	y := (r.Top + r.Bottom) / 2
	for attempt := 1; attempt <= 3; attempt++ {
		if isStopRequested() {
			return 0
		}
		clickScreenPoint(x, y)
		if !interruptibleSleep(90 * time.Millisecond) {
			return 0
		}
		if edit := waitFocusedEditForTargetV4(root, target, 220*time.Millisecond); edit != 0 {
			logf("INFO", "focus gate ready target=0x%x edit=0x%x class=%q attempt=%d", target.Hwnd, edit, className(edit), attempt)
			return edit
		}
	}
	focus := focusedControlOfForeground(root)
	logf("WARN", "focus gate failed target=0x%x/%s focus=0x%x/%s", target.Hwnd, target.Class, focus, className(focus))
	return 0
}

// writeVerifiedEditV4 restores the proven V0.0.10-style SendInput Unicode path,
// but only after focus is confirmed. KEYEVENTF_UNICODE avoids dependence on the
// user's current Chinese/English IME state. Ctrl+A is never used.
func writeVerifiedEditV4(edit uintptr, value string) bool {
	if edit == 0 || isStopRequested() {
		return false
	}
	if !clearFocusedEditSelectionV2(edit) {
		return false
	}
	if value != "" {
		sendUnicodeText(value)
		if !interruptibleSleep(140 * time.Millisecond) {
			return false
		}
	}
	if getWindowText(edit) == value {
		return true
	}

	// Delphi data-aware TDBEdit accepted WM_SETTEXT in the earlier working
	// prototype. Use it only after focus has been verified, then read back.
	ret, _, _ := pSendMessageW.Call(edit, WM_SETTEXT, 0, uintptr(unsafe.Pointer(wstr(value))))
	if !interruptibleSleep(100 * time.Millisecond) {
		return false
	}
	if ret != 0 && getWindowText(edit) == value {
		logf("INFO", "focus gate WM_SETTEXT fallback success edit=0x%x chars=%d", edit, len([]rune(value)))
		return true
	}
	logf("WARN", "focus gate write mismatch edit=0x%x expected_len=%d actual_len=%d", edit, len([]rune(value)), len([]rune(getWindowText(edit))))
	return false
}

func setTextControlInteractiveV4(root uintptr, target ControlInfo, value string) bool {
	edit := focusTargetEditV4(root, target)
	if edit == 0 {
		return false
	}
	return writeVerifiedEditV4(edit, value)
}

func setDateControlInteractiveV4(root uintptr, target ControlInfo, value string) bool {
	digits, expected, ok := normalizeDateInput(strings.TrimSpace(value))
	if !ok || isStopRequested() {
		return false
	}
	edit := focusTargetEditV4(root, target)
	if edit == 0 || !writeVerifiedEditV4(edit, digits) {
		return false
	}
	pressVK(VK_TAB)
	if !interruptibleSleep(320 * time.Millisecond) {
		return false
	}
	after := strings.TrimSpace(getWindowText(target.Hwnd))
	if after == "" {
		after = strings.TrimSpace(getWindowText(edit))
	}
	if after == expected {
		logf("INFO", "date V4 normalized by ERP hwnd=0x%x", target.Hwnd)
		return true
	}
	commitHeaderField(root)
	if !interruptibleSleep(240 * time.Millisecond) {
		return false
	}
	after = strings.TrimSpace(getWindowText(target.Hwnd))
	if after == "" {
		after = strings.TrimSpace(getWindowText(edit))
	}
	if after == expected {
		logf("INFO", "date V4 normalized after commit hwnd=0x%x", target.Hwnd)
		return true
	}
	logf("WARN", "date V4 normalization not confirmed hwnd=0x%x after_len=%d", target.Hwnd, len([]rune(after)))
	return false
}

// activateDetailFirstRowV4 performs the separate first body click observed in
// COPI08. This click only creates/activates the first grid row; it never types.
func activateDetailFirstRowV4(root uintptr, grid ControlInfo) bool {
	if isStopRequested() || !prepareERPWindow(root) {
		return false
	}
	x := grid.Rect.Left + 30
	y := grid.Rect.Top + 33
	clickScreenPoint(x, y)
	if !interruptibleSleep(240 * time.Millisecond) {
		return false
	}
	logf("INFO", "detail first-row activation click point=%d,%d focus=0x%x/%s", x, y, focusedControlOfForeground(root), className(focusedControlOfForeground(root)))
	return true
}

func focusedGridEditorV4(root uintptr, grid ControlInfo) uintptr {
	focus := focusedControlOfForeground(root)
	if focus == 0 || focus == grid.Hwnd {
		return 0
	}
	cls := strings.ToUpper(className(focus))
	if !strings.Contains(cls, "EDIT") {
		return 0
	}
	r := rectOf(focus)
	cx := (r.Left + r.Right) / 2
	cy := (r.Top + r.Bottom) / 2
	if !pointInsideRectV4(grid.Rect, cx, cy) {
		return 0
	}
	return focus
}

func waitGridEditorV4(root uintptr, grid ControlInfo, timeout time.Duration) uintptr {
	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		if isStopRequested() {
			return 0
		}
		if edit := focusedGridEditorV4(root, grid); edit != 0 {
			return edit
		}
		if !interruptibleSleep(35 * time.Millisecond) {
			return 0
		}
	}
	return 0
}

// setDetailCellEnterV4 follows the real-grid behavior confirmed by the user:
// click the cell once, wait, press Enter, then WAIT FOR A REAL GRID EDITOR.
// No input is sent while focus remains on TcxGridSite.
func setDetailCellEnterV4(root uintptr, grid ControlInfo, col int, value string) bool {
	if isStopRequested() {
		return false
	}
	ratio, ok := detailColumnRatio(col)
	if !ok {
		return false
	}
	w := float64(grid.Rect.Right - grid.Rect.Left)
	x := grid.Rect.Left + int32(w*ratio)
	y := grid.Rect.Top + 33

	for attempt := 1; attempt <= 2; attempt++ {
		if isStopRequested() || !prepareERPWindow(root) {
			return false
		}
		clickScreenPoint(x, y)
		if !interruptibleSleep(160 * time.Millisecond) {
			return false
		}
		pressVK(VK_RETURN)
		if edit := waitGridEditorV4(root, grid, 800*time.Millisecond); edit != 0 {
			logf("INFO", "detail editor ready col=%d edit=0x%x/%s attempt=%d", col, edit, className(edit), attempt)
			if !writeVerifiedEditV4(edit, value) {
				return false
			}
			pressVK(VK_TAB)
			return interruptibleSleep(220 * time.Millisecond)
		}
		focus := focusedControlOfForeground(root)
		logf("WARN", "detail editor not ready col=%d attempt=%d focus=0x%x/%s", col, attempt, focus, className(focus))
	}
	return false
}
