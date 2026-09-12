package service

import (
	"testing"

	"cyenvelope/internal/model"
)

func TestCommitPrintIntentRemembersAddressPhoneAndDelivery(t *testing.T) {
	state := model.DefaultState()
	first, err := CommitPrintIntent(&state, PrintInput{
		Recipient: "王小明", Address: "臺北市大安區忠孝東路1號",
		Phone: "02-23810435 分機 123", DeliveryIDs: []string{"delivery-2"},
	})
	if err != nil {
		t.Fatal(err)
	}
	if len(first.Addresses) != 1 || first.Addresses[0].PostalCode != "106" {
		t.Fatalf("address=%+v", first.Addresses)
	}
	if len(first.Phones) != 1 || first.Phones[0].Display() != "(02) 2381-0435 #123" {
		t.Fatalf("phone=%+v", first.Phones)
	}
	if first.Addresses[0].LastPhoneID != first.Phones[0].ID || first.LastDeliveryOption[0] != "delivery-2" {
		t.Fatal("last-used state not linked")
	}

	second, err := CommitPrintIntent(&state, PrintInput{
		Recipient: "王小明", Address: "新北市板橋區文化路2號",
		Phone: "0912345678", AddressConflict: AddressAdd,
	})
	if err != nil {
		t.Fatal(err)
	}
	if len(second.Addresses) != 2 || len(second.Phones) != 2 {
		t.Fatalf("contact=%+v", second)
	}
	if second.LastAddressID != second.Addresses[1].ID || second.Addresses[1].LastPhoneID != second.Phones[1].ID {
		t.Fatal("new last-used state incorrect")
	}
}

func TestOverwriteLastAddress(t *testing.T) {
	state := model.DefaultState()
	_, _ = CommitPrintIntent(&state, PrintInput{Recipient: "公司", Address: "臺北市信義區A路1號"})
	c, err := CommitPrintIntent(&state, PrintInput{Recipient: "公司", Address: "臺北市信義區B路2號", AddressConflict: AddressOverwriteLast})
	if err != nil {
		t.Fatal(err)
	}
	if len(c.Addresses) != 1 || c.Addresses[0].Value != "臺北市信義區B路2號" {
		t.Fatalf("addresses=%+v", c.Addresses)
	}
}

func TestSuggestionsPrefixBeforeContains(t *testing.T) {
	state := model.DefaultState()
	state.Contacts = []model.Contact{{Name: "高美中醫診所"}, {Name: "高雄大學"}, {Name: "美好公司"}}
	got := ContactSuggestions(&state, "高")
	if len(got) != 2 || got[0] != "高美中醫診所" || got[1] != "高雄大學" {
		t.Fatalf("got=%v", got)
	}
	got = ContactSuggestions(&state, "美")
	if got[0] != "美好公司" {
		t.Fatalf("prefix not first: %v", got)
	}
}
