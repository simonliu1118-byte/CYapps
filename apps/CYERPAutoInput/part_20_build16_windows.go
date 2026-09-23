//go:build windows

package main

import "strings"

// setupBuild16LabelsV16 is kept as the compatibility hook called by createControls.
// Build 17 keeps the Build 16 F2 confirmation work and advances the visible labels
// for the detail-grid detection fix.
func setupBuild16LabelsV16(parent uintptr) {
	setWindowText(parent, "CYERPAutoInput V0.0.10 Build 17 — SMART ERP 自動輸入工具")
	for _, c := range enumControls(parent) {
		if strings.Contains(c.Text, "Build 15") || strings.Contains(c.Text, "Build 16") {
			text := strings.ReplaceAll(c.Text, "Build 15", "Build 17")
			text = strings.ReplaceAll(text, "Build 16", "Build 17")
			setWindowText(c.Hwnd, text)
		}
	}
}
