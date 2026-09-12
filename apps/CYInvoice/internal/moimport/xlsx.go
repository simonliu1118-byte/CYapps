package moimport

import (
	"archive/zip"
	"encoding/xml"
	"errors"
	"fmt"
	"io"
	"path"
	"strconv"
	"strings"
)

type sharedStringTable struct { Items []sharedString `xml:"si"` }
type sharedString struct { Text string `xml:"t"`; Runs []struct { Text string `xml:"t"` } `xml:"r"` }
type worksheet struct { Rows []sheetRow `xml:"sheetData>row"` }
type sheetRow struct { Cells []sheetCell `xml:"c"` }
type sheetCell struct { Reference string `xml:"r,attr"`; Type string `xml:"t,attr"`; Value string `xml:"v"`; Inline string `xml:"is>t"` }
type workbookManifest struct { Sheets []struct { RelationshipID string `xml:"id,attr"` } `xml:"sheets>sheet"` }
type workbookRelationships struct { Relationships []struct { ID string `xml:"Id,attr"`; Target string `xml:"Target,attr"` } `xml:"Relationship"` }

func ReadXLSX(path string) ([][]string, error) {
	book, err := zip.OpenReader(path)
	if err != nil { return nil, fmt.Errorf("開啟 xlsx：%w", err) }
	defer book.Close()
	shared := []string{}
	if file := zipFile(book.File, "xl/sharedStrings.xml"); file != nil {
		data, err := readZipFile(file); if err != nil { return nil, err }
		var table sharedStringTable
		if err := xml.Unmarshal(data, &table); err != nil { return nil, fmt.Errorf("讀取 xlsx 共用文字：%w", err) }
		for _, item := range table.Items { var value strings.Builder; value.WriteString(item.Text); for _, run := range item.Runs { value.WriteString(run.Text) }; shared = append(shared, value.String()) }
	}
	sheetPath, err := firstWorksheetPath(book.File)
	if err != nil { return nil, err }
	sheetFile := zipFile(book.File, sheetPath)
	if sheetFile == nil { return nil, errors.New("xlsx 找不到第一個工作表") }
	data, err := readZipFile(sheetFile); if err != nil { return nil, err }
	var sheet worksheet
	if err := xml.Unmarshal(data, &sheet); err != nil { return nil, fmt.Errorf("讀取 xlsx 工作表：%w", err) }
	rows := make([][]string, 0, len(sheet.Rows))
	if len(sheet.Rows) > maxExcelRows { return nil, fmt.Errorf("xlsx 超過 %d 列，已停止匯入", maxExcelRows) }
	for _, source := range sheet.Rows {
		row := []string{}
		for _, cell := range source.Cells {
			column := columnIndex(cell.Reference)
			if column >= maxExcelColumns { return nil, fmt.Errorf("xlsx 超過 %d 欄，已停止匯入", maxExcelColumns) }
			for len(row) <= column { row = append(row, "") }
			value := cell.Value
			switch cell.Type {
			case "s":
				index, conversionErr := strconv.Atoi(value); if conversionErr != nil || index < 0 || index >= len(shared) { return nil, fmt.Errorf("xlsx 共用文字索引錯誤：%s", value) }; value = shared[index]
			case "inlineStr": value = cell.Inline
			}
			row[column] = value
		}
		rows = append(rows, row)
	}
	return rows, nil
}

func zipFile(files []*zip.File, name string) *zip.File { for _, file := range files { if file.Name == name { return file } }; return nil }
func readZipFile(file *zip.File) ([]byte, error) {
	reader, err := file.Open(); if err != nil { return nil, err }; defer reader.Close()
	const limit = 32 << 20
	data, err := io.ReadAll(io.LimitReader(reader, limit+1))
	if err != nil { return nil, err }
	if len(data) > limit { return nil, fmt.Errorf("xlsx 內部檔案 %s 超過 32 MB，已停止匯入", file.Name) }
	return data, nil
}

func firstWorksheetPath(files []*zip.File) (string, error) {
	manifestFile := zipFile(files, "xl/workbook.xml")
	relationsFile := zipFile(files, "xl/_rels/workbook.xml.rels")
	if manifestFile == nil && relationsFile == nil {
		// Keep compatibility with minimal test workbooks while real Office files
		// are resolved through their relationship table below.
		return "xl/worksheets/sheet1.xml", nil
	}
	if manifestFile == nil || relationsFile == nil { return "", errors.New("xlsx 工作表關聯資料不完整") }
	manifestData, err := readZipFile(manifestFile); if err != nil { return "", err }
	relationsData, err := readZipFile(relationsFile); if err != nil { return "", err }
	var manifest workbookManifest
	var relations workbookRelationships
	if err := xml.Unmarshal(manifestData, &manifest); err != nil { return "", fmt.Errorf("讀取 xlsx 工作表清單：%w", err) }
	if err := xml.Unmarshal(relationsData, &relations); err != nil { return "", fmt.Errorf("讀取 xlsx 工作表關聯：%w", err) }
	if len(manifest.Sheets) == 0 || manifest.Sheets[0].RelationshipID == "" { return "", errors.New("xlsx 找不到第一個工作表關聯") }
	for _, relationship := range relations.Relationships {
		if relationship.ID != manifest.Sheets[0].RelationshipID { continue }
		target := strings.ReplaceAll(strings.TrimSpace(relationship.Target), "\\", "/")
		if strings.HasPrefix(target, "/") { target = strings.TrimPrefix(target, "/") } else { target = path.Join("xl", target) }
		if !strings.HasPrefix(target, "xl/worksheets/") { return "", errors.New("xlsx 第一個工作表路徑不安全") }
		return target, nil
	}
	return "", errors.New("xlsx 找不到第一個工作表檔案")
}
func columnIndex(reference string) int { result := 0; found := false; for _, char := range reference { if char < 'A' || char > 'Z' { break }; found = true; result = result*26+int(char-'A'+1) }; if !found { return 0 }; return result-1 }
