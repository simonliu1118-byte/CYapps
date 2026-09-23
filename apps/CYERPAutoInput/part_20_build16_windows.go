//go:build windows

package main

import "strings"

// setupBuild16LabelsV16 is kept as the compatibility hook called by createControls.
// Build 19 keeps Build 18 behavior and adds diagnostic logging for exactly what
// Win32 can read from the F2 unit lookup while the selected row moves.
func setupBuild16LabelsV16(parent uintptr) {
	setWindowText(parent, "CYERPAutoInput V0.0.10 Build 19 — SMART ERP 自動輸入工具")
	for _, c := range enumControls(parent) {
		if strings.Contains(c.Text, "Build 15") || strings.Contains(c.Text, "Build 16") || strings.Contains(c.Text, "Build 17") || strings.Contains(c.Text, "Build 18") {
			text := strings.ReplaceAll(c.Text, "Build 15", "Build 19")
			text = strings.ReplaceAll(text, "Build 16", "Build 19")
			text = strings.ReplaceAll(text, "Build 17", "Build 19")
			text = strings.ReplaceAll(text, "Build 18", "Build 19")
			setWindowText(c.Hwnd, text)
		}
	}
}
