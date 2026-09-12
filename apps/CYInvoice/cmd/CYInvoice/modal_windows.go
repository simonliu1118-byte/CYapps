//go:build windows

package main

import (
	"fmt"
	"unsafe"
)

// runOwnedModalWindow centralizes the modal lifecycle used by CYInvoice's
// auxiliary windows. The owner is always restored even when GetMessageW fails,
// and WM_QUIT is reposted so the outer application message loop can still
// terminate normally.
//
// IsDialogMessageW is used only for dialog-style keyboard navigation (Tab,
// Shift+Tab, radio navigation). Enter is deliberately dispatched to the focused
// control first so each CYInvoice window can define a safe, explicit Enter
// action instead of accidentally invoking an unrelated default action.
func runOwnedModalWindow(owner, window, initialFocus uintptr) error {
	if window == 0 {
		return fmt.Errorf("modal window is nil")
	}
	if owner != 0 {
		procEnableWindow.Call(owner, 0)
	}
	defer restoreModalOwner(owner)

	procShowWindow.Call(window, swShow)
	procUpdateWindow.Call(window)
	if initialFocus != 0 {
		procSetFocus.Call(initialFocus)
	}

	var msg message
	for {
		alive, _, _ := procIsWindow.Call(window)
		if alive == 0 {
			return nil
		}
		result, _, messageErr := procGetMessageW.Call(uintptr(unsafe.Pointer(&msg)), 0, 0, 0)
		if int32(result) == -1 {
			return fmt.Errorf("GetMessageW: %v", messageErr)
		}
		if result == 0 {
			procPostQuitMessage.Call(0)
			return nil
		}

		// Explicit Enter handlers (first-time setup, settings authentication,
		// etc.) must receive VK_RETURN. Do not let dialog translation turn Enter
		// into an implicit default-button click before those handlers run.
		if msg.Message == wmKeyDown && msg.WParam == vkReturn {
			procTranslateMessage.Call(uintptr(unsafe.Pointer(&msg)))
			procDispatchMessageW.Call(uintptr(unsafe.Pointer(&msg)))
			continue
		}
		if handled, _, _ := procIsDialogMessageW.Call(window, uintptr(unsafe.Pointer(&msg))); handled != 0 {
			continue
		}
		procTranslateMessage.Call(uintptr(unsafe.Pointer(&msg)))
		procDispatchMessageW.Call(uintptr(unsafe.Pointer(&msg)))
	}
}

func restoreModalOwner(owner uintptr) {
	if owner == 0 {
		return
	}
	alive, _, _ := procIsWindow.Call(owner)
	if alive == 0 {
		return
	}
	procEnableWindow.Call(owner, 1)
	procSetFocus.Call(owner)
}

func forgetControlIDs(ids ...int) {
	for _, id := range ids {
		handle := handles[id]
		if handle != 0 {
			delete(baseRects, handle)
			delete(colorStyles, handle)
		}
		delete(handles, id)
	}
}
