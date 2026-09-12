package invoicing

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"strconv"
	"strings"
	"sync"
	"time"

	"cyinvoice/internal/amego"
	"cyinvoice/internal/appdata"
	"cyinvoice/internal/coupangimport"
	"cyinvoice/internal/moimport"
)

const (
	DeliveryPaper = "紙本"
	testBuyerIdentifier = "28080623"
	testCarrierID = "cyinvoice-test@example.com"
)

type Gateway interface {
	Issue(context.Context, amego.IssueRequest) (amego.IssueResponse, error)
	QueryByOrderID(context.Context, string) (amego.QueryResponse, error)
	QueryByInvoiceNumber(context.Context, string) (amego.QueryResponse, error)
	Status(context.Context, []string) (amego.StatusResponse, error)
	QueryBAN(context.Context, []string) (amego.BANResponse, error)
}

type GatewayFactory func(invoice, appKey string) Gateway

type Service struct {
	Repository *appdata.Repository
	NewGateway GatewayFactory
	Now        func() time.Time
	issueMu sync.Mutex
	gatewayMu sync.Mutex
	cachedGateway Gateway
	cachedGatewayKey string
	updateStatus func(string, appdata.StatusUpdate) error
}

type NameLookup struct {
	Name            string
	Local           bool
	LookupSucceeded bool
	APIName         string
}

type IssueResult struct {
	Record appdata.InvoiceRecord
	Opened bool
	Unknown bool
	LocalSaveError error
}

// LocalPersistenceError means AMEGO has already opened the invoice but the
// final local status (or buyer-name memory) could not be saved. Callers must
// never present this as an invoice rejection or offer to resend it.
type LocalPersistenceError struct {
	InvoiceNumber string
	Err error
}

func (err *LocalPersistenceError) Error() string {
	return fmt.Sprintf("光貿已成功開立發票 %s，但本機紀錄保存失敗：%v；請勿重送，重新整理狀態後會再次修復", err.InvoiceNumber, err.Err)
}

func (err *LocalPersistenceError) Unwrap() error { return err.Err }

func New(repository *appdata.Repository) *Service {
	return &Service{
		Repository: repository,
		NewGateway: func(invoice, appKey string) Gateway { return amego.New(invoice, appKey) },
		Now: time.Now,
	}
}

func (service *Service) HealthCheck(ctx context.Context) error {
	if service.Repository == nil {
		return errors.New("本機資料庫尚未初始化")
	}
	settings, err := service.Repository.Settings.LoadOrCreate()
	if err != nil {
		return err
	}
	ban := amego.TestInvoice
	if settings.Environment == appdata.EnvironmentProduction {
		ban = strings.TrimSpace(settings.ProdInvoice)
	}
	gateway, _, err := service.gateway()
	if err != nil {
		return err
	}
	if _, err = gateway.QueryBAN(ctx, []string{ban}); err != nil {
		// Code 99 is a business-field validation reply from the company-name
		// endpoint. It proves the signed request reached AMEGO and was parsed,
		// so it is healthy for connectivity purposes even when the test invoice
		// number is not a valid buyer BAN.
		if amego.IsAPIErrorCode(err, 99) {
			return nil
		}
		return fmt.Errorf("API 健康檢查：%w", err)
	}
	return nil
}

func (service *Service) gateway() (Gateway, string, error) {
	if service.Repository == nil { return nil, "", errors.New("本機資料庫尚未初始化") }
	settings, err := service.Repository.Settings.LoadOrCreate()
	if err != nil { return nil, "", err }
	invoice, key := amego.TestInvoice, amego.TestAppKey
	if settings.Environment == appdata.EnvironmentProduction {
		if !settings.AdminPasswordSet { return nil, "", errors.New("首次使用請先到設定視窗建立管理密碼，正式環境目前已鎖定") }
		invoice = strings.TrimSpace(settings.ProdInvoice)
		key, err = service.Repository.Settings.ProdAppKey(settings)
		if err != nil { return nil, "", fmt.Errorf("解密正式 App Key：%w", err) }
		if invoice == "" || strings.TrimSpace(key) == "" {
			return nil, "", errors.New("正式環境尚未設定公司統編與 App Key")
		}
	}
	factory := service.NewGateway
	if factory == nil { factory = func(invoice, appKey string) Gateway { return amego.New(invoice, appKey) } }
	cacheKey := settings.Environment + "\x00" + invoice + "\x00" + key
	service.gatewayMu.Lock()
	defer service.gatewayMu.Unlock()
	if service.cachedGateway == nil || service.cachedGatewayKey != cacheKey {
		service.cachedGateway = factory(invoice, key)
		service.cachedGatewayKey = cacheKey
	}
	return service.cachedGateway, settings.Environment, nil
}

func (service *Service) LookupBuyerName(ctx context.Context, ban string) (NameLookup, error) {
	ban = strings.TrimSpace(ban)
	if len(ban) != 8 { return NameLookup{}, errors.New("公司統編必須為 8 碼") }
	for _, character := range ban {
		if character < '0' || character > '9' { return NameLookup{}, errors.New("公司統編必須為 8 碼數字") }
	}
	name, found, err := service.Repository.BuyerNames.Lookup(ban)
	if err != nil { return NameLookup{}, err }
	if found { return NameLookup{Name: name, Local: true}, nil }
	gateway, _, err := service.gateway()
	if err != nil { return NameLookup{}, err }
	response, err := gateway.QueryBAN(ctx, []string{ban})
	if err != nil {
		// Code 99 is the company-name endpoint's business reply for a BAN that
		// cannot be resolved. The signed API request still completed normally,
		// so callers may ask for a manual buyer name without marking API health
		// as abnormal.
		if amego.IsAPIErrorCode(err, 99) {
			return NameLookup{LookupSucceeded: true}, nil
		}
		return NameLookup{}, fmt.Errorf("查詢買受人名稱：%w", err)
	}
	result := NameLookup{LookupSucceeded: true}
	for _, item := range response.Data {
		if strings.TrimSpace(item.BAN) == ban {
			result.Name = strings.TrimSpace(item.Name)
			result.APIName = result.Name
			break
		}
	}
	return result, nil
}

func (service *Service) IssueManual(ctx context.Context, draft appdata.InvoiceDraft) (IssueResult, error) {
	lookup := NameLookup{}
	if draft.CompanyBuyer {
		var err error
		lookup, err = service.LookupBuyerName(ctx, draft.BuyerIdentifier)
		if err != nil { return IssueResult{}, err }
		if lookup.Name != "" { draft.BuyerName = lookup.Name }
	}
	return service.IssueManualWithLookup(ctx, draft, lookup)
}

// IssueManualWithLookup is used by the GUI after it has shown the resolved
// buyer name in the final confirmation. It avoids a second API lookup between
// confirmation and issuing.
func (service *Service) IssueManualWithLookup(ctx context.Context, draft appdata.InvoiceDraft, lookup NameLookup) (IssueResult, error) {
	if draft.CompanyBuyer && lookup.Name != "" { draft.BuyerName = lookup.Name }
	return service.issue(ctx, draft, issueOptions{Source: appdata.SourceManual, OriginalOrderID: draft.OrderID, Delivery: DeliveryPaper}, lookup)
}

func (service *Service) IssueMO(ctx context.Context, order moimport.Order) (IssueResult, error) {
	company := isMOCompany(order)
	if company {
		lookup, err := service.LookupBuyerName(ctx, order.BuyerBAN)
		if err != nil { return IssueResult{}, err }
		if strings.TrimSpace(lookup.Name) == "" {
			return IssueResult{}, errors.New("光貿查詢成功但沒有公司名稱，請先在匯入確認視窗輸入買方名稱")
		}
		order.BuyerName = lookup.Name
		return service.IssueMOWithLookup(ctx, order, lookup)
	}
	return service.IssueMOWithLookup(ctx, order, NameLookup{})
}

// IssueMOWithLookup is called only after the import confirmation window has
// resolved the buyer name. It preserves that exact decision and never performs
// a second BAN lookup between confirmation and issuing.
func (service *Service) IssueMOWithLookup(ctx context.Context, order moimport.Order, lookup NameLookup) (IssueResult, error) {
	company := isMOCompany(order)
	settings, err := service.Repository.Settings.LoadOrCreate()
	if err != nil { return IssueResult{}, err }
	if company {
		switch {
		case lookup.Local && strings.TrimSpace(lookup.Name) != "":
			order.BuyerName = strings.TrimSpace(lookup.Name)
		case lookup.LookupSucceeded && strings.TrimSpace(lookup.APIName) != "":
			order.BuyerName = strings.TrimSpace(lookup.APIName)
		case lookup.LookupSucceeded:
			order.BuyerName = strings.TrimSpace(order.BuyerName)
			if order.BuyerName == "" {
				return IssueResult{}, errors.New("光貿查不到公司名稱，請在匯入確認視窗輸入買方名稱")
			}
		default:
			return IssueResult{}, errors.New("買方名稱尚未完成查詢，已擋下且未送出")
		}
		order.Carrier = "公司戶"
		order.CarrierID1 = ""
		order.CarrierID2 = ""
		order.NPOBAN = ""
	} else {
		order.BuyerBAN = ""
		order.BuyerName = strings.TrimSpace(order.BuyerName)
		if order.BuyerName == "" { order.BuyerName = "消費者" }
		order.Carrier = moimport.CarrierMember
		if strings.TrimSpace(order.CarrierID1) == "" { order.CarrierID1 = "motmp_" + order.OrderID }
		if strings.TrimSpace(order.CarrierID2) == "" { order.CarrierID2 = order.CarrierID1 }
	}
	draft := appdata.InvoiceDraft{
		OrderID: order.OrderID, CompanyBuyer: company,
		BuyerIdentifier: order.BuyerBAN, BuyerName: order.BuyerName,
		Items: order.Items, TotalAmount: order.TotalAmount, MainRemark: order.MainRemark,
	}
	carrierType, err := carrierType(order.Carrier)
	if err != nil { return IssueResult{}, err }
	delivery := strings.TrimSpace(order.Carrier)
	if company { delivery = DeliveryPaper }
	if strings.TrimSpace(order.NPOBAN) != "" { delivery = "捐贈" }
	if delivery == "" { delivery = DeliveryPaper }
	options := issueOptions{
		Source: appdata.SourceMO, OriginalOrderID: order.OrderID, Delivery: delivery,
		BuyerAddress: order.BuyerAddress, BuyerPhone: order.BuyerPhone, BuyerEmail: order.BuyerEmail,
		BuyerName: order.BuyerName, CarrierType: carrierType, CarrierID1: order.CarrierID1,
		CarrierID2: order.CarrierID2, NPOBAN: order.NPOBAN,
	}
	if settings.Environment == appdata.EnvironmentTest {
		options.APIBuyerName = "測試消費者"
		options.TestPrivacy = true
	}
	return service.issue(ctx, draft, options, lookup)
}

func isMOCompany(order moimport.Order) bool {
	ban := strings.TrimSpace(order.BuyerBAN)
	return ban != "" && ban != "0000000000"
}

func (service *Service) IssueCoupang(ctx context.Context, order coupangimport.Order) (IssueResult, error) {
	company := strings.TrimSpace(order.BuyerBAN) != ""
	lookup := NameLookup{}
	if company {
		var err error
		lookup, err = service.LookupBuyerName(ctx, order.BuyerBAN)
		if err != nil {
			return IssueResult{}, err
		}
		if lookup.Name == "" { return IssueResult{}, errors.New("光貿查不到公司名稱，請在匯入確認視窗輸入買方名稱") }
		order.BuyerName = lookup.Name
	}
	return service.IssueCoupangWithLookup(ctx, order, lookup)
}

// IssueCoupangWithLookup receives the buyer-name decision made in the shared
// import confirmation window. All imported platforms therefore use the same
// lookup, confirmation, duplicate protection and sequential issuing rules.
func (service *Service) IssueCoupangWithLookup(ctx context.Context, order coupangimport.Order, lookup NameLookup) (IssueResult, error) {
	company := strings.TrimSpace(order.BuyerBAN) != ""
	if company {
		switch {
		case lookup.Local && strings.TrimSpace(lookup.Name) != "":
			order.BuyerName = strings.TrimSpace(lookup.Name)
		case lookup.LookupSucceeded && strings.TrimSpace(lookup.APIName) != "":
			order.BuyerName = strings.TrimSpace(lookup.APIName)
		case lookup.LookupSucceeded:
			order.BuyerName = strings.TrimSpace(order.BuyerName)
			if order.BuyerName == "" { return IssueResult{}, errors.New("光貿查不到公司名稱，請在匯入確認視窗輸入買方名稱") }
		default:
			return IssueResult{}, errors.New("買方名稱尚未完成查詢，已擋下且未送出")
		}
	} else {
		order.BuyerBAN = ""
		order.BuyerName = strings.TrimSpace(order.BuyerName)
		if order.BuyerName == "" { order.BuyerName = "消費者" }
	}
	draft := appdata.InvoiceDraft{
		OrderID: order.OrderID, CompanyBuyer: company,
		BuyerIdentifier: order.BuyerBAN, BuyerName: order.BuyerName,
		Items: order.Items, TotalAmount: order.TotalAmount,
	}
	options := issueOptions{
		Source: appdata.SourceCoupang, OriginalOrderID: order.OrderID,
		Delivery: DeliveryPaper, BuyerName: draft.BuyerName,
	}
	settings, err := service.Repository.Settings.LoadOrCreate()
	if err != nil { return IssueResult{}, err }
	if settings.Environment == appdata.EnvironmentTest {
		options.APIBuyerName = "測試消費者"
		options.TestPrivacy = true
	}
	return service.issue(ctx, draft, options, lookup)
}

type issueOptions struct {
	Source, OriginalOrderID, Delivery string
	BuyerAddress, BuyerPhone, BuyerEmail, BuyerName string
	CarrierType, CarrierID1, CarrierID2, NPOBAN string
	APIOrderID, APIBuyerName string
	TestPrivacy bool
}

func (service *Service) issue(ctx context.Context, draft appdata.InvoiceDraft, options issueOptions, lookup NameLookup) (IssueResult, error) {
	service.issueMu.Lock()
	defer service.issueMu.Unlock()
	if err := draft.Validate(); err != nil { return IssueResult{}, err }
	if options.Source == "" { options.Source = appdata.SourceManual }
	if options.OriginalOrderID == "" { options.OriginalOrderID = draft.OrderID }
	if options.Delivery == "" { options.Delivery = DeliveryPaper }

	records, err := service.Repository.Invoices.LoadOrCreate()
	if err != nil { return IssueResult{}, err }
	gateway, environment, err := service.gateway()
	if err != nil { return IssueResult{}, err }
	if reason := appdata.DuplicateBlockReason(records, options.Source, options.OriginalOrderID, environment); reason != "" {
		return IssueResult{}, errors.New(reason)
	}
	now := service.now()
	attempt := nextAttempt(records, options.Source, options.OriginalOrderID, environment)
	apiOrderID := retryAPIOrderID(requestOrderID(draft, options), attempt)
	record := appdata.InvoiceRecord{
		ID: newRecordID(now), Source: options.Source,
		OriginalOrderID: options.OriginalOrderID, OrderID: draft.OrderID, Attempt: attempt,
		BuyerIdentifier: normalizedBuyerIdentifier(draft), BuyerName: effectiveBuyerName(draft, options.BuyerName),
		APIOrderID: apiOrderID, CarrierType: options.CarrierType,
		CarrierID1: options.CarrierID1, CarrierID2: options.CarrierID2, NPOBAN: options.NPOBAN,
		Amount: draft.TotalAmount, Delivery: options.Delivery, InvoiceState: appdata.InvoiceStateChanging,
		Environment: environment, SentAt: formatDateTime(now), Items: draft.Items, MainRemark: draft.MainRemark,
		DetailVAT: detailVAT(draft),
		BuyerNameNeedsMemory: draft.CompanyBuyer && !lookup.Local && lookup.LookupSucceeded && lookup.APIName == "",
	}
	if err := service.Repository.Invoices.Append(record); err != nil { return IssueResult{}, err }

	options.APIOrderID = apiOrderID
	response, issueErr := gateway.Issue(ctx, buildIssueRequest(draft, options))
	if issueErr != nil {
		if isExplicitAPIError(issueErr) {
			record.InvoiceState = appdata.InvoiceStateFailed
			record.ErrorMessage = issueErr.Error()
			if saveErr := service.saveStatus(record.ID, statusUpdate(record, now)); saveErr != nil {
				return IssueResult{Record: record}, fmt.Errorf("%w；另有本機紀錄保存失敗：%v", issueErr, saveErr)
			}
			return IssueResult{Record: record}, issueErr
		}
		query, queryErr := gateway.QueryByOrderID(ctx, apiOrderID)
		if queryErr == nil {
			queryErr = verifyQueryResult(record, draft, query.Data, "")
		}
		if queryErr == nil {
			record = applyQueryResult(record, query.Data, 0, "", service.now())
			if saveErr := service.persistOpened(record, lookup, draft.BuyerName); saveErr != nil {
				return IssueResult{Record: record, Opened: true, LocalSaveError: saveErr}, &LocalPersistenceError{InvoiceNumber: record.InvoiceNumber, Err: saveErr}
			}
			return IssueResult{Record: record, Opened: true}, nil
		}
		record.InvoiceState = appdata.InvoiceStateUnknown
		record.ErrorMessage = issueErr.Error()
		if queryErr != nil { record.ErrorMessage += "；嚴格回查未確認成功：" + queryErr.Error() }
		if saveErr := service.saveStatus(record.ID, statusUpdate(record, now)); saveErr != nil {
			return IssueResult{Record: record, Unknown: true, LocalSaveError: saveErr}, fmt.Errorf("開立結果不明且本機狀態保存失敗：%v；原始錯誤：%w", saveErr, issueErr)
		}
		return IssueResult{Record: record, Unknown: true}, fmt.Errorf("開立結果不明，已禁止重送；請從開立清單重新查詢：%w", issueErr)
	}

	record.InvoiceNumber = strings.TrimSpace(response.InvoiceNumber)
	if record.InvoiceNumber == "" {
		record.InvoiceState = appdata.InvoiceStateUnknown
		record.ErrorMessage = "光貿回覆成功但沒有發票號碼"
		saveErr := service.saveStatus(record.ID, statusUpdate(record, service.now()))
		if saveErr != nil { record.ErrorMessage += "；本機狀態保存失敗：" + saveErr.Error() }
		return IssueResult{Record: record, Unknown: true, LocalSaveError: saveErr}, errors.New("光貿回覆成功但沒有發票號碼，結果不明且已禁止重送")
	}
	if response.InvoiceTime > 0 {
		issuedAt := time.Unix(response.InvoiceTime, 0)
		record.InvoiceDate = issuedAt.Format("2006/01/02")
		record.InvoiceTime = issuedAt.Format("15:04:05")
	}
	query, queryErr := gateway.QueryByInvoiceNumber(ctx, record.InvoiceNumber)
	if queryErr == nil { queryErr = verifyQueryResult(record, draft, query.Data, record.InvoiceNumber) }
	if queryErr == nil {
		record = applyQueryResult(record, query.Data, 0, "", service.now())
	} else {
		record.InvoiceState = appdata.InvoiceStateOpened
		record.ErrorMessage = "發票已開立；嚴格回查待確認：" + queryErr.Error()
		record.LastChecked = formatDateTime(service.now())
	}
	if saveErr := service.persistOpened(record, lookup, draft.BuyerName); saveErr != nil {
		return IssueResult{Record: record, Opened: true, LocalSaveError: saveErr}, &LocalPersistenceError{InvoiceNumber: record.InvoiceNumber, Err: saveErr}
	}
	return IssueResult{Record: record, Opened: true}, nil
}

func (service *Service) RefreshAll(ctx context.Context) ([]appdata.InvoiceRecord, error) {
	records, err := service.Repository.Invoices.LoadOrCreate()
	if err != nil { return nil, err }
	if len(records) == 0 { return records, nil }
	gateway, environment, err := service.gateway()
	if err != nil { return nil, err }
	var problems []string
	for index := range records {
		record := &records[index]
		if record.Environment != "" && record.Environment != environment { continue }
		if record.InvoiceState == appdata.InvoiceStateFailed { continue }
		var query amego.QueryResponse
		var queryErr error
		if strings.TrimSpace(record.InvoiceNumber) != "" {
			query, queryErr = gateway.QueryByInvoiceNumber(ctx, record.InvoiceNumber)
		} else {
			orderID := strings.TrimSpace(record.APIOrderID)
			if orderID == "" { orderID = record.OriginalOrderID }
			query, queryErr = gateway.QueryByOrderID(ctx, orderID)
		}
		if queryErr != nil {
			record.LastChecked = formatDateTime(service.now())
			if record.InvoiceState == appdata.InvoiceStateUnknown && amego.IsAPIErrorCode(queryErr, 71) {
				record.ErrorMessage = "查無發票；原結果仍不明，未自動重送"
			}
			if saveErr := service.saveStatus(record.ID, statusUpdate(*record, service.now())); saveErr != nil {
				problems = append(problems, record.OriginalOrderID+": 本機保存失敗："+saveErr.Error())
			}
			problems = append(problems, record.OriginalOrderID+": "+queryErr.Error())
			continue
		}
		if verifyErr := verifyStoredQueryResult(*record, query.Data); verifyErr != nil {
			problems = append(problems, record.OriginalOrderID+": "+verifyErr.Error())
			continue
		}
		*record = applyQueryResult(*record, query.Data, record.UploadStatus, record.UploadStatusText, service.now())
		if saveErr := service.saveStatus(record.ID, statusUpdate(*record, service.now())); saveErr != nil {
			problems = append(problems, record.OriginalOrderID+": 本機保存失敗："+saveErr.Error())
			continue
		}
		if record.BuyerNameNeedsMemory {
			if rememberErr := service.rememberBuyerName(*record, NameLookup{LookupSucceeded: true}, record.BuyerName); rememberErr != nil {
				problems = append(problems, record.OriginalOrderID+": 買方名稱記憶失敗："+rememberErr.Error())
			}
		}
		if record.InvoiceNumber != "" {
			status, statusErr := gateway.Status(ctx, []string{record.InvoiceNumber})
			if statusErr == nil && len(status.Data) > 0 {
				record.UploadStatus = status.Data[0].Status
				record.UploadStatusText = uploadStatusText(status.Data[0].Status)
				if saveErr := service.saveStatus(record.ID, statusUpdate(*record, service.now())); saveErr != nil {
					problems = append(problems, record.OriginalOrderID+": 上傳狀態保存失敗："+saveErr.Error())
				}
			} else if statusErr != nil {
				problems = append(problems, record.InvoiceNumber+": "+statusErr.Error())
			}
		}
	}
	updated, loadErr := service.Repository.Invoices.LoadOrCreate()
	if loadErr != nil { return nil, loadErr }
	if len(problems) > 0 { return updated, fmt.Errorf("部分紀錄更新失敗：%s", strings.Join(problems, "；")) }
	return updated, nil
}

func applyQueryResult(record appdata.InvoiceRecord, query amego.QueryResult, upload int, uploadText string, now time.Time) appdata.InvoiceRecord {
	if query.InvoiceNumber != "" { record.InvoiceNumber = query.InvoiceNumber }
	record.InvoiceState = appdata.InvoiceStateOpened
	if query.CancelDate != 0 { record.InvoiceState = appdata.InvoiceStateVoided }
	if query.InvoiceDate != "" { record.InvoiceDate = normalizeDate(query.InvoiceDate) }
	if query.InvoiceTime != "" { record.InvoiceTime = query.InvoiceTime }
	record.UploadStatus = upload
	record.UploadStatusText = uploadText
	record.ErrorMessage = ""
	record.LastChecked = formatDateTime(now)
	return record
}

func (service *Service) persistOpened(record appdata.InvoiceRecord, lookup NameLookup, manual string) error {
	if err := service.saveStatus(record.ID, statusUpdate(record, service.now())); err != nil { return err }
	return service.rememberBuyerName(record, lookup, manual)
}

func (service *Service) saveStatus(id string, update appdata.StatusUpdate) error {
	if service.updateStatus != nil { return service.updateStatus(id, update) }
	return service.Repository.Invoices.UpdateStatus(id, update)
}

func (service *Service) rememberBuyerName(record appdata.InvoiceRecord, lookup NameLookup, manual string) error {
	if !record.BuyerNameNeedsMemory || record.InvoiceState != appdata.InvoiceStateOpened { return nil }
	_, err := service.Repository.BuyerNames.RememberAfterSuccessfulInvoice(record.BuyerIdentifier, lookup.LookupSucceeded, lookup.APIName, manual, true)
	return err
}

func buildIssueRequest(draft appdata.InvoiceDraft, options issueOptions) amego.IssueRequest {
	items := make([]amego.ProductItem, 0, len(draft.Items))
	for index, item := range draft.Items {
		quantity := interface{}(item.Quantity)
		if strings.TrimSpace(item.QuantityDecimal) != "" {
			quantity = json.Number(strings.TrimSpace(item.QuantityDecimal))
		}
		unitPrice := interface{}(item.UnitPrice)
		if strings.TrimSpace(item.UnitPriceDecimal) != "" {
			unitPrice = json.Number(strings.TrimSpace(item.UnitPriceDecimal))
		}
		amount := interface{}(item.Amount)
		if strings.TrimSpace(item.AmountDecimal) != "" {
			amount = json.Number(strings.TrimSpace(item.AmountDecimal))
		}
		description, remark := item.Description, item.Remark
		if options.TestPrivacy {
			description = fmt.Sprintf("測試商品 %d", index+1)
			remark = ""
		}
		items = append(items, amego.ProductItem{Description: description, Quantity: quantity, UnitPrice: unitPrice, Amount: amount, Remark: remark, TaxType: 1})
	}
	sales, tax, total, err := appdata.CalculateInvoiceTotals(draft.Items, draft.CompanyBuyer, draft.PricesExcludeTax)
	if err != nil {
		sales, tax, total = draft.TotalAmount, 0, draft.TotalAmount
		if draft.CompanyBuyer {
			sales = draft.TotalAmount
		}
	}
	if !draft.CompanyBuyer {
		tax = 0
		sales = total
	}
	buyerIdentifier := normalizedBuyerIdentifier(draft)
	buyerName := requestBuyerName(draft, options)
	buyerAddress, buyerPhone, buyerEmail := options.BuyerAddress, options.BuyerPhone, options.BuyerEmail
	carrierID1, carrierID2, npoBAN := options.CarrierID1, options.CarrierID2, options.NPOBAN
	mainRemark := draft.MainRemark
	if options.TestPrivacy {
		buyerIdentifier = "0000000000"
		if draft.CompanyBuyer { buyerIdentifier = testBuyerIdentifier }
		buyerName = "測試消費者"
		buyerAddress, buyerPhone, buyerEmail, mainRemark, npoBAN = "", "", "", "", ""
		if strings.TrimSpace(options.CarrierType) != "" {
			carrierID1, carrierID2 = testCarrierID, testCarrierID
		} else {
			carrierID1, carrierID2 = "", ""
		}
	}
	return amego.IssueRequest{
		OrderID: requestOrderID(draft, options), BuyerIdentifier: buyerIdentifier, BuyerName: buyerName,
		BuyerAddress: buyerAddress, BuyerTelephoneNumber: buyerPhone, BuyerEmailAddress: buyerEmail,
		CarrierType: options.CarrierType, CarrierID1: carrierID1, CarrierID2: carrierID2, NPOBAN: npoBAN,
		MainRemark: mainRemark, ProductItems: items, SalesAmount: sales,
		FreeTaxSalesAmount: 0, ZeroTaxSalesAmount: 0, TaxType: 1, TaxRate: "0.05",
		TaxAmount: tax, TotalAmount: total, DetailVAT: detailVAT(draft), DetailAmountRound: detailAmountRound(draft),
	}
}

func requestOrderID(draft appdata.InvoiceDraft, options issueOptions) string {
	if value := strings.TrimSpace(options.APIOrderID); value != "" { return value }
	return strings.TrimSpace(draft.OrderID)
}

func requestBuyerName(draft appdata.InvoiceDraft, options issueOptions) string {
	if value := strings.TrimSpace(options.APIBuyerName); value != "" { return value }
	return effectiveBuyerName(draft, options.BuyerName)
}

func retryAPIOrderID(orderID string, attempt int) string {
	orderID = strings.TrimSpace(orderID)
	if attempt <= 1 { return orderID }
	suffix := "-R" + strconv.Itoa(attempt)
	runes := []rune(orderID)
	limit := 40 - len([]rune(suffix))
	if limit < 1 { return suffix[1:] }
	if len(runes) > limit { runes = runes[:limit] }
	return string(runes) + suffix
}

func detailAmountRound(draft appdata.InvoiceDraft) int {
	for _, item := range draft.Items { if item.AllowSubtotalRounding { return 1 } }
	return 0
}

func detailVAT(draft appdata.InvoiceDraft) int {
	if draft.PricesExcludeTax { return 0 }
	return 1
}

func normalizedBuyerIdentifier(draft appdata.InvoiceDraft) string {
	if draft.CompanyBuyer { return strings.TrimSpace(draft.BuyerIdentifier) }
	return "0000000000"
}
func normalizedBuyerName(draft appdata.InvoiceDraft) string {
	if draft.CompanyBuyer { return strings.TrimSpace(draft.BuyerName) }
	return "消費者"
}
func effectiveBuyerName(draft appdata.InvoiceDraft, override string) string {
	if value := strings.TrimSpace(override); value != "" { return value }
	return normalizedBuyerName(draft)
}
func carrierType(carrier string) (string, error) {
	switch strings.TrimSpace(carrier) {
	case "", "紙本", "公司戶": return "", nil
	case moimport.CarrierMember: return "amego", nil
	case moimport.CarrierMobile: return "3J0002", nil
	case moimport.CarrierCitizen: return "CQ0001", nil
	default: return "", fmt.Errorf("尚未支援的 MO店+ 載具類型：%s", carrier)
	}
}
func statusUpdate(record appdata.InvoiceRecord, now time.Time) appdata.StatusUpdate {
	return appdata.StatusUpdate{InvoiceNumber: record.InvoiceNumber, InvoiceState: record.InvoiceState,
		UploadStatus: record.UploadStatus, UploadStatusText: record.UploadStatusText,
		ErrorMessage: record.ErrorMessage, InvoiceDate: record.InvoiceDate,
		InvoiceTime: record.InvoiceTime, LastChecked: formatDateTime(now)}
}
func isExplicitAPIError(err error) bool { var value *amego.APIError; return errors.As(err, &value) }
func uploadStatusText(status int) string {
	switch status {
	case amego.UploadPending: return "待處理"
	case amego.UploadUploading: return "上傳中"
	case amego.UploadUploaded: return "已上傳"
	case amego.UploadProcessing: return "處理中"
	case amego.UploadConfirming: return "待確認"
	case amego.UploadError: return "錯誤"
	case amego.UploadComplete: return "完成"
	default: return fmt.Sprintf("狀態 %d", status)
	}
}
func nextAttempt(records []appdata.InvoiceRecord, source, original, environment string) int {
	result := 1
	for _, record := range records {
		if strings.EqualFold(strings.TrimSpace(record.Source), strings.TrimSpace(source)) && strings.TrimSpace(record.OriginalOrderID) == strings.TrimSpace(original) && sameEnvironment(record.Environment, environment) && record.Attempt >= result {
			result = record.Attempt + 1
		}
	}
	return result
}

func sameEnvironment(recordEnvironment, currentEnvironment string) bool {
	recordEnvironment, currentEnvironment = strings.TrimSpace(recordEnvironment), strings.TrimSpace(currentEnvironment)
	return recordEnvironment == "" || currentEnvironment == "" || recordEnvironment == currentEnvironment
}

func verifyStoredQueryResult(record appdata.InvoiceRecord, query amego.QueryResult) error {
	if strings.TrimSpace(query.InvoiceNumber) == "" { return errors.New("回查缺少發票號碼") }
	if record.InvoiceNumber != "" && strings.TrimSpace(query.InvoiceNumber) != strings.TrimSpace(record.InvoiceNumber) { return errors.New("回查發票號碼與本機紀錄不符") }
	expectedOrderID := strings.TrimSpace(record.APIOrderID)
	if expectedOrderID == "" { expectedOrderID = strings.TrimSpace(record.OrderID) }
	if strings.TrimSpace(query.OrderID) != expectedOrderID { return errors.New("回查訂單編號與本次送出不符") }
	if query.TotalAmount.String() == "" { return errors.New("回查缺少發票總額") }
	total, err := query.TotalAmount.Int64()
	if err != nil || total != record.Amount { return fmt.Errorf("回查發票總額不符：取得 %s，預期 %d", query.TotalAmount.String(), record.Amount) }
	if len(record.Items) > 0 {
		if !query.DetailVATPresent { return errors.New("回查缺少 DetailVat 計稅模式") }
		if query.DetailVAT != record.DetailVAT {
			return fmt.Errorf("回查 DetailVat 不符：取得 %d，預期 %d", query.DetailVAT, record.DetailVAT)
		}
		companyBuyer := strings.TrimSpace(record.BuyerIdentifier) != "" && strings.TrimSpace(record.BuyerIdentifier) != "0000000000"
		sales, tax, calculatedTotal, calculateErr := appdata.CalculateInvoiceTotals(record.Items, companyBuyer, record.DetailVAT == 0)
		if calculateErr != nil { return fmt.Errorf("計算本機發票稅額：%w", calculateErr) }
		if !companyBuyer { sales, tax = calculatedTotal, 0 }
		if calculatedTotal != record.Amount { return errors.New("本機明細與發票總額不一致") }
		if query.SalesAmount.String() == "" || query.TaxAmount.String() == "" { return errors.New("回查缺少銷售額或稅額") }
		querySales, salesErr := query.SalesAmount.Int64()
		queryTax, taxErr := query.TaxAmount.Int64()
		if salesErr != nil || taxErr != nil || querySales != sales || queryTax != tax {
			return fmt.Errorf("回查稅額資料不符：取得銷售額 %s、稅額 %s，預期 %d、%d", query.SalesAmount.String(), query.TaxAmount.String(), sales, tax)
		}
	}
	return nil
}

func verifyQueryResult(record appdata.InvoiceRecord, draft appdata.InvoiceDraft, query amego.QueryResult, expectedInvoiceNumber string) error {
	if err := verifyStoredQueryResult(record, query); err != nil { return err }
	if expectedInvoiceNumber != "" && strings.TrimSpace(query.InvoiceNumber) != strings.TrimSpace(expectedInvoiceNumber) { return errors.New("回查發票號碼不是本次開立取得的號碼") }
	if query.CancelDate != 0 { return errors.New("回查取得的是已作廢發票，不能視為本次開立成功") }
	sales, tax, total, err := appdata.CalculateInvoiceTotals(draft.Items, draft.CompanyBuyer, draft.PricesExcludeTax)
	if err != nil { return err }
	if !draft.CompanyBuyer { sales, tax = total, 0 }
	if total != record.Amount { return errors.New("本次發票計算總額與本機紀錄不符") }
	if query.SalesAmount.String() == "" || query.TaxAmount.String() == "" { return errors.New("回查缺少銷售額或稅額") }
	querySales, salesErr := query.SalesAmount.Int64()
	queryTax, taxErr := query.TaxAmount.Int64()
	if salesErr != nil || taxErr != nil || querySales != sales || queryTax != tax {
		return fmt.Errorf("回查稅額資料不符：取得銷售額 %s、稅額 %s，預期 %d、%d", query.SalesAmount.String(), query.TaxAmount.String(), sales, tax)
	}
	return nil
}
func newRecordID(now time.Time) string {
	random := make([]byte, 6)
	if _, err := rand.Read(random); err != nil { return fmt.Sprintf("%d", now.UnixNano()) }
	return fmt.Sprintf("%d-%s", now.UnixNano(), hex.EncodeToString(random))
}
func (service *Service) now() time.Time { if service.Now != nil { return service.Now() }; return time.Now() }
func formatDateTime(value time.Time) string { return value.Format("2006/01/02 15:04:05") }
func normalizeDate(value string) string {
	value = strings.TrimSpace(value)
	if len(value) == 8 && !strings.Contains(value, "/") { return value[:4]+"/"+value[4:6]+"/"+value[6:] }
	return value
}
