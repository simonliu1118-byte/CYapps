//go:build windows

package main

import "unsafe"

// CVT icon: #F09D03. The business UI uses the shared Apricot palette, with
// a darker primary-button shade so white action text stays legible.
const (
    colorWhite            uintptr = 0x00FFFFFF // #FFFFFF
    colorWindowBackground uintptr = 0x00FCFAF8 // #F8FAFC
    colorTextPrimary      uintptr = 0x0037291F // #1F2937
    colorTextSecondary    uintptr = 0x00857066 // #667085
    colorCVTAccent        uintptr = 0x004A84D8 // #D8844A
    colorPrimaryButton    uintptr = 0x001D5092 // #92501D
    colorPrimaryPressed   uintptr = 0x000F376F // #6F370F
    colorDisabledSurface  uintptr = 0x00F5F3F1 // #F1F3F5

    IMAGE_ICON         = 1
    BS_OWNERDRAW       = 0x0000000B
    WM_DRAWITEM        = 0x002B
    SPI_GETHIGHCONTRAST = 0x0042
    HCF_HIGHCONTRASTON = 1
    COLOR_BTNTEXT      = 18
    COLOR_BTNFACE      = 15
    ODT_BUTTON         = 4
    ODS_SELECTED       = 0x0001
    ODS_DISABLED       = 0x0004
    ODS_FOCUS          = 0x0010
    CDIS_SELECTED      = 0x0001
    DT_CENTER          = 0x0001
    DT_VCENTER         = 0x0004
    DT_SINGLELINE      = 0x0020
    PBM_SETBARCOLOR    = 0x0409
    LVM_SETBKCOLOR     = LVM_FIRST + 1
    LVM_SETTEXTCOLOR   = LVM_FIRST + 36
    LVM_SETTEXTBKCOLOR = LVM_FIRST + 38
)

type HIGHCONTRASTW struct {
    CbSize uint32
    DwFlags uint32
    LpszDefaultScheme *uint16
}

type DRAWITEMSTRUCT struct {
    CtlType uint32
    CtlID uint32
    ItemID uint32
    ItemAction uint32
    ItemState uint32
    HwndItem uintptr
    Hdc uintptr
    RcItem RECT
    ItemData uintptr
}

func isHighContrastEnabled() bool {
    var hc HIGHCONTRASTW
    hc.CbSize = uint32(unsafe.Sizeof(hc))
    ok, _, _ := pSystemParametersInfoW.Call(SPI_GETHIGHCONTRAST, uintptr(hc.CbSize), uintptr(unsafe.Pointer(&hc)), 0)
    return ok != 0 && hc.DwFlags&HCF_HIGHCONTRASTON != 0
}

func createWindowBackgroundBrush() uintptr {
    if !isHighContrastEnabled() {
        brush, _, _ := pCreateSolidBrush.Call(colorWindowBackground)
        if brush != 0 {
            return brush // owned by the window class for its entire process lifetime
        }
    }
    brush, _, _ := pGetSysColorBrush.Call(COLOR_WINDOW)
    return brush
}

func applyCVTListTheme(hwnd uintptr) {
    if hwnd == 0 || isHighContrastEnabled() {
        return
    }
    pSendMessageW.Call(hwnd, LVM_SETBKCOLOR, 0, colorWhite)
    pSendMessageW.Call(hwnd, LVM_SETTEXTBKCOLOR, 0, colorWhite)
    pSendMessageW.Call(hwnd, LVM_SETTEXTCOLOR, 0, colorTextPrimary)
}

// Owner-draw is confined to the single primary action; other controls retain
// Windows' native keyboard behavior, focus, accessibility, and sizing.
func drawCVTPrimaryButton(lparam uintptr) bool {
    if lparam == 0 {
        return false
    }
    di := (*DRAWITEMSTRUCT)(unsafe.Pointer(lparam))
    if di.CtlType != ODT_BUTTON || di.CtlID != ID_START {
        return false
    }
    bg, fg := colorPrimaryButton, colorWhite
    if isHighContrastEnabled() {
        sysBg, _, _ := pGetSysColor.Call(COLOR_BTNFACE)
        sysFg, _, _ := pGetSysColor.Call(COLOR_BTNTEXT)
        bg, fg = sysBg, sysFg
    } else if di.ItemState&ODS_DISABLED != 0 {
        bg, fg = colorDisabledSurface, colorTextSecondary
    } else if di.ItemState&ODS_SELECTED != 0 {
        bg = colorPrimaryPressed
    }
    brush, _, _ := pCreateSolidBrush.Call(bg)
    if brush != 0 {
        pFillRect.Call(di.Hdc, uintptr(unsafe.Pointer(&di.RcItem)), brush)
        pDeleteObject.Call(brush)
    }
    pSetBkMode.Call(di.Hdc, TRANSPARENT)
    pSetTextColor.Call(di.Hdc, fg)
    if hFont != 0 {
        old, _, _ := pSelectObject.Call(di.Hdc, hFont)
        defer pSelectObject.Call(di.Hdc, old)
    }
    caption := wstr("開始轉換")
    rect := di.RcItem
    pDrawTextW.Call(di.Hdc, uintptr(unsafe.Pointer(caption)), ^uintptr(0), uintptr(unsafe.Pointer(&rect)), DT_CENTER|DT_VCENTER|DT_SINGLELINE)
    if di.ItemState&ODS_FOCUS != 0 {
        focus := di.RcItem
        focus.Left += 4
        focus.Top += 4
        focus.Right -= 4
        focus.Bottom -= 4
        pDrawFocusRect.Call(di.Hdc, uintptr(unsafe.Pointer(&focus)))
    }
    return true
}
