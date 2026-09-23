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

func setDateControlInteractiveV5(root uintptr, target ControlInfo, value string) bool {
	digits, expected, ok := normalizeDateInput(strings.TrimSpace(value))
	if !ok || isStopRequested() {
		return false
	}
	edit := focusTargetEditV4(root, target)
	if edit == 0 {
		return false
	}

	pressVK(VK_HOME)
	if !interruptibleSleep(120 * time.Millisecond) {
		return false
	}
	if !sendSequentialUnicodeV5(digits, 105*time.Millisecond) {
		return false
	}
	if !interruptibleSleep(160 * time.Millisecond) {
		return false
	}

	pressVK(VK_TAB)
	left := waitFocusLeavesTargetV5(root, target, edit, 700*time.Millisecond)
	if !left {
		commitHeaderField(root)
		if !interruptibleSleep(320 * time.Millisecond) {
			return false
		}
	}

	after := strings.TrimSpace(getWindowText(target.Hwnd))
	if after == "" {
		after = strings.TrimSpace(getWindowText(edit))
	}
	if after == expected {
		logf("INFO", "date V5 home+sequential normalization confirmed hwnd=0x%x", target.Hwnd)
		return true
	}
	logf("WARN", "date V5 normalization not confirmed hwnd=0x%x expected_format_len=%d actual_len=%d", target.Hwnd, len([]rune(expected)), len([]rune(after)))
	return false
}

// Build 18 keeps the proven sequential editor input, but delegates the actual
// cell targeting to the non-maximized-safe geometry helper.
func setDetailCellEnterV5(root uintptr, grid ControlInfo, col int, value string) bool {
	return setDetailCellAtRowV18(root, grid, 0, col, value)
}
