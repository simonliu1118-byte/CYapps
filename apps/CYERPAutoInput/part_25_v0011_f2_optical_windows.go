//go:build windows

package main

import "strings"

// focusedLookupGridTextDirectV011 is the runtime bridge used by getWindowText.
// It deliberately scopes OCR substitution to a focused TcxGridSite inside the
// currently visible F2 lookup window. Normal ERP/control text never goes through
// this path.
func focusedLookupGridTextDirectV011(hwnd uintptr) (string, bool) {
	if hwnd == 0 || !strings.EqualFold(className(hwnd), "TcxGridSite") {
		return "", false
	}
	lookup := findVisibleUnitLookupV19()
	if lookup == 0 || !hwndInsideV4(hwnd, lookup) {
		return "", false
	}
	if focusedControlOfForeground(lookup) != hwnd {
		return "", false
	}
	cache, ok := ensureLookupOpticalCacheV011(hwnd)
	if !ok || len(cache.Rows) == 0 {
		return "", false
	}
	img, err := captureScreenRectV011(rectOf(hwnd))
	if err != nil {
		logf("WARN", "unit optical V0.0.11 selection capture failed: %v", err)
		return "", false
	}
	idx := selectedUnitRowIndexV011(img, cache.Rows)
	if idx < 0 || idx >= len(cache.Rows) {
		return "", false
	}
	text := strings.TrimSpace(cache.Rows[idx].Text)
	if text == "" {
		return "", false
	}
	logf("INFO", "unit optical V0.0.11 selected row=%d text_len=%d", idx+1, len([]rune(text)))
	return text, true
}
