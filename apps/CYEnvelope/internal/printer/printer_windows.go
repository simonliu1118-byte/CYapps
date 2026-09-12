//go:build windows

package printer

import (
	"errors"
	"math"
	"sort"
	"strings"
	"syscall"
	"unsafe"

	"cyenvelope/internal/model"
	"github.com/lxn/win"
	"golang.org/x/sys/windows"
)

var (
	spool              = windows.NewLazySystemDLL("winspool.drv")
	openPrinterW       = spool.NewProc("OpenPrinterW")
	closePrinter       = spool.NewProc("ClosePrinter")
	printerPropertiesW = spool.NewProc("PrinterProperties")
)

func DefaultName() string {
	var count uint32
	win.GetDefaultPrinter(nil, &count)
	if count == 0 {
		return ""
	}
	buf := make([]uint16, count)
	if !win.GetDefaultPrinter(&buf[0], &count) {
		return ""
	}
	return windows.UTF16ToString(buf)
}

func Names() []string {
	flags := uint32(win.PRINTER_ENUM_LOCAL | win.PRINTER_ENUM_CONNECTIONS)
	var needed, returned uint32
	win.EnumPrinters(flags, nil, 4, nil, 0, &needed, &returned)
	if needed == 0 {
		return nil
	}
	buf := make([]byte, needed)
	if !win.EnumPrinters(flags, nil, 4, &buf[0], needed, &needed, &returned) {
		return nil
	}
	items := unsafe.Slice((*win.PRINTER_INFO_4)(unsafe.Pointer(&buf[0])), int(returned))
	names := make([]string, 0, len(items))
	for _, item := range items {
		if item.PPrinterName != nil {
			names = append(names, windows.UTF16PtrToString(item.PPrinterName))
		}
	}
	sort.Strings(names)
	return names
}

func open(name string) (win.HANDLE, *uint16, error) {
	namePtr, _ := windows.UTF16PtrFromString(name)
	var handle win.HANDLE
	r, _, e := openPrinterW.Call(uintptr(unsafe.Pointer(namePtr)), uintptr(unsafe.Pointer(&handle)), 0)
	if r == 0 {
		return 0, nil, e
	}
	return handle, namePtr, nil
}

func Properties(owner uintptr, name string) error {
	if name == "" {
		name = DefaultName()
	}
	if name == "" {
		return errors.New("找不到 Windows 印表機")
	}
	h, _, err := open(name)
	if err != nil {
		return err
	}
	defer closePrinter.Call(uintptr(h))
	r, _, e := printerPropertiesW.Call(owner, uintptr(h))
	if r == 0 {
		return e
	}
	return nil
}

func Print(name string, job Job) error {
	if name == "" {
		name = DefaultName()
	}
	if name == "" {
		return errors.New("找不到 Windows 預設印表機")
	}
	h, namePtr, err := open(name)
	if err != nil {
		return err
	}
	defer closePrinter.Call(uintptr(h))
	size := win.DocumentProperties(0, h, namePtr, nil, nil, 0)
	if size <= 0 {
		return errors.New("無法讀取印表機紙張設定")
	}
	buf := make([]byte, size)
	dm := (*win.DEVMODE)(unsafe.Pointer(&buf[0]))
	if win.DocumentProperties(0, h, namePtr, dm, nil, win.DM_OUT_BUFFER) < 0 {
		return errors.New("無法建立印表機設定")
	}
	dm.DmFields |= win.DM_PAPERSIZE | win.DM_PAPERWIDTH | win.DM_PAPERLENGTH | win.DM_ORIENTATION
	dm.DmPaperSize = win.DMPAPER_USER
	dm.DmPaperWidth = int16(math.Round(job.Format.WidthMM * 10))
	dm.DmPaperLength = int16(math.Round(job.Format.HeightMM * 10))
	if job.Format.Orientation == model.OrientationLandscape {
		dm.DmOrientation = win.DMORIENT_LANDSCAPE
	} else {
		dm.DmOrientation = win.DMORIENT_PORTRAIT
	}
	if win.DocumentProperties(0, h, namePtr, dm, dm, win.DM_IN_BUFFER|win.DM_OUT_BUFFER) < 0 {
		return errors.New("印表機不接受自訂信封尺寸")
	}
	driver, _ := windows.UTF16PtrFromString("WINSPOOL")
	hdc := win.CreateDC(driver, namePtr, nil, dm)
	if hdc == 0 {
		return errors.New("無法建立列印工作")
	}
	defer win.DeleteDC(hdc)
	title, _ := windows.UTF16PtrFromString("CYEnvelope")
	doc := win.DOCINFO{CbSize: int32(unsafe.Sizeof(win.DOCINFO{})), LpszDocName: title}
	if win.StartDoc(hdc, &doc) <= 0 {
		return errors.New("Windows 拒絕列印工作")
	}
	ok := false
	defer func() {
		if !ok {
			win.AbortDoc(hdc)
		}
	}()
	if win.StartPage(hdc) <= 0 {
		return errors.New("無法開始列印頁面")
	}
	drawJob(hdc, job)
	if win.EndPage(hdc) <= 0 || win.EndDoc(hdc) <= 0 {
		return errors.New("列印工作送出失敗")
	}
	ok = true
	return nil
}

func drawJob(hdc win.HDC, job Job) {
	dpiX, dpiY := win.GetDeviceCaps(hdc, win.LOGPIXELSX), win.GetDeviceCaps(hdc, win.LOGPIXELSY)
	offX, offY := win.GetDeviceCaps(hdc, win.PHYSICALOFFSETX), win.GetDeviceCaps(hdc, win.PHYSICALOFFSETY)
	px := func(mm float64) int32 { return int32(math.Round(mm/25.4*float64(dpiX))) - offX }
	py := func(mm float64) int32 { return int32(math.Round(mm/25.4*float64(dpiY))) - offY }
	win.SetBkMode(hdc, win.TRANSPARENT)
	drawTextLayout(hdc, job.Recipient, job.Format.Recipient, px, py, dpiY)
	drawTextLayout(hdc, job.Address, job.Format.Address, px, py, dpiY)
	drawTextLayout(hdc, job.Phone, job.Format.Phone, px, py, dpiY)
	drawTextLayout(hdc, job.PostalCode, job.Format.PostalCode, px, py, dpiY)
	selected := map[string]bool{}
	for _, id := range job.DeliveryIDs {
		selected[id] = true
	}
	pen := win.GetStockObject(win.BLACK_PEN)
	oldPen := win.SelectObject(hdc, pen)
	defer win.SelectObject(hdc, oldPen)
	for _, option := range job.Format.Delivery {
		if !selected[option.ID] {
			continue
		}
		x, y, s := px(option.X), py(option.Y), int32(math.Round(option.MarkSize/25.4*float64(dpiX)))
		win.MoveToEx(hdc, int(x), int(y+s/2), nil)
		win.LineTo(hdc, x+s/3, y+s)
		win.MoveToEx(hdc, int(x+s/3), int(y+s), nil)
		win.LineTo(hdc, x+s, y)
	}
	if job.FrameVisible && strings.TrimSpace(job.FrameText) != "" {
		r := job.Format.Frame.Rect
		l, t, rr, b := px(r.X), py(r.Y), px(r.X+r.W), py(r.Y+r.H)
		win.MoveToEx(hdc, int(l), int(t), nil)
		win.LineTo(hdc, rr, t)
		win.LineTo(hdc, rr, b)
		win.LineTo(hdc, l, b)
		win.LineTo(hdc, l, t)
		layout := model.TextLayout{Rect: r, Font: job.Format.Frame.Font, FontSize: job.Format.Frame.FontSize, MinSize: 8, Vertical: false, MaxColumns: 1}
		drawTextLayout(hdc, job.FrameText, layout, px, py, dpiY)
	}
}

func drawTextLayout(hdc win.HDC, text string, layout model.TextLayout, px, py func(float64) int32, dpiY int32) {
	text = strings.TrimSpace(text)
	if text == "" {
		return
	}
	fontSize := layout.FontSize
	if fontSize <= 0 {
		fontSize = 12
	}
	if layout.MinSize <= 0 {
		layout.MinSize = 8
	}
	runes := []rune(text)
	maxCols := layout.MaxColumns
	if maxCols < 1 {
		maxCols = 1
	}
	if layout.Vertical {
		for fontSize > layout.MinSize {
			lineMM := fontSize * 25.4 / 72 * 1.08
			capacity := int(layout.Rect.H/lineMM) * maxCols
			if capacity >= len(runes) {
				break
			}
			fontSize -= .5
		}
	} else {
		for fontSize > layout.MinSize && float64(len(runes))*fontSize*.55*25.4/72 > layout.Rect.W*float64(maxCols) {
			fontSize -= .5
		}
	}
	hfont := makeFont(layout.Font, fontSize, dpiY)
	if hfont == 0 {
		return
	}
	defer win.DeleteObject(win.HGDIOBJ(hfont))
	old := win.SelectObject(hdc, win.HGDIOBJ(hfont))
	defer win.SelectObject(hdc, old)
	if !layout.Vertical {
		parts := []string{text}
		if maxCols > 1 && len(runes) > 1 {
			per := int(math.Ceil(float64(len(runes)) / float64(maxCols)))
			parts = nil
			for i := 0; i < len(runes); i += per {
				end := i + per
				if end > len(runes) {
					end = len(runes)
				}
				parts = append(parts, string(runes[i:end]))
			}
		}
		linePx := int32(math.Round(fontSize / 72 * float64(dpiY) * 1.25))
		for i, line := range parts {
			textOut(hdc, px(layout.Rect.X), py(layout.Rect.Y)+int32(i)*linePx, line)
		}
		return
	}
	linePx := int32(math.Round(fontSize / 72 * float64(dpiY) * 1.08))
	if linePx < 1 {
		linePx = 1
	}
	capacity := int((py(layout.Rect.Y+layout.Rect.H) - py(layout.Rect.Y)) / linePx)
	if capacity < 1 {
		capacity = 1
	}
	colPx := int32(math.Round(fontSize / 72 * float64(dpiY) * 1.25))
	for i, r := range runes {
		col, row := i/capacity, i%capacity
		if col >= maxCols {
			break
		}
		textOut(hdc, px(layout.Rect.X+layout.Rect.W)-int32(col+1)*colPx, py(layout.Rect.Y)+int32(row)*linePx, string(r))
	}
}

func makeFont(face string, points float64, dpi int32) win.HFONT {
	if face == "" {
		face = "DFKai-SB"
	}
	lf := win.LOGFONT{LfHeight: -int32(math.Round(points / 72 * float64(dpi))), LfWeight: 400, LfCharSet: win.DEFAULT_CHARSET, LfQuality: win.CLEARTYPE_QUALITY}
	u, _ := syscall.UTF16FromString(face)
	copy(lf.LfFaceName[:], u)
	return win.CreateFontIndirect(&lf)
}

func textOut(hdc win.HDC, x, y int32, text string) {
	u, _ := syscall.UTF16FromString(text)
	if len(u) > 1 {
		win.TextOut(hdc, x, y, &u[0], int32(len(u)-1))
	}
}
