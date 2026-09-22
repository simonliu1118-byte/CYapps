//go:build windows

package main

import (
	"strings"
	"time"
)

// commitLookupFieldV3 explicitly leaves a lookup field so SMART ERP can run
// its native lookup/validation logic. It does not use Ctrl+A and it does not
// press Enter, because Enter can have field-specific meanings in COPI08.
func commitLookupFieldV3(root uintptr, target ControlInfo, sheet uintptr) bool {
	if isStopRequested() || root == 0 {
		return false
	}
	before := focusedControlOfForeground(root)
	pressVK(VK_TAB)
	if !interruptibleSleep(420 * time.Millisecond) {
		return false
	}
	after := focusedControlOfForeground(root)
	if after != 0 && after != before {
		logf("INFO", "lookup commit by Tab target=0x%x focus_before=0x%x focus_after=0x%x", target.Hwnd, before, after)
		return true
	}

	// Some Delphi data-aware edits keep focus after the first Tab while they
	// finish validation. Give the ERP one more chance by clicking a harmless
	// blank area in the active section.
	if sheet != 0 {
		clickBlankArea(sheet)
	} else {
		commitHeaderField(root)
	}
	if !interruptibleSleep(360 * time.Millisecond) {
		return false
	}
	after2 := focusedControlOfForeground(root)
	logf("INFO", "lookup commit fallback target=0x%x focus_before=0x%x focus_after=0x%x", target.Hwnd, before, after2)
	return true
}

func doubleClickScreenPointV3(x, y int32) bool {
	if isStopRequested() {
		return false
	}
	clickScreenPoint(x, y)
	if !interruptibleSleep(75 * time.Millisecond) {
		return false
	}
	clickScreenPoint(x, y)
	return interruptibleSleep(110 * time.Millisecond)
}

// setDetailCellDoubleClickV3 follows the actual TcxGrid behavior observed on
// COPI08: first click selects the cell, second click enters edit mode. F2 is
// intentionally not used because on lookup cells it opens the ERP query dialog.
func setDetailCellDoubleClickV3(root uintptr, grid ControlInfo, col int, value string) bool {
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

	if !prepareERPWindow(root) {
		return false
	}
	if !doubleClickScreenPointV3(x, y) {
		return false
	}
	focus := focusedControlOfForeground(root)
	logf("INFO", "detail double-click col=%d point=%d,%d focus=0x%x class=%q", col, x, y, focus, className(focus))
	if focus == 0 || !strings.Contains(strings.ToUpper(className(focus)), "EDIT") {
		logf("WARN", "detail edit mode not entered col=%d focus=0x%x class=%q", col, focus, className(focus))
		return false
	}
	if !clearFocusedEditSelectionV2(focus) {
		return false
	}
	if value != "" && !sendWMCharTextV2(focus, value) {
		return false
	}
	if !interruptibleSleep(110 * time.Millisecond) {
		return false
	}
	if strings.TrimSpace(getWindowText(focus)) != strings.TrimSpace(value) {
		logf("WARN", "detail text mismatch col=%d expected_len=%d actual_len=%d", col, len([]rune(value)), len([]rune(getWindowText(focus))))
		return false
	}

	// Tab commits the current cell through the grid's own navigation logic.
	pressVK(VK_TAB)
	return interruptibleSleep(180 * time.Millisecond)
}
