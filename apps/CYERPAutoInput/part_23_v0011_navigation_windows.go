//go:build windows

package main

import "sort"

const vkShiftV011 = 0x10

// handleAppFieldNavigationV011 gives the CYERPAutoInput form spreadsheet-like
// keyboard flow. Tab and Enter both advance through visible value fields; Shift
// reverses direction. The direct-detail cell editor already has its own
// Enter/Tab behavior and is deliberately left to detailCellEditWndProcV15.
func handleAppFieldNavigationV011(msg *MSG) bool {
	if msg == nil || msg.Message != wmKeyDownV15 {
		return false
	}
	if msg.WParam != VK_TAB && msg.WParam != VK_RETURN {
		return false
	}
	if msg.Hwnd == 0 || msg.Hwnd == detailCellEditV15 {
		return false
	}

	candidates := visibleValueFieldsV011()
	current := -1
	for i, hwnd := range candidates {
		if hwnd == msg.Hwnd {
			current = i
			break
		}
	}
	if current < 0 {
		if msg.Hwnd == detailGridV15 && detailGridV15 != 0 {
			startDetailCellEditorV15(0, 1)
			return true
		}
		return false
	}

	backward := shiftDownV011()
	if !backward && current == len(candidates)-1 && detailGridV15 != 0 {
		vis, _, _ := pIsWindowVisible.Call(detailGridV15)
		en, _, _ := pIsWindowEnabled.Call(detailGridV15)
		if vis != 0 && en != 0 {
			startDetailCellEditorV15(0, 1)
			return true
		}
	}

	if len(candidates) <= 1 {
		return true
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
