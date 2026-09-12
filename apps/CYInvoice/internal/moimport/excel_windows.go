//go:build windows

package moimport

import (
	"errors"
	"fmt"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"

	ole "github.com/go-ole/go-ole"
	"github.com/go-ole/go-ole/oleutil"
)

const (
	maxExcelRows    = 100000
	maxExcelColumns = 512
)

// readWithExcel reads password-protected legacy .xls workbooks directly
// through Excel COM. COM must stay on one OS thread for the whole session.
func readWithExcel(path, password string) (rows [][]string, err error) {
	runtime.LockOSThread()
	defer runtime.UnlockOSThread()

	if err := ole.CoInitialize(0); err != nil {
		return nil, fmt.Errorf("啟動 Excel COM：%w", err)
	}
	defer ole.CoUninitialize()

	unknown, err := oleutil.CreateObject("Excel.Application")
	if err != nil {
		return nil, fmt.Errorf("啟動 Microsoft Excel 失敗（請確認已安裝 Excel）：%w", err)
	}
	defer unknown.Release()

	excel, err := unknown.QueryInterface(ole.IID_IDispatch)
	if err != nil {
		return nil, fmt.Errorf("取得 Excel COM 介面：%w", err)
	}
	defer excel.Release()
	defer func() {
		if result, quitErr := oleutil.CallMethod(excel, "Quit"); result != nil {
			_ = result.Clear()
		} else if err == nil && quitErr != nil {
			err = fmt.Errorf("關閉 Excel：%w", quitErr)
		}
	}()

	if err := putExcelProperty(excel, "Visible", false); err != nil {
		return nil, err
	}
	if err := putExcelProperty(excel, "DisplayAlerts", false); err != nil {
		return nil, err
	}
	// Force-disable VBA and workbook events before opening an untrusted export.
	// ReadOnly and UpdateLinks=0 alone do not prevent Workbook_Open macros.
	if err := putExcelProperty(excel, "AutomationSecurity", 3); err != nil {
		return nil, fmt.Errorf("停用 Excel 巨集失敗，為保護電腦已停止匯入：%w", err)
	}
	if err := putExcelProperty(excel, "EnableEvents", false); err != nil {
		return nil, fmt.Errorf("停用 Excel 事件失敗，為保護電腦已停止匯入：%w", err)
	}
	if err := putExcelProperty(excel, "AskToUpdateLinks", false); err != nil {
		return nil, fmt.Errorf("停用 Excel 外部連結提示失敗，為保護電腦已停止匯入：%w", err)
	}

	workbooks, err := dispatchProperty(excel, "Workbooks")
	if err != nil {
		return nil, fmt.Errorf("取得 Excel 活頁簿集合：%w", err)
	}
	defer workbooks.Release()

	// Filename, UpdateLinks, ReadOnly, Format, Password. ReadOnly prevents the
	// imported MO file from being modified even if Excel later reports a warning.
	bookValue, err := oleutil.CallMethod(workbooks, "Open", filepath.Clean(path), 0, true, 5, password)
	if err != nil {
		return nil, fmt.Errorf("Excel 開啟失敗（請確認檔案密碼正確）：%w", err)
	}
	book := bookValue.ToIDispatch()
	if book == nil {
		_ = bookValue.Clear()
		return nil, errors.New("Excel 開啟活頁簿後沒有回傳可讀取物件")
	}
	defer book.Release()
	defer func() {
		if result, closeErr := oleutil.CallMethod(book, "Close", false); result != nil {
			_ = result.Clear()
		} else if err == nil && closeErr != nil {
			err = fmt.Errorf("關閉 Excel 活頁簿：%w", closeErr)
		}
	}()

	worksheets, err := dispatchProperty(book, "Worksheets")
	if err != nil {
		return nil, fmt.Errorf("取得 Excel 工作表集合：%w", err)
	}
	defer worksheets.Release()
	sheet, err := dispatchProperty(worksheets, "Item", 1)
	if err != nil {
		return nil, fmt.Errorf("取得 Excel 第一個工作表：%w", err)
	}
	defer sheet.Release()
	usedRange, err := dispatchProperty(sheet, "UsedRange")
	if err != nil {
		return nil, fmt.Errorf("取得 Excel 已使用範圍：%w", err)
	}
	defer usedRange.Release()

	rowCount, err := rangeDimension(usedRange, "Rows")
	if err != nil {
		return nil, fmt.Errorf("取得 Excel 列數：%w", err)
	}
	columnCount, err := rangeDimension(usedRange, "Columns")
	if err != nil {
		return nil, fmt.Errorf("取得 Excel 欄數：%w", err)
	}
	if rowCount < 1 || columnCount < 1 {
		return nil, errors.New("Excel 沒有可匯入的資料")
	}
	if rowCount > maxExcelRows || columnCount > maxExcelColumns {
		return nil, fmt.Errorf("Excel 使用範圍異常（%d 列 × %d 欄），已停止匯入", rowCount, columnCount)
	}

	cells, err := dispatchProperty(usedRange, "Cells")
	if err != nil {
		return nil, fmt.Errorf("取得 Excel 儲存格集合：%w", err)
	}
	defer cells.Release()

	rows = make([][]string, rowCount)
	for rowIndex := 1; rowIndex <= rowCount; rowIndex++ {
		row := make([]string, columnCount)
		for columnIndex := 1; columnIndex <= columnCount; columnIndex++ {
			cell, cellErr := dispatchProperty(cells, "Item", rowIndex, columnIndex)
			if cellErr != nil {
				return nil, fmt.Errorf("讀取 Excel 第 %d 列第 %d 欄：%w", rowIndex, columnIndex, cellErr)
			}
			row[columnIndex-1], cellErr = excelCellText(cell)
			cell.Release()
			if cellErr != nil {
				return nil, fmt.Errorf("讀取 Excel 第 %d 列第 %d 欄內容：%w", rowIndex, columnIndex, cellErr)
			}
		}
		rows[rowIndex-1] = row
	}
	return rows, nil
}

func putExcelProperty(dispatch *ole.IDispatch, name string, value interface{}) error {
	result, err := oleutil.PutProperty(dispatch, name, value)
	if result != nil {
		_ = result.Clear()
	}
	if err != nil {
		return fmt.Errorf("設定 Excel %s：%w", name, err)
	}
	return nil
}

func dispatchProperty(dispatch *ole.IDispatch, name string, arguments ...interface{}) (*ole.IDispatch, error) {
	value, err := oleutil.GetProperty(dispatch, name, arguments...)
	if err != nil {
		return nil, err
	}
	result := value.ToIDispatch()
	if result == nil {
		_ = value.Clear()
		return nil, fmt.Errorf("Excel %s 沒有回傳物件", name)
	}
	// The returned dispatch owns the COM reference stored in the VARIANT. Its
	// caller releases that reference; clearing the VARIANT here would release it
	// too early and leave the dispatch pointer invalid.
	return result, nil
}

func rangeDimension(usedRange *ole.IDispatch, name string) (int, error) {
	collection, err := dispatchProperty(usedRange, name)
	if err != nil {
		return 0, err
	}
	defer collection.Release()
	value, err := oleutil.GetProperty(collection, "Count")
	if err != nil {
		return 0, err
	}
	defer value.Clear()
	count, ok := variantInteger(value.Value())
	if !ok {
		return 0, fmt.Errorf("Excel %s.Count 格式錯誤", name)
	}
	return count, nil
}

func excelCellText(cell *ole.IDispatch) (string, error) {
	textValue, textErr := oleutil.GetProperty(cell, "Text")
	if textErr == nil {
		text := ""
		if raw := textValue.Value(); raw != nil {
			text = strings.TrimSpace(fmt.Sprint(raw))
		}
		_ = textValue.Clear()
		// Excel displays a row of # when the column is too narrow. Value2 avoids
		// silently importing that display artifact. The same fallback expands
		// scientific notation so a 14-digit MO order number remains unchanged.
		if text != "" && !onlyHashes(text) && !scientificNumber(text) {
			return text, nil
		}
	} else if textValue != nil {
		_ = textValue.Clear()
	}

	value, err := oleutil.GetProperty(cell, "Value2")
	if err != nil {
		if textErr != nil {
			return "", fmt.Errorf("Text: %v；Value2: %w", textErr, err)
		}
		return "", err
	}
	defer value.Clear()
	switch item := value.Value().(type) {
	case nil:
		return "", nil
	case float64:
		return strconv.FormatFloat(item, 'f', -1, 64), nil
	case float32:
		return strconv.FormatFloat(float64(item), 'f', -1, 32), nil
	default:
		return strings.TrimSpace(fmt.Sprint(item)), nil
	}
}

func scientificNumber(value string) bool {
	upper := strings.ToUpper(strings.ReplaceAll(value, ",", ""))
	if !strings.Contains(upper, "E+") && !strings.Contains(upper, "E-") {
		return false
	}
	_, err := strconv.ParseFloat(upper, 64)
	return err == nil
}

func onlyHashes(value string) bool {
	if value == "" {
		return false
	}
	for _, character := range value {
		if character != '#' {
			return false
		}
	}
	return true
}

func variantInteger(value interface{}) (int, bool) {
	switch number := value.(type) {
	case int:
		return number, true
	case int8:
		return int(number), true
	case int16:
		return int(number), true
	case int32:
		return int(number), true
	case int64:
		return int(number), true
	case uint:
		return int(number), true
	case uint8:
		return int(number), true
	case uint16:
		return int(number), true
	case uint32:
		return int(number), true
	case uint64:
		if uint64(int(number)) != number {
			return 0, false
		}
		return int(number), true
	default:
		return 0, false
	}
}
