//go:build windows

package main

import (
	"strings"
	"time"
)

// freshDetailGridV18 refreshes the screen rect immediately before every mouse
// action. COPI08 can be restored/moved/resized between row activation and the
// next cell click, so carrying a stale RECT across those steps is unsafe.
func freshDetailGridV18(grid ControlInfo) ControlInfo {
	if grid.Hwnd != 0 {
		grid.Rect = rectOf(grid.Hwnd)
	}
	return grid
}

// V0.0.11 no longer derives a detail-column X from the current window width.
// The point comes from the screenshot-derived grid geometry. If optical
// detection fails, stop rather than falling back to a guessed coordinate.
func detailColumnScreenXV18(grid ControlInfo, col int) (int32, bool) {
	grid = freshDetailGridV18(grid)
	return opticalDetailColumnXV011(grid, col)
}

func setDetailCellAtRowV18(root uintptr, grid ControlInfo, row int, col int, value string) bool {
	if row < 0 || isStopRequested() {
		return false
	}
	for attempt := 1; attempt <= 2; attempt++ {
		if isStopRequested() || !prepareERPWindow(root) {
			return false
		}
		fresh := freshDetailGridV18(grid)
		x, y, ok := opticalDetailPointV011(fresh, row, col)
		if !ok {
			logf("WARN", "detail V0.0.11 optical target unavailable row=%d col=%d attempt=%d", row+1, col, attempt)
			return false
		}
		clickScreenPoint(x, y)
		if !interruptibleSleep(180 * time.Millisecond) {
			return false
		}
		pressVK(VK_RETURN)
		edit := waitGridEditorV4(root, fresh, 950*time.Millisecond)
		if edit == 0 {
			focus := focusedControlOfForeground(root)
			logf("WARN", "detail V0.0.11 optical editor not ready row=%d col=%d attempt=%d point=%d,%d grid_width=%d focus=0x%x/%s", row+1, col, attempt, x, y, fresh.Rect.Right-fresh.Rect.Left, focus, className(focus))
			continue
		}
		logf("INFO", "detail V0.0.11 optical editor ready row=%d col=%d edit=0x%x/%s attempt=%d point=%d,%d", row+1, col, edit, className(edit), attempt, x, y)
		if !writeDetailSequentialV7(edit, value) {
			return false
		}
		if !interruptibleSleep(150 * time.Millisecond) {
			return false
		}
		pressVK(VK_RETURN)
		if !interruptibleSleep(300 * time.Millisecond) {
			return false
		}
		logf("INFO", "detail V0.0.11 committed by Enter row=%d col=%d", row+1, col)
		return true
	}
	return false
}

// dispatchLookupEnterV18 is intentionally narrow. Only when the foreground
// window is the ERP F2 lookup dialog and the requested key is Enter do we send
// the key directly to the focused DevExpress grid/control. This mirrors the
// confirmed manual sequence: open F2 -> click desired unit -> Enter.
func dispatchLookupEnterV18() bool {
	fg, _, _ := pGetForeground.Call()
	if fg == 0 || !strings.Contains(strings.TrimSpace(getWindowText(fg)), "F2開窗查詢") {
		return false
	}
	grid := lookupGridV14(fg)
	focus := focusedControlOfForeground(fg)
	target := focus
	if target == 0 || !hwndInsideV4(target, fg) {
		target = 0
	}
	if grid != nil {
		// Prefer the grid itself. Manual Enter is handled by the active TcxGridSite;
		// sending to an arbitrary child/painted cell can be ignored.
		target = grid.Hwnd
	}
	if target == 0 {
		logf("WARN", "unit lookup V18 Enter target missing foreground=0x%x focus=0x%x", fg, focus)
		return false
	}
	pSendMessageW.Call(target, 0x0100, uintptr(VK_RETURN), 0) // WM_KEYDOWN
	pSendMessageW.Call(target, 0x0101, uintptr(VK_RETURN), 0) // WM_KEYUP
	logf("INFO", "unit lookup V0.0.11 Enter dispatched directly target=0x%x class=%q focus_before=0x%x/%s", target, className(target), focus, className(focus))
	return true
}

// waitLookupClosedV18 keeps the confirmation path bounded and never clicks a
// fallback button.
func waitLookupClosedV18(lookup uintptr, timeout time.Duration) bool {
	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		if isStopRequested() {
			return false
		}
		vis, _, _ := pIsWindowVisible.Call(lookup)
		if vis == 0 {
			return true
		}
		if !interruptibleSleep(40 * time.Millisecond) {
			return false
		}
	}
	return false
}
