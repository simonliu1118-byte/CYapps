package appdata

import "encoding/json"

var invoiceRecordJSONFields = map[string]struct{}{
	"id": {}, "source": {}, "original_order_id": {}, "order_id": {},
	"attempt": {}, "invoice_number": {}, "buyer_identifier": {},
	"buyer_name": {}, "amount": {}, "delivery": {}, "invoice_state": {},
	"api_order_id": {}, "carrier_type": {}, "carrier_id1": {},
	"carrier_id2": {}, "npo_ban": {},
	"upload_status": {}, "upload_status_text": {}, "error_message": {},
	"environment": {}, "sent_at": {}, "invoice_date": {}, "invoice_time": {},
	"last_checked": {}, "items": {}, "main_remark": {}, "detail_vat": {},
	"buyer_name_needs_memory": {},
}

func (record *InvoiceRecord) UnmarshalJSON(data []byte) error {
	type plain InvoiceRecord
	var decoded plain
	if err := json.Unmarshal(data, &decoded); err != nil {
		return err
	}
	var fields map[string]json.RawMessage
	if err := json.Unmarshal(data, &fields); err != nil {
		return err
	}
	for known := range invoiceRecordJSONFields {
		delete(fields, known)
	}
	*record = InvoiceRecord(decoded)
	if len(fields) != 0 {
		record.Extra = fields
	}
	return nil
}

func (record InvoiceRecord) MarshalJSON() ([]byte, error) {
	type plain InvoiceRecord
	knownData, err := json.Marshal(plain(record))
	if err != nil {
		return nil, err
	}
	var fields map[string]json.RawMessage
	if err := json.Unmarshal(knownData, &fields); err != nil {
		return nil, err
	}
	for name, value := range record.Extra {
		if _, known := invoiceRecordJSONFields[name]; !known {
			fields[name] = value
		}
	}
	return json.Marshal(fields)
}
