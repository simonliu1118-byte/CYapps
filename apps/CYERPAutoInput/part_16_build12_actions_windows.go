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
			pSendMessageW.Call(f.ApplyHwnd, BM_SETCHECK, state, 0)
			continue
		}
		if currentUIModeV12 == uiModeStandardV12 && !standardFieldKeysV12[f.Key] {
			pSendMessageW.Call(f.ApplyHwnd, BM_SETCHECK, BST_UNCHECKED, 0)
			continue
		}
		pSendMessageW.Call(f.ApplyHwnd, BM_SETCHECK, state, 0)
	}
	if selected {
		setStatus("已勾選目前模式可見欄位")
	} else {
		setStatus("已取消所有欄位勾選")
	}
}
