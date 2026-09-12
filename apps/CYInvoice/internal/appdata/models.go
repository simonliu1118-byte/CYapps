package appdata

import (
	"encoding/json"
	"strings"
)

const (
	EnvironmentTest       = "test"
	EnvironmentProduction = "prod"

	InvoiceStateOpened   = "已開立"
	InvoiceStateFailed   = "開立失敗"
	InvoiceStateUnknown  = "結果不明"
	InvoiceStateChanging = "資料變更中"
	InvoiceStateVoided   = "已作廢"

	SourceManual = "手動"
	SourceMO     = "MO店+"
	SourceCoupang = "酷澎"
	SourceERP     = "鼎新 ERP"
)

// Settings mirrors the field names used by the V1.0.0 settings.json file.
// Passwords and the production App Key are never stored as plaintext.
type Settings struct {
	Environment   string `json:"environment"`
	ProdInvoice   string `json:"prod_invoice"`
	ProdAppKeyEnc string `json:"prod_app_key_enc"`
	MOPasswordEnc string `json:"mo_password_enc"`
	PasswordSalt  string `json:"password_salt"`
	PasswordHash  string `json:"password_hash"`
	AdminPasswordSet bool `json:"admin_password_set"`
}

type InvoiceItem struct {
	Description     string  `json:"description"`
	Quantity        float64 `json:"quantity"`
	QuantityDecimal string  `json:"quantity_decimal,omitempty"`
	UnitPrice       int64   `json:"unit_price"`
	UnitPriceDecimal string `json:"unit_price_decimal,omitempty"`
	TaxType         string  `json:"tax_type"`
	Amount          int64   `json:"amount"`
	AmountDecimal   string  `json:"amount_decimal,omitempty"`
	Remark          string  `json:"remark,omitempty"`
	AllowSubtotalRounding bool `json:"allow_subtotal_rounding,omitempty"`
}

// InvoiceRecord keeps V1.0.0 JSON names so existing Data/invoices.json files
// can be loaded without renaming fields.
type InvoiceRecord struct {
	ID                   string        `json:"id"`
	Source               string        `json:"source"`
	OriginalOrderID      string        `json:"original_order_id"`
	OrderID              string        `json:"order_id"`
	Attempt              int           `json:"attempt"`
	InvoiceNumber        string        `json:"invoice_number"`
	BuyerIdentifier      string        `json:"buyer_identifier"`
	BuyerName            string        `json:"buyer_name"`
	APIOrderID           string        `json:"api_order_id,omitempty"`
	CarrierType          string        `json:"carrier_type,omitempty"`
	CarrierID1           string        `json:"carrier_id1,omitempty"`
	CarrierID2           string        `json:"carrier_id2,omitempty"`
	NPOBAN               string        `json:"npo_ban,omitempty"`
	Amount               int64         `json:"amount"`
	Delivery             string        `json:"delivery"`
	InvoiceState         string        `json:"invoice_state"`
	UploadStatus         int           `json:"upload_status"`
	UploadStatusText     string        `json:"upload_status_text"`
	ErrorMessage         string        `json:"error_message"`
	Environment          string        `json:"environment"`
	SentAt               string        `json:"sent_at,omitempty"`
	InvoiceDate          string        `json:"invoice_date"`
	InvoiceTime          string        `json:"invoice_time"`
	LastChecked          string        `json:"last_checked"`
	Items                []InvoiceItem `json:"items,omitempty"`
	MainRemark           string        `json:"main_remark,omitempty"`
	DetailVAT            int           `json:"detail_vat,omitempty"`
	BuyerNameNeedsMemory bool          `json:"buyer_name_needs_memory,omitempty"`
	Extra                map[string]json.RawMessage `json:"-"`
}

type StatusUpdate struct {
	InvoiceNumber    string
	InvoiceState     string
	UploadStatus     int
	UploadStatusText string
	ErrorMessage     string
	InvoiceDate      string
	InvoiceTime      string
	LastChecked      string
}

func normalizeSource(value string) string {
	return strings.ToLower(strings.TrimSpace(value))
}
