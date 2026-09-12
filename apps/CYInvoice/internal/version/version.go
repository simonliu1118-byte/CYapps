package version

import (
	"fmt"
	"strconv"
)

const (
	ProductName = "CYInvoice"
	CompanyName = "Chihyuan"
	Copyright   = "© 2026 C.C.LIU All Rights Reserved."
)

// Value, Build and Commit are replaced by the build script through -ldflags.
var (
	Value  = "1.1.0"
	Build  = "0"
	Commit = "unknown"
)

func Display() string {
	display := "V" + Value
	build, err := strconv.Atoi(Build)
	if err == nil && build > 0 {
		return fmt.Sprintf("%s Build %d", display, build)
	}
	return display
}

func WindowTitle() string {
	return fmt.Sprintf("CY 電子發票 %s", Display())
}
