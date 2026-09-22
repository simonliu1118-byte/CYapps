//go:build windows

package main

import (
	"strings"
	"time"
)

// clearFocusedEditSelectionForcedV7 is used only for fields that must replace
// an ERP-provided default (currently 銷貨單別). It deliberately does not depend
// on GetWindowText because Delphi DB-aware edits can expose stale/empty text.
// Ctrl+A and Shift-selection are never used here.
func clearFocusedEditSelectionForcedV7(focus uintptr) bool {
	if focus == 0 || isStopRequested() {
		return false
	}

	// Build 9: COPI08 repeatedly ignored Shift+Home even when sent atomically.
	// 銷貨單別 is at most 4 characters, so use the deterministic fallback the
	// ERP accepts reliably: move to End, then send Backspace four times.
	pressVK(VK_END)
	if !interruptibleSleep(110 * time.Millisecond) {
		return false
	}

	for i := 0; i < 4; i++ {
		pressVK(VK_BACK)
		if !interruptibleSleep(85 * time.Millisecond) {
			return false
		}
	}
	if !interruptibleSleep(120 * time.Millisecond) {
		return false
	}

	logf("INFO", "forced replacement clear dispatched hwnd=0x%x class=%q sequence=End+Backspace*4", focus, className(focus))
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

// Build 11: always send popup type-ahead as Unicode characters instead of
// physical 0-9 / A-Z virtual keys. This keeps selection independent of the
// active Windows IME; e.g. requested "5" must stay "5" rather than becoming
// a Zhuyin symbol when a Chinese input method is active.
func sendComboTypeAheadV7(value string) bool {
	return sendSequentialUnicodeV5(value, 100*time.Millisecond)
}

// selectDevExpressComboDirectV7 intentionally does NOT enumerate/read option
// lists. The requested value from CYERPAutoInput is used as popup type-ahead,
// then Enter confirms that exact requested item.
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
	logf("INFO", "combo direct selection dispatched hwnd=0x%x class=%q requested_len=%d unicode_typeahead=true", target.Hwnd, target.Class, len([]rune(wanted)))
	return true
}
