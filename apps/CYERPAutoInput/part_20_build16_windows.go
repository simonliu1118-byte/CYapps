//go:build windows

package main

import "strings"

// setupBuild16LabelsV16 is kept as the compatibility hook called by createControls.
// Build 18 keeps the prior UI structure while advancing the visible labels for
// the non-maximized detail targeting and F2 Enter fixes.
func setupBuild16LabelsV16(parent uintptr) {
	setWindowText(parent, "CYERPAutoInput V0.0.10 Build 18 — SMART ERP 自動輸入工具")
	for _, c := range enumControls(parent) {
		if strings.Contains(c.Text, "Build 15") || strings.Contains(c.Text, "Build 16") || strings.Contains(c.Text, "Build 17") {
			text := strings.ReplaceAll(c.Text, "Build 15", "Build 18")
			text = strings.ReplaceAll(text, "Build 16", "Build 18")
			text = strings.ReplaceAll(text, "Build 17", "Build 18")
			setWindowText(c.Hwnd, text)
		}
	}
}
