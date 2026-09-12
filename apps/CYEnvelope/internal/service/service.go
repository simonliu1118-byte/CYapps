package service

import (
	"errors"
	"strings"
	"time"

	"cyenvelope/internal/model"
	"cyenvelope/internal/phone"
	"cyenvelope/internal/postal"
)

type AddressChoice int

const (
	AddressAdd AddressChoice = iota
	AddressOverwriteLast
)

type PrintInput struct {
	Recipient       string
	Address         string
	PostalCode      string
	Phone           string
	DeliveryIDs     []string
	FrameVisible    bool
	FrameText       string
	AddressConflict AddressChoice
}

func CommitPrintIntent(state *model.State, input PrintInput) (*model.Contact, error) {
	input.Recipient = model.NormalizeName(input.Recipient)
	input.Address = strings.TrimSpace(input.Address)
	if input.Recipient == "" || input.Address == "" {
		return nil, errors.New("收件人與收件人地址為必填")
	}
	parsed, err := phone.Parse(input.Phone)
	if err != nil {
		return nil, err
	}
	contact := state.ContactByName(input.Recipient)
	if contact == nil {
		state.Contacts = append(state.Contacts, model.Contact{ID: model.NewID("contact"), Name: input.Recipient})
		contact = &state.Contacts[len(state.Contacts)-1]
	}
	address := contact.AddressByValue(input.Address)
	if address == nil {
		if input.AddressConflict == AddressOverwriteLast && len(contact.Addresses) > 0 {
			address = contact.AddressByID(contact.LastAddressID)
			if address == nil {
				address = &contact.Addresses[len(contact.Addresses)-1]
			}
			address.Value = input.Address
		} else {
			contact.Addresses = append(contact.Addresses, model.Address{ID: model.NewID("address"), Value: input.Address})
			address = &contact.Addresses[len(contact.Addresses)-1]
		}
	}
	if input.PostalCode == "" {
		input.PostalCode = postal.Lookup(input.Address)
	}
	address.PostalCode = input.PostalCode
	contact.LastAddressID = address.ID
	if parsed.Number != "" {
		phoneItem := contact.PhoneByDisplay(parsed.Number, parsed.Extension)
		if phoneItem == nil {
			contact.Phones = append(contact.Phones, model.Phone{ID: model.NewID("phone"), Number: parsed.Number, Extension: parsed.Extension})
			phoneItem = &contact.Phones[len(contact.Phones)-1]
		}
		address.LastPhoneID = phoneItem.ID
	}
	contact.LastDeliveryOption = append([]string(nil), input.DeliveryIDs...)
	contact.UpdatedAt = time.Now()
	return contact, nil
}

func ContactSuggestions(state *model.State, input string) []string {
	q := strings.ToLower(strings.TrimSpace(input))
	var prefix, contains []string
	for _, c := range state.Contacts {
		name := strings.ToLower(c.Name)
		if q == "" || strings.HasPrefix(name, q) {
			prefix = append(prefix, c.Name)
		} else if strings.Contains(name, q) {
			contains = append(contains, c.Name)
		}
	}
	return append(prefix, contains...)
}
