package version

import "fmt"

const (
	ProductName = "CYInvoice"
	CompanyName = "Chihyuan"
	Copyright   = "© 2026 C.C.LIU All Rights Reserved."
)

// Value and Commit are replaced by the build script through -ldflags.
var (
	Value  = "1.0.0-rebuild.1"
	Commit = "unknown"
)

func Display() string {
	return "V" + Value
}

func WindowTitle() string {
	return fmt.Sprintf("CY 電子發票 %s", Display())
}

