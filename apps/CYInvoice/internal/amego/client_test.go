package amego

import (
	"context"
	"crypto/md5"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"net/url"
	"strconv"
	"strings"
	"testing"
	"time"
)

func TestIssueUsesOfficialFormAndSignature(t *testing.T) {
	fixed := time.Unix(1700000000, 0)
	var received url.Values
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		if got := request.Header.Get("Content-Type"); got != "application/x-www-form-urlencoded" {
			t.Fatalf("Content-Type = %q", got)
		}
		body, _ := io.ReadAll(request.Body)
		received, _ = url.ParseQuery(string(body))
		writer.Header().Set("Content-Type", "application/json")
		io.WriteString(writer, `{"code":0,"msg":"","invoice_number":"AB12345678","invoice_time":1700000000,"random_number":"1234"}`)
	}))
	defer server.Close()

	client := New("12345678", "secret")
	client.BaseURL = server.URL
	client.Now = func() time.Time { return fixed }
	response, err := client.Issue(context.Background(), IssueRequest{
		OrderID: "ORDER-1", BuyerIdentifier: "0000000000", BuyerName: "客人",
		ProductItems: []ProductItem{{Description: "商品", Quantity: 1, UnitPrice: 100, Amount: 100, TaxType: 1}},
		SalesAmount: 100, TaxType: 1, TaxRate: "0.05", TaxAmount: 0, TotalAmount: 100,
	})
	if err != nil { t.Fatal(err) }
	if response.InvoiceNumber != "AB12345678" { t.Fatalf("invoice = %q", response.InvoiceNumber) }
	expected := md5.Sum([]byte(received.Get("data") + "1700000000" + "secret"))
	if received.Get("sign") != hex.EncodeToString(expected[:]) { t.Fatal("signature mismatch") }
	if received.Get("invoice") != "12345678" { t.Fatalf("invoice credential = %q", received.Get("invoice")) }
}

func TestCode15SynchronizesOfficialServerTimeAndRetriesOnce(t *testing.T) {
	localTime := time.Unix(1700000000, 0)
	serverTime := localTime.Add(5 * time.Minute).Unix()
	postCalls := 0
	timeCalls := 0
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		writer.Header().Set("Content-Type", "application/json")
		if request.Method == http.MethodGet && request.URL.Path == "/json/time" {
			timeCalls++
			fmt.Fprintf(writer, `{"timestamp":%d,"text":"server time"}`, serverTime)
			return
		}
		postCalls++
		body, _ := io.ReadAll(request.Body)
		form, _ := url.ParseQuery(string(body))
		if postCalls == 1 {
			if form.Get("time") != "1700000000" { t.Fatalf("first time = %q", form.Get("time")) }
			io.WriteString(writer, `{"code":15,"msg":"time(時間戳記)錯誤"}`)
			return
		}
		if form.Get("time") != strconv.FormatInt(serverTime, 10) { t.Fatalf("retry time = %q", form.Get("time")) }
		io.WriteString(writer, `{"code":0,"msg":"","data":[{"ban":"12345678","name":"光貿測試公司"}]}`)
	}))
	defer server.Close()

	client := New("12345678", "secret")
	client.BaseURL = server.URL
	client.Now = func() time.Time { return localTime }
	if _, err := client.QueryBAN(context.Background(), []string{"12345678"}); err != nil { t.Fatal(err) }
	if postCalls != 2 || timeCalls != 1 { t.Fatalf("post=%d time=%d", postCalls, timeCalls) }
}

func TestIssueAcceptsLegacyDataArray(t *testing.T) {
	client := New("12345678", "secret")
	client.HTTPClient = roundTripFunc(func(request *http.Request) (*http.Response, error) {
		return jsonResponse(`{"code":0,"msg":"","data":[{"invoice_number":"XY87654321","barcode":"b"}]}`), nil
	})
	response, err := client.Issue(context.Background(), minimalIssue())
	if err != nil { t.Fatal(err) }
	if response.InvoiceNumber != "XY87654321" { t.Fatalf("invoice = %q", response.InvoiceNumber) }
}

func TestIssueRejectsSuccessWithoutInvoiceNumber(t *testing.T) {
	client := New("12345678", "secret")
	client.HTTPClient = roundTripFunc(func(request *http.Request) (*http.Response, error) {
		return jsonResponse(`{"code":0,"msg":""}`), nil
	})
	if _, err := client.Issue(context.Background(), minimalIssue()); err == nil {
		t.Fatal("expected missing invoice_number error")
	}
}

func TestStatusAcceptsArrayAndKeyedObject(t *testing.T) {
	responses := []string{
		`{"code":0,"msg":"","data":[{"invoice_number":"AA1","type":"C0401","status":99,"total_amount":100}]}`,
		`{"code":0,"msg":"","data":{"AA1":{"type":"C0401","status":99,"total_amount":100}}}`,
	}
	for _, body := range responses {
		client := New("12345678", "secret")
		client.HTTPClient = roundTripFunc(func(request *http.Request) (*http.Response, error) { return jsonResponse(body), nil })
		response, err := client.Status(context.Background(), []string{"AA1"})
		if err != nil { t.Fatal(err) }
		if len(response.Data) != 1 || response.Data[0].InvoiceNumber != "AA1" || response.Data[0].Status != 99 {
			t.Fatalf("unexpected response: %#v", response)
		}
	}
}

func TestQueryByOrderID(t *testing.T) {
	client := New("12345678", "secret")
	client.HTTPClient = roundTripFunc(func(request *http.Request) (*http.Response, error) {
		body, _ := io.ReadAll(request.Body)
		form, _ := url.ParseQuery(string(body))
		var payload QueryRequest
		if err := json.Unmarshal([]byte(form.Get("data")), &payload); err != nil { t.Fatal(err) }
		if payload.Type != "order" || payload.OrderID != "ORDER-1" { t.Fatalf("payload = %#v", payload) }
		return jsonResponse(`{"code":0,"msg":"","data":{"invoice_number":"AA12345678","order_id":"ORDER-1","type":"C0401","status":31,"date":"2026-09-05","time":"11:22:33"}}`), nil
	})
	response, err := client.QueryByOrderID(context.Background(), "ORDER-1")
	if err != nil { t.Fatal(err) }
	if response.Data.InvoiceNumber != "AA12345678" || response.Data.InvoiceStatus != 31 || response.Data.InvoiceType != "C0401" || response.Data.InvoiceDate != "2026-09-05" || response.Data.InvoiceTime != "11:22:33" {
		t.Fatalf("response = %#v", response)
	}
}

func TestQueryAcceptsLegacyArrayAndFieldAliases(t *testing.T) {
	client := New("12345678", "secret")
	client.HTTPClient = roundTripFunc(func(request *http.Request) (*http.Response, error) {
		return jsonResponse(`{"code":0,"msg":"","data":[{"invoice_number":"ZZ87654321","order_id":"ORDER-2","invoice_type":"C0401","invoice_status":99,"invoice_date":"2026-09-04","invoice_time":"08:09:10"}]}`), nil
	})
	response, err := client.QueryByInvoiceNumber(context.Background(), "ZZ87654321")
	if err != nil { t.Fatal(err) }
	if response.Data.InvoiceNumber != "ZZ87654321" || response.Data.OrderID != "ORDER-2" || response.Data.InvoiceStatus != 99 || response.Data.InvoiceDate != "2026-09-04" {
		t.Fatalf("response = %#v", response)
	}
}

func TestQueryAcceptsNestedArrayReturnedByExistingDeployment(t *testing.T) {
	client := New("12345678", "secret")
	client.HTTPClient = roundTripFunc(func(request *http.Request) (*http.Response, error) {
		return jsonResponse(`{"code":0,"msg":"","data":[[{"invoice_number":"XY12345678","order_id":"ORDER-3","type":"C0401","status":99,"date":"2026-09-07","time":"10:11:12"}]]}`), nil
	})
	response, err := client.QueryByOrderID(context.Background(), "ORDER-3")
	if err != nil { t.Fatal(err) }
	if response.Data.InvoiceNumber != "XY12345678" || response.Data.OrderID != "ORDER-3" || response.Data.InvoiceStatus != 99 {
		t.Fatalf("response = %#v", response)
	}
}

func TestQueryRejectsSuccessWithoutData(t *testing.T) {
	client := New("12345678", "secret")
	client.HTTPClient = roundTripFunc(func(request *http.Request) (*http.Response, error) {
		return jsonResponse(`{"code":0,"msg":"","data":null}`), nil
	})
	if _, err := client.QueryByOrderID(context.Background(), "ORDER-1"); err == nil {
		t.Fatal("expected missing query data error")
	}
}

func TestQuerySelectsOnlyExactRequestedOrderFromArray(t *testing.T) {
	client := New("12345678", "secret")
	client.HTTPClient = roundTripFunc(func(request *http.Request) (*http.Response, error) {
		return jsonResponse(`{"code":0,"msg":"","data":[{"invoice_number":"AA12345678","order_id":"OTHER"},{"invoice_number":"BB12345678","order_id":"ORDER-1","sales_amount":100,"tax_amount":5,"total_amount":105,"detail_vat":1}]}`), nil
	})
	response, err := client.QueryByOrderID(context.Background(), "ORDER-1")
	if err != nil { t.Fatal(err) }
	if response.Data.InvoiceNumber != "BB12345678" || !response.Data.DetailVATPresent || response.Data.DetailVAT != 1 {
		t.Fatalf("response=%#v", response)
	}
}

func TestQueryRejectsMultipleExactMatches(t *testing.T) {
	client := New("12345678", "secret")
	client.HTTPClient = roundTripFunc(func(request *http.Request) (*http.Response, error) {
		return jsonResponse(`{"code":0,"msg":"","data":[{"invoice_number":"AA12345678","order_id":"ORDER-1"},{"invoice_number":"BB12345678","order_id":"ORDER-1"}]}`), nil
	})
	if _, err := client.QueryByOrderID(context.Background(), "ORDER-1"); err == nil {
		t.Fatal("accepted multiple exact order matches")
	}
}

func minimalIssue() IssueRequest {
	return IssueRequest{
		OrderID: "ORDER-1", BuyerIdentifier: "0000000000", BuyerName: "客人",
		ProductItems: []ProductItem{{Description: "商品", Quantity: 1, UnitPrice: 100, Amount: 100, TaxType: 1}},
		SalesAmount: 100, TaxType: 1, TaxRate: "0.05", TaxAmount: 0, TotalAmount: 100,
	}
}

type roundTripFunc func(*http.Request) (*http.Response, error)
func (function roundTripFunc) Do(request *http.Request) (*http.Response, error) { return function(request) }
func jsonResponse(body string) *http.Response {
	return &http.Response{StatusCode: http.StatusOK, Header: http.Header{"Content-Type": []string{"application/json"}}, Body: io.NopCloser(strings.NewReader(body))}
}
