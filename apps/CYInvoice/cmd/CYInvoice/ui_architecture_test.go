package main

import (
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
)

// These tests intentionally protect CYInvoice-specific UI architecture rules.
// They are NOT a CYapps-wide policy. CYInvoice handles electronic invoices, so
// predictable native Win32 behavior is preferred over layered UI patching.

func cyInvoiceSourceDir(t *testing.T) string {
	t.Helper()
	_, file, _, ok := runtime.Caller(0)
	if !ok {
		t.Fatal("cannot resolve test source path")
	}
	return filepath.Dir(file)
}

func readCYInvoiceSource(t *testing.T, name string) string {
	t.Helper()
	data, err := os.ReadFile(filepath.Join(cyInvoiceSourceDir(t), name))
	if err != nil {
		t.Fatalf("read %s: %v", name, err)
	}
	return string(data)
}

func TestCYInvoiceHasNoRefinePatchLayer(t *testing.T) {
	dir := cyInvoiceSourceDir(t)
	for _, name := range []string{"ui_refine_windows.go", "mo_confirm_refine_windows.go"} {
		if _, err := os.Stat(filepath.Join(dir, name)); err == nil {
			t.Fatalf("%s must not return; merge final behavior into the primary build/layout path", name)
		} else if !os.IsNotExist(err) {
			t.Fatalf("stat %s: %v", name, err)
		}
	}
}

func TestCYInvoiceDoesNotBootstrapNativeButtonsAsOwnerDraw(t *testing.T) {
	files, err := filepath.Glob(filepath.Join(cyInvoiceSourceDir(t), "*_windows.go"))
	if err != nil {
		t.Fatal(err)
	}
	// These are Go identifiers / legacy callback names, not prose terms. Keep
	// the guard focused on executable architecture so documentation comments can
	// still explain what CYInvoice intentionally avoids.
	banned := []string{
		"bsOwnerDraw",
		"bmSetStyle",
		"normalizeNativeMainControls",
		"refineMainUI",
		"refinedMOConfirmWindowProc",
	}
	for _, path := range files {
		data, err := os.ReadFile(path)
		if err != nil {
			t.Fatalf("read %s: %v", filepath.Base(path), err)
		}
		text := string(data)
		for _, token := range banned {
			if strings.Contains(text, token) {
				t.Fatalf("%s contains banned CYInvoice UI patch token %q", filepath.Base(path), token)
			}
		}
	}
}

func functionBody(t *testing.T, source, name string) string {
	t.Helper()
	startToken := "func " + name + "("
	start := strings.Index(source, startToken)
	if start < 0 {
		t.Fatalf("function %s not found", name)
	}
	rest := source[start+len(startToken):]
	next := strings.Index(rest, "\nfunc ")
	if next < 0 {
		return rest
	}
	return rest[:next]
}

func TestCYInvoicePaintHandlersDoNotMutateTaxState(t *testing.T) {
	source := readCYInvoiceSource(t, "ui_native_windows.go")
	body := functionBody(t, source, "handleButtonColor")
	for _, banned := range []string{"EnableWindow", "setChecked", "updateTaxChoice", "setBuyerMode", "syncTaxInputAvailability"} {
		if strings.Contains(body, banned) {
			t.Fatalf("handleButtonColor must be paint-only; found %q", banned)
		}
	}
}

func TestCYInvoiceGeneralConsumerTaxModeIsExplicit(t *testing.T) {
	source := readCYInvoiceSource(t, "ui_windows.go")
	body := functionBody(t, source, "setBuyerMode")
	for _, required := range []string{
		"procEnableWindow.Call(handles[idTaxInclusive], 1)",
		"procEnableWindow.Call(handles[idTaxExclusive], boolValue(company))",
		"pricesAreInclusive = true",
		"updateTaxChoice(true)",
	} {
		if !strings.Contains(body, required) {
			t.Fatalf("setBuyerMode must explicitly enforce general-consumer inclusive-tax state; missing %q", required)
		}
	}
}

func TestCYInvoiceTabIsCreatedAtFinalHeight(t *testing.T) {
	source := readCYInvoiceSource(t, "ui_native_windows.go")
	body := functionBody(t, source, "addTab")
	if strings.Contains(body, "tabHeight :=") {
		t.Fatal("addTab must use its final requested height directly; temporary tab-strip sizing is forbidden")
	}
	if !strings.Contains(body, "width, height") {
		t.Fatal("addTab must create SysTabControl32 using the requested final width/height")
	}
}


func TestCYInvoiceTabUsesBlueUnderlinePaintOnly(t *testing.T) {
	source := readCYInvoiceSource(t, "tab_windows.go")
	body := functionBody(t, source, "handleTabOwnerDraw")
	for _, required := range []string{"tabAccentHeight", "procFillRect", "boldFont"} {
		if !strings.Contains(body, required) {
			t.Fatalf("tab header must keep the approved blue-underline paint path; missing %q", required)
		}
	}
	for _, banned := range []string{"procPolygon", "procCreatePen", "setBuyerMode", "refreshRecords", "showPage"} {
		if strings.Contains(body, banned) {
			t.Fatalf("tab owner draw must stay rectangular and paint-only; found %q", banned)
		}
	}
}

func TestCYInvoiceListViewsKeepNativeStableChrome(t *testing.T) {
	source := readCYInvoiceSource(t, "ui_native_windows.go")
	createBody := functionBody(t, source, "addInvoiceListView")
	for _, required := range []string{"wsVScroll", "lvsNoSortHeader"} {
		if !strings.Contains(createBody, required) {
			t.Fatalf("invoice ListViews must be created with their final native chrome; missing %q", required)
		}
	}
	configureBody := functionBody(t, source, "configureInvoiceListView")
	for _, required := range []string{"hdsNoSizing", "hdsButtons", "hdsDragDrop"} {
		if !strings.Contains(configureBody, required) {
			t.Fatalf("invoice ListView headers must reject click/resize/reorder behavior; missing %q", required)
		}
	}
	if strings.Contains(source, "configureClassicListView") {
		t.Fatal("a ListView-wide classic-theme override must not return")
	}
	for _, required := range []string{
		"createInvoiceListScrollPlaceholder(handle)",
		"createInvoiceListScrollPlaceholder",
		"\"SCROLLBAR\"",
		"invoiceListScrollPlaceholders",
		"layoutInvoiceListChrome",
		"procGetSystemMetrics.Call(smCxVScroll)",
	} {
		if !strings.Contains(source, required) {
			t.Fatalf("invoice ListViews must keep a stable native scrollbar gutter; missing %q", required)
		}
	}
	for _, banned := range []string{"sifDisableNoScroll", "procSetScrollInfo", "listViewHeaderWidth"} {
		if strings.Contains(source, banned) {
			t.Fatalf("broken rc.8 scrollbar-width path must not return; found %q", banned)
		}
	}
	if !strings.Contains(configureBody, "procSetWindowTheme.Call(header") {
		t.Fatal("only SysHeader32 must use native classic rendering so its lines join the ListView grid")
	}
}

func TestCYInvoiceColumnWidthsUsePermanentScrollbarSlot(t *testing.T) {
	source := readCYInvoiceSource(t, "ui_native_windows.go")
	if !strings.Contains(functionBody(t, source, "layoutProductColumnsToClient"), "layoutInvoiceListChrome(invoiceItemsList)") {
		t.Fatal("product columns must stop at the permanent scrollbar slot")
	}
	recordsSource := readCYInvoiceSource(t, "records_layout_windows.go")
	if !strings.Contains(functionBody(t, recordsSource, "layoutRecordColumnsToClient"), "layoutInvoiceListChrome(recordsList)") {
		t.Fatal("record columns must stop at the permanent scrollbar slot")
	}
}


func TestCYInvoiceTabSizingAndSettingsButtonFollowNativeHeader(t *testing.T) {
	native := readCYInvoiceSource(t, "ui_native_windows.go")
	tabBody := functionBody(t, native, "addTab")
	for _, required := range []string{"boldFont", "tcmSetPadding"} {
		if !strings.Contains(tabBody, required) {
			t.Fatalf("tab items must be sized for the selected bold label; missing %q", required)
		}
	}
	layout := readCYInvoiceSource(t, "records_layout_windows.go")
	buttonBody := functionBody(t, layout, "layoutSettingsButtonToTab")
	for _, required := range []string{"tcmGetItemRect", "buttonWidth := settingsButtonWidth", "buttonHeight := settingsButtonHeight"} {
		if !strings.Contains(buttonBody, required) {
			t.Fatalf("settings button must follow the real native tab header; missing %q", required)
		}
	}
	for _, banned := range []string{"settingsButtonWidth) * sx", "settingsButtonHeight) * sy"} {
		if strings.Contains(buttonBody, banned) {
			t.Fatalf("settings button size must not stretch with main-window scale; found %q", banned)
		}
	}
}

func TestCYInvoiceSettingsAppKeyUsesAlignedFullRow(t *testing.T) {
	source := readCYInvoiceSource(t, "settings_compact_windows.go")
	body := functionBody(t, source, "buildCompactSettingsPage")
	for _, required := range []string{
		"addStatic(parent, \"統編\", 126, 78, 56",
		"addStatic(parent, \"App Key\", 126, 111, 56",
		"addEdit(parent, \"\", 188, 73, 100, 24, idProdBAN",
		"addEdit(parent, \"\", 188, 106, 318, 24, idProdKey",
		"MO店+ Excel 保護密碼\", 34, 194, 164",
	} {
		if !strings.Contains(body, required) {
			t.Fatalf("settings labels and full-row App Key layout regressed; missing %q", required)
		}
	}
}

func TestCYInvoiceRecordListKeepsFixedIdentifiersAndBlankZebraRows(t *testing.T) {
	layout := readCYInvoiceSource(t, "records_layout_windows.go")
	for _, required := range []string{
		"\"MO店+\"", "\"好賣+\"", "\"iOPEN\"", "\"115102696774269\"",
		"if sourceWidth < 84", "if orderWidth < 158", "if banWidth < 112",
		"func syncRecordPlaceholderRows", "lvmGetCountPerPage", "lvmDeleteItem",
	} {
		if !strings.Contains(layout, required) {
			t.Fatalf("record width/zebra invariant is missing %q", required)
		}
	}
}

func TestCYInvoiceBuyerLookupNoMatchUsesNormalReminder(t *testing.T) {
	source := readCYInvoiceSource(t, "ui_windows.go")
	body := functionBody(t, source, "finishBuyerNameLookup")
	for _, required := range []string{
		"refreshAPIState()",
		"查無此統一編號，請再次確認或自行輸入買方名稱。",
	} {
		if !strings.Contains(body, required) {
			t.Fatalf("buyer lookup no-match handling is missing %q", required)
		}
	}
	noMatch := strings.Index(body, `if outcome.Result.Name == ""`)
	reminder := strings.Index(body, "查無此統一編號，請再次確認或自行輸入買方名稱。")
	interactiveSuccess := strings.LastIndex(body, "if outcome.Interactive")
	if noMatch < 0 || reminder < noMatch || interactiveSuccess < 0 || reminder > interactiveSuccess {
		t.Fatal("automatic 8-digit BAN lookup must show the no-match reminder before the interactive-only success messages")
	}}

func TestCYInvoiceRecordDoubleClickOpensReadOnlyDetail(t *testing.T) {
	native := readCYInvoiceSource(t, "ui_native_windows.go")
	notify := functionBody(t, native, "handleNotifyMessage")
	for _, required := range []string{"nmDblClk", "visibleRecordRows", "showRecordDetail"} {
		if !strings.Contains(notify, required) {
			t.Fatalf("record double-click path is missing %q", required)
		}
	}
	detail := readCYInvoiceSource(t, "record_detail_windows.go")
	for _, required := range []string{"esReadOnly", "recordview.FormatDetail", "runOwnedModalWindow"} {
		if !strings.Contains(detail, required) {
			t.Fatalf("record detail must remain native and read-only; missing %q", required)
		}
	}
}
