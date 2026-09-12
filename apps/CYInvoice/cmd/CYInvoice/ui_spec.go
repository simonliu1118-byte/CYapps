package main

import "time"

const (
	mainWindowInitialWidth  = 1180
	mainWindowInitialHeight = 850
	mainWindowMinimumWidth  = 1180
	mainWindowMinimumHeight = 850

	// Keep the settings button inside the tab-header row rather than covering
	// the tab control's right frame. Its right edge aligns with the page content.
	settingsButtonX      = 1020
	settingsButtonY      = 76
	settingsButtonWidth  = 108
	settingsButtonHeight = 29
)

func defaultRecordDateRange(now time.Time) (string, string) {
	firstDay := time.Date(now.Year(), now.Month(), 1, 0, 0, 0, 0, now.Location())
	return firstDay.Format("2006/01/02"), now.Format("2006/01/02")
}
