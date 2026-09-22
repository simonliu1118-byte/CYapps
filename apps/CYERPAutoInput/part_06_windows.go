//go:build windows

package main

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"
	"unicode/utf16"
	"unsafe"
)

func selectDevExpressComboByCode(root uintptr, target ControlInfo, wanted string) bool {
	wanted = strings.TrimSpace(wanted)
	if wanted == "" || isStopRequested() { return false }
	original := strings.TrimSpace(comboDisplayText(target))
	if comboCodeMatches(original, wanted) { logf("INFO", "combo already matches hwnd=0x%x code=<configured> display=%q", target.Hwnd, original); return true }
	if _, ok := comboCycleClosed(root,target,wanted,false); ok { logf("INFO", "combo selected by closed-cycle hwnd=0x%x", target.Hwnd); return true }
	if isStopRequested(){ return false }
	current := comboCommitFirst(root,target); logf("INFO", "combo popup probe hwnd=0x%x step=first display=%q", target.Hwnd,current)
	if comboCodeMatches(current,wanted){ return true }
	if current=="" { logf("WARN", "combo popup probe aborted: display text unreadable hwnd=0x%x", target.Hwnd); return false }
	seen:=map[string]bool{current:true}; deadline:=time.Now().Add(5*time.Second); empty:=0
	for step:=1; step<=20 && time.Now().Before(deadline); step++ {
		if isStopRequested(){return false}; next:=comboCommitNext(root,target); logf("INFO", "combo popup probe hwnd=0x%x step=%d display=%q", target.Hwnd,step,next)
		if comboCodeMatches(next,wanted){return true}; if next==""{empty++}else{empty=0}; if next!=""&&seen[next]{break}; if next!=""{seen[next]=true}; current=next; if empty>=2{break}
	}
	logf("WARN", "combo selection failed hwnd=0x%x original=%q last=%q", target.Hwnd,original,current); return false
}

func setTextControlByFocus(root uintptr,target ControlInfo,value string) bool {
	if isStopRequested(){return false}; r:=target.Rect; clickScreenPoint((r.Left+r.Right)/2,(r.Top+r.Bottom)/2); time.Sleep(80*time.Millisecond); edit:=descendantEdit(target.Hwnd); focus:=focusedControlOfForeground(root)
	if focus!=0 && strings.Contains(strings.ToUpper(className(focus)),"EDIT") { edit=focus }
	if edit==0{return false}; pSendMessageW.Call(edit,EM_SETSEL,0,^uintptr(0)); time.Sleep(20*time.Millisecond); units:=utf16.Encode([]rune(value)); for _,u:=range units { pSendMessageW.Call(edit,WM_CHAR,uintptr(u),0) }; time.Sleep(80*time.Millisecond)
	after:=getWindowText(edit); if after==value{return true}; ret,_,_:=pSendMessageW.Call(edit,WM_SETTEXT,0,uintptr(unsafe.Pointer(wstr(value)))); time.Sleep(80*time.Millisecond); after2:=getWindowText(edit); if ret!=0&&after2==value{return true}
	if strings.Contains(strings.ToUpper(target.Class),"COMBO") && (strings.HasPrefix(after2,value)||strings.HasPrefix(after,value)){return true}; return false
}

func clearFocusedEditNoShortcut(focus uintptr) bool {
	if focus==0||isStopRequested(){return false}; current:=getWindowText(focus); sendInputs([]INPUT{keyInput(VK_END,0,0),keyInput(VK_END,0,KEYEVENTF_KEYUP)}); if !interruptibleSleep(30*time.Millisecond){return false}
	n:=len([]rune(current))+8; if n<16{n=16}; if n>256{n=256}; for i:=0;i<n;i++ { if isStopRequested(){return false}; sendInputs([]INPUT{keyInput(VK_BACK,0,0),keyInput(VK_BACK,0,KEYEVENTF_KEYUP)}) }
	return interruptibleSleep(40*time.Millisecond)
}

func sendDigitKeys(s string) bool {
	for _,r:=range s { if isStopRequested(){return false}; if r<'0'||r>'9'{return false}; vk:=uint16(0x30+(r-'0')); sendInputs([]INPUT{keyInput(vk,0,0),keyInput(vk,0,KEYEVENTF_KEYUP)}); if !interruptibleSleep(18*time.Millisecond){return false} }
	return true
}

func normalizeDateInput(value string)(digits,expected string,ok bool){
	for _,r:=range value { if r>='0'&&r<='9'{digits+=string(r)} }; if len(digits)!=8{return digits,"",false}; expected=digits[:4]+"/"+digits[4:6]+"/"+digits[6:8]; return digits,expected,true
}

func setDateControlInteractive(root uintptr,target ControlInfo,value string) bool {
	digits,expected,ok:=normalizeDateInput(strings.TrimSpace(value)); if !ok||isStopRequested()||!prepareERPWindow(root){logf("WARN","date input rejected before typing format_len=%d",len([]rune(value)));return false}
	r:=target.Rect; clickScreenPoint((r.Left+r.Right)/2,(r.Top+r.Bottom)/2); if !interruptibleSleep(100*time.Millisecond){return false}; focus:=focusedControlOfForeground(root); if focus==0{return false}; if !clearFocusedEditNoShortcut(focus)||!sendDigitKeys(digits){return false}
	sendInputs([]INPUT{keyInput(VK_TAB,0,0),keyInput(VK_TAB,0,KEYEVENTF_KEYUP)}); if !interruptibleSleep(260*time.Millisecond){return false}; after:=strings.TrimSpace(getWindowText(target.Hwnd)); if after==""{after=strings.TrimSpace(getWindowText(focus))}; if after==expected{logf("INFO","date normalized by ERP hwnd=0x%x",target.Hwnd);return true}
	commitHeaderField(root); if !interruptibleSleep(220*time.Millisecond){return false}; after=strings.TrimSpace(getWindowText(target.Hwnd)); if after==""{after=strings.TrimSpace(getWindowText(focus))}; if after==expected{logf("INFO","date normalized by ERP after blank click hwnd=0x%x",target.Hwnd);return true}; logf("WARN","date normalization not confirmed hwnd=0x%x after_len=%d",target.Hwnd,len([]rune(after))); return false
}

func setTextControlInteractive(root uintptr,target ControlInfo,value string) bool {
	if isStopRequested(){return false}; if !prepareERPWindow(root){return false}; r:=target.Rect; clickScreenPoint((r.Left+r.Right)/2,(r.Top+r.Bottom)/2); time.Sleep(100*time.Millisecond); focus:=focusedControlOfForeground(root); if focus==0{logf("WARN","interactive text no focus target=0x%x class=%q",target.Hwnd,target.Class);return false}
	if !clearFocusedEditNoShortcut(focus){return false}; sendUnicodeText(value); time.Sleep(120*time.Millisecond); edit:=focus; if !strings.Contains(strings.ToUpper(className(edit)),"EDIT") { if d:=descendantEdit(target.Hwnd); d!=0{edit=d} }; after:=getWindowText(edit); if after==value{return true}; ret,_,_:=pSendMessageW.Call(edit,WM_SETTEXT,0,uintptr(unsafe.Pointer(wstr(value)))); time.Sleep(80*time.Millisecond); return ret!=0&&getWindowText(edit)==value
}

func commitHeaderField(root uintptr){ r:=rectOf(root); w:=r.Right-r.Left; x:=r.Left+w*68/100; y:=r.Top+185; if y>=r.Bottom-20{y=r.Top+120}; clickScreenPoint(x,y); time.Sleep(220*time.Millisecond) }

func setCheckboxControl(target ControlInfo,desired bool) bool {
	if !strings.Contains(strings.ToUpper(target.Class),"CHECKBOX"){return false}; cur,_,_:=pSendMessageW.Call(target.Hwnd,BM_GETCHECK,0,0); want:=uintptr(BST_UNCHECKED); if desired{want=BST_CHECKED}; if cur==want{return true}; r:=target.Rect; clickScreenPoint((r.Left+r.Right)/2,(r.Top+r.Bottom)/2); time.Sleep(80*time.Millisecond); cur2,_,_:=pSendMessageW.Call(target.Hwnd,BM_GETCHECK,0,0); return cur2==want
}

func clickBlankArea(sheet uintptr){ r:=rectOf(sheet); x:=r.Left+1050; if x>r.Right-20{x=r.Right-20}; y:=r.Top+105; if y>r.Bottom-20{y=r.Bottom-20}; clickScreenPoint(x,y) }

func initSettings(){ exe,_:=os.Executable(); base:=filepath.Dir(exe); dir:=filepath.Join(base,"config"); _=os.MkdirAll(dir,0755); settingsPath=filepath.Join(dir,"settings.json"); settings=AppSettings{Version:1,Combos:map[string]ComboSetting{}}; b,err:=os.ReadFile(settingsPath); if err==nil&&len(b)>0 { var loaded AppSettings; if json.Unmarshal(b,&loaded)==nil { if loaded.Combos==nil{loaded.Combos=map[string]ComboSetting{}}; if loaded.Version==0{loaded.Version=1}; settings=loaded; return } }; _=writeSettings() }

func writeSettings() error { if settings.Combos==nil{settings.Combos=map[string]ComboSetting{}}; settings.Version=1; b,err:=json.MarshalIndent(settings,"","  "); if err!=nil{return err}; return os.WriteFile(settingsPath,append(b,'\n'),0644) }

func applySettingsToUI(){ for _,f:=range fields { if f.Kind!="combo"||f.ValueHwnd==0{continue}; if cs,ok:=settings.Combos[f.Key]; ok&&strings.TrimSpace(cs.Selected)!=""{setWindowText(f.ValueHwnd,cs.Selected)} } }

func saveComboSettingsFromUI(){ changed:=0; for _,f:=range fields { if f.Kind!="combo"{continue}; cs:=settings.Combos[f.Key]; cs.Selected=strings.TrimSpace(getWindowText(f.ValueHwnd)); settings.Combos[f.Key]=cs; changed++ }; if err:=writeSettings(); err!=nil{setStatus("設定：儲存失敗，請提供除錯紀錄");logError("設定","settings.json","SAVE_FAILED",err.Error());return}; setStatus(fmt.Sprintf("設定：已儲存 %d 個下拉欄位設定（本機）",changed)); logf("INFO","combo settings saved fields=%d path=%s",changed,settingsPath) }

func windowStyle(hwnd uintptr) uint32 { r,_,_:=pGetWindowLongW.Call(hwnd,uintptr(^uint32(15))); return uint32(r) }

func headerStateEdits(root uintptr) []ControlInfo { rr:=rectOf(root); all:=enumControls(root); out:=make([]ControlInfo,0,8); for _,c:=range all { if !c.Visible||!strings.EqualFold(c.Class,"TDBEdit"){continue}; top:=c.Rect.Top-rr.Top; if top>=150&&top<=235{out=append(out,c)} }; return out }

func detectERPMode(root uintptr) ERPMode {
	edits:=headerStateEdits(root); if len(edits)<3{logf("WARN","state detect insufficient header edits count=%d",len(edits));return ERPModeUnknown}; ro,rw:=0,0
	for _,c:=range edits { style:=windowStyle(c.Hwnd); readonly:=style&ES_READONLY!=0; if readonly{ro++}else{rw++}; logf("INFO","state signal hwnd=0x%x class=%s readonly=%t style=0x%08x enabled=%t",c.Hwnd,c.Class,readonly,style,c.Enabled) }
	if ro>=2&&rw<=1{logf("INFO","ERP mode detected=BROWSE readonly=%d writable=%d",ro,rw);return ERPModeBrowse}; if rw>=2{logf("INFO","ERP mode detected=INPUT readonly=%d writable=%d",ro,rw);return ERPModeInput}; logf("WARN","ERP mode detected=UNKNOWN readonly=%d writable=%d",ro,rw); return ERPModeUnknown
}

func findRibbonEditGroup(root uintptr)*ControlInfo { all:=enumControls(root); for i:=range all { c:=&all[i]; if c.Visible&&strings.EqualFold(c.Class,"TdxRibbonGroupBarControl")&&strings.TrimSpace(c.Text)=="編輯"{return c} }; return nil }
