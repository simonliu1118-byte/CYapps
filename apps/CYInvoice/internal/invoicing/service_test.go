package invoicing

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"strconv"
	"strings"
	"testing"
	"time"

	"cyinvoice/internal/amego"
	"cyinvoice/internal/appdata"
	"cyinvoice/internal/coupangimport"
	"cyinvoice/internal/moimport"
)

type fakeProtector struct{}
func (fakeProtector) Protect(value []byte) (string, error) { return base64.StdEncoding.EncodeToString(value), nil }
func (fakeProtector) Unprotect(value string) ([]byte, error) { return base64.StdEncoding.DecodeString(value) }

type fakeGateway struct {
	issueResponse amego.IssueResponse
	issueError error
	queryResponse amego.QueryResponse
	queryError error
	banResponse amego.BANResponse
	banError error
	statusResponse amego.StatusResponse
	statusError error
	issueCalls int
	banCalls int
	lastIssue amego.IssueRequest
	lastBANs []string
	lastQueryOrderID string
}
func (fake *fakeGateway) Issue(_ context.Context, request amego.IssueRequest) (amego.IssueResponse, error) { fake.issueCalls++; fake.lastIssue = request; return fake.issueResponse, fake.issueError }
func (fake *fakeGateway) QueryByOrderID(_ context.Context, orderID string) (amego.QueryResponse, error) { fake.lastQueryOrderID = orderID; return fake.queryResponse, fake.queryError }
func (fake *fakeGateway) QueryByInvoiceNumber(context.Context, string) (amego.QueryResponse, error) { return fake.queryResponse, fake.queryError }
func (fake *fakeGateway) Status(context.Context, []string) (amego.StatusResponse, error) { return fake.statusResponse, fake.statusError }
func (fake *fakeGateway) QueryBAN(_ context.Context, bans []string) (amego.BANResponse, error) { fake.banCalls++; fake.lastBANs = append([]string(nil), bans...); return fake.banResponse, fake.banError }

func newTestService(t *testing.T, gateway *fakeGateway) *Service {
	t.Helper()
	repository, err := appdata.Open(t.TempDir(), fakeProtector{})
	if err != nil { t.Fatal(err) }
	return &Service{Repository: repository, NewGateway: func(string, string) Gateway { return gateway }, Now: func() time.Time { return time.Date(2026, 9, 5, 9, 8, 7, 0, time.FixedZone("Taipei", 8*60*60)) }}
}

func validManualDraft() appdata.InvoiceDraft {
	return appdata.InvoiceDraft{OrderID: "20260905001", Items: []appdata.InvoiceItem{{Description: "商品", Quantity: 1, UnitPrice: 105, Amount: 105}}, TotalAmount: 105}
}

func confirmedQuery(orderID, invoiceNumber string, sales, tax, total int64, detailVAT int) amego.QueryResponse {
	return amego.QueryResponse{Data: amego.QueryResult{
		OrderID: orderID, InvoiceNumber: invoiceNumber,
		SalesAmount: json.Number(strconv.FormatInt(sales, 10)),
		TaxAmount: json.Number(strconv.FormatInt(tax, 10)),
		TotalAmount: json.Number(strconv.FormatInt(total, 10)),
		DetailVAT: detailVAT, DetailVATPresent: true,
	}}
}

func TestIssueManualWritesPendingThenOpensAndDoesNotResend(t *testing.T) {
	fake := &fakeGateway{
		issueResponse: amego.IssueResponse{IssueResult: amego.IssueResult{InvoiceNumber: "AB12345678", InvoiceTime: 1788566887}},
		queryResponse: confirmedQuery("20260905001", "AB12345678", 105, 0, 105, 1),
	}
	service := newTestService(t, fake)
	result, err := service.IssueManual(context.Background(), validManualDraft())
	if err != nil { t.Fatal(err) }
	if !result.Opened || result.Record.InvoiceNumber != "AB12345678" { t.Fatalf("result = %#v", result) }
	if fake.issueCalls != 1 { t.Fatalf("issue calls = %d", fake.issueCalls) }
	if fake.lastIssue.BuyerIdentifier != "0000000000" || fake.lastIssue.SalesAmount != int64(105) || fake.lastIssue.TaxAmount != int64(0) || fake.lastIssue.DetailVAT != 1 { t.Fatalf("request = %#v", fake.lastIssue) }
	if fake.lastIssue.BuyerName != "消費者" { t.Fatalf("buyer name = %q", fake.lastIssue.BuyerName) }
	if _, err = service.IssueManual(context.Background(), validManualDraft()); err == nil || !strings.Contains(err.Error(), "避免重複") { t.Fatalf("duplicate error = %v", err) }
	if fake.issueCalls != 1 { t.Fatalf("duplicate caused resend: %d", fake.issueCalls) }
}

func TestIssueManualAmbiguousFailureBecomesUnknown(t *testing.T) {
	fake := &fakeGateway{issueError: errors.New("connection reset"), queryError: &amego.APIError{Code: 71, Message: "not found"}}
	service := newTestService(t, fake)
	result, err := service.IssueManual(context.Background(), validManualDraft())
	if err == nil || !result.Unknown || result.Record.InvoiceState != appdata.InvoiceStateUnknown { t.Fatalf("result=%#v err=%v", result, err) }
	records, _ := service.Repository.Invoices.LoadOrCreate()
	if len(records) != 1 || records[0].InvoiceState != appdata.InvoiceStateUnknown { t.Fatalf("records = %#v", records) }
}

func TestIssueManualExplicitRejectionBecomesFailed(t *testing.T) {
	fake := &fakeGateway{issueError: &amego.APIError{Code: 101, Message: "invalid"}, queryError: &amego.APIError{Code: 71, Message: "not found"}}
	service := newTestService(t, fake)
	result, err := service.IssueManual(context.Background(), validManualDraft())
	if err == nil || result.Record.InvoiceState != appdata.InvoiceStateFailed { t.Fatalf("result=%#v err=%v", result, err) }
}

func TestIssueManualRecoversFromAmbiguousResponseByOrderQuery(t *testing.T) {
	fake := &fakeGateway{issueError: errors.New("decode failed"), queryResponse: confirmedQuery("20260905001", "CD87654321", 105, 0, 105, 1)}
	service := newTestService(t, fake)
	result, err := service.IssueManual(context.Background(), validManualDraft())
	if err != nil || !result.Opened || result.Record.InvoiceNumber != "CD87654321" { t.Fatalf("result=%#v err=%v", result, err) }
	if fake.issueCalls != 1 { t.Fatalf("issue calls = %d", fake.issueCalls) }
}

func TestCompanyTaxAndBuyerNameMemory(t *testing.T) {
	fake := &fakeGateway{
		banResponse: amego.BANResponse{Data: []amego.BANResult{{BAN: "12345675", Name: ""}}},
		issueResponse: amego.IssueResponse{IssueResult: amego.IssueResult{InvoiceNumber: "EF12345678"}},
		queryResponse: amego.QueryResponse{Data: amego.QueryResult{InvoiceNumber: "EF12345678"}},
	}
	service := newTestService(t, fake)
	draft := validManualDraft(); draft.CompanyBuyer = true; draft.BuyerIdentifier = "12345675"; draft.BuyerName = "人工公司"
	result, err := service.IssueManual(context.Background(), draft)
	if err != nil || !result.Opened { t.Fatalf("result=%#v err=%v", result, err) }
	if fake.lastIssue.SalesAmount != int64(100) || fake.lastIssue.TaxAmount != int64(5) { t.Fatalf("company totals = %#v", fake.lastIssue) }
	name, found, err := service.Repository.BuyerNames.Lookup("12345675")
	if err != nil || !found || name != "人工公司" { t.Fatalf("memory=%q,%v err=%v", name, found, err) }
}

func TestUntaxedCompanyInvoiceUsesOfficialDetailVATFormula(t *testing.T) {
	fake := &fakeGateway{
		banResponse: amego.BANResponse{Data: []amego.BANResult{{BAN: "12345675", Name: "測試公司"}}},
		issueResponse: amego.IssueResponse{IssueResult: amego.IssueResult{InvoiceNumber: "UV12345678"}},
		queryResponse: amego.QueryResponse{Data: amego.QueryResult{InvoiceNumber: "UV12345678"}},
	}
	service := newTestService(t, fake)
	draft := validManualDraft()
	draft.CompanyBuyer = true
	draft.BuyerIdentifier = "12345675"
	draft.BuyerName = "測試公司"
	draft.PricesExcludeTax = true
	draft.Items[0].UnitPrice = 100
	draft.Items[0].Amount = 100
	draft.TotalAmount = 105
	result, err := service.IssueManual(context.Background(), draft)
	if err != nil || !result.Opened { t.Fatalf("result=%#v err=%v", result, err) }
	if fake.lastIssue.DetailVAT != 0 || fake.lastIssue.SalesAmount != int64(100) || fake.lastIssue.TaxAmount != int64(5) || fake.lastIssue.TotalAmount != int64(105) {
		t.Fatalf("untaxed request = %#v", fake.lastIssue)
	}
}

func TestUntaxedDecimalRequestPreservesSevenPlacesAndRoundsTotal(t *testing.T) {
	fake := &fakeGateway{
		banResponse: amego.BANResponse{Data: []amego.BANResult{{BAN: "12345675", Name: "測試公司"}}},
		issueResponse: amego.IssueResponse{IssueResult: amego.IssueResult{InvoiceNumber: "WX12345678"}},
		queryResponse: amego.QueryResponse{Data: amego.QueryResult{InvoiceNumber: "WX12345678"}},
	}
	service := newTestService(t, fake)
	draft := appdata.InvoiceDraft{
		OrderID: "20260905009", CompanyBuyer: true,
		BuyerIdentifier: "12345675", BuyerName: "測試公司", PricesExcludeTax: true,
		Items: []appdata.InvoiceItem{{Description: "商品", Quantity: 1, QuantityDecimal: "1", UnitPrice: 190, UnitPriceDecimal: "190.4761905", Amount: 190, AmountDecimal: "190.4761905"}},
		TotalAmount: 200,
	}
	if _, err := service.IssueManual(context.Background(), draft); err != nil { t.Fatal(err) }
	item := fake.lastIssue.ProductItems[0]
	if item.UnitPrice != json.Number("190.4761905") || item.Amount != json.Number("190.4761905") {
		t.Fatalf("decimal item = %#v", item)
	}
	if fake.lastIssue.SalesAmount != int64(190) || fake.lastIssue.TaxAmount != int64(10) || fake.lastIssue.TotalAmount != int64(200) || fake.lastIssue.DetailVAT != 0 {
		t.Fatalf("request totals = %#v", fake.lastIssue)
	}
}

func TestHealthCheckUsesReadOnlyBANQuery(t *testing.T) {
	fake := &fakeGateway{}
	service := newTestService(t, fake)
	if err := service.HealthCheck(context.Background()); err != nil { t.Fatal(err) }
	if len(fake.lastBANs) != 1 || fake.lastBANs[0] != amego.TestInvoice {
		t.Fatalf("health check BANs = %#v", fake.lastBANs)
	}
	if fake.issueCalls != 0 { t.Fatalf("health check issued %d invoice(s)", fake.issueCalls) }
}

func TestHealthCheckTreatsBANValidationReplyAsReachable(t *testing.T) {
	fake := &fakeGateway{banError: &amego.APIError{Code: 99, Message: "第1筆 統一編號格式錯誤"}}
	service := newTestService(t, fake)
	if err := service.HealthCheck(context.Background()); err != nil { t.Fatalf("health check = %v", err) }
}

func TestHealthCheckKeepsAuthenticationFailureAbnormal(t *testing.T) {
	fake := &fakeGateway{banError: &amego.APIError{Code: 16, Message: "sign error"}}
	service := newTestService(t, fake)
	if err := service.HealthCheck(context.Background()); err == nil { t.Fatal("authentication failure reported healthy") }
}

func TestBuyerLookupTreatsValidationReplyAsReachableNoMatch(t *testing.T) {
	fake := &fakeGateway{banError: &amego.APIError{Code: 99, Message: "第1筆 統一編號格式錯誤"}}
	service := newTestService(t, fake)
	lookup, err := service.LookupBuyerName(context.Background(), "13871381")
	if err != nil { t.Fatalf("lookup = %v", err) }
	if !lookup.LookupSucceeded || lookup.Name != "" || lookup.Local {
		t.Fatalf("lookup = %#v", lookup)
	}
}

func TestBuyerLookupRejectsNonNumericEightCharacterValue(t *testing.T) {
	fake := &fakeGateway{}
	service := newTestService(t, fake)
	if _, err := service.LookupBuyerName(context.Background(), "1234567A"); err == nil {
		t.Fatal("accepted a non-numeric company identifier")
	}
	if len(fake.lastBANs) != 0 { t.Fatalf("invalid identifier reached API: %#v", fake.lastBANs) }
}

func TestNegativeDiscountLineAllowedWithPositiveInvoiceTotal(t *testing.T) {
	draft := validManualDraft()
	draft.Items = []appdata.InvoiceItem{{Description: "商品", Quantity: 1, UnitPrice: 150, Amount: 150}, {Description: "折扣", Quantity: 1, UnitPrice: -45, Amount: -45}}
	if err := draft.Validate(); err != nil { t.Fatal(err) }
}

func TestIssueMOMapsMemberCarrierInTestEnvironment(t *testing.T) {
	fake := &fakeGateway{issueResponse: amego.IssueResponse{IssueResult: amego.IssueResult{InvoiceNumber: "MO12345678"}}, queryResponse: amego.QueryResponse{Data: amego.QueryResult{InvoiceNumber: "MO12345678"}}}
	service := newTestService(t, fake)
	order := moimport.Order{
		OrderID: "66090300839412", InvoiceType: "B2C", Carrier: moimport.CarrierMember,
		CarrierID1: "motmp_66090300839412", CarrierID2: "motmp_66090300839412",
		BuyerName: "陳*菁", BuyerAddress: "真實地址", BuyerPhone: "0912345678", BuyerEmail: "buyer@example.com",
		MainRemark: "真實主備註", Items: []appdata.InvoiceItem{{Description: "隱私商品名稱", Quantity: 1, UnitPrice: 653, Amount: 653, TaxType: "1", Remark: "真實明細備註"}}, TotalAmount: 653,
	}
	result, err := service.IssueMO(context.Background(), order)
	if err != nil || !result.Opened { t.Fatalf("result=%#v err=%v", result, err) }
	if fake.lastIssue.CarrierType != "amego" || fake.lastIssue.CarrierID1 != testCarrierID || fake.lastIssue.CarrierID2 != testCarrierID { t.Fatalf("request=%#v", fake.lastIssue) }
	if fake.lastIssue.BuyerName != "測試消費者" || fake.lastIssue.OrderID != order.OrderID || result.Record.APIOrderID != fake.lastIssue.OrderID {
		t.Fatalf("test privacy request=%#v record=%#v", fake.lastIssue, result.Record)
	}
	if fake.lastIssue.BuyerIdentifier != "0000000000" || fake.lastIssue.BuyerAddress != "" ||
		fake.lastIssue.BuyerTelephoneNumber != "" || fake.lastIssue.BuyerEmailAddress != "" ||
		fake.lastIssue.MainRemark != "" || fake.lastIssue.NPOBAN != "" ||
		len(fake.lastIssue.ProductItems) != 1 || fake.lastIssue.ProductItems[0].Description != "測試商品 1" || fake.lastIssue.ProductItems[0].Remark != "" {
		t.Fatalf("private data reached shared test pool: %#v", fake.lastIssue)
	}
	if result.Record.BuyerName != "陳*菁" || result.Record.CarrierType != "amego" ||
		result.Record.CarrierID1 != order.CarrierID1 || result.Record.APIOrderID != fake.lastIssue.OrderID ||
		result.Record.Items[0].Description != "隱私商品名稱" || result.Record.MainRemark != "真實主備註" {
		t.Fatalf("persisted record=%#v", result.Record)
	}
}

func TestIssueMOCompanyIsPaperAndTestPayloadMasksCustomerData(t *testing.T) {
	fake := &fakeGateway{
		issueResponse: amego.IssueResponse{IssueResult: amego.IssueResult{InvoiceNumber: "MO12345679"}},
		queryResponse: amego.QueryResponse{Data: amego.QueryResult{InvoiceNumber: "MO12345679"}},
	}
	service := newTestService(t, fake)
	order := moimport.Order{
		OrderID: "66090700928934", BuyerBAN: "12345675", BuyerName: "真實買方名稱",
		Carrier: moimport.CarrierMember, CarrierID1: "不應上傳", CarrierID2: "不應上傳",
		Items: []appdata.InvoiceItem{{Description: "商品", Quantity: 1, UnitPrice: 105, Amount: 105, TaxType: "1"}},
		TotalAmount: 105,
	}
	result, err := service.IssueMOWithLookup(context.Background(), order, NameLookup{LookupSucceeded: true})
	if err != nil || !result.Opened { t.Fatalf("result=%#v err=%v", result, err) }
	if fake.lastIssue.BuyerName != "測試消費者" || fake.lastIssue.OrderID != order.OrderID ||
		fake.lastIssue.BuyerIdentifier != testBuyerIdentifier || fake.lastIssue.CarrierType != "" ||
		fake.lastIssue.CarrierID1 != "" || fake.lastIssue.CarrierID2 != "" ||
		fake.lastIssue.ProductItems[0].Description != "測試商品 1" {
		t.Fatalf("company test request=%#v", fake.lastIssue)
	}
	if result.Record.BuyerName != "真實買方名稱" || result.Record.Delivery != DeliveryPaper {
		t.Fatalf("local company record=%#v", result.Record)
	}
}

func TestIssueCoupangWithLookupUsesSharedConfirmationDecisionAndTestPrivacy(t *testing.T) {
	fake := &fakeGateway{
		issueResponse: amego.IssueResponse{IssueResult: amego.IssueResult{InvoiceNumber: "CP12345678"}},
		queryResponse: amego.QueryResponse{Data: amego.QueryResult{InvoiceNumber: "CP12345678"}},
	}
	service := newTestService(t, fake)
	order := coupangimport.Order{
		OrderID: "CP-PRIVATE-1234567", BuyerBAN: "12345675", BuyerName: "人工醫院名稱",
		Items: []appdata.InvoiceItem{{Description: "真實酷澎商品", Quantity: 1, UnitPrice: 105, Amount: 105, TaxType: "1", Remark: "真實備註"}},
		TotalAmount: 105,
	}
	result, err := service.IssueCoupangWithLookup(context.Background(), order, NameLookup{LookupSucceeded: true})
	if err != nil || !result.Opened { t.Fatalf("result=%#v err=%v", result, err) }
	if fake.banCalls != 0 { t.Fatalf("confirmation decision caused a second BAN lookup: %d", fake.banCalls) }
	if fake.lastIssue.OrderID != order.OrderID || fake.lastIssue.BuyerIdentifier != testBuyerIdentifier ||
		fake.lastIssue.BuyerName != "測試消費者" || fake.lastIssue.ProductItems[0].Description != "測試商品 1" ||
		fake.lastIssue.ProductItems[0].Remark != "" {
		t.Fatalf("Coupang private data reached shared test pool: %#v", fake.lastIssue)
	}
	if result.Record.Source != appdata.SourceCoupang || result.Record.BuyerIdentifier != "12345675" ||
		result.Record.BuyerName != "人工醫院名稱" || result.Record.Items[0].Description != "真實酷澎商品" {
		t.Fatalf("local Coupang record lost source data: %#v", result.Record)
	}
}

func TestProductionIsLockedUntilAdministratorPasswordIsConfigured(t *testing.T) {
	fake := &fakeGateway{}
	service := newTestService(t, fake)
	settings, err := service.Repository.Settings.LoadOrCreate()
	if err != nil { t.Fatal(err) }
	settings.Environment = appdata.EnvironmentProduction
	settings.ProdInvoice = "12345675"
	if err = service.Repository.Settings.SetProdAppKey(&settings, "TEST-KEY-NOT-REAL"); err != nil { t.Fatal(err) }
	if err = service.Repository.Settings.Save(settings); err != nil { t.Fatal(err) }
	if _, _, err = service.gateway(); err == nil || !strings.Contains(err.Error(), "正式環境目前已鎖定") { t.Fatalf("gateway error = %v", err) }
}


func TestIssueMOWithConfirmedManualNameUsesOfficialInclusiveTotalAndRemembersAfterSuccess(t *testing.T) {
	fake := &fakeGateway{
		issueResponse: amego.IssueResponse{IssueResult: amego.IssueResult{InvoiceNumber: "MO12345678"}},
		queryResponse: amego.QueryResponse{Data: amego.QueryResult{InvoiceNumber: "MO12345678"}},
	}
	service := newTestService(t, fake)
	order := moimport.Order{
		OrderID: "66090500839412", BuyerBAN: "12345675", BuyerName: "人工醫院名稱",
		Items: []appdata.InvoiceItem{{Description: "商品", Quantity: 1, UnitPrice: 1014, Amount: 1014, TaxType: "1"}},
		TotalAmount: 1014,
	}
	lookup := NameLookup{LookupSucceeded: true}
	result, err := service.IssueMOWithLookup(context.Background(), order, lookup)
	if err != nil || !result.Opened { t.Fatalf("result=%#v err=%v", result, err) }
	if fake.lastIssue.BuyerName != "測試消費者" ||
		fake.lastIssue.SalesAmount != int64(966) ||
		fake.lastIssue.TaxAmount != int64(48) ||
		fake.lastIssue.TotalAmount != int64(1014) ||
		fake.lastIssue.DetailVAT != 1 {
		t.Fatalf("company MO request = %#v", fake.lastIssue)
	}
	name, found, err := service.Repository.BuyerNames.Lookup("12345675")
	if err != nil || !found || name != "人工醫院名稱" {
		t.Fatalf("memory=%q,%v err=%v", name, found, err)
	}
}

func TestIssueMOWithConfirmedManualNameDoesNotRememberRejectedInvoice(t *testing.T) {
	fake := &fakeGateway{
		issueError: &amego.APIError{Code: 101, Message: "invalid"},
		queryError: &amego.APIError{Code: 71, Message: "not found"},
	}
	service := newTestService(t, fake)
	order := moimport.Order{
		OrderID: "66090500839413", BuyerBAN: "12345675", BuyerName: "不應記住",
		Items: []appdata.InvoiceItem{{Description: "商品", Quantity: 1, UnitPrice: 105, Amount: 105, TaxType: "1"}},
		TotalAmount: 105,
	}
	if _, err := service.IssueMOWithLookup(context.Background(), order, NameLookup{LookupSucceeded: true}); err == nil {
		t.Fatal("rejected invoice returned no error")
	}
	if name, found, err := service.Repository.BuyerNames.Lookup("12345675"); err != nil || found || name != "" {
		t.Fatalf("rejected name was remembered: %q,%v err=%v", name, found, err)
	}
}

func TestIssueMOWithLookupBlocksTechnicalLookupFailureBeforeSending(t *testing.T) {
	fake := &fakeGateway{}
	service := newTestService(t, fake)
	order := moimport.Order{
		OrderID: "66090500839414", BuyerBAN: "12345675", BuyerName: "未經確認",
		Items: []appdata.InvoiceItem{{Description: "商品", Quantity: 1, UnitPrice: 105, Amount: 105, TaxType: "1"}},
		TotalAmount: 105,
	}
	if _, err := service.IssueMOWithLookup(context.Background(), order, NameLookup{}); err == nil {
		t.Fatal("unresolved lookup was allowed")
	}
	if fake.issueCalls != 0 { t.Fatalf("unresolved lookup sent %d request(s)", fake.issueCalls) }
}

func TestRefreshAllRemembersManualNameOnlyAfterUnknownInvoiceIsConfirmedOpened(t *testing.T) {
	fake := &fakeGateway{
		issueError: errors.New("connection reset"),
		queryError: errors.New("temporary query failure"),
	}
	service := newTestService(t, fake)
	order := moimport.Order{
		OrderID: "66090500839415", BuyerBAN: "12345675", BuyerName: "稍後才記住",
		Items: []appdata.InvoiceItem{{Description: "商品", Quantity: 1, UnitPrice: 105, Amount: 105, TaxType: "1"}},
		TotalAmount: 105,
	}
	result, err := service.IssueMOWithLookup(context.Background(), order, NameLookup{LookupSucceeded: true})
	if err == nil || !result.Unknown { t.Fatalf("result=%#v err=%v", result, err) }
	if _, found, lookupErr := service.Repository.BuyerNames.Lookup("12345675"); lookupErr != nil || found {
		t.Fatalf("unknown invoice remembered too early: found=%v err=%v", found, lookupErr)
	}

	fake.queryError = nil
	fake.queryResponse = confirmedQuery(order.OrderID, "MO87654321", 100, 5, 105, 1)
	if _, err = service.RefreshAll(context.Background()); err != nil { t.Fatal(err) }
	name, found, lookupErr := service.Repository.BuyerNames.Lookup("12345675")
	if lookupErr != nil || !found || name != "稍後才記住" {
		t.Fatalf("confirmed name=%q found=%v err=%v", name, found, lookupErr)
	}
}


func TestBuyerLookupUsesArtificialMemoryBeforeAPI(t *testing.T) {
	fake := &fakeGateway{banError: errors.New("API should not be called")}
	service := newTestService(t, fake)
	saved, err := service.Repository.BuyerNames.RememberAfterSuccessfulInvoice("12345675", true, "", "人工醫療院所", true)
	if err != nil || !saved { t.Fatalf("seed memory saved=%v err=%v", saved, err) }
	lookup, err := service.LookupBuyerName(context.Background(), "12345675")
	if err != nil || !lookup.Local || lookup.Name != "人工醫療院所" {
		t.Fatalf("lookup=%#v err=%v", lookup, err)
	}
	if fake.banCalls != 0 { t.Fatalf("API called %d time(s) despite local memory", fake.banCalls) }
}

func TestBuyerLookupDoesNotCacheOfficialAPIName(t *testing.T) {
	fake := &fakeGateway{banResponse: amego.BANResponse{Data: []amego.BANResult{{BAN: "12345675", Name: "官方公司名稱"}}}}
	service := newTestService(t, fake)
	for index := 0; index < 2; index++ {
		lookup, err := service.LookupBuyerName(context.Background(), "12345675")
		if err != nil || lookup.Local || lookup.APIName != "官方公司名稱" {
			t.Fatalf("lookup %d=%#v err=%v", index, lookup, err)
		}
	}
	if fake.banCalls != 2 { t.Fatalf("official name API calls=%d, want 2", fake.banCalls) }
	if name, found, err := service.Repository.BuyerNames.Lookup("12345675"); err != nil || found || name != "" {
		t.Fatalf("official API name was cached: %q,%v err=%v", name, found, err)
	}
}

func TestAmbiguousIssueDoesNotAcceptWrongOrderOrVoidedInvoice(t *testing.T) {
	voided := confirmedQuery("20260905001", "GH12345678", 105, 0, 105, 1)
	voided.Data.CancelDate = 1788566887
	tests := []struct {
		name  string
		query amego.QueryResponse
	}{
		{name: "wrong order", query: confirmedQuery("ANOTHER-ORDER", "GH12345678", 105, 0, 105, 1)},
		{name: "voided", query: voided},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			fake := &fakeGateway{issueError: errors.New("connection reset"), queryResponse: test.query}
			service := newTestService(t, fake)
			result, err := service.IssueManual(context.Background(), validManualDraft())
			if err == nil || !result.Unknown || result.Opened {
				t.Fatalf("result=%#v err=%v", result, err)
			}
		})
	}
}

func TestVoidedOrderReissueUsesNewAPIOrderID(t *testing.T) {
	fake := &fakeGateway{issueResponse: amego.IssueResponse{IssueResult: amego.IssueResult{InvoiceNumber: "JK12345678"}}}
	service := newTestService(t, fake)
	if err := service.Repository.Invoices.Append(appdata.InvoiceRecord{
		ID: "old-attempt", Source: appdata.SourceManual,
		OriginalOrderID: "20260905001", OrderID: "20260905001",
		APIOrderID: "20260905001", Attempt: 1,
		InvoiceState: appdata.InvoiceStateVoided, Environment: appdata.EnvironmentTest,
	}); err != nil {
		t.Fatal(err)
	}
	result, err := service.IssueManual(context.Background(), validManualDraft())
	if err != nil || !result.Opened {
		t.Fatalf("result=%#v err=%v", result, err)
	}
	if fake.lastIssue.OrderID != "20260905001-R2" || result.Record.APIOrderID != "20260905001-R2" || result.Record.Attempt != 2 {
		t.Fatalf("request=%#v record=%#v", fake.lastIssue, result.Record)
	}
}

func TestRemoteSuccessAndLocalSaveFailureRemainOpened(t *testing.T) {
	fake := &fakeGateway{issueResponse: amego.IssueResponse{IssueResult: amego.IssueResult{InvoiceNumber: "LM12345678"}}}
	service := newTestService(t, fake)
	service.updateStatus = func(string, appdata.StatusUpdate) error { return errors.New("disk full") }
	result, err := service.IssueManual(context.Background(), validManualDraft())
	var localErr *LocalPersistenceError
	if !result.Opened || result.Record.InvoiceNumber != "LM12345678" || !errors.As(err, &localErr) {
		t.Fatalf("result=%#v err=%v", result, err)
	}
}

func TestRoundedImportSetsDetailAmountRound(t *testing.T) {
	draft := validManualDraft()
	draft.Items[0].AllowSubtotalRounding = true
	request := buildIssueRequest(draft, issueOptions{})
	if request.DetailAmountRound != 1 {
		t.Fatalf("DetailAmountRound=%d", request.DetailAmountRound)
	}
}
