//go:build windows

package main

import (
	"bytes"
	"context"
	"encoding/base64"
	"encoding/binary"
	"fmt"
	"image"
	"image/png"
	"os"
	"os/exec"
	"sort"
	"strconv"
	"strings"
	"sync"
	"syscall"
	"time"
	"unicode/utf16"
	"unsafe"
)

const (
	srccopyV011      = 0x00CC0020
	dibRGBColorsV011 = 0
	biRGBV011        = 0
)

type bitmapInfoHeaderV011 struct {
	Size          uint32
	Width         int32
	Height        int32
	Planes        uint16
	BitCount      uint16
	Compression   uint32
	SizeImage     uint32
	XPelsPerMeter int32
	YPelsPerMeter int32
	ClrUsed       uint32
	ClrImportant  uint32
}

type bitmapInfoV011 struct {
	Header bitmapInfoHeaderV011
	Colors [1]uint32
}

type detailOpticalGeometryV011 struct {
	Hwnd       uintptr
	Rect       RECT
	Vertical   []int
	Rows       []int
	ColOffset  int
	CapturedAt time.Time
}

type ocrWordV011 struct {
	Text       string
	X, Y, W, H int
}

type unitRowV011 struct {
	Y    int
	Text string
}

type lookupOpticalCacheV011 struct {
	Grid       uintptr
	Rect       RECT
	ImageW     int
	ImageH     int
	Rows       []unitRowV011
	CapturedAt time.Time
}

var (
	pGetDCV011                 = user32.NewProc("GetDC")
	pReleaseDCV011             = user32.NewProc("ReleaseDC")
	pCreateCompatibleDCV011    = gdi32.NewProc("CreateCompatibleDC")
	pDeleteDCV011              = gdi32.NewProc("DeleteDC")
	pCreateCompatibleBitmapV011 = gdi32.NewProc("CreateCompatibleBitmap")
	pSelectObjectV011          = gdi32.NewProc("SelectObject")
	pBitBltV011                = gdi32.NewProc("BitBlt")
	pGetDIBitsV011             = gdi32.NewProc("GetDIBits")
	pDeleteObjectV011          = gdi32.NewProc("DeleteObject")

	detailOpticalMuV011 sync.Mutex
	detailOpticalV011   *detailOpticalGeometryV011
	lookupOpticalMuV011 sync.Mutex
	lookupOpticalV011   = map[uintptr]*lookupOpticalCacheV011{}
)

// captureScreenRectV011 copies the requested on-screen rectangle into memory.
// No screenshot is persisted by this function.
func captureScreenRectV011(r RECT) (*image.RGBA, error) {
	w := int(r.Right - r.Left)
	h := int(r.Bottom - r.Top)
	if w <= 2 || h <= 2 {
		return nil, fmt.Errorf("invalid capture rect %d,%d,%d,%d", r.Left, r.Top, r.Right, r.Bottom)
	}
	screen, _, _ := pGetDCV011.Call(0)
	if screen == 0 {
		return nil, fmt.Errorf("GetDC failed")
	}
	defer pReleaseDCV011.Call(0, screen)
	mem, _, _ := pCreateCompatibleDCV011.Call(screen)
	if mem == 0 {
		return nil, fmt.Errorf("CreateCompatibleDC failed")
	}
	defer pDeleteDCV011.Call(mem)
	bmp, _, _ := pCreateCompatibleBitmapV011.Call(screen, uintptr(w), uintptr(h))
	if bmp == 0 {
		return nil, fmt.Errorf("CreateCompatibleBitmap failed")
	}
	defer pDeleteObjectV011.Call(bmp)
	old, _, _ := pSelectObjectV011.Call(mem, bmp)
	defer pSelectObjectV011.Call(mem, old)
	ok, _, _ := pBitBltV011.Call(mem, 0, 0, uintptr(w), uintptr(h), screen, uintptr(r.Left), uintptr(r.Top), srccopyV011)
	if ok == 0 {
		return nil, fmt.Errorf("BitBlt failed")
	}

	buf := make([]byte, w*h*4)
	info := bitmapInfoV011{Header: bitmapInfoHeaderV011{
		Size:        uint32(unsafe.Sizeof(bitmapInfoHeaderV011{})),
		Width:       int32(w),
		Height:      -int32(h),
		Planes:      1,
		BitCount:    32,
		Compression: biRGBV011,
	}}
	got, _, _ := pGetDIBitsV011.Call(mem, bmp, 0, uintptr(h), uintptr(unsafe.Pointer(&buf[0])), uintptr(unsafe.Pointer(&info)), dibRGBColorsV011)
	if got == 0 {
		return nil, fmt.Errorf("GetDIBits failed")
	}
	img := image.NewRGBA(image.Rect(0, 0, w, h))
	for i := 0; i < w*h; i++ {
		bi := i * 4
		ri := i * 4
		img.Pix[ri+0] = buf[bi+2]
		img.Pix[ri+1] = buf[bi+1]
		img.Pix[ri+2] = buf[bi+0]
		img.Pix[ri+3] = 255
	}
	return img, nil
}

func opticalDetailPointV011(grid ControlInfo, row, col int) (int32, int32, bool) {
	grid.Rect = rectOf(grid.Hwnd)
	geom, ok := ensureDetailOpticalGeometryV011(grid)
	if !ok {
		return 0, 0, false
	}
	seg := col + geom.ColOffset
	if seg < 0 || seg+1 >= len(geom.Vertical) {
		logf("WARN", "detail optical V0.0.11 column not visible col=%d segments=%d", col, len(geom.Vertical)-1)
		return 0, 0, false
	}
	visibleRow := detailVisibleRowIndexV11(grid, row)
	if len(geom.Rows) == 0 {
		return 0, 0, false
	}
	if visibleRow >= len(geom.Rows) {
		visibleRow = len(geom.Rows) - 1
	}
	xLocal := (geom.Vertical[seg] + geom.Vertical[seg+1]) / 2
	yLocal := geom.Rows[visibleRow]
	x := geom.Rect.Left + int32(xLocal)
	y := geom.Rect.Top + int32(yLocal)
	logf("INFO", "detail optical V0.0.11 point row=%d visible_row=%d col=%d point=%d,%d", row+1, visibleRow+1, col, x, y)
	return x, y, true
}

func opticalDetailColumnXV011(grid ControlInfo, col int) (int32, bool) {
	x, _, ok := opticalDetailPointV011(grid, 0, col)
	return x, ok
}

func activateDetailFirstRowOpticalV011(root uintptr, grid ControlInfo) bool {
	if isStopRequested() || !prepareERPWindow(root) {
		return false
	}
	grid.Rect = rectOf(grid.Hwnd)
	geom, ok := ensureDetailOpticalGeometryV011(grid)
	if !ok || len(geom.Rows) == 0 || len(geom.Vertical) < 2 {
		return false
	}
	seg := 0
	if geom.ColOffset == 0 && len(geom.Vertical) > 2 {
		seg = 0
	}
	x := geom.Rect.Left + int32((geom.Vertical[seg]+geom.Vertical[seg+1])/2)
	y := geom.Rect.Top + int32(geom.Rows[0])
	clickScreenPoint(x, y)
	if !interruptibleSleep(220 * time.Millisecond) {
		return false
	}
	logf("INFO", "detail optical V0.0.11 first-row activation point=%d,%d", x, y)
	return true
}

func ensureDetailOpticalGeometryV011(grid ControlInfo) (*detailOpticalGeometryV011, bool) {
	detailOpticalMuV011.Lock()
	defer detailOpticalMuV011.Unlock()
	if detailOpticalV011 != nil && detailOpticalV011.Hwnd == grid.Hwnd && detailOpticalV011.Rect == grid.Rect {
		return detailOpticalV011, true
	}
	img, err := captureScreenRectV011(grid.Rect)
	if err != nil {
		logf("WARN", "detail optical V0.0.11 capture failed: %v", err)
		return nil, false
	}
	vertical := detectGridBoundariesV011(img, true)
	horizontal := detectGridBoundariesV011(img, false)
	rows := dataRowCentersV011(horizontal)
	if len(vertical) < 4 || len(rows) < 1 {
		logf("WARN", "detail optical V0.0.11 geometry failed vertical=%d horizontal=%d rows=%d", len(vertical), len(horizontal), len(rows))
		return nil, false
	}
	offset := 0
	if len(vertical) >= 3 && vertical[1]-vertical[0] <= 100 {
		offset = 1
	}
	geom := &detailOpticalGeometryV011{Hwnd: grid.Hwnd, Rect: grid.Rect, Vertical: vertical, Rows: rows, ColOffset: offset, CapturedAt: time.Now()}
	detailOpticalV011 = geom
	logf("INFO", "detail optical V0.0.11 geometry ready vertical=%d rows=%d selector_offset=%d size=%dx%d", len(vertical), len(rows), offset, grid.Rect.Right-grid.Rect.Left, grid.Rect.Bottom-grid.Rect.Top)
	return geom, true
}

func detectGridBoundariesV011(img *image.RGBA, vertical bool) []int {
	w := img.Bounds().Dx()
	h := img.Bounds().Dy()
	if w < 4 || h < 4 {
		return nil
	}
	gray := make([]uint8, w*h)
	for y := 0; y < h; y++ {
		for x := 0; x < w; x++ {
			r, g, b, _ := img.At(x, y).RGBA()
			gray[y*w+x] = uint8((299*int(r>>8) + 587*int(g>>8) + 114*int(b>>8)) / 1000)
		}
	}
	try := func(diffMin, coveragePct int) []int {
		limit := w
		span := h
		if !vertical {
			limit = h
			span = w
		}
		scores := make([]int, limit)
		for p := 1; p < limit-1; p++ {
			count := 0
			for q := 1; q < span-1; q++ {
				var a, b uint8
				if vertical {
					a = gray[q*w+p]
					b = gray[q*w+p-1]
				} else {
					a = gray[p*w+q]
					b = gray[(p-1)*w+q]
				}
				d := int(a) - int(b)
				if d < 0 {
					d = -d
				}
				if d >= diffMin {
					count++
				}
			}
			scores[p] = count
		}
		threshold := span * coveragePct / 100
		out := []int{0}
		for p := 1; p < limit-1; {
			if scores[p] < threshold {
				p++
				continue
			}
			best, bestScore := p, scores[p]
			q := p + 1
			for q < limit-1 && scores[q] >= threshold {
				if scores[q] > bestScore {
					best, bestScore = q, scores[q]
				}
				q++
			}
			if best-out[len(out)-1] >= 4 {
				out = append(out, best)
			}
			p = q
		}
		if limit-1-out[len(out)-1] >= 4 {
			out = append(out, limit-1)
		}
		return out
	}
	out := try(10, 38)
	minimum := 4
	if vertical {
		minimum = 7
	}
	if len(out) < minimum {
		out = try(6, 25)
	}
	return out
}

func dataRowCentersV011(boundaries []int) []int {
	if len(boundaries) < 2 {
		return nil
	}
	rows := []int{}
	for i := 0; i+1 < len(boundaries); i++ {
		a, b := boundaries[i], boundaries[i+1]
		gap := b - a
		if a < 14 || gap < 17 || gap > 38 {
			continue
		}
		rows = append(rows, (a+b)/2)
	}
	return rows
}

// focusedLookupGridTextV011 supplies the selected unit text only for the focused
// F2 TcxGridSite. This lets the existing bounded lookup loop keep its safety
// contract while replacing unavailable Win32 cell text with one OCR snapshot
// plus inexpensive highlight detection on subsequent probes.
func focusedLookupGridTextV011(hwnd uintptr) (string, bool) {
	if hwnd == 0 || !strings.EqualFold(className(hwnd), "TcxGridSite") {
		return "", false
	}
	top := topLevelWindowV011(hwnd)
	if top == 0 || !strings.Contains(rawWindowTextV011(top), "F2開窗查詢") {
		return "", false
	}
	if focusedControlOfForeground(top) != hwnd {
		return "", false
	}
	cache, ok := ensureLookupOpticalCacheV011(hwnd)
	if !ok || len(cache.Rows) == 0 {
		return "", false
	}
	img, err := captureScreenRectV011(rectOf(hwnd))
	if err != nil {
		return "", false
	}
	idx := selectedUnitRowIndexV011(img, cache.Rows)
	if idx < 0 || idx >= len(cache.Rows) {
		return "", false
	}
	return cache.Rows[idx].Text, true
}

func topLevelWindowV011(hwnd uintptr) uintptr {
	cur := hwnd
	for cur != 0 {
		parent, _, _ := pGetParent.Call(cur)
		if parent == 0 || parent == cur {
			return cur
		}
		cur = parent
	}
	return 0
}

func rawWindowTextV011(hwnd uintptr) string {
	n, _, _ := pGetWindowTextLenW.Call(hwnd)
	if n == 0 {
		return ""
	}
	buf := make([]uint16, int(n)+2)
	pGetWindowTextW.Call(hwnd, uintptr(unsafe.Pointer(&buf[0])), uintptr(len(buf)))
	return syscall.UTF16ToString(buf)
}

func ensureLookupOpticalCacheV011(grid uintptr) (*lookupOpticalCacheV011, bool) {
	lookupOpticalMuV011.Lock()
	defer lookupOpticalMuV011.Unlock()
	r := rectOf(grid)
	if c := lookupOpticalV011[grid]; c != nil && c.Rect == r {
		return c, true
	}
	img, err := captureScreenRectV011(r)
	if err != nil {
		logf("WARN", "unit optical V0.0.11 capture failed: %v", err)
		return nil, false
	}
	words, err := windowsOCRWordsV011(img)
	if err != nil {
		logf("WARN", "unit optical V0.0.11 OCR failed: %v", err)
		return nil, false
	}
	rows := extractUnitRowsV011(words, img.Bounds().Dx(), img.Bounds().Dy())
	if len(rows) == 0 {
		logf("WARN", "unit optical V0.0.11 OCR returned no unit rows words=%d", len(words))
		return nil, false
	}
	c := &lookupOpticalCacheV011{Grid: grid, Rect: r, ImageW: img.Bounds().Dx(), ImageH: img.Bounds().Dy(), Rows: rows, CapturedAt: time.Now()}
	lookupOpticalV011[grid] = c
	logf("INFO", "unit optical V0.0.11 OCR cache ready rows=%d words=%d", len(rows), len(words))
	return c, true
}

func extractUnitRowsV011(words []ocrWordV011, width, height int) []unitRowV011 {
	if len(words) == 0 || width <= 0 || height <= 0 {
		return nil
	}
	headerX := -1
	headerY := 0
	for _, w := range words {
		t := normalizeOCRTextV011(w.Text)
		if strings.Contains(t, "換算單位") || t == "單位" || strings.Contains(t, "換算") {
			if w.Y < height/3 {
				headerX = w.X + w.W/2
				headerY = w.Y + w.H
				break
			}
		}
	}
	left, right := width*15/100, width*52/100
	if headerX >= 0 {
		band := width / 9
		if band < 55 {
			band = 55
		}
		left = headerX - band
		right = headerX + band
	}
	if left < 0 {
		left = 0
	}
	if right > width*70/100 {
		right = width * 70 / 100
	}
	type rowWords struct {
		y int
		w []ocrWordV011
	}
	groups := []rowWords{}
	for _, word := range words {
		text := normalizeOCRTextV011(word.Text)
		if text == "" || text == "=" {
			continue
		}
		cx := word.X + word.W/2
		cy := word.Y + word.H/2
		if cx < left || cx > right || cy <= headerY+4 {
			continue
		}
		idx := -1
		for i := range groups {
			d := groups[i].y - cy
			if d < 0 {
				d = -d
			}
			if d <= 8 {
				idx = i
				break
			}
		}
		if idx < 0 {
			groups = append(groups, rowWords{y: cy, w: []ocrWordV011{word}})
		} else {
			groups[idx].w = append(groups[idx].w, word)
		}
	}
	out := make([]unitRowV011, 0, len(groups))
	for _, g := range groups {
		sort.Slice(g.w, func(i, j int) bool { return g.w[i].X < g.w[j].X })
		parts := make([]string, 0, len(g.w))
		for _, w := range g.w {
			parts = append(parts, normalizeOCRTextV011(w.Text))
		}
		text := strings.Join(parts, "")
		if text != "" {
			out = append(out, unitRowV011{Y: g.y, Text: text})
		}
	}
	sort.Slice(out, func(i, j int) bool { return out[i].Y < out[j].Y })
	return out
}

func normalizeOCRTextV011(s string) string {
	s = strings.TrimSpace(s)
	s = strings.ReplaceAll(s, " ", "")
	s = strings.ReplaceAll(s, "\u3000", "")
	return s
}

func selectedUnitRowIndexV011(img *image.RGBA, rows []unitRowV011) int {
	w := img.Bounds().Dx()
	h := img.Bounds().Dy()
	if w <= 0 || h <= 0 || len(rows) == 0 {
		return -1
	}
	best, bestScore := -1, float64(0)
	x0, x1 := w*8/100, w*68/100
	for i, row := range rows {
		if row.Y < 2 || row.Y >= h-2 {
			continue
		}
		var total, count int
		for y := row.Y - 5; y <= row.Y+5; y++ {
			if y < 0 || y >= h {
				continue
			}
			for x := x0; x < x1; x += 3 {
				r, g, b, _ := img.At(x, y).RGBA()
				ri, gi, bi := int(r>>8), int(g>>8), int(b>>8)
				mx, mn := ri, ri
				if gi > mx { mx = gi }
				if bi > mx { mx = bi }
				if gi < mn { mn = gi }
				if bi < mn { mn = bi }
				total += mx - mn
				count++
			}
		}
		if count == 0 {
			continue
		}
		score := float64(total) / float64(count)
		if score > bestScore {
			best, bestScore = i, score
		}
	}
	if bestScore < 7.0 {
		return -1
	}
	return best
}

func windowsOCRWordsV011(img *image.RGBA) ([]ocrWordV011, error) {
	f, err := os.CreateTemp("", "CYERPAutoInput_ocr_*.png")
	if err != nil {
		return nil, err
	}
	path := f.Name()
	defer os.Remove(path)
	if err := png.Encode(f, img); err != nil {
		f.Close()
		return nil, err
	}
	if err := f.Close(); err != nil {
		return nil, err
	}

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	cmd := exec.CommandContext(ctx, "powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encodePowerShellV011(windowsOCRScriptV011))
	cmd.Env = append(os.Environ(), "CYERP_OCR_IMAGE="+path)
	out, err := cmd.CombinedOutput()
	if ctx.Err() != nil {
		return nil, fmt.Errorf("OCR timeout")
	}
	if err != nil {
		msg := strings.TrimSpace(string(out))
		if len(msg) > 240 {
			msg = msg[:240]
		}
		return nil, fmt.Errorf("powershell OCR: %v %s", err, msg)
	}
	return parseOCRWordsV011(out)
}

func parseOCRWordsV011(out []byte) ([]ocrWordV011, error) {
	lines := bytes.Split(out, []byte{'\n'})
	words := make([]ocrWordV011, 0, len(lines))
	for _, raw := range lines {
		line := strings.TrimSpace(strings.TrimSuffix(string(raw), "\r"))
		if line == "" {
			continue
		}
		parts := strings.Split(line, "\t")
		if len(parts) != 5 {
			continue
		}
		b, err := base64.StdEncoding.DecodeString(parts[0])
		if err != nil {
			continue
		}
		nums := [4]int{}
		ok := true
		for i := 0; i < 4; i++ {
			n, e := strconv.Atoi(parts[i+1])
			if e != nil {
				ok = false
				break
			}
			nums[i] = n
		}
		if !ok {
			continue
		}
		words = append(words, ocrWordV011{Text: string(b), X: nums[0], Y: nums[1], W: nums[2], H: nums[3]})
	}
	if len(words) == 0 {
		return nil, fmt.Errorf("OCR produced no words")
	}
	return words, nil
}

func encodePowerShellV011(script string) string {
	units := utf16.Encode([]rune(script))
	buf := make([]byte, len(units)*2)
	for i, u := range units {
		binary.LittleEndian.PutUint16(buf[i*2:], u)
	}
	return base64.StdEncoding.EncodeToString(buf)
}

const windowsOCRScriptV011 = `$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null=[Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]
$null=[Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime]
$null=[Windows.Foundation.IAsyncOperation` + "`" + `1,Windows.Foundation,ContentType=WindowsRuntime]
$null=[Windows.Graphics.Imaging.SoftwareBitmap,Windows.Foundation,ContentType=WindowsRuntime]
$null=[Windows.Storage.Streams.RandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
$null=[WindowsRuntimeSystemExtensions]
$awaiter=[WindowsRuntimeSystemExtensions].GetMember('GetAwaiter','Method','Public,Static') | Where-Object { $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation` + "`" + `1' } | Select-Object -First 1
function Invoke-Async([object]$AsyncTask,[Type]$As){ return $awaiter.MakeGenericMethod($As).Invoke($null,@($AsyncTask)).GetResult() }
$engine=[Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
if($null -eq $engine){ throw 'OCR engine unavailable' }
$path=$env:CYERP_OCR_IMAGE
$file=Invoke-Async ([Windows.Storage.StorageFile]::GetFileFromPathAsync($path)) ([Windows.Storage.StorageFile])
$stream=Invoke-Async ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
$decoder=Invoke-Async ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
$bitmap=Invoke-Async ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
$result=Invoke-Async ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
foreach($line in $result.Lines){ foreach($word in $line.Words){ $r=$word.BoundingRect; $t=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($word.Text)); [Console]::Out.WriteLine("$t`t$([int]$r.X)`t$([int]$r.Y)`t$([int]$r.Width)`t$([int]$r.Height)") } }`
