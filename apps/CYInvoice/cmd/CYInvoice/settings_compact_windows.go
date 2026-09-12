//go:build windows

package main

import (
	"strings"

	"cyinvoice/internal/appdata"
)

func buildCompactSettingsPage(parent uintptr) {
	settingsControls = nil
	settingsLoginControls = nil

	addGroup(parent, "設定權限驗證", 18, 16, 506, 142, &settingsLoginControls)
	addStatic(parent, "管理密碼", 56, 57, 78, 22, &settingsLoginControls)
	addEdit(parent, "", 138, 53, 278, 24, idSettingsLoginPassword, esPassword|esAutoHScroll, &settingsLoginControls)
	addButtonStyle(parent, "進入設定", 174, 105, 105, 30, idSettingsUnlock, bsDefaultPushButton, &settingsLoginControls)
	addButton(parent, "取消", 291, 105, 88, 30, idSettingsCancel, &settingsLoginControls)

	addGroup(parent, "使用環境", 16, 12, 510, 148, &settingsControls)
	addButtonStyle(parent, "光貿測試環境", 34, 37, 124, 24, idEnvironmentTest, bsAutoRadioButton|wsGroup, &settingsControls)
	addStatic(parent, "測試帳號由光貿固定提供，不可修改。", 166, 39, 320, 21, &settingsControls)
	addButtonStyle(parent, "正式公司", 34, 76, 86, 24, idEnvironmentProd, bsAutoRadioButton, &settingsControls)
	addStatic(parent, "統編", 126, 78, 56, 21, &settingsControls)
	prodBANEdit := addEdit(parent, "", 188, 73, 100, 24, idProdBAN, esAutoHScroll, &settingsControls)
	addStatic(parent, "App Key", 126, 111, 56, 21, &settingsControls)
	prodKeyEdit := addEdit(parent, "", 188, 106, 318, 24, idProdKey, esPassword|esAutoHScroll, &settingsControls)
	setStaticStyle(prodBANEdit, rgb(112, 112, 112), rgb(238, 238, 238), disabledEditBrush, false)
	setStaticStyle(prodKeyEdit, rgb(112, 112, 112), rgb(238, 238, 238), disabledEditBrush, false)

	addGroup(parent, "平台檔案密碼", 16, 169, 510, 64, &settingsControls)
	addStatic(parent, "MO店+ Excel 保護密碼", 34, 194, 164, 21, &settingsControls)
	addEdit(parent, "", 202, 189, 304, 24, idMOPassword, esPassword|esAutoHScroll, &settingsControls)

	addButton(parent, "設定管理密碼", 116, 247, 116, 30, idSettingsChangePassword, &settingsControls)
	addButtonStyle(parent, "儲存設定", 242, 247, 105, 30, idSettingsSave, bsDefaultPushButton, &settingsControls)
	addButton(parent, "取消", 357, 247, 86, 30, idSettingsBack, &settingsControls)
}

func loadCompactSettingsIntoControls() {
	if appRepository == nil { return }
	settings, err := appRepository.Settings.LoadOrCreate()
	if err != nil { showError("讀取設定失敗：" + err.Error()); return }
	setSettingsEnvironment(settings.Environment == appdata.EnvironmentProduction)
	setControlText(handles[idProdBAN], settings.ProdInvoice)
	setControlText(handles[idProdKey], "")
	password, err := appRepository.Settings.MOPassword(settings)
	if err != nil {
		showError("解密 MO店+ Excel 保護密碼失敗：" + err.Error())
		setControlText(handles[idMOPassword], "")
		return
	}
	setControlText(handles[idMOPassword], password)
}

func saveCompactSettings() {
	if !settingsUnlocked {
		showError("設定視窗已鎖定，請重新輸入管理密碼")
		return
	}
	settings, err := appRepository.Settings.LoadOrCreate()
	if err != nil { showError(err.Error()); return }
	if isChecked(handles[idEnvironmentProd]) {
		settings.Environment = appdata.EnvironmentProduction
	} else {
		settings.Environment = appdata.EnvironmentTest
	}
	settings.ProdInvoice = strings.TrimSpace(controlText(handles[idProdBAN]))
	if key := controlText(handles[idProdKey]); key != "" {
		if err = appRepository.Settings.SetProdAppKey(&settings, key); err != nil { showError(err.Error()); return }
	}
	password := controlText(handles[idMOPassword])
	if strings.TrimSpace(password) == "" {
		showError("MO店+ Excel 保護密碼不可空白")
		return
	}
	if err = appRepository.Settings.SetMOPassword(&settings, password); err != nil { showError(err.Error()); return }
	if err = appRepository.Settings.Save(settings); err != nil { showError(err.Error()); return }
	if appLogger != nil { appLogger.Infof("settings saved environment=%s", settings.Environment) }
	refreshAPIState()
	showInfo("設定已安全儲存。\nApp Key 與 MO店+ 密碼使用 Windows DPAPI 加密，不會以明文寫入。")
}
