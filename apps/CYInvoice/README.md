# CYInvoice

CY Windows 10/11 x64 電子發票工具。Public 可見不代表開放原始碼；使用、修改與散布權利以 repository 根目錄 `LICENSE` 為準。

Copyright © 2026 C.C. Liu, Chihyuan Co. All Rights Reserved.

## 版本狀態

- 目前工程測試基準：**V2.6.3 Build 0**。
- 最新公開正式 Release：**CYInvoice V2.4.2**（tag：`cyinvoice-v2.4.2`）。
- V2.6.3 為 V2.6.2 折讓／待辦工程線的後續 Patch，新增系統診斷與折讓專屬自動化測試；仍需 Windows／光貿實機驗證，目前不是正式 Release。
- C#／WinForms 自 V2.0.0 起為唯一正式產品線。
- 唯一版本來源為 `VERSION`；正式 Release 必須由 `main` 重新建置與驗證。
- 只有使用者於當次工作明確要求 `release` 時，才可建立正式 tag／Release；PR、VERSION、BUILD 或 engineering Artifact 都不代表發布授權。
- Go／Win32 V1.1.0 只保留為歷史公開回退版本。

## 現行主要功能

### 發票開立與匯入

- 手動開立。
- MO店+ Excel 匯入。
- 酷澎 DeliveryList 匯入。
- 鼎新 SMART ERP 銷貨單 `.xlsx` 匯入。
- 一般消費者固定含稅；公司統編可切換含稅／未稅。
- 固定精度金額計算、防重複開票、結果不明禁止盲目重送。
- 測試環境使用 CYInvoice 專用 OrderID namespace，並依測試隱私規則去識別化。

### 本機資料與同步

- 主要資料庫：`Data/CYInvoice.db`。
- 光貿官方資料為發票內容權威來源；SQLite 是本機 Cache 加 CYInvoice metadata。
- 程式啟動、每 5 分鐘與手動重新整理共用最近 3 天同步核心。
- 每日本機日第一次自動同步會額外校對目前兩月發票期別及上一期。
- 雙擊任一發票前一律重新執行 `invoice_query`，不以舊 Cache 冒充最新資料。
- 正式環境本機發票 Cache 保留目前及上一個兩月期別；測試環境保留當日。

### 員工與權限

- 4 碼員工編號。
- 角色：超級管理員、管理員、一般使用者。
- 員工密碼至少 8 碼，只接受 ASCII 英文字母與數字。
- 首次使用建立唯一超級管理員；超級管理員支援一次性離線復原碼。
- 帳號管理、密碼變更／重設、啟用／停用均依角色限制。
- MO店+ Excel 密碼與員工／管理員登入分離，不再是首次設定必要欄位；未設定時點 MO店+ 會先提示設定，不開啟選檔視窗。
- 現行不採持續登入；需要權限的操作才進行員工／管理員驗證。

### 發票作廢

- 一般使用者可提出作廢並以員工帳密驗證。
- 直接作廢的 `CancelReason` 格式：`使用者編號-原因`。
- 管理員覆核作廢的 `CancelReason` 格式：`管理員編號-使用者編號-原因`。
- 紙本證明聯尚未收回時，不直接送光貿，改建立人工待辦。
- 作廢結果不明／等待官方確認時不盲目重送。
- 超過目前＋上一個兩月期別的舊 pending，可由管理員在詳細待辦中手動結案；此動作只停止本機追蹤，不代表光貿已完成作廢。

### 折讓（V2.6.x 暫行人工流程）

- 發票詳細資訊可提出人工折讓申請：原因、含稅折讓總額、員工帳密。
- 申請建立後進入「上傳問題」，由管理員至光貿網站人工處理。
- 管理員標記人工操作完成後，CYInvoice 共用 `invoice_query.allowance[]` 回查，不新增第二套折讓查詢流程。
- 系統以提出申請當下的折讓單號基線及含稅金額比對新折讓資料；無法唯一判定時保留待辦，不猜測結案。
- 已完成折讓會出現在發票詳細資訊的「作廢 / 折讓紀錄」。
- 折讓詳細資訊可透過 `/json/allowance_file` 取得官方 PDF；支援 A4、A4 (地址+A5)、A5 三種官方版型，沿用既有版型選擇器與 WebView2 PDF Viewer。
- 折讓 PDF Cache 以官方 `allowance[]` 資料指紋版本化；官方狀態、日期、類型或金額變更後不會誤用舊 PDF。
- 已完成折讓可由一般使用者提出「折讓作廢」人工申請；目前不直接呼叫 `/json/g0501`，由管理員在光貿網站人工完成，再於「上傳問題」結案或取消退回。
- 發票作廢、折讓與折讓作廢的舊待辦超過兩期後，均可由管理員只在本機手動結案。

### PDF 與列印

- 紙本發票支援官方 PDF 預覽、WebView2 檢視與直接列印。
- 公司統編發票支援五種官方發票 PDF 版型。
- 折讓 PDF 支援三種官方版型並使用獨立 `Cache/AllowancePDF`。
- PDF 短效網址不保存；只保存下載後且通過 PDF 驗證的內容。

### 上傳問題

V2.6.2 起整併為單一清單，包含：

- 同步／查詢／解析等技術問題。
- 發票開立失敗紀錄。
- 紙本作廢人工確認。
- 折讓人工處理。
- 折讓作廢人工處理。
- 等待官方確認或可由管理員結案的 pending。

只有「開立失敗」列可勾選清除；人工待辦一律雙擊開啟詳細視窗處理。

### 系統診斷（V2.6.3）

入口：`設定 → 系統診斷`。

診斷頁以唯讀方式顯示：

- 程式、Windows／.NET 與 x64 狀態。
- 目前測試／正式環境與公司／App Key 設定是否完整（不顯示 App Key）。
- 光貿服務連線與目前帳號 API 驗證。
- SQLite quick_check 與本機資料筆數摘要。
- 每日完整同步最後成功時間、最近官方回查時間。
- 未解決待辦／同步問題與開立失敗數量。
- WebView2 Runtime、Excel COM、發票印表機與 Cache 大小。
- 可重新檢查並複製不含密碼、App Key、發票內容與完整本機路徑的診斷摘要。

## 實機驗證重點

- 全新首次設定與超級管理員建立。
- 帳號管理、權限驗證、密碼規則與復原碼。
- MO店+ 未設定 Excel 密碼時不得先開選檔視窗。
- 直接作廢、紙本未收回人工覆核、CancelReason 格式及官方回查。
- 人工折讓建立、管理員處理、`invoice_query.allowance[]` 自動比對。
- 折讓 PDF 三種版型的光貿實際回傳與官方資料變更後 Cache 換版。
- 折讓作廢人工待辦的建立、取消退回與人工完成。
- 單一「上傳問題」清單、開立失敗清除與各類詳細待辦。
- 超過兩期 pending 的管理員結案。
- `設定 → 系統診斷` 的實機資訊、重新檢查與複製摘要。

完整步驟見 [RC／實機測試](docs/RC_TEST.md)。

## 單機版明確不做

以下不是遺漏，而是目前產品範圍決策：

- 本機操作／稽核紀錄：等有雲端資料庫後做跨機稽核。
- 本機備份／還原：CYInvoice 是中介層，發票／折讓官方資料以光貿為準，不另外做使用者備份系統。
- 發票 Excel／CSV 匯出：需要時使用光貿網站；業務單號另回填 ERP。
- 管理員開機待辦提醒：目前沒有持續登入，無法以「誰打開程式」判定管理員身分。

小型營運摘要保留為未來候選，不排入目前版本。

## 尚未完成／延後項目

- 正式折讓 API 自動開立 `/json/g0401`。
- 正式折讓作廢 API `/json/g0501`。
- `allowance_query`／`allowance_status` 等獨立折讓同步模型；現階段仍共用 `invoice_query`。
- 折讓單號自動產生與跨裝置防重。
- 雲端化後的中央員工／權限、Device Token、跨機待辦、防重與操作稽核。
- Email 復原、MO 密碼雲端同步與小型營運摘要等非第一階段功能。
- 酷澎未出貨、公司統編、多商品／多數量、折扣等尚缺可靠實際樣本的格式。

雲端上線需求見 [雲端版上線需求盤點](docs/CLOUD_ROADMAP.md)。詳細待辦見 [docs/TODO.md](docs/TODO.md)。

## 技術基準

- 語言／UI：C#、.NET 10、Windows Forms。
- 平台：Windows 10/11 x64。
- Solution：`CYInvoice.sln`。
- 執行方式：self-contained 可攜資料夾內的 `CYInvoice.exe`。
- WebView2 managed 元件集中於 `Runtime/WebView2`。
- 現行本機主要資料：`Data/CYInvoice.db`；安全設定：`Data/settings.json`。
- SQLite business key 不設過度嚴格 UNIQUE；遇到多筆候選時停止自動更新，不猜測對應。

## 開發與驗證

日常版本與文件變更經 PR 驗證後提供 engineering Artifact。Windows CI 依變更範圍執行 source confidentiality scan、warnings-as-errors build、WinForms startup smoke、核心回歸、作廢／折讓專屬回歸、SQLite migration／retention、invoice sync、sync coordinator、Windows x64 package、PE／layout 與 packaged startup smoke。

正式 Release 只有在使用者當次工作明確要求 `release` 後才執行。

## 目錄

```text
apps/CYInvoice/
├─ src/CYInvoice.Core/                    # 發票、安全、SQLite、同步、員工與作業核心
├─ src/CYInvoice.WinForms/                # Windows Forms 正式 UI
├─ tests/CYInvoice.Core.Tests/            # 核心 parity／回歸
├─ tests/CYInvoice.VoidWorkflow.Tests/    # 員工作廢／折讓人工流程
├─ tests/CYInvoice.Allowance.Tests/       # 折讓 PDF／折讓作廢專屬回歸
├─ tests/CYInvoice.SqliteMigration.Tests/ # SQLite migration／retention／員工 schema
├─ tests/CYInvoice.Sync.Tests/            # AMEGO 同步與 sync issue
├─ tests/CYInvoice.SyncCoordinator.Tests/ # 啟動／排程／手動同步協調
├─ assets/
├─ scripts/
└─ docs/
```

## 文件

- [功能基準](docs/REQUIREMENTS.md)
- [CYInvoice 永久規則](PROJECT_RULES.md)
- [待辦與後續規劃](docs/TODO.md)
- [雲端版上線需求盤點](docs/CLOUD_ROADMAP.md)
- [RC／實機測試](docs/RC_TEST.md)
- [本機資料格式與安全規則](docs/DATA_FORMAT.md)
- [V2 遷移紀錄](docs/MIGRATION_HISTORY.md)
- [歷史版本紀錄](CHANGELOG.md)
