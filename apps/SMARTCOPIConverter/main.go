//go:build windows

package main

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

var version = "dev"

const (
	WS_OVERLAPPEDWINDOW  = 0x00CF0000
	WS_VISIBLE           = 0x10000000
	WS_CHILD             = 0x40000000
	WS_BORDER            = 0x00800000
	WS_TABSTOP           = 0x00010000
	WS_VSCROLL           = 0x00200000
	WS_DISABLED          = 0x08000000
	BS_PUSHBUTTON        = 0x00000000
	BS_AUTOCHECKBOX      = 0x00000003
	LBS_NOTIFY           = 0x0001
	LBS_NOINTEGRALHEIGHT = 0x0100
	SS_LEFT              = 0x00000000
	CW_USEDEFAULT        = 0x80000000
	SW_SHOW              = 5
	WM_DESTROY           = 0x0002
	WM_COMMAND           = 0x0111
	WM_CLOSE             = 0x0010
	WM_SETFONT           = 0x0030
	WM_SETREDRAW         = 0x000B
	WM_BATCH_EVENT       = 0x8001
	BM_GETCHECK          = 0x00F0
	BM_SETCHECK          = 0x00F1
	BST_CHECKED          = 1
	LB_ADDSTRING         = 0x0180
	LB_RESETCONTENT      = 0x0184
	LB_GETCURSEL         = 0x0188
	LB_DELETESTRING      = 0x0182
	MB_OK                = 0x00000000
	MB_ICONINFORMATION   = 0x00000040
	MB_ICONWARNING       = 0x00000030
	MB_ICONERROR         = 0x00000010
	OFN_ALLOWMULTISELECT = 0x00000200
	OFN_EXPLORER         = 0x00080000
	OFN_FILEMUSTEXIST    = 0x00001000
	OFN_PATHMUSTEXIST    = 0x00000800
	BIF_RETURNONLYFSDIRS = 0x00000001
	BIF_NEWDIALOGSTYLE   = 0x00000040
	PROGRESS_CLASS       = "msctls_progress32"
	PBM_SETRANGE32       = 0x0406
	PBM_SETPOS           = 0x0402
)

const (
	ID_SELECT = 1001
	ID_CLEAR  = 1002
	ID_START  = 1003
	ID_OPEN   = 1004
	ID_HELP   = 1005
	ID_CHANGE = 1006
	ID_LOG    = 1007
	ID_REMOVE = 1008
	ID_FAIL   = 1009
)

type WNDCLASSEXW struct {
	CbSize        uint32
	Style         uint32
	LpfnWndProc   uintptr
	CbClsExtra    int32
	CbWndExtra    int32
	HInstance     uintptr
	HIcon         uintptr
	HCursor       uintptr
	HbrBackground uintptr
	LpszMenuName  *uint16
	LpszClassName *uint16
	HIconSm       uintptr
}
type POINT struct{ X, Y int32 }
type MSG struct {
	Hwnd           uintptr
	Message        uint32
	WParam, LParam uintptr
	Time           uint32
	Pt             POINT
	LPrivate       uint32
}
type OPENFILENAMEW struct {
	LStructSize       uint32
	HwndOwner         uintptr
	HInstance         uintptr
	LpstrFilter       *uint16
	LpstrCustomFilter *uint16
	NMaxCustFilter    uint32
	NFilterIndex      uint32
	LpstrFile         *uint16
	NMaxFile          uint32
	LpstrFileTitle    *uint16
	NMaxFileTitle     uint32
	LpstrInitialDir   *uint16
	LpstrTitle        *uint16
	Flags             uint32
	NFileOffset       uint16
	NFileExtension    uint16
	LpstrDefExt       *uint16
	LCustData         uintptr
	LpfnHook          uintptr
	LpTemplateName    *uint16
	PvReserved        uintptr
	DwReserved        uint32
	FlagsEx           uint32
}
type BROWSEINFOW struct {
	HwndOwner      uintptr
	PidlRoot       uintptr
	PszDisplayName *uint16
	LpszTitle      *uint16
	UlFlags        uint32
	Lpfn           uintptr
	LParam         uintptr
	IImage         int32
}

var (
	user32   = syscall.NewLazyDLL("user32.dll")
	kernel32 = syscall.NewLazyDLL("kernel32.dll")
	comdlg32 = syscall.NewLazyDLL("comdlg32.dll")
	shell32  = syscall.NewLazyDLL("shell32.dll")
	gdi32    = syscall.NewLazyDLL("gdi32.dll")
	comctl32 = syscall.NewLazyDLL("comctl32.dll")

	pRegisterClassExW     = user32.NewProc("RegisterClassExW")
	pCreateWindowExW      = user32.NewProc("CreateWindowExW")
	pDefWindowProcW       = user32.NewProc("DefWindowProcW")
	pShowWindow           = user32.NewProc("ShowWindow")
	pUpdateWindow         = user32.NewProc("UpdateWindow")
	pGetMessageW          = user32.NewProc("GetMessageW")
	pTranslateMessage     = user32.NewProc("TranslateMessage")
	pDispatchMessageW     = user32.NewProc("DispatchMessageW")
	pPostQuitMessage      = user32.NewProc("PostQuitMessage")
	pMessageBoxW          = user32.NewProc("MessageBoxW")
	pSendMessageW         = user32.NewProc("SendMessageW")
	pSetWindowTextW       = user32.NewProc("SetWindowTextW")
	pEnableWindow         = user32.NewProc("EnableWindow")
	pLoadCursorW          = user32.NewProc("LoadCursorW")
	pPostMessageW         = user32.NewProc("PostMessageW")
	pGetModuleHandleW     = kernel32.NewProc("GetModuleHandleW")
	pGetOpenFileNameW     = comdlg32.NewProc("GetOpenFileNameW")
	pShellExecuteW        = shell32.NewProc("ShellExecuteW")
	pSHBrowseForFolderW   = shell32.NewProc("SHBrowseForFolderW")
	pSHGetPathFromIDListW = shell32.NewProc("SHGetPathFromIDListW")
	pCoTaskMemFree        = syscall.NewLazyDLL("ole32.dll").NewProc("CoTaskMemFree")
	pCreateFontW          = gdi32.NewProc("CreateFontW")
	pInitCommonControls   = comctl32.NewProc("InitCommonControls")
)

type batchEvent struct {
	Kind    string
	File    string
	Result  ConvResult
	ErrText string
	Index   int
	Total   int
	KeepLog bool
}

var uiEvents = make(chan batchEvent, 128)

var (
	hwndMain, hwndPath, hwndList, hwndCount, hwndProgress, hwndStatus, hwndHistory, hwndStart, hwndClear, hwndRemove, hwndOpen, hwndChange, hwndFail, hwndLog uintptr
	hFont                                                                                                                                                     uintptr
	selectedFiles                                                                                                                                             []string
	failures                                                                                                                                                  []string
	state                                                                                                                                                     AppState
	converting                                                                                                                                                bool
)

func wstr(s string) *uint16   { p, _ := syscall.UTF16PtrFromString(s); return p }
func loword(v uintptr) uint16 { return uint16(v & 0xffff) }

func main() {
	state = loadState()
	pInitCommonControls.Call()
	hInst, _, _ := pGetModuleHandleW.Call(0)
	hCursor, _, _ := pLoadCursorW.Call(0, 32512)
	cls := wstr("SMARTCOPIConverterWindow")
	wc := WNDCLASSEXW{CbSize: uint32(unsafe.Sizeof(WNDCLASSEXW{})), LpfnWndProc: syscall.NewCallback(wndProc), HInstance: hInst, HCursor: hCursor, HbrBackground: uintptr(6), LpszClassName: cls}
	pRegisterClassExW.Call(uintptr(unsafe.Pointer(&wc)))
	title := "SMART 銷貨單格式轉換工具"
	if version != "" && version != "dev" {
		title += " V" + version
	}
	hwndMain, _, _ = pCreateWindowExW.Call(0, uintptr(unsafe.Pointer(cls)), uintptr(unsafe.Pointer(wstr(title))), WS_OVERLAPPEDWINDOW|WS_VISIBLE, 100, 80, 1000, 760, 0, 0, hInst, 0)
	createControls(hwndMain)
	refreshPath()
	refreshList()
	refreshHistory()
	updateButtons()
	pShowWindow.Call(hwndMain, SW_SHOW)
	pUpdateWindow.Call(hwndMain)
	if strings.TrimSpace(state.Settings.POSOutputDir) == "" {
		message("首次啟動設定", "請先選擇 SMART POS 銷貨單匯入檔的輸出資料夾。", MB_OK|MB_ICONINFORMATION)
		chooseOutputFolder()
	}
	var msg MSG
	for {
		r, _, _ := pGetMessageW.Call(uintptr(unsafe.Pointer(&msg)), 0, 0, 0)
		if int32(r) <= 0 {
			break
		}
		pTranslateMessage.Call(uintptr(unsafe.Pointer(&msg)))
		pDispatchMessageW.Call(uintptr(unsafe.Pointer(&msg)))
	}
}

func createControls(parent uintptr) {
	hFont, _, _ = pCreateFontW.Call(^uintptr(16), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, uintptr(unsafe.Pointer(wstr("Microsoft JhengHei UI"))))
	createStatic(parent, "POS輸出資料夾：", 18, 18, 112, 24)
	hwndPath = createStatic(parent, "", 130, 18, 690, 24)
	hwndChange = createButton(parent, "變更", ID_CHANGE, 825, 14, 60, 30)
	hwndLog = createCheck(parent, "保留LOG", ID_LOG, 890, 18, 88, 26)

	createButton(parent, "選擇檔案", ID_SELECT, 18, 56, 112, 34)
	hwndClear = createButton(parent, "清空清單", ID_CLEAR, 140, 56, 112, 34)
	hwndStart = createButton(parent, "開始轉換", ID_START, 262, 56, 112, 34)
	hwndOpen = createButton(parent, "開啟POS資料夾", ID_OPEN, 384, 56, 138, 34)
	createButton(parent, "使用說明", ID_HELP, 532, 56, 112, 34)
	hwndRemove = createButton(parent, "移除選取", ID_REMOVE, 654, 56, 112, 34)

	hwndCount = createStatic(parent, "待轉檔檔案：0 個", 18, 104, 300, 24)
	hwndList = createControl(parent, "LISTBOX", "", WS_CHILD|WS_VISIBLE|WS_BORDER|WS_VSCROLL|LBS_NOTIFY|LBS_NOINTEGRALHEIGHT, 18, 130, 960, 155, 0)
	createStatic(parent, "轉換進度", 18, 298, 100, 22)
	hwndProgress = createControl(parent, PROGRESS_CLASS, "", WS_CHILD|WS_VISIBLE, 18, 322, 960, 22, 0)
	pSendMessageW.Call(hwndProgress, PBM_SETRANGE32, 0, 100)
	hwndStatus = createStatic(parent, "等待操作", 18, 352, 760, 24)
	hwndFail = createButton(parent, "失敗紀錄", ID_FAIL, 830, 346, 148, 32)
	createStatic(parent, "轉檔紀錄（最近 99 筆）", 18, 392, 260, 24)
	// 10 visible rows, text-based to avoid custom painting/timer instability.
	hwndHistory = createControl(parent, "LISTBOX", "", WS_CHILD|WS_VISIBLE|WS_BORDER|WS_VSCROLL|LBS_NOINTEGRALHEIGHT, 18, 418, 960, 245, 0)
	createStatic(parent, "© 2026 C.C. Liu, Chihyuan Co. All Rights Reserved.", 18, 678, 520, 22)
}

func createStatic(parent uintptr, text string, x, y, w, h int) uintptr {
	return createControl(parent, "STATIC", text, WS_CHILD|WS_VISIBLE|SS_LEFT, x, y, w, h, 0)
}
func createButton(parent uintptr, text string, id, x, y, w, h int) uintptr {
	return createControl(parent, "BUTTON", text, WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_PUSHBUTTON, x, y, w, h, id)
}
func createCheck(parent uintptr, text string, id, x, y, w, h int) uintptr {
	return createControl(parent, "BUTTON", text, WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_AUTOCHECKBOX, x, y, w, h, id)
}
func createControl(parent uintptr, cls, text string, style uintptr, x, y, w, h, id int) uintptr {
	hwnd, _, _ := pCreateWindowExW.Call(0, uintptr(unsafe.Pointer(wstr(cls))), uintptr(unsafe.Pointer(wstr(text))), style, uintptr(x), uintptr(y), uintptr(w), uintptr(h), parent, uintptr(id), 0, 0)
	if hFont != 0 {
		pSendMessageW.Call(hwnd, WM_SETFONT, hFont, 1)
	}
	return hwnd
}
func setText(hwnd uintptr, s string) { pSetWindowTextW.Call(hwnd, uintptr(unsafe.Pointer(wstr(s)))) }
func message(title, text string, flags uintptr) {
	pMessageBoxW.Call(hwndMain, uintptr(unsafe.Pointer(wstr(text))), uintptr(unsafe.Pointer(wstr(title))), flags)
}
func enable(hwnd uintptr, on bool) {
	v := uintptr(0)
	if on {
		v = 1
	}
	pEnableWindow.Call(hwnd, v)
}

func wndProc(hwnd uintptr, msg uint32, wparam, lparam uintptr) uintptr {
	switch msg {
	case WM_COMMAND:
		id := int(loword(wparam))
		switch id {
		case ID_SELECT:
			selectFiles()
		case ID_CLEAR:
			if !converting {
				selectedFiles = nil
				refreshList()
				updateButtons()
			}
		case ID_REMOVE:
			if !converting {
				removeSelected()
			}
		case ID_START:
			if !converting {
				startBatch()
			}
		case ID_OPEN:
			openOutput()
		case ID_HELP:
			showHelp()
		case ID_CHANGE:
			if !converting {
				chooseOutputFolder()
			}
		case ID_FAIL:
			showFailures()
		}
		return 0
	case WM_BATCH_EVENT:
		handleBatchEvent()
		return 0
	case WM_CLOSE:
		if converting {
			message("轉換中", "目前正在轉換檔案，請等待完成。", MB_OK|MB_ICONWARNING)
			return 0
		}
	case WM_DESTROY:
		pPostQuitMessage.Call(0)
		return 0
	}
	r, _, _ := pDefWindowProcW.Call(hwnd, uintptr(msg), wparam, lparam)
	return r
}

func selectFiles() {
	buf := make([]uint16, 65536)
	filter := syscall.StringToUTF16("Excel 檔案 (*.xlsx)\x00*.xlsx\x00所有檔案 (*.*)\x00*.*\x00\x00")
	ofn := OPENFILENAMEW{LStructSize: uint32(unsafe.Sizeof(OPENFILENAMEW{})), HwndOwner: hwndMain, LpstrFilter: &filter[0], LpstrFile: &buf[0], NMaxFile: uint32(len(buf)), LpstrTitle: wstr("選擇 COPI 匯出的 Excel 檔案"), Flags: OFN_ALLOWMULTISELECT | OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST, LpstrDefExt: wstr("xlsx")}
	r, _, _ := pGetOpenFileNameW.Call(uintptr(unsafe.Pointer(&ofn)))
	if r == 0 {
		return
	}
	parts := splitUTF16Multi(buf)
	if len(parts) == 0 {
		return
	}
	var files []string
	if len(parts) == 1 {
		files = []string{parts[0]}
	} else {
		for _, n := range parts[1:] {
			files = append(files, filepath.Join(parts[0], n))
		}
	}
	for _, f := range files {
		if strings.Contains(strings.ToLower(filepath.Base(f)), "copi08_1") {
			message("檔案提醒", "你選擇了 COPI08_1。這通常代表 ERP 尚未切換到「交易資料」頁籤；若你確認檔案正確，仍可繼續加入。", MB_OK|MB_ICONWARNING)
		}
		if !contains(selectedFiles, f) {
			selectedFiles = append(selectedFiles, f)
		}
	}
	refreshList()
	updateButtons()
}
func splitUTF16Multi(buf []uint16) []string {
	var out []string
	start := 0
	for i := 0; i < len(buf); i++ {
		if buf[i] == 0 {
			if i == start {
				break
			}
			out = append(out, syscall.UTF16ToString(buf[start:i]))
			start = i + 1
		}
	}
	return out
}
func contains(a []string, s string) bool {
	for _, x := range a {
		if strings.EqualFold(x, s) {
			return true
		}
	}
	return false
}
func removeSelected() {
	r, _, _ := pSendMessageW.Call(hwndList, LB_GETCURSEL, 0, 0)
	idx := int(int32(r))
	if idx < 0 || idx >= len(selectedFiles) {
		return
	}
	selectedFiles = append(selectedFiles[:idx], selectedFiles[idx+1:]...)
	refreshList()
	updateButtons()
}
func refreshList() {
	pSendMessageW.Call(hwndList, LB_RESETCONTENT, 0, 0)
	for _, f := range selectedFiles {
		pSendMessageW.Call(hwndList, LB_ADDSTRING, 0, uintptr(unsafe.Pointer(wstr(filepath.Base(f)))))
	}
	setText(hwndCount, fmt.Sprintf("待轉檔檔案：%d 個", len(selectedFiles)))
}
func refreshPath() {
	p := state.Settings.POSOutputDir
	if strings.TrimSpace(p) == "" {
		p = "（尚未設定）"
	}
	setText(hwndPath, p)
}
func updateButtons() {
	ok := !converting
	enable(hwndClear, ok && len(selectedFiles) > 0)
	enable(hwndRemove, ok && len(selectedFiles) > 0)
	enable(hwndStart, ok && len(selectedFiles) > 0 && strings.TrimSpace(state.Settings.POSOutputDir) != "")
	enable(hwndOpen, strings.TrimSpace(state.Settings.POSOutputDir) != "")
	enable(hwndChange, ok)
}

func chooseOutputFolder() {
	var display [260]uint16
	bi := BROWSEINFOW{HwndOwner: hwndMain, PszDisplayName: &display[0], LpszTitle: wstr("選擇 SMART POS 銷貨單匯入檔的輸出資料夾"), UlFlags: BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE}
	pidl, _, _ := pSHBrowseForFolderW.Call(uintptr(unsafe.Pointer(&bi)))
	if pidl == 0 {
		return
	}
	defer pCoTaskMemFree.Call(pidl)
	var path [32768]uint16
	r, _, _ := pSHGetPathFromIDListW.Call(pidl, uintptr(unsafe.Pointer(&path[0])))
	if r == 0 {
		message("設定失敗", "無法取得資料夾路徑。", MB_OK|MB_ICONERROR)
		return
	}
	state.Settings.POSOutputDir = syscall.UTF16ToString(path[:])
	if err := saveState(state); err != nil {
		message("設定失敗", err.Error(), MB_OK|MB_ICONERROR)
		return
	}
	refreshPath()
	updateButtons()
}
func openOutput() {
	p := state.Settings.POSOutputDir
	if p == "" {
		chooseOutputFolder()
		return
	}
	pShellExecuteW.Call(0, uintptr(unsafe.Pointer(wstr("open"))), uintptr(unsafe.Pointer(wstr(p))), 0, 0, SW_SHOW)
}
func showHelp() {
	text := "1. 在ERP選擇要匯出的單據，切換到「交易資料」頁籤，點上方選單EXCEL→轉出至EXCEL\r\n\r\n" +
		"2. 預設路徑欄位最後檔名應該為COPI08_2，如果是COPI08_1代表未切換到「交易資料」頁籤。建議選擇路徑為桌面。\r\n\r\n" +
		"3. 按「選擇檔案」選擇所有要轉換的檔案，確認檔案無誤按「開始轉換」並確認全部都轉換成功，轉換完成的檔案會以ERP的訂單編號命名。\r\n\r\n" +
		"4. 在POS機操作，點選「SMART銷貨單」，選擇相應檔案，匯入成功就會自動印出發票。"
	message("使用說明", text, MB_OK|MB_ICONINFORMATION)
}
func showFailures() {
	if len(failures) == 0 {
		message("失敗紀錄", "目前沒有失敗紀錄。", MB_OK|MB_ICONINFORMATION)
		return
	}
	message("失敗紀錄", strings.Join(failures, "\r\n\r\n"), MB_OK|MB_ICONERROR)
}

func startBatch() {
	if len(selectedFiles) == 0 {
		return
	}
	if state.Settings.POSOutputDir == "" {
		chooseOutputFolder()
		if state.Settings.POSOutputDir == "" {
			return
		}
	}
	converting = true
	failures = nil
	updateButtons()
	pSendMessageW.Call(hwndProgress, PBM_SETPOS, 0, 0)
	setText(hwndStatus, "開始轉換…")

	files := append([]string(nil), selectedFiles...)
	outDir := state.Settings.POSOutputDir
	keepLog := isLogChecked()
	go runBatch(files, outDir, keepLog)
}

func runBatch(files []string, outDir string, keepLog bool) {
	for i, f := range files {
		postBatchEvent(batchEvent{Kind: "progress", File: f, Index: i + 1, Total: len(files)})
		res, err := convertFile(f, outDir)
		if err != nil {
			writeLogEnabled(keepLog, fmt.Sprintf("FAIL %s: %v", f, err))
			postBatchEvent(batchEvent{Kind: "failure", File: f, ErrText: err.Error(), Index: i + 1, Total: len(files)})
		} else {
			writeLogEnabled(keepLog, fmt.Sprintf("OK %s -> %s", f, res.OutputPath))
			postBatchEvent(batchEvent{Kind: "success", File: f, Result: res, Index: i + 1, Total: len(files)})
		}
	}
	postBatchEvent(batchEvent{Kind: "done", Total: len(files)})
}

func postBatchEvent(ev batchEvent) {
	uiEvents <- ev
	pPostMessageW.Call(hwndMain, WM_BATCH_EVENT, 0, 0)
}

func handleBatchEvent() {
	select {
	case ev := <-uiEvents:
		switch ev.Kind {
		case "progress":
			setText(hwndStatus, fmt.Sprintf("轉換中 %d/%d：%s", ev.Index, ev.Total, filepath.Base(ev.File)))
		case "failure":
			failures = append(failures, fmt.Sprintf("%s\r\n%s", filepath.Base(ev.File), ev.ErrText))
			pct := int(float64(ev.Index) / float64(ev.Total) * 100)
			pSendMessageW.Call(hwndProgress, PBM_SETPOS, uintptr(pct), 0)
		case "success":
			state.History = append([]HistoryItem{{Date: time.Now().Format("2006/01/02"), OrderType: ev.Result.OrderType, OrderNo: ev.Result.OrderNo, Customer: ev.Result.Customer}}, state.History...)
			if len(state.History) > 99 {
				state.History = state.History[:99]
			}
			_ = saveState(state)
			pct := int(float64(ev.Index) / float64(ev.Total) * 100)
			pSendMessageW.Call(hwndProgress, PBM_SETPOS, uintptr(pct), 0)
		case "done":
			converting = false
			refreshHistory()
			if len(failures) == 0 {
				setText(hwndStatus, "轉換完成：全部成功")
				message("轉換完成", "全部檔案轉換成功。", MB_OK|MB_ICONINFORMATION)
			} else {
				setText(hwndStatus, fmt.Sprintf("轉換完成：%d 個失敗", len(failures)))
				message("轉換完成", fmt.Sprintf("完成，但有 %d 個檔案轉換失敗。請查看「失敗紀錄」。", len(failures)), MB_OK|MB_ICONWARNING)
			}
			updateButtons()
		}
	default:
	}
}

func isLogChecked() bool {
	r, _, _ := pSendMessageW.Call(hwndLog, BM_GETCHECK, 0, 0)
	return r == BST_CHECKED
}

func refreshHistory() {
	pSendMessageW.Call(hwndHistory, LB_RESETCONTENT, 0, 0)
	header := "序號   轉檔日期       銷貨單別    銷貨單號             客戶全名"
	pSendMessageW.Call(hwndHistory, LB_ADDSTRING, 0, uintptr(unsafe.Pointer(wstr(header))))
	for i, h := range state.History {
		line := fmt.Sprintf("%-4d  %-12s  %-10s  %-18s  %s", i+1, h.Date, h.OrderType, h.OrderNo, h.Customer)
		pSendMessageW.Call(hwndHistory, LB_ADDSTRING, 0, uintptr(unsafe.Pointer(wstr(line))))
	}
}
func writeLogEnabled(enabled bool, line string) {
	if !enabled {
		return
	}
	p, err := logPath()
	if err != nil {
		return
	}
	f, err := os.OpenFile(p, os.O_CREATE|os.O_APPEND|os.O_WRONLY, 0o600)
	if err != nil {
		return
	}
	defer f.Close()
	fmt.Fprintf(f, "%s %s\n", time.Now().Format("2006-01-02 15:04:05"), line)
}
