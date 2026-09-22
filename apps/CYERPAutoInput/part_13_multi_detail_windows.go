//go:build windows

package main

import "time"

const detailRowHeightV8 int32 = 24

// openNextDetailRowV8 follows the confirmed COPI08 behavior after the first
// detail row has been committed: Down opens the next row. COPI08 may place the
// active cell on an arbitrary column, so this function only creates/activates
// the row; the next write always re-clicks the exact target cell.
func openNextDetailRowV8(root uintptr, grid ControlInfo) bool {
	if isStopRequested() || !prepareERPWindow(root) {
		return false
	}
	pressVK(VK_DOWN)
	if !interruptibleSleep(360 * time.Millisecond) {
		return false
	}
	focus := focusedControlOfForeground(root)
	logf("INFO", "detail V8 next-row opened by Down grid=0x%x focus=0x%x/%s", grid.Hwnd, focus, className(focus))
	return true
}

// setDetailCellAtRowV8 is used for rows after the first row. After Down creates
// the new row, COPI08 does not guarantee which column is active, therefore we
// explicitly click the requested cell, press Enter to enter its editor, wait
// for editor-ready, type sequentially, and press Enter again to commit.
func setDetailCellAtRowV8(root uintptr, grid ControlInfo, row int, col int, value string) bool {
	if row < 1 || isStopRequested() {
		return false
	}
	ratio, ok := detailColumnRatio(col)
	if !ok {
		return false
	}
	w := float64(grid.Rect.Right - grid.Rect.Left)
	x := grid.Rect.Left + int32(w*ratio)
	y := grid.Rect.Top + 33 + int32(row)*detailRowHeightV8

	for attempt := 1; attempt <= 2; attempt++ {
		if isStopRequested() || !prepareERPWindow(root) {
			return false
		}
		// Always re-click the exact cell. This is especially important for the
		// second-row item-code cell because Down can land on another column.
		clickScreenPoint(x, y)
		if !interruptibleSleep(180 * time.Millisecond) {
			return false
		}
		pressVK(VK_RETURN)
		edit := waitGridEditorV4(root, grid, 950*time.Millisecond)
		if edit == 0 {
			focus := focusedControlOfForeground(root)
			logf("WARN", "detail V8 editor not ready row=%d col=%d attempt=%d point=%d,%d focus=0x%x/%s", row+1, col, attempt, x, y, focus, className(focus))
			continue
		}
		logf("INFO", "detail V8 editor ready row=%d col=%d edit=0x%x/%s attempt=%d before_len=%d", row+1, col, edit, className(edit), attempt, len([]rune(getWindowText(edit))))
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
		logf("INFO", "detail V8 committed by Enter row=%d col=%d", row+1, col)
		return true
	}
	return false
}
