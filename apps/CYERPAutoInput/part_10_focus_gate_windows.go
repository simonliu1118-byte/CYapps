//go:build windows

package main

import (
	"strings"
	"time"
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

// writeVerifiedEditV4 is the new-entry writer used by Build 5.
// It NEVER clears an ERP field and NEVER uses Ctrl+A / Shift+Home / Shift+End
// or WM_SETTEXT. This project currently automates new documents, so selected
// fields are expected to be blank. If reliable readback shows an existing value,
// the field is skipped rather than overwritten or appended to.
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

	// Send WM_CHAR directly to the already-confirmed editor. This bypasses the
	// active IME while still supporting Unicode text, and does not alter any
	// pre-existing ERP value before typing.
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

	// Some Delphi/DevExpress editors do not expose their live buffer through
	// GetWindowText while editing. Focus was already gated and the characters
	// were dispatched directly to that editor, so do not attempt a destructive
	// fallback. ERP validation/commit remains responsible for final acceptance.
	logf("INFO", "new-entry write dispatched edit=0x%x class=%q requested_len=%d readback_len=%d (no clear/no fallback)", edit, className(edit), len([]rune(value)), len([]rune(after)))
	return true
}

func setTextControlInteractiveV4(root uintptr, target ControlInfo, value string) bool {
	edit := focusTargetEditV4(root, target)
	if edit == 0 {
		return false
	}
	return writeVerifiedEditV4(edit, value)
}

// Date fields use the sequential masked-edit implementation. Keeping this
// wrapper preserves the existing call sites while avoiding regression in other
// normal-field input logic.
func setDateControlInteractiveV4(root uintptr, target ControlInfo, value string) bool {
	return setDateControlInteractiveV5(root, target, value)
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

// Detail cells use the click -> Enter -> input -> Enter sequence.
func setDetailCellEnterV4(root uintptr, grid ControlInfo, col int, value string) bool {
	return setDetailCellEnterV5(root, grid, col, value)
}
