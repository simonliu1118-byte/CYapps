//go:build windows

package main

import (
	"strings"
	"time"
)

// clearFocusedEditSelectionForcedV7 is used only for fields that must replace
// an ERP-provided default (currently 銷貨單別). It deliberately does not depend
// on GetWindowText because Delphi DB-aware edits can expose stale/empty text.
// Ctrl+A is never used.
func clearFocusedEditSelectionForcedV7(focus uintptr) bool {
	if focus == 0 || isStopRequested() {
		return false
	}

	// End -> Shift+Home -> Delete, with visible spacing between each keyboard
	// state transition. Build 6 sent the Shift chord too quickly for COPI08.
	pressVK(VK_END)
	if !interruptibleSleep(90 * time.Millisecond) {
		return false
	}

	sendInputs([]INPUT{keyInput(vkShiftV2, 0, 0)})
	if !interruptibleSleep(70 * time.Millisecond) {
		return false
	}
	sendInputs([]INPUT{keyInput(VK_HOME, 0, 0), keyInput(VK_HOME, 0, KEYEVENTF_KEYUP)})
	if !interruptibleSleep(80 * time.Millisecond) {
		return false
	}
	sendInputs([]INPUT{keyInput(vkShiftV2, 0, KEYEVENTF_KEYUP)})
	if !interruptibleSleep(80 * time.Millisecond) {
		return false
	}
	sendInputs([]INPUT{keyInput(vkDeleteV2, 0, 0), keyInput(vkDeleteV2, 0, KEYEVENTF_KEYUP)})
	if !interruptibleSleep(140 * time.Millisecond) {
		return false
	}

	logf("INFO", "forced replacement clear dispatched hwnd=0x%x class=%q sequence=End+ShiftHome+Delete", focus, className(focus))
	return true
}

// writeDetailSequentialV7 keeps the confirmed grid editor workflow but sends
// detail text one character at a time, like the stable date input path. No
// clear/select/delete is performed because detail rows are NEW rows.
func writeDetailSequentialV7(edit uintptr, value string) bool {
	if edit == 0 || isStopRequested() {
		return false
	}
	before := getWindowText(edit)
	if before == value {
		return true
	}
	if strings.TrimSpace(before) != "" {
		logf("WARN", "detail sequential skipped non-empty editor hwnd=0x%x class=%q before_len=%d requested_len=%d", edit, className(edit), len([]rune(before)), len([]rune(value)))
		return false
	}
	if value == "" {
		return true
	}
	if !sendSequentialUnicodeV5(value, 105*time.Millisecond) {
		return false
	}
	if !interruptibleSleep(160 * time.Millisecond) {
		return false
	}
	after := getWindowText(edit)
	if after == value {
		logf("INFO", "detail sequential exact match hwnd=0x%x chars=%d", edit, len([]rune(value)))
		return true
	}
	// DevExpress live editors can report an empty/stale buffer while editing.
	// Characters were sent only after editor-ready gating, so avoid destructive
	// fallbacks and let Enter commit ERP-side validation.
	logf("INFO", "detail sequential dispatched hwnd=0x%x requested_len=%d readback_len=%d", edit, len([]rune(value)), len([]rune(after)))
	return true
}

func sendComboTypeAheadV7(value string) bool {
	for _, r := range value {
		if isStopRequested() {
			return false
		}
		if r >= '0' && r <= '9' {
			vk := uint16('0' + (r - '0'))
			sendInputs([]INPUT{keyInput(vk, 0, 0), keyInput(vk, 0, KEYEVENTF_KEYUP)})
		} else if r >= 'A' && r <= 'Z' {
			vk := uint16(r)
			sendInputs([]INPUT{keyInput(vk, 0, 0), keyInput(vk, 0, KEYEVENTF_KEYUP)})
		} else if r >= 'a' && r <= 'z' {
			vk := uint16(r - 'a' + 'A')
			sendInputs([]INPUT{keyInput(vk, 0, 0), keyInput(vk, 0, KEYEVENTF_KEYUP)})
		} else {
			u := uint16(r)
			sendInputs([]INPUT{keyInput(0, u, KEYEVENTF_UNICODE), keyInput(0, u, KEYEVENTF_UNICODE|KEYEVENTF_KEYUP)})
		}
		if !interruptibleSleep(100 * time.Millisecond) {
			return false
		}
	}
	return true
}

// selectDevExpressComboDirectV7 intentionally does NOT enumerate/read option
// lists. The requested value from CYERPAutoInput is used as popup type-ahead,
// then Enter confirms that exact requested item. Example: requested "7" sends
// key 7 while the popup is open, then Enter.
func selectDevExpressComboDirectV7(root uintptr, target ControlInfo, wanted string) bool {
	wanted = strings.TrimSpace(wanted)
	if wanted == "" || isStopRequested() || !prepareERPWindow(root) {
		return false
	}
	comboClickArrow(root, target)
	if !interruptibleSleep(180 * time.Millisecond) {
		return false
	}
	if !sendComboTypeAheadV7(wanted) {
		return false
	}
	if !interruptibleSleep(120 * time.Millisecond) {
		return false
	}
	pressVK(VK_RETURN)
	if !interruptibleSleep(240 * time.Millisecond) {
		return false
	}
	logf("INFO", "combo direct selection dispatched hwnd=0x%x class=%q requested=%q (no option enumeration)", target.Hwnd, target.Class, wanted)
	return true
}
