//go:build !windows

package moimport

import "errors"

func readWithExcel(path, password string) ([][]string, error) {
	return nil, errors.New("受密碼保護的 .xls 必須在已安裝 Microsoft Excel 的 Windows 電腦上讀取")
}
