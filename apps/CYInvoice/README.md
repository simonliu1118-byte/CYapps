# CYInvoice


CY Windows 10/11 x64 電子發票工具。Public 可見不代表開放原始碼；使用、修改與散布權利以 repository 根目錄 `LICENSE` 為準。

Copyright © 2026 C.C. Liu, Chihyuan Co. All Rights Reserved.

## 版本狀態

- 目前工程：**V3.1.50／BUILD6、Cloud Source0.9.15待部署**；提出者可撤回自己的待核准折讓，主管保留核准及退回。清單顯示帳號、姓名與原因；兩個商品表有序號、原因列入審核資訊；啟動圈圈與文字放大並修正轉動，退回原因單行20字，確認退回後立即恢復可折讓。詳見 [V3.2計畫](docs/V3_2_IMPLEMENTATION_PLAN.md)。工程包不是正式Release。
- 原Worker：**Cloud0.9.14／API1／schema與marker23／CYID_ONLY**。2026-11-05已由本機MCP部署為100%流量；平台版本ID、線上程式SHA、原資料／D1／production CYID／Secrets／scope／Routes／旗標前後核對通過。固定程式Source為 `fb381f5140f92e240f1c405f5ad38ad5a3a5efc9`；文件提交不取代部署Source。
- 正式Release：**[V3.1.0](https://github.com/simonliu1118-byte/CYapps/releases/tag/cyinvoice-v3.1.0)**。本次未建立新tag／Release或覆寫桌面安裝。
- 同機折讓與作廢的一次主管核准、自動處理及完整詳情已實作；完整真人業務驗收尚未全部完成。跨機協同依現行計畫留下一版，雙Windows真人驗收依既有延期決定追蹤。
- 版本歷史只在 [CHANGELOG](CHANGELOG.md) 保存；目前工程待辦見 [TODO](docs/TODO.md)、流程見 [V3.2計畫](docs/V3_2_IMPLEMENTATION_PLAN.md)、維護完成證據見 [MCP交接](docs/V3_2_C_CLOUDFLARE_MCP_HANDOFF.md)。

## 現行主要功能

### 發票開立與匯入

- 同訂單再次送出仍由光貿判定；明確拒絕保存為開立失敗，只在「上傳問題」顯示。官方同步及詳細回查不把失敗嘗試改成已開立。

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
- Local 密碼至少 8 碼 ASCII 英數；CYID 使用 canonical 的 8～16 個 Unicode code points，完成首次 Email 驗證後才可使用。新版不提供 Built-in 現役模式。
- Local 首次使用建立唯一超級管理員；超級管理員支援一次性離線復原碼。
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

### 折讓與折讓作廢（目前先完成同機流程）

- 使用者從原發票或折讓提出申請，核對商品、金額、原因並驗證員工帳密；成功後進入原待辦等待主管核准。
- 主管一次核准後由原coordinator連續配號／送交光貿／原號查回。核准前可填原因取消退回；核准後移除取消退回與再次送出操作，不能用第二次密碼或手動送出繞過原狀態。
- 正常等待主管／官方處理留在「處理中」；真正異常留「上傳問題」。結果不明沿原ID／原號查回，不清Claim、換申請或盲目重送。財務送出／不明案件不能以一般舊待辦結案功能偽裝完成。
- 歷程同張折讓單顯示一列；完整詳情保留原申請員工、覆核主管、原因、商品與執行事件，缺舊資料不補造人員。官方PDF與版本化Cache沿原查詢／檢視器。
- 完整授權、例外及驗收見 [唯一V3.2計畫](docs/V3_2_IMPLEMENTATION_PLAN.md)；本次維護未執行真光貿交易，部署成功不等於所有真人流程驗收通過。

### PDF 與列印

- 紙本發票支援官方 PDF 預覽、WebView2 檢視與直接列印。
- 公司統編發票支援五種官方發票 PDF 版型。
- 折讓 PDF 支援三種官方版型並使用獨立 `Cache/AllowancePDF`。
- PDF 短效網址不保存；只保存下載後且通過 PDF 驗證的內容。

### 上傳問題與處理中

V2.6.11 將正常等待拆到「處理中」；「上傳問題」仍以單一表顯示：

- 同步／查詢／解析等技術問題。
- 發票開立失敗紀錄。
- 紙本作廢人工確認。
- 折讓處理中的異常。
- 折讓作廢處理中的異常。
- 結果不明、回查失敗或需人工判定的 pending；正常等待顯示在「處理中」。

只有「開立失敗」列可勾選清除；人工待辦一律雙擊開啟詳細視窗處理。

「上傳問題」旁的「處理中」沿用相同清單與雙擊明細，顯示正常上傳等待、光貿明確確認處理中的作廢，以及人工操作完成且正常等待官方確認的折讓。資料由現有發票狀態與待辦投影，沒有第二套 pending 資料庫。完成後從處理中移除；回查失敗、折讓金額／候選衝突仍保留在上傳問題。處理中項目不能勾選刪除或直接標記成功；一般舊待辦的本機結案不代表官方成功；財務已送出／不明折讓及作廢不適用人工結案。舊折讓未保存明確確認分類時，先留在上傳問題，經正常回查後再分類。

### Local／CYID Cloud 與裝置復原

- CYInvoice自行管理Workspace／Device及配對／邀請；中央Employee／帳密／App Access由CYWEB／CYID管理，舊Built-in Employee Transition不屬V3現役操作。
- 本版提供 Local／CYID Cloud；正式 role 為 SUPER_ADMIN / ADMIN / USER。歷史 Built-in／Self-hosted 原版本保留，新版不以舊帳號降級放行。
- Online 權限操作取最新中央 authority；只有真正傳輸斷線／timeout 使用最後可信 protected offline cache。
- 裝置管理支援 inventory／revoke，保留歷史與最後一台 active Device 防護；Cloud → Local 使用雙重確認及 crash-safe 本機重置。
- 本版配套後端為 **Cloud0.9.14／API1／legacy相容標記8／storage marker23**；實際D1 migration與平台版本由原部署owner核對，健康檢查不再回報自填APP_ENV。詳見 [維護交接](docs/V3_2_C_CLOUDFLARE_MCP_HANDOFF.md)。
- 單一正式公司卡由CYInvoice超管管理；授權裝置沿原登入／重連／選卡／批次作業前版本檢查同步，憑證在雲端加密、本機DPAPI保存，不新增定期輪詢。
- CYWEB 超管可重新驗證後簽發短效裝置復原邀請；新裝置接管原公司成功時，才撤銷所有其他舊裝置。Pending Token／scope 與遺失回覆讀回機制避免盲目重簽或重建裝置身分。
- 本版採隔離／虛擬機軟體驗收，未將真實寄信或實際異機還原標為通過。跨機 Work Item／審核、正式折讓 API 與後續項目留待 3.1／3.2／3.3；不合併 PR #440。完整內容與限制見 [V3.0.0 發行摘要](release-notes/V3.0.0.txt)。

### 問題診斷

原「系統診斷／匯出診斷」獨立介面已退役。例外與Cloud執行事件沿原IssueDiagnosticLog寫入程式同目錄log，使用原設定／同步／待辦的同一資料owner；不新增診斷視窗或背景輪詢。帳密、Token、金鑰與原始交易內容不寫入診斷。

## 實機驗證重點

- 全新首次設定與超級管理員建立。
- 帳號管理、權限驗證、密碼規則與復原碼。
- MO店+ 未設定 Excel 密碼時不得先開選檔視窗。
- 直接作廢、紙本未收回人工覆核、CancelReason 格式及官方回查。
- 同機折讓申請、主管一次核准、自動處理、原號查回與完整詳情。
- 折讓 PDF 三種版型的光貿實際回傳與官方資料變更後 Cache 換版。
- 折讓作廢核准前取消退回、核准後自動處理及未知結果只查回。
- 「上傳問題／處理中」分流、數量、開立失敗清除、正常等待轉完成／錯誤與各類詳細待辦。
- 超過兩期 pending 的管理員結案。
- `設定 → 系統診斷` 的實機資訊、重新檢查與複製摘要。

完整步驟見 [RC／實機測試](docs/RC_TEST.md)。

## 單機版明確不做

以下不是遺漏，而是目前產品範圍決策：

- 不另建第二套本機財務／稽核權威；現有SQLite歷史與log診斷保留。
- 本機備份／還原：CYInvoice 是中介層，發票／折讓官方資料以光貿為準，不另外做使用者備份系統。
- 發票 Excel／CSV 匯出：需要時使用光貿網站；業務單號另回填 ERP。
- 管理員開機待辦提醒：目前沒有持續登入，無法以「誰打開程式」判定管理員身分。

小型營運摘要保留為未來候選，不排入目前版本。

## 尚未完成／延後項目

- 同機真人業務完整驗收、跨機協同下一版及既有雙Windows延期項目，依 [TODO](docs/TODO.md) 與 [唯一V3.2計畫](docs/V3_2_IMPLEMENTATION_PLAN.md) 追蹤；不能把Source／CI／部署通過當成真人全部完成。
- V3.0／V3.1已批准延期的Email、異機金鑰／復原、角色及DPI證據，只在 [發行後待驗收](docs/POST_RELEASE_ACCEPTANCE.md) 勾選。
- 酷澎特殊樣本、一般跨機OrderID／Audit、營運摘要、對外整合與多公司等原定後續需求保留於TODO／長期路線圖，不在README維護第二張進度表。

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
├─ tests/CYInvoice.Cloud.Tests/          # Cloud／identity／device／reset contract
├─ cloud/                               # Reference Worker／D1 forward migrations
├─ assets/
├─ scripts/
└─ docs/
```

## 文件

- [現行 V3.1 原始碼交接](docs/V3_1_HANDOFF.md)
- [Cloud 工程狀態](docs/CLOUD_ARCHITECTURE_STATUS.md)
- [CY ID 整合差異](docs/CY_ID_INTEGRATION.md)
- [功能基準](docs/REQUIREMENTS.md)
- [CYInvoice 永久規則](PROJECT_RULES.md)
- [待辦與後續規劃](docs/TODO.md)
- [CYID 整合現況與歷史接入交接](docs/CY_ID_INTEGRATION.md)
- [雲端版上線需求盤點](docs/CLOUD_ROADMAP.md)
- [RC／實機測試](docs/RC_TEST.md)
- [本機資料格式與安全規則](docs/DATA_FORMAT.md)
- [V2 遷移紀錄](docs/MIGRATION_HISTORY.md)
- [歷史版本紀錄](CHANGELOG.md)
