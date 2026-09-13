//go:build windows

package main

import (
	"fmt"
	"path/filepath"
	"strings"
	"syscall"
	"unsafe"
)

func wndProc(hwnd uintptr, msg uint32, wparam, lparam uintptr) uintptr {
	switch msg {
	case WM_CREATE:
		diag("WM_CREATE")
		createControls(hwnd)
		refreshAll()
		return 0
	case WM_FIRST_RUN:
		diag("WM_FIRST_RUN")
		message("首次啟動設定", "請先選擇 SMART POS 銷貨單匯入檔的輸出資料夾。", MB_OK|MB_ICONINFORMATION)
		chooseOutputFolder()
		return 0
	case WM_COMMAND:
		id := int(loword(wparam))
		diag("WM_COMMAND id=%d", id)
		switch id {
		case ID_SELECT:
			if !converting {
				handleSelectFiles()
			}
		case ID_CLEAR:
			if !converting {
				clearFiles()
			}
		case ID_START:
			if !converting {
				startBatch()
			}
		case ID_OPEN:
			openOutput()
		case ID_HELP:
			showHelp()
		case ID_FAIL:
			showFailures()
		case ID_CHANGE:
			if !converting {
				chooseOutputFolder()
			}
		}
		return 0
	case WM_NOTIFY:
		hdr := (*NMHDR)(unsafe.Pointer(lparam))
		if hdr != nil && int(hdr.IDFrom) == ID_FILES && hdr.Code == NM_CLICK {
			act := (*NMITEMACTIVATE)(unsafe.Pointer(lparam))
			if act.IItem >= 0 && act.ISubItem == 1 && !converting {
				removeFile(int(act.IItem))
			}
		}
		return 0
	case WM_UI_EVENT:
		handleUIEvents()
		return 0
	case WM_CTLCOLORSTATIC:
		// Keep native STATIC controls visually consistent with the white main window.
		// This is standard Win32 color handling, not custom/owner drawing.
		pSetBkMode.Call(wparam, TRANSPARENT)
		brush, _, _ := pGetSysColorBrush.Call(COLOR_WINDOW)
		return brush
	case WM_CTLCOLORBTN:
		// The checkbox label otherwise inherits the dialog/button-face gray background.
		if lparam == hwndLog {
			pSetBkMode.Call(wparam, TRANSPARENT)
			brush, _, _ := pGetSysColorBrush.Call(COLOR_WINDOW)
			return brush
		}
	case WM_CLOSE:
		if converting {
			message("轉換中", "目前正在轉換檔案，請等待完成。", MB_OK|MB_ICONWARNING)
			return 0
		}
	case WM_DESTROY:
		diag("WM_DESTROY")
		pPostQuitMessage.Call(0)
		return 0
	}
	r, _, _ := pDefWindowProcW.Call(hwnd, uintptr(msg), wparam, lparam)
	return r
}

func createControls(parent uintptr) {
	hFont = createFont(18, 400)

	hwndPath = createStatic(parent, "POS輸出資料夾：", 26, 24, 510, 28)
	hwndChange = createButton(parent, "設定", ID_CHANGE, 548, 20, 58, 30)
	hwndLog = createCheck(parent, "保留LOG", ID_LOG, 618, 24, 110, 26)

	hwndSelect = createButton(parent, "選擇檔案", ID_SELECT, 26, 64, 116, 34)
	hwndClear = createButton(parent, "清空清單", ID_CLEAR, 154, 64, 116, 34)
	hwndStart = createButton(parent, "開始轉換", ID_START, 282, 64, 120, 34)
	hwndOpen = createButton(parent, "開啟POS資料夾", ID_OPEN, 410, 64, 148, 34)
	hwndHelp = createButton(parent, "使用說明", ID_HELP, 568, 64, 116, 34)

	hwndCount = createStatic(parent, "待轉檔檔案：0 個", 26, 106, 300, 26)
	hwndFiles = createControl(parent, "SysListView32", "", WS_CHILD|WS_VISIBLE|WS_BORDER|LVS_REPORT|LVS_SINGLESEL|LVS_SHOWSELALWAYS|LVS_NOCOLUMNHEADER, 26, 136, 696, 76, ID_FILES)
	listSetExtended(hwndFiles, LVS_EX_FULLROWSELECT)
	listAddColumn(hwndFiles, 0, "檔案", 635)
	listAddColumn(hwndFiles, 1, "狀態", 55)

	createStatic(parent, "轉換進度：", 26, 232, 100, 26)
	hwndProgress = createControl(parent, PROGRESS_CLASS, "", WS_CHILD|WS_VISIBLE, 124, 232, 600, 24, 0)
	pSendMessageW.Call(hwndProgress, PBM_SETRANGE32, 0, 1000)

	hwndStatus = createStatic(parent, "請選擇 COPI 匯出的 .xlsx 檔案。", 26, 266, 560, 28)
	hwndFail = createButton(parent, "失敗紀錄", ID_FAIL, 608, 260, 116, 34)

	createStatic(parent, "轉檔歷史紀錄：", 26, 306, 200, 26)
	hwndHistory = createControl(parent, "SysListView32", "", WS_CHILD|WS_VISIBLE|WS_BORDER|LVS_REPORT|LVS_SINGLESEL|LVS_SHOWSELALWAYS, 26, 332, 696, 224, ID_HISTORY)
	listSetExtended(hwndHistory, LVS_EX_GRIDLINES|LVS_EX_FULLROWSELECT)
	listAddColumn(hwndHistory, 0, "序號", 40)
	listAddColumn(hwndHistory, 1, "轉檔日期", 96)
	listAddColumn(hwndHistory, 2, "銷貨單別", 96)
	listAddColumn(hwndHistory, 3, "銷貨單號", 130)
	listAddColumn(hwndHistory, 4, "客戶全名", 310)
}

func listSetExtended(hwnd uintptr, style uintptr) {
	pSendMessageW.Call(hwnd, LVM_SETEXTENDEDLISTVIEWSTYLE, style, style)
}
func listAddColumn(hwnd uintptr, index int, text string, width int) {
	t := wstr(text)
	c := LVCOLUMNW{Mask: LVCF_FMT | LVCF_WIDTH | LVCF_TEXT, Fmt: LVCFMT_LEFT, Cx: int32(width), PszText: t}
	pSendMessageW.Call(hwnd, LVM_INSERTCOLUMNW, uintptr(index), uintptr(unsafe.Pointer(&c)))
}
func listClear(hwnd uintptr) { pSendMessageW.Call(hwnd, LVM_DELETEALLITEMS, 0, 0) }
func listInsert(hwnd uintptr, row int, values ...string) {
	if len(values) == 0 {
		return
	}
	t := wstr(values[0])
	item := LVITEMW{Mask: LVIF_TEXT, IItem: int32(row), ISubItem: 0, PszText: t}
	pSendMessageW.Call(hwnd, LVM_INSERTITEMW, 0, uintptr(unsafe.Pointer(&item)))
	for i := 1; i < len(values); i++ {
		listSetText(hwnd, row, i, values[i])
	}
}
func listSetText(hwnd uintptr, row, col int, text string) {
	t := wstr(text)
	item := LVITEMW{Mask: LVIF_TEXT, IItem: int32(row), ISubItem: int32(col), PszText: t}
	pSendMessageW.Call(hwnd, LVM_SETITEMW, 0, uintptr(unsafe.Pointer(&item)))
}

func refreshAll() { refreshPath(); refreshFiles(); refreshHistory(); updateButtons() }
func refreshPath() {
	p := strings.TrimSpace(state.Settings.POSOutputDir)
	if p == "" {
		p = "（尚未設定）"
	}
	setText(hwndPath, "POS輸出資料夾："+p)
}
func refreshFiles() {
	listClear(hwndFiles)
	for i, f := range files {
		listInsert(hwndFiles, i, filepath.Base(f.Path), f.Status)
	}
	setText(hwndCount, fmt.Sprintf("待轉檔檔案：%d 個", len(files)))
}
func refreshHistory() {
	listClear(hwndHistory)
	limit := len(state.History)
	if limit > 99 {
		limit = 99
	}
	for i := 0; i < limit; i++ {
		h := state.History[i]
		listInsert(hwndHistory, i, fmt.Sprintf("%d", i+1), h.Date, h.OrderType, h.OrderNo, h.Customer)
	}
}
func updateButtons() {
	enable(hwndSelect, !converting)
	enable(hwndClear, !converting && len(files) > 0)
	enable(hwndStart, !converting && len(files) > 0 && strings.TrimSpace(state.Settings.POSOutputDir) != "")
	enable(hwndOpen, strings.TrimSpace(state.Settings.POSOutputDir) != "")
	enable(hwndChange, !converting)
	enable(hwndFail, len(failures) > 0)
}

func handleSelectFiles() {
	selected, err := openFileDialog(hwndMain)
	if err != nil {
		diag("openFileDialog error: %v", err)
		message("選擇檔案失敗", err.Error(), MB_OK|MB_ICONERROR)
		return
	}
	if len(selected) == 0 {
		diag("openFileDialog canceled")
		return
	}
	diag("selected %d file(s)", len(selected))
	if batchDone {
		files = nil
		failures = nil
		batchDone = false
	}
	seen := map[string]bool{}
	for _, f := range files {
		seen[strings.ToLower(f.Path)] = true
	}
	for _, p := range selected {
		if strings.Contains(strings.ToLower(filepath.Base(p)), "copi08_1") {
			message("檔案提醒", "你選擇了 COPI08_1。這通常代表 ERP 尚未切換到「交易資料」頁籤；若你確認檔案正確，仍可繼續加入。", MB_OK|MB_ICONWARNING)
		}
		k := strings.ToLower(p)
		if !seen[k] {
			files = append(files, fileEntry{Path: p, Status: "×"})
			seen[k] = true
		}
	}
	refreshFiles()
	updateButtons()
}
func clearFiles() {
	files = nil
	failures = nil
	batchDone = false
	refreshFiles()
	setText(hwndStatus, "請選擇 COPI 匯出的 .xlsx 檔案。")
	pSendMessageW.Call(hwndProgress, PBM_SETPOS, 0, 0)
	updateButtons()
}
func removeFile(index int) {
	if index < 0 || index >= len(files) {
		return
	}
	diag("remove file index=%d path=%q", index, files[index].Path)
	files = append(files[:index], files[index+1:]...)
	refreshFiles()
	updateButtons()
}

func openFileDialog(owner uintptr) ([]string, error) {
	buf := make([]uint16, 65536)
	filter := makeOpenFileFilter("Excel 檔案 (*.xlsx)", "*.xlsx", "所有檔案 (*.*)", "*.*")
	ofn := OPENFILENAMEW{LStructSize: uint32(unsafe.Sizeof(OPENFILENAMEW{})), HwndOwner: owner, LpstrFilter: &filter[0], LpstrFile: &buf[0], NMaxFile: uint32(len(buf)), LpstrTitle: wstr("選擇 COPI 匯出的 Excel 檔案"), Flags: OFN_ALLOWMULTISELECT | OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST, LpstrDefExt: wstr("xlsx")}
	r, _, e := pGetOpenFileNameW.Call(uintptr(unsafe.Pointer(&ofn)))
	if r == 0 {
		if errno, ok := e.(syscall.Errno); ok && errno != 0 {
			return nil, e
		}
		return nil, nil
	}
	parts := splitUTF16Multi(buf)
	if len(parts) == 0 {
		return nil, nil
	}
	if len(parts) == 1 {
		return []string{parts[0]}, nil
	}
	out := make([]string, 0, len(parts)-1)
	for _, n := range parts[1:] {
		out = append(out, filepath.Join(parts[0], n))
	}
	return out, nil
}

func makeOpenFileFilter(parts ...string) []uint16 {
	// OPENFILENAMEW expects NUL-separated display/pattern pairs terminated by a double NUL.
	// syscall.StringToUTF16 rejects embedded NUL bytes, so each segment must be encoded separately.
	var out []uint16
	for _, part := range parts {
		u := syscall.StringToUTF16(part) // includes one trailing NUL
		out = append(out, u...)
	}
	out = append(out, 0) // second NUL terminator
	return out
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

func chooseOutputFolder() {
	diag("folder picker begin")
	path, err := pickFolder(hwndMain)
	if err != nil {
		diag("folder picker error: %v", err)
		message("設定失敗", err.Error(), MB_OK|MB_ICONERROR)
		return
	}
	if path == "" {
		diag("folder picker canceled")
		return
	}
	diag("folder picker selected=%q", path)
	old := state.Settings.POSOutputDir
	state.Settings.POSOutputDir = path
	diag("saveState begin")
	if err := saveState(state); err != nil {
		state.Settings.POSOutputDir = old
		diag("saveState error: %v", err)
		message("設定失敗", err.Error(), MB_OK|MB_ICONERROR)
		return
	}
	diag("saveState done")
	refreshPath()
	updateButtons()
}

// pickFolder uses the native IFileDialog with FOS_PICKFOLDERS. It does not probe the selected network path.
func pickFolder(owner uintptr) (string, error) {
	clsid := GUID{Data1: 0xDC1C5A9C, Data2: 0xE88A, Data3: 0x4DDE, Data4: [8]byte{0xA5, 0xA1, 0x60, 0xF8, 0x2A, 0x20, 0xAE, 0xF7}}
	iid := GUID{Data1: 0xD57C7288, Data2: 0xD4AD, Data3: 0x4768, Data4: [8]byte{0xBE, 0x02, 0x9D, 0x96, 0x95, 0x32, 0xD9, 0x60}}
	var obj uintptr
	hr, _, _ := pCoCreateInstance.Call(uintptr(unsafe.Pointer(&clsid)), 0, CLSCTX_INPROC_SERVER, uintptr(unsafe.Pointer(&iid)), uintptr(unsafe.Pointer(&obj)))
	if int32(hr) < 0 || obj == 0 {
		return "", fmt.Errorf("無法開啟資料夾選擇器 (0x%08X)", uint32(hr))
	}
	defer comRelease(obj)
	opts := uint32(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST)
	if hr := comCall(obj, 9, uintptr(opts)); int32(hr) < 0 {
		return "", fmt.Errorf("設定資料夾選擇器失敗 (0x%08X)", uint32(hr))
	}
	if hr := comCall(obj, 3, owner); int32(hr) < 0 {
		if uint32(hr) == 0x800704C7 {
			return "", nil
		}
		return "", fmt.Errorf("資料夾選擇器失敗 (0x%08X)", uint32(hr))
	}
	var item uintptr
	if hr := comCall(obj, 20, uintptr(unsafe.Pointer(&item))); int32(hr) < 0 || item == 0 {
		return "", fmt.Errorf("無法取得選擇的資料夾 (0x%08X)", uint32(hr))
	}
	defer comRelease(item)
	var psz *uint16
	if hr := comCall(item, 5, SIGDN_FILESYSPATH, uintptr(unsafe.Pointer(&psz))); int32(hr) < 0 || psz == nil {
		return "", fmt.Errorf("無法取得資料夾路徑 (0x%08X)", uint32(hr))
	}
	defer pCoTaskMemFree.Call(uintptr(unsafe.Pointer(psz)))
	return syscall.UTF16ToString((*[1 << 20]uint16)(unsafe.Pointer(psz))[:]), nil
}

func openOutput() {
	p := strings.TrimSpace(state.Settings.POSOutputDir)
	if p == "" {
		chooseOutputFolder()
		return
	}
	diag("open output=%q", p)
	pShellExecuteW.Call(0, uintptr(unsafe.Pointer(wstr("open"))), uintptr(unsafe.Pointer(wstr(p))), 0, 0, SW_SHOW)
}
func showHelp() {
	text := "1. 在ERP選擇要匯出的單據，切換到「交易資料」頁籤，點上方選單EXCEL→轉出至EXCEL\r\n\r\n" +
		"2. 預設路徑欄位最後檔名應該為COPI08_2，如果是COPI08_1代表未切換到「交易資料」頁籤。建議選擇路徑為桌面。\r\n\r\n" +
		"3. 按「選擇檔案」選擇所有要轉換的檔案，點選待轉檔檔案右側的X可以移除單項；確認檔案無誤按「開始轉換」，並確認全部都轉換成功。轉換完成的檔案會以ERP的訂單編號命名。\r\n\r\n" +
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
