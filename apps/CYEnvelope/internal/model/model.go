package model

import "time"

const (
	CurrentSchemaVersion = 2
	OrientationPortrait  = "portrait"
	OrientationLandscape = "landscape"
	MainLayoutSplit      = "split"
	MainLayoutDirect     = "direct"
)

type RectMM struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
	W float64 `json:"w"`
	H float64 `json:"h"`
}

type TextLayout struct {
	Rect       RectMM  `json:"rect"`
	Font       string  `json:"font"`
	FontSize   float64 `json:"font_size"`
	MinSize    float64 `json:"min_size"`
	Vertical   bool    `json:"vertical"`
	MaxColumns int     `json:"max_columns"`
}

type DeliveryOption struct {
	ID       string  `json:"id"`
	Label    string  `json:"label"`
	X        float64 `json:"x"`
	Y        float64 `json:"y"`
	MarkSize float64 `json:"mark_size"`
}

type FrameLayout struct {
	Rect           RectMM  `json:"rect"`
	Font           string  `json:"font"`
	FontSize       float64 `json:"font_size"`
	LineWidth      float64 `json:"line_width"`
	DefaultVisible bool    `json:"default_visible"`
	DefaultTextID  string  `json:"default_text_id"`
	NeedsText      bool    `json:"needs_text"`
}

type EnvelopeFormat struct {
	ID          string           `json:"id"`
	Name        string           `json:"name"`
	WidthMM     float64          `json:"width_mm"`
	HeightMM    float64          `json:"height_mm"`
	Orientation string           `json:"orientation"`
	Rotation    int              `json:"rotation"`
	IsDefault   bool             `json:"is_default"`
	Recipient   TextLayout       `json:"recipient"`
	Address     TextLayout       `json:"address"`
	Phone       TextLayout       `json:"phone"`
	PostalCode  TextLayout       `json:"postal_code"`
	Delivery    []DeliveryOption `json:"delivery"`
	Frame       FrameLayout      `json:"frame"`
	OffsetXMM   float64          `json:"offset_x_mm"`
	OffsetYMM   float64          `json:"offset_y_mm"`
}

type Address struct {
	ID          string `json:"id"`
	Label       string `json:"label"`
	Value       string `json:"value"`
	Note        string `json:"note"`
	PostalCode  string `json:"postal_code"`
	LastPhoneID string `json:"last_phone_id"`
}

type Phone struct {
	ID        string `json:"id"`
	Number    string `json:"number"`
	Extension string `json:"extension"`
	Note      string `json:"note"`
}

func (p Phone) Display() string {
	if p.Extension == "" {
		return p.Number
	}
	return p.Number + " #" + p.Extension
}

type Contact struct {
	ID                 string    `json:"id"`
	Name               string    `json:"name"`
	Addresses          []Address `json:"addresses"`
	Phones             []Phone   `json:"phones"`
	LastAddressID      string    `json:"last_address_id"`
	LastDeliveryOption []string  `json:"last_delivery_option"`
	UpdatedAt          time.Time `json:"updated_at"`
}

type FrameText struct {
	ID   string `json:"id"`
	Text string `json:"text"`
}

type Settings struct {
	MainLayout       string `json:"main_layout"`
	SelectedFormatID string `json:"selected_format_id"`
	PrinterName      string `json:"printer_name"`
}

type State struct {
	SchemaVersion int              `json:"schema_version"`
	Contacts      []Contact        `json:"contacts"`
	Formats       []EnvelopeFormat `json:"formats"`
	FrameTexts    []FrameText      `json:"frame_texts"`
	Settings      Settings         `json:"settings"`
}

func DefaultState() State {
	const kai = "DFKai-SB"
	return State{
		SchemaVersion: CurrentSchemaVersion,
		FrameTexts:    []FrameText{{ID: "frame-statement", Text: "內附對帳單"}},
		Formats: []EnvelopeFormat{{
			ID: "format-15k", Name: "15K 標準信封", WidthMM: 105, HeightMM: 222,
			Orientation: OrientationPortrait, Rotation: 0, IsDefault: true,
			Recipient:  TextLayout{Rect: RectMM{X: 43, Y: 49, W: 18, H: 142}, Font: kai, FontSize: 24, MinSize: 14, Vertical: true, MaxColumns: 1},
			Address:    TextLayout{Rect: RectMM{X: 64, Y: 46, W: 29, H: 148}, Font: kai, FontSize: 14, MinSize: 9, Vertical: true, MaxColumns: 2},
			Phone:      TextLayout{Rect: RectMM{X: 32, Y: 59, W: 8, H: 132}, Font: kai, FontSize: 10, MinSize: 8, Vertical: true, MaxColumns: 1},
			PostalCode: TextLayout{Rect: RectMM{X: 52, Y: 21, W: 40, H: 8}, Font: kai, FontSize: 12, MinSize: 10, Vertical: false, MaxColumns: 1},
			Delivery: []DeliveryOption{
				{ID: "delivery-1", Label: "平信", X: 8.2, Y: 63, MarkSize: 2.8},
				{ID: "delivery-2", Label: "限時", X: 8.2, Y: 67, MarkSize: 2.8},
				{ID: "delivery-3", Label: "掛號", X: 8.2, Y: 71, MarkSize: 2.8},
				{ID: "delivery-4", Label: "限時掛號", X: 8.2, Y: 75, MarkSize: 2.8},
				{ID: "delivery-5", Label: "印刷品", X: 8.2, Y: 79, MarkSize: 2.8},
				{ID: "delivery-6", Label: "航空", X: 8.2, Y: 83, MarkSize: 2.8},
				{ID: "delivery-7", Label: "其他", X: 8.2, Y: 87, MarkSize: 2.8},
			},
			Frame: FrameLayout{Rect: RectMM{X: 7, Y: 95, W: 30, H: 12}, Font: kai, FontSize: 11, LineWidth: .35, DefaultVisible: true, DefaultTextID: "frame-statement"},
		}},
		Settings: Settings{MainLayout: MainLayoutSplit, SelectedFormatID: "format-15k"},
	}
}
