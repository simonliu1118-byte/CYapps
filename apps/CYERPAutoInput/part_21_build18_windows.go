//go:build windows

package main

import (
	"strings"
	"time"
)

const detailReferenceWidthV18 int32 = 1800

// freshDetailGridV18 refreshes the screen rect immediately before every mouse
// action. COPI08 can be restored/moved/resized between row activation and the
// next cell click, so carrying a stale RECT across those steps is unsafe.
func freshDetailGridV18(grid ControlInfo) ControlInfo {
	if grid.Hwnd != 0 {
		grid.Rect = rectOf(grid.Hwnd)
	}
	return grid
}

// detailColumnScreenXV18 keeps the column calibration in pixels when COPI08 is
// not maximized. The old implementation multiplied the historical column ratio
// by the *current* grid width. DevExpress keeps these columns close to fixed
// pixel widths, so shrinking the window pulled the click left into the wrong
// column (most visibly item code). Use the proven full-width calibration as a
// minimum basis; if a target column is genuinely off-screen, stop instead of
// clicking another column.
func detailColumnScreenXV18(grid ControlInfo, col int) (int32, bool) {
	ratio, ok := detailColumnRatio(col)
	if !ok {
		return 0, false
	}
	grid = freshDetailGridV18(grid)
	w := grid.Rect.Right - grid.Rect.Left
	if w <= 0 {
		return 0, false
	}
	basis := w
	if basis < detailReferenceWidthV18 {
		basis = detailReferenceWidthV18
	}
	x := grid.Rect.Left + int32(float64(basis)*ratio)
	if x < grid.Rect.Left+4 || x > grid.Rect.Right-4 {
		logf("WARN", "detail V18 target column offscreen col=%d x=%d grid=%d..%d width=%d basis=%d", col, x, grid.Rect.Left, grid.Rect.Right, w, basis)
		return 0, false
	}
	return x, true
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
		x, ok := detailColumnScreenXV18(fresh, col)
		if !ok {
			return false
		}
		visibleRow := detailVisibleRowIndexV11(fresh, row)
		y := fresh.Rect.Top + 33 + int32(visibleRow)*detailRowHeightV8
		if y > fresh.Rect.Bottom-12 {
			y = fresh.Rect.Bottom - 12
		}
		clickScreenPoint(x, y)
		if !interruptibleSleep(180 * time.Millisecond) {
			return false
		}
		pressVK(VK_RETURN)
		edit := waitGridEditorV4(root, fresh, 950*time.Millisecond)
		if edit == 0 {
			focus := focusedControlOfForeground(root)
			logf("WARN", "detail V18 editor not ready row=%d visible_row=%d col=%d attempt=%d point=%d,%d grid_width=%d focus=0x%x/%s", row+1, visibleRow+1, col, attempt, x, y, fresh.Rect.Right-fresh.Rect.Left, focus, className(focus))
			continue
		}
		logf("INFO", "detail V18 editor ready row=%d visible_row=%d col=%d edit=0x%x/%s attempt=%d point=%d,%d", row+1, visibleRow+1, col, edit, className(edit), attempt, x, y)
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
		logf("INFO", "detail V18 committed by Enter row=%d col=%d", row+1, col)
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
	logf("INFO", "unit lookup V18 Enter dispatched directly target=0x%x class=%q focus_before=0x%x/%s", target, className(target), focus, className(focus))
	return true
}

// waitLookupClosedV18 is used only for diagnostics by future callers; it keeps
// the confirmation path bounded and never clicks a fallback button.
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
