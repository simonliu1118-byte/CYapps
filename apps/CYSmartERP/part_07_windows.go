//go:build windows

package main

import (
	"fmt"
	"strings"
	"time"
	"unicode/utf16"
	"unsafe"
)

func clickNewCommand(root uintptr) bool {
	group := findRibbonEditGroup(root)
	if group == nil {
		logError("新增", "Ribbon", "EDIT_GROUP_NOT_FOUND", "找不到可見的編輯 Ribbon 群組")
		return false
	}
	w := group.Rect.Right - group.Rect.Left
	h := group.Rect.Bottom - group.Rect.Top
	x := group.Rect.Left + w*13/100
	y := group.Rect.Top + h*30/100
	prepareERPWindow(root)
	time.Sleep(120 * time.Millisecond)
	logf("INFO", "click NEW via ribbon group hwnd=0x%x point=%d,%d group_rect=%d,%d,%d,%d", group.Hwnd, x, y, group.Rect.Left, group.Rect.Top, group.Rect.Right, group.Rect.Bottom)
	clickScreenPoint(x, y)
	return true
}

func ensureInputMode(root uintptr) bool {
	mode := detectERPMode(root)
	if mode == ERPModeInput {
		logf("INFO", "ensure input: already INPUT")
		return true
	}
	if mode == ERPModeUnknown {
		logf("WARN", "ensure input: mode UNKNOWN; refusing to click NEW blindly")
		return false
	}
	if !clickNewCommand(root) { return false }
	for i := 0; i < 8; i++ {
		if !interruptibleSleep(150 * time.Millisecond) { return false }
		mode = detectERPMode(root)
		if mode == ERPModeInput {
			logf("INFO", "ensure input: NEW confirmed after %d ms", (i+1)*150)
			return true
		}
	}
	logf("WARN", "ensure input: NEW click did not produce confirmed INPUT mode")
	return false
}

func testERPMode() {
	root := findERPWindow(); if root == 0 { setStatus("ERP：找不到 COPI08"); return }
	mode := detectERPMode(root)
	switch mode {
	case ERPModeBrowse: setStatus("ERP 狀態：瀏覽（尚未進入輸入狀態）")
	case ERPModeInput: setStatus("ERP 狀態：輸入中（新增／修改不再細分）")
	default: setStatus("ERP 狀態：無法判斷；請提供 logs 資料夾")
	}
}

func testEnsureInputMode() {
	root := findERPWindow(); if root == 0 { setStatus("ERP：找不到 COPI08"); return }
	if ensureInputMode(root) {
		if !isStopRequested() { setStatus("ERP：已確認進入輸入狀態（此版不儲存）") }
	} else if !isStopRequested() { setStatus("ERP：無法安全進入／確認輸入狀態；已停止") }
}

func selectedComboFields() []*Field {
	out := []*Field{}
	for _, f := range fields { if f.Kind == "combo" && checked(f.ApplyHwnd) { out = append(out, f) } }
	return out
}

func selectDevExpressComboByDisplay(root uintptr, target ControlInfo, wanted string) bool {
	wanted = strings.TrimSpace(wanted); if wanted == "" || isStopRequested() { return false }
	current := comboCommitFirst(root, target); if current == wanted { return true }; if current == "" { return false }
	seen := map[string]bool{current:true}; deadline := time.Now().Add(5*time.Second)
	for i:=0; i<20 && time.Now().Before(deadline); i++ {
		if isStopRequested(){return false}; next:=comboCommitNext(root,target); if next==wanted{return true}; if next==""||seen[next]{break}; seen[next]=true; current=next
	}
	return false
}

func discoverComboOptions(root uintptr, target ControlInfo) []string {
	if isStopRequested(){return nil}; original:=strings.TrimSpace(comboDisplayText(target)); logf("INFO","combo discover start hwnd=0x%x class=%q original=%q",target.Hwnd,target.Class,original)
	options,_:=comboCycleClosed(root,target,"",true); if isStopRequested(){return nil}; if len(options)>1{logf("INFO","combo discover closed-cycle success hwnd=0x%x count=%d",target.Hwnd,len(options));return options}
	cur:=comboCommitFirst(root,target); if isStopRequested(){return nil}; if strings.TrimSpace(cur)==""{logf("WARN","combo discover aborted: display text unreadable after bounded probes hwnd=0x%x",target.Hwnd);return nil}
	seen:=map[string]bool{}; options=options[:0]; add:=func(v string)bool{v=strings.TrimSpace(v);if v==""||seen[v]{return false};seen[v]=true;options=append(options,v);return true}; add(cur); deadline:=time.Now().Add(5*time.Second); empty:=0
	for i:=0;i<20&&time.Now().Before(deadline);i++{if isStopRequested(){return nil};next:=comboCommitNext(root,target);if next==""{empty++}else{empty=0};if next!=""&&seen[next]{break};add(next);if empty>=2{break}}
	logf("INFO","combo discover popup end hwnd=0x%x count=%d",target.Hwnd,len(options));return options
}

func readSelectedComboOptions() {
	root:=findERPWindow();if root==0{setStatus("ERP：找不到 COPI08");return};if !prepareERPWindow(root){setStatus("設定：ERP 無法移到前景，未讀取選項");return};selected:=selectedComboFields();if len(selected)==0{setStatus("設定：請先勾選至少一個下拉欄位");return};if !ensureInputMode(root){setStatus("設定：無法確認 ERP 輸入狀態，未讀取選項");return}
	readCount:=0;pending:=map[string]ComboSetting{}
	for _,f:=range selected{if isStopRequested(){return};if !activateDevExpressTab(root,f.Group){logError("設定",f.Label,"TAB_ACTIVATE_FAILED",f.Group);continue};sheet:=findTabSheet(root,f.Group);rows:=buildActionRows(sheet);if !validateGroupLayout(f.Group,rows)||f.Row>=len(rows)||f.Col>=len(rows[f.Row]){logError("設定",f.Label,"FIELD_POSITION_MISSING","下拉欄位定位失敗");continue};target:=rows[f.Row][f.Col];options:=discoverComboOptions(root,target);if isStopRequested(){return};if len(options)==0{logError("設定",f.Label,"OPTION_READ_FAILED",fmt.Sprintf("hwnd=0x%x class=%s",target.Hwnd,target.Class));continue};cs:=settings.Combos[f.Key];cs.Options=options;if strings.TrimSpace(cs.Selected)==""{cs.Selected=strings.TrimSpace(getWindowText(f.ValueHwnd))};pending[f.Key]=cs;readCount++;logf("INFO","combo options read key=%s label=%s count=%d",f.Key,f.Label,len(options))}
	if isStopRequested(){return};for k,v:=range pending{settings.Combos[k]=v};if err:=writeSettings();err!=nil{logError("設定","settings.json","SAVE_FAILED",err.Error());setStatus("設定：選項已讀取但設定檔儲存失敗");return};if isStopRequested(){return};setStatus(fmt.Sprintf("設定：已讀取 %d 個下拉欄位；結果寫入本機 settings.json",readCount));showComboSettingsSummary()
}

func showComboSettingsSummary(){var b strings.Builder;b.WriteString("下拉欄位設定／偵測結果\r\n\r\n");for _,f:=range fields{if f.Kind!="combo"{continue};cs:=settings.Combos[f.Key];b.WriteString(f.Group+" / "+f.Label+"\r\n");if strings.TrimSpace(cs.Selected)==""{b.WriteString("  設定值：未設定\r\n")}else{b.WriteString("  設定值：已設定\r\n")};if len(cs.Options)==0{b.WriteString("  讀取選項：（尚未讀到）\r\n")}else{for i,opt:=range cs.Options{b.WriteString(fmt.Sprintf("  %d. %s\r\n",i+1,opt))}};b.WriteString("\r\n")};pMessageBoxW.Call(mainHwnd,uintptr(unsafe.Pointer(wstr(b.String()))),uintptr(unsafe.Pointer(wstr("CY SMART ERP — 欄位設定"))),MB_OK|MB_ICONINFORMATION)}

func focusTypingTest(){txt:=getWindowText(focusText);if txt==""{logf("WARN","focus typing test cancelled: empty text");return};for i:=3;i>=1;i--{if isStopRequested(){return};setStatus(fmt.Sprintf("焦點測試：%d 秒內請點 ERP 目標欄位…（Esc 可停止）",i));pUpdateWindow.Call(mainHwnd);if !interruptibleSleep(time.Second){return}};fg,_,_:=pGetForeground.Call();title:=getWindowText(fg);if !(strings.Contains(title,"銷貨單建立作業")||strings.Contains(strings.ToUpper(title),"SMART")){setStatus("焦點測試：前景不是 SMART ERP，已取消");logError("焦點測試","前景視窗","WRONG_FOREGROUND",fmt.Sprintf("title=%q",title));return};target:=focusedControlOfForeground(fg);if target==0{setStatus("焦點測試：抓不到 ERP 內目前焦點欄位");logError("焦點測試","焦點欄位","FOCUS_NOT_FOUND",fmt.Sprintf("foreground=0x%x title=%q",fg,title));return};cls:=className(target);before:=getWindowText(target);logf("INFO","focus target hwnd=0x%x class=%q before_len=%d",target,cls,len([]rune(before)));pSendMessageW.Call(target,EM_SETSEL,0,^uintptr(0));time.Sleep(40*time.Millisecond);units:=utf16.Encode([]rune(txt));for _,u:=range units{pSendMessageW.Call(target,WM_CHAR,uintptr(u),0)};time.Sleep(120*time.Millisecond);after:=getWindowText(target);if after==txt{setStatus(fmt.Sprintf("焦點測試：成功，%s 已輸入（未按 Enter、未儲存）",cls));logf("INFO","focus direct input success hwnd=0x%x class=%q after_len=%d",target,cls,len([]rune(after)));return};r,_,_:=pSendMessageW.Call(target,WM_SETTEXT,0,uintptr(unsafe.Pointer(wstr(txt))));time.Sleep(120*time.Millisecond);after2:=getWindowText(target);if r!=0&&after2==txt{setStatus(fmt.Sprintf("焦點測試：成功（WM_SETTEXT），%s 已輸入；未儲存",cls));logf("INFO","focus WM_SETTEXT fallback success hwnd=0x%x class=%q after_len=%d",target,cls,len([]rune(after2)));return};setStatus("焦點測試：欄位已找到，但直接輸入仍失敗；請提供 LOG");logError("焦點測試","輸入","DIRECT_INPUT_FAILED",fmt.Sprintf("hwnd=0x%x class=%q before_len=%d after_len=%d after2_len=%d wm_settext_ret=%d",target,cls,len([]rune(before)),len([]rune(after)),len([]rune(after2)),r))}

func focusedControlOfForeground(fg uintptr)uintptr{if fg==0{return 0};tid,_,_:=pGetWindowThreadProcessId.Call(fg,0);if tid==0{return 0};info:=GUITHREADINFO{CbSize:uint32(unsafe.Sizeof(GUITHREADINFO{}))};r,_,_:=pGetGUIThreadInfo.Call(tid,uintptr(unsafe.Pointer(&info)));if r==0{return 0};return info.HwndFocus}
func keyInput(vk uint16,scan uint16,flags uint32)INPUT{return INPUT{Type:INPUT_KEYBOARD,Ki:KEYBDINPUT{WVk:vk,WScan:scan,DwFlags:flags}}}
func sendUnicodeText(s string){units:=utf16.Encode([]rune(s));for _,u:=range units{sendInputs([]INPUT{keyInput(0,u,KEYEVENTF_UNICODE),keyInput(0,u,KEYEVENTF_UNICODE|KEYEVENTF_KEYUP)})}}
func sendInputs(inputs []INPUT){if len(inputs)==0{return};pSendInput.Call(uintptr(len(inputs)),uintptr(unsafe.Pointer(&inputs[0])),unsafe.Sizeof(INPUT{}))}
func openPath(path string){pShellExecuteW.Call(0,uintptr(unsafe.Pointer(wstr("open"))),uintptr(unsafe.Pointer(wstr(path))),0,0,SW_SHOW)}
