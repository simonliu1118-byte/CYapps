# CYInvoice 原始碼復原計畫

## 原則

不從 V1.1.2 的失敗執行檔繼續修補 PE，也不把舊版「三聯式發票開立計算機」原始碼混入 CYInvoice。以 V1.0.0 可正常執行版本作為行為比對基準，重新建立可重複建置的 Go 原始碼。

## 階段

### 1. 建置骨架

- 建立 Go module、Windows x64 程式進入點及明確的版本常數。
- 將 INV icon 與 amd64 manifest 保留為原始資源。
- 建立不破壞 Go 原生 PE 結構的資源嵌入方式。
- 建立單一 PowerShell 建置／封裝腳本，輸出固定 `CYInvoice` 資料夾。
- 啟動時立即建立一般 LOG；例外及計算/API 錯誤另寫 ERROR LOG。

### 2. 重建 V1.0.0 核心行為

- 手動開立。
- MO店+ 匯入與批次確認。
- 已開立發票清單、狀態回查與防重複開票。
- 本機設定及資料格式相容性。
- Windows 實機 UI、icon、manifest 與長時間操作驗證。

### 3. 逐項加入 V1.1 功能

每一項使用獨立分支與 PR，避免再次形成無法定位問題的大型失敗線：

1. 紙本 PDF API 與 Cache。
2. 內嵌 PDF Viewer、下載與列印。
3. 公司統編五種 PDF 版型。
4. 會員載具成功資訊與模擬檢視。
5. 員工權限及作廢流程。
6. 網路逾時、OrderId 回查及結果不明鎖定。
7. MO店+ 加密 Excel COM 流程與實際樣本驗證。

### 4. 正式發行

- 完成 Windows 10/11 x64 實機驗證。
- 由 Git commit 建置正式 ZIP。
- 產生 SHA-256、建立 Git 標籤與 GitHub Release。
- 發行後的任何修正都從對應標籤建立分支，不直接修改舊發行檔。

## 參考發行包雜湊

- `CYInvoice_V1.0.0.zip`: `ffc86d6cbf01eb70d3a5edf28cba192b26bedfaa5b5f66822f10abdce53046b9`
- `CYInvoice_V1.1.2.zip`: `58ba12a7175bb77debd3ba19b0459888f2d90df0b23b3f6ba216ef1e324e2898`

這兩個 ZIP 只作為外部行為及歷史比對材料，不提交至 Git。

