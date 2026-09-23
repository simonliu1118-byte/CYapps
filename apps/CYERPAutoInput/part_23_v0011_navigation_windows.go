//go:build windows

package main

import (
	"sort"
	"syscall"
)

const vkShiftV011 = 0x10

var (
	fieldNavOldProcV011 = map[uintptr]uintptr{}
	fieldNavWndProcV011CB uintptr
)

// setupFieldNavigationV011 makes both Enter and Tab advance through the visible
// CYERPAutoInput value fields. The direct-detail table already has its own
// Enter/Tab cell navigation, so it is intentionally not subclassed here.
func setupFieldNavigationV011() {
	if fieldNavWndProcV011CB == 0 {
		fieldNavWndProcV011CB = syscall.NewCallback(fieldNavWndProcV011)
	}
	for _, f := range fields {
		if f == nil || f.Group == "明細" || f.ValueHwnd == 0 {
			continue
		}
		if _, exists := fieldNavOldProcV011[f.ValueHwnd]; exists {
			continue
		}
		old, _, _ := pSetWindowLongPtrWV12.Call(f.ValueHwnd, gwlpWndProcV12, fieldNavWndProcV011CB)
		if old != 0 {
			fieldNavOldProcV011[f.ValueHwnd] = old
		}
	}
}

func fieldNavWndProcV011(hwnd uintptr, msg uint32, wParam, lParam uintptr) uintptr {
	if msg == wmKeyDownV15 && (wParam == VK_TAB || wParam == VK_RETURN) {
		if moveFocusFromFieldV011(hwnd, shiftDownV011()) {
			return 0
		}
	}
	if old := fieldNavOldProcV011[hwnd]; old != 0 {
		r, _, _ := pCallWindowProcWV12.Call(old, hwnd, uintptr(msg), wParam, lParam)
		return r
	}
	r, _, _ := pDefWindowProcW.Call(hwnd, uintptr(msg), wParam, lParam)
	return r
}

func moveFocusFromFieldV011(hwnd uintptr, backward bool) bool {
	candidates := visibleValueFieldsV011()
	if len(candidates) == 0 {
		return false
	}
	current := -1
	for i, h := range candidates {
		if h == hwnd {
			current = i
			break
		}
	}
	if current < 0 {
		return false
	}

	if !backward && current == len(candidates)-1 && detailGridV15 != 0 {
		vis, _, _ := pIsWindowVisible.Call(detailGridV15)
		en, _, _ := pIsWindowEnabled.Call(detailGridV15)
		if vis != 0 && en != 0 {
			startDetailCellEditorV15(0, 1)
			return true
		}
	}

	next := current + 1
	if backward {
		next = current - 1
	}
	if next >= len(candidates) {
		next = 0
	}
	if next < 0 {
		next = len(candidates) - 1
	}
	pSetFocusV14.Call(candidates[next])
	return true
}

func visibleValueFieldsV011() []uintptr {
	type entry struct {
		hwnd uintptr
		r    RECT
	}
	seen := map[uintptr]bool{}
	items := make([]entry, 0, len(fields))
	for _, f := range fields {
		if f == nil || f.Group == "明細" || f.ValueHwnd == 0 || seen[f.ValueHwnd] {
			continue
		}
		vis, _, _ := pIsWindowVisible.Call(f.ValueHwnd)
		en, _, _ := pIsWindowEnabled.Call(f.ValueHwnd)
		if vis == 0 || en == 0 {
			continue
		}
		r := rectOf(f.ValueHwnd)
		if r.Right <= r.Left || r.Bottom <= r.Top {
			continue
		}
		seen[f.ValueHwnd] = true
		items = append(items, entry{hwnd: f.ValueHwnd, r: r})
	}
	sort.SliceStable(items, func(i, j int) bool {
		ciY := (items[i].r.Top + items[i].r.Bottom) / 2
		cjY := (items[j].r.Top + items[j].r.Bottom) / 2
		if d := ciY - cjY; d < -7 || d > 7 {
			return ciY < cjY
		}
		return items[i].r.Left < items[j].r.Left
	})
	out := make([]uintptr, 0, len(items))
	for _, e := range items {
		out = append(out, e.hwnd)
	}
	return out
}

func shiftDownV011() bool {
	r, _, _ := pGetAsyncKeyState.Call(vkShiftV011)
	return uint16(r)&0x8000 != 0
}
