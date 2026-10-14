# CYInvoice → CYERPAutoInput 介接工作交接（2026-10-14）

狀態：**尚未交付可用開票接口**；本分支目前完成 request wire-contract parser、CLI arguments parser 與原子結果檔 writer（均屬底層元件，尚未接入執行入口）。不得將本文件解讀為 AutoInput 現在可以呼叫 CYInvoice 開立發票。

## 來源與範圍
- 需求母本：`apps/CYERPAutoInput/docs/CYINVOICE_INTERFACE_HANDOFF.md`（AutoInput `cyerp-auto-input/shopee-batch` 分支）。
- CYInvoice 本次獨立工作分支：`cyinvoice/feat-autoinput-interface`。與 V3 recovery Draft #440 分開。
- 無 Cloudflare／D1／CYID 變更；不得用真實光貿開票驗收。
- 公開源碼不能含真實訂單、員工、API Key、路徑或正式資料。

## 本次已加入
- `src/CYInvoice.Core/AutoInputContract.cs`：定義 `cyinvoice-autoinput-request/1`、`cyinvoice-autoinput-result/1` 名稱及基本 record；驗證 JSON 頂層、schema、GUID、來源、絕對輸入路徑、選擇性 orderIds（限制數量、重複和空白）。
- `src/CYInvoice.Core/AutoInputCommandLine.cs`：驗證 `--autoinput-issue|--autoinput-query <request> --result <result>` 參數、絕對路徑及 request/result 不可相同。\n- `src/CYInvoice.Core/AutoInputResultFile.cs`：結果 envelope/status 驗證、暫存檔寫入後同目錄原子更名；拒絕覆蓋既存結果。\n- 尚未寫入 `Program.cs`，也尚未提供 IPC、開票或查詢。因此 **不可由 AutoInput 使用**。
- 本次工作尚無本地 Windows 編譯／CI 成功證據，不能標示測試通過。

## AutoInput 已定案的呼叫契約（待 CYInvoice 落地）
```text
CYInvoice.exe --autoinput-issue <request.json> --result <result.json>
CYInvoice.exe --autoinput-query <request.json> --result <result.json>
```
來源：`mo`、`coupang`。輸入由 CYInvoice 現有 importer 解析；回覆逐筆 `issued|existing|failed|unknown|skipped`，包含發票號碼、日期、買方資料及總金額。只有既有開票 owner 可以打光貿、寫本地資料；已開立不能重新開立，結果不明不得自動重送。查詢必須唯讀、不開立。輸出先寫同目錄暫存檔再原子更名。AutoInput 在正式 ERP 僅接受 `environment=production`。

## 完成接口前必要工作
1. 核對 `InvoiceService.IssueMoAsync`／`IssueCoupangAsync`、既有匯入入口、有效本機設定及官方結果回查，確立 source+OrderId+environment 防重複的一致性；不能直接使用 parser 建立新的開票分支。
2. 在 `Program.cs` 接入命令列，禁止主視窗和任何 modal prompt；重設／撤銷／初始化／連線阻擋時回狀態而不是掛住。
3. 單資料夾單寫入者：既有 Mutex `Local\\CYInvoice-<directoryKey>`；若主視窗已開啟，由同一 Windows 使用者的受保護 IPC 交給原行程處理，不開第二個 SQLite writer，不可跨資料夾誤連。
4. 硬性綁定來源、訂單、正式／測試環境，阻止同 requestId 多次執行造成重送；lost response、逾時、官方返回不明都必須走查回或維持 unknown。
5. 測試：合成訂單檔、非法 JSON/檔案、同筆重跑、query 唯讀、結果不明、local-save 失敗、已開／未開主程式、IPC ACL、多筆部分失敗、reset 中、環境切換、正式／測試資料隔離；再做 Windows CI 與手動實機驗收。
6. 實作完整後更新本文件（可用版本、精確 commit、CI run、剩餘限制）並通知 AutoInput owner；**不得在完成前宣告介面 ready**。

## AutoInput 端目前應保持
現行 MO店+ 預覽手動補發票號碼／日期的過渡流程不應移除，`--autoinput-issue` 不得提前啟用；暫存檔若含需人工處理訂單須保留。AutoInput 呼叫逾時只能停止等待、之後 query 查回，不得連續重試 issue。
