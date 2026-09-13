//go:build windows

package main

import (
	"fmt"
	"runtime"
	"runtime/debug"
	"syscall"
	"unsafe"
)

func wndProc(hwnd uintptr, msg uint32, wParam, lParam uintptr) (ret uintptr) {
	defer func() {
		if r := recover(); r != nil {
			panicText := fmt.Sprintf("wndProc panic: %v\n%s", r, debug.Stack())
			traceLog("WNDPROC_PANIC", "%s", panicText)
			traceLog("WNDPROC_STACK", "%s", panicText)
			showMessage(appTitle, "程式發生例外，請上傳 EXE 同資料夾的 Trace LOG。", MB_OK|MB_ICONERROR)
			ret = 0
		}
	}()

	switch msg {
	case WM_CREATE:
		traceLog("WM_CREATE_START", "hwnd=%d", hwnd)
		mainHwnd = hwnd
		appReady = false
		createControls()
		currentResult = calcResult{Valid: true}
		posted, _, _ := procPostMessageW.Call(hwnd, WM_APP_READY, 0, 0)
		traceLog("WM_CREATE_END", "controls=%d ready_post=%d", len(orderedEdits), posted)
		return 0
	case WM_APP_READY:
		appReady = true
		traceLog("APPLICATION_READY", "")
		return 0
	case WM_APP_RECALC:
		pendingRecalc = false
		if appReady { recalc(false) }
		return 0
	case WM_APP_FOCUS:
		pendingFocus = false
		if !appReady || len(orderedEdits)==0 { return 0 }
		order:=pendingFocusOrder;pendingFocusOrder=-1;next:=0;if order>=0{next=order+1;if next>=len(orderedEdits){next=0}}
		suppressKillFocus=true;procSetFocus.Call(orderedEdits[next]);suppressKillFocus=false;scheduleRecalc();return 0
	case WM_COMMAND:
		id:=int(loword(wParam));code:=hiword(wParam);if id==ID_CLEAR&&code==BN_CLICKED{clearAll();return 0};if code==EN_KILLFOCUS{if appReady&&!suppressKillFocus&&!inRecalc{scheduleRecalc()};return 0}
	case WM_PAINT:
		paintWindow(hwnd);return 0
	case WM_ERASEBKGND:
		return 1
	case WM_CLOSE:
		appReady=false;procDefWindowProcW.Call(hwnd,uintptr(msg),wParam,lParam);return 0
	case WM_DESTROY:
		appReady=false;traceNormal=true;procPostQuitMessage.Call(0);return 0
	}
	r,_,_:=procDefWindowProcW.Call(hwnd,uintptr(msg),wParam,lParam);return r
}

func main(){
	runtime.LockOSThread();defer runtime.UnlockOSThread();initTraceLog();defer func(){closeTraceLog(traceNormal)}();defer func(){if r:=recover();r!=nil{traceNormal=false;panicText:=fmt.Sprintf("main panic: %v\n%s",r,debug.Stack());traceLog("MAIN_PANIC","%s",panicText);traceLog("MAIN_STACK","%s",panicText)}}()
	h,_,_:=procGetModuleHandleW.Call(0);hInstance=h
	mainBrush,_,_=procCreateSolidBrush.Call(rgb(238,242,244));panelBrush,_,_=procCreateSolidBrush.Call(rgb(250,251,252));invoiceBrush,_,_=procCreateSolidBrush.Call(rgb(255,255,253));editBrush,_,_=procCreateSolidBrush.Call(rgb(255,255,255));headerBrush,_,_=procCreateSolidBrush.Call(rgb(75,113,124));accentBrush,_,_=procCreateSolidBrush.Call(rgb(225,241,239));errorBrush,_,_=procCreateSolidBrush.Call(rgb(255,235,235))
	fontTitle=createFont(-28,FW_BOLD,"Microsoft JhengHei UI");fontSection=createFont(-22,FW_SEMIBOLD,"Microsoft JhengHei UI");fontNormal=createFont(-18,FW_NORMAL,"Microsoft JhengHei UI");fontSmall=createFont(-15,FW_NORMAL,"Microsoft JhengHei UI");fontInvoiceTitle=createFont(-29,FW_BOLD,"Microsoft JhengHei");fontInvoiceHeader=createFont(-17,FW_SEMIBOLD,"Microsoft JhengHei");fontInvoiceText=createFont(-17,FW_NORMAL,"Microsoft JhengHei");fontInvoiceSmall=createFont(-15,FW_NORMAL,"Microsoft JhengHei");fontInvoiceDigit=createFont(-34,FW_BOLD,"Microsoft JhengHei");fontInvoiceAmountUnit=createFont(-15,FW_SEMIBOLD,"Microsoft JhengHei")
	className:=utf16Ptr("InvoiceCalcWindowClass");cursor,_,_:=procLoadCursorW.Call(0,IDC_ARROW);appIcon,_,_:=procLoadIconW.Call(hInstance,IDI_APP);wc:=WNDCLASSEX{CbSize:uint32(unsafe.Sizeof(WNDCLASSEX{})),Style:CS_HREDRAW|CS_VREDRAW,LpfnWndProc:syscall.NewCallback(wndProc),HInstance:hInstance,HIcon:appIcon,HCursor:cursor,HbrBackground:mainBrush,LpszClassName:className,HIconSm:appIcon};if r,_,_:=procRegisterClassExW.Call(uintptr(unsafe.Pointer(&wc)));r==0{return}
	screenW,_,_:=procGetSystemMetrics.Call(0);screenH,_,_:=procGetSystemMetrics.Call(1);winW,winH:=int32(1228),int32(730);x:=int32(screenW)/2-winW/2;y:=int32(screenH)/2-winH/2;if x<0{x=0};if y<0{y=0}
	hwnd,_,_:=procCreateWindowExW.Call(0,uintptr(unsafe.Pointer(className)),uintptr(unsafe.Pointer(utf16Ptr(appTitle))),WS_OVERLAPPED|WS_CAPTION|WS_SYSMENU|WS_MINIMIZEBOX|WS_VISIBLE,uintptr(x),uintptr(y),uintptr(winW),uintptr(winH),0,0,hInstance,0);if hwnd==0{return};mainHwnd=hwnd;if appIcon!=0{procSendMessageW.Call(hwnd,WM_SETICON,ICON_BIG,appIcon);procSendMessageW.Call(hwnd,WM_SETICON,ICON_SMALL,appIcon)};procShowWindow.Call(hwnd,SW_SHOW);procUpdateWindow.Call(hwnd)
	var msg MSG;for{r,_,_:=procGetMessageW.Call(uintptr(unsafe.Pointer(&msg)),0,0,0);if int32(r)==-1{traceNormal=false;break};if r==0{break};procTranslateMessage.Call(uintptr(unsafe.Pointer(&msg)));procDispatchMessageW.Call(uintptr(unsafe.Pointer(&msg)))}
	for _,obj:=range []uintptr{mainBrush,panelBrush,invoiceBrush,editBrush,headerBrush,accentBrush,errorBrush,fontTitle,fontSection,fontNormal,fontSmall,fontInvoiceTitle,fontInvoiceHeader,fontInvoiceText,fontInvoiceSmall,fontInvoiceDigit,fontInvoiceAmountUnit}{if obj!=0{procDeleteObject.Call(obj)}}
}
