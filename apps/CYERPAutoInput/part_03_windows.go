//go:build windows

package main

import (
	"fmt"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

func runAutomation(name string, fn func()) {
	beginAutomation(name)
	defer endAutomation(name)
	fn()
}

func handleCommand(id uint16) {
	switch id {
	case 1001:
		hwnd := findERPWindow()
		if hwnd == 0 {
			setStatus("ERP：找不到 COPI08 視窗")
			logf("WARN", "ERP window not found")
		} else {
			prepareERPWindow(hwnd)
			setStatus("ERP：已找到並帶到前景 0x" + strconv.FormatUint(uint64(hwnd), 16) + "  " + getWindowText(hwnd))
			logf("INFO", "ERP found/foreground requested hwnd=0x%x title=%q", hwnd, getWindowText(hwnd))
		}
	case 1002:
		scanERP()
	case 1003:
		for _, f := range fields {
			pSendMessageW.Call(f.ApplyHwnd, BM_SETCHECK, BST_CHECKED, 0)
		}
		logf("INFO", "all fields selected")
	case 1004:
		for _, f := range fields {
			pSendMessageW.Call(f.ApplyHwnd, BM_SETCHECK, BST_UNCHECKED, 0)
		}
		logf("INFO", "all fields cleared")
	case 1005:
		runAutomation("auto-fill", fillAllSelected)
	case 1006:
		openPath(filepath.Dir(logPath))
	case 1007:
		runAutomation("focus-test", focusTypingTest)
	case 1008:
		runAutomation("tab-delivery", func() { testActivateTab("送貨資料") })
	case 1009:
		runAutomation("tab-invoice1", func() { testActivateTab("發票資料(一)") })
	case 1015:
		runAutomation("tab-transaction", func() { testActivateTab("交易資料") })
	case 1010:
		testERPMode()
	case 1011:
		runAutomation("ensure-input", testEnsureInputMode)
	case 1012:
		readSelectedComboOptions()
	case 1013:
		saveComboSettingsFromUI()
	case 1014:
		openPath(settingsPath)
	case 1016:
		showComboSettingsSummary()
	}
}

func setStatus(s string) { setWindowText(statusHwnd, s) }

func findERPWindow() uintptr {
	var visibleFound, anyFound uintptr
	cb := syscall.NewCallback(func(hwnd, lParam uintptr) uintptr {
		title := getWindowText(hwnd)
		if strings.Contains(title, "銷貨單建立作業") && strings.Contains(strings.ToUpper(title), "COPI08") {
			if anyFound == 0 {
				anyFound = hwnd
			}
			if r, _, _ := pIsWindowVisible.Call(hwnd); r != 0 {
				visibleFound = hwnd
				return 0
			}
		}
		return 1
	})
	pEnumWindows.Call(cb, 0)
	if visibleFound != 0 {
		return visibleFound
	}
	return anyFound
}

func prepareERPWindow(root uintptr) bool {
	if root == 0 {
		return false
	}
	if iconic, _, _ := pIsIconic.Call(root); iconic != 0 {
		pShowWindow.Call(root, SW_RESTORE)
		time.Sleep(220 * time.Millisecond)
	}
	pSetWindowPos.Call(root, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE|SWP_NOSIZE|SWP_SHOWWINDOW)
	pBringWindowToTop.Call(root)
	pSetForeground.Call(root)
	pUpdateWindow.Call(root)
	for i := 0; i < 8; i++ {
		fg, _, _ := pGetForeground.Call()
		if fg == root {
			logf("INFO", "ERP foreground confirmed hwnd=0x%x", root)
			return true
		}
		time.Sleep(60 * time.Millisecond)
		pSetForeground.Call(root)
	}
	fg, _, _ := pGetForeground.Call()
	logf("WARN", "ERP foreground not confirmed target=0x%x foreground=0x%x title=%q", root, fg, getWindowText(fg))
	return false
}

func getWindowText(hwnd uintptr) string {
	// DevExpress F2 grid cells do not expose text through GetWindowText. For the
	// focused F2 TcxGridSite only, V0.0.11 supplies the selected row text from
	// one OCR snapshot plus highlight tracking. Every other HWND keeps the native
	// Win32 path unchanged.
	if text, ok := focusedLookupGridTextV011(hwnd); ok {
		return text
	}
	n, _, _ := pGetWindowTextLenW.Call(hwnd)
	if n == 0 {
		return ""
	}
	buf := make([]uint16, int(n)+2)
	pGetWindowTextW.Call(hwnd, uintptr(unsafe.Pointer(&buf[0])), uintptr(len(buf)))
	return syscall.UTF16ToString(buf)
}

func setWindowText(hwnd uintptr, s string) bool {
	r, _, _ := pSetWindowTextW.Call(hwnd, uintptr(unsafe.Pointer(wstr(s))))
	return r != 0
}

func className(hwnd uintptr) string {
	buf := make([]uint16, 256)
	n, _, _ := pGetClassNameW.Call(hwnd, uintptr(unsafe.Pointer(&buf[0])), uintptr(len(buf)))
	if n == 0 {
		return ""
	}
	return syscall.UTF16ToString(buf[:n])
}

func rectOf(hwnd uintptr) RECT {
	var r RECT
	pGetWindowRect.Call(hwnd, uintptr(unsafe.Pointer(&r)))
	return r
}

func enumControls(root uintptr) []ControlInfo {
	out := []ControlInfo{}
	cb := syscall.NewCallback(func(hwnd, lParam uintptr) uintptr {
		parent, _, _ := pGetParent.Call(hwnd)
		vis, _, _ := pIsWindowVisible.Call(hwnd)
		en, _, _ := pIsWindowEnabled.Call(hwnd)
		out = append(out, ControlInfo{Hwnd: hwnd, Parent: parent, Class: className(hwnd), Text: getWindowText(hwnd), Rect: rectOf(hwnd), Visible: vis != 0, Enabled: en != 0})
		return 1
	})
	pEnumChildWindows.Call(root, cb, 0)
	return out
}

func scanERP() {
	root := findERPWindow()
	if root == 0 {
		setStatus("ERP：掃描失敗，找不到視窗")
		logError("掃描", "ERP", "WINDOW_NOT_FOUND", "找不到 COPI08 主視窗")
		return
	}
	setStatus("ERP：掃描中…")
	controls := enumControls(root)
	dir := filepath.Dir(logPath)
	path := filepath.Join(dir, "control-scan_"+time.Now().Format("20060102_150405")+".txt")
	f, err := os.Create(path)
	if err != nil {
		logError("掃描", "控制項", "CREATE_SCAN_FAILED", err.Error())
		return
	}
	defer f.Close()
	rr := rectOf(root)
	fmt.Fprintf(f, "CY SMART ERP Prototype control scan\nTime: %s\nWindow: %s\nHWND: 0x%x\nRootRect: %d,%d,%d,%d\nCount: %d\n\n", time.Now().Format(time.RFC3339), getWindowText(root), root, rr.Left, rr.Top, rr.Right, rr.Bottom, len(controls))
	for i, c := range controls {
		text := c.Text
		if isEditableClass(c.Class) && text != "" {
			text = "<masked>"
		}
		fmt.Fprintf(f, "%04d hwnd=0x%x parent=0x%x class=%q visible=%t enabled=%t rect=%d,%d,%d,%d text=%q\n", i, c.Hwnd, c.Parent, c.Class, c.Visible, c.Enabled, c.Rect.Left-rr.Left, c.Rect.Top-rr.Top, c.Rect.Right-rr.Left, c.Rect.Bottom-rr.Top, text)
	}
	logf("INFO", "control scan complete: %d controls -> %s", len(controls), path)
	setStatus(fmt.Sprintf("ERP：掃描完成，共 %d 個控制項", len(controls)))
}

func isEditableClass(cls string) bool {
	u := strings.ToUpper(cls)
	return strings.Contains(u, "EDIT") || strings.Contains(u, "COMBO") || strings.Contains(u, "MEMO") || strings.Contains(u, "MASK") || strings.Contains(u, "SPIN") || strings.Contains(u, "DATE")
}

func isCandidateClass(cls string) bool {
	u := strings.ToUpper(cls)
	return strings.Contains(u, "EDIT") || strings.Contains(u, "COMBO") || strings.Contains(u, "MEMO") || strings.Contains(u, "MASK") || strings.Contains(u, "SPIN") || strings.Contains(u, "DATE") || strings.Contains(u, "LOOKUP")
}

func normalize(s string) string {
	r := strings.NewReplacer(" ", "", "_", "", "（", "(", "）", ")", "：", "", ":", "")
	return strings.ToUpper(strings.TrimSpace(r.Replace(s)))
}

func findLabelControl(controls []ControlInfo, aliases []string) *ControlInfo {
	for _, a := range aliases {
		na := normalize(a)
		for i := range controls {
			c := &controls[i]
			if !c.Visible {
				continue
			}
			if normalize(c.Text) == na {
				return c
			}
		}
	}
	return nil
}

func findInputRightOf(label *ControlInfo, controls []ControlInfo) *ControlInfo {
	ly := (label.Rect.Top + label.Rect.Bottom) / 2
	var best *ControlInfo
	bestScore := int64(1 << 62)
	for i := range controls {
		c := &controls[i]
		if !c.Visible || !c.Enabled || !isCandidateClass(c.Class) {
			continue
		}
		cy := (c.Rect.Top + c.Rect.Bottom) / 2
		dy := int64(cy - ly)
		if dy < 0 {
			dy = -dy
		}
		if dy > 24 {
			continue
		}
		dx := int64(c.Rect.Left - label.Rect.Right)
		if dx < -20 || dx > 650 {
			continue
		}
		score := dy*20 + dx
		if score < 0 {
			score = -score + 200
		}
		if score < bestScore {
			bestScore = score
			best = c
		}
	}
	return best
}

func setTargetValue(c *ControlInfo, value string) bool {
	u := strings.ToUpper(c.Class)
	if strings.Contains(u, "COMBO") {
		pSendMessageW.Call(c.Hwnd, CB_SELECTSTRING, ^uintptr(0), uintptr(unsafe.Pointer(wstr(value))))
		time.Sleep(40 * time.Millisecond)
	}
	r, _, _ := pSendMessageW.Call(c.Hwnd, WM_SETTEXT, 0, uintptr(unsafe.Pointer(wstr(value))))
	return r != 0
}

func fillField(root uintptr, f *Field) bool {
	if checked(f.ApplyHwnd) == false {
		return true
	}
	controls := enumControls(root)
	label := findLabelControl(controls, f.Aliases)
	if label == nil {
		logError(f.Group, f.Label, "LABEL_NOT_FOUND", "目前頁面找不到欄位標籤；可能是自繪控制項或尚未切到正確頁籤")
		return false
	}
	if f.Kind == "bool" {
		desired := checked(f.ValueHwnd)
		if strings.Contains(strings.ToUpper(label.Class), "BUTTON") {
			state := uintptr(BST_UNCHECKED)
			if desired {
				state = BST_CHECKED
			}
			pSendMessageW.Call(label.Hwnd, BM_SETCHECK, state, 0)
			logf("INFO", "filled %s/%s (checkbox=%t)", f.Group, f.Label, desired)
			return true
		}
		logError(f.Group, f.Label, "CHECKBOX_NOT_FOUND", "找到文字但不是可直接設定的 checkbox")
		return false
	}
	input := findInputRightOf(label, controls)
	if input == nil {
		logError(f.Group, f.Label, "INPUT_NOT_FOUND", fmt.Sprintf("label hwnd=0x%x class=%s", label.Hwnd, label.Class))
		return false
	}
	val := getWindowText(f.ValueHwnd)
	if setTargetValue(input, val) {
		logf("INFO", "filled %s/%s -> hwnd=0x%x class=%s", f.Group, f.Label, input.Hwnd, input.Class)
		return true
	}
	logError(f.Group, f.Label, "SET_FAILED", fmt.Sprintf("target hwnd=0x%x class=%s", input.Hwnd, input.Class))
	return false
}

func checked(hwnd uintptr) bool {
	r, _, _ := pSendMessageW.Call(hwnd, BM_GETCHECK, 0, 0)
	return r == BST_CHECKED
}

func selectedInGroup(group string) bool {
	for _, f := range fields {
		if f.Group == group && checked(f.ApplyHwnd) {
			return true
		}
	}
	return false
}

func fillGroup(root uintptr, groups ...string) (ok, fail int) {
	set := map[string]bool{}
	for _, g := range groups {
		set[g] = true
	}
	for _, f := range fields {
		if set[f.Group] && checked(f.ApplyHwnd) {
			if fillField(root, f) {
				ok++
			} else {
				fail++
			}
			time.Sleep(60 * time.Millisecond)
		}
	}
	return
}

func findTabControl(root uintptr) uintptr {
	ctrls := enumControls(root)
	for _, c := range ctrls {
		if strings.EqualFold(c.Class, "SysTabControl32") && c.Visible {
			return c.Hwnd
		}
	}
	return 0
}

func selectTab(root uintptr, index int) bool {
	tab := findTabControl(root)
	if tab == 0 {
		return false
	}
	count, _, _ := pSendMessageW.Call(tab, TCM_GETITEMCOUNT, 0, 0)
	if int(count) <= index {
		return false
	}
	var r RECT
	ok, _, _ := pSendMessageW.Call(tab, TCM_GETITEMRECT, uintptr(index), uintptr(unsafe.Pointer(&r)))
	if ok == 0 {
		return false
	}
	x := (r.Left + r.Right) / 2
	y := (r.Top + r.Bottom) / 2
	lp := uintptr(uint32(uint16(x)) | uint32(uint16(y))<<16)
	pSendMessageW.Call(tab, WM_LBUTTONDOWN, MK_LBUTTON, lp)
	pSendMessageW.Call(tab, WM_LBUTTONUP, 0, lp)
	time.Sleep(250 * time.Millisecond)
	return true
}

func anySelected() bool {
	for _, f := range fields {
		if checked(f.ApplyHwnd) {
			return true
		}
	}
	return false
}
