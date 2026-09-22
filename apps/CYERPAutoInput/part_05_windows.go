//go:build windows

package main

import (
	"fmt"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

func buildActionRows(sheet uintptr) [][]ControlInfo {
	items := directActionControls(sheet)

	for i := 0; i < len(items); i++ {
		for j := i + 1; j < len(items); j++ {
			iy := (items[i].Rect.Top + items[i].Rect.Bottom) / 2
			jy := (items[j].Rect.Top + items[j].Rect.Bottom) / 2
			if jy < iy || (jy == iy && items[j].Rect.Left < items[i].Rect.Left) {
				items[i], items[j] = items[j], items[i]
			}
		}
	}
	rows := [][]ControlInfo{}
	rowCenters := []int32{}
	for _, c := range items {
		cy := (c.Rect.Top + c.Rect.Bottom) / 2
		idx := -1
		for i, ry := range rowCenters {
			d := cy - ry
			if d < 0 { d = -d }
			if d <= 9 { idx = i; break }
		}
		if idx < 0 { rows = append(rows, []ControlInfo{c}); rowCenters = append(rowCenters, cy) } else { rows[idx] = append(rows[idx], c) }
	}
	for i := range rows {
		for a := 0; a < len(rows[i]); a++ {
			for b := a + 1; b < len(rows[i]); b++ {
				if rows[i][b].Rect.Left < rows[i][a].Rect.Left { rows[i][a], rows[i][b] = rows[i][b], rows[i][a] }
			}
		}
	}
	return rows
}

func validateGroupLayout(group string, rows [][]ControlInfo) bool {
	want := []int{}
	if group == "交易資料" { want = []int{3,4,3} } else if group == "送貨資料" { want = []int{1,1,1,2,3,3,3} } else if group == "發票資料(一)" { want = []int{3,3,4,2,1,1,1} } else { return false }
	if len(rows) != len(want) { return false }
	for i := range want { if len(rows[i]) != want[i] { return false } }
	return true
}

func writeLayoutLog(group string, sheet uintptr, rows [][]ControlInfo) {
	logf("INFO", "layout %s sheet=0x%x rows=%d", group, sheet, len(rows))
	for i, row := range rows {
		parts := make([]string,0,len(row)); for j,c := range row { parts = append(parts, fmt.Sprintf("c%d:0x%x/%s",j,c.Hwnd,c.Class)) }
		logf("INFO", "layout %s row%d [%s]", group, i, strings.Join(parts,", "))
	}
}

func tabCenterX(tabName string) (int32, bool) {
	centers := map[string]int32{"交易資料":48,"送貨資料":137,"發票資料(一)":226,"發票資料(二)":316,"其他資料":405,"訂金資料":493,"客戶描述":582,"資料瀏覽":671}
	x,ok := centers[tabName]; return x,ok
}

func activateDevExpressTab(root uintptr, tabName string) bool {
	sheet := findTabSheet(root,tabName); if sheet == 0 { return false }
	if v,_,_ := pIsWindowVisible.Call(sheet); v != 0 { return true }
	page,_,_ := pGetParent.Call(sheet); if page == 0 || !strings.EqualFold(className(page),"TcxPageControl") { return false }
	x,ok := tabCenterX(tabName); if !ok { return false }
	y := int32(12); lp := uintptr(uint32(uint16(x)) | uint32(uint16(y))<<16)
	pSendMessageW.Call(page,WM_LBUTTONDOWN,MK_LBUTTON,lp); pSendMessageW.Call(page,WM_LBUTTONUP,0,lp)
	for i:=0;i<8;i++ { if !interruptibleSleep(50*time.Millisecond){return false}; if v,_,_:=pIsWindowVisible.Call(sheet); v!=0 { logf("INFO","tab activated by message: %s",tabName); return true } }
	pr := rectOf(page); pSetForeground.Call(root); if !interruptibleSleep(100*time.Millisecond){return false}; clickScreenPoint(pr.Left+x,pr.Top+y)
	for i:=0;i<10;i++ { if !interruptibleSleep(60*time.Millisecond){return false}; if v,_,_:=pIsWindowVisible.Call(sheet); v!=0 { logf("INFO","tab activated by real click: %s",tabName); return true } }
	return false
}

func testActivateTab(tabName string) {
	root := findERPWindow(); if root==0 { setStatus("ERP：找不到 COPI08"); return }
	var old POINT; pGetCursorPos.Call(uintptr(unsafe.Pointer(&old))); defer pSetCursorPos.Call(uintptr(old.X),uintptr(old.Y)); pSetForeground.Call(root)
	if !interruptibleSleep(180*time.Millisecond){return}
	if activateDevExpressTab(root,tabName) { setStatus("ERP：已自動切到「"+tabName+"」") } else { setStatus("ERP：切換「"+tabName+"」失敗，請提供 LOG"); logError(tabName,"頁籤","TAB_ACTIVATE_FAILED","測試切頁失敗") }
}

func clickScreenPoint(x,y int32) {
	if isStopRequested(){return}; pSetCursorPos.Call(uintptr(x),uintptr(y)); time.Sleep(35*time.Millisecond); pMouseEvent.Call(MOUSEEVENTF_LEFTDOWN,0,0,0,0); time.Sleep(25*time.Millisecond); pMouseEvent.Call(MOUSEEVENTF_LEFTUP,0,0,0,0)
}

func descendantEdit(hwnd uintptr) uintptr {
	u := strings.ToUpper(className(hwnd)); if strings.Contains(u,"EDIT") { return hwnd }
	var found uintptr
	cb := syscall.NewCallback(func(ch,lParam uintptr) uintptr { if strings.Contains(strings.ToUpper(className(ch)),"EDIT") { found=ch; return 0 }; return 1 })
	pEnumChildWindows.Call(hwnd,cb,0); return found
}

func pressVK(vk uint16) { if isStopRequested(){return}; sendInputs([]INPUT{keyInput(vk,0,0),keyInput(vk,0,KEYEVENTF_KEYUP)}) }

func openComboDropdown() { sendInputs([]INPUT{keyInput(VK_MENU,0,0),keyInput(VK_DOWN,0,0),keyInput(VK_DOWN,0,KEYEVENTF_KEYUP),keyInput(VK_MENU,0,KEYEVENTF_KEYUP)}) }

func comboDisplayText(target ControlInfo) string {
	edit := descendantEdit(target.Hwnd); if edit!=0 { if t:=strings.TrimSpace(getWindowText(edit)); t!="" { return t } }
	return strings.TrimSpace(getWindowText(target.Hwnd))
}

func comboCodeMatches(display,wanted string) bool {
	d:=strings.TrimSpace(display); w:=strings.TrimSpace(wanted); if w=="" { return false }; if d==w { return true }; return strings.HasPrefix(d,w+":") || strings.HasPrefix(d,w+"：") || strings.HasPrefix(d,w+" ")
}

func comboClickArrow(root uintptr,target ControlInfo) {
	if isStopRequested(){return}; r:=target.Rect; prepareERPWindow(root); if !interruptibleSleep(80*time.Millisecond){return}; clickScreenPoint(r.Left+8,(r.Top+r.Bottom)/2); if !interruptibleSleep(70*time.Millisecond){return}; clickScreenPoint(r.Right-9,(r.Top+r.Bottom)/2); interruptibleSleep(140*time.Millisecond)
}

func comboFocus(root uintptr,target ControlInfo) uintptr {
	if isStopRequested() || !prepareERPWindow(root){return 0}; r:=target.Rect; clickScreenPoint(r.Left+8,(r.Top+r.Bottom)/2); if !interruptibleSleep(100*time.Millisecond){return 0}; focus:=focusedControlOfForeground(root); logf("INFO","combo focus target=0x%x/%s focus=0x%x/%s display=%q",target.Hwnd,target.Class,focus,className(focus),comboDisplayText(target)); return focus
}

func comboCycleClosed(root uintptr,target ControlInfo,wanted string,discover bool)([]string,bool){
	if isStopRequested() || comboFocus(root,target)==0 { return nil,false }
	wanted=strings.TrimSpace(wanted); seen:=map[string]bool{}; options:=[]string{}
	add:=func(v string) bool { v=strings.TrimSpace(v); if v==""||seen[v]{return false}; seen[v]=true; options=append(options,v); return true }
	pressVK(VK_HOME); if !interruptibleSleep(150*time.Millisecond){return options,false}; cur:=strings.TrimSpace(comboDisplayText(target)); add(cur); logf("INFO","combo closed-cycle first hwnd=0x%x display=%q",target.Hwnd,cur); if !discover && comboCodeMatches(cur,wanted){return options,true}
	deadline:=time.Now().Add(4*time.Second); stagnant:=0; empty:=0
	for i:=0;i<20 && time.Now().Before(deadline);i++ { if isStopRequested(){return options,false}; pressVK(VK_DOWN); if !interruptibleSleep(140*time.Millisecond){return options,false}; next:=strings.TrimSpace(comboDisplayText(target)); logf("INFO","combo closed-cycle step=%d hwnd=0x%x display=%q",i+1,target.Hwnd,next); if !discover&&comboCodeMatches(next,wanted){return options,true}; if next==""{empty++}else{empty=0}; if next==cur{stagnant++}else{stagnant=0}; if next!=""&&seen[next]{break}; add(next); cur=next; if stagnant>=2||empty>=2{logf("WARN","combo closed-cycle stopped early hwnd=0x%x stagnant=%d empty=%d",target.Hwnd,stagnant,empty);break} }
	return options,false
}

func comboCommitFirst(root uintptr,target ControlInfo) string {
	if isStopRequested(){return ""}; comboClickArrow(root,target); if isStopRequested(){return ""}; pressVK(VK_HOME); if !interruptibleSleep(80*time.Millisecond){return ""}; pressVK(VK_RETURN); if !interruptibleSleep(140*time.Millisecond){return ""}; cur:=strings.TrimSpace(comboDisplayText(target)); logf("INFO","combo first strategy=arrow hwnd=0x%x display=%q",target.Hwnd,cur); if cur!=""{return cur}
	r:=target.Rect; clickScreenPoint(r.Left+8,(r.Top+r.Bottom)/2); if !interruptibleSleep(60*time.Millisecond){return ""}; pressVK(VK_F4); if !interruptibleSleep(120*time.Millisecond){return ""}; pressVK(VK_HOME); if !interruptibleSleep(70*time.Millisecond){return ""}; pressVK(VK_RETURN); if !interruptibleSleep(130*time.Millisecond){return ""}; cur=strings.TrimSpace(comboDisplayText(target)); logf("INFO","combo first strategy=F4 hwnd=0x%x display=%q",target.Hwnd,cur); if cur!=""{return cur}
	openComboDropdown(); if !interruptibleSleep(120*time.Millisecond){return ""}; pressVK(VK_HOME); if !interruptibleSleep(70*time.Millisecond){return ""}; pressVK(VK_RETURN); if !interruptibleSleep(130*time.Millisecond){return ""}; cur=strings.TrimSpace(comboDisplayText(target)); logf("INFO","combo first strategy=alt-down hwnd=0x%x display=%q",target.Hwnd,cur); return cur
}

func comboCommitNext(root uintptr,target ControlInfo) string {
	if isStopRequested(){return ""}; comboClickArrow(root,target); if isStopRequested(){return ""}; pressVK(VK_DOWN); if !interruptibleSleep(70*time.Millisecond){return ""}; pressVK(VK_RETURN); if !interruptibleSleep(130*time.Millisecond){return ""}; return strings.TrimSpace(comboDisplayText(target))
}
