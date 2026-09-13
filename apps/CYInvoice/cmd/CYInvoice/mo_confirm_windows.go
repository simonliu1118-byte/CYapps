//go:build windows

package main

import (
	"context"
	"errors"
	"fmt"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"syscall"
	"unsafe"

	"cyinvoice/internal/appdata"
	"cyinvoice/internal/coupangimport"
	"cyinvoice/internal/invoicing"
	"cyinvoice/internal/moimport"
)

const (
	moConfirmClass = "CYInvoiceMOImportConfirmWindow"

	idMOConfirmList       = 9101
	idMOConfirmBuyerName  = 9102
	idMOConfirmApplyName  = 9103
	idMOConfirmRetryName  = 9104
	idMOConfirmCancel     = 9105
	idMOConfirmIssue      = 9106

	wmMOConfirmStartLoad = 0x8101
	wmMOConfirmLoadPhase = 0x8102
	wmMOConfirmLoaded    = 0x8103
	pbsMarquee           = 0x00000008
	pbmSetMarquee        = 0x040A

	moConfirmWidth  = 1160
	moConfirmHeight = 610
)

type moConfirmEntry struct {
	Source     string
	Order      moimport.Order
	Lookup     invoicing.NameLookup
	LookupErr  error
	Selected   bool
	Finished   bool
	Status     string
}

type moConfirmLoadResult struct {
	Generation uint64
	Entries []moConfirmEntry
	Err error
}

var (
	moConfirmWindow       uintptr
	moConfirmList         uintptr
	moConfirmSummary      uintptr
	moConfirmBuyerHint    uintptr
	moConfirmEntries      []moConfirmEntry
	moConfirmActiveRow    = -1
	moConfirmIssuing      bool
	moConfirmFinalMessage string
	moConfirmProgress     uintptr
	moConfirmProgressText uintptr
	moConfirmPath         string
	moConfirmPassword     string
	moConfirmSource       string
	moConfirmGeneration   uint64
	moConfirmLoadMu       sync.Mutex
	moConfirmLoadedResult moConfirmLoadResult
	moConfirmRefreshing   bool
	moConfirmCallback     = syscall.NewCallback(moConfirmWindowProc)
)

func showPlatformImportConfirmation(source, path, password string, settings appdata.Settings) {
	moConfirmEntries = nil
	moConfirmPath = path
	moConfirmPassword = password
	moConfirmSource = source
	generation := atomic.AddUint64(&moConfirmGeneration, 1)
	moConfirmActiveRow = -1
	moConfirmIssuing = false
	moConfirmFinalMessage = ""

	instance, _, callErr := procGetModuleHandleW.Call(0)
	if instance == 0 {
		showError(fmt.Sprintf("建立匯入確認視窗失敗：%v", callErr))
		return
	}
	className := mustUTF16Ptr(moConfirmClass)
	cursor, _, _ := procLoadCursorW.Call(0, 32512)
	class := windowClassEx{
		Size: uint32(unsafe.Sizeof(windowClassEx{})),
		Style: csHRedraw | csVRedraw,
		WindowProc: moConfirmCallback,
		Instance: instance, Icon: loadApplicationIcon(instance), Cursor: cursor,
		Background: 16, ClassName: className, IconSmall: loadApplicationIcon(instance),
	}
	if registered, _, registerErr := procRegisterClassExW.Call(uintptr(unsafe.Pointer(&class))); registered == 0 {
		if errno, ok := registerErr.(syscall.Errno); !ok || errno != 1410 {
			showError(fmt.Sprintf("註冊匯入確認視窗失敗：%v", registerErr))
			return
		}
	}

	environment := "光貿測試環境"
	if settings.Environment == appdata.EnvironmentProduction { environment = "正式公司環境" }
	title := mustUTF16Ptr("CYInvoice｜" + source + " 開立發票匯入確認｜" + environment)
	moConfirmWindow, _, callErr = procCreateWindowExW.Call(
		0, uintptr(unsafe.Pointer(className)), uintptr(unsafe.Pointer(title)),
		0x00C00000|0x00080000, cwUseDefault, cwUseDefault,
		moConfirmWidth, moConfirmHeight, mainWindow, 0, instance, 0,
	)
	if moConfirmWindow == 0 {
		showError(fmt.Sprintf("建立匯入確認視窗失敗：%v", callErr))
		return
	}
	centerWindowOnParent(moConfirmWindow, mainWindow, moConfirmWidth, moConfirmHeight)
	procEnableWindow.Call(mainWindow, 0)
	procShowWindow.Call(moConfirmWindow, swShow)
	procUpdateWindow.Call(moConfirmWindow)
	procPostMessageW.Call(moConfirmWindow, wmMOConfirmStartLoad, uintptr(generation), 0)

	var msg message
	for {
		window := moConfirmWindow
		if window == 0 { break }
		alive, _, _ := procIsWindow.Call(window)
		if alive == 0 { break }
		result, _, messageErr := procGetMessageW.Call(uintptr(unsafe.Pointer(&msg)), 0, 0, 0)
		if int32(result) == -1 {
			moConfirmMessage("讀取匯入確認視窗訊息失敗："+messageErr.Error(), 0x10)
			break
		}
		if result == 0 {
			procPostQuitMessage.Call(0)
			break
		}
		procTranslateMessage.Call(uintptr(unsafe.Pointer(&msg)))
		procDispatchMessageW.Call(uintptr(unsafe.Pointer(&msg)))
	}
	procEnableWindow.Call(mainWindow, 1)
	procSetFocus.Call(mainWindow)
	refreshRecords()
	if moConfirmFinalMessage != "" {
		showInfo(moConfirmFinalMessage)
	}
}

func prepareMOConfirmEntries(source string, orders []moimport.Order) []moConfirmEntry {
	entries := make([]moConfirmEntry, len(orders))
	cache := make(map[string]moConfirmEntry)
	for index, order := range orders {
		entry := moConfirmEntry{Source: source, Order: order, Selected: true, Status: "可開立"}
		if !isMOCompanyOrder(order) {
			entry.Order.BuyerName = strings.TrimSpace(order.BuyerName)
			if entry.Order.BuyerName == "" { entry.Order.BuyerName = "消費者" }
			entries[index] = entry
			continue
		}

		ban := strings.TrimSpace(order.BuyerBAN)
		if cached, found := cache[ban]; found {
			entry.Lookup = cached.Lookup
			entry.LookupErr = cached.LookupErr
		} else {
			lookup, err := invoiceService.LookupBuyerName(context.Background(), ban)
			entry.Lookup, entry.LookupErr = lookup, err
			cache[ban] = moConfirmEntry{Lookup: lookup, LookupErr: err}
		}
		switch {
		case entry.LookupErr != nil:
			entry.Selected = false
			entry.Status = "統編查詢異常：" + shortMOConfirmError(entry.LookupErr)
			entry.Order.BuyerName = ""
		case entry.Lookup.Local:
			entry.Order.BuyerName = entry.Lookup.Name
			entry.Status = "本機記憶名稱"
		case strings.TrimSpace(entry.Lookup.APIName) != "":
			entry.Order.BuyerName = entry.Lookup.APIName
			entry.Status = "光貿查詢完成"
		default:
			entry.Order.BuyerName = ""
			entry.Status = "請輸入買方名稱"
		}
		entries[index] = entry
	}
	return entries
}

func moConfirmWindowProc(window uintptr, msg uint32, wParam uintptr, lParam unsafe.Pointer) uintptr {
	switch msg {
	case wmCreate:
		moConfirmWindow = window
		buildMOConfirmControls(window)
		return 0
	case wmMOConfirmStartLoad:
		startMOConfirmLoad(uint64(wParam))
		return 0
	case wmMOConfirmLoadPhase:
		if uint64(wParam) == atomic.LoadUint64(&moConfirmGeneration) {
			setControlText(moConfirmProgressText, "正在查詢公司統編與準備逐張確認資料…")
		}
		return 0
	case wmMOConfirmLoaded:
		finishMOConfirmLoad(uint64(wParam))
		return 0
	case wmCommand:
		handleMOConfirmCommand(int(wParam & 0xffff))
		return 0
	case wmNotify:
		if customResult, handled := handleMOConfirmCustomDraw(lParam); handled {
			return customResult
		}
		if lParam == nil { return 0 }
		header := (*nmHdr)(lParam)
		if header.WindowFrom == moConfirmList && int32(header.Code) == lvnItemChanged {
			handleMOConfirmListStateChange((*nmItemActivate)(lParam))
			return 0
		}
		if header.WindowFrom == moConfirmList && int32(header.Code) == nmClick {
			handleMOConfirmListClick((*nmItemActivate)(lParam))
			return 0
		}
	case wmCtlColorStatic, wmCtlColorBtn:
		return handlePlainControlColor(wParam)
	case wmClose:
		if !moConfirmIssuing {
			procDestroyWindow.Call(window)
		}
		return 0
	case wmDestroy:
		moConfirmWindow = 0
		moConfirmList = 0
		moConfirmProgress = 0
		moConfirmProgressText = 0
		return 0
	default:
		result, _, _ := procDefWindowProcW.Call(window, uintptr(msg), wParam, uintptr(lParam))
		return result
	}
	result, _, _ := procDefWindowProcW.Call(window, uintptr(msg), wParam, uintptr(lParam))
	return result
}

func buildMOConfirmControls(parent uintptr) {
	heading := addStatic(parent, moConfirmSource+" 開立發票匯入確認", 22, 16, 262, 30, nil)
	procSendMessageW.Call(heading, wmSetFont, boldFont, 1)
	note := addStatic(parent, "請先逐張核對，取消或未勾選的訂單不會送出。", 294, 18, 570, 28, nil)
	procSendMessageW.Call(note, wmSetFont, smallFont, 1)

	moConfirmList = addListView(parent, 22, 55, 1114, 306, idMOConfirmList, nil)
	procSendMessageW.Call(moConfirmList, wmSetFont, contentFont, 1)
	procSendMessageW.Call(moConfirmList, lvmSetExtendedListStyle, 0, lvsExGridLines|lvsExFullRowSelect|lvsExDoubleBuffer|lvsExCheckboxes)
	addListViewColumns(moConfirmList, []struct{ Title string; Width int; Right bool }{
		{"開立", 54, false}, {"訂單編號", 142, false}, {"統一編號", 88, false},
		{"買方名稱", 170, false}, {"課稅別", 64, false}, {"應稅銷售額", 98, true},
		{"營業稅額", 82, true}, {"發票總額", 94, true}, {"項目數", 60, true},
		{"檢查狀態", 238, false},
	})

	addStatic(parent, "買方名稱", 22, 381, 80, 26, nil)
	addEdit(parent, "", 104, 379, 420, 27, idMOConfirmBuyerName, esAutoHScroll, nil)
	addButton(parent, "套用名稱", 536, 378, 104, 30, idMOConfirmApplyName, nil)
	addButton(parent, "重新查詢統編", 650, 378, 130, 30, idMOConfirmRetryName, nil)
	moConfirmBuyerHint = addStatic(parent, "", 796, 381, 340, 28, nil)
	procSendMessageW.Call(moConfirmBuyerHint, wmSetFont, smallFont, 1)

	moConfirmSummary = addStatic(parent, "", 22, 429, 1114, 30, nil)
	procSendMessageW.Call(moConfirmSummary, wmSetFont, boldFont, 1)
	moConfirmProgressText = addStatic(parent, "正在唯讀解析 "+moConfirmSource+" Excel…", 22, 468, 330, 24, nil)
	procSendMessageW.Call(moConfirmProgressText, wmSetFont, smallFont, 1)
	moConfirmProgress = addControl("msctls_progress32", "", parent, 360, 470, 376, 18, wsChild|wsVisible|pbsMarquee, 0, nil)
	procSendMessageW.Call(moConfirmProgress, pbmSetMarquee, 1, 45)

	addButton(parent, "取消匯入", 800, 510, 145, 38, idMOConfirmCancel, nil)
	addButtonStyle(parent, "確認開立", 958, 510, 178, 38, idMOConfirmIssue, bsDefaultPushButton, nil)
	refreshMOConfirmList()
	loadMOConfirmBuyerEditor()
	setMOConfirmControlsEnabled(false)
	procEnableWindow.Call(handles[idMOConfirmCancel], 1)
}

func startMOConfirmLoad(generation uint64) {
	path, password, source, window := moConfirmPath, moConfirmPassword, moConfirmSource, moConfirmWindow
	go func() {
		orders, err := readPlatformImport(source, path, password)
		if err == nil {
			procPostMessageW.Call(window, wmMOConfirmLoadPhase, uintptr(generation), 0)
		}
		var entries []moConfirmEntry
		if err == nil { entries = prepareMOConfirmEntries(source, orders) }
		moConfirmLoadMu.Lock()
		moConfirmLoadedResult = moConfirmLoadResult{Generation: generation, Entries: entries, Err: err}
		moConfirmLoadMu.Unlock()
		procPostMessageW.Call(window, wmMOConfirmLoaded, uintptr(generation), 0)
	}()
}

func readPlatformImport(source, path, password string) ([]moimport.Order, error) {
	switch source {
	case appdata.SourceMO:
		return moimport.Read(path, password)
	case appdata.SourceCoupang:
		orders, err := coupangimport.Read(path)
		if err != nil { return nil, err }
		result := make([]moimport.Order, len(orders))
		for index, order := range orders {
			result[index] = moimport.Order{
				OrderID: order.OrderID, BuyerBAN: order.BuyerBAN, BuyerName: order.BuyerName,
				Items: order.Items, TotalAmount: order.TotalAmount, Carrier: invoicing.DeliveryPaper,
			}
		}
		return result, nil
	case appdata.SourceERP:
		return nil, errors.New("鼎新 ERP 原始銷貨單尚缺實際檔案樣本與欄位對照，目前未讀取內容，也不會送出任何發票")
	default:
		return nil, fmt.Errorf("不支援的匯入來源：%s", source)
	}
}

func finishMOConfirmLoad(generation uint64) {
	if generation != atomic.LoadUint64(&moConfirmGeneration) { return }
	moConfirmLoadMu.Lock()
	result := moConfirmLoadedResult
	moConfirmLoadMu.Unlock()
	if result.Generation != generation { return }
	procSendMessageW.Call(moConfirmProgress, pbmSetMarquee, 0, 0)
	procShowWindow.Call(moConfirmProgress, swHide)
	if result.Err != nil {
		setControlText(moConfirmProgressText, "匯入失敗："+shortMOConfirmError(result.Err))
		if appLogger != nil { appLogger.Errorf("read %s workbook: %v", moConfirmSource, result.Err) }
		moConfirmMessage(result.Err.Error(), 0x10)
		return
	}
	if len(result.Entries) == 0 {
		setControlText(moConfirmProgressText, "Excel 沒有可匯入的訂單")
		return
	}
	moConfirmEntries = result.Entries
	moConfirmActiveRow = 0
	setControlText(moConfirmProgressText, "")
	procShowWindow.Call(moConfirmProgressText, swHide)
	setMOConfirmControlsEnabled(true)
	refreshMOConfirmList()
	loadMOConfirmBuyerEditor()
}

func handleMOConfirmCommand(id int) {
	if moConfirmIssuing { return }
	switch id {
	case idMOConfirmApplyName:
		applyMOConfirmBuyerName(true)
	case idMOConfirmRetryName:
		retryMOConfirmBuyerLookup()
	case idMOConfirmCancel:
		procDestroyWindow.Call(moConfirmWindow)
	case idMOConfirmIssue:
		issueMOConfirmSelection()
	}
}

func handleMOConfirmListClick(activate *nmItemActivate) {
	if activate == nil || moConfirmIssuing || moConfirmRefreshing { return }
	row := int(activate.Item)
	if row < 0 || row >= len(moConfirmEntries) { return }
	moConfirmActiveRow = row
	loadMOConfirmBuyerEditor()
}

func handleMOConfirmListStateChange(change *nmItemActivate) {
	if change == nil || moConfirmIssuing || moConfirmRefreshing || change.Changed&lvifState == 0 { return }
	row := int(change.Item)
	if row < 0 || row >= len(moConfirmEntries) { return }
	if (change.NewState^change.OldState)&lvisStateImageMask == 0 { return }
	allowed := !moConfirmEntries[row].Finished && moConfirmEntries[row].LookupErr == nil
	checked := change.NewState&lvisStateImageMask == 0x2000
	moConfirmEntries[row].Selected = allowed && checked
	if !allowed && checked { setListViewChecked(moConfirmList, row, false) }
	refreshMOConfirmSummary()
}

func loadMOConfirmBuyerEditor() {
	if moConfirmActiveRow < 0 || moConfirmActiveRow >= len(moConfirmEntries) {
		setControlText(handles[idMOConfirmBuyerName], "")
		procEnableWindow.Call(handles[idMOConfirmBuyerName], 0)
		procEnableWindow.Call(handles[idMOConfirmApplyName], 0)
		procEnableWindow.Call(handles[idMOConfirmRetryName], 0)
		return
	}
	entry := moConfirmEntries[moConfirmActiveRow]
	setControlText(handles[idMOConfirmBuyerName], entry.Order.BuyerName)
	manual := isMOCompanyOrder(entry.Order) && entry.LookupErr == nil &&
		entry.Lookup.LookupSucceeded && strings.TrimSpace(entry.Lookup.APIName) == "" && !entry.Finished
	procEnableWindow.Call(handles[idMOConfirmBuyerName], boolValue(manual))
	procEnableWindow.Call(handles[idMOConfirmApplyName], boolValue(manual))
	procEnableWindow.Call(handles[idMOConfirmRetryName], boolValue(isMOCompanyOrder(entry.Order) && !entry.Finished))
}

func applyMOConfirmBuyerName(showMessage bool) bool {
	row := moConfirmActiveRow
	if row < 0 || row >= len(moConfirmEntries) { return true }
	entry := &moConfirmEntries[row]
	manual := isMOCompanyOrder(entry.Order) && entry.LookupErr == nil &&
		entry.Lookup.LookupSucceeded && strings.TrimSpace(entry.Lookup.APIName) == "" && !entry.Finished
	if !manual { return true }
	name := strings.TrimSpace(controlText(handles[idMOConfirmBuyerName]))
	if name == "" {
		if showMessage { moConfirmMessage("請輸入買方名稱。", 0x30) }
		return false
	}
	ban := strings.TrimSpace(entry.Order.BuyerBAN)
	for index := range moConfirmEntries {
		other := &moConfirmEntries[index]
		if strings.TrimSpace(other.Order.BuyerBAN) != ban || other.Finished ||
			other.LookupErr != nil || !other.Lookup.LookupSucceeded ||
			strings.TrimSpace(other.Lookup.APIName) != "" {
			continue
		}
		other.Order.BuyerName = name
		other.Status = "人工名稱待成功後記憶"
	}
	refreshMOConfirmList()
	if showMessage { moConfirmMessage("已套用到本批次相同統編的訂單；只有成功開立後才會記憶。", 0x40) }
	return true
}

func retryMOConfirmBuyerLookup() {
	row := moConfirmActiveRow
	if row < 0 || row >= len(moConfirmEntries) { return }
	entry := &moConfirmEntries[row]
	if !isMOCompanyOrder(entry.Order) || entry.Finished { return }
	ban := strings.TrimSpace(entry.Order.BuyerBAN)
	setControlText(moConfirmBuyerHint, "正在重新查詢 "+ban+"…")
	procUpdateWindow.Call(moConfirmWindow)
	lookup, err := invoiceService.LookupBuyerName(context.Background(), ban)
	for index := range moConfirmEntries {
		other := &moConfirmEntries[index]
		if strings.TrimSpace(other.Order.BuyerBAN) != ban || other.Finished { continue }
		other.Lookup, other.LookupErr = lookup, err
		switch {
		case err != nil:
			other.Selected = false
			other.Order.BuyerName = ""
			other.Status = "統編查詢異常：" + shortMOConfirmError(err)
		case lookup.Local:
			other.Order.BuyerName = lookup.Name
			other.Status = "本機記憶名稱"
		case strings.TrimSpace(lookup.APIName) != "":
			other.Order.BuyerName = lookup.APIName
			other.Status = "光貿查詢完成"
		default:
			other.Order.BuyerName = ""
			other.Status = "請輸入買方名稱"
		}
	}
	setControlText(moConfirmBuyerHint, "")
	refreshMOConfirmList()
	loadMOConfirmBuyerEditor()
}

func issueMOConfirmSelection() {
	if !applyMOConfirmBuyerName(false) { moConfirmMessage("請先輸入並套用買方名稱。", 0x30); return }
	selected := make([]int, 0)
	for index := range moConfirmEntries {
		entry := &moConfirmEntries[index]
		if !entry.Selected || entry.Finished { continue }
		if entry.LookupErr != nil {
			moConfirmActiveRow = index
			loadMOConfirmBuyerEditor()
			moConfirmMessage("仍有統編查詢異常，請重新查詢或取消勾選該張發票。", 0x30)
			return
		}
		if isMOCompanyOrder(entry.Order) && strings.TrimSpace(entry.Order.BuyerName) == "" {
			moConfirmActiveRow = index
			selectListViewRow(moConfirmList, index)
			loadMOConfirmBuyerEditor()
			moConfirmMessage("第 "+strconv.Itoa(index+1)+" 張公司發票尚未輸入買方名稱。", 0x30)
			return
		}
		selected = append(selected, index)
	}
	if len(selected) == 0 {
		moConfirmMessage("目前沒有勾選要開立的發票。", 0x30)
		return
	}

	moConfirmIssuing = true
	setMOConfirmControlsEnabled(false)
	procShowWindow.Call(moConfirmProgressText, swShow)
	procShowWindow.Call(moConfirmProgress, swShow)
	procSendMessageW.Call(moConfirmProgress, pbmSetMarquee, 1, 45)
	success, failed := 0, 0
	for position, index := range selected {
		entry := &moConfirmEntries[index]
		entry.Status = fmt.Sprintf("開立中（%d/%d）", position+1, len(selected))
		setControlText(moConfirmProgressText, fmt.Sprintf("正在開立第 %d/%d 張發票…", position+1, len(selected)))
		refreshMOConfirmList()
		procUpdateWindow.Call(moConfirmWindow)

		result, err := issuePlatformConfirmEntry(*entry)
		entry.Selected = false
		if err == nil && result.Opened {
			entry.Finished = true
			entry.Status = "成功：" + result.Record.InvoiceNumber
			success++
			if appLogger != nil {
				appLogger.Infof("%s invoice opened order=%s invoice=%s", entry.Source, entry.Order.OrderID, result.Record.InvoiceNumber)
			}
			continue
		}
		failed++
		if result.Unknown {
			entry.Finished = true
			entry.Status = "結果不明（禁止重送）"
		} else {
			state := result.Record.InvoiceState
			if state == "" { state = "已擋下" }
			entry.Status = state + "：" + shortMOConfirmError(err)
		}
		if appLogger != nil {
			appLogger.Errorf("%s issue order=%s state=%s: %v", entry.Source, entry.Order.OrderID, entry.Status, err)
		}
	}
	refreshRecords()
	procSendMessageW.Call(moConfirmProgress, pbmSetMarquee, 0, 0)
	procShowWindow.Call(moConfirmProgress, swHide)
	procShowWindow.Call(moConfirmProgressText, swHide)
	refreshMOConfirmList()
	if failed == 0 && !hasMOConfirmBlockingResult() {
		moConfirmFinalMessage = fmt.Sprintf("%s 批次開立完成\n\n成功：%d 張\n未勾選的訂單未送出。", moConfirmSource, success)
		procDestroyWindow.Call(moConfirmWindow)
		return
	}
	moConfirmIssuing = false
	setMOConfirmControlsEnabled(true)
	loadMOConfirmBuyerEditor()
	moConfirmMessage(fmt.Sprintf("批次處理完成。\n\n成功：%d 張\n未成功：%d 張\n\n確認視窗會保留，請依每列狀態處理；結果不明不可重送。", success, failed), 0x30)
}

func issuePlatformConfirmEntry(entry moConfirmEntry) (invoicing.IssueResult, error) {
	switch entry.Source {
	case appdata.SourceMO:
		return invoiceService.IssueMOWithLookup(context.Background(), entry.Order, entry.Lookup)
	case appdata.SourceCoupang:
		order := coupangimport.Order{
			OrderID: entry.Order.OrderID, BuyerBAN: entry.Order.BuyerBAN,
			BuyerName: entry.Order.BuyerName, Items: entry.Order.Items,
			TotalAmount: entry.Order.TotalAmount,
		}
		return invoiceService.IssueCoupangWithLookup(context.Background(), order, entry.Lookup)
	default:
		return invoicing.IssueResult{}, fmt.Errorf("%s 尚未完成欄位解析，已擋下且未送出", entry.Source)
	}
}

func hasMOConfirmBlockingResult() bool {
	for _, entry := range moConfirmEntries {
		status := entry.Status
		if strings.Contains(status, "結果不明") ||
			strings.Contains(status, "異常") ||
			strings.Contains(status, "失敗") ||
			strings.Contains(status, "擋下") {
			return true
		}
	}
	return false
}

func setMOConfirmControlsEnabled(enabled bool) {
	value := boolValue(enabled)
	for _, id := range []int{idMOConfirmList, idMOConfirmCancel, idMOConfirmIssue} {
		procEnableWindow.Call(handles[id], value)
	}
	if enabled {
		loadMOConfirmBuyerEditor()
	} else {
		for _, id := range []int{idMOConfirmBuyerName, idMOConfirmApplyName, idMOConfirmRetryName} {
			procEnableWindow.Call(handles[id], 0)
		}
	}
}

func refreshMOConfirmList() {
	if moConfirmList == 0 { return }
	moConfirmRefreshing = true
	defer func() { moConfirmRefreshing = false }()
	clearListView(moConfirmList)
	for index, entry := range moConfirmEntries {
		company := isMOCompanyOrder(entry.Order)
		sales, tax, total, err := appdata.CalculateInvoiceTotals(entry.Order.Items, company, false)
		if err != nil {
			sales, tax, total = 0, 0, entry.Order.TotalAmount
			if entry.Status == "可開立" { entry.Status = "金額檢查失敗" }
		}
		if !company {
			sales, tax, total = entry.Order.TotalAmount, 0, entry.Order.TotalAmount
		}
		ban := strings.TrimSpace(entry.Order.BuyerBAN)
		if !company { ban = "" }
		taxType := "應稅"
		addListViewRow(moConfirmList, index, []string{
			"", entry.Order.OrderID, ban, entry.Order.BuyerName, taxType,
			formatMOConfirmMoney(sales), formatMOConfirmMoney(tax), formatMOConfirmMoney(total),
			strconv.Itoa(len(entry.Order.Items)), entry.Status,
		})
		setListViewChecked(moConfirmList, index, entry.Selected && !entry.Finished && entry.LookupErr == nil)
	}
	refreshMOConfirmSummary()
	if moConfirmActiveRow >= 0 && moConfirmActiveRow < len(moConfirmEntries) {
		selectListViewRow(moConfirmList, moConfirmActiveRow)
	}
	procInvalidateRect.Call(moConfirmWindow, 0, 1)
}

func refreshMOConfirmSummary() {
	selectedCount := 0
	selectedTotal := int64(0)
	for _, entry := range moConfirmEntries {
		if entry.Selected && !entry.Finished {
			selectedCount++
			selectedTotal += entry.Order.TotalAmount
		}
	}
	setControlText(moConfirmSummary, fmt.Sprintf(
		"訂單共 %d 張　｜　已選擇 %d 張　｜　選擇總額 $%s",
		len(moConfirmEntries), selectedCount, formatMOConfirmMoney(selectedTotal),
	))
	setControlText(handles[idMOConfirmIssue], fmt.Sprintf("確認開立（%d 張）", selectedCount))
}

func handleMOConfirmCustomDraw(lParam unsafe.Pointer) (uintptr, bool) {
	if lParam == nil || moConfirmList == 0 { return 0, false }
	draw := (*nmListViewCustomDraw)(lParam)
	if draw.Draw.Header.WindowFrom != moConfirmList || int32(draw.Draw.Header.Code) != nmCustomDraw {
		return 0, false
	}
	switch draw.Draw.DrawStage {
	case cddsPrePaint:
		return cdrfNotifyItemDraw, true
	case cddsItemPrePaint:
		row := int(draw.Draw.ItemSpec)
		applyZebraBackground(draw)
		if row >= 0 && row < len(moConfirmEntries) {
			status := moConfirmEntries[row].Status
			switch {
			case strings.Contains(status, "成功"):
				draw.TextColor = rgb(0, 150, 55)
			case strings.Contains(status, "異常") || strings.Contains(status, "失敗") || strings.Contains(status, "擋下"):
				draw.TextColor = rgb(200, 0, 0)
			case strings.Contains(status, "請輸入") || strings.Contains(status, "結果不明"):
				draw.TextColor = rgb(205, 112, 0)
			}
		}
		return cdrfNotifySubItemDraw | cdrfNewFont, true
	case cddsItemPrePaint | cddsSubItem:
		applyZebraBackground(draw)
		return cdrfNewFont, true
	default:
		return 0, true
	}
}

func isMOCompanyOrder(order moimport.Order) bool {
	ban := strings.TrimSpace(order.BuyerBAN)
	return ban != "" && ban != "0000000000"
}

func formatMOConfirmMoney(value int64) string {
	sign := ""
	if value < 0 { sign, value = "-", -value }
	digits := strconv.FormatInt(value, 10)
	for index := len(digits) - 3; index > 0; index -= 3 {
		digits = digits[:index] + "," + digits[index:]
	}
	return sign + digits
}

func shortMOConfirmError(err error) string {
	if err == nil { return "未知錯誤" }
	value := strings.TrimSpace(err.Error())
	runes := []rune(value)
	if len(runes) > 32 { value = string(runes[:32]) + "…" }
	return value
}

func moConfirmMessage(text string, icon uintptr) {
	value := mustUTF16Ptr(text)
	title := mustUTF16Ptr("CYInvoice｜MO店+ 匯入確認")
	parent := moConfirmWindow
	if parent == 0 { parent = mainWindow }
	procMessageBoxW.Call(parent, uintptr(unsafe.Pointer(value)), uintptr(unsafe.Pointer(title)), icon)
}
