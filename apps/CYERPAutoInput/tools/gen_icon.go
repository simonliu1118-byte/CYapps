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

func main() {
	sizes := []int{16, 24, 32, 48, 64, 128, 256}
	payloads := make([][]byte, 0, len(sizes))
	for _, size := range sizes {
		img := drawIcon(size)
		var b bytes.Buffer
		if err := png.Encode(&b, img); err != nil {
			panic(err)
		}
		payloads = append(payloads, b.Bytes())
	}

	var out bytes.Buffer
	_ = binary.Write(&out, binary.LittleEndian, uint16(0))
	_ = binary.Write(&out, binary.LittleEndian, uint16(1))
	_ = binary.Write(&out, binary.LittleEndian, uint16(len(sizes)))

	offset := uint32(6 + 16*len(sizes))
	for i, size := range sizes {
		w, h := byte(size), byte(size)
		if size >= 256 {
			w, h = 0, 0
		}
		out.WriteByte(w)
		out.WriteByte(h)
		out.WriteByte(0)
		out.WriteByte(0)
		_ = binary.Write(&out, binary.LittleEndian, uint16(1))
		_ = binary.Write(&out, binary.LittleEndian, uint16(32))
		_ = binary.Write(&out, binary.LittleEndian, uint32(len(payloads[i])))
		_ = binary.Write(&out, binary.LittleEndian, offset)
		offset += uint32(len(payloads[i]))
	}
	for _, payload := range payloads {
		out.Write(payload)
	}

	if err := os.MkdirAll("assets", 0o755); err != nil {
		panic(err)
	}
	if err := os.WriteFile(filepath.Join("assets", "CYERPAutoInput.ico"), out.Bytes(), 0o644); err != nil {
		panic(err)
	}
}

func drawIcon(size int) *image.RGBA {
	img := image.NewRGBA(image.Rect(0, 0, size, size))
	blue := color.RGBA{0, 114, 170, 255}
	white := color.RGBA{255, 255, 255, 255}
	line := color.RGBA{0, 114, 170, 255}
	orange := color.RGBA{238, 77, 45, 255}
	transparent := color.RGBA{0, 0, 0, 0}

	for y := 0; y < size; y++ {
		for x := 0; x < size; x++ {
			img.SetRGBA(x, y, transparent)
		}
	}

	radius := size / 5
	if radius < 3 {
		radius = 3
	}
	for y := 0; y < size; y++ {
		for x := 0; x < size; x++ {
			if insideRoundedRect(x, y, size, size, radius) {
				img.SetRGBA(x, y, blue)
			}
		}
	}

	pad := size / 5
	if pad < 3 {
		pad = 3
	}
	for y := pad; y < size-pad; y++ {
		for x := pad; x < size-pad; x++ {
			img.SetRGBA(x, y, white)
		}
	}

	lineHeight := size / 14
	if lineHeight < 1 {
		lineHeight = 1
	}
	left := pad + size/8
	right := size - pad - size/8
	for row := 0; row < 3; row++ {
		y0 := pad + size/6 + row*(size/6)
		for y := y0; y < y0+lineHeight && y < size-pad; y++ {
			for x := left; x < right; x++ {
				img.SetRGBA(x, y, line)
			}
		}
	}

	// Import arrow: orange L-shaped arrow entering the lower-right of the document.
	stroke := size / 12
	if stroke < 1 {
		stroke = 1
	}
	x0 := size - pad - size/4
	y0 := size - pad - size/5
	for y := y0; y < y0+stroke && y < size; y++ {
		for x := x0; x < size-pad/2; x++ {
			if x >= 0 && x < size {
				img.SetRGBA(x, y, orange)
			}
	}
	}
	for x := x0; x < x0+stroke && x < size; x++ {
		for y := y0-size/7; y <= y0+size/7 && y < size; y++ {
			if y >= 0 {
				img.SetRGBA(x, y, orange)
			}
		}
	}
	for i := 0; i < size/8; i++ {
		for t := 0; t < stroke; t++ {
			x := x0 + i
			if x >= 0 && x < size {
				y1 := y0 - size/7 + i + t
				y2 := y0 + size/7 - i - t
				if y1 >= 0 && y1 < size { img.SetRGBA(x, y1, orange) }
				if y2 >= 0 && y2 < size { img.SetRGBA(x, y2, orange) }
			}
		}
	}
	return img
}

func insideRoundedRect(x, y, w, h, r int) bool {
	if x >= r && x < w-r { return true }
	if y >= r && y < h-r { return true }
	cx := r
	if x >= w-r { cx = w-r-1 }
	cy := r
	if y >= h-r { cy = h-r-1 }
	dx := x-cx
	dy := y-cy
	return dx*dx+dy*dy <= r*r
}
