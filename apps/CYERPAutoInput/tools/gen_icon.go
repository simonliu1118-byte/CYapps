package main

import (
	"bytes"
	"encoding/binary"
	"image"
	"image/color"
	"image/png"
	"os"
	"path/filepath"
)

var (
	transparent = color.RGBA{0, 0, 0, 0}
	white       = color.RGBA{255, 255, 255, 255}
	apricot     = color.RGBA{0xD8, 0x84, 0x4A, 0xFF} // CYApps Apricot accent.
)

func main() {
	sizes := []int{16, 24, 32, 48, 64, 128, 256}
	payloads := make([][]byte, 0, len(sizes))
	for _, size := range sizes {
		img := drawIcon(size)
		var b bytes.Buffer
		if err := png.Encode(&b, img); err != nil { panic(err) }
		payloads = append(payloads, b.Bytes())
	}

	var out bytes.Buffer
	_ = binary.Write(&out, binary.LittleEndian, uint16(0))
	_ = binary.Write(&out, binary.LittleEndian, uint16(1))
	_ = binary.Write(&out, binary.LittleEndian, uint16(len(sizes)))

	offset := uint32(6 + 16*len(sizes))
	for i, size := range sizes {
		w, h := byte(size), byte(size)
		if size >= 256 { w, h = 0, 0 }
		out.WriteByte(w); out.WriteByte(h); out.WriteByte(0); out.WriteByte(0)
		_ = binary.Write(&out, binary.LittleEndian, uint16(1))
		_ = binary.Write(&out, binary.LittleEndian, uint16(32))
		_ = binary.Write(&out, binary.LittleEndian, uint32(len(payloads[i])))
		_ = binary.Write(&out, binary.LittleEndian, offset)
		offset += uint32(len(payloads[i]))
	}
	for _, payload := range payloads { out.Write(payload) }

	if err := os.MkdirAll("assets", 0o755); err != nil { panic(err) }
	if err := os.WriteFile(filepath.Join("assets", "CYERPAutoInput.ico"), out.Bytes(), 0o644); err != nil { panic(err) }
}

func drawIcon(size int) *image.RGBA {
	img := image.NewRGBA(image.Rect(0, 0, size, size))
	for y := 0; y < size; y++ {
		for x := 0; x < size; x++ { img.SetRGBA(x, y, transparent) }
	}

	// INV-family optical geometry: slightly taller than wide with transparent margin.
	x0 := round(size, 0.09)
	x1 := round(size, 0.91)
	y0 := round(size, 0.065)
	y1 := round(size, 0.935)
	border := max(1, round(size, 0.075))
	radius := max(2, round(size, 0.105))
	fillRounded(img, x0, y0, x1, y1, radius, apricot)
	fillRounded(img, x0+border, y0+border, x1-border, y1-border, max(1, radius-border), white)

	// EAI abbreviation zone.
	stroke := max(1, round(size, 0.045))
	top := round(size, 0.245)
	bottom := round(size, 0.49)

	// E
	ex0 := round(size, 0.245)
	ex1 := round(size, 0.385)
	fillRect(img, ex0, top, ex0+stroke, bottom, apricot)
	fillRect(img, ex0, top, ex1, top+stroke, apricot)
	mid := (top + bottom) / 2
	fillRect(img, ex0, mid-max(1, stroke/2), ex1-max(1, stroke/2), mid+max(1, stroke/2), apricot)
	fillRect(img, ex0, bottom-stroke, ex1, bottom, apricot)

	// A
	axL := round(size, 0.425)
	axC := round(size, 0.515)
	axR := round(size, 0.605)
	drawThickLine(img, axL, bottom, axC, top, stroke, apricot)
	drawThickLine(img, axC, top, axR, bottom, stroke, apricot)
	barY := round(size, 0.405)
	drawThickLine(img, axL+stroke, barY, axR-stroke, barY, stroke, apricot)

	// I
	ix := round(size, 0.675)
	fillRect(img, ix, top, ix+stroke, bottom, apricot)

	// Data-flow symbol: three streams entering one receiving slot.
	flowStroke := max(1, round(size, 0.032))
	fx0 := round(size, 0.285)
	flowYs := []float64{0.615, 0.685, 0.755}
	flowEnds := []float64{0.515, 0.55, 0.49}
	for i, fy := range flowYs {
		y := round(size, fy)
		drawThickLine(img, fx0, y, round(size, flowEnds[i]), y, flowStroke, apricot)
	}

	rx0 := round(size, 0.605)
	rx1 := round(size, 0.685)
	ry0 := round(size, 0.585)
	ry1 := round(size, 0.79)
	rr := max(1, round(size, 0.025))
	fillRounded(img, rx0, ry0, rx1, ry1, rr, apricot)
	inset := max(1, round(size, 0.022))
	if rx1-rx0 > inset*2+1 && ry1-ry0 > inset*2+1 {
		fillRounded(img, rx0+inset, ry0+inset, rx1-inset, ry1-inset, max(1, rr-inset), white)
	}
	return img
}

func fillRect(img *image.RGBA, x0, y0, x1, y1 int, c color.RGBA) {
	b := img.Bounds()
	if x0 < b.Min.X { x0 = b.Min.X }; if y0 < b.Min.Y { y0 = b.Min.Y }
	if x1 > b.Max.X { x1 = b.Max.X }; if y1 > b.Max.Y { y1 = b.Max.Y }
	for y := y0; y < y1; y++ { for x := x0; x < x1; x++ { img.SetRGBA(x, y, c) } }
}

func fillRounded(img *image.RGBA, x0, y0, x1, y1, r int, c color.RGBA) {
	for y := y0; y < y1; y++ {
		for x := x0; x < x1; x++ {
			if insideRounded(x, y, x0, y0, x1, y1, r) { img.SetRGBA(x, y, c) }
		}
	}
}

func insideRounded(x, y, x0, y0, x1, y1, r int) bool {
	if x < x0 || x >= x1 || y < y0 || y >= y1 { return false }
	if r <= 0 { return true }
	if x >= x0+r && x < x1-r { return true }
	if y >= y0+r && y < y1-r { return true }
	cx := x0+r; if x >= x1-r { cx = x1-r-1 }
	cy := y0+r; if y >= y1-r { cy = y1-r-1 }
	dx, dy := x-cx, y-cy
	return dx*dx+dy*dy <= r*r
}

func drawThickLine(img *image.RGBA, x0, y0, x1, y1, thickness int, c color.RGBA) {
	dx := x1-x0; if dx < 0 { dx = -dx }
	dy := y1-y0; if dy < 0 { dy = -dy }
	steps := dx; if dy > steps { steps = dy }; if steps < 1 { steps = 1 }
	r := max(0, thickness/2)
	for i := 0; i <= steps; i++ {
		x := x0 + (x1-x0)*i/steps
		y := y0 + (y1-y0)*i/steps
		fillRect(img, x-r, y-r, x+r+1, y+r+1, c)
	}
}

func round(size int, ratio float64) int { return int(float64(size)*ratio + 0.5) }
func max(a, b int) int { if a > b { return a }; return b }
