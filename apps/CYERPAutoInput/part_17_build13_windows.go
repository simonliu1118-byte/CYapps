//go:build windows

package main

import (
	"strings"
	"time"
	"unsafe"
)

const build13Label = "Build 13"

func init() {
	// Build 13 keeps the Build 12 UI layout but updates the visible build label
	// and window icon after the controls have been created.
	go func() {
		for i := 0; i < 100; i++ {
			if mainHwnd != 0 && statusHwnd != 0 {
				setWindowText(mainHwnd, "CYERPAutoInput V0.0.10 Build 13 — SMART ERP 自動輸入工具")
				for _, c := range enumControls(mainHwnd) {
					if strings.Contains(c.Text, "V0.0.10 Build 12") {
						setWindowText(c.Hwnd, strings.ReplaceAll(c.Text, "V0.0.10 Build 12", "V0.0.10 Build 13"))
					}
				}
				applyAppIconV13(mainHwnd)
				return
			}
			time.Sleep(50 * time.Millisecond)
		}
	}()
}

func isSimpleMoneyFieldV13(key string) bool {
	return key == "cod" || key == "freight_fee"
}

// setSimpleClickInputV13 intentionally follows the ERP behavior confirmed on-site:
// click the field -> type the value -> leave the field. No selection/clear sequence,
// no lookup commit, and no Ctrl+A.
func setSimpleClickInputV13(root uintptr, target ControlInfo, value string, sheet uintptr) bool {
	if isStopRequested() {
		return false
	}
	value = strings.TrimSpace(value)
	if value == "" {
		logf("INFO", "simple money field skipped blank target=0x%x", target.Hwnd)
		return true
	}
	edit := focusTargetEditV4(root, target)
	if edit == 0 {
		return false
	}
	if !interruptibleSleep(90 * time.Millisecond) {
		return false
	}
	if !sendSequentialUnicodeV5(value, 105*time.Millisecond) {
		return false
	}
	if !interruptibleSleep(140 * time.Millisecond) {
		return false
	}
	clickBlankArea(sheet)
	if !interruptibleSleep(180 * time.Millisecond) {
		return false
	}
	logf("INFO", "simple money field click-input-leave target=0x%x edit=0x%x requested_len=%d", target.Hwnd, edit, len([]rune(value)))
	return true
}

func applyAppIconV13(hwnd uintptr) {
	big := makeAppIconV13(32)
	small := makeAppIconV13(16)
	if big != 0 {
		pSendMessageW.Call(hwnd, wmSetIconV12, iconBigV12, big)
	}
	if small != 0 {
		pSendMessageW.Call(hwnd, wmSetIconV12, iconSmallV12, small)
	}
}

func makeAppIconV13(size int) uintptr {
	if size < 16 {
		size = 16
	}
	pixels := make([]uint32, size*size)
	maskStride := ((size + 15) / 16) * 2
	mask := make([]byte, maskStride*size)
	white := uint32(0x00FFFFFF)
	apricot := uint32(0x004A84D8) // RGB #D8844A in little-endian BGR order.

	for i := range pixels {
		pixels[i] = white
	}

	x0 := int(float64(size)*0.09 + 0.5)
	x1 := int(float64(size)*0.91 + 0.5)
	y0 := int(float64(size)*0.065 + 0.5)
	y1 := int(float64(size)*0.935 + 0.5)
	if x0 < 1 { x0 = 1 }
	if y0 < 1 { y0 = 1 }
	if x1 > size-1 { x1 = size-1 }
	if y1 > size-1 { y1 = size-1 }
	border := maxiV13(1, int(float64(size)*0.075+0.5))
	radius := maxiV13(2, int(float64(size)*0.105+0.5))

	for y := 0; y < size; y++ {
		for x := 0; x < size; x++ {
			if !insideRoundedBoxV13(x, y, x0, y0, x1, y1, radius) {
				setMaskTransparentV13(mask, maskStride, x, y)
			}
		}
	}

	fillRoundedBoxV13(pixels, size, x0, y0, x1, y1, radius, apricot)
	fillRoundedBoxV13(pixels, size, x0+border, y0+border, x1-border, y1-border, maxiV13(1, radius-border), white)

	stroke := maxiV13(1, int(float64(size)*0.045+0.5))
	top := int(float64(size)*0.245 + 0.5)
	bottom := int(float64(size)*0.49 + 0.5)

	ex0 := int(float64(size)*0.245 + 0.5)
	ex1 := int(float64(size)*0.385 + 0.5)
	fillRectV13(pixels, size, ex0, top, ex0+stroke, bottom, apricot)
	fillRectV13(pixels, size, ex0, top, ex1, top+stroke, apricot)
	fillRectV13(pixels, size, ex0, (top+bottom)/2-stroke/2, ex1-stroke/2, (top+bottom)/2+maxiV13(1, stroke/2), apricot)
	fillRectV13(pixels, size, ex0, bottom-stroke, ex1, bottom, apricot)

	axL := int(float64(size)*0.425 + 0.5)
	axC := int(float64(size)*0.515 + 0.5)
	axR := int(float64(size)*0.605 + 0.5)
	drawThickLineV13(pixels, size, axL, bottom, axC, top, stroke, apricot)
	drawThickLineV13(pixels, size, axC, top, axR, bottom, stroke, apricot)
	barY := int(float64(size)*0.405 + 0.5)
	drawThickLineV13(pixels, size, axL+stroke, barY, axR-stroke, barY, stroke, apricot)

	ix := int(float64(size)*0.675 + 0.5)
	fillRectV13(pixels, size, ix, top, ix+stroke, bottom, apricot)

	flowStroke := maxiV13(1, int(float64(size)*0.032+0.5))
	fx0 := int(float64(size)*0.285 + 0.5)
	flowYs := []float64{0.615, 0.685, 0.755}
	flowEnds := []float64{0.515, 0.55, 0.49}
	for i, fy := range flowYs {
		y := int(float64(size)*fy + 0.5)
		xEnd := int(float64(size)*flowEnds[i] + 0.5)
		drawThickLineV13(pixels, size, fx0, y, xEnd, y, flowStroke, apricot)
	}
	rx0 := int(float64(size)*0.605 + 0.5)
	rx1 := int(float64(size)*0.685 + 0.5)
	ry0 := int(float64(size)*0.585 + 0.5)
	ry1 := int(float64(size)*0.79 + 0.5)
	rr := maxiV13(1, int(float64(size)*0.025+0.5))
	fillRoundedBoxV13(pixels, size, rx0, ry0, rx1, ry1, rr, apricot)
	inset := maxiV13(1, int(float64(size)*0.022+0.5))
	if rx1-rx0 > inset*2+1 && ry1-ry0 > inset*2+1 {
		fillRoundedBoxV13(pixels, size, rx0+inset, ry0+inset, rx1-inset, ry1-inset, maxiV13(1, rr-inset), white)
	}

	colorBmp, _, _ := pCreateBitmapV12.Call(uintptr(size), uintptr(size), 1, 32, uintptr(unsafe.Pointer(&pixels[0])))
	maskBmp, _, _ := pCreateBitmapV12.Call(uintptr(size), uintptr(size), 1, 1, uintptr(unsafe.Pointer(&mask[0])))
	info := iconInfoV12{FIcon: 1, HbmMask: maskBmp, HbmColor: colorBmp}
	icon, _, _ := pCreateIconIndirectV12.Call(uintptr(unsafe.Pointer(&info)))
	if colorBmp != 0 { pDeleteObjectV12.Call(colorBmp) }
	if maskBmp != 0 { pDeleteObjectV12.Call(maskBmp) }
	return icon
}

func setMaskTransparentV13(mask []byte, stride, x, y int) {
	idx := y*stride + x/8
	if idx >= 0 && idx < len(mask) {
		mask[idx] |= byte(0x80 >> uint(x%8))
	}
}

func fillRoundedBoxV13(pixels []uint32, size, x0, y0, x1, y1, radius int, c uint32) {
	for y := y0; y < y1; y++ {
		for x := x0; x < x1; x++ {
			if insideRoundedBoxV13(x, y, x0, y0, x1, y1, radius) {
				pixels[y*size+x] = c
			}
		}
}

func insideRoundedBoxV13(x, y, x0, y0, x1, y1, r int) bool {
	if x < x0 || x >= x1 || y < y0 || y >= y1 { return false }
	if r <= 0 { return true }
	if x >= x0+r && x < x1-r { return true }
	if y >= y0+r && y < y1-r { return true }
	cx := x0 + r
	if x >= x1-r { cx = x1-r-1 }
	cy := y0 + r
	if y >= y1-r { cy = y1-r-1 }
	dx := x-cx
	dy := y-cy
	return dx*dx+dy*dy <= r*r
}

func fillRectV13(pixels []uint32, size, x0, y0, x1, y1 int, c uint32) {
	if x0 < 0 { x0 = 0 }; if y0 < 0 { y0 = 0 }
	if x1 > size { x1 = size }; if y1 > size { y1 = size }
	for y := y0; y < y1; y++ {
		for x := x0; x < x1; x++ { pixels[y*size+x] = c }
	}
}

func drawThickLineV13(pixels []uint32, size, x0, y0, x1, y1, thickness int, c uint32) {
	dx := x1-x0; if dx < 0 { dx = -dx }
	dy := y1-y0; if dy < 0 { dy = -dy }
	steps := dx; if dy > steps { steps = dy }
	if steps < 1 { steps = 1 }
	r := maxiV13(0, thickness/2)
	for i := 0; i <= steps; i++ {
		x := x0 + (x1-x0)*i/steps
		y := y0 + (y1-y0)*i/steps
		fillRectV13(pixels, size, x-r, y-r, x+r+1, y+r+1, c)
	}
}

func maxiV13(a, b int) int { if a > b { return a }; return b }
