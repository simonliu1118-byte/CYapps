package moimport

import (
	"fmt"
	"path/filepath"
	"strings"
)

func Read(path, password string) ([]Order, error) {
	extension := strings.ToLower(filepath.Ext(path))
	var rows [][]string
	var err error
	if extension == ".xlsx" {
		rows, err = ReadXLSX(path)
		if err != nil && strings.TrimSpace(password) != "" { rows, err = readWithExcel(path, password) }
	} else if extension == ".xls" {
		rows, err = readWithExcel(path, password)
	} else {
		return nil, fmt.Errorf("不支援的 Excel 格式 %q，請選擇 .xls 或 .xlsx", extension)
	}
	if err != nil { return nil, err }
	return ParseRows(rows)
}
