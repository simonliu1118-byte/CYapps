package model

import (
	"strings"
	"time"
)

func Migrate(state *State) bool {
	if state.SchemaVersion >= CurrentSchemaVersion {
		return false
	}
	for index := range state.Formats {
		f := &state.Formats[index]
		if f.ID != "format-15k" {
			continue
		}
		f.Recipient.Rect = RectMM{X: 43, Y: 49, W: 18, H: 142}
		f.Address.Rect = RectMM{X: 64, Y: 46, W: 29, H: 148}
		f.Phone.Rect = RectMM{X: 32, Y: 59, W: 8, H: 132}
		f.PostalCode.Rect = RectMM{X: 52, Y: 21, W: 40, H: 8}
		for i := range f.Delivery {
			f.Delivery[i].X = 8.2
			f.Delivery[i].Y = 63 + float64(i)*4
			f.Delivery[i].MarkSize = 2.8
		}
		f.Frame.Rect = RectMM{X: 7, Y: 95, W: 30, H: 12}
	}
	state.SchemaVersion = CurrentSchemaVersion
	return true
}

func NormalizeName(value string) string {
	return strings.Join(strings.Fields(strings.TrimSpace(value)), " ")
}

func NormalizeAddress(value string) string {
	value = strings.ReplaceAll(value, "台", "臺")
	value = strings.ReplaceAll(value, " ", "")
	value = strings.ReplaceAll(value, "　", "")
	return strings.TrimSpace(value)
}

func (s *State) ContactByName(name string) *Contact {
	target := NormalizeName(name)
	for index := range s.Contacts {
		if strings.EqualFold(NormalizeName(s.Contacts[index].Name), target) {
			return &s.Contacts[index]
		}
	}
	return nil
}

func (c *Contact) AddressByID(id string) *Address {
	for index := range c.Addresses {
		if c.Addresses[index].ID == id {
			return &c.Addresses[index]
		}
	}
	return nil
}

func (c *Contact) PhoneByID(id string) *Phone {
	for index := range c.Phones {
		if c.Phones[index].ID == id {
			return &c.Phones[index]
		}
	}
	return nil
}

func (c *Contact) AddressByValue(value string) *Address {
	target := NormalizeAddress(value)
	for index := range c.Addresses {
		if NormalizeAddress(c.Addresses[index].Value) == target {
			return &c.Addresses[index]
		}
	}
	return nil
}

func (c *Contact) PhoneByDisplay(number, extension string) *Phone {
	for index := range c.Phones {
		if c.Phones[index].Number == number && c.Phones[index].Extension == extension {
			return &c.Phones[index]
		}
	}
	return nil
}

func (c *Contact) Renumber() {
	// Address/phone numbers are presentation order, not persistent identities.
	for index := range c.Addresses {
		if strings.TrimSpace(c.Addresses[index].Label) == "" {
			continue
		}
	}
	for index := range c.Phones {
		_ = index
	}
}

func NewID(prefix string) string {
	return prefix + "-" + time.Now().UTC().Format("20060102150405.000000000")
}
