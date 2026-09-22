//go:build windows

package main

import (
	"strings"
	"time"
)

// sendSequentialUnicodeV5 sends one character at a time with a deliberate
// inter-key delay. SMART ERP masked/date edits update their internal mask
// between keystrokes, so burst-style input is intentionally avoided here.
func sendSequentialUnicodeV5(s string, delay time.Duration) bool {
	for _, r := range s {
		if isStopRequested() {
			return false
		}
		u := uint16(r)
		sendInputs([]INPUT{
			keyInput(0, u, KEYEVENTF_UNICODE),
			keyInput(0, u, KEYEVENTF_UNICODE|KEYEVENTF_KEYUP),
		})
		if !interruptibleSleep(delay) {
			return false
		}
	}
	return true
}

func waitFocusLeavesTargetV5(root uintptr, target ControlInfo, originalEdit uintptr, timeout time.Duration) bool {
	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		if isStopRequested() {
			return false
		}
		focus := focusedControlOfForeground(root)
		if focus != 0 && focus != originalEdit && !hwndInsideV4(focus, target.Hwnd) {
			return true
		}
		if !interruptibleSleep(35 * time.Millisecond) {
			return false
		}
	}
	return false
}

// setDateControlInteractiveV5 does not use WM_SETTEXT or one-shot text input.
// COPI08 date fields must receive 8 digits sequentially and then lose focus so
// ERP native date normalization/validation can run.
func setDateControlInteractiveV5(root uintptr, target ControlInfo, value string) bool {
	digits, expected, ok := normalizeDateInput(strings.TrimSpace(value))
	if !ok || isStopRequested() {
		return false
	}
	edit := focusTargetEditV4(root, target)
	if edit == 0 {
		return false
	}
	if !clearFocusedEditSelectionV2(edit) {
		return false
	}
	if !sendSequentialUnicodeV5(digits, 95*time.Millisecond) {
		return false
	}
	if !interruptibleSleep(140 * time.Millisecond) {
		return false
	}

	pressVK(VK_TAB)
	left := waitFocusLeavesTargetV5(root, target, edit, 650*time.Millisecond)
	if !left {
		// Fallback only changes focus. It never writes date text directly.
		commitHeaderField(root)
		if !interruptibleSleep(300 * time.Millisecond) {
			return false
		}
	}

	after := strings.TrimSpace(getWindowText(target.Hwnd))
	if after == "" {
		after = strings.TrimSpace(getWindowText(edit))
	}
	if after == expected {
		logf("INFO", "date V5 sequential normalization confirmed hwnd=0x%x", target.Hwnd)
		return true
	}
	logf("WARN", "date V5 normalization not confirmed hwnd=0x%x expected_format_len=%d actual_len=%d", target.Hwnd, len([]rune(expected)), len([]rune(after)))
	return false
}

// setDetailCellEnterV5 follows the confirmed COPI08 grid sequence:
// click target cell -> Enter to enter editor -> input -> Enter to commit.
// The next selected field is then reached by an explicit click before Enter.
func setDetailCellEnterV5(root uintptr, grid ControlInfo, col int, value string) bool {
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
		if !interruptibleSleep(170 * time.Millisecond) {
			return false
		}
		pressVK(VK_RETURN)
		edit := waitGridEditorV4(root, grid, 900*time.Millisecond)
		if edit == 0 {
			focus := focusedControlOfForeground(root)
			logf("WARN", "detail V5 editor not ready col=%d attempt=%d focus=0x%x/%s", col, attempt, focus, className(focus))
			continue
		}
		logf("INFO", "detail V5 editor ready col=%d edit=0x%x/%s attempt=%d", col, edit, className(edit), attempt)
		if !writeVerifiedEditV4(edit, value) {
			return false
		}
		if !interruptibleSleep(120 * time.Millisecond) {
			return false
		}

		// In COPI08, Enter commits the current detail cell more reliably than
		// Tab. The next iteration will click the next selected column explicitly.
		pressVK(VK_RETURN)
		if !interruptibleSleep(260 * time.Millisecond) {
			return false
		}
		logf("INFO", "detail V5 committed by Enter col=%d", col)
		return true
	}
	return false
}
