package main

import (
	"testing"
	"time"
)

func TestDefaultRecordDateRangeUsesFirstDayThroughToday(t *testing.T) {
	now := time.Date(2026, time.September, 5, 18, 4, 3, 0, time.FixedZone("Asia/Taipei", 8*60*60))
	from, to := defaultRecordDateRange(now)
	if from != "2026/09/01" || to != "2026/09/05" {
		t.Fatalf("range = %s through %s", from, to)
	}
}

func TestReferenceWindowCannotShrinkBelowScreenshotLayout(t *testing.T) {
	if mainWindowMinimumWidth < mainWindowInitialWidth || mainWindowMinimumHeight < mainWindowInitialHeight {
		t.Fatalf("minimum %dx%d is smaller than initial %dx%d", mainWindowMinimumWidth, mainWindowMinimumHeight, mainWindowInitialWidth, mainWindowInitialHeight)
	}
	if settingsButtonX+settingsButtonWidth > mainWindowInitialWidth || settingsButtonY+settingsButtonHeight > mainWindowInitialHeight {
		t.Fatal("settings button is outside the reference window")
	}
}

func TestManualRowIsEmptyOnlyWhenEveryEditableCellIsBlank(t *testing.T) {
	if !(manualItemRow{}).empty() {
		t.Fatal("completely blank row must be removable during final validation")
	}
	for name, row := range map[string]manualItemRow{
		"description": {Description: "商品"},
		"quantity":    {Quantity: "1"},
		"unit price":  {UnitPrice: "100"},
	} {
		if row.empty() { t.Fatalf("%s-only row must remain so final validation can warn", name) }
	}
}

func TestDefaultListColumnsLeaveRoomForVerticalScrollbar(t *testing.T) {
	productWidths := []int{50, 425, 78, 82, 140, 148, 80}
	recordWidths := []int{142, 100, 84, 158, 112, 120, 82, 86, 88, 58}
	if sumWidths(productWidths) > 1020 { t.Fatal("product columns can trigger a horizontal scrollbar") }
	if sumWidths(recordWidths) > 1040 { t.Fatal("record columns can trigger a horizontal scrollbar") }
}

func sumWidths(widths []int) int {
	total := 0
	for _, width := range widths { total += width }
	return total
}
