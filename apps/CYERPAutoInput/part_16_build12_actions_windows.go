//go:build windows

package main

import "strings"

func selectVisibleFieldsV12(selected bool) {
	state := uintptr(BST_UNCHECKED)
	if selected {
		state = BST_CHECKED
	}
	for _, f := range fields {
		if f == nil || f.ApplyHwnd == 0 {
			continue
		}
		if strings.HasPrefix(f.Key, "detail_r") {
			// Build 14 detail rows live in a ListView. Keep only the hidden legacy
			// marker in sync here; actual ListView checkboxes are handled below.
			pSendMessageW.Call(f.ApplyHwnd, BM_SETCHECK, state, 0)
			continue
		}
		if currentUIModeV12 == uiModeStandardV12 && !standardFieldKeysV12[f.Key] {
			pSendMessageW.Call(f.ApplyHwnd, BM_SETCHECK, BST_UNCHECKED, 0)
			continue
		}
		pSendMessageW.Call(f.ApplyHwnd, BM_SETCHECK, state, 0)
	}
	setAllDetailListChecksV14(selected)
	if selected {
		setStatus("已勾選目前模式可見欄位與商品明細")
	} else {
		setStatus("已取消所有欄位與商品明細")
	}
}
