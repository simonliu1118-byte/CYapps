//go:build windows

package main

import (
	"fmt"
	"sort"
	"strings"
	"time"
)

func detailVisibleRowIndexV11(grid ControlInfo, row int) int {
	if row <= 0 {
		return 0
	}
	h := grid.Rect.Bottom - grid.Rect.Top
	// Header/editor offset is about 33 px and each confirmed row is about 24 px.
	// When COPI08 scrolls the grid after Down, the newly-created active row stays
	// visible near the bottom, so clamp the click row to the last visible slot.
	maxVisible := int((h - 45) / detailRowHeightV8)
	if maxVisible < 1 {
		maxVisible = 1
	}
	if row > maxVisible {
		return maxVisible
	}
	return row
}

func setDetailCellAtRowV11(root uintptr, grid ControlInfo, row int, col int, value string) bool {
	if row < 0 || isStopRequested() {
		return false
	}
	ratio, ok := detailColumnRatio(col)
	if !ok {
		return false
	}
	w := float64(grid.Rect.Right - grid.Rect.Left)
	x := grid.Rect.Left + int32(w*ratio)
	visibleRow := detailVisibleRowIndexV11(grid, row)
	y := grid.Rect.Top + 33 + int32(visibleRow)*detailRowHeightV8
	if y > grid.Rect.Bottom-12 {
		y = grid.Rect.Bottom - 12
	}

	for attempt := 1; attempt <= 2; attempt++ {
		if isStopRequested() || !prepareERPWindow(root) {
			return false
		}
		clickScreenPoint(x, y)
		if !interruptibleSleep(180 * time.Millisecond) {
			return false
		}
		pressVK(VK_RETURN)
		edit := waitGridEditorV4(root, grid, 950*time.Millisecond)
		if edit == 0 {
			focus := focusedControlOfForeground(root)
			logf("WARN", "detail V11 editor not ready row=%d visible_row=%d col=%d attempt=%d point=%d,%d focus=0x%x/%s", row+1, visibleRow+1, col, attempt, x, y, focus, className(focus))
			continue
		}
		logf("INFO", "detail V11 editor ready row=%d visible_row=%d col=%d edit=0x%x/%s attempt=%d before_len=%d", row+1, visibleRow+1, col, edit, className(edit), attempt, len([]rune(getWindowText(edit))))
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
		logf("INFO", "detail V11 committed by Enter row=%d col=%d", row+1, col)
		return true
	}
	return false
}

func selectedDetailRowsV11() map[int][]*Field {
	rows := map[int][]*Field{}
	for _, f := range fields {
		if f == nil || f.Group != "明細" || !checked(f.ApplyHwnd) {
			continue
		}
		rows[f.Row] = append(rows[f.Row], f)
	}
	for row := range rows {
		sort.Slice(rows[row], func(i, j int) bool { return rows[row][i].Col < rows[row][j].Col })
	}
	return rows
}

func findDetailFieldByColV11(fs []*Field, col int) *Field {
	for _, f := range fs {
		if f.Col == col {
			return f
		}
	}
	return nil
}

func fillDetailSelectedV11(root uintptr) (ok, fail int) {
	if detailListViewV14 != 0 {
		return fillDetailSelectedV14(root)
	}

	rows := selectedDetailRowsV11()
	if len(rows) == 0 {
		return 0, 0
	}

	maxRow := -1
	for row := range rows {
		if row > maxRow {
			maxRow = row
		}
	}
	for row := 0; row <= maxRow; row++ {
		fs, exists := rows[row]
		if !exists {
			logError("明細", fmt.Sprintf("第%d列", row+1), "DETAIL_ROW_GAP", "明細列必須由第1列開始連續勾選；中間不可跳列")
			return 0, 1
		}
		item := findDetailFieldByColV11(fs, 0)
		if item == nil || strings.TrimSpace(getWindowText(item.ValueHwnd)) == "" {
			logError("明細", fmt.Sprintf("第%d列品號", row+1), "DETAIL_ITEM_REQUIRED", "已勾選的明細列必須填入品號")
			return 0, 1
		}
	}

	grid := findDetailGridSite(root)
	if grid == nil {
		logError("明細", "TcxGrid", "GRID_NOT_FOUND", "找不到可見的標準明細 TcxGridSite")
		return 0, 1
	}
	logf("INFO", "detail V11 grid hwnd=0x%x rect=%d,%d,%d,%d selected_rows=%d", grid.Hwnd, grid.Rect.Left, grid.Rect.Top, grid.Rect.Right, grid.Rect.Bottom, maxRow+1)
	if !activateDetailFirstRowV4(root, *grid) {
		logError("明細", "第一列", "ROW_ACTIVATION_FAILED", "表頭完成後無法啟用表身第一列")
		return 0, 1
	}

	for row := 0; row <= maxRow; row++ {
		if isStopRequested() {
			return ok, fail
		}
		if row > 0 {
			if !openNextDetailRowV8(root, *grid) {
				fail++
				logError("明細", fmt.Sprintf("第%d列", row+1), "NEXT_ROW_OPEN_FAILED", "上一列完成後按 Down 無法進入下一列")
				return ok, fail
			}
		}

		fs := rows[row]
		item := findDetailFieldByColV11(fs, 0)
		itemValue := strings.TrimSpace(getWindowText(item.ValueHwnd))
		itemOK := false
		if row == 0 {
			itemOK = setDetailCell(root, *grid, 0, itemValue)
		} else {
			itemOK = setDetailCellAtRowV11(root, *grid, row, 0, itemValue)
		}
		if !itemOK {
			fail++
			logError("明細", item.Label, "GRID_CELL_SET_FAILED", fmt.Sprintf("row=%d col=0 grid=0x%x", row+1, grid.Hwnd))
			return ok, fail
		}
		ok++
		logf("INFO", "filled 明細/%s col=0", item.Label)

		for _, f := range fs {
			if isStopRequested() {
				return ok, fail
			}
			if f.Col == 0 {
				continue
			}
			value := strings.TrimSpace(getWindowText(f.ValueHwnd))
			if value == "" {
				continue
			}
			if f.Kind == "deferred_click" {
				logf("INFO", "detail V11 deferred click field row=%d col=%d label=%q value_len=%d", row+1, f.Col, f.Label, len([]rune(value)))
				continue
			}
			if setDetailCellAtRowV11(root, *grid, row, f.Col, value) {
				ok++
				logf("INFO", "filled 明細/%s col=%d", f.Label, f.Col)
			} else {
				fail++
				logError("明細", f.Label, "GRID_CELL_SET_FAILED", fmt.Sprintf("row=%d col=%d grid=0x%x", row+1, f.Col, grid.Hwnd))
				return ok, fail
			}
		}
	}
	return ok, fail
}
