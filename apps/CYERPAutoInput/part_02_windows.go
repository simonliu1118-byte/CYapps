//go:build windows

package main

import (
	"time"
)

func createControls(parent uintptr) {
	createCtrl("STATIC", "V0.0.10 Build 8：Esc 可緊急停止；下拉直接依指定值選取；明細支援第 2 列測試。此版仍不儲存 ERP 單據。", WS_CHILD|WS_VISIBLE, 16, 10, 1250, 22, parent, 0)
	createCtrl("BUTTON", "尋找 ERP", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 16, 38, 90, 30, parent, 1001)
	createCtrl("BUTTON", "偵測狀態", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 112, 38, 95, 30, parent, 1010)
	createCtrl("BUTTON", "進入輸入狀態", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 213, 38, 115, 30, parent, 1011)
	createCtrl("BUTTON", "填入選取欄位（不儲存）", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 334, 38, 190, 30, parent, 1005)
	createCtrl("BUTTON", "讀取已勾選下拉選項", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 530, 38, 175, 30, parent, 1012)
	createCtrl("BUTTON", "儲存下拉設定", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 711, 38, 120, 30, parent, 1013)
	createCtrl("BUTTON", "欄位設定", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 837, 38, 105, 30, parent, 1016)
	createCtrl("BUTTON", "開啟設定檔", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 948, 38, 105, 30, parent, 1014)
	createCtrl("BUTTON", "除錯紀錄資料夾", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 1059, 38, 125, 30, parent, 1006)
	createCtrl("BUTTON", "掃描控制項", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 1190, 38, 110, 30, parent, 1002)

	createCtrl("BUTTON", "全部勾選", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 16, 74, 95, 28, parent, 1003)
	createCtrl("BUTTON", "全部取消", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 117, 74, 95, 28, parent, 1004)
	createCtrl("BUTTON", "切到交易資料", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 218, 74, 115, 28, parent, 1015)
	createCtrl("BUTTON", "切到送貨資料", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 339, 74, 115, 28, parent, 1008)
	createCtrl("BUTTON", "切到發票資料(一)", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 460, 74, 145, 28, parent, 1009)
	createCtrl("STATIC", "焦點單欄測試：", WS_CHILD|WS_VISIBLE, 620, 78, 95, 20, parent, 0)
	focusText = createCtrl("EDIT", "CYTEST123", WS_CHILD|WS_VISIBLE|WS_BORDER|ES_AUTOHSCROLL, 714, 74, 120, 26, parent, 1100)
	createCtrl("BUTTON", "3 秒後輸入焦點", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 840, 74, 150, 28, parent, 1007)
	statusHwnd = createCtrl("STATIC", "ERP：尚未偵測", WS_CHILD|WS_VISIBLE, 16, 108, 1530, 22, parent, 0)

	xCols := []int32{16, 398, 780, 1162}
	groupW := int32(372)
	groupY := int32(136)
	groupH := int32(404)
	createCtrl("BUTTON", "表頭", WS_CHILD|WS_VISIBLE|BS_GROUPBOX, xCols[0], groupY, groupW, groupH, parent, 0)
	createCtrl("BUTTON", "交易資料", WS_CHILD|WS_VISIBLE|BS_GROUPBOX, xCols[1], groupY, groupW, groupH, parent, 0)
	createCtrl("BUTTON", "送貨資料", WS_CHILD|WS_VISIBLE|BS_GROUPBOX, xCols[2], groupY, groupW, groupH, parent, 0)
	createCtrl("BUTTON", "發票資料（一）", WS_CHILD|WS_VISIBLE|BS_GROUPBOX, xCols[3], groupY, groupW, groupH, parent, 0)

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
	makeFieldColumn(parent, xCols[0]+10, 160, header, 350)
	makeFieldColumn(parent, xCols[1]+10, 160, trade, 350)
	makeFieldColumn(parent, xCols[2]+10, 160, ship, 350)
	makeFieldColumn(parent, xCols[3]+10, 160, inv, 350)

	createCtrl("BUTTON", "明細（第 1 列 + 第 2 列測試）", WS_CHILD|WS_VISIBLE|BS_GROUPBOX, 16, 548, 1518, 150, parent, 0)
	detail := []*Field{
		{Key: "item_code", Group: "明細", Label: "品號", Col: 0, Default: ""},
		{Key: "qty", Group: "明細", Label: "數量", Col: 1, Default: ""},
		{Key: "item_type", Group: "明細", Label: "類型", Col: 2, Default: ""},
		{Key: "gift_qty", Group: "明細", Label: "贈/備品量", Col: 3, Default: ""},
		{Key: "unit", Group: "明細", Label: "單位", Col: 4, Default: ""},
		{Key: "batch", Group: "明細", Label: "批號", Col: 5, Default: ""},
		{Key: "warehouse", Group: "明細", Label: "倉別", Col: 6, Default: ""},
		{Key: "unit_price", Group: "明細", Label: "單價", Col: 7, Default: ""},
		{Key: "discount_rate", Group: "明細", Label: "折扣率", Col: 8, Default: ""},
		{Key: "detail_note", Group: "明細", Label: "備註", Col: 9, Default: ""},
	}
	makeDetailFields(parent, 28, 576, detail)

	// Build 8 initially exposes the two most important second-row fields. This
	// is enough to validate the confirmed COPI08 sequence without expanding the
	// experimental UI too aggressively: Down -> re-click row 2 品號 -> Enter.
	detail2 := []*Field{
		{Key: "row2_item_code", Group: "明細", Label: "第2列 品號", Col: 0, Default: ""},
		{Key: "row2_qty", Group: "明細", Label: "第2列 數量", Col: 1, Default: ""},
	}
	makeDetailFields(parent, 28, 672, detail2)

	applySettingsToUI()
	createCtrl("STATIC", "除錯紀錄只寫入 logs；本機下拉設定存於 config\\settings.json。第2列測試流程：第一列完成→Down→重點第2列品號→Enter。仍不儲存 ERP。", WS_CHILD|WS_VISIBLE, 16, 708, 1480, 20, parent, 0)
}

func makeFieldColumn(parent uintptr, x, y int32, fs []*Field, totalWidth int32) {
	row := int32(0)
	inputWidth := totalWidth - 150
	for _, f := range fs {
		yy := y + row*24
		f.ApplyHwnd = createCtrl("BUTTON", "", WS_CHILD|WS_VISIBLE|BS_AUTOCHECKBOX, x, yy, 18, 20, parent, nextID)
		nextID++
		createCtrl("STATIC", f.Label, WS_CHILD|WS_VISIBLE|SS_LEFT, x+22, yy+2, 105, 18, parent, 0)
		if f.Kind == "bool" {
			f.ValueHwnd = createCtrl("BUTTON", "勾選", WS_CHILD|WS_VISIBLE|BS_AUTOCHECKBOX, x+130, yy, 85, 20, parent, nextID)
		} else {
			f.ValueHwnd = createCtrl("EDIT", f.Default, WS_CHILD|WS_VISIBLE|WS_BORDER|ES_AUTOHSCROLL|WS_TABSTOP, x+130, yy, inputWidth, 21, parent, nextID)
		}
		fieldByID[nextID] = f
		nextID++
		fields = append(fields, f)
		row++
	}
}

func makeDetailFields(parent uintptr, x, y int32, fs []*Field) {
	cellW := int32(296)
	for i, f := range fs {
		r := int32(i / 5)
		c := int32(i % 5)
		xx := x + c*cellW
		yy := y + r*48
		f.ApplyHwnd = createCtrl("BUTTON", "", WS_CHILD|WS_VISIBLE|BS_AUTOCHECKBOX, xx, yy, 18, 20, parent, nextID)
		nextID++
		createCtrl("STATIC", f.Label, WS_CHILD|WS_VISIBLE|SS_LEFT, xx+22, yy+2, 75, 18, parent, 0)
		f.ValueHwnd = createCtrl("EDIT", f.Default, WS_CHILD|WS_VISIBLE|WS_BORDER|ES_AUTOHSCROLL|WS_TABSTOP, xx+96, yy, 185, 21, parent, nextID)
		fieldByID[nextID] = f
		nextID++
		fields = append(fields, f)
	}
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
