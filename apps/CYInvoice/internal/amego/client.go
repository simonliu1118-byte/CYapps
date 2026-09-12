package amego

import (
	"bytes"
	"context"
	"crypto/md5"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strconv"
	"strings"
	"sync"
	"time"
)

type HTTPDoer interface {
	Do(*http.Request) (*http.Response, error)
}

type Client struct {
	BaseURL    string
	Invoice    string
	AppKey     string
	HTTPClient HTTPDoer
	Now        func() time.Time
	clockMu    sync.RWMutex
	clockOffsetSeconds int64
}

func New(invoice, appKey string) *Client {
	return &Client{
		BaseURL: DefaultBaseURL,
		Invoice: strings.TrimSpace(invoice),
		AppKey: strings.TrimSpace(appKey),
		HTTPClient: &http.Client{Timeout: 20 * time.Second},
		Now: time.Now,
	}
}

func (client *Client) Issue(ctx context.Context, request IssueRequest) (IssueResponse, error) {
	if strings.TrimSpace(request.OrderID) == "" {
		return IssueResponse{}, errors.New("OrderId is required")
	}
	if len(request.ProductItems) == 0 {
		return IssueResponse{}, errors.New("ProductItem is required")
	}
	var raw json.RawMessage
	if err := client.post(ctx, "/json/f0401", request, &raw); err != nil {
		return IssueResponse{}, err
	}
	return decodeIssueResponse(raw)
}

func (client *Client) QueryByOrderID(ctx context.Context, orderID string) (QueryResponse, error) {
	orderID = strings.TrimSpace(orderID)
	if orderID == "" {
		return QueryResponse{}, errors.New("order_id is required")
	}
	return client.query(ctx, QueryRequest{Type: "order", OrderID: orderID})
}

func (client *Client) QueryByInvoiceNumber(ctx context.Context, number string) (QueryResponse, error) {
	number = strings.TrimSpace(number)
	if number == "" {
		return QueryResponse{}, errors.New("invoice_number is required")
	}
	return client.query(ctx, QueryRequest{Type: "invoice", InvoiceNumber: number})
}

func (client *Client) query(ctx context.Context, request QueryRequest) (QueryResponse, error) {
	var raw json.RawMessage
	if err := client.post(ctx, "/json/invoice_query", request, &raw); err != nil {
		return QueryResponse{}, err
	}
	return decodeQueryResponseForRequest(raw, request)
}

func (client *Client) Status(ctx context.Context, numbers []string) (StatusResponse, error) {
	request := make([]StatusRequest, 0, len(numbers))
	for _, number := range numbers {
		if number = strings.TrimSpace(number); number != "" {
			request = append(request, StatusRequest{InvoiceNumber: number})
		}
	}
	if len(request) == 0 {
		return StatusResponse{}, errors.New("at least one invoice number is required")
	}
	var raw json.RawMessage
	if err := client.post(ctx, "/json/invoice_status", request, &raw); err != nil {
		return StatusResponse{}, err
	}
	return decodeStatusResponse(raw)
}

func (client *Client) QueryBAN(ctx context.Context, bans []string) (BANResponse, error) {
	request := make([]BANRequest, 0, len(bans))
	for _, ban := range bans {
		if ban = strings.TrimSpace(ban); ban != "" {
			request = append(request, BANRequest{BAN: ban})
		}
	}
	if len(request) == 0 {
		return BANResponse{}, errors.New("at least one BAN is required")
	}
	var response BANResponse
	if err := client.post(ctx, "/json/ban_query", request, &response); err != nil {
		return BANResponse{}, err
	}
	return response, responseError(response.Code, response.Msg)
}

func (client *Client) post(ctx context.Context, path string, payload any, destination any) error {
	if strings.TrimSpace(client.Invoice) == "" || strings.TrimSpace(client.AppKey) == "" {
		return errors.New("invoice and App Key are required")
	}
	data, err := json.Marshal(payload)
	if err != nil {
		return fmt.Errorf("encode API data: %w", err)
	}
	for attempt := 0; attempt < 2; attempt++ {
		body, err := client.postSigned(ctx, path, data)
		if err != nil {
			return err
		}
		var envelope struct { Code int `json:"code"` }
		if json.Unmarshal(body, &envelope) == nil && envelope.Code == 15 && attempt == 0 {
			if err := client.syncServerTime(ctx); err != nil {
				return fmt.Errorf("校正光貿伺服器時間：%w", err)
			}
			continue
		}
		decoder := json.NewDecoder(bytes.NewReader(body))
		decoder.UseNumber()
		if err := decoder.Decode(destination); err != nil {
			return fmt.Errorf("decode API response: %w", err)
		}
		return nil
	}
	return errors.New("光貿 API 時間校正後仍無法完成請求")
}

func (client *Client) postSigned(ctx context.Context, path string, data []byte) ([]byte, error) {
	now := time.Now
	if client.Now != nil { now = client.Now }
	client.clockMu.RLock()
	offset := client.clockOffsetSeconds
	client.clockMu.RUnlock()
	timestamp := strconv.FormatInt(now().Unix()+offset, 10)
	signature := md5.Sum([]byte(string(data) + timestamp + client.AppKey))
	form := url.Values{
		"invoice": {client.Invoice},
		"data":    {string(data)},
		"time":    {timestamp},
		"sign":    {hex.EncodeToString(signature[:])},
	}
	baseURL := strings.TrimRight(client.BaseURL, "/")
	if baseURL == "" {
		baseURL = DefaultBaseURL
	}
	httpClient := client.HTTPClient
	if httpClient == nil {
		httpClient = &http.Client{Timeout: 20 * time.Second}
	}
	request, err := http.NewRequestWithContext(ctx, http.MethodPost, baseURL+path, strings.NewReader(form.Encode()))
	if err != nil {
		return nil, fmt.Errorf("create API request: %w", err)
	}
	request.Header.Set("Content-Type", "application/x-www-form-urlencoded")
	request.Header.Set("Accept", "application/json")
	response, err := httpClient.Do(request)
	if err != nil {
		return nil, fmt.Errorf("call API: %w", err)
	}
	defer response.Body.Close()
	body, err := io.ReadAll(io.LimitReader(response.Body, 8<<20))
	if err != nil {
		return nil, fmt.Errorf("read API response: %w", err)
	}
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return nil, fmt.Errorf("API HTTP %d: %s", response.StatusCode, strings.TrimSpace(string(body)))
	}
	return body, nil
}

// syncServerTime uses AMEGO's unauthenticated official clock endpoint. Signed
// requests accept only a narrow timestamp window, so a code 15 response is
// retried once with the measured server offset instead of asking Windows users
// to change their system clock.
func (client *Client) syncServerTime(ctx context.Context) error {
	baseURL := strings.TrimRight(client.BaseURL, "/")
	if baseURL == "" { baseURL = DefaultBaseURL }
	httpClient := client.HTTPClient
	if httpClient == nil { httpClient = &http.Client{Timeout: 20 * time.Second} }
	request, err := http.NewRequestWithContext(ctx, http.MethodGet, baseURL+"/json/time", nil)
	if err != nil { return fmt.Errorf("create server time request: %w", err) }
	request.Header.Set("Accept", "application/json")
	response, err := httpClient.Do(request)
	if err != nil { return fmt.Errorf("call server time API: %w", err) }
	defer response.Body.Close()
	body, err := io.ReadAll(io.LimitReader(response.Body, 1<<20))
	if err != nil { return fmt.Errorf("read server time response: %w", err) }
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return fmt.Errorf("server time HTTP %d", response.StatusCode)
	}
	var envelope struct { Timestamp json.RawMessage `json:"timestamp"` }
	if err := json.Unmarshal(body, &envelope); err != nil { return fmt.Errorf("decode server time response: %w", err) }
	serverUnix, err := parseServerTimestamp(envelope.Timestamp)
	if err != nil { return err }
	now := time.Now
	if client.Now != nil { now = client.Now }
	client.clockMu.Lock()
	client.clockOffsetSeconds = serverUnix - now().Unix()
	client.clockMu.Unlock()
	return nil
}

func parseServerTimestamp(raw json.RawMessage) (int64, error) {
	if len(raw) == 0 || string(raw) == "null" { return 0, errors.New("server time response missing timestamp") }
	var number json.Number
	decoder := json.NewDecoder(bytes.NewReader(raw))
	decoder.UseNumber()
	if err := decoder.Decode(&number); err == nil {
		if value, parseErr := number.Int64(); parseErr == nil && value > 0 { return value, nil }
	}
	var text string
	if err := json.Unmarshal(raw, &text); err == nil {
		if value, parseErr := strconv.ParseInt(strings.TrimSpace(text), 10, 64); parseErr == nil && value > 0 { return value, nil }
	}
	return 0, errors.New("server time response contains invalid timestamp")
}

func responseError(code int, message string) error {
	if code == 0 {
		return nil
	}
	if strings.TrimSpace(message) == "" {
		message = "unknown API error"
	}
	return &APIError{Code: code, Message: message}
}

// APIError preserves AMEGO's numeric response code so callers can distinguish
// an explicit rejection from an ambiguous network or decoding failure.
type APIError struct {
	Code    int
	Message string
}

func (err *APIError) Error() string {
	return fmt.Sprintf("API code %d: %s", err.Code, err.Message)
}

func IsAPIErrorCode(err error, code int) bool {
	var apiErr *APIError
	return errors.As(err, &apiErr) && apiErr.Code == code
}

func decodeIssueResponse(raw json.RawMessage) (IssueResponse, error) {
	var envelope struct {
		Code int             `json:"code"`
		Msg  string          `json:"msg"`
		Data json.RawMessage `json:"data"`
		IssueResult
	}
	decoder := json.NewDecoder(bytes.NewReader(raw))
	decoder.UseNumber()
	if err := decoder.Decode(&envelope); err != nil {
		return IssueResponse{}, fmt.Errorf("decode issue response: %w", err)
	}
	response := IssueResponse{Code: envelope.Code, Msg: envelope.Msg, IssueResult: envelope.IssueResult}
	if response.Code != 0 {
		return response, responseError(response.Code, response.Msg)
	}
	if response.InvoiceNumber == "" && len(envelope.Data) > 0 && string(envelope.Data) != "null" {
		var list []IssueResult
		if err := json.Unmarshal(envelope.Data, &list); err == nil && len(list) > 0 {
			response.IssueResult = list[0]
		} else {
			var single IssueResult
			if err := json.Unmarshal(envelope.Data, &single); err != nil {
				return response, fmt.Errorf("decode issue data: %w", err)
			}
			response.IssueResult = single
		}
	}
	if strings.TrimSpace(response.InvoiceNumber) == "" {
		return response, errors.New("API returned success without invoice_number")
	}
	return response, nil
}

func decodeStatusResponse(raw json.RawMessage) (StatusResponse, error) {
	var envelope struct {
		Code int             `json:"code"`
		Msg  string          `json:"msg"`
		Data json.RawMessage `json:"data"`
	}
	if err := json.Unmarshal(raw, &envelope); err != nil {
		return StatusResponse{}, fmt.Errorf("decode status response: %w", err)
	}
	response := StatusResponse{Code: envelope.Code, Msg: envelope.Msg}
	if response.Code != 0 {
		return response, responseError(response.Code, response.Msg)
	}
	if len(envelope.Data) == 0 || string(envelope.Data) == "null" {
		return response, nil
	}
	if err := json.Unmarshal(envelope.Data, &response.Data); err == nil {
		return response, nil
	}
	var keyed map[string]StatusResult
	if err := json.Unmarshal(envelope.Data, &keyed); err != nil {
		return response, fmt.Errorf("decode status data: %w", err)
	}
	for number, item := range keyed {
		if item.InvoiceNumber == "" {
			item.InvoiceNumber = number
		}
		response.Data = append(response.Data, item)
	}
	return response, nil
}

func decodeQueryResponse(raw json.RawMessage) (QueryResponse, error) {
	return decodeQueryResponseForRequest(raw, QueryRequest{})
}

func decodeQueryResponseForRequest(raw json.RawMessage, request QueryRequest) (QueryResponse, error) {
	var envelope struct {
		Code int             `json:"code"`
		Msg  string          `json:"msg"`
		Data json.RawMessage `json:"data"`
	}
	decoder := json.NewDecoder(bytes.NewReader(raw))
	decoder.UseNumber()
	if err := decoder.Decode(&envelope); err != nil {
		return QueryResponse{}, fmt.Errorf("decode query response: %w", err)
	}
	response := QueryResponse{Code: envelope.Code, Msg: envelope.Msg}
	if response.Code != 0 {
		return response, responseError(response.Code, response.Msg)
	}
	if len(envelope.Data) == 0 || string(envelope.Data) == "null" {
		return response, errors.New("API returned success without query data")
	}
	results, err := decodeQueryDataAll(envelope.Data)
	if err != nil { return response, fmt.Errorf("decode query data: %w", err) }
	if len(results) == 0 { return response, errors.New("API returned success without usable query data") }
	matched := make([]QueryResult, 0, 1)
	for _, result := range results {
		switch request.Type {
		case "order":
			if strings.TrimSpace(result.OrderID) == strings.TrimSpace(request.OrderID) { matched = append(matched, result) }
		case "invoice":
			if strings.TrimSpace(result.InvoiceNumber) == strings.TrimSpace(request.InvoiceNumber) { matched = append(matched, result) }
		default:
			matched = append(matched, result)
		}
	}
	if len(matched) == 0 { return response, errors.New("API query data does not contain the requested invoice") }
	if len(matched) > 1 { return response, errors.New("API query data contains more than one matching invoice") }
	response.Data = matched[0]
	return response, nil
}

// decodeQueryData accepts the documented object response and the array or
// nested-array shapes returned by existing AMEGO invoice-query deployments.
func decodeQueryData(raw json.RawMessage) (QueryResult, error) {
	results, err := decodeQueryDataAll(raw)
	if err != nil { return QueryResult{}, err }
	if len(results) == 0 { return QueryResult{}, errors.New("API returned success without usable query data") }
	return results[0], nil
}

func decodeQueryDataAll(raw json.RawMessage) ([]QueryResult, error) {
	trimmed := bytes.TrimSpace(raw)
	if len(trimmed) == 0 || bytes.Equal(trimmed, []byte("null")) {
		return nil, errors.New("API returned success without query data")
	}
	switch trimmed[0] {
	case '{':
		var result QueryResult
		if err := json.Unmarshal(trimmed, &result); err != nil { return nil, err }
		if strings.TrimSpace(result.InvoiceNumber) == "" && strings.TrimSpace(result.OrderID) == "" {
			return nil, errors.New("API query object is missing invoice_number and order_id")
		}
		return []QueryResult{result}, nil
	case '[':
		var entries []json.RawMessage
		if err := json.Unmarshal(trimmed, &entries); err != nil { return nil, err }
		if len(entries) == 0 { return nil, errors.New("API returned success with empty query data") }
		var lastErr error
		results := make([]QueryResult, 0, len(entries))
		for _, entry := range entries {
			decoded, err := decodeQueryDataAll(entry)
			if err != nil { lastErr = err; continue }
			results = append(results, decoded...)
		}
		if len(results) > 0 { return results, nil }
		return nil, lastErr
	default:
		return nil, fmt.Errorf("unsupported query data shape %q", string(trimmed[0]))
	}
}
