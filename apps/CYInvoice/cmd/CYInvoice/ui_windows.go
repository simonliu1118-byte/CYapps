//go:build windows

package main

import (
	"context"
	"fmt"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"syscall"
	"time"
	"unsafe"

	"cyinvoice/internal/appdata"
	"cyinvoice/internal/fixeddecimal"
	"cyinvoice/internal/invoicing"
)

const (
	wsChild = 0x40000000
	wsTabStop = 0x00010000
	wsGroup = 0x00020000
	wsBorder = 0x00800000
	wsVScroll = 0x00200000
	bsPushButton = 0
	bsDefaultPushButton = 1
	bsOwnerDraw = 11
	bsAutoRadioButton = 9
	bsGroupBox = 7
	esPassword = 0x0020
	esAutoHScroll = 0x0080
	esAutoVScroll = 0x0040
	esMultiLine = 0x0004
	esReadOnly = 0x0800
	wmSetFont = 0x0030
	bmGetCheck = 0x00F0
	bmSetCheck = 0x00F1
	bstChecked = 1
	swHide = 0
	swShow = 5

	idTabInvoice = 1001
	idTabRecords = 1002
	idSettings = 1003
	idPaperBuyer = 1101
	idCompanyBuyer = 1102
	idOrderID = 1103
	idBuyerBAN = 1104
	idBuyerName = 1105
	idDescription = 1106
	idQuantity = 1107
	idUnitPrice = 1108
	idRemark = 1109
	idClear = 1110
	idPreview = 1111
	idIssue = 1112
	idImportMO = 1113
	idItemAdd = 1114
	idItemDelete = 1115
	idBuyerLookup = 1116
	idOrderAuto = 1117
	idOrderCustom = 1118
	idImportERP = 1119
	idImportCoupang = 1120
	idTaxInclusive = 1121
	idTaxExclusive = 1122
	idRecordsRefresh = 1201
	idRecordsFilter = 1202
	idRecordsSearch = 1203
	idRecordsClear = 1204
	idRecordsDateFrom = 1205
	idRecordsDateTo = 1206
	idRecordsInvoiceNo = 1207
	idRecordsOrderNo = 1208
	idRecordsBuyer = 1209
	idRecordsBAN = 1210
	idRecordsSource = 1211
	idRecordsStatus = 1212
	idSettingsBack = 1301
	idEnvironmentTest = 1302
	idEnvironmentProd = 1303
	idProdBAN = 1304
	idProdKey = 1305
	idMOPassword = 1306
	idAdminPassword = 1307
	idSettingsSave = 1308
	idSettingsLoginPassword = 1310
	idSettingsUnlock = 1311
	idSettingsCancel = 1312
	idSettingsChangePassword = 1313
	apiRetryTimerID = 71
	apiRetryMilliseconds = 10 * 1000
)

var (
	defaultFont uintptr
	invoiceControls []uintptr
	recordControls []uintptr
	settingsControls []uintptr
	settingsLoginControls []uintptr
	handles = map[int]uintptr{}
	salesLabel uintptr
	taxLabel uintptr
	totalLabel uintptr
	recordsText uintptr
	apiStatusLabel uintptr
	apiReasonLabel uintptr
	environmentBanner uintptr
	remarkCounterLabel uintptr
	invoiceService *invoicing.Service
	itemsList uintptr
	manualRows = []manualItemRow{{}}
	activeManualRow int
	pricesAreInclusive = true
	recordRowStyles []recordRowStyle
	apiHealthGeneration uint64
	apiHealthInFlight uint32
	apiHealthMu sync.Mutex
	apiHealthResults = map[uint64]error{}
	buyerLookupGeneration uint64
	buyerLookupMu sync.Mutex
	buyerLookupResults = map[uint64]buyerLookupOutcome{}
	lastBuyerLookupBAN string
	settingsUnlocked bool
)

type manualItemRow struct {
	Description string
	Quantity string
	UnitPrice string
}

type recordRowStyle struct {
	State  string
	Upload int
}

type buyerLookupOutcome struct {
	BAN string
	Result invoicing.NameLookup
	Err error
}

func buildGUI(parent uintptr) error {
	defaultFont = uiFont
	environmentBanner = addControl("STATIC", "測試環境｜光貿測試公司 12345678｜不會開立正式發票", parent,
		18, 12, 1128, 49, wsChild|wsVisible|ssCenter|ssCenterImage, 0, nil)
	procSendMessageW.Call(environmentBanner, wmSetFont, bannerFont, 1)
	setStaticStyle(environmentBanner, rgb(0, 51, 153), rgb(238, 246, 255), bannerBrush, false)
	apiStatusLabel = addControl("STATIC", "● API 正常", parent,
		930, 12, 198, 49, wsChild|wsVisible|ssRight|ssCenterImage, 0, nil)
	setStaticStyle(apiStatusLabel, rgb(0, 170, 65), rgb(238, 246, 255), bannerBrush, false)
	apiReasonLabel = addControl("STATIC", "", parent,
		760, 43, 368, 14, wsChild|wsVisible|ssRight|ssCenterImage, 0, nil)
	procSendMessageW.Call(apiReasonLabel, wmSetFont, apiReasonFont, 1)
	setStaticStyle(apiReasonLabel, rgb(96, 96, 96), rgb(238, 246, 255), bannerBrush, false)
	tabHandle = addTab(parent, 18, 73, 1128, 704)
	addButton(parent, "設定", settingsButtonX, settingsButtonY, settingsButtonWidth, settingsButtonHeight, idSettings, nil)
	defaultFont = contentFont
	buildInvoicePage(parent)
	buildRecordsPage(parent)
	setChecked(handles[idOrderAuto], true)
	setChecked(handles[idTaxInclusive], true)
	setChecked(handles[idTaxExclusive], false)
	setChecked(handles[idPaperBuyer], true)
	setChecked(handles[idEnvironmentTest], true)
	setControlText(handles[idOrderID], nextOrderID())
	setOrderMode(false)
	setBuyerMode(false)
	refreshTotals()
	invoiceService = invoicing.New(appRepository)
	refreshRecords()
	refreshAPIState()
	showPage(invoiceControls)
	relayoutGUI(parent)
	return nil
}

func buildInvoicePage(parent uintptr) {
	addPanelTitleGroup(parent, "Excel 匯入開立", 36, 132, 1092, 68, 118, &invoiceControls)
	addButtonStyle(parent, "匯入鼎新 ERP 銷貨單", 56, 157, 196, 32, idImportERP, bsOwnerDraw, &invoiceControls)
	addButtonStyle(parent, "匯入 MO店+", 270, 157, 134, 32, idImportMO, bsOwnerDraw, &invoiceControls)
	addButtonStyle(parent, "匯入酷澎", 422, 157, 126, 32, idImportCoupang, bsOwnerDraw, &invoiceControls)

	addPanelTitleGroup(parent, "發票基本資料", 36, 214, 1092, 132, 112, &invoiceControls)
	addStatic(parent, "訂單編號", 56, 241, 80, 25, &invoiceControls)
	addButtonStyle(parent, "自動產生", 136, 238, 104, 26, idOrderAuto, bsAutoRadioButton|wsGroup, &invoiceControls)
	addButtonStyle(parent, "自訂", 320, 238, 72, 26, idOrderCustom, bsAutoRadioButton, &invoiceControls)
	addEdit(parent, "", 402, 239, 190, 24, idOrderID, esAutoHScroll, &invoiceControls)
	addStatic(parent, "買方資料", 56, 278, 80, 25, &invoiceControls)
	addButtonStyle(parent, "一般消費者（紙本）", 136, 275, 176, 26, idPaperBuyer, bsAutoRadioButton|wsGroup, &invoiceControls)
	addButtonStyle(parent, "公司統編（紙本）", 320, 275, 205, 26, idCompanyBuyer, bsAutoRadioButton, &invoiceControls)
	addStatic(parent, "統一編號", 56, 315, 80, 25, &invoiceControls)
	buyerBANEdit := addEdit(parent, "", 136, 313, 136, 24, idBuyerBAN, esAutoHScroll, &invoiceControls)
	addStatic(parent, "買方名稱", 292, 315, 80, 25, &invoiceControls)
	buyerNameEdit := addEdit(parent, "", 376, 313, 730, 24, idBuyerName, esAutoHScroll, &invoiceControls)
	setStaticStyle(buyerBANEdit, rgb(112, 112, 112), rgb(238, 238, 238), disabledEditBrush, false)
	setStaticStyle(buyerNameEdit, rgb(112, 112, 112), rgb(238, 238, 238), disabledEditBrush, false)

	addPanelTitleGroup(parent, "商品明細資料（最多 50 筆）", 36, 360, 1092, 206, 218, &invoiceControls)
	addButtonStyle(parent, "以含稅輸入", 76, 386, 135, 27, idTaxInclusive, bsOwnerDraw|wsGroup, &invoiceControls)
	addButtonStyle(parent, "以未稅輸入", 218, 386, 135, 27, idTaxExclusive, bsOwnerDraw, &invoiceControls)
	addButton(parent, "＋ 新增明細", 987, 385, 121, 29, idItemAdd, &invoiceControls)
	invoiceItemsList = addControl("SysListView32", "", parent, 57, 424, 1051, 132,
		wsChild|wsVisible|wsTabStop|wsBorder|lvsReport|lvsOwnerDrawFixed|lvsSingleSel|lvsShowSelAlways, 0, &invoiceControls)
	procSendMessageW.Call(invoiceItemsList, lvmSetExtendedListStyle, 0, lvsExGridLines|lvsExFullRowSelect|lvsExDoubleBuffer)
	addListViewColumns(invoiceItemsList, []struct{ Title string; Width int; Right bool }{
		{"序號", 50, false}, {"品名", 425, false}, {"課稅別", 78, false}, {"數量", 82, true},
		{"單價（含稅）", 140, true}, {"金額（含稅）", 148, true}, {"操作", 80, false},
	})
	refreshManualRows()

	addPanelTitleGroup(parent, "發票總備註", 36, 579, 678, 112, 96, &invoiceControls)
	addEdit(parent, "", 54, 604, 642, 62, idRemark, esMultiLine|esAutoVScroll|wsVScroll, &invoiceControls)
	remarkCounterLabel = addStatic(parent, "0 / 200", 610, 666, 80, 20, &invoiceControls)

	addPanelTitleGroup(parent, "金額總計", 724, 579, 404, 112, 80, &invoiceControls)
	addStatic(parent, "應稅銷售額", 750, 602, 130, 25, &invoiceControls)
	salesLabel = addStatic(parent, "0", 1042, 602, 64, 25, &invoiceControls)
	addStatic(parent, "營業稅額（5%）", 750, 629, 150, 25, &invoiceControls)
	taxLabel = addStatic(parent, "0", 1042, 629, 64, 25, &invoiceControls)
	addControl("STATIC", "", parent, 750, 654, 356, 2, wsChild|wsVisible|ssEtchedHorizontal, 0, &invoiceControls)
	addStatic(parent, "發票總額", 750, 660, 130, 26, &invoiceControls)
	totalLabel = addStatic(parent, "0", 1042, 660, 64, 26, &invoiceControls)
	procSendMessageW.Call(totalLabel, wmSetFont, boldFont, 1)

	addButton(parent, "清空", 356, 707, 140, 36, idClear, &invoiceControls)
	addButtonStyle(parent, "開立測試發票", 512, 707, 142, 36, idIssue, bsDefaultPushButton, &invoiceControls)
	addButton(parent, "預覽", 669, 707, 140, 36, idPreview, &invoiceControls)
	setEditLimit(idOrderID, 40); setEditLimit(idRemark, 200)
}

func buildRecordsPage(parent uintptr) {
	addStatic(parent, "開立日期", 41, 133, 70, 27, &recordControls)
	addEdit(parent, "", 109, 132, 115, 27, idRecordsDateFrom, esAutoHScroll, &recordControls)
	addStatic(parent, "～", 233, 133, 24, 27, &recordControls)
	addEdit(parent, "", 260, 132, 115, 27, idRecordsDateTo, esAutoHScroll, &recordControls)
	addStatic(parent, "發票號碼", 396, 133, 75, 27, &recordControls)
	addEdit(parent, "", 474, 132, 110, 27, idRecordsInvoiceNo, esAutoHScroll, &recordControls)
	addStatic(parent, "訂單編號", 598, 133, 75, 27, &recordControls)
	addEdit(parent, "", 678, 132, 248, 27, idRecordsOrderNo, esAutoHScroll, &recordControls)
	addStatic(parent, "買受人", 41, 174, 65, 27, &recordControls)
	addEdit(parent, "", 109, 173, 160, 27, idRecordsBuyer, esAutoHScroll, &recordControls)
	addStatic(parent, "統編", 283, 174, 45, 27, &recordControls)
	addEdit(parent, "", 333, 173, 107, 27, idRecordsBAN, esAutoHScroll, &recordControls)
	addStatic(parent, "來源", 454, 174, 45, 27, &recordControls)
	addCombo(parent, 505, 171, 120, 180, idRecordsSource, []string{"全部", "手動", "MO店+", "酷澎"}, &recordControls)
	addStatic(parent, "發票狀態", 640, 174, 75, 27, &recordControls)
	addCombo(parent, 722, 171, 128, 180, idRecordsStatus, []string{"全部", "已開立", "開立失敗", "結果不明", "資料變更中", "已作廢"}, &recordControls)
	addButton(parent, "查詢", 41, 213, 89, 34, idRecordsFilter, &recordControls)
	addButton(parent, "清除條件", 141, 213, 101, 34, idRecordsClear, &recordControls)
	addButton(parent, "重新整理狀態", 253, 213, 135, 34, idRecordsRefresh, &recordControls)
	recordsList = addListView(parent, 41, 259, 1078, 510, 0, &recordControls)
	addListViewColumns(recordsList, []struct{ Title string; Width int; Right bool }{
		{"開立時間", 130, false}, {"發票號碼", 100, false}, {"來源", 70, false},
		{"訂單編號", 145, false}, {"統編", 80, false}, {"買受人", 145, false},
		{"金額", 95, true}, {"交付方式", 90, false}, {"發票狀態", 90, false}, {"上傳", 60, false},
	})
	from, to := defaultRecordDateRange(time.Now())
	setControlText(handles[idRecordsDateFrom], from)
	setControlText(handles[idRecordsDateTo], to)
}

func buildSettingsPage(parent uintptr) {
	settingsControls = nil
	settingsLoginControls = nil
	addGroup(parent, "設定權限驗證", 44, 48, 672, 230, &settingsLoginControls)
	addStatic(parent, "請輸入管理密碼", 138, 108, 160, 28, &settingsLoginControls)
	addEdit(parent, "", 298, 105, 300, 28, idSettingsLoginPassword, esPassword|esAutoHScroll, &settingsLoginControls)
	addButtonStyle(parent, "進入設定", 260, 176, 130, 38, idSettingsUnlock, bsDefaultPushButton, &settingsLoginControls)
	addButton(parent, "取消", 404, 176, 110, 38, idSettingsCancel, &settingsLoginControls)

	addGroup(parent, "使用環境", 28, 24, 704, 190, &settingsControls)
	addButtonStyle(parent, "光貿測試環境", 56, 58, 150, 30, idEnvironmentTest, bsAutoRadioButton|wsGroup, &settingsControls)
	addStatic(parent, "測試帳號由光貿固定提供，不可修改。", 222, 61, 400, 28, &settingsControls)
	addButtonStyle(parent, "正式公司", 56, 111, 120, 30, idEnvironmentProd, bsAutoRadioButton, &settingsControls)
	addStatic(parent, "統編", 188, 114, 44, 28, &settingsControls)
	prodBANEdit := addEdit(parent, "", 232, 111, 112, 28, idProdBAN, esAutoHScroll, &settingsControls)
	addStatic(parent, "App Key", 360, 114, 68, 28, &settingsControls)
	prodKeyEdit := addEdit(parent, "", 430, 111, 270, 28, idProdKey, esPassword|esAutoHScroll, &settingsControls)
	setStaticStyle(prodBANEdit, rgb(112, 112, 112), rgb(238, 238, 238), disabledEditBrush, false)
	setStaticStyle(prodKeyEdit, rgb(112, 112, 112), rgb(238, 238, 238), disabledEditBrush, false)

	addGroup(parent, "平台檔案密碼", 28, 228, 704, 100, &settingsControls)
	addStatic(parent, "MO店+ Excel 保護密碼", 56, 267, 185, 28, &settingsControls)
	addEdit(parent, "", 251, 264, 449, 28, idMOPassword, esPassword|esAutoHScroll, &settingsControls)

	addButton(parent, "設定管理密碼", 158, 360, 140, 40, idSettingsChangePassword, &settingsControls)
	addButtonStyle(parent, "儲存設定", 312, 360, 140, 40, idSettingsSave, bsDefaultPushButton, &settingsControls)
	addButton(parent, "取消", 466, 360, 140, 40, idSettingsBack, &settingsControls)
	loadSettingsIntoControls()
}

func handleCommand(id int) {
	switch id {
	case idTabInvoice:
		showPage(invoiceControls)
	case idTabRecords:
		refreshRecords()
		showPage(recordControls)
	case idSettings:
		openSettingsLogin()
	case idSettingsBack:
		leaveSettings()
	case idSettingsUnlock:
		unlockSettings()
	case idSettingsCancel:
		leaveSettings()
	case idSettingsChangePassword:
		showChangePasswordWindow()
	case idEnvironmentTest:
		setSettingsEnvironment(false)
	case idEnvironmentProd:
		setSettingsEnvironment(true)
	case idOrderAuto:
		setOrderMode(false)
	case idOrderCustom:
		setOrderMode(true)
	case idPaperBuyer:
		setBuyerMode(false)
	case idCompanyBuyer:
		setBuyerMode(true)
	case idBuyerLookup:
		lookupBuyerName()
	case idBuyerBAN:
		buyerBANChanged()
	case idClear:
		clearDraft()
	case idPreview:
		previewDraft()
	case idIssue:
		issueDraft()
	case idItemAdd:
		addManualItem()
	case idItemDelete:
		deleteManualItem()
	case idImportMO:
		importMOExcel()
	case idImportCoupang:
		importCoupangExcel()
	case idImportERP:
		importERPExcel()
	case idTaxInclusive:
		if !isChecked(handles[idCompanyBuyer]) { updateTaxChoice(true); return }
		convertTaxMode(true)
	case idTaxExclusive:
		if !isChecked(handles[idCompanyBuyer]) { updateTaxChoice(true); return }
		convertTaxMode(false)
	case idRecordsRefresh:
		refreshRecordsFromAPI()
	case idRecordsFilter:
		refreshRecords()
	case idRecordsClear:
		clearRecordFilters()
	case idSettingsSave:
		saveSettings()
	case idRemark:
		refreshRemarkCounter()
	}
}

func setOrderMode(custom bool) {
	setChecked(handles[idOrderCustom], custom)
	setChecked(handles[idOrderAuto], !custom)
	procEnableWindow.Call(handles[idOrderID], boolValue(custom))
	if !custom {
		setControlText(handles[idOrderID], nextOrderID())
	} else {
		procInvalidateRect.Call(handles[idOrderID], 0, 1)
		procSetFocus.Call(handles[idOrderID])
		procSendMessageW.Call(handles[idOrderID], emSetSel, 0, ^uintptr(0))
	}
}

func boolValue(value bool) uintptr {
	if value { return 1 }
	return 0
}

func showPage(page []uintptr) {
	hadProductEdit := productCellEditor != 0
	commitProductCellEdit(false)
	if hadProductEdit { refreshTotals() }
	for _, group := range [][]uintptr{invoiceControls, recordControls} {
		for _, handle := range group {
			procShowWindow.Call(handle, swHide)
		}
	}
	for _, handle := range page {
		procShowWindow.Call(handle, swShow)
	}
}

func setBuyerMode(company bool) {
	if !company && !pricesAreInclusive {
		if !convertTaxMode(true) {
			setChecked(handles[idCompanyBuyer], true)
			setChecked(handles[idPaperBuyer], false)
			return
		}
	}
	setChecked(handles[idCompanyBuyer], company)
	setChecked(handles[idPaperBuyer], !company)
	enabled := uintptr(0)
	if company { enabled = 1 }
	procEnableWindow.Call(handles[idBuyerBAN], enabled)
	procEnableWindow.Call(handles[idBuyerName], enabled)
	procEnableWindow.Call(handles[idTaxInclusive], enabled)
	procEnableWindow.Call(handles[idTaxExclusive], enabled)
	procInvalidateRect.Call(handles[idBuyerBAN], 0, 1)
	procInvalidateRect.Call(handles[idBuyerName], 0, 1)
	if !company {
		setControlText(handles[idBuyerBAN], "")
		setControlText(handles[idBuyerName], "")
		updateTaxChoice(true)
		pricesAreInclusive = true
		lastBuyerLookupBAN = ""
	}
	refreshTotals()
}

func clearDraft() {
	for _, id := range []int{idBuyerBAN, idBuyerName, idRemark} {
		setControlText(handles[id], "")
	}
	setControlText(handles[idOrderID], nextOrderID())
	setOrderMode(false)
	setBuyerMode(false)
	manualRows = []manualItemRow{{}}
	activeManualRow = 0
	pricesAreInclusive = true
	updateTaxChoice(true)
	refreshManualRows()
	refreshRemarkCounter()
	refreshTotals()
}

func draftFromControls() (appdata.InvoiceDraft, error) {
	syncActiveManualRow()
	rows := make([]manualItemRow, 0, len(manualRows))
	for _, row := range manualRows {
		if !row.empty() { rows = append(rows, row) }
	}
	manualRows = rows
	if len(manualRows) == 0 { manualRows = []manualItemRow{{}} }
	if activeManualRow >= len(manualRows) { activeManualRow = len(manualRows)-1 }
	refreshManualRows()
	items := make([]appdata.InvoiceItem, 0, len(manualRows))
	for index, row := range manualRows {
		if row.empty() { continue }
		item, err := row.item(index)
		if err != nil { return appdata.InvoiceDraft{}, err }
		items = append(items, item)
	}
	pricesExcludeTax := isChecked(handles[idCompanyBuyer]) && !pricesAreInclusive
	draft := appdata.InvoiceDraft{
		OrderID: controlText(handles[idOrderID]),
		CompanyBuyer: isChecked(handles[idCompanyBuyer]),
		BuyerIdentifier: controlText(handles[idBuyerBAN]),
		BuyerName: controlText(handles[idBuyerName]),
		Items: items,
		MainRemark: controlText(handles[idRemark]),
		PricesExcludeTax: pricesExcludeTax,
	}
	_, _, total, err := appdata.CalculateInvoiceTotals(items, draft.CompanyBuyer, pricesExcludeTax)
	if err != nil { return draft, err }
	draft.TotalAmount = total
	if err := draft.Validate(); err != nil { return draft, err }
	return draft, nil
}

func refreshTotals() {
	syncActiveManualRow()
	items := make([]appdata.InvoiceItem, 0, len(manualRows))
	for _, row := range manualRows {
		amount, err := row.amountDecimal()
		if err != nil { continue }
		items = append(items, appdata.InvoiceItem{Quantity: 1, UnitPrice: fixeddecimal.RoundInt64(amount), UnitPriceDecimal: fixeddecimal.Format(amount), Amount: fixeddecimal.RoundInt64(amount), AmountDecimal: fixeddecimal.Format(amount)})
	}
	pricesExcludeTax := isChecked(handles[idCompanyBuyer]) && !pricesAreInclusive
	sales, tax, total, err := appdata.CalculateInvoiceTotals(items, isChecked(handles[idCompanyBuyer]), pricesExcludeTax)
	if err != nil { sales, tax, total = 0, 0, 0 }
	setControlText(salesLabel, strconv.FormatInt(sales, 10))
	setControlText(taxLabel, strconv.FormatInt(tax, 10))
	setControlText(totalLabel, strconv.FormatInt(total, 10))
	if pricesExcludeTax {
		setListViewColumnTitle(invoiceItemsList, 4, "單價（未稅）")
		setListViewColumnTitle(invoiceItemsList, 5, "金額（未稅）")
	} else {
		setListViewColumnTitle(invoiceItemsList, 4, "單價（含稅）")
		setListViewColumnTitle(invoiceItemsList, 5, "金額（含稅）")
	}
}

func previewDraft() {
	draft, err := draftFromControls()
	if err != nil { showError(err.Error()); return }
	showInfo(previewText(draft))
}

func previewText(draft appdata.InvoiceDraft) string {
	sales, tax, _, _ := appdata.CalculateInvoiceTotals(draft.Items, draft.CompanyBuyer, draft.PricesExcludeTax)
	buyer := "一般消費者（紙本）"
	if draft.CompanyBuyer { buyer = draft.BuyerIdentifier + "　" + draft.BuyerName }
	var text strings.Builder
	fmt.Fprintf(&text, "發票預覽\n\n訂單編號：%s\n買受人：%s\n商品明細：\n", draft.OrderID, buyer)
	limit := len(draft.Items); if limit > 10 { limit = 10 }
	for index := 0; index < limit; index++ {
		item := draft.Items[index]
		quantity, unitPrice, amount, _ := appdata.InvoiceItemDecimals(item)
		fmt.Fprintf(&text, "%d. %s　%s × %s = %s\n", index+1, item.Description, fixeddecimal.Format(quantity), fixeddecimal.Format(unitPrice), fixeddecimal.Format(amount))
	}
	if len(draft.Items) > limit { fmt.Fprintf(&text, "…另有 %d 筆商品\n", len(draft.Items)-limit) }
	fmt.Fprintf(&text, "\n應稅銷售額：%d\n營業稅額：%d\n發票總額：%d", sales, tax, draft.TotalAmount)
	return text.String()
}

func (row manualItemRow) empty() bool {
	return strings.TrimSpace(row.Description) == "" && strings.TrimSpace(row.Quantity) == "" && strings.TrimSpace(row.UnitPrice) == ""
}

func (row manualItemRow) amountDecimal() (fixeddecimal.Value, error) {
	quantity, err := fixeddecimal.Parse(row.Quantity)
	if err != nil { return 0, err }
	price, err := fixeddecimal.Parse(row.UnitPrice)
	if err != nil { return 0, err }
	return fixeddecimal.Multiply(quantity, price)
}

func (row manualItemRow) amountText() string {
	amount, err := row.amountDecimal()
	if err != nil { return "0" }
	return fixeddecimal.Format(amount)
}

func (row manualItemRow) item(index int) (appdata.InvoiceItem, error) {
	description := strings.TrimSpace(row.Description)
	if description == "" { return appdata.InvoiceItem{}, fmt.Errorf("第 %d 筆請輸入品名", index+1) }
	quantity, err := fixeddecimal.Parse(row.Quantity)
	if err != nil || quantity <= 0 { return appdata.InvoiceItem{}, fmt.Errorf("第 %d 筆數量格式錯誤（最多小數點後 7 位）", index+1) }
	price, err := fixeddecimal.Parse(row.UnitPrice)
	if err != nil { return appdata.InvoiceItem{}, fmt.Errorf("第 %d 筆單價格式錯誤（最多小數點後 7 位）", index+1) }
	amount, err := fixeddecimal.Multiply(quantity, price)
	if err != nil { return appdata.InvoiceItem{}, fmt.Errorf("第 %d 筆商品金額超出範圍", index+1) }
	return appdata.InvoiceItem{
		Description: description,
		Quantity: fixeddecimal.Float64(quantity), QuantityDecimal: fixeddecimal.Format(quantity),
		UnitPrice: fixeddecimal.RoundInt64(price), UnitPriceDecimal: fixeddecimal.Format(price),
		TaxType: "1",
		Amount: fixeddecimal.RoundInt64(amount), AmountDecimal: fixeddecimal.Format(amount),
	}, nil
}

func convertTaxMode(toInclusive bool) bool {
	syncActiveManualRow()
	if toInclusive == pricesAreInclusive {
		updateTaxChoice(pricesAreInclusive)
		refreshTotals()
		return true
	}
	converted := append([]manualItemRow(nil), manualRows...)
	for index := range converted {
		if strings.TrimSpace(converted[index].UnitPrice) == "" { continue }
		price, err := fixeddecimal.Parse(converted[index].UnitPrice)
		if err != nil {
			updateTaxChoice(pricesAreInclusive)
			showError(fmt.Sprintf("第 %d 筆單價格式錯誤，無法切換含稅／未稅。\n請修正為最多小數點後 7 位的數值。", index+1))
			return false
		}
		if toInclusive {
			price, err = fixeddecimal.MultiplyRatio(price, 21, 20)
		} else {
			price, err = fixeddecimal.MultiplyRatio(price, 20, 21)
		}
		if err != nil {
			updateTaxChoice(pricesAreInclusive)
			showError(fmt.Sprintf("第 %d 筆單價超出可換算範圍。", index+1))
			return false
		}
		converted[index].UnitPrice = fixeddecimal.Format(price)
	}
	manualRows = converted
	pricesAreInclusive = toInclusive
	updateTaxChoice(toInclusive)
	refreshManualRows()
	refreshTotals()
	return true
}

func syncActiveManualRow() {
	commitProductCellEdit(false)
}

func addManualItem() {
	syncActiveManualRow()
	if len(manualRows) >= appdata.MaxInvoiceItems { showError(fmt.Sprintf("商品明細最多 %d 筆", appdata.MaxInvoiceItems)); return }
	manualRows = append(manualRows, manualItemRow{})
	activeManualRow = len(manualRows)-1
	refreshManualRows()
	refreshTotals()
}

func deleteManualItem() {
	syncActiveManualRow()
	if len(manualRows) <= 1 {
		manualRows = []manualItemRow{{}}
		activeManualRow = 0
	} else {
		manualRows = append(manualRows[:activeManualRow], manualRows[activeManualRow+1:]...)
		if activeManualRow >= len(manualRows) { activeManualRow = len(manualRows)-1 }
	}
	refreshManualRows()
	refreshTotals()
}

func refreshRemarkCounter() {
	setControlText(remarkCounterLabel, fmt.Sprintf("%d / %d", len([]rune(controlText(handles[idRemark]))), appdata.MaxRemarkRunes))
}

func refreshManualRows() {
	commitProductCellEdit(false)
	if invoiceItemsList == 0 { return }
	clearListView(invoiceItemsList)
	for index, row := range manualRows {
		addListViewRow(invoiceItemsList, index, []string{
			strconv.Itoa(index+1), row.Description, "應稅", row.Quantity,
			row.UnitPrice, row.amountText(), "",
		})
	}
	for index := len(manualRows); index < 5; index++ {
		addListViewRow(invoiceItemsList, index, []string{"", "", "", "", "", "", ""})
	}
	if activeManualRow < 0 { activeManualRow = 0 }
	if activeManualRow >= len(manualRows) { activeManualRow = len(manualRows)-1 }
	procInvalidateRect.Call(invoiceItemsList, 0, 1)
}

func updateTaxChoice(inclusive bool) {
	pricesAreInclusive = inclusive
	setChecked(handles[idTaxInclusive], inclusive)
	setChecked(handles[idTaxExclusive], !inclusive)
	procInvalidateRect.Call(handles[idTaxInclusive], 0, 1)
	procInvalidateRect.Call(handles[idTaxExclusive], 0, 1)
}

func issueDraft() {
	if invoiceService == nil { showError("開立服務尚未初始化"); return }
	lookup := invoicing.NameLookup{}
	if isChecked(handles[idCompanyBuyer]) {
		var lookupErr error
		setAPIWorking("● 正在查詢買受人名稱…")
		lookup, lookupErr = invoiceService.LookupBuyerName(context.Background(), controlText(handles[idBuyerBAN]))
		refreshAPIState()
		if lookupErr != nil { showError(lookupErr.Error()); return }
		if lookup.Name != "" { setControlText(handles[idBuyerName], lookup.Name) }
		if strings.TrimSpace(controlText(handles[idBuyerName])) == "" { showError("光貿查詢成功但沒有公司名稱，請手動輸入買方名稱後再開立。"); return }
	}
	draft, err := draftFromControls()
	if err != nil { showError(err.Error()); return }
	settings, err := appRepository.Settings.LoadOrCreate()
	if err != nil { showError("讀取環境設定失敗：" + err.Error()); return }
	environment := "光貿測試環境"
	if settings.Environment == appdata.EnvironmentProduction { environment = "正式公司環境" }
	if !confirmAction(previewText(draft) + "\n\n即將送至「" + environment + "」開立。\n是否確定開立？") { return }
	setAPIWorking("● 正在連線光貿 API…")
	procEnableWindow.Call(handles[idIssue], 0)
	result, issueErr := invoiceService.IssueManualWithLookup(context.Background(), draft, lookup)
	procEnableWindow.Call(handles[idIssue], 1)
	refreshAPIState()
	refreshRecords()
	if issueErr != nil {
		appLogger.Errorf("issue invoice order=%s state=%s: %v", draft.OrderID, result.Record.InvoiceState, issueErr)
		showError(issueErr.Error())
		return
	}
	appLogger.Infof("invoice opened order=%s invoice=%s environment=%s", result.Record.OrderID, result.Record.InvoiceNumber, result.Record.Environment)
	showInfo(fmt.Sprintf("發票開立成功\n\n發票號碼：%s\n訂單編號：%s\n買受人：%s\n金額：%d\n正式發票時間：%s %s\n送出時間：%s",
		result.Record.InvoiceNumber, result.Record.OrderID, result.Record.BuyerName,
		result.Record.Amount, result.Record.InvoiceDate, result.Record.InvoiceTime, result.Record.SentAt))
	clearDraft()
}

func nextOrderID() string {
	records, err := appRepository.Invoices.LoadOrCreate()
	if err != nil { return time.Now().Format("20060102") + "001" }
	id, err := appdata.NextManualOrderID(time.Now(), records)
	if err != nil { return "" }
	return id
}

func refreshRecords() {
	if recordsList == 0 { return }
	records, err := appRepository.Invoices.LoadOrCreate()
	if err != nil { showError("讀取已開立發票清單失敗：" + err.Error()); return }
	from, fromErr := optionalDate(controlText(handles[idRecordsDateFrom]))
	to, toErr := optionalDate(controlText(handles[idRecordsDateTo]))
	if fromErr != nil || toErr != nil {
		showError("開立日期格式必須為 YYYY/MM/DD")
		return
	}
	invoiceNumber := controlText(handles[idRecordsInvoiceNo])
	orderID := controlText(handles[idRecordsOrderNo])
	buyer := controlText(handles[idRecordsBuyer])
	ban := controlText(handles[idRecordsBAN])
	source := comboText(handles[idRecordsSource])
	state := comboText(handles[idRecordsStatus])
	clearListView(recordsList)
	recordRowStyles = recordRowStyles[:0]
	row := 0
	for _, record := range records {
		date := recordDate(record)
		if !from.IsZero() && (date.IsZero() || date.Before(from)) { continue }
		if !to.IsZero() && (date.IsZero() || date.After(to)) { continue }
		if !containsFold(record.InvoiceNumber, invoiceNumber) || !containsFold(record.OrderID, orderID) ||
			!containsFold(record.BuyerName, buyer) || !containsFold(record.BuyerIdentifier, ban) {
			continue
		}
		if source != "" && source != "全部" && record.Source != source { continue }
		if state != "" && state != "全部" && record.InvoiceState != state { continue }
		addListViewRow(recordsList, row, []string{
			recordIssueTime(record), record.InvoiceNumber, record.Source, record.OrderID,
			record.BuyerIdentifier, record.BuyerName, strconv.FormatInt(record.Amount, 10),
			record.Delivery, record.InvoiceState, recordUploadText(record),
		})
		recordRowStyles = append(recordRowStyles, recordRowStyle{State: record.InvoiceState, Upload: record.UploadStatus})
		row++
	}
}

func optionalDate(value string) (time.Time, error) {
	value = strings.TrimSpace(value)
	if value == "" { return time.Time{}, nil }
	return time.ParseInLocation("2006/01/02", value, time.Local)
}

func recordDate(record appdata.InvoiceRecord) time.Time {
	for _, value := range []string{record.InvoiceDate, record.SentAt} {
		value = strings.TrimSpace(value)
		if len(value) >= 10 {
			if parsed, err := time.ParseInLocation("2006/01/02", value[:10], time.Local); err == nil {
				return parsed
			}
		}
	}
	return time.Time{}
}

func recordIssueTime(record appdata.InvoiceRecord) string {
	if strings.TrimSpace(record.InvoiceDate) != "" {
		return strings.TrimSpace(record.InvoiceDate + " " + record.InvoiceTime)
	}
	return record.SentAt
}

func recordUploadText(record appdata.InvoiceRecord) string {
	if record.UploadStatus != 0 { return "●" }
	return ""
}

func clearRecordFilters() {
	from, to := defaultRecordDateRange(time.Now())
	setControlText(handles[idRecordsDateFrom], from)
	setControlText(handles[idRecordsDateTo], to)
	for _, id := range []int{idRecordsInvoiceNo, idRecordsOrderNo, idRecordsBuyer, idRecordsBAN} {
		setControlText(handles[id], "")
	}
	procSendMessageW.Call(handles[idRecordsSource], cbSetCurSel, 0, 0)
	procSendMessageW.Call(handles[idRecordsStatus], cbSetCurSel, 0, 0)
	refreshRecords()
}

func refreshRecordsFromAPI() {
	if invoiceService == nil { refreshRecords(); return }
	procEnableWindow.Call(handles[idRecordsRefresh], 0)
	setControlText(handles[idRecordsRefresh], "重新整理中…")
	procUpdateWindow.Call(mainWindow)
	_, err := invoiceService.RefreshAll(context.Background())
	setControlText(handles[idRecordsRefresh], "重新整理狀態")
	procEnableWindow.Call(handles[idRecordsRefresh], 1)
	refreshRecords()
	if err != nil {
		appLogger.Errorf("refresh invoice status: %v", err)
		showError(err.Error() + "\n\n已保留原紀錄與原開立時間，未重送任何發票。")
	}
}

func refreshAPIState() {
	if apiStatusLabel == 0 || appRepository == nil { return }
	if !atomic.CompareAndSwapUint32(&apiHealthInFlight, 0, 1) { return }
	settings, err := appRepository.Settings.LoadOrCreate()
	if err != nil {
		atomic.StoreUint32(&apiHealthInFlight, 0)
		setControlText(apiStatusLabel, "● API 異常")
		setStaticStyle(apiStatusLabel, rgb(210, 0, 0), rgb(238, 246, 255), bannerBrush, false)
		setAPIReason(shortAPIErrorReason(fmt.Errorf("設定讀取失敗：%w", err)))
		startAPIRetryTimer()
		return
	}
	if settings.Environment == appdata.EnvironmentProduction {
		setControlText(environmentBanner, fmt.Sprintf("正式環境｜公司統編 %s｜將開立正式發票", settings.ProdInvoice))
		setControlText(handles[idIssue], "開立發票")
	} else {
		setControlText(environmentBanner, "測試環境｜光貿測試公司 12345678｜不會開立正式發票")
		setControlText(handles[idIssue], "開立測試發票")
	}
	if invoiceService == nil {
		atomic.StoreUint32(&apiHealthInFlight, 0)
		setControlText(apiStatusLabel, "● API 尚未檢查")
		setStaticStyle(apiStatusLabel, rgb(96, 96, 96), rgb(238, 246, 255), bannerBrush, false)
		setAPIReason("")
		return
	}
	setControlText(apiStatusLabel, "● API 查詢中")
	setStaticStyle(apiStatusLabel, rgb(96, 96, 96), rgb(238, 246, 255), bannerBrush, false)
	setAPIReason("")
	procInvalidateRect.Call(mainWindow, 0, 1)
	generation := atomic.AddUint64(&apiHealthGeneration, 1)
	go func() {
		ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
		defer cancel()
		healthErr := invoiceService.HealthCheck(ctx)
		apiHealthMu.Lock()
		apiHealthResults[generation] = healthErr
		apiHealthMu.Unlock()
		procPostMessageW.Call(mainWindow, wmAPIHealthResult, uintptr(generation), 0)
	}()
}

func setAPIWorking(text string) {
	if apiStatusLabel == 0 { return }
	setControlText(apiStatusLabel, text)
	setStaticStyle(apiStatusLabel, rgb(96, 96, 96), rgb(238, 246, 255), bannerBrush, false)
	setAPIReason("")
	procInvalidateRect.Call(mainWindow, 0, 1)
}

func finishAPIHealthCheck(generation uint64) {
	apiHealthMu.Lock()
	healthErr, found := apiHealthResults[generation]
	delete(apiHealthResults, generation)
	apiHealthMu.Unlock()
	atomic.StoreUint32(&apiHealthInFlight, 0)
	if !found || generation != atomic.LoadUint64(&apiHealthGeneration) { return }
	if healthErr != nil {
		setControlText(apiStatusLabel, "● API 異常")
		setStaticStyle(apiStatusLabel, rgb(210, 0, 0), rgb(238, 246, 255), bannerBrush, false)
		setAPIReason(shortAPIErrorReason(healthErr))
		startAPIRetryTimer()
		if appLogger != nil { appLogger.Errorf("API health check: %v", healthErr) }
	} else {
		setControlText(apiStatusLabel, "● API 正常")
		setStaticStyle(apiStatusLabel, rgb(0, 170, 65), rgb(238, 246, 255), bannerBrush, false)
		setAPIReason("")
		stopAPIRetryTimer()
	}
	procInvalidateRect.Call(mainWindow, 0, 1)
}

func shortAPIErrorReason(err error) string {
	if err == nil { return "" }
	reason := strings.TrimSpace(err.Error())
	reason = strings.TrimPrefix(reason, "API 健康檢查：")
	if len([]rune(reason)) > 52 { reason = string([]rune(reason)[:52]) + "…" }
	return "原因：" + reason + "（10 秒後重試）"
}

func setAPIReason(reason string) {
	if apiReasonLabel != 0 { setControlText(apiReasonLabel, reason) }
}

func startAPIRetryTimer() {
	if mainWindow != 0 { procSetTimer.Call(mainWindow, apiRetryTimerID, apiRetryMilliseconds, 0) }
}

func stopAPIRetryTimer() {
	if mainWindow != 0 { procKillTimer.Call(mainWindow, apiRetryTimerID) }
}

func lookupBuyerName() {
	if !isChecked(handles[idCompanyBuyer]) { showInfo("請先選擇「公司統編（紙本）」。"); return }
	ban := strings.TrimSpace(controlText(handles[idBuyerBAN]))
	setAPIWorking("● 正在查詢買受人名稱…")
	result, err := invoiceService.LookupBuyerName(context.Background(), ban)
	refreshAPIState()
	if err != nil { showError(err.Error()); return }
	if result.Name == "" { showInfo("光貿查詢成功，但沒有公司名稱。\n請手動輸入；發票成功開立後才會記憶於本機。" ); return }
	setControlText(handles[idBuyerName], result.Name)
	if result.Local { showInfo("已從本機安全記憶資料帶入公司名稱。") } else { showInfo("已從光貿統編查詢帶入公司名稱；此名稱不會另存本機。") }
}

func buyerBANChanged() {
	if !isChecked(handles[idCompanyBuyer]) || invoiceService == nil { return }
	ban := strings.TrimSpace(controlText(handles[idBuyerBAN]))
	if len(ban) != 8 {
		lastBuyerLookupBAN = ""
		setControlText(handles[idBuyerName], "")
		return
	}
	for _, character := range ban {
		if character < '0' || character > '9' {
			lastBuyerLookupBAN = ""
			setControlText(handles[idBuyerName], "")
			return
		}
	}
	if ban == lastBuyerLookupBAN { return }
	lastBuyerLookupBAN = ban
	setControlText(handles[idBuyerName], "")
	setAPIWorking("● 正在查詢買受人名稱…")
	generation := atomic.AddUint64(&buyerLookupGeneration, 1)
	go func() {
		ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
		defer cancel()
		result, lookupErr := invoiceService.LookupBuyerName(ctx, ban)
		buyerLookupMu.Lock()
		buyerLookupResults[generation] = buyerLookupOutcome{BAN: ban, Result: result, Err: lookupErr}
		buyerLookupMu.Unlock()
		procPostMessageW.Call(mainWindow, wmBuyerLookupResult, uintptr(generation), 0)
	}()
}

func finishBuyerNameLookup(generation uint64) {
	buyerLookupMu.Lock()
	outcome, found := buyerLookupResults[generation]
	delete(buyerLookupResults, generation)
	buyerLookupMu.Unlock()
	if !found || generation != atomic.LoadUint64(&buyerLookupGeneration) { return }
	if !isChecked(handles[idCompanyBuyer]) || strings.TrimSpace(controlText(handles[idBuyerBAN])) != outcome.BAN { return }
	if outcome.Err != nil {
		setControlText(apiStatusLabel, "● API 異常")
		setStaticStyle(apiStatusLabel, rgb(210, 0, 0), rgb(238, 246, 255), bannerBrush, false)
		procInvalidateRect.Call(mainWindow, 0, 1)
		if appLogger != nil { appLogger.Errorf("automatic buyer BAN lookup %s: %v", outcome.BAN, outcome.Err) }
		return
	}
	if outcome.Result.Name != "" {
		setControlText(handles[idBuyerName], outcome.Result.Name)
	}
	refreshAPIState()
}

func importMOExcel() {
	path := chooseExcelFile()
	if path == "" { return }
	settings, err := appRepository.Settings.LoadOrCreate()
	if err != nil { showError("讀取設定失敗：" + err.Error()); return }
	password, err := appRepository.Settings.MOPassword(settings)
	if err != nil { showError("解密 MO店+ Excel 密碼失敗：" + err.Error()); return }
	if strings.TrimSpace(password) == "" { showError("請先到設定視窗輸入 MO店+ Excel 保護密碼"); return }
	showPlatformImportConfirmation(appdata.SourceMO, path, password, settings)
}

func importCoupangExcel() {
	path := chooseExcelFile()
	if path == "" { return }
	settings, err := appRepository.Settings.LoadOrCreate()
	if err != nil { showError("讀取設定失敗：" + err.Error()); return }
	showPlatformImportConfirmation(appdata.SourceCoupang, path, "", settings)
}

func importERPExcel() {
	path := chooseExcelFile()
	if path == "" { return }
	settings, err := appRepository.Settings.LoadOrCreate()
	if err != nil { showError("讀取設定失敗：" + err.Error()); return }
	showPlatformImportConfirmation(appdata.SourceERP, path, "", settings)
}

func loadSettingsIntoControls() {
	if appRepository == nil { return }
	settings, err := appRepository.Settings.LoadOrCreate()
	if err != nil { showError("讀取設定失敗：" + err.Error()); return }
	setSettingsEnvironment(settings.Environment == appdata.EnvironmentProduction)
	setControlText(handles[idProdBAN], settings.ProdInvoice)
	setControlText(handles[idProdKey], "")
	setControlText(handles[idMOPassword], "")
}

func saveSettings() {
	if !settingsUnlocked {
		showError("設定視窗已鎖定，請重新輸入管理密碼")
		openSettingsLogin()
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
	if password := controlText(handles[idMOPassword]); password != "" {
		if err = appRepository.Settings.SetMOPassword(&settings, password); err != nil { showError(err.Error()); return }
	}
	if err = appRepository.Settings.Save(settings); err != nil { showError(err.Error()); return }
	appLogger.Infof("settings saved environment=%s", settings.Environment)
	refreshAPIState()
	showInfo("設定已安全儲存。\nApp Key 與 MO店+ 密碼使用 Windows DPAPI 加密，不會以明文寫入。")
}

func openSettingsLogin() {
	showSettingsWindow()
}

func unlockSettings() {
	password := controlText(handles[idSettingsLoginPassword])
	if strings.TrimSpace(password) == "" {
		showError("請輸入管理密碼")
		return
	}
	settings, err := appRepository.Settings.LoadOrCreate()
	if err != nil { showError("讀取設定失敗：" + err.Error()); return }
	if settings.AdminPasswordSet {
		if !appdata.CheckAdminPassword(settings, password) {
			showError("管理密碼錯誤")
			return
		}
	} else {
		if err = appRepository.Settings.SetAdminPassword(&settings, password); err != nil {
			showError("建立管理密碼失敗：" + err.Error())
			return
		}
		if err = appRepository.Settings.Save(settings); err != nil {
			showError("儲存管理密碼失敗：" + err.Error())
			return
		}
	}
	settingsUnlocked = true
	setControlText(handles[idSettingsLoginPassword], "")
	loadSettingsIntoControls()
	showSettingsPage(settingsControls)
}

func lockSettingsPage() {
	settingsUnlocked = false
	setControlText(handles[idSettingsLoginPassword], "")
	setControlText(handles[idProdKey], "")
	setControlText(handles[idMOPassword], "")
}

func setSettingsEnvironment(production bool) {
	setChecked(handles[idEnvironmentTest], !production)
	setChecked(handles[idEnvironmentProd], production)
	value := boolValue(production)
	procEnableWindow.Call(handles[idProdBAN], value)
	procEnableWindow.Call(handles[idProdKey], value)
	procInvalidateRect.Call(handles[idProdBAN], 0, 1)
	procInvalidateRect.Call(handles[idProdKey], 0, 1)
}

func leaveSettings() {
	lockSettingsPage()
	if settingsWindow != 0 { procDestroyWindow.Call(settingsWindow) }
}

func addGroup(p uintptr, t string, x, y, w, h int, list *[]uintptr) uintptr {
	return addButtonStyle(p, t, x, y, w, h, 0, bsGroupBox, list)
}
func addPanelTitleGroup(p uintptr, t string, x, y, w, h, titleWidth int, list *[]uintptr) uintptr {
	group := addGroup(p, "", x, y, w, h, list)
	title := addStatic(p, t, x+8, y, titleWidth, 24, list)
	setStaticStyle(title, rgb(0, 0, 0), rgb(240, 240, 240), panelTitleBrush, false)
	return group
}
func addButton(p uintptr, t string, x, y, w, h, id int, list *[]uintptr) uintptr {
	return addButtonStyle(p, t, x, y, w, h, id, bsPushButton, list)
}
func addButtonStyle(p uintptr, t string, x, y, w, h, id int, style uintptr, list *[]uintptr) uintptr {
	return addControl("BUTTON", t, p, x, y, w, h, wsChild|wsVisible|wsTabStop|style, id, list)
}
func addStatic(p uintptr, t string, x, y, w, h int, list *[]uintptr) uintptr {
	return addControl("STATIC", t, p, x, y, w, h, wsChild|wsVisible, 0, list)
}
func addEdit(p uintptr, t string, x, y, w, h, id int, style uintptr, list *[]uintptr) uintptr {
	return addControl("EDIT", t, p, x, y, w, h, wsChild|wsVisible|wsTabStop|wsBorder|style, id, list)
}
func addListBox(p uintptr, x, y, w, h int, list *[]uintptr) uintptr {
	return addControl("LISTBOX", "", p, x, y, w, h, wsChild|wsVisible|wsVScroll|wsBorder|0x0100, 0, list)
}
func setEditLimit(id, maximum int) {
	if handle := handles[id]; handle != 0 { procSendMessageW.Call(handle, 0x00C5, uintptr(maximum), 0) }
}
func addControl(class, text string, parent uintptr, x, y, w, h int, style uintptr, id int, list *[]uintptr) uintptr {
	c := mustUTF16Ptr(class)
	t := mustUTF16Ptr(text)
	handle, _, err := procCreateWindowExW.Call(0, uintptr(unsafe.Pointer(c)), uintptr(unsafe.Pointer(t)),
		style, uintptr(x), uintptr(y), uintptr(w), uintptr(h), parent, uintptr(id), 0, 0)
	if handle == 0 { panic(fmt.Sprintf("create %s: %v", class, err)) }
	rememberRect(handle, x, y, w, h)
	if defaultFont != 0 { procSendMessageW.Call(handle, wmSetFont, defaultFont, 1) }
	if id != 0 { handles[id] = handle }
	if list != nil { *list = append(*list, handle) }
	return handle
}

func controlText(handle uintptr) string {
	if handle == 0 { return "" }
	length, _, _ := procGetWindowTextLengthW.Call(handle)
	buffer := make([]uint16, length+1)
	procGetWindowTextW.Call(handle, uintptr(unsafe.Pointer(&buffer[0])), length+1)
	return syscall.UTF16ToString(buffer)
}
func setControlText(handle uintptr, value string) {
	if handle == 0 { return }
	text := mustUTF16Ptr(value)
	procSetWindowTextW.Call(handle, uintptr(unsafe.Pointer(text)))
}
func setChecked(handle uintptr, value bool) {
	checked := uintptr(0)
	if value { checked = bstChecked }
	procSendMessageW.Call(handle, bmSetCheck, checked, 0)
}
func isChecked(handle uintptr) bool {
	checked, _, _ := procSendMessageW.Call(handle, bmGetCheck, 0, 0)
	return checked == bstChecked
}
