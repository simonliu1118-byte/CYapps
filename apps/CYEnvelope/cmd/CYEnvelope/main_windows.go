//go:build windows

package main

import (
	"fmt"
	"log"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"unicode/utf16"

	"cyenvelope/internal/model"
	"cyenvelope/internal/phone"
	"cyenvelope/internal/postal"
	"cyenvelope/internal/printer"
	"cyenvelope/internal/service"
	"cyenvelope/internal/store"
	"github.com/lxn/walk"
	. "github.com/lxn/walk/declarative"
)

const version = "0.1.1"

type envelopeApp struct {
	*walk.MainWindow
	store   *store.Store
	state   model.State
	baseDir string

	recipient, address, phone *walk.ComboBox
	postal                    *walk.LineEdit
	frameVisible              *walk.CheckBox
	frameText                 *walk.ComboBox
	delivery                  []*walk.CheckBox
	preview                   *walk.CustomWidget
	status                    *walk.Label
	printButton               *walk.PushButton
	changing                  bool
	recipientUpdateSeq        uint64
}

func main() {
	exe, _ := os.Executable()
	base := filepath.Dir(exe)
	if strings.Contains(strings.ToLower(base), "go-build") {
		if wd, err := os.Getwd(); err == nil {
			base = wd
		}
	}
	_ = os.MkdirAll(filepath.Join(base, "Logs"), 0755)
	logFile, err := os.OpenFile(filepath.Join(base, "Logs", "CYEnvelope.log"), os.O_CREATE|os.O_APPEND|os.O_WRONLY, 0644)
	if err == nil {
		defer logFile.Close()
		log.SetOutput(logFile)
	}
	db, err := store.Open(base)
	if err != nil {
		walk.MsgBox(nil, "CYEnvelope 啟動失敗", err.Error(), walk.MsgBoxOK|walk.MsgBoxIconError)
		return
	}
	defer db.Close()
	state, err := db.Load()
	if err != nil {
		walk.MsgBox(nil, "CYEnvelope 啟動失敗", err.Error(), walk.MsgBoxOK|walk.MsgBoxIconError)
		return
	}
	a := &envelopeApp{store: db, state: state, baseDir: base}
	if err := a.run(); err != nil {
		log.Print(err)
		walk.MsgBox(nil, "CYEnvelope", err.Error(), walk.MsgBoxOK|walk.MsgBoxIconError)
	}
}

func (a *envelopeApp) run() error {
	uiFont := Font{Family: "Microsoft JhengHei UI", PointSize: 10}
	toolbar := []MenuItem{
		Action{Text: "資料", OnTriggered: a.openContacts},
		Action{Text: "列印設定", OnTriggered: a.openPrintSettings},
		Action{Text: "格式設定", OnTriggered: a.openFormatSettings},
		Action{Text: "方框文字", OnTriggered: a.openFrameTexts},
		Action{Text: "設定", OnTriggered: a.openSettings},
	}
	main := MainWindow{
		AssignTo: &a.MainWindow, Title: "CYEnvelope  信封套印  V" + version,
		MinSize: Size{Width: 1100, Height: 760}, Size: Size{Width: 1220, Height: 850}, Font: uiFont,
		ToolBar: ToolBar{ButtonStyle: ToolBarButtonTextOnly, Items: toolbar},
		Layout:  VBox{Margins: Margins{Left: 18, Top: 14, Right: 18, Bottom: 12}, Spacing: 10},
		Children: []Widget{
			Composite{Layout: HBox{Spacing: 18}, Children: []Widget{
				Composite{MinSize: Size{Width: 410, Height: 600}, MaxSize: Size{Width: 470}, Layout: VBox{Spacing: 8}, Children: []Widget{
					Label{Text: "收件資料", Font: Font{Family: "Microsoft JhengHei UI", PointSize: 15, Bold: true}},
					Label{Text: "收件人  *"},
					ComboBox{AssignTo: &a.recipient, Editable: true, Model: a.contactNames(), OnTextChanged: a.recipientChanged, OnCurrentIndexChanged: a.recipientSelected},
					Label{Text: "收件人地址  *"},
					ComboBox{AssignTo: &a.address, Editable: true, OnTextChanged: a.addressChanged, OnCurrentIndexChanged: a.addressSelected},
					Composite{Layout: Grid{Columns: 2, Spacing: 8}, Children: []Widget{
						Composite{Layout: VBox{MarginsZero: true, Spacing: 3}, Children: []Widget{Label{Text: "郵遞區號（3碼）"}, LineEdit{AssignTo: &a.postal, MaxLength: 3}}},
						Composite{Layout: VBox{MarginsZero: true, Spacing: 3}, Children: []Widget{Label{Text: "電話／手機／分機（選填）"}, ComboBox{AssignTo: &a.phone, Editable: true}}},
					}},
					Label{Text: "郵件種類（可複選；只印勾記）"},
					a.deliveryWidgets(),
					Composite{Layout: Grid{Columns: 2, Spacing: 8}, Children: []Widget{
						CheckBox{AssignTo: &a.frameVisible, Text: "印出方框文字", OnCheckedChanged: a.invalidate},
						ComboBox{AssignTo: &a.frameText, Editable: true, Model: a.frameTextValues(), OnTextChanged: a.invalidate},
					}},
					VSpacer{},
					Composite{Layout: HBox{MarginsZero: true, Spacing: 10}, Children: []Widget{
						PushButton{AssignTo: &a.printButton, Text: "列  印", MinSize: Size{Width: 210, Height: 52}, Font: Font{Family: "Microsoft JhengHei UI", PointSize: 14, Bold: true}, OnClicked: a.print},
						PushButton{Text: "清空", MinSize: Size{Width: 100, Height: 42}, OnClicked: a.clear},
					}},
				}},
				Composite{Layout: VBox{MarginsZero: true}, Children: []Widget{
					Label{Text: "即時套印預覽", Font: Font{Family: "Microsoft JhengHei UI", PointSize: 12, Bold: true}},
					CustomWidget{AssignTo: &a.preview, ClearsBackground: true, InvalidatesOnResize: true, Paint: a.paintEnvelope},
				}},
			}},
			Label{AssignTo: &a.status, Text: "就緒｜資料會在按下「列印」後、送入 Windows 前先保存。"},
		},
	}
	if err := main.Create(); err != nil {
		return err
	}
	a.setupKeys()
	a.applyFormatDefaults()
	a.MainWindow.Show()
	a.MainWindow.Run()
	return nil
}

func (a *envelopeApp) deliveryWidgets() Widget {
	children := make([]Widget, 0, 7)
	envelopeFormat := a.currentFormat()
	for i := 0; i < 7; i++ {
		label := fmt.Sprintf("選項 %d", i+1)
		if i < len(envelopeFormat.Delivery) && envelopeFormat.Delivery[i].Label != "" {
			label = envelopeFormat.Delivery[i].Label
		}
		cb := new(walk.CheckBox)
		a.delivery = append(a.delivery, cb)
		children = append(children, CheckBox{AssignTo: &cb, Text: label, OnCheckedChanged: a.invalidate})
	}
	return Composite{Layout: Grid{Columns: 2, Spacing: 4}, Children: children}
}

func (a *envelopeApp) setupKeys() {
	a.recipient.KeyDown().Attach(func(k walk.Key) {
		if k == walk.KeyReturn {
			if a.recipient.CurrentIndex() >= 0 {
				a.recipientSelected()
			}
			a.address.SetFocus()
		}
	})
	a.address.KeyDown().Attach(func(k walk.Key) {
		if k == walk.KeyReturn {
			if a.address.CurrentIndex() >= 0 {
				a.addressSelected()
			}
			a.phone.SetFocus()
		}
	})
	a.phone.KeyDown().Attach(func(k walk.Key) {
		if k == walk.KeyReturn {
			a.printButton.SetFocus()
		}
	})
	a.postal.TextChanged().Attach(a.invalidate)
}

func (a *envelopeApp) contactNames() []string {
	r := service.ContactSuggestions(&a.state, "")
	if len(r) > 30 {
		r = r[:30]
	}
	return r
}
func (a *envelopeApp) frameTextValues() []string {
	r := make([]string, len(a.state.FrameTexts))
	for i, v := range a.state.FrameTexts {
		r[i] = v.Text
	}
	return r
}
func (a *envelopeApp) currentFormat() model.EnvelopeFormat {
	for _, f := range a.state.Formats {
		if f.ID == a.state.Settings.SelectedFormatID {
			return f
		}
	}
	for _, f := range a.state.Formats {
		if f.IsDefault {
			return f
		}
	}
	return a.state.Formats[0]
}

func (a *envelopeApp) recipientChanged() {
	if a.changing {
		return
	}
	a.invalidate()
	text := a.recipient.Text()
	a.recipientUpdateSeq++
	seq := a.recipientUpdateSeq
	// SetModel inside the native EN_CHANGE notification resets the child Edit
	// caret. With a Chinese IME the next committed character then appears at the
	// beginning. Defer the list update until the notification has returned.
	a.recipient.Synchronize(func() {
		if seq != a.recipientUpdateSeq || a.recipient.Text() != text {
			return
		}
		names := service.ContactSuggestions(&a.state, text)
		if len(names) > 20 {
			names = names[:20]
		}
		a.changing = true
		_ = a.recipient.SetModel(names)
		_ = a.recipient.SetText(text)
		caret := len(utf16.Encode([]rune(text)))
		a.recipient.SetTextSelection(caret, caret)
		a.changing = false
		if text != "" && len(names) > 0 {
			a.recipient.SendMessage(0x014F, 1, 0) // CB_SHOWDROPDOWN
		}
	})
}
func (a *envelopeApp) recipientSelected() {
	if a.changing {
		return
	}
	name := a.recipient.Text()
	if i := a.recipient.CurrentIndex(); i >= 0 {
		if names, ok := a.recipient.Model().([]string); ok && i < len(names) {
			name = names[i]
			_ = a.recipient.SetText(name)
		}
	}
	c := a.state.ContactByName(name)
	if c == nil {
		return
	}
	a.loadContact(c)
}
func (a *envelopeApp) loadContact(c *model.Contact) {
	a.changing = true
	defer func() { a.changing = false; a.invalidate() }()
	addresses := make([]string, len(c.Addresses))
	for i, v := range c.Addresses {
		label := v.Label
		if label == "" {
			label = fmt.Sprintf("地址%d", i+1)
		}
		addresses[i] = label + "｜" + v.Value
	}
	_ = a.address.SetModel(addresses)
	idx := 0
	for i, v := range c.Addresses {
		if v.ID == c.LastAddressID {
			idx = i
		}
	}
	if len(c.Addresses) > 0 {
		_ = a.address.SetCurrentIndex(idx)
		_ = a.address.SetText(c.Addresses[idx].Value)
		a.postal.SetText(c.Addresses[idx].PostalCode)
		a.loadPhones(c, &c.Addresses[idx])
	}
	selected := map[string]bool{}
	for _, id := range c.LastDeliveryOption {
		selected[id] = true
	}
	f := a.currentFormat()
	for i, cb := range a.delivery {
		checked := false
		if i < len(f.Delivery) {
			checked = selected[f.Delivery[i].ID]
		}
		cb.SetChecked(checked)
	}
}
func (a *envelopeApp) addressSelected() {
	if a.changing {
		return
	}
	c := a.state.ContactByName(a.recipient.Text())
	if c == nil {
		return
	}
	i := a.address.CurrentIndex()
	if i < 0 || i >= len(c.Addresses) {
		return
	}
	v := &c.Addresses[i]
	a.changing = true
	_ = a.address.SetText(v.Value)
	a.postal.SetText(v.PostalCode)
	a.loadPhones(c, v)
	a.changing = false
	a.invalidate()
}
func (a *envelopeApp) loadPhones(c *model.Contact, addr *model.Address) {
	vals := make([]string, len(c.Phones))
	idx := -1
	for i, p := range c.Phones {
		vals[i] = p.Display()
		if p.ID == addr.LastPhoneID {
			idx = i
		}
	}
	_ = a.phone.SetModel(vals)
	if idx >= 0 {
		_ = a.phone.SetCurrentIndex(idx)
		_ = a.phone.SetText(vals[idx])
	} else {
		_ = a.phone.SetText("")
	}
}
func (a *envelopeApp) addressChanged() {
	if a.changing {
		return
	}
	if a.postal.Text() == "" {
		if code := postal.Lookup(a.address.Text()); code != "" {
			a.postal.SetText(code)
		}
	}
	a.invalidate()
}
func (a *envelopeApp) invalidate() {
	if a.preview != nil {
		_ = a.preview.Invalidate()
	}
}

func (a *envelopeApp) applyFormatDefaults() {
	f := a.currentFormat()
	a.frameVisible.SetChecked(f.Frame.DefaultVisible)
	text := ""
	for _, v := range a.state.FrameTexts {
		if v.ID == f.Frame.DefaultTextID {
			text = v.Text
		}
	}
	_ = a.frameText.SetModel(a.frameTextValues())
	_ = a.frameText.SetText(text)
	a.invalidate()
}
func (a *envelopeApp) clear() {
	a.changing = true
	_ = a.recipient.SetText("")
	_ = a.address.SetText("")
	_ = a.address.SetModel(nil)
	_ = a.phone.SetText("")
	_ = a.phone.SetModel(nil)
	a.postal.SetText("")
	for _, cb := range a.delivery {
		cb.SetChecked(false)
	}
	a.changing = false
	a.applyFormatDefaults()
	a.status.SetText("已清空；目前格式與印表機設定保留。")
	a.recipient.SetFocus()
}

func (a *envelopeApp) print() {
	name, addr := strings.TrimSpace(a.recipient.Text()), strings.TrimSpace(a.address.Text())
	if name == "" || addr == "" {
		walk.MsgBox(a, "資料未完成", "收件人與收件人地址為必填。", walk.MsgBoxOK|walk.MsgBoxIconWarning)
		return
	}
	choice := service.AddressAdd
	if c := a.state.ContactByName(name); c != nil && len(c.Addresses) > 0 && c.AddressByValue(addr) == nil {
		n := len(c.Addresses) + 1
		result := walk.MsgBox(a, "發現新地址", fmt.Sprintf("「%s」已有地址資料。\n\n選「是」：另存為地址%d\n選「否」：覆蓋上一次使用的地址\n選「取消」：返回修改", name, n), walk.MsgBoxYesNoCancel|walk.MsgBoxIconQuestion)
		if result == walk.DlgCmdCancel {
			return
		}
		if result == walk.DlgCmdNo {
			choice = service.AddressOverwriteLast
		}
	}
	delivery := []string{}
	f := a.currentFormat()
	for i, cb := range a.delivery {
		if cb.Checked() && i < len(f.Delivery) {
			delivery = append(delivery, f.Delivery[i].ID)
		}
	}
	input := service.PrintInput{Recipient: name, Address: addr, PostalCode: a.postal.Text(), Phone: a.phone.Text(), DeliveryIDs: delivery, FrameVisible: a.frameVisible.Checked(), FrameText: a.frameText.Text(), AddressConflict: choice}
	if input.FrameVisible && strings.TrimSpace(input.FrameText) == "" {
		walk.MsgBox(a, "方框文字未設定", "已勾選印出方框文字，請選擇或輸入文字。", walk.MsgBoxOK|walk.MsgBoxIconWarning)
		return
	}
	c, err := service.CommitPrintIntent(&a.state, input)
	if err != nil {
		walk.MsgBox(a, "資料格式錯誤", err.Error(), walk.MsgBoxOK|walk.MsgBoxIconWarning)
		return
	}
	if err = a.store.Save(a.state); err != nil {
		walk.MsgBox(a, "無法保存", err.Error(), walk.MsgBoxOK|walk.MsgBoxIconError)
		return
	}
	a.loadContact(c)
	parsed, _ := phone.Parse(input.Phone)
	display := parsed.Number
	if parsed.Extension != "" {
		display += " #" + parsed.Extension
	}
	_ = a.phone.SetText(display)
	job := printer.Job{Recipient: name, Address: addr, Phone: display, PostalCode: a.postal.Text(), DeliveryIDs: delivery, FrameVisible: input.FrameVisible, FrameText: input.FrameText, Format: f}
	a.status.SetText("資料已保存，正在交給 Windows 列印……")
	if err = printer.Print(a.state.Settings.PrinterName, job); err != nil {
		a.status.SetText("資料已保存；列印未送出。")
		walk.MsgBox(a, "資料已保存，但列印失敗", err.Error()+"\n\n輸入內容已保存，不需重打。", walk.MsgBoxOK|walk.MsgBoxIconError)
		return
	}
	a.status.SetText("資料已保存，列印工作已送入 Windows。")
}

func (a *envelopeApp) paintEnvelope(canvas *walk.Canvas, _ walk.Rectangle) error {
	b := a.preview.ClientBounds()
	bg, _ := walk.NewSolidColorBrush(walk.RGB(241, 244, 248))
	defer bg.Dispose()
	canvas.FillRectangle(bg, b)
	f := a.currentFormat()
	wmm, hmm := f.WidthMM, f.HeightMM
	if f.Orientation == model.OrientationLandscape {
		wmm, hmm = hmm, wmm
	}
	scale := float64(b.Height-42) / hmm
	if float64(b.Width-42)/wmm < scale {
		scale = float64(b.Width-42) / wmm
	}
	w, h := int(wmm*scale), int(hmm*scale)
	ox, oy := (b.Width-w)/2, (b.Height-h)/2
	paper, _ := walk.NewSolidColorBrush(walk.RGB(253, 252, 255))
	defer paper.Dispose()
	border, _ := walk.NewCosmeticPen(walk.PenSolid, walk.RGB(185, 187, 198))
	defer border.Dispose()
	cut := max(8, int(5.5*scale))
	// The 15K envelope has clipped/sloped top corners rather than a plain sheet.
	canvas.FillRectangle(paper, walk.Rectangle{X: ox + cut, Y: oy, Width: w - cut*2, Height: cut})
	canvas.FillRectangle(paper, walk.Rectangle{X: ox, Y: oy + cut, Width: w, Height: h - cut})
	canvas.DrawLine(border, walk.Point{X: ox + cut, Y: oy}, walk.Point{X: ox + w - cut, Y: oy})
	canvas.DrawLine(border, walk.Point{X: ox + cut, Y: oy}, walk.Point{X: ox, Y: oy + cut})
	canvas.DrawLine(border, walk.Point{X: ox + w - cut, Y: oy}, walk.Point{X: ox + w, Y: oy + cut})
	canvas.DrawLine(border, walk.Point{X: ox, Y: oy + cut}, walk.Point{X: ox, Y: oy + h})
	canvas.DrawLine(border, walk.Point{X: ox + w, Y: oy + cut}, walk.Point{X: ox + w, Y: oy + h})
	canvas.DrawLine(border, walk.Point{X: ox, Y: oy + h}, walk.Point{X: ox + w, Y: oy + h})
	red, _ := walk.NewCosmeticPen(walk.PenSolid, walk.RGB(220, 128, 132))
	defer red.Dispose()
	fmtRect := func(r model.RectMM) walk.Rectangle {
		return walk.Rectangle{X: ox + int(r.X*scale), Y: oy + int(r.Y*scale), Width: max(2, int(r.W*scale)), Height: max(2, int(r.H*scale))}
	}
	drawMMBox := func(x, y, width, height float64) walk.Rectangle {
		r := fmtRect(model.RectMM{X: x, Y: y, W: width, H: height})
		canvas.DrawRectangle(red, r)
		return r
	}
	// Pre-printed structure from the real 15K envelope.
	for i := 0; i < 5; i++ {
		drawMMBox(52+float64(i)*8.2, 21, 6.6, 8)
	}
	stamp := drawMMBox(7, 34, 22, 18)
	canvas.DrawLine(red, walk.Point{X: stamp.X + stamp.Width/2, Y: stamp.Y}, walk.Point{X: stamp.X + stamp.Width/2, Y: stamp.Y + stamp.Height})
	deliveryTable := drawMMBox(7, 57, 22, 32)
	headerY := deliveryTable.Y + max(6, int(5*scale))
	canvas.DrawLine(red, walk.Point{X: deliveryTable.X, Y: headerY}, walk.Point{X: deliveryTable.X + deliveryTable.Width, Y: headerY})
	rowHeight := max(3, (deliveryTable.Y+deliveryTable.Height-headerY)/7)
	for i := 1; i < 7; i++ {
		y := headerY + i*rowHeight
		canvas.DrawLine(red, walk.Point{X: deliveryTable.X, Y: y}, walk.Point{X: deliveryTable.X + deliveryTable.Width, Y: y})
	}
	canvas.DrawLine(red, walk.Point{X: deliveryTable.X + max(7, int(5*scale)), Y: headerY}, walk.Point{X: deliveryTable.X + max(7, int(5*scale)), Y: deliveryTable.Y + deliveryTable.Height})
	drawMMBox(37, 42, 34, 155)
	for i := 0; i < 5; i++ {
		drawMMBox(5+float64(i)*7.2, 205, 5.8, 7)
	}
	labelFont, _ := walk.NewFont("Microsoft JhengHei UI", 7, 0)
	if labelFont != nil {
		defer labelFont.Dispose()
		labelColor := walk.RGB(202, 105, 110)
		canvas.DrawText("請寫收件人郵遞區號", labelFont, labelColor, fmtRect(model.RectMM{X: 51, Y: 30, W: 44, H: 6}), walk.TextCenter|walk.TextVCenter|walk.TextSingleLine)
		canvas.DrawText("郵票", labelFont, labelColor, walk.Rectangle{X: stamp.X, Y: stamp.Y + 2, Width: stamp.Width, Height: stamp.Height / 2}, walk.TextCenter|walk.TextVCenter|walk.TextSingleLine)
		canvas.DrawText("正聯", labelFont, labelColor, walk.Rectangle{X: stamp.X, Y: stamp.Y + stamp.Height/2, Width: stamp.Width, Height: stamp.Height/2 - 2}, walk.TextCenter|walk.TextVCenter|walk.TextSingleLine)
		canvas.DrawText("郵件種類／方式", labelFont, labelColor, walk.Rectangle{X: deliveryTable.X, Y: deliveryTable.Y, Width: deliveryTable.Width, Height: headerY - deliveryTable.Y}, walk.TextCenter|walk.TextVCenter|walk.TextSingleLine)
		canvas.DrawText("寄件人郵遞區號", labelFont, labelColor, fmtRect(model.RectMM{X: 4, Y: 214, W: 38, H: 5}), walk.TextCenter|walk.TextVCenter|walk.TextSingleLine)
		for i, opt := range f.Delivery {
			if i >= 7 {
				break
			}
			r := walk.Rectangle{X: deliveryTable.X + max(8, int(6*scale)), Y: headerY + i*rowHeight, Width: deliveryTable.Width - max(8, int(6*scale)), Height: rowHeight}
			canvas.DrawText(opt.Label, labelFont, labelColor, r, walk.TextCenter|walk.TextVCenter|walk.TextSingleLine)
		}
	}
	drawPreviewText(canvas, a.recipient.Text(), f.Recipient, ox, oy, scale, walk.RGB(38, 49, 61))
	drawPreviewText(canvas, a.address.Text(), f.Address, ox, oy, scale, walk.RGB(38, 49, 61))
	drawPreviewText(canvas, a.phone.Text(), f.Phone, ox, oy, scale, walk.RGB(65, 76, 88))
	drawPreviewText(canvas, a.postal.Text(), f.PostalCode, ox, oy, scale, walk.RGB(65, 76, 88))
	for i, opt := range f.Delivery {
		if i >= len(a.delivery) {
			break
		}
		x, y := ox+int(opt.X*scale), oy+int(opt.Y*scale)
		s := max(5, int(opt.MarkSize*scale))
		if a.delivery[i].Checked() {
			mark, _ := walk.NewCosmeticPen(walk.PenSolid, walk.RGB(35, 48, 62))
			if mark != nil {
				canvas.DrawLine(mark, walk.Point{X: x, Y: y + s/2}, walk.Point{X: x + s/3, Y: y + s})
				canvas.DrawLine(mark, walk.Point{X: x + s/3, Y: y + s}, walk.Point{X: x + s, Y: y})
				mark.Dispose()
			}
		}
	}
	if a.frameVisible.Checked() {
		r := fmtRect(f.Frame.Rect)
		canvas.DrawRectangle(red, r)
		layout := model.TextLayout{Rect: f.Frame.Rect, Font: f.Frame.Font, FontSize: f.Frame.FontSize, MinSize: 8}
		drawPreviewText(canvas, a.frameText.Text(), layout, ox, oy, scale, walk.RGB(38, 49, 61))
	}
	return nil
}

func drawPreviewText(c *walk.Canvas, text string, l model.TextLayout, ox, oy int, scale float64, color walk.Color) {
	text = strings.TrimSpace(text)
	if text == "" {
		return
	}
	size := l.FontSize
	if size < 8 {
		size = 8
	}
	font, err := walk.NewFont(l.Font, int(size), 0)
	if err != nil {
		font, _ = walk.NewFont("Microsoft JhengHei", int(size), 0)
	}
	if font == nil {
		return
	}
	defer font.Dispose()
	r := walk.Rectangle{X: ox + int(l.Rect.X*scale), Y: oy + int(l.Rect.Y*scale), Width: max(2, int(l.Rect.W*scale)), Height: max(2, int(l.Rect.H*scale))}
	if !l.Vertical {
		c.DrawText(text, font, color, r, walk.TextCenter|walk.TextVCenter|walk.TextSingleLine)
		return
	}
	runes := []rune(text)
	line := max(1, int(size*1.45))
	cap := max(1, r.Height/line)
	cols := l.MaxColumns
	if cols < 1 {
		cols = 1
	}
	for i, ch := range runes {
		col, row := i/cap, i%cap
		if col >= cols {
			break
		}
		cr := walk.Rectangle{X: r.X + r.Width - (col+1)*line, Y: r.Y + row*line, Width: line, Height: line}
		c.DrawText(string(ch), font, color, cr, walk.TextCenter|walk.TextVCenter|walk.TextSingleLine)
	}
}
func max(a, b int) int {
	if a > b {
		return a
	}
	return b
}

func (a *envelopeApp) openPrintSettings() {
	var dlg *walk.Dialog
	var printers *walk.ComboBox
	names := printer.Names()
	selected := 0
	for i, n := range names {
		if n == a.state.Settings.PrinterName {
			selected = i
		}
	}
	if len(names) == 0 {
		names = []string{"（找不到印表機）"}
	}
	_, err := Dialog{AssignTo: &dlg, Title: "列印設定", MinSize: Size{560, 250}, Layout: VBox{}, Children: []Widget{Label{Text: "Windows 印表機"}, ComboBox{AssignTo: &printers, Model: names, CurrentIndex: selected}, Label{Text: "信封尺寸與方向由目前格式自動送給印表機驅動程式。"}, Composite{Layout: HBox{}, Children: []Widget{PushButton{Text: "開啟原生印表機內容", OnClicked: func() { printer.Properties(uintptr(dlg.Handle()), printers.Text()) }}, HSpacer{}, PushButton{Text: "儲存", OnClicked: func() {
		if printers.Text() != "（找不到印表機）" {
			a.state.Settings.PrinterName = printers.Text()
			a.store.Save(a.state)
		}
		dlg.Accept()
	}}, PushButton{Text: "取消", OnClicked: func() { dlg.Cancel() }}}}}}.Run(a)
	if err != nil {
		walk.MsgBox(a, "列印設定", err.Error(), walk.MsgBoxOK|walk.MsgBoxIconError)
	}
}

func (a *envelopeApp) openSettings() {
	var dlg *walk.Dialog
	var split, direct *walk.CheckBox
	_, _ = Dialog{AssignTo: &dlg, Title: "設定", MinSize: Size{520, 270}, Layout: VBox{}, Children: []Widget{Label{Text: "主畫面版型", Font: Font{Family: "Microsoft JhengHei UI", PointSize: 12, Bold: true}}, CheckBox{AssignTo: &split, Text: "左右分割：左側輸入、右側即時預覽", Checked: a.state.Settings.MainLayout != model.MainLayoutDirect, OnClicked: func() { direct.SetChecked(false) }}, CheckBox{AssignTo: &direct, Text: "信封直接操作：依直式／橫式格式呈現", Checked: a.state.Settings.MainLayout == model.MainLayoutDirect, OnClicked: func() { split.SetChecked(false) }}, Label{Text: "版型會在下次啟動套用；欄位資料與列印座標不受影響。"}, VSpacer{}, Composite{Layout: HBox{}, Children: []Widget{HSpacer{}, PushButton{Text: "儲存", OnClicked: func() {
		if direct.Checked() {
			a.state.Settings.MainLayout = model.MainLayoutDirect
		} else {
			a.state.Settings.MainLayout = model.MainLayoutSplit
		}
		a.store.Save(a.state)
		dlg.Accept()
		walk.MsgBox(a, "設定已儲存", "主畫面版型將於下次啟動套用。", walk.MsgBoxOK|walk.MsgBoxIconInformation)
	}}, PushButton{Text: "取消", OnClicked: func() { dlg.Cancel() }}}}}}.Run(a)
	_ = split
}

func (a *envelopeApp) openFrameTexts() {
	changed := a.runFrameTextDialog(a)
	if changed {
		_ = a.frameText.SetModel(a.frameTextValues())
		a.applyFormatDefaults()
	}
}
func (a *envelopeApp) runFrameTextDialog(owner walk.Form) bool {
	var dlg *walk.Dialog
	var list *walk.ListBox
	var edit *walk.LineEdit
	changed := false
	values := a.frameTextValues()
	reload := func() {
		values = a.frameTextValues()
		if list != nil {
			list.SetModel(values)
		}
	}
	_, _ = Dialog{AssignTo: &dlg, Title: "方框文字", MinSize: Size{560, 430}, Layout: VBox{}, Children: []Widget{Label{Text: "此處只管理可用文字清單；是否顯示與預設文字由各信封格式決定。"}, ListBox{AssignTo: &list, Model: values, OnCurrentIndexChanged: func() {
		i := list.CurrentIndex()
		if i >= 0 && i < len(a.state.FrameTexts) {
			edit.SetText(a.state.FrameTexts[i].Text)
		}
	}}, LineEdit{AssignTo: &edit}, Composite{Layout: HBox{}, Children: []Widget{PushButton{Text: "新增", OnClicked: func() {
		t := strings.TrimSpace(edit.Text())
		if t != "" {
			a.state.FrameTexts = append(a.state.FrameTexts, model.FrameText{ID: model.NewID("frame"), Text: t})
			changed = true
			reload()
		}
	}}, PushButton{Text: "修改", OnClicked: func() {
		i := list.CurrentIndex()
		t := strings.TrimSpace(edit.Text())
		if i >= 0 && i < len(a.state.FrameTexts) && t != "" {
			id := a.state.FrameTexts[i].ID
			a.state.FrameTexts[i].Text = t
			for x := range a.state.Formats {
				if a.state.Formats[x].Frame.DefaultTextID == id {
					a.state.Formats[x].Frame.DefaultTextID = ""
					a.state.Formats[x].Frame.NeedsText = true
				}
			}
			changed = true
			reload()
		}
	}}, PushButton{Text: "刪除", OnClicked: func() {
		i := list.CurrentIndex()
		if i >= 0 && i < len(a.state.FrameTexts) {
			id := a.state.FrameTexts[i].ID
			a.state.FrameTexts = append(a.state.FrameTexts[:i], a.state.FrameTexts[i+1:]...)
			for x := range a.state.Formats {
				if a.state.Formats[x].Frame.DefaultTextID == id {
					a.state.Formats[x].Frame.DefaultTextID = ""
					a.state.Formats[x].Frame.NeedsText = true
				}
			}
			changed = true
			reload()
		}
	}}, HSpacer{}, PushButton{Text: "完成", OnClicked: func() {
		if changed {
			a.store.Save(a.state)
		}
		dlg.Accept()
	}}}}}}.Run(owner)
	return changed
}

func (a *envelopeApp) openContacts() {
	var dlg *walk.Dialog
	var list *walk.ListBox
	var summary *walk.TextEdit
	names := a.contactNames()
	show := func() {
		i := list.CurrentIndex()
		if i < 0 || i >= len(a.state.Contacts) {
			return
		}
		c := a.state.ContactByName(names[i])
		if c == nil {
			return
		}
		var b strings.Builder
		fmt.Fprintf(&b, "收件人：%s\r\n\r\n", c.Name)
		for j, v := range c.Addresses {
			label := v.Label
			if label == "" {
				label = fmt.Sprintf("地址%d", j+1)
			}
			fmt.Fprintf(&b, "%s：%s\r\n備註：%s\r\n", label, v.Value, v.Note)
		}
		for j, v := range c.Phones {
			fmt.Fprintf(&b, "\r\n電話%d：%s\r\n備註：%s", j+1, v.Display(), v.Note)
		}
		summary.SetText(b.String())
	}
	_, _ = Dialog{AssignTo: &dlg, Title: "聯絡人資料", MinSize: Size{760, 540}, Layout: HBox{}, Children: []Widget{Composite{MinSize: Size{240, 0}, Layout: VBox{}, Children: []Widget{Label{Text: "收件人"}, ListBox{AssignTo: &list, Model: names, OnCurrentIndexChanged: show}, PushButton{Text: "刪除聯絡人", OnClicked: func() {
		i := list.CurrentIndex()
		if i >= 0 && i < len(names) && walk.MsgBox(dlg, "確認刪除", "刪除「"+names[i]+"」及其地址、電話？", walk.MsgBoxYesNo|walk.MsgBoxIconQuestion) == walk.DlgCmdYes {
			for j := range a.state.Contacts {
				if a.state.Contacts[j].Name == names[i] {
					a.state.Contacts = append(a.state.Contacts[:j], a.state.Contacts[j+1:]...)
					break
				}
			}
			a.store.Save(a.state)
			names = a.contactNames()
			list.SetModel(names)
			summary.SetText("")
		}
	}}}}, Composite{Layout: VBox{}, Children: []Widget{Label{Text: "資料內容（地址名稱／備註與電話備註可在下方編輯）"}, TextEdit{AssignTo: &summary, ReadOnly: true}, PushButton{Text: "編輯選取聯絡人", OnClicked: func() {
		i := list.CurrentIndex()
		if i >= 0 && i < len(names) {
			a.editContact(dlg, names[i])
			show()
		}
	}}, PushButton{Text: "關閉", OnClicked: func() { dlg.Accept() }}}}}}.Run(a)
	_ = a.recipient.SetModel(a.contactNames())
}

func (a *envelopeApp) editContact(owner walk.Form, name string) {
	c := a.state.ContactByName(name)
	if c == nil {
		return
	}
	var dlg *walk.Dialog
	var addressList, phoneList *walk.ListBox
	var label, addressNote, phoneNote *walk.LineEdit
	addressNames := func() []string {
		r := make([]string, len(c.Addresses))
		for i, v := range c.Addresses {
			l := v.Label
			if l == "" {
				l = fmt.Sprintf("地址%d", i+1)
			}
			r[i] = l + "｜" + v.Value
		}
		return r
	}
	phoneNames := func() []string {
		r := make([]string, len(c.Phones))
		for i, v := range c.Phones {
			r[i] = v.Display()
		}
		return r
	}
	_, _ = Dialog{AssignTo: &dlg, Title: "編輯聯絡人｜" + name, MinSize: Size{760, 500}, Layout: Grid{Columns: 2}, Children: []Widget{Label{Text: "地址"}, Label{Text: "電話"}, ListBox{AssignTo: &addressList, Model: addressNames(), OnCurrentIndexChanged: func() {
		i := addressList.CurrentIndex()
		if i >= 0 && i < len(c.Addresses) {
			label.SetText(c.Addresses[i].Label)
			addressNote.SetText(c.Addresses[i].Note)
		}
	}}, ListBox{AssignTo: &phoneList, Model: phoneNames(), OnCurrentIndexChanged: func() {
		i := phoneList.CurrentIndex()
		if i >= 0 && i < len(c.Phones) {
			phoneNote.SetText(c.Phones[i].Note)
		}
	}}, Composite{Layout: Grid{Columns: 2}, Children: []Widget{Label{Text: "地址名稱"}, LineEdit{AssignTo: &label}, Label{Text: "地址備註"}, LineEdit{AssignTo: &addressNote}, PushButton{ColumnSpan: 2, Text: "儲存地址名稱／備註", OnClicked: func() {
		i := addressList.CurrentIndex()
		if i >= 0 && i < len(c.Addresses) {
			c.Addresses[i].Label = label.Text()
			c.Addresses[i].Note = addressNote.Text()
			a.store.Save(a.state)
			addressList.SetModel(addressNames())
		}
	}}, PushButton{ColumnSpan: 2, Text: "刪除此地址", OnClicked: func() {
		i := addressList.CurrentIndex()
		if i >= 0 && i < len(c.Addresses) {
			c.Addresses = append(c.Addresses[:i], c.Addresses[i+1:]...)
			a.store.Save(a.state)
			addressList.SetModel(addressNames())
		}
	}}}}, Composite{Layout: Grid{Columns: 2}, Children: []Widget{Label{Text: "電話備註"}, LineEdit{AssignTo: &phoneNote}, PushButton{ColumnSpan: 2, Text: "儲存電話備註", OnClicked: func() {
		i := phoneList.CurrentIndex()
		if i >= 0 && i < len(c.Phones) {
			c.Phones[i].Note = phoneNote.Text()
			a.store.Save(a.state)
		}
	}}, PushButton{ColumnSpan: 2, Text: "刪除此電話", OnClicked: func() {
		i := phoneList.CurrentIndex()
		if i >= 0 && i < len(c.Phones) {
			id := c.Phones[i].ID
			c.Phones = append(c.Phones[:i], c.Phones[i+1:]...)
			for j := range c.Addresses {
				if c.Addresses[j].LastPhoneID == id {
					c.Addresses[j].LastPhoneID = ""
				}
			}
			a.store.Save(a.state)
			phoneList.SetModel(phoneNames())
		}
	}}}}, PushButton{ColumnSpan: 2, Text: "完成", OnClicked: func() { dlg.Accept() }}}}.Run(owner)
}

func (a *envelopeApp) openFormatSettings() {
	var dlg *walk.Dialog
	var formatBox, elementBox, fontBox, frameBox *walk.ComboBox
	var width, height, x, y, w, h, size, minSize *walk.NumberEdit
	var landscape, frameDefault *walk.CheckBox
	var preview *walk.CustomWidget
	formatNames := func() []string {
		r := make([]string, len(a.state.Formats))
		for i, v := range a.state.Formats {
			r[i] = v.Name
		}
		return r
	}
	current := 0
	for i, v := range a.state.Formats {
		if v.ID == a.state.Settings.SelectedFormatID {
			current = i
		}
	}
	loading := false
	selectedLayout := func(f *model.EnvelopeFormat) *model.TextLayout {
		switch elementBox.CurrentIndex() {
		case 0:
			return &f.Recipient
		case 1:
			return &f.Address
		case 2:
			return &f.Phone
		default:
			return &f.PostalCode
		}
	}
	load := func() {
		if formatBox == nil || elementBox == nil {
			return
		}
		i := formatBox.CurrentIndex()
		if i < 0 || i >= len(a.state.Formats) {
			return
		}
		loading = true
		f := &a.state.Formats[i]
		width.SetValue(f.WidthMM)
		height.SetValue(f.HeightMM)
		landscape.SetChecked(f.Orientation == model.OrientationLandscape)
		l := selectedLayout(f)
		x.SetValue(l.Rect.X)
		y.SetValue(l.Rect.Y)
		w.SetValue(l.Rect.W)
		h.SetValue(l.Rect.H)
		size.SetValue(l.FontSize)
		minSize.SetValue(l.MinSize)
		fontBox.SetText(l.Font)
		frameDefault.SetChecked(f.Frame.DefaultVisible)
		idx := -1
		for j, t := range a.state.FrameTexts {
			if t.ID == f.Frame.DefaultTextID {
				idx = j
			}
		}
		frameBox.SetModel(a.frameTextValues())
		frameBox.SetCurrentIndex(idx)
		if f.Frame.NeedsText {
			frameBox.SetText("⚠ 請重新選擇")
		}
		loading = false
		preview.Invalidate()
	}
	saveFields := func() {
		if loading {
			return
		}
		i := formatBox.CurrentIndex()
		if i < 0 || i >= len(a.state.Formats) {
			return
		}
		f := &a.state.Formats[i]
		f.WidthMM = width.Value()
		f.HeightMM = height.Value()
		if landscape.Checked() {
			f.Orientation = model.OrientationLandscape
		} else {
			f.Orientation = model.OrientationPortrait
		}
		l := selectedLayout(f)
		l.Rect = model.RectMM{X: x.Value(), Y: y.Value(), W: w.Value(), H: h.Value()}
		l.FontSize = size.Value()
		l.MinSize = minSize.Value()
		l.Font = fontBox.Text()
		f.Frame.DefaultVisible = frameDefault.Checked()
		if j := frameBox.CurrentIndex(); j >= 0 && j < len(a.state.FrameTexts) {
			f.Frame.DefaultTextID = a.state.FrameTexts[j].ID
			f.Frame.NeedsText = false
		}
		preview.Invalidate()
	}
	paint := func(c *walk.Canvas, _ walk.Rectangle) error {
		if formatBox == nil {
			return nil
		}
		i := formatBox.CurrentIndex()
		if i < 0 || i >= len(a.state.Formats) {
			return nil
		}
		f := a.state.Formats[i]
		b := preview.ClientBounds()
		bg, _ := walk.NewSolidColorBrush(walk.RGB(242, 246, 250))
		defer bg.Dispose()
		c.FillRectangle(bg, b)
		scale := float64(b.Height-40) / f.HeightMM
		if float64(b.Width-40)/f.WidthMM < scale {
			scale = float64(b.Width-40) / f.WidthMM
		}
		ew, eh := int(f.WidthMM*scale), int(f.HeightMM*scale)
		ox, oy := (b.Width-ew)/2, (b.Height-eh)/2
		paper, _ := walk.NewSolidColorBrush(walk.RGB(255, 255, 252))
		defer paper.Dispose()
		pen, _ := walk.NewCosmeticPen(walk.PenSolid, walk.RGB(210, 70, 70))
		defer pen.Dispose()
		c.FillRectangle(paper, walk.Rectangle{X: ox, Y: oy, Width: ew, Height: eh})
		l := selectedLayout(&f)
		r := walk.Rectangle{X: ox + int(l.Rect.X*scale), Y: oy + int(l.Rect.Y*scale), Width: int(l.Rect.W * scale), Height: int(l.Rect.H * scale)}
		c.DrawRectangle(pen, r)
		represent := []string{"收件人", "收件人地址", "收件人電話", "郵遞區號"}[elementBox.CurrentIndex()]
		drawPreviewText(c, represent, *l, ox, oy, scale, walk.RGB(40, 53, 67))
		font, _ := walk.NewFont("Microsoft JhengHei UI", 9, 0)
		if font != nil {
			defer font.Dispose()
			label := fmt.Sprintf("X %.1f  Y %.1f  W %.1f  H %.1f mm", l.Rect.X, l.Rect.Y, l.Rect.W, l.Rect.H)
			c.DrawText(label, font, walk.RGB(200, 45, 45), walk.Rectangle{X: r.X, Y: max(0, r.Y-26), Width: 260, Height: 24}, walk.TextLeft|walk.TextVCenter|walk.TextSingleLine)
		}
		return nil
	}
	change := func() { saveFields() }
	_, _ = Dialog{AssignTo: &dlg, Title: "格式設定", MinSize: Size{1060, 710}, Layout: VBox{}, Children: []Widget{Composite{Layout: HBox{}, Children: []Widget{Label{Text: "信封格式"}, ComboBox{AssignTo: &formatBox, Model: formatNames(), CurrentIndex: current, OnCurrentIndexChanged: load}, PushButton{Text: "新增", OnClicked: func() {
		nf := a.currentFormat()
		nf.ID = model.NewID("format")
		nf.Name = "新信封格式" + strconv.Itoa(len(a.state.Formats)+1)
		nf.IsDefault = false
		a.state.Formats = append(a.state.Formats, nf)
		formatBox.SetModel(formatNames())
		formatBox.SetCurrentIndex(len(a.state.Formats) - 1)
	}}, PushButton{Text: "複製", OnClicked: func() {
		i := formatBox.CurrentIndex()
		if i >= 0 {
			nf := a.state.Formats[i]
			nf.ID = model.NewID("format")
			nf.Name += "（複製）"
			nf.IsDefault = false
			a.state.Formats = append(a.state.Formats, nf)
			formatBox.SetModel(formatNames())
			formatBox.SetCurrentIndex(len(a.state.Formats) - 1)
		}
	}}, PushButton{Text: "刪除", OnClicked: func() {
		i := formatBox.CurrentIndex()
		if i > 0 {
			a.state.Formats = append(a.state.Formats[:i], a.state.Formats[i+1:]...)
			formatBox.SetModel(formatNames())
			formatBox.SetCurrentIndex(0)
		}
	}}, HSpacer{}, PushButton{Text: "開啟方框文字設定", OnClicked: func() { a.runFrameTextDialog(dlg); load() }}}}, Composite{Layout: HBox{}, Children: []Widget{CustomWidget{AssignTo: &preview, MinSize: Size{570, 520}, ClearsBackground: true, InvalidatesOnResize: true, Paint: paint}, Composite{MinSize: Size{390, 0}, Layout: Grid{Columns: 2, Spacing: 6}, Children: []Widget{Label{Text: "信封寬 (mm)"}, NumberEdit{AssignTo: &width, Decimals: 1, MinValue: 50, MaxValue: 500, OnValueChanged: change}, Label{Text: "信封高 (mm)"}, NumberEdit{AssignTo: &height, Decimals: 1, MinValue: 50, MaxValue: 500, OnValueChanged: change}, Label{Text: "橫式"}, CheckBox{AssignTo: &landscape, OnCheckedChanged: change}, Label{ColumnSpan: 2, Text: "目前校正項目", Font: Font{Family: "Microsoft JhengHei UI", PointSize: 11, Bold: true}}, Label{Text: "項目"}, ComboBox{AssignTo: &elementBox, Model: []string{"收件人", "收件人地址", "收件人電話", "郵遞區號"}, CurrentIndex: 0, OnCurrentIndexChanged: load}, Label{Text: "X (mm)"}, NumberEdit{AssignTo: &x, Decimals: 1, MaxValue: 500, OnValueChanged: change}, Label{Text: "Y (mm)"}, NumberEdit{AssignTo: &y, Decimals: 1, MaxValue: 500, OnValueChanged: change}, Label{Text: "寬 W (mm)"}, NumberEdit{AssignTo: &w, Decimals: 1, MaxValue: 500, OnValueChanged: change}, Label{Text: "高 H (mm)"}, NumberEdit{AssignTo: &h, Decimals: 1, MaxValue: 500, OnValueChanged: change}, Label{Text: "字體"}, ComboBox{AssignTo: &fontBox, Editable: true, Model: []string{"DFKai-SB", "Microsoft JhengHei", "Microsoft JhengHei UI"}, OnTextChanged: change}, Label{Text: "字級"}, NumberEdit{AssignTo: &size, Decimals: 1, MinValue: 6, MaxValue: 72, OnValueChanged: change}, Label{Text: "最小字級"}, NumberEdit{AssignTo: &minSize, Decimals: 1, MinValue: 6, MaxValue: 72, OnValueChanged: change}, Label{Text: "預設印方框文字"}, CheckBox{AssignTo: &frameDefault, OnCheckedChanged: change}, Label{Text: "格式預設文字"}, ComboBox{AssignTo: &frameBox, Model: a.frameTextValues(), OnCurrentIndexChanged: change}, Label{ColumnSpan: 2, Text: "紅框與數值標籤會即時更新；方向、勾選位置、方框位置與全域偏移將在取得實際掃描信封後精校。"}}}}}, Composite{Layout: HBox{}, Children: []Widget{HSpacer{}, PushButton{Text: "設為預設並儲存", OnClicked: func() {
		saveFields()
		i := formatBox.CurrentIndex()
		if i < 0 {
			return
		}
		if a.state.Formats[i].Frame.DefaultVisible && a.state.Formats[i].Frame.DefaultTextID == "" {
			walk.MsgBox(dlg, "尚未完成", "此格式預設印方框文字，必須重新選擇方框文字。", walk.MsgBoxOK|walk.MsgBoxIconWarning)
			return
		}
		for j := range a.state.Formats {
			a.state.Formats[j].IsDefault = j == i
		}
		a.state.Settings.SelectedFormatID = a.state.Formats[i].ID
		a.store.Save(a.state)
		dlg.Accept()
		a.applyFormatDefaults()
		a.invalidate()
	}}, PushButton{Text: "取消", OnClicked: func() { state, _ := a.store.Load(); a.state = state; dlg.Cancel() }}}}}}.Run(a)
	_ = load
}
