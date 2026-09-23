//go:build windows

package main

import (
	"fmt"
	"time"
)

func createControls(parent uintptr) {
	resetUIRegistrationV12()
	setWindowText(parent, "CYERPAutoInput V0.0.10 Build 16 — SMART ERP 自動輸入工具")
	applyAppIconV12(parent)

	createUIControlV12("STATIC", "SMART ERP 自動輸入工具", WS_CHILD|WS_VISIBLE|SS_LEFT, 20, 12, 420, 26, parent, 0)
	createUIControlV12("STATIC", "V0.0.10 Build 16  ·  新增模式  ·  Esc 緊急停止  ·  目前仍不自動儲存 ERP 單據", WS_CHILD|WS_VISIBLE|SS_LEFT, 20, 39, 760, 22, parent, 0)

	modeStandardButtonV12 = createUIControlV12("BUTTON", "標準模式", WS_CHILD|WS_VISIBLE|bsAutoRadioButtonV12|wsGroupV12, 1090, 14, 112, 30, parent, 1201)
	modeAdvancedButtonV12 = createUIControlV12("BUTTON", "進階模式", WS_CHILD|WS_VISIBLE|bsAutoRadioButtonV12, 1208, 14, 112, 30, parent, 1202)
	settingsButton := createUIControlV12("BUTTON", "設定", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 1358, 12, 118, 34, parent, 1203)
	registerButtonActionV12(modeStandardButtonV12, func() { setUIModeV12(uiModeStandardV12) }, "")
	registerButtonActionV12(modeAdvancedButtonV12, func() { setUIModeV12(uiModeAdvancedV12) }, "")
	registerButtonActionV12(settingsButton, showSettingsWindowV12, "")

	createUIControlV12("BUTTON", "尋找 ERP", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 20, 70, 132, 34, parent, 1001)
	createUIControlV12("BUTTON", "偵測狀態", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 160, 70, 132, 34, parent, 1010)
	fillButton := createUIControlV12("BUTTON", "開始輸入 ERP", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON|bsOwnerDrawV12, 306, 67, 190, 40, parent, 1005)
	registerButtonActionV12(fillButton, nil, "primary")
	selectAllButton := createUIControlV12("BUTTON", "勾選目前欄位", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 514, 70, 142, 34, parent, 1210)
	clearAllButton := createUIControlV12("BUTTON", "全部取消", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 664, 70, 116, 34, parent, 1211)
	registerButtonActionV12(selectAllButton, func() { selectVisibleFieldsV12(true) }, "")
	registerButtonActionV12(clearAllButton, func() { selectVisibleFieldsV12(false) }, "")
	statusHwnd = createUIControlV12("STATIC", "ERP：尚未偵測", WS_CHILD|WS_VISIBLE|SS_LEFT, 804, 77, 670, 24, parent, 0)

	importBox := createUIControlV12("BUTTON", "訂單匯入", WS_CHILD|WS_VISIBLE|BS_GROUPBOX, 16, 112, 1518, 70, parent, 0)
	_ = importBox
	createUIControlV12("STATIC", "匯入來源", WS_CHILD|WS_VISIBLE|SS_LEFT, 34, 141, 84, 22, parent, 0)
	shopeeButton := createUIControlV12("BUTTON", "蝦皮", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON|bsOwnerDrawV12, 126, 132, 132, 36, parent, 1220)
	moButton := createUIControlV12("BUTTON", "MO店+", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON|bsOwnerDrawV12, 270, 132, 132, 36, parent, 1221)
	coupangButton := createUIControlV12("BUTTON", "酷澎", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON|bsOwnerDrawV12, 414, 132, 132, 36, parent, 1222)
	registerButtonActionV12(shopeeButton, func() { showImportPlaceholderV12("蝦皮") }, "shopee")
	registerButtonActionV12(moButton, func() { showImportPlaceholderV12("MO店+") }, "mo")
	registerButtonActionV12(coupangButton, func() { showImportPlaceholderV12("酷澎") }, "coupang")
	createUIControlV12("STATIC", "匯入按鈕在標準／進階模式都固定保留；格式解析後續逐一串接。", WS_CHILD|WS_VISIBLE|SS_LEFT, 572, 141, 720, 22, parent, 0)

	xCols := []int32{16, 398, 780, 1162}
	groupW := int32(372)
	groupY := int32(196)
	for i, name := range []string{"表頭", "交易資料", "送貨資料", "發票資料（一）"} {
		group := name
		key := group
		if name == "發票資料（一）" { key = "發票資料(一)" }
		h := createUIControlV12("BUTTON", group, WS_CHILD|WS_VISIBLE|BS_GROUPBOX, xCols[i], groupY, groupW, 398, parent, 0)
		registerGroupWidgetV12(key, h, xCols[i], groupY, groupW)
	}

	header := []*Field{
		{Key: "order_type", Group: "表頭", Label: "銷貨單別", Row: 0, Col: 0, Default: "", Kind: "lookup"},
		{Key: "order_date", Group: "表頭", Label: "單據日期", Row: 0, Col: 2, Default: "", Kind: "date"},
		{Key: "customer_code", Group: "表頭", Label: "客戶代號", Row: 1, Col: 1, Default: "", Kind: "lookup"},
	}
	trade := []*Field{
		{Key: "dept_code", Group: "交易資料", Label: "部門代號", Row: 0, Col: 0, Default: "", Kind: "lookup"},
		{Key: "currency", Group: "交易資料", Label: "幣別", Row: 0, Col: 1, Default: ""},
		{Key: "trade_note", Group: "交易資料", Label: "備註", Row: 0, Col: 2, Default: ""},
		{Key: "salesperson", Group: "交易資料", Label: "業務人員", Row: 1, Col: 0, Default: "", Kind: "lookup"},
		{Key: "exchange_rate", Group: "交易資料", Label: "匯率", Row: 1, Col: 1, Default: ""},
		{Key: "transmit_count", Group: "交易資料", Label: "傳送次數", Row: 1, Col: 2, Default: ""},
		{Key: "invoice_print", Group: "交易資料", Label: "發票列印", Row: 1, Col: 3, Default: ""},
		{Key: "receipt_salesperson", Group: "交易資料", Label: "收款業務員", Row: 2, Col: 0, Default: "", Kind: "lookup"},
		{Key: "employee_code", Group: "交易資料", Label: "員工代號", Row: 2, Col: 1, Default: "", Kind: "lookup"},
		{Key: "payment_terms", Group: "交易資料", Label: "付款條件", Row: 2, Col: 2, Default: ""},
	}
	ship := []*Field{
		{Key: "ship_name", Group: "送貨資料", Label: "送貨客戶全名", Row: 0, Col: 0, Default: ""},
		{Key: "ship_addr1", Group: "送貨資料", Label: "送貨地址(一)", Row: 1, Col: 0, Default: ""},
		{Key: "ship_addr2", Group: "送貨資料", Label: "送貨地址(二)", Row: 2, Col: 0, Default: ""},
		{Key: "contact", Group: "送貨資料", Label: "連絡人", Row: 3, Col: 0, Default: ""},
		{Key: "receiver", Group: "送貨資料", Label: "收貨人", Row: 3, Col: 1, Default: ""},
		{Key: "tel", Group: "送貨資料", Label: "TEL_NO", Row: 4, Col: 0, Default: ""},
		{Key: "fax", Group: "送貨資料", Label: "FAX_NO", Row: 4, Col: 1, Default: ""},
		{Key: "mobile", Group: "送貨資料", Label: "行動電話", Row: 4, Col: 2, Default: ""},
		{Key: "appoint_date", Group: "送貨資料", Label: "指定日期", Row: 5, Col: 0, Default: "", Kind: "date"},
		{Key: "delivery_slot", Group: "送貨資料", Label: "配送時段", Row: 5, Col: 1, Default: "", Kind: "combo"},
		{Key: "freight_type", Group: "送貨資料", Label: "貨運別", Row: 5, Col: 2, Default: "", Kind: "lookup"},
		{Key: "cod", Group: "送貨資料", Label: "代收貨款", Row: 6, Col: 0, Default: ""},
		{Key: "freight_fee", Group: "送貨資料", Label: "運費", Row: 6, Col: 1, Default: ""},
		{Key: "freight_file", Group: "送貨資料", Label: "產生貨運文字檔", Row: 6, Col: 2, Kind: "bool"},
	}
	inv := []*Field{
		{Key: "inv_date", Group: "發票資料(一)", Label: "發票日期", Row: 0, Col: 0, Default: "", Kind: "date"},
		{Key: "inv_time", Group: "發票資料(一)", Label: "發票開立時間", Row: 0, Col: 1, Default: ""},
		{Key: "inv_copies", Group: "發票資料(一)", Label: "發票聯數", Row: 0, Col: 2, Default: "", Kind: "combo"},
		{Key: "inv_no", Group: "發票資料(一)", Label: "發票號碼", Row: 1, Col: 0, Default: ""},
		{Key: "tax_type", Group: "發票資料(一)", Label: "課稅別", Row: 1, Col: 1, Default: "", Kind: "combo"},
		{Key: "customs", Group: "發票資料(一)", Label: "通關方式", Row: 1, Col: 2, Default: "", Kind: "combo"},
		{Key: "tax_id", Group: "發票資料(一)", Label: "統一編號", Row: 2, Col: 0, Default: ""},
		{Key: "tax_rate", Group: "發票資料(一)", Label: "營業稅率", Row: 2, Col: 1, Default: ""},
		{Key: "report_month", Group: "發票資料(一)", Label: "申報年月", Row: 2, Col: 2, Default: ""},
		{Key: "voided", Group: "發票資料(一)", Label: "發票作廢", Row: 2, Col: 3, Kind: "bool"},
		{Key: "inv_name", Group: "發票資料(一)", Label: "客戶全名", Row: 3, Col: 0, Default: ""},
		{Key: "card4", Group: "發票資料(一)", Label: "信用卡末四碼", Row: 3, Col: 1, Default: ""},
		{Key: "inv_addr1", Group: "發票資料(一)", Label: "發票地址(一)", Row: 4, Col: 0, Default: ""},
		{Key: "inv_addr2", Group: "發票資料(一)", Label: "發票地址(二)", Row: 5, Col: 0, Default: ""},
		{Key: "email", Group: "發票資料(一)", Label: "連絡人EMAIL", Row: 6, Col: 0, Default: ""},
	}
	makeFieldColumn(parent, xCols[0]+10, groupY+26, header, 350)
	makeFieldColumn(parent, xCols[1]+10, groupY+26, trade, 350)
	makeFieldColumn(parent, xCols[2]+10, groupY+26, ship, 350)
	makeFieldColumn(parent, xCols[3]+10, groupY+26, inv, 350)

	detailBoxV12 = createUIControlV12("BUTTON", "商品明細（ListView；勾選要輸入 ERP 的列）", WS_CHILD|WS_VISIBLE|BS_GROUPBOX, 16, detailBaseYV12, 1518, 310, parent, 0)
	makeDetailTable(parent, 30, detailBaseYV12+24, 8)
	footerV12 = createUIControlV12("STATIC", "明細：品號必填；單位有填時先 F2 選單位，再輸入數量。贈/備品量、庫別、單價有填才輸入；批號點選後續處理。", WS_CHILD|WS_VISIBLE|SS_LEFT, 16, detailBaseYV12+320, 1510, 22, parent, 0)
	setupBuild15UIV15(parent)
	setupBuild16LabelsV16(parent)

	applySettingsToUI()
	applyMainModeV12(loadUIModeV12())
}

func makeFieldColumn(parent uintptr, x, y int32, fs []*Field, totalWidth int32) {
	row := int32(0)
	inputWidth := totalWidth - 150
	for _, f := range fs {
		yy := y + row*26
		f.ApplyHwnd = createUIControlV12("BUTTON", "", WS_CHILD|WS_VISIBLE|BS_AUTOCHECKBOX, x, yy, 18, 22, parent, nextID)
		nextID++
		label := createUIControlV12("STATIC", f.Label, WS_CHILD|WS_VISIBLE|SS_LEFT, x+22, yy+2, 105, 20, parent, 0)
		if f.Kind == "bool" {
			f.ValueHwnd = createUIControlV12("BUTTON", "勾選", WS_CHILD|WS_VISIBLE|BS_AUTOCHECKBOX, x+130, yy, 85, 22, parent, nextID)
		} else {
			f.ValueHwnd = createUIControlV12("EDIT", f.Default, WS_CHILD|WS_VISIBLE|WS_BORDER|ES_AUTOHSCROLL|WS_TABSTOP, x+130, yy, inputWidth, 23, parent, nextID)
		}
		fieldByID[nextID] = f
		nextID++
		fields = append(fields, f)
		registerFieldWidgetV12(f.Key, f.Group, f.ApplyHwnd, label, f.ValueHwnd)
		row++
	}
}

type detailColumnDefV11 struct {
	key   string
	label string
	col   int
	width int32
	kind  string
}

func detailUIControlV12(class, text string, style uint32, x, y, w, h int32, parent uintptr, id uint16) uintptr {
	hwnd := createUIControlV12(class, text, style, x, y, w, h, parent, id)
	registerDetailWidgetV12(hwnd, x, y, w, h)
	return hwnd
}

func makeDetailTable(parent uintptr, x, y int32, rowCount int) {
	cols := []detailColumnDefV11{
		{key: "item_code", label: "品號", col: 0, width: 250},
		{key: "qty", label: "數量", col: 1, width: 125},
		{key: "gift_qty", label: "贈/備品量", col: 3, width: 150},
		{key: "unit", label: "單位*", col: 4, width: 135, kind: "deferred_click"},
		{key: "batch", label: "批號*", col: 5, width: 180, kind: "deferred_click"},
		{key: "warehouse", label: "庫別", col: 6, width: 155},
		{key: "unit_price", label: "單價", col: 7, width: 155},
	}

	detailUIControlV12("STATIC", "列", WS_CHILD|WS_VISIBLE|SS_LEFT, x, y, 28, 20, parent, 0)
	cx := x + 46
	for _, c := range cols {
		detailUIControlV12("STATIC", c.label, WS_CHILD|WS_VISIBLE|SS_LEFT, cx, y, c.width-8, 20, parent, 0)
		cx += c.width
	}
	detailUIControlV12("STATIC", "* 單位／批號後續改為點選", WS_CHILD|WS_VISIBLE|SS_LEFT, cx+8, y, 230, 20, parent, 0)

	for r := 0; r < rowCount; r++ {
		yy := y + 26 + int32(r)*29
		apply := detailUIControlV12("BUTTON", "", WS_CHILD|WS_VISIBLE|BS_AUTOCHECKBOX, x, yy, 18, 22, parent, nextID)
		nextID++
		detailUIControlV12("STATIC", fmt.Sprintf("%d", r+1), WS_CHILD|WS_VISIBLE|SS_LEFT, x+22, yy+2, 22, 20, parent, 0)
		cx = x + 46
		for _, c := range cols {
			f := &Field{
				Key:       fmt.Sprintf("detail_r%d_%s", r+1, c.key),
				Group:     "明細",
				Label:     fmt.Sprintf("第%d列 %s", r+1, c.label),
				Kind:      c.kind,
				ApplyHwnd: apply,
				Row:       r,
				Col:       c.col,
			}
			f.ValueHwnd = detailUIControlV12("EDIT", "", WS_CHILD|WS_VISIBLE|WS_BORDER|ES_AUTOHSCROLL|WS_TABSTOP, cx, yy, c.width-8, 23, parent, nextID)
			fieldByID[nextID] = f
			nextID++
			fields = append(fields, f)
			cx += c.width
		}
	}

	setupDetailListViewV14(parent, x, y)
}

func startEscapeWatcher() {
	go func() {
		for {
			if automationRunning.Load() && !stopRequested.Load() {
				r, _, _ := pGetAsyncKeyState.Call(VK_ESCAPE)
				if uint16(r)&0x8000 != 0 {
					stopRequested.Store(true)
					if stopLogged.CompareAndSwap(false, true) {
						logf("WARN", "automation stop requested by physical ESC key")
					}
				}
			}
			time.Sleep(20 * time.Millisecond)
		}
	}()
}

func beginAutomation(name string) {
	stopRequested.Store(false)
	stopLogged.Store(false)
	automationRunning.Store(true)
	logf("INFO", "automation begin name=%s", name)
}

func endAutomation(name string) {
	stopped := stopRequested.Load()
	automationRunning.Store(false)
	if stopped {
		setStatus("已停止：偵測到 Esc，所有自動操作已中止")
		logf("WARN", "automation end name=%s result=STOPPED_BY_ESC", name)
	} else {
		logf("INFO", "automation end name=%s", name)
	}
}

func isStopRequested() bool {
	return stopRequested.Load()
}

func interruptibleSleep(d time.Duration) bool {
	deadline := time.Now().Add(d)
	for time.Now().Before(deadline) {
		if isStopRequested() {
			return false
		}
		remain := time.Until(deadline)
		step := 20 * time.Millisecond
		if remain < step {
			step = remain
		}
		if step > 0 {
			time.Sleep(step)
		}
	}
	return !isStopRequested()
}
