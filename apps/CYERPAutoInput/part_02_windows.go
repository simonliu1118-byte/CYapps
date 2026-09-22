//go:build windows

package main

import (
	"fmt"
	"time"
)

func createControls(parent uintptr) {
	setWindowText(parent, "CYERPAutoInput V0.0.10 Build 11 — SMART ERP 自動輸入工具（不儲存）")
	pSetWindowPos.Call(parent, HWND_TOP, 0, 0, 1580, 900, SWP_NOMOVE|SWP_SHOWWINDOW)
	createCtrl("STATIC", "V0.0.10 Build 11：新增單據輸入測試；Esc 可緊急停止；明細支援多列。此版仍不儲存 ERP 單據。", WS_CHILD|WS_VISIBLE, 16, 10, 1450, 22, parent, 0)

	createCtrl("BUTTON", "尋找 ERP", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 16, 36, 90, 30, parent, 1001)
	createCtrl("BUTTON", "偵測狀態", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 112, 36, 95, 30, parent, 1010)
	createCtrl("BUTTON", "進入輸入狀態", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 213, 36, 115, 30, parent, 1011)
	createCtrl("BUTTON", "填入選取欄位（不儲存）", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 334, 36, 190, 30, parent, 1005)
	createCtrl("BUTTON", "全部勾選", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 530, 36, 95, 30, parent, 1003)
	createCtrl("BUTTON", "全部取消", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 631, 36, 95, 30, parent, 1004)
	createCtrl("BUTTON", "儲存下拉設定", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 732, 36, 120, 30, parent, 1013)
	createCtrl("BUTTON", "欄位設定", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 858, 36, 105, 30, parent, 1016)
	createCtrl("BUTTON", "開啟設定檔", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 969, 36, 105, 30, parent, 1014)
	createCtrl("BUTTON", "除錯紀錄資料夾", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 1080, 36, 125, 30, parent, 1006)
	createCtrl("BUTTON", "掃描控制項", WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON, 1211, 36, 110, 30, parent, 1002)

	statusHwnd = createCtrl("STATIC", "ERP：尚未偵測", WS_CHILD|WS_VISIBLE, 16, 74, 1530, 22, parent, 0)

	xCols := []int32{16, 398, 780, 1162}
	groupW := int32(372)
	groupY := int32(100)
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
	makeFieldColumn(parent, xCols[0]+10, 124, header, 350)
	makeFieldColumn(parent, xCols[1]+10, 124, trade, 350)
	makeFieldColumn(parent, xCols[2]+10, 124, ship, 350)
	makeFieldColumn(parent, xCols[3]+10, 124, inv, 350)

	createCtrl("BUTTON", "商品明細（勾選列；空白欄位不輸入）", WS_CHILD|WS_VISIBLE|BS_GROUPBOX, 16, 512, 1518, 310, parent, 0)
	makeDetailTable(parent, 30, 536, 8)

	applySettingsToUI()
	createCtrl("STATIC", "明細規則：每列先輸入品號；其餘空白欄位保留 ERP 自動帶值。單位、批號 Build 11 只保留欄位，後續改成實際點選。仍不儲存 ERP。", WS_CHILD|WS_VISIBLE, 16, 832, 1510, 20, parent, 0)
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

type detailColumnDefV11 struct {
	key   string
	label string
	col   int
	width int32
	kind  string
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

	createCtrl("STATIC", "列", WS_CHILD|WS_VISIBLE|SS_LEFT, x, y, 28, 18, parent, 0)
	cx := x + 46
	for _, c := range cols {
		createCtrl("STATIC", c.label, WS_CHILD|WS_VISIBLE|SS_LEFT, cx, y, c.width-8, 18, parent, 0)
		cx += c.width
	}
	createCtrl("STATIC", "* 單位／批號後續改為點選", WS_CHILD|WS_VISIBLE|SS_LEFT, cx+8, y, 230, 18, parent, 0)

	for r := 0; r < rowCount; r++ {
		yy := y + 24 + int32(r)*29
		apply := createCtrl("BUTTON", "", WS_CHILD|WS_VISIBLE|BS_AUTOCHECKBOX, x, yy, 18, 20, parent, nextID)
		nextID++
		createCtrl("STATIC", fmt.Sprintf("%d", r+1), WS_CHILD|WS_VISIBLE|SS_LEFT, x+22, yy+2, 22, 18, parent, 0)
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
			f.ValueHwnd = createCtrl("EDIT", "", WS_CHILD|WS_VISIBLE|WS_BORDER|ES_AUTOHSCROLL|WS_TABSTOP, cx, yy, c.width-8, 21, parent, nextID)
			fieldByID[nextID] = f
			nextID++
			fields = append(fields, f)
			cx += c.width
		}
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
