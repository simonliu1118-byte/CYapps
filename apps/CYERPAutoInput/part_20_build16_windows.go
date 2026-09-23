//go:build windows

package main

import "strings"

// setupBuild16LabelsV16 runs after the Build 15 UI compatibility layer.
// Build 16 is a focused ERP F2 confirmation fix, so the UI structure stays the
// same while the visible build label is advanced consistently.
func setupBuild16LabelsV16(parent uintptr) {
	setWindowText(parent, "CYERPAutoInput V0.0.10 Build 16 — SMART ERP 自動輸入工具")
	for _, c := range enumControls(parent) {
		if strings.Contains(c.Text, "Build 15") {
			setWindowText(c.Hwnd, strings.ReplaceAll(c.Text, "Build 15", "Build 16"))
		}
	}
}
