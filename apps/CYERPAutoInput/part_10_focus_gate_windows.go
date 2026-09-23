//go:build windows

package main

import (
	"strings"
	"time"
)

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

func writeVerifiedEditV4(edit uintptr, value string) bool {
	if edit == 0 || isStopRequested() {
		return false
	}
	before := getWindowText(edit)
	if before == value {
		logf("INFO", "new-entry field already matches edit=0x%x chars=%d", edit, len([]rune(value)))
		return true
	}
	if strings.TrimSpace(before) != "" {
		logf("WARN", "new-entry write skipped non-empty edit=0x%x class=%q before_len=%d requested_len=%d", edit, className(edit), len([]rune(before)), len([]rune(value)))
		return false
	}
	if value == "" {
		return true
	}
	if !sendWMCharTextV2(edit, value) {
		return false
	}
	if !interruptibleSleep(140 * time.Millisecond) {
		return false
	}
	after := getWindowText(edit)
	if after == value {
		logf("INFO", "new-entry write exact match edit=0x%x chars=%d", edit, len([]rune(value)))
		return true
	}
	logf("INFO", "new-entry write dispatched edit=0x%x class=%q requested_len=%d readback_len=%d (no clear/no fallback)", edit, className(edit), len([]rune(value)), len([]rune(after)))
	return true
}

func isHeaderOrderTypeTargetV6(root uintptr, target ControlInfo) bool {
	rows := buildHeaderRows(root)
	return len(rows) == 2 && len(rows[0]) == 3 && rows[0][0].Hwnd == target.Hwnd
}

func writeReplaceEditV6(edit uintptr, value string) bool {
	if edit == 0 || isStopRequested() {
		return false
	}
	if getWindowText(edit) == value {
		return true
	}
	// Build 7: 銷貨單別 is the explicit replacement exception. Always dispatch
	// the full forced clear sequence even when Delphi readback is stale/empty.
	if !clearFocusedEditSelectionForcedV7(edit) {
		return false
	}
	if value != "" && !sendWMCharTextV2(edit, value) {
		return false
	}
	if !interruptibleSleep(140 * time.Millisecond) {
		return false
	}
	after := getWindowText(edit)
	if after == value {
		logf("INFO", "order type replace exact match edit=0x%x chars=%d", edit, len([]rune(value)))
		return true
	}
	logf("INFO", "order type replace dispatched edit=0x%x requested_len=%d readback_len=%d", edit, len([]rune(value)), len([]rune(after)))
	return true
}

func setTextControlInteractiveV4(root uintptr, target ControlInfo, value string) bool {
	edit := focusTargetEditV4(root, target)
	if edit == 0 {
		return false
	}
	if isHeaderOrderTypeTargetV6(root, target) {
		return writeReplaceEditV6(edit, value)
	}
	return writeVerifiedEditV4(edit, value)
}

func setDateControlInteractiveV4(root uintptr, target ControlInfo, value string) bool {
	return setDateControlInteractiveV5(root, target, value)
}

func activateDetailFirstRowV4(root uintptr, grid ControlInfo) bool {
	return activateDetailFirstRowOpticalV011(root, freshDetailGridV18(grid))
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

func setDetailCellEnterV4(root uintptr, grid ControlInfo, col int, value string) bool {
	return setDetailCellEnterV5(root, grid, col, value)
}
