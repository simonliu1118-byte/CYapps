//go:build windows

package main

import "strings"

// setupBuild16LabelsV16 is kept as the compatibility hook called by createControls.
// V0.0.11 advances the detail automation to screenshot/optical positioning and
// adds Enter/Tab field navigation while retaining the validated upper-field UI.
func setupBuild16LabelsV16(parent uintptr) {
	setWindowText(parent, "CYERPAutoInput V0.0.11 — SMART ERP 自動輸入工具")
	for _, c := range enumControls(parent) {
		if strings.Contains(c.Text, "V0.0.10") || strings.Contains(c.Text, "Build 15") || strings.Contains(c.Text, "Build 16") || strings.Contains(c.Text, "Build 17") || strings.Contains(c.Text, "Build 18") || strings.Contains(c.Text, "Build 19") {
			text := c.Text
			for _, old := range []string{"V0.0.10 Build 15", "V0.0.10 Build 16", "V0.0.10 Build 17", "V0.0.10 Build 18", "V0.0.10 Build 19", "V0.0.10"} {
				text = strings.ReplaceAll(text, old, "V0.0.11")
			}
			setWindowText(c.Hwnd, text)
		}
	}
	setupFieldNavigationV011()
}
