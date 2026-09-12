package printer

import "cyenvelope/internal/model"

type Job struct {
	Recipient    string
	Address      string
	Phone        string
	PostalCode   string
	DeliveryIDs  []string
	FrameText    string
	FrameVisible bool
	Format       model.EnvelopeFormat
}
