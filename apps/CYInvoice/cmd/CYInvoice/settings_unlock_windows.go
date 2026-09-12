//go:build windows

package main

import (
	"fmt"
	"strings"
	"time"

	"cyinvoice/internal/appdata"
)

// unlockSettingsWindow is the only settings-window authentication transition.
// It changes state explicitly and does not rely on paint/layout callbacks.
func unlockSettingsWindow() bool {
	if remaining := time.Until(settingsLoginBlockedUntil); remaining > 0 {
		showError(fmt.Sprintf("管理密碼連續輸入錯誤，請等待 %d 秒後再試", int(remaining.Seconds())+1))
		return false
	}
	password := controlText(handles[idSettingsLoginPassword])
	if strings.TrimSpace(password) == "" {
		showError("請輸入管理密碼")
		return false
	}
	settings, err := appRepository.Settings.LoadOrCreate()
	if err != nil {
		showError("讀取設定失敗：" + err.Error())
		return false
	}
	if settings.AdminPasswordSet {
		if !appdata.CheckAdminPassword(settings, password) {
			settingsLoginFailures++
			if settingsLoginFailures >= 5 {
				settingsLoginBlockedUntil = time.Now().Add(30 * time.Second)
				settingsLoginFailures = 0
				showError("管理密碼連續輸入錯誤，已暫停嘗試 30 秒")
				return false
			}
			showError("管理密碼錯誤")
			return false
		}
	} else {
		if err = appRepository.Settings.SetAdminPassword(&settings, password); err != nil {
			showError("建立管理密碼失敗：" + err.Error())
			return false
		}
		if err = appRepository.Settings.Save(settings); err != nil {
			showError("儲存管理密碼失敗：" + err.Error())
			return false
		}
	}
	settingsUnlocked = true
	settingsLoginFailures = 0
	settingsLoginBlockedUntil = time.Time{}
	setControlText(handles[idSettingsLoginPassword], "")
	showSettingsPage(settingsControls)
	loadCompactSettingsIntoControls()
	return true
}
