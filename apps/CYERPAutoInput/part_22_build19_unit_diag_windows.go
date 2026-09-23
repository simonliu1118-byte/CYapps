//go:build windows

package main

import (
	"fmt"
	"sort"
	"strings"
	"syscall"
	"time"
)

// Build 19 is intentionally diagnostic-only for the F2 unit lookup. It does not
// change the existing selection/confirmation behavior. While automation is
// running and the F2 lookup is visible, record exactly what Win32 can read from
// the lookup window so the next real-ERP test can distinguish "no cell text is
// exposed" from "text is exposed but does not match the requested unit".
func init() {
	go watchUnitLookupReadbackV19()
}

func watchUnitLookupReadbackV19() {
	var active uintptr
	probe := 0
	for {
		if !automationRunning.Load() {
			active = 0
			probe = 0
			time.Sleep(80 * time.Millisecond)
			continue
		}

		lookup := findVisibleUnitLookupV19()
		if lookup == 0 {
			active = 0
			probe = 0
			time.Sleep(80 * time.Millisecond)
			continue
		}
		if lookup != active {
			active = lookup
			probe = 0
			logf("INFO", "unit lookup V19 diagnostic begin hwnd=0x%x title=%q", lookup, strings.TrimSpace(getWindowText(lookup)))
		}

		probe++
		logUnitLookupReadbackV19(lookup, probe)
		time.Sleep(140 * time.Millisecond)
	}
}

func findVisibleUnitLookupV19() uintptr {
	var found uintptr
	cb := syscall.NewCallback(func(hwnd, lParam uintptr) uintptr {
		vis, _, _ := pIsWindowVisible.Call(hwnd)
		if vis == 0 {
			return 1
		}
		if strings.Contains(strings.TrimSpace(getWindowText(hwnd)), "F2開窗查詢") {
			found = hwnd
			return 0
		}
		return 1
	})
	pEnumWindows.Call(cb, 0)
	return found
}

func logUnitLookupReadbackV19(lookup uintptr, probe int) {
	focus := focusedControlOfForeground(lookup)
	focusClass := className(focus)
	focusText := ""
	if focus != 0 {
		focusText = strings.TrimSpace(getWindowText(focus))
	}

	controls := enumControls(lookup)
	readable := make([]string, 0, len(controls))
	for _, c := range controls {
		if !c.Visible {
			continue
		}
		text := strings.TrimSpace(c.Text)
		if text == "" {
			continue
		}
		readable = append(readable, fmt.Sprintf("0x%x/%s=%q", c.Hwnd, c.Class, text))
	}
	sort.Strings(readable)

	gridText := ""
	gridHwnd := uintptr(0)
	if grid := lookupGridV14(lookup); grid != nil {
		gridHwnd = grid.Hwnd
		gridText = strings.TrimSpace(getWindowText(grid.Hwnd))
	}

	logf("INFO", "unit lookup V19 readback probe=%d focus=0x%x/%s focus_text=%q grid=0x%x grid_text=%q readable=[%s]", probe, focus, focusClass, focusText, gridHwnd, gridText, strings.Join(readable, " | "))
}
