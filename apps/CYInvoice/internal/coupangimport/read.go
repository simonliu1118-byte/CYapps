package coupangimport

import (
	"fmt"
	"path/filepath"
	"strings"

	"cyinvoice/internal/moimport"
)

func Read(path string) ([]Order, error) {
	if strings.ToLower(filepath.Ext(path)) != ".xlsx" {
		return nil, fmt.Errorf("酷澎匯入目前只支援 .xlsx")
	}
	rows, err := moimport.ReadXLSX(path)
	if err != nil {
		return nil, err
	}
	return ParseRows(rows)
}
