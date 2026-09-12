package amego

import (
	"bytes"
	"encoding/json"
	"fmt"
	"strings"
)

const (
	DefaultBaseURL = "https://invoice-api.amego.tw"
	TestInvoice    = "12345678"
	// TestAppKey is the public demonstration credential published in AMEGO's
	// official API documentation. Production keys must never be compiled in.
	TestAppKey     = "sHeq7t8G1wiQvhAuIM27"

	UploadPending    = 1
	UploadUploading  = 2
	UploadUploaded   = 3
	UploadProcessing = 31
	UploadConfirming = 32
	UploadError      = 91
	UploadComplete   = 99
)

type ProductItem struct {
	Description string      `json:"Description"`
	Quantity    interface{} `json:"Quantity"`
	Unit        string      `json:"Unit,omitempty"`
	UnitPrice   interface{} `json:"UnitPrice"`
	Amount      interface{} `json:"Amount"`
	Remark      string      `json:"Remark,omitempty"`
	TaxType     int         `json:"TaxType"`
}

type IssueRequest struct {
	OrderID                 string        `json:"OrderId"`
	BuyerIdentifier         string        `json:"BuyerIdentifier"`
	BuyerName               string        `json:"BuyerName"`
	BuyerAddress            string        `json:"BuyerAddress"`
	BuyerTelephoneNumber    string        `json:"BuyerTelephoneNumber"`
	BuyerEmailAddress       string        `json:"BuyerEmailAddress"`
	MainRemark              string        `json:"MainRemark"`
	CarrierType             string        `json:"CarrierType"`
	CarrierID1              string        `json:"CarrierId1"`
	CarrierID2              string        `json:"CarrierId2"`
	NPOBAN                  string        `json:"NPOBAN"`
	ProductItems            []ProductItem `json:"ProductItem"`
	SalesAmount             interface{}   `json:"SalesAmount"`
	FreeTaxSalesAmount      interface{}   `json:"FreeTaxSalesAmount"`
	ZeroTaxSalesAmount      interface{}   `json:"ZeroTaxSalesAmount"`
	TaxType                 int           `json:"TaxType"`
	TaxRate                 string        `json:"TaxRate"`
	TaxAmount               interface{}   `json:"TaxAmount"`
	TotalAmount             interface{}   `json:"TotalAmount"`
	DetailVAT               int           `json:"DetailVat"`
	DetailAmountRound       int           `json:"DetailAmountRound,omitempty"`
}

type IssueResult struct {
	InvoiceNumber string `json:"invoice_number"`
	InvoiceTime   int64  `json:"invoice_time"`
	RandomNumber  string `json:"random_number"`
	Barcode       string `json:"barcode"`
	QRCodeLeft    string `json:"qrcode_left"`
	QRCodeRight   string `json:"qrcode_right"`
	Base64Data    string `json:"base64_data"`
}

type IssueResponse struct {
	Code int
	Msg  string
	IssueResult
}

type QueryRequest struct {
	Type          string `json:"type"`
	OrderID       string `json:"order_id,omitempty"`
	InvoiceNumber string `json:"invoice_number,omitempty"`
}

type QueryResult struct {
	InvoiceNumber   string          `json:"invoice_number"`
	InvoiceType     string          `json:"invoice_type"`
	InvoiceStatus   int             `json:"invoice_status"`
	InvoiceDate     string          `json:"invoice_date"`
	InvoiceTime     string          `json:"invoice_time"`
	BuyerIdentifier string          `json:"buyer_identifier"`
	BuyerName       string          `json:"buyer_name"`
	SalesAmount     json.Number     `json:"sales_amount"`
	TaxAmount       json.Number     `json:"tax_amount"`
	TotalAmount     json.Number     `json:"total_amount"`
	CarrierType     string          `json:"carrier_type"`
	CarrierID1      string          `json:"carrier_id1"`
	CarrierID2      string          `json:"carrier_id2"`
	NPOBAN          string          `json:"npoban"`
	CancelDate      int64           `json:"cancel_date"`
	OrderID         string          `json:"order_id"`
	CreateDate      int64           `json:"create_date"`
	ProductItems    json.RawMessage `json:"product_item"`
	DetailVAT       int             `json:"-"`
	DetailVATPresent bool           `json:"-"`
}

// UnmarshalJSON accepts the current API documentation field names and the
// invoice_* aliases returned by earlier AMEGO deployments. Some production
// responses encode invoice date/time as JSON numbers while others use strings;
// both shapes represent scalar text and are normalized here before business
// verification so a harmless wire-type difference cannot break status refresh.
func (result *QueryResult) UnmarshalJSON(data []byte) error {
	type wire struct {
		InvoiceNumber    string          `json:"invoice_number"`
		Type             string          `json:"type"`
		InvoiceType      string          `json:"invoice_type"`
		Status           int             `json:"status"`
		InvoiceStatus    int             `json:"invoice_status"`
		Date             json.RawMessage `json:"date"`
		InvoiceDate      json.RawMessage `json:"invoice_date"`
		Time             json.RawMessage `json:"time"`
		InvoiceTime      json.RawMessage `json:"invoice_time"`
		BuyerIdentifier  string          `json:"buyer_identifier"`
		BuyerName        string          `json:"buyer_name"`
		SalesAmount      json.Number     `json:"sales_amount"`
		TaxAmount        json.Number     `json:"tax_amount"`
		TotalAmount      json.Number     `json:"total_amount"`
		CarrierType      string          `json:"carrier_type"`
		CarrierID1       string          `json:"carrier_id1"`
		CarrierID2       string          `json:"carrier_id2"`
		NPOBAN           string          `json:"npoban"`
		CancelDate       int64           `json:"cancel_date"`
		OrderID          string          `json:"order_id"`
		CreateDate       int64           `json:"create_date"`
		ProductItems     json.RawMessage `json:"product_item"`
		DetailVAT        *int            `json:"detail_vat"`
		DetailVATLegacy  *int            `json:"DetailVat"`
	}
	var value wire
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.UseNumber()
	if err := decoder.Decode(&value); err != nil {
		return err
	}
	date, err := queryScalarText(value.Date)
	if err != nil { return fmt.Errorf("date: %w", err) }
	invoiceDate, err := queryScalarText(value.InvoiceDate)
	if err != nil { return fmt.Errorf("invoice_date: %w", err) }
	timeValue, err := queryScalarText(value.Time)
	if err != nil { return fmt.Errorf("time: %w", err) }
	invoiceTime, err := queryScalarText(value.InvoiceTime)
	if err != nil { return fmt.Errorf("invoice_time: %w", err) }

	result.InvoiceNumber = value.InvoiceNumber
	result.InvoiceType = value.Type
	if result.InvoiceType == "" { result.InvoiceType = value.InvoiceType }
	result.InvoiceStatus = value.Status
	if result.InvoiceStatus == 0 { result.InvoiceStatus = value.InvoiceStatus }
	result.InvoiceDate = date
	if result.InvoiceDate == "" { result.InvoiceDate = invoiceDate }
	result.InvoiceTime = timeValue
	if result.InvoiceTime == "" { result.InvoiceTime = invoiceTime }
	result.BuyerIdentifier = value.BuyerIdentifier
	result.BuyerName = value.BuyerName
	result.SalesAmount = value.SalesAmount
	result.TaxAmount = value.TaxAmount
	result.TotalAmount = value.TotalAmount
	result.CarrierType = value.CarrierType
	result.CarrierID1 = value.CarrierID1
	result.CarrierID2 = value.CarrierID2
	result.NPOBAN = value.NPOBAN
	result.CancelDate = value.CancelDate
	result.OrderID = value.OrderID
	result.CreateDate = value.CreateDate
	result.ProductItems = value.ProductItems
	if value.DetailVAT != nil {
		result.DetailVAT = *value.DetailVAT
		result.DetailVATPresent = true
	} else if value.DetailVATLegacy != nil {
		result.DetailVAT = *value.DetailVATLegacy
		result.DetailVATPresent = true
	}
	return nil
}

func queryScalarText(raw json.RawMessage) (string, error) {
	trimmed := bytes.TrimSpace(raw)
	if len(trimmed) == 0 || bytes.Equal(trimmed, []byte("null")) { return "", nil }
	if trimmed[0] == '"' {
		var text string
		if err := json.Unmarshal(trimmed, &text); err != nil { return "", err }
		return strings.TrimSpace(text), nil
	}
	decoder := json.NewDecoder(bytes.NewReader(trimmed))
	decoder.UseNumber()
	var number json.Number
	if err := decoder.Decode(&number); err != nil {
		return "", fmt.Errorf("expected string or number")
	}
	return strings.TrimSpace(number.String()), nil
}

type QueryResponse struct {
	Code int         `json:"code"`
	Msg  string      `json:"msg"`
	Data QueryResult `json:"data"`
}

type StatusRequest struct {
	InvoiceNumber string `json:"InvoiceNumber"`
}

type StatusResult struct {
	InvoiceNumber string      `json:"invoice_number"`
	Type          string      `json:"type"`
	Status        int         `json:"status"`
	TotalAmount   json.Number `json:"total_amount"`
}

type StatusResponse struct {
	Code int
	Msg  string
	Data []StatusResult
}

type BANRequest struct {
	BAN string `json:"ban"`
}

type BANResult struct {
	BAN  string `json:"ban"`
	Name string `json:"name"`
}

type BANResponse struct {
	Code int         `json:"code"`
	Msg  string      `json:"msg"`
	Data []BANResult `json:"data"`
}
