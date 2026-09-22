//go:build windows

package main

import (
	"strings"
	"time"
	"unicode/utf16"
)

const (
	vkShiftV2  = 0x10
	vkDeleteV2 = 0x2E
)

// sendWMCharTextV2 writes characters directly to the focused ERP edit control.
// This deliberately bypasses the active Windows IME so ASCII digits/codes do not
// turn into Zhuyin, while Chinese text can still be written as Unicode.
func sendWMCharTextV2(hwnd uintptr, value string) bool {
	if hwnd == 0 || isStopRequested() {
		return false
	}
	for _, u := range utf16.Encode([]rune(value)) {
		if isStopRequested() {
			return false
		}
		pSendMessageW.Call(hwnd, WM_CHAR, uintptr(u), 0)
		if !interruptibleSleep(8 * time.Millisecond) {
			return false
		}
	}
	return true
}

func shiftSelectToV2(vk uint16) bool {
	if isStopRequested() {
		return false
	}
	sendInputs([]INPUT{
		keyInput(vkShiftV2, 0, 0),
		keyInput(vk, 0, 0),
		keyInput(vk, 0, KEYEVENTF_KEYUP),
		keyInput(vkShiftV2, 0, KEYEVENTF_KEYUP),
	})
	return interruptibleSleep(35 * time.Millisecond)
}

func deleteSelectionV2() bool {
	if isStopRequested() {
		return false
	}
	sendInputs([]INPUT{
		keyInput(vkDeleteV2, 0, 0),
		keyInput(vkDeleteV2, 0, KEYEVENTF_KEYUP),
	})
	return interruptibleSleep(45 * time.Millisecond)
}

// clearFocusedEditSelectionV2 never uses Ctrl+A.
// Primary path: End -> Shift+Home -> Delete.
// Fallback: Home -> Shift+End -> Delete.
func clearFocusedEditSelectionV2(focus uintptr) bool {
	if focus == 0 || isStopRequested() {
		return false
	}
	if getWindowText(focus) == "" {
		return true
	}

	pressVK(VK_END)
	if !interruptibleSleep(30*time.Millisecond) || !shiftSelectToV2(VK_HOME) || !deleteSelectionV2() {
		return false
	}
	if getWindowText(focus) == "" {
		return true
	}

	pressVK(VK_HOME)
	if !interruptibleSleep(30*time.Millisecond) || !shiftSelectToV2(VK_END) || !deleteSelectionV2() {
		return false
	}
	if getWindowText(focus) == "" {
		return true
	}

	logf("WARN", "selection clear not confirmed hwnd=0x%x class=%q remaining_len=%d", focus, className(focus), len([]rune(getWindowText(focus))))
	return false
}

func focusedEditForTargetV2(root uintptr, target ControlInfo) uintptr {
	focus := focusedControlOfForeground(root)
	if focus != 0 && strings.Contains(strings.ToUpper(className(focus)), "EDIT") {
		return focus
	}
	if edit := descendantEdit(target.Hwnd); edit != 0 {
		return edit
	}
	return focus
}

func setTextControlInteractiveV2(root uintptr, target ControlInfo, value string) bool {
	if isStopRequested() || !prepareERPWindow(root) {
		return false
	}
	r := target.Rect
	clickScreenPoint((r.Left+r.Right)/2, (r.Top+r.Bottom)/2)
	if !interruptibleSleep(100 * time.Millisecond) {
		return false
	}

	edit := focusedEditForTargetV2(root, target)
	if edit == 0 {
		logf("WARN", "interactive V2 text no edit focus target=0x%x class=%q", target.Hwnd, target.Class)
		return false
	}
	if !clearFocusedEditSelectionV2(edit) {
		return false
	}
	if value != "" && !sendWMCharTextV2(edit, value) {
		return false
	}
	if !interruptibleSleep(120 * time.Millisecond) {
		return false
	}

	after := getWindowText(edit)
	if after == value {
		logf("INFO", "interactive V2 text exact match hwnd=0x%x class=%q chars=%d", edit, className(edit), len([]rune(value)))
		return true
	}
	logf("WARN", "interactive V2 text mismatch hwnd=0x%x class=%q expected_len=%d actual_len=%d", edit, className(edit), len([]rune(value)), len([]rune(after)))
	return false
}

func setDateControlInteractiveV2(root uintptr, target ControlInfo, value string) bool {
	digits, expected, ok := normalizeDateInput(strings.TrimSpace(value))
	if !ok || isStopRequested() || !prepareERPWindow(root) {
		logf("WARN", "date V2 input rejected before typing format_len=%d", len([]rune(value)))
		return false
	}

	r := target.Rect
	clickScreenPoint((r.Left+r.Right)/2, (r.Top+r.Bottom)/2)
	if !interruptibleSleep(100 * time.Millisecond) {
		return false
	}
	edit := focusedEditForTargetV2(root, target)
	if edit == 0 || !clearFocusedEditSelectionV2(edit) {
		return false
	}
	if !sendWMCharTextV2(edit, digits) {
		return false
	}

	pressVK(VK_TAB)
	if !interruptibleSleep(280 * time.Millisecond) {
		return false
	}
	after := strings.TrimSpace(getWindowText(target.Hwnd))
	if after == "" {
		after = strings.TrimSpace(getWindowText(edit))
	}
	if after == expected {
		logf("INFO", "date V2 normalized by ERP hwnd=0x%x", target.Hwnd)
		return true
	}

	commitHeaderField(root)
	if !interruptibleSleep(220 * time.Millisecond) {
		return false
	}
	after = strings.TrimSpace(getWindowText(target.Hwnd))
	if after == "" {
		after = strings.TrimSpace(getWindowText(edit))
	}
	if after == expected {
		logf("INFO", "date V2 normalized by ERP after blank click hwnd=0x%x", target.Hwnd)
		return true
	}
	logf("WARN", "date V2 normalization not confirmed hwnd=0x%x after_len=%d", target.Hwnd, len([]rune(after)))
	return false
}

// activateDevExpressTabForFill always performs a real click before trusting
// TcxTabSheet visibility. This avoids treating a stale/hidden tab sheet as
// already active during a multi-page automatic fill.
func activateDevExpressTabForFill(root uintptr, tabName string) bool {
	sheet := findTabSheet(root, tabName)
	if sheet == 0 || isStopRequested() {
		return false
	}
	page, _, _ := pGetParent.Call(sheet)
	if page == 0 || !strings.EqualFold(className(page), "TcxPageControl") {
		return false
	}
	x, ok := tabCenterX(tabName)
	if !ok || !prepareERPWindow(root) {
		return false
	}

	pr := rectOf(page)
	y := int32(12)
	logf("INFO", "tab fill force-click target=%s point=%d,%d page=0x%x", tabName, pr.Left+x, pr.Top+y, page)
	clickScreenPoint(pr.Left+x, pr.Top+y)
	for i := 0; i < 10; i++ {
		if !interruptibleSleep(60 * time.Millisecond) {
			return false
		}
		if v, _, _ := pIsWindowVisible.Call(sheet); v != 0 {
			logf("INFO", "tab fill activated by real click: %s", tabName)
			return true
		}
	}

	lp := uintptr(uint32(uint16(x)) | uint32(uint16(y))<<16)
	pSendMessageW.Call(page, WM_LBUTTONDOWN, MK_LBUTTON, lp)
	pSendMessageW.Call(page, WM_LBUTTONUP, 0, lp)
	for i := 0; i < 8; i++ {
		if !interruptibleSleep(60 * time.Millisecond) {
			return false
		}
		if v, _, _ := pIsWindowVisible.Call(sheet); v != 0 {
			logf("INFO", "tab fill activated by message fallback: %s", tabName)
			return true
		}
	}
	logf("WARN", "tab fill activation not confirmed: %s", tabName)
	return false
}
