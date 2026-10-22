# CYInvoice 版本紀錄

本檔保存可確認的歷史事實。正式 Git 標籤只會從「原始碼可重建、Windows 驗證通過」的版本建立；日常工程版本不因 VERSION／BUILD 推進而自動成為正式 Release。

## V3.1.39 Build 0 — 2026/10/22（V3.2-F：官方折讓狀態歷程整合）

- 將官方 invoice_query 折讓狀態每次**實際變更**寫入原本 CYInvoice SQLite 內不可 UPDATE／DELETE 的 `official_allowance_observations`，與發票本機更新置於同一交易；同一狀態反覆查詢不重複新增，作廢／回復等變更各保留一筆。僅完整官方折讓摘要可新增稽核，不能從資料缺失推斷已作廢，也不能證明本程式發送了交易。
- 原發票詳情「操作歷程」可唯讀查詢每次官方狀態、查證時間及原號；歷程不含買受人個資。既有發票清理機制、正常保留期與刪除完全不修改；縱然舊原發票被正常清理，獨立歷程仍保存在同一資料庫，舊版本資料不能被事後憑空補造。
- 近期新折讓、舊折讓作廢輪巡、跨期缺原發票按號唯讀補回、每日上限提示皆沿用先前已整合的程式。查證失敗改在既有「上傳問題」保留至成功唯讀查回，移除兩次重複 ApplyQuery。隔離測試涵蓋新舊歷程、不重複、恢復與原發票正常清理後歷程仍留存。
- **Source 範圍完成，非真環境驗收完成**：官方 API 真實回覆、CYID/D1 雲端正式同步、Local→Cloud 權威切換與真正跨機測試仍需使用者授權的環境與 H 驗收。沒有建立 Cloud D1/Worker、啟用 g0501、正式 Release 或修改旧發票清理政策。

## V3.1.38 Build 0 — 2026/10/22（V3.2-F：缺失原發票按需查回）

- 按使用者明確要求：不修改舊發票清理、跨期清理週期或長期保留機制。只有折讓清單關聯的原發票在本機完全缺失時，才用原發票號碼執行既有唯讀 `invoice_query`，證實原號、狀態、OrderID、必要內容正確後補成本機官方同步來源紀錄；測試環境保留日期前綴隔離。
- 光貿回應查無（71）時在原「上傳問題」顯示 **查無原發票** 並留待後續確認；回傳另一發票、資料不完整或本機號碼／OrderID 衝突亦不自動補造。日後官方成功查回可自動解除該異常。新增按需補回、查無後恢復及錯號拒絕隔離測試；每日折讓清單 150 筆／原發票 50 張上限不變，無新資料庫／新 HTTP 路由／正式交易或部署。

## V3.1.37 Build 4 — 2026/10/22（V3.2-F 查詢量與舊折讓作廢輪巡）

- Build 4：修正容量提醒回歸案例的測試順序：超限後每日成功標記仍空；恢復正常查詢並清除提醒後才應有成功標記。僅測試斷言調整。
- Build 3：依新設計修正既有折讓餘額隔離測試：官方完全沒回 allowance[] 時保留上次觀察 1 筆，完整性 false 仍禁止送出；只改測試預期，未放寬授權安全。
- Build 2：修正舊人工折讓覆核回歸：`allowance[]` 有資料但官方未提供完整性標記時，保留這次回傳的新增明細供人工流程核對；只有真正沒有明細的回覆才暫存上次觀察。任何摘要不完整仍不得作為 V3.2 金額授權依據。
- Build 1：修正 .NET 編譯時 `InvalidDataException` 為 sealed、不可作為專用超限例外的基底類別；改由 `IOException` 衍生，保留可識別的超限處理與原上傳問題提示；並正確重設本版 BUILD，不改營運邏輯。
- 依使用者要求將每日折讓關聯原發票上限由 20 提升到 **50 張**，折讓列表提升到 **每頁 50、最多 3 頁／150 筆**；遇任一上限寫入現有「上傳問題」專屬類型，明確提示回來調整程式上限，不寫每日完成標記；後續正常查證成功才解決提醒，不新增介面或資料表。
- 沿既有 `invoice_query` 與本機官方折讓清單，以當日未用完的 50 張額度重新查證**本機保留的既有折讓**，依最久未核對先查，涵蓋舊折讓今日才作廢而不在建立日期列表的情境；原號與金額仍以官方資料為準、不推測。
- 官方不回完整 allowance[] 時保留先前觀察供未來輪巡，但完整性設為 false，絕不可當可折讓餘額。補 50 張／150 筆容量、超限提醒、成功解決及舊折讓作廢合成測試。不操作正式財務服務、D1、Cloud Worker 或 Release。

## V3.1.36 Build 2 — 2026/10/22（V3.2-F：官方新折讓低頻查證）

- 沿既有 AMEGO Gateway 增加唯讀 allowance_list（date_select=2 建立日期）及有界分頁，正式日間完整同步後再查近三日新折讓，最多三頁／20 張關聯發票，以原 InvoiceDetailRefreshService 的 invoice_query 更新本機已存在的唯一原發票及 PDF／預覽快取。複數原發票可分別回查；未對應／異常、不完整、逾上限均保守停止或列出待核對項目，不推測官方狀態、不送出折讓／作廢請求。
- 新增舊日期已留存發票被新折讓觸發的隔離回歸，確認同日不重複執行 allowance_list，並檢查超量不查、不寫入每日完成標記；移除早先在 InvoiceOfficialState 以外的重複 ApplyQuery。舊折讓今日作廢、保留期外完整歷史、G 退役仍屬後續工作。雙 Windows 真人驗收維持 V3.2 上線後補做，正式 Release V3.1.0 不變。

## V3.1.35 Build 0 — 2026/10/22（V3.2-F：官方折讓同步基礎）

- Build 2：第二輪 Windows CI 於既有 Coordinator 測試發現兩個其他 Fake Gateway 未實作新唯讀 allowance_list；只補這兩個隔離替身為空列表，不修改正式產品邏輯。保留所有原情境測試。
- Build 1：首輪 Windows CI 發現同步測試誤引用跨 assembly 不可見的 internal scope 常數；測試改直接使用既有固定 scope 字串，不修改正式同步邏輯。
- 在原有 invoice_list／invoice_query 與單筆詳情服務中，安全寫回官方 allowance[] 明細及完整性，並納入變更比對；官方折讓異動時失效舊 PDF／預覽快取。缺失的 allowance[] 不視為零折讓，不能放行折讓金額。新增隔離回歸測試，無額外 API 呼叫、資料表、Worker 路由或真交易。
- 使用者已將 V3.2 雙 Windows 真人驗收延至正式上線後，仍標示未驗收；財務送出及 Local→Cloud 單一權威的安全條件照舊。Cloud g0501 尚未啟用，正式 Release 仍 V3.1.0。

## V3.1.34 Build 0 — 2026/10/21（V3.2 D/E：折讓作廢整合工程）

- **D／E 雙裝置整合合成驗收**：沿用現有 CYID HTTP 路由及 SQLite Fixture：A 裝置建立 Cloud 作廢申請後模擬遺失回覆，B 裝置依同原 ID 唯讀查回且不重建；主管 B 裝置撤權不得預覽、公司卡版本更動不得核准；錯環境拒絕，同時確認員工 A 裝置仍可查狀態、官方 g0501 POST 仍為零。WinForms 修正已核准申請回線時的狀態說明，不再誤顯示「待主管覆核」。本輪僅 Source／合成測試，真實兩台 Windows 與光貿測試公司卡仍需獨立驗收。

- **安全驗證與測試小幅整理**：將 g0501 官方證據的 60 秒時效／未來 1 秒容差，以及公司卡版次／統編／App Key 一致性，統一由原官方查核模組提供純判斷函式；主管預覽、核准、單次送出和回線唯讀查回仍在原本時間點分別執行 CYID／Device 及公司卡二次檢查，不放寬任何 CAS／永久 Claim。把四個查回情境重複的「不可自動重送、結案或歸因」斷言整理為同一測試輔助，補邊界測試；無新增資料表、Worker 路由、交易或部署。

- **本輪精簡**：刪除不能證明離線設備已停送的 D1 Cloud-exclusive Gate、交接雜湊欄位及其 3 個 Trigger；單次 Cloud g0501 送出器改由獨立明確授權參數**預設 false**，且依舊沒有可呼叫它的 Worker HTTP 路由。主管核准仍可於未切換送出權威前完成；真正送出必須另經 Local 停送核對及授權，D1 永久單次 Claim、角色／Device／公司卡二次檢查、官方證據及不可變 Audit 全保留。Cloud 主管／送出器的 CYID 權限檢查整併為同一 owner，減少權限規則分岔；合成測試改覆蓋預設關閉、Claim 前／後撤權與結果不明禁止重發。未對 Cloudflare 或 AMEGO 操作。

- 員工端沿用現有 CYID 跨機折讓 WinForms 視窗，新增原號／原發票／原因的 `void-create` 申請操作；同 Workspace、環境、官方原折讓號產生固定申請 ID，斷線、重新開啟與跨 Device 查回皆沿用原 ID。Windows Client 僅在認證的原 ID `NOT_FOUND` 時建立一次申請，其餘逾時、權限撤銷、結果不明均不補發。Fake HTTP 覆蓋已有申請直接讀取、首次建立、建立回覆遺失後回線查回、503／撤權拒絕等；仍不開 g0501 送出路由。

- 本輪 CYID 跨機作廢主管功能沿用既有 Worker `allowances-route` 與 WinForms `CloudAllowanceManagerForm`：新增 `void-preview`（原申請 ID 下主管身分、原號、原發票、原因與官方雙查唯讀）及 `void-approve`（再驗 CYID／Device、完整原號確認、原子 Grant）；Windows Client 不接受回應暗示可發送或官方成功。原視窗加入預覽、核准與原 ID 查回，無新送出入口。保留 schema20 草案、獨立受控切換與預設關閉旗標；合成測試涵蓋越權、無主管權限拒絕、單次核准、回覆語意與零 g0501。

- 沿既有 CYID Device／員工認證、allowances-route 與 Windows Cloud Client 新增 `void-create`／`void-status`（僅 schema20＋原旗標允許），跨機建立固定申請 ID 與唯讀查回；無 g0501 送出入口。包含越權、重複、未就緒與 Windows 契約測試。

- **Local**：同一公司／環境＋原折讓號共用永久 g0501 Claim；跨待辦 ID、結果不明及回線重入均不得第二次送出。曾參與 CYID Cloud 的裝置不得僅切回 LocalOnly 即恢復本機 g0501。
- **CYID Cloud（未對外開放）**：原申請 ID、加密 Frozen、主管重新驗權與官方原號／原發票唯讀驗證、原子核准、永久唯一 Claim、最多一次 g0501、結果不明時原 ID 唯讀查回；保留不可變稽核。
- **簡化**：移除無法證實實際停機的 Local 設備快照與 Cloud 清冊預檢兩條平行路徑。Cloud g0501 協調器一律預設禁送；舊 Local 裝置停送與舊交易核對在正式切換前人工確認，不另建日常盤點平台。
- **驗證／限制**：SQLite／Fake HTTP／Windows CI 覆蓋單次送出、撤權、斷線與回覆遺失；schema20 僅 draft，無 g0501 Worker 路由或真實 AMEGO 操作。真雙機／Local 切 Cloud、正式交易歸因與受控升級尚待驗收；正式 Release 仍 V3.1.0。

## V3.1.33 Build 0 — 2026/10/21（V3.2-E：Local 折讓作廢）

- 既有折讓作廢待辦新增 Local 主管即時官方預覽、15 分鐘凍結核准及同一主管重新登入／輸入原號後單次 g0501。每次核准、送出均查官方原折讓明細與 allowance_status；買方、原發票、公司設定、申請及主管權限變更即拒絕。跨多張原發票的折讓目前拒絕由單張發票入口整張作廢。
- 原 SQLite 保存 append-only 核准、公司／環境／折讓號唯一送出 claim 及具操作者的結果觀察。先 durable claim 再 POST，code 15、斷線、回覆遺失、程序重入或結果稽核寫入失敗都不釋放 claim；併發最多一個 g0501。送出後只能原號查回，取消、網站人工結案、行政結案與直接清除待辦皆受阻擋。
- allowance_status 只把原號 D0501／99 記為「官方已作廢但本次 POST 無法歸因」；不把 code=0、D0501／91 或未知資料視為交易成功，不自動結案。真實交易歸因／人工處置政策及 Local 實機驗收仍未完成。既有網站人工流程在尚未送出 API 時保留，未提前完成 G 舊流程退役。
- 合成測試涵蓋官方簽名與零重試、錯號／錯買方／錯原發票／未知狀態、預覽變更、主管撤權、核准過期、申請改動、帳號範圍、取消／併發競態、重啟不重送、append-only 與稽核失敗。沿用既有 workflow service／SQLite／待辦畫面，無新背景送出 owner。
- 本批是獨立 Local 作廢工程功能，Patch 升為 3.1.33、Build 歸零；CYID g0501、D 離線／回線、遠端 schema19 升級及真交易仍待完成。Cloudflare 讀取／寫入／部署均 0，真光貿呼叫 0；不變更 Cloud schema、旗標或正式 Release（仍 V3.1.0）。

## V3.1.32 Build 0 — 2026/10/21（V3.2 C：schema19 正式來源與受控部署準備）

- 將 0019 人工稽核／外部占號證據的唯一來源從 drafts 移至 canonical migrations；既有 0001～0018 完全不變。新增 1 欄位、1 表、3 索引及 4 觸發器，原 Workspace／Device／身份與既有證據內容不改。原始碼 `SCHEMA_VERSION=19` 是待部署目標，不代表遠端已升級。
- 非空 schema18 測試保留真實合成 Frozen／Ledger／官方讀回，先製作檔案備份，再演練 0019 中途失敗整筆回滾、成功一次套用、重套拒絕及舊備份還原；逐筆舊證據、舊表結構、FK 與 integrity 均驗證。
- 部署仍由原唯一 preflight／version owner 執行；code-only 模式保留原 runtime marker。只有明確 `CYINVOICE_SCHEMA_UPGRADE=18:19`、deploy 模式、原 CYID／schema18、完整 canonical 19 遷移已套用且四道折讓旗標不存在或 false，才可只改 schema marker；其他 vars／Scopes／Services／Secrets／D1／限流設定不變。未完成遷移、未知升級、不同意圖／checkpoint、未真正部署或綁定讀回不同均拒絕；migration history 查詢上限 20 筆。
- 本批是獨立的 schema19 來源升格／部署準備工作，Patch 升至 3.1.32、Build 歸零。未操作原 Cloudflare D1／Worker、未啟用任何折讓旗標、未呼叫真光貿或發布 Release。C7 歸因政策與雙機真測仍待完成；正式 Release V3.1.0 不變。

## V3.1.31 Build 0 — 2026/10/21（V3.2 B/C：Cloud 主管外部占號覆核與下一號預留）

- 只有第一次 g0401 明確拒絕占號，且同次官方原號查回為已完成、不同買方及不含任何相同原發票的完整折讓，才以同一交易原子保存 `occupied_external` 與外部證據；查無、同買方、同原發票、未完成、回覆遺失或稽核失敗均保留 `sending`，禁止新配號。
- 原核准主管重新輸入當次 CYID 帳密與含原號的完整確認文字後，Worker 再驗官方原號、原發票餘額、原 Frozen／Grant、公司卡版次、Workspace／Device 及權限；同一 D1 batch 追加原 Ledger 下一 attempt 與不可變前後號稽核，只預留、不送出。新版 Windows 介面及原申請 ID 查回沿用同一流程。
- 擴充**尚未套用的 schema19 草案**：區分官方全項吻合與外部占號證據、建立索引及下一號稽核連結。新增獨立、預設關閉 `ALLOWANCE_EXTERNAL_COLLISION_ENABLED`；需 schema19 且明確開啟才可查證外部占號／改配號／送出下一 attempt。原 `ALLOWANCE_API_ENABLED`／`ALLOWANCE_G0401_SEND_ENABLED` 仍各自獨立，人工分類旗標不能授權改配號。
- 原號吻合始終不證明本次 POST 成功；新號要另行確認並通過原唯一 sending CAS／coordinator，且必須有不可變前後號稽核。合成測試涵蓋 ADMIN／SUPER_ADMIN 正向、USER 拒絕、旗標、併發、撤權／公司卡競態、稽核失敗原子回滾、回覆遺失不重配、schema18 非空升級／DDL 回滾及零盲目重送。
- 新增受控操作是獨立工作項目，因此 Patch 升為 3.1.31、Build 歸零。canonical migrations 仍僅 0001～0018、部署預檢／原 D1／Worker／Secrets／公司卡與真光貿均未操作。C7 歸因政策、雙 Windows 及真測仍未完成；正式 Release 維持 V3.1.0。

## V3.1.30 Build 1 — 2026/10/21（V3.2 Batch 1：Cloud 人工異常分類、稽核與 WinForms）

- **原始碼／合成 CI**：建立待審核的 `cloud/drafts/0019_allowance_manual_review_audit.sql` 作為**待本機 MCP 受控套用**的下一筆遷移來源，沿用 schema18 原 Allowance Application／Ledger／官方查回，不覆寫也不重套 0017／0018。新表永久不可修改／刪除、主管裝置與原 Ledger 綁定、官方證據欄可追溯，禁止以人工作業冒充財務成功。
- 新增 `manual-review`／`manual-history` 路徑與 Windows 主管查詢／分類介面。前者需 schema19＋**獨立預設關閉** `ALLOWANCE_MANUAL_REVIEW_ENABLED=true`、CYID 主管身份、原 Workspace／Device 有效、原核准申請、原 Ledger 為 sending／occupied_external。全項吻合分類須有同一 Ledger 的真實官方查回紀錄；其他分類只代表待查。
- 人工稽核只可 **append-only**；不更動號碼、Ledger、官方成功狀態、Grant 或原單次 g0401 發送條件。網路回覆遺失時只從原申請 ID 查人工歷史，不能視為成功或盲重試。合成測試涵蓋未知 schema／旗標關閉、一般員工拒絕、稽核不變更及零額外 provider POST。
- **Build 1**：先前將待審 0019 放入正式 migrations/ 被既有部署護欄正確拒絕（UNREVIEWED_PENDING_MIGRATIONS）。移至 cloud/drafts/，維持 canonical 0001～0018 不變；只在合成記憶體庫試行 0019，不修改／繞過任何部署護欄。
- **尚未提升為正式 Migration，更未部署**：原 CYInvoice D1／Worker 仍依使用者先前回報為 schema18、`ALLOWANCE_API_ENABLED=false`，草案 schema19 不得自行套用，須由使用者另一本機 Work＋Cloudflare MCP、確認私有備份／資源預算／回復路徑後再決定受控上線。真正光貿、雙 Windows、Cloud 可歸因成功政策與受控外部撞號下一號仍待驗收；正式 Release V3.1.0 未變。

## V3.1.29 Build 0 — 2026/10/21（V3.2 Batch 1：Local 外部占號人工覆核 UI 收斂）

- 將既有 Local `occupied_external` 的安全下一號預留能力接入既有「折讓人工處理」待辦明細；主管必須重新登入、看到原號與明確警示並**完整輸入指定文字**後，後端才會再次驗證即時光貿證據與 Frozen 核准，最後僅預留新號。沒有增加新交易來源或額外 g0401 入口。
- 不完整或不匹配的人工文字不能繼續；再次按同一舊號會被原 Ledger 的不可重複身份檢查拒絕。發送端仍只對新 `reserved` 狀態提供獨立且明確確認的第一次正式送出。
- 修正 Local 查回提示：同號／同內容匹配**不能證明本次 POST 完成**，不得以原號唯讀查回直接視為可歸因成功；沿用 `sending`、不可再送和不可自動改配號。WinForms startup smoke 覆蓋 occupied_external → 主管人工覆核入口、sending → 唯讀查回、confirmed 不提供新送出操作。
- Cloud schema18、Worker、旗標、公司卡與真交易**完全未動**。完整 Cloud 手動 Audit／跨機改配號與兩機真光貿驗收仍需後續獨立審核，不能藉此次 UI 收斂宣告 B／C 已完成。正式 Release 仍 V3.1.0。

## V3.1.28 Build 1 — 2026/10/21（V3.2 Batch 1：Local 外部占號人工覆核與唯一下一號預留）

- 從現有 Local `occupied_external` 出發，提供同一 Work Item 的**人工確認下一號預留**；僅允許已確定第一次 g0401 被光貿拒絕占號，且新的一次性原號唯讀查回顯示完整官方折讓由**不同買方、不同原發票**建立。逾時、查無資料、同買方、同原發票、號碼不符、未完成狀態或僅憑錯誤碼都禁止新配號。
- 需主管再次驗權、原核准 Frozen 不變、正式原發票餘額／明細最新驗證及明確「確認外部占號並重新配號」文字；每次新分配在原 Local SQLite 原有 Ledger 以 `attempt=N+1` 追加，前一次 `occupied_external` 不變。同一 SQLite Transaction 同時存不可變的新號／舊號／主管／官方 SHA256 快照稽核，沒有第二個 business owner。
- 新號只是 `reserved`，**不會自動呼叫 g0401**；必須另行受權限的既有唯一 send 首次 CAS。回讀結果不明仍維持 `sending`，禁止亂配號。合成測試覆蓋碰撞、配號、不同買方／原發票、撤權、重複確認與不可修改稽核。
- Build 1：Windows CI 先前證明舊號於成功改配後遭更嚴格的凍結身份比對拒絕；修正合成測試預期為 `InvalidDataException`，不放寬安全檢查。
- 不變更原 Cloudflare D1 schema18／Worker、公司卡、折讓旗標或真交易。Local 主管 UI 的人工第二次確認和跨機 Cloud 同等撞號流程、真實光貿驗收仍待整合完成；正式 Release V3.1.0。

## V3.1.27 Build 1 — 2026/10/21（V3.2 B/C：Local 原號回查與不可歸因 Audit 一致性）

- 修正 Local g0401 單次送出／唯讀查回：即使官方逐欄完全吻合，也**不能歸因到本次 POST**，回傳 `FullMatchUnattributed` 而不是自動 `OfficiallyConfirmed`；原號 Ledger 保留 `sending`，不可再次送出或換號。
- 沿用原 Local SQLite 而非新交易來源，新增僅存不可逆 SHA-256 官方快照指紋、原折讓號、主管和查核時間的 `allowance_local_readback_audit`（INSERT-only trigger），不保存買方明文。只有真正在原號 `sending` 且匹配同一份凍結 payload 時允許記錄，任何比對失敗／未知都不得偽造成功。
- 停用無可信 origin 證據的 Local `MarkOfficiallyConfirmed` 直接晉級能力，避免沿用舊測試將官方同號誤視為本次送出成功。合成測試涵蓋完全吻合、逾時後唯讀查回、稽核永久不可修改／刪除及零第二次 POST。
- Build 1：Windows 合成測試讀取本機 Audit 採用與正式 Store 相同的非 pooled SQLite 連線，確保 Windows 測試完成後檔案可立即釋放；無額外業務變更。
- 本次**只修改本機程式與 Local SQLite 新空表**；不套原 Cloudflare D1 migration、不變更原 Worker、CYID、公司卡或光貿。Local 撞號後受控再次配號與人工歸因判定仍需後續受權限、稽核及官方證據完整設計；正式 Release 仍 V3.1.0。

## V3.1.26 Build 1 — 2026/10/21（V3.2 批次 1：Cloud g0401 受控單次送出與主管查證原始碼）

- 原有唯一 Cloud coordinator 已具備官方最新可折額、主管 Grant、same-ID CAS、單次光貿 POST、原號唯讀查回的保守邏輯；本次只接唯一受權限保護的 `send-once` HTTP 路徑與 `attempt` 唯讀查證，絕不接受 client 傳入的 Ledger ID／折讓號。
- `send-once` 除既有 `ALLOWANCE_API_ENABLED`（現為 false）外，仍須**獨立的 `ALLOWANCE_G0401_SEND_ENABLED=true`** 且傳入當次主管明確確認；預設不存在／false 一律禁止，權限、原 Workspace／Device、最新版次與首次 `reserved` 由伺服器再檢查。第二次點擊、結果不明、逾時或撞號均只回讀，不再次發送、不自動配號或宣告 confirmed。
- Windows Cloud 主管畫面整合原申請 ID 的唯讀 attempt 證據、原 Ledger 發送前明確警告與單次送出；Core HttpClient 加入嚴格 fail-closed 契約驗證。合成 Cloud／Windows 測試覆蓋獨立旗標、越權、已送出禁止第二次 POST 與結果不得冒充光貿成功。
- Build 1：Cloud 合成測試將一般員工越權案例的獨立旗標明確設為 true，確保先進入 CYID 角色拒絕流程（403）；生產預設關閉行為不變。原工程碼未變。
- **本批仍未完結整個 B/C**：可稽核人工裁決和完整證據持久化、Local 撞號後受控下一號、真裝置／光貿測試需依 V3.2 計畫後續集中收斂，不能在旗標仍關閉時宣告實際可用。沒有原 Cloudflare 部署、D1 migration、真交易或新正式 Release；正式 Release 維持 V3.1.0。

## V3.1.25 Build 0 — 2026/10/21（V3.2-C7a：保守的單次送出結果人工歸因分類；工程版）
- 只擴充既有內部 `allowance-one-shot-coordinator.ts` 的回傳契約：將完整吻合無法歸因、完整吻合但 Audit 未成功寫入、號碼撞號、已接受但官方未能歸因、明確拒絕、結果不明六類列為人工審查，不把人工分類當作成功證明。
- 所有分類一律 `manualReviewRequired=true`、`proofOfThisPost=false`、`ledgerStatus=sending`、禁止自動確認／再次送出／改配號。補合成回歸測試；沒有新增 Public HTTP route、D1 schema、外部呼叫、正式 Release 或啟用 Cloudflare 旗標。
- C7 仍待人工簽核可信發送歸因政策、真正可追溯的持久人工操作、雙機與真光貿**測試**環境；單憑官方全項吻合不可自動標記 `confirmed`。

## V3.1.24 Build 1 — 2026/10/20（V3.2-C6 正式 Migration 17／18 原始碼升格）
- 將已由 #529 非空 schema16 備份／rollback 演練的兩份折讓 D1 Schema 正式列入 `cloud/migrations/0017_allowance_applications.sql`、`0018_allowance_frozen_ledger.sql`，並**移除**舊 `cloud/drafts` 同名 SQL，維持唯一 canonical owner；只新增折讓專屬 6 張表、4 個索引、23 個觸發器，不改動非折讓 Work Item／CYID 既有資料。
- 調整 C1／C2／C3／C4／C5、CYID-only、非折讓與部署預檢測試：合成 fixture 先停在 schema16，再逐批執行正式 17/18；正常應用整套 canonical migrations 的 fixture 不得再重套。完整交易失敗回滾、FK、舊表／角色護欄與備份還原仍由 CI 驗證。
- Deployment preflight 對已審查的 schema16→17→18 僅允許**唯讀差異盤點**，正式 deploy／CYID activation 仍要求無 pending migration；`wrangler.jsonc` 的工程 `SCHEMA_VERSION` 改為 **18**，不表示原遠端 D1 已升級；`ALLOWANCE_API_ENABLED` 仍預設關閉。
- 本批沒有操作原 Cloudflare D1／Worker／Bindings／Secrets、無真 g0401/g0501；實際遷移由使用者另開有 MCP 的本機 Work 依 `docs/V3_2_C_CLOUDFLARE_MCP_HANDOFF.md` 執行，須按共通規則 2.9.1 §11.3 計量、備份、停損。
- Build 1：移除剩餘 C5 回滾測試對已退役 drafts 路徑的依賴，所有 0017／0018 測試皆只讀唯一 canonical 來源。
- C 的安全可歸因單次送出、真 Cloudflare、兩台 Windows／光貿測試環境未驗收，不得宣告 V3.2-C 全部完成或正式發布（正式 Release 仍 V3.1.0）。

## V3.1.23 Build 0 — 2026/10/20（V3.2-C4e Cloud 加密申請原因與主管查閱）
- C4d 已保留的員工折讓原因現在於 C4a API 強制檢核並與原 `unnumberedTemplateJson` 一起放入唯一 AES-GCM Authenticated Frozen 封套；同一 request HMAC 同時綁定原因與官方模板，密文不含任何明文原因，無須改動 D1 欄位、Grant、Ledger 或額外引入第二資料 owner。
- Cloud `/v1/allowances/get` 僅經 CYID、active Device 與本人／主管權限驗證後才解密並回傳原因；list/status/preview 永不回傳原因。Windows Cloud Client 補嚴格 GET DTO 與合成安全測試；跨機主管視窗提供受權限控制的「查看申請原因」，不提供真 g0401/g0501。
- Frozen envelope v1 是尚未部署的 V3.2 合成契約；正式 D1 仍 schema16，Worker 折讓旗標未開。仍需獨立正式 schema17/18 升格、受控送出歸因策略、使用者本機 Work MCP 及兩台 Windows 真測。不得將此工程版當成正式 Release（仍 V3.1.0）。

## V3.1.22 Build 1 — 2026/10/20（V3.2-C4d CYID Cloud 折讓申請人入口／工程版）
- 選擇性參照 #440 的申請前持久化 ID 思路，將現行 RecordDetailForm／AllowanceRequestForm 既有完整編輯與原發票二次官方查回接入新的 C4a CloudAllowanceClient，無第二套 Work Item owner 或舊 API。
- CYID Cloud 模式先做 bounded list/readiness 預檢，再檢查光貿最新餘額及 7 位小數明細，固定新 application ID 與 SHA256 payload fingerprint 寫回原 Local SQLite 發票擴充資料，**成功持久化後才允許一次 Cloud create**。逾時／回覆遺失保留 ID，重入僅 status 唯讀查回，不自動重建、換 ID 或盲目送出；Local-only 舊人工流程不變。
- 原始 Frozen template 只送 C4a AES-GCM Server-owned store，Local pending marker 不保存買方資料、明文模板或密碼。Windows Core 提供純合成草稿驗證，Cloudflare／光貿正式交易仍未實施。
- **已知待修**：Cloud C4a 尚未提供跨機「申請原因」加密展示欄位，因此原因目前僅做本機輸入驗證；本批不宣告完整 UI/業務驗收，不得在正式 Worker 啟用功能旗標前忽略此缺口；後續須補齊加密 reason contract、正式 migration 與雙機測試。
- Build 1：Windows 編譯抓出 application ID 非 Null 性警告，補上明確 ArgumentNullException 防衛；無業務功能或對外協定變動。
- 正式 Release 維持 V3.1.0；C5 合成演練通過不等於 schema17/18 已升格。

## V3.1.21 Build 0 — 2026/10/20（V3.2-C5 非空 schema16 備份、Migration 與回復合成演練）
- 選擇性取用 #524 的非空原 CYID-only schema16 SQLite Migration 演練，將 C1/C2 的 0017/0018 審查草稿在**獨立本機合成資料庫**依序執行，先完整備份再套 migration。
- 驗證原 Workspace、Device、現有 work_items 與其他正式表仍在；各自注入 0017/0018 中途 SQL 錯誤，檢查回滾不留下半套 schema；外鍵、integrity、備份還原與 immutability 檢查納入 Cloud CI。
- 本批**不將**草稿提升為正式 migrations，也不調整原 D1 schema16／Worker／Secrets／Bindings，不實際消耗 Cloudflare D1 額度。正式遠端部署依共通規則 2.9.0 §11.3，另列實際讀寫量估算、帳號共用額度、停損與備份驗收交使用者本機 Work。
- 此為 Source-only 演練，正式 schema17/18 升格及 C4d 申請人入口仍未完成；不能稱為 C 階段正式驗收或正式 Release（仍 V3.1.0）。

## V3.1.20 Build 0 — 2026/10/20（V3.2-C4c Windows CYID 跨機折讓主管待辦；工程版）
- 從舊 #523 選擇性重建 WinForms 折讓主管待辦，乾淨接在 #526 已合併 C4b 後；申請跨裝置 List、同 application ID Status 查回、Review、光貿官方唯讀 Preview、Approve、Reserve，每次操作重新輸入 CYID 身分，由 Worker 驗證。
- 預覽只顯示匿名化必要資訊，嚴格拒絕 `canApprove=true` 等錯誤授權旗標；沒有公開 g0401/g0501，也不提供自動重送、調號、首次送出按鈕。HttpClient 明確關閉轉址、密碼不持久化。
- 主視窗「跨機待辦」改選單，保留原非折讓待辦；WinForms startup smoke 與 Fake HTTP 契約測試。申請人 Cloud Frozen 建立入口仍待 C4d；schema17/18 尚未升格／Cloudflare 沒部署，正式 Release 維持 V3.1.0。

## V3.1.19 Build 1 — 2026/10/20（3.2-C4b Windows CYID Cloud 折讓客戶端；工程版）
- 由 #522 原型以已合併 #525 的乾淨 main 重新整理，僅回收跨機 Client 與隔離 HttpClient 測試；沿用 #440 已驗證的「保留原申請 ID、結果不明只能回讀」設計，不帶回舊 work_items.ALLOWANCE。
- Windows Core 新增 CloudAllowanceClient：HTTPS、CYID Device Bearer、每次員工帳密、create/list/review/approve/reserve/status 白名單；嚴格驗證回覆狀態、rev、號碼，禁止自動重試、換號、把 reserve 當成 g0401 交易許可，明文憑證與 Frozen 請求不持久化。
- 新增模擬 HttpClient 回覆、遺失與不安全狀態測試，登記進 Windows Cloud regression。Worker feature flag 未啟用，D1 保持 schema16，真折讓呼叫仍不存在。
- Build 1 保留原 #522 同項 C# 宣告修正身分；C4c WinForms 與 C5 migration、真人雙機驗收另批進行。正式 Release 仍 V3.1.0。

## V3.1.18 Build 1 — 2026/10/19（3.2-C4 Cloud CYID 折讓申請／主管覆核 API；工程版）

- 新增 opt-in `/v1/allowances/{create,get,list,review,withdraw,preview,approve,reserve,status}`，安全綁現行 `withCyIdSession` 登入、合法 Device bearer、活動 Workspace、CYID 主管身份重新驗證及 D1 原 C1/C2 獨立資料表，保留專案單一身份來源；一般 USER 僅查自己申請，經授權主管可列出／認領覆核、重新查官方資料、核准並預留號碼。
- App ingress 的 bounded authentication body 路徑登記，上限 36 KiB 只開在 AES-GCM 凍結資料建立入口，其餘 4 KiB；嚴格白名單與狀態 CAS、錯誤不吐出公司金鑰／Frozen 明細。維持舊非折讓 Work Items 分流。
- 全部入口必須 `ALLOWANCE_API_ENABLED=true` 且 `SCHEMA_VERSION>=18` 才可用；**正式 Worker 未加入開啟旗標、正式 schema16 未改**，沒有公開 g0401/g0501 送出端點（C3c3b 繼續保持 internal-only）。
- 新合成 SQLite schema16＋未升格 17/18、CYID Device token 與假官方 HTTP 端到端路由測試：feature gate、越權、USER 建立與查詢、主管 CAS 認領／官方預覽／核准／預留，確認完全沒有光貿正式交易。舊 #440 混合 Work Item 路由未移植。
- 尚需 C4 Windows 跨機用戶端、C Migration 審核升格與本機 MCP，完成正式 C 前不得稱已上線。正式發行仍 V3.1.0。

- Build 1：首輪 CI 發現 synthetic D1 adapter 遺漏 `.all()`，已補齊；加強 GET 本人／主管的讀取前權限檢查，避免同 Workspace 不同員工看到折讓資訊；撤回時追加最新 CYID 身分與 Device 複驗，並新增跨員工隱私測試。

## V3.1.17 — 2026/10/19（3.2-C3c3b Cloud 唯一 g0401 單次發送協調器；工程版）

- 只新增 **internal-only** `allowance-one-shot-coordinator.ts`，沿用 C3c2 的完整官方前查＋`reserved→sending` CAS。CAS 成功後僅允許本次一次 `g0401` 簽名 POST，隨後最多一次原折讓號唯讀 `allowance_query` 查回；沒有額外 Worker route、沒有任意開放低階傳輸器。
- 即使官方 code=0 且全品項、買方、日期、稅額吻合，也先標記 `full_match_unattributed`，**不**憑相同號碼當作本次送出歸因、不改 Ledger 為 confirmed、不自動重試或換號；逾時／Crash／撤權／撞號全都只允許原號唯讀調查。
- 覆核查回證據透過 C2 `allowance_reconciliation_checks` 以 SQL active Device／Workspace 約束寫入 append-only Hash 紀錄，避免把原始發票/買方個資放到 Ledger。寫入 Audit 不明一律回未記錄，不影響發送防重試旗標。
- 使用真 Cloud 原始服務配合 schema16＋未升格 0017/0018 的隔離 SQLite／Fake signed HTTP，覆蓋成功接受但不能確認、回覆遺失、外部占號、撤銷、D1 CAS 回覆遺失及第二次絕對零 POST。
- 本片不接真 CYID HTTP route、WinForms、Cloudflare D1/Worker、正式 AMEGO 或 Release。後續 C3d 要正式規劃可信歸因與人工安全復核，再做 CYID HTTP 和 Windows；正式版仍 V3.1.0。

## V3.1.16 — 2026/10/19（3.2-C3c3a Cloud g0401 單次傳輸與完整原號查回；工程版）

- 從 #440 選擇性移植 `cloud/src/amego-allowance-issue.ts` 及隔離測試：只能透過單一簽名 form POST `/json/g0401` 送已凍結且配號的 payload；不自動重試，`code=0` 只表示 accepted_unconfirmed，4040161/4040162 只標示本次明確號碼衝突，不代表允許換號；timeout、傳輸中斷、回覆異常一律 unknown。
- 新增 `cloud/src/amego-allowance-exact-readback.ts`：使用唯一原折讓號簽名 **唯讀** `/json/allowance_query`，嚴格比對官方正式狀態、折讓日期、買方統編／姓名、未稅額、稅額及**每一筆原發票、日期、品名、數量、單價、金額、稅別**；未知、缺欄、品項變動、其他買方或待處理都 fail-closed。即使全項吻合仍標記 `full_frozen_match_unattributed`、`safeToAutoConfirm=false`，不把號碼相同當作可證明由本次 POST 建立。
- 隔離 Fake HTTP 測試覆蓋單次簽章、含稅金額與原始商品逐項比對、官方撞號、timeout、不合法凍結請求零連線、官方待處理與資料不一致拒絕；沒有正式 g0401 請求或真 D1 操作。
- **尚未將上述兩個 transport 連到 C3c2 CAS 或 Cloud app.ts 路由**。下一片 C3c3b 才能以受控單一 owner 協調送出與 readback，不得把這兩個低階 transport 直接當作送出權限。正式 migration、Worker、Secrets、Release 都不變，正式發行仍 V3.1.0。

## V3.1.15 Build 1 — 2026/10/19（3.2-C3c2 Cloud reserved→sending 原子 CAS，工程版）

- 從 #440 的單次折讓執行安全邏輯擷取狀態護欄，新增 `cloud/src/allowance-sending-claim.ts`，依 schema16＋C1/C2 draft17/18 的正式獨立折讓申請／核准／Ledger，**不使用舊 work_items.ALLOWANCE**。
- Internal-only 執行入口：讀回原 application_id 的 AES-GCM Frozen request／已預留號碼，核對主管與核准 Device、active Workspace、公司卡版次、approved revision、不可變 Grant 和同一 RequestHash；執行前重新以唯讀 `invoice_query`＋逐號 `allowance_query` 查完整官方原發票有效餘額，並對照先前主管核准的官方證據 Hash，不直接信任前端傳來的金額或成功旗標。
- 通過後以單一 SQL UPDATE CAS 競爭 `reserved → sending`，查核 SQL 時有效 Grant、主管／Device、公司版次、官方 Hash 和同張來源發票尚無另一筆 sending。失敗、官方狀態異動、撤權、逾時或 D1 回覆遺失都只能讀回原號，不可再次 CLAIM、不得更換號碼；即使 CAS 成功本批也**不連接任何 g0401**（`providerPostPerformed=false`）。
- 真 TypeScript＋schema16/drafts17/18 Synthetic SQLite 與假光貿官方 HTTP 測試：首次 CAS、重入拒絕、Grant 過期／官方狀態變動、角色撤銷、公司異動、相同來源發票兩案的發送互斥、CAS 時 Device 撤權、寫入成功但回覆遺失及發送後再次撤權。
- 未修改正式 `cloud/migrations`；沒有 Cloudflare D1／Worker 操作、沒有真 AMEGO g0401/g0501、沒有公開 HTTP route，正式 Release 仍 V3.1.0。

- Build 1：首輪 CI 指出新合成測試 D1.batch adapter 錯將 Statement 的 `.q` 欄位讀成 `.sql`；已修正測試接線，不更動 Cloud 執行／SQL CAS 邏輯。

## V3.1.14 — 2026/10/19（3.2-C3c1 Cloud 核准後首次配號 Ledger，工程版）

- 從 #440 選擇性回收單日 12 碼折讓號碼格式 `AyyyyMMdd001..999` 與 `AyyyyMMddA00..ZZZ`，最大 34695 號；新 `allowance-number-reservation.ts` 改接 C1/C2 獨立 `allowance_applications`、Frozen Request、5 分鐘 Grant 和原號 Ledger，**絕不使用舊 work_items.ALLOWANCE**。
- Internal-only CYID manager／active Device、公司卡版次、申請 approved revision、核准管理員與 Device、requestHash 和核准有效期皆重新檢查；SQL 條件式 INSERT 原子確認 next sequence 與未配號的唯一 application。兩台合法申請競爭同號只允許一筆成功；同一申請重按只讀回保留的 reserved 原號，後續 sending／confirmed／occupied_external 不重新配號。
- 以 canonical schema16＋review drafts17/18 執行 SQLite／Node 合成測試，驗證首筆、合法多次部分折讓分號、999/1000/34695 編碼、撤權、公司卡改版、過期核准、併發競爭、寫入後遺失回覆與唯讀復原。號碼保留不代表可送出：`maySendWithoutFreshOfficialCheck=false`，沒有公式化簽發成功欄位、沒有任何 g0401/g0501 或正式查詢路由。
- `cloud/migrations` 不變，schema16 不動；正式 Cloudflare D1、Worker、Bindings、Secrets 尚未更新；正式 Release 仍 V3.1.0。

## V3.1.13 Build 1 — 2026/10/19（3.2-C3b2b Cloud 主管正式核准 CAS；工程版）

- 選擇性參照 #440 的主管決策記錄與狀態 CAS，改接現行 C1／C2 獨立 `allowance_applications` 與 `allowance_execution_approvals`，不復活 `work_items.ALLOWANCE`。
- 以 C3a AES-GCM 凍結資料載入、C3b2a 光貿 `invoice_query` 加逐原發票 `allowance_query` 的**最新官方完整核對**作為唯一證據來源；本次決策先檢查 CYID ADMIN／SUPER_ADMIN、active Device/Workspace、公司卡版次及 review revision，核對後重新驗權並確認官方證據仍新鮮。
- 單一 `D1.batch` 內以 INSERT SELECT 條件柵欄寫入不可修改 Grant，接著 CAS 更新 review→approved（revision +1），使用現有 SQL trigger 保持稽核 append-only；裝置失效、公司卡異動、不同主管、重入、舊 revision、未完成官方核對皆 fail-closed。
- 結果不明時回報需按**原 application_id 唯讀查回**，不能把失敗當成重試或另配號權限。隔離測試：真 schema16＋兩份未升格 draft SQL、假 CYID、假官方回覆、兩步原子性、撤權、競態、不可變核准／稽核。
- **只有內部核准 service，沒有公開路由、正式 D1 Migration、Cloudflare 部署、g0401/g0501 或正式 Release**；C3c 再分批處理 Ledger／單次送出；正式 Release 維持 V3.1.0。

- Build 1：首次 Cloud CI 在 SQLite 合成測試發現 DatabaseSync 回傳 null-prototype row，改採逐欄斷言；核准 CAS 與業務實作無需改動。

## V3.1.12 Build 2 — 2026/10/19（3.2-C3b2a 官方逐原發票折讓證據，工程測試）

- 以 #440 唯讀 `allowance_query` 邏輯與光貿現行公開 API 文件為依據，新增 `amego-allowance-detail.ts`：簽名唯讀 POST、不重試、最多 256 KiB、逐商品原發票號／原發票日期／未稅金額與稅額嚴格驗證。正式折讓號碼與每筆明細嚴格核對；待辦或結果不明拒絕當作完成證據。
- 新增內部 `allowance-official-proof.ts`：先取 `invoice_query` 最新官方折讓編號摘要，再逐號 `allowance_query` 查完整商品；使用 7 位整數固定小數只計入**本原發票**品項，累積有效已折額，明確且完整作廢才釋放額度。要求官方各筆明細總額與摘要相符，來源或凍結品項／日期不符、待處理／漏欄／超額一律 fail-closed。
- 以純合成 HTTP 驗證跨原發票同一折讓、有效及作廢歷史、缺漏／重號／待確認、金額不符及超額，`approvalRecorded=false` 與 `numberReserved=false` 明確保留。**這是可信官方金額核對的工程來源，不是主管核准／D1 Grant，也未安裝公開路由。**
- 本批沒有修改 `cloud/migrations`，正式環境仍 schema16；沒有 Cloudflare 修改、真光貿呼叫、g0401/g0501 或正式 Release。

- Build 1：第一輪 Cloud Validate／tsc 嚴格型別檢查發現新 evidence 模組的 nullable 及 definitely assigned 註記不完整；已在同一切片修正來源型別，不改計算與網路安全語意。

- Build 2：CI 在前一輪只剩 invoiceDate nullable 型別未收斂（TS2345），來源已明確在日期驗證後套用非空型別，不改官方查詢、數值或權限語意。

## V3.1.11 — 2026/10/19（3.2-C3b1 Cloud 主管光貿唯讀預覽；engineering）

- 從 #440 精選回收正式 `invoice_query` **唯讀簽名 adapter** 及其 synthetic transport 測試；code=0 與官方折讓摘要不直接等同可開立／可折餘額。缺漏、未知、跨原發票的摘要金額不估成可折上限。
- 新增 `allowance-manager-preview.ts` 作 C3a Frozen 申請的內部主管預覽：使用 Worker 專屬公司卡 AppKey、同一 Workspace／active Device、CYID ADMIN/SUPER_ADMIN 前後重新驗權、requestHash／revision／review 狀態二次比對、公司卡版本與 AppKey 改動防護。預覽只回官方摘要與資料完整度，不回傳金鑰或原加密請求本文。
- 明確固定 `canApprove=false`／`canAllocateNumber=false`／`safeToInferRemainingBalance=false`，禁止把純 `invoice_query` 摘要誤用為已完成正式折讓核准。尚未接 Cloud HTTP route 或任何官方 mutation；完整原發票逐項與有效餘額核對、主管核准 Grant CAS 留下一批 C3b2。
- 隔離 SQLite＋合成官方 HTTP 測試覆蓋簽章、零重試、未授權／撤銷零網路呼叫、官方 pending／不完整／91、跨 Workspace、公司改版、身分切換、申請 revision race。正式 Release 仍為 V3.1.0。

## V3.1.10 Build 1 — 2026/10/19（3.2-C3a Cloud 加密折讓申請封存／安全讀回；engineering）

- 從 #440 舊 `allowance-applications.ts` 選擇性重接，新增 `cloud/src/allowance-frozen-store.ts`，採現行 schema16＋drafts/0017/0018 的獨立 application_id；**不帶回** `work_items.kind='ALLOWANCE'`。
- 後端使用既有公司卡 AES-GCM helper 與 application_id＋HMAC requestHash 專屬 AAD；原發票關聯／來源 fingerprint 由 Worker 金鑰 HKDF domain-separated HMAC 製作，避免僅對低熵發票號直接 SHA256 洩漏推測。
- 僅可信內部 service 可用：建立前／後重新比對 CYID actor 與 active Device；讀取僅原申請員工或管理員且再次驗權，Workspace、environment、公司卡 revision、加密明細與 requestHash 跨欄綁定。透過 D1.batch 在單一交易內寫入申請與 Frozen Request，失敗回滾；原 ID 供不明結果唯讀查回，不盲目另建 ID。
- 新增合成 SQLite／esbuild 回歸：加密不含買方明文、跨 Workspace／身分／環境拒絕、裝置撤銷中止讀回、金鑰錯誤、原號第二份獨立申請、公司版次競爭原子回滾、時間與格式檢查。
- **未掛 Cloud API routes、未執行 AMEGO、未操作 Cloudflare、未轉成正式 migration，也未聲稱完成 Cloud 主管核准／發送。** 正式 Release 仍為 V3.1.0。

- Build 1：首輪 Cloud CI 偵測 3.0 原始 D1 schema 帶有 `idx_single_workspace`，C3a 測試插入第二個 Workspace 違反既有單一 Workspace 約束；已調整跨 Workspace 越權測試只查詢不存在的其他 Workspace，不修改正式 schema 或業務行為。

## V3.1.9 — 2026/10/19（3.2-C2 Cloud 加密申請、核准快照與單次配號 Ledger；engineering）

- 獨立工程 Patch `VERSION=3.1.9`／`BUILD=0`；正式 Release 仍是 V3.1.0。
- 選擇性移植 #440 的加密折讓 Frozen Request／官方核准 Grant／永久配號 Ledger 與唯讀原號核對 Audit 概念，重新接到 C1 新的 `allowance_applications.application_id`，不重建 schema16 `work_items` 或覆寫 migration0016。
- `cloud/drafts/0018_allowance_frozen_ledger.sql` 為**審查用草稿**，不是 canonical migration：原請求明細以未來 Worker AES-GCM 加密 envelope 存放、請求及原發票關聯使用 opaque hash；D1 限制 Workspace／Device／公司卡版次、核准版本、狀態單向 CAS、單次 sending 及已知撞號歷史；不同合法申請保留多次部分折讓能力。
- 隔離 SQLite 以既有 schema16 套草稿0017→0018，驗證失敗回滾／還原、Frozen/Grant/Audit append-only、兩筆相同來源發票在未定結果時互斥 sending、確認撞號後新 attempt，以及不改舊 Work Items。
- 尚**沒有**移植 Worker 加解密 caller、CYID 管理員角色執行驗證、實際配號交易、官方 AMEGO 請求或正式 D1/Worker。SQL 結構不能替代實際 AES-GCM／官方證據／即時權限驗證；以上留後續 C3 與 H。

## V3.1.8 — 2026/10/19（3.2-C1 Cloud 折讓申請儲存結構草稿；engineering）

- 從正式 schema16 出發，新增 drafts/0017_allowance_applications.sql 作審查用草稿，尚非 canonical migration，不會隨一般 D1 migration 自動套用。
- 分開保存折讓申請（不存發票號、買方或官方證據）與不可變審核 Audit，Workspace／Device 外鍵、revision CAS、唯一 application_id、終態不可變；同發票來源雜湊可有多筆獨立合法折讓。
- 新增隔離 SQLite schema16 升級、失敗回滾、舊資料／schema 保留、CAS、Audit 不可修改及備份還原測試；未包含配號 ledger、正式 Cloud API／CYID 操作與部署。正式 Release 仍為 V3.1.0。

## V3.1.7 Build 1 — 2026/10/19（3.2-B2d Local Windows 單次折讓執行；engineering）

- 獨立工程 Patch VERSION=3.1.7、BUILD=1；上一正式 Release 為 V3.1.0。
- 原 Local 折讓待辦沿用已核准草稿與 SQLite ledger，由同一位管理員重新登入並確認後才可進行正式單次 g0401。Core 送前重新驗證權限、核准與凍結明細，取得光貿完整最新折讓紀錄、剩餘可折金額及核准摘要一致，才由原 B2c OneShotService 先 CAS 標記 sending 再發送一次。
- 結果不明、斷線或重開只能依原號唯讀查回，不盲重送／換號；官方 confirmed 不會自動結案原人工待辦。新隔離測試涵蓋無權限、官方歷史變動零發送、未知結果防重入。無實際光貿交易、正式 Cloudflare 變更及正式 Release。

- Build 1：首次精確 PR Windows CI 發現待辦詳情 rows.Add 的 switch 表達式缺少外層右括號，已修正 C# 編譯語法並重新驗證；沒有更動 B2d 業務範圍。

## V3.1.6 — 2026/10/19（3.2-B2c Core 單次 g0401 HTTP 與原號唯讀查回；engineering）

- 獨立 Patch `VERSION=3.1.6`／`BUILD=0`，正式 Release 仍是 V3.1.0。
- 光貿 `g0401` 正式格式的簽名傳輸已加入 Core `IAmegoAllowanceGateway`；**一次性直接 POST，禁用通用 PostAsync 在 code 15 後的自動重試**。code=0 不等於官方已完成。加入 `allowance_query` 原號唯讀查回。
- 新增尚未接 Windows UI 的 `LocalAllowanceOneShotService`：由後續 caller 提供真 Local 管理員與光貿 preflight，先執行 SQLite reserved→sending CAS，才送最多一次官方請求。逾時、未知回覆或程式重開只讀查原號，不盲重送、不換號；確定 4040161／4040162 僅記錄碰撞。官方回查完成且凍結買方、日期、原發票品項、未稅與稅額完整一致才轉 confirmed。
- SQLite／模擬 HTTP 回歸涵蓋簽名表單、零重試、帳務重入拒絕、超時回復、明確撞號與原號查回；尚未執行真 g0401、Cloudflare 遷移或正式 Release。下一批才接有權限的主管正式操作與實機驗證。

## V3.1.5 Build 1 — 2026/10/19（3.2-B2b Local 已核准折讓配號與固定 g0401 草稿；engineering）

- 新獨立 Patch VERSION=3.1.5、BUILD=1，上一正式 Release 仍 V3.1.0。
- 新增 Local 已核准折讓的「預留折讓單號（不送出）」：同一位主管重驗身分、配號前再查光貿，官方資料與核准當下不同或不足額一律停止。核准時新增草稿摘要，原發票、買方或草稿品項在核准後變動也停止配號。
- 依 V3.1.3 Local SQLite ledger 保存原號與完整不可變含未稅 Amount／Tax 的 Frozen request，支援重開／重按原號回查，不會盲目新增號碼；輸入含稅單價時仍保留原 UI 切換並轉成後續 API 需用的未稅請求。
- 本 Patch **只 reserve，絕不標 sending、也不觸發 g0401/g0501**。正式單次送出／官方原號查回、結果不明防重送及明確撞號處理待 B2c。Cloudflare 不動，既有 V3.0+V3.1 真人驗收清單不改。

- Build 1：首輪 exact-head CI 編譯發現 SQLite Work Item ID 需要 invariant 字串，但現有待辦 issue.Id 是 long。統一轉成 CultureInfo.InvariantCulture 字串後重跑 CI；本次只修此 Patch，版本 Patch 不另加。

## V3.1.4 — 2026/10/19（3.2-B2a Local 管理員官方即時覆核與草稿核准；engineering）

- 獨立 Patch `VERSION=3.1.4`、`BUILD=1`，上個正式 Release 仍是 V3.1.0。
- 主管在現有 Local 折讓待辦點「核准折讓草稿（不送出）」後須當次登入；先向光貿重查完整折讓清單與剩餘額供主管確認，按確定時再重驗主管及重查官方資料，結果變化／缺明細／超額拒絕核准。核准資訊含 UTC 時間、主管員編和本次官方查詢摘要雜湊持久化；核准後禁止舊人工完成或取消。
- 僅限 Local、已有完整編輯明細的新申請；舊人工流程、3.1 Cloud 協作不變。不呼叫 g0401／g0501，也未使用 B1 ledger 進行實際配號或交易；下一批再將正式核准和 B1 配號／單次送出連線。無正式 Cloudflare MCP 或 Release 操作。

## V3.1.3 — 2026/10/19（3.2-B1 Local 配號與核准快照持久化；engineering）

- 新獨立 Patch **VERSION=3.1.3、BUILD=1**。從歷史 #440 **只選擇性移植** Local SQLite 配號與狀態鎖（不導入未經驗證的正式 g0401/g0501 連線與舊 Cloud schema）。本工程版無新的正式 Release，上一正式版仍 V3.1.0。
- 本機 SQLite additive 建立配號 ledger：以賣方／環境／日期隔離序號，保存管理員員編與核准 UTC 時間、不可變原請求及完整帶號的 payload。重開程式後同一工作項目與同一主管核准只取回原號，修改核准／明細一律拒絕。
- reserved→sending 的條件更新先於任何未來網路送出，sending／confirmed 禁止重入；明確 4040161／4040162 只標記占號、**本階段不自動重配**，須等下一片完成原號官方只讀回查確認後才設受控改號。SQLite 合成測試覆蓋同工單、防竄改、撞號與重開、編號唯一及環境分隔。
- 尚未在 WinForms 接上正式主管核准或 g0401；本批 ledger 只是不可繞過的底層安全前置。沒有動 Cloudflare D1／Worker／Secrets、真實光貿或 V3.0＋V3.1 唯一延後驗收清單。

## V3.1.2 Build 2 — 2026/10/19（3.2-A 折讓品項編輯與有效餘額；engineering）

- 新獨立 Patch，VERSION=3.1.2、BUILD=2，正式 Release 仍為 V3.1.0。
- Build 2：第二輪 CI 指出測試案例的含稅單價 100.1234567 換算未稅七位小數期望值填錯；精確 100/105 換算應為 **95.3556730**，已修正合成測試並重新執行完整 CI。未修改金額運算實作，依同一 Patch 返修規則維持 VERSION=3.1.2、BUILD=2。
- 同一 Patch 第一次 CI 測試發現舊人工折讓確認流程回歸，按根目錄 §6 **Build +1**；保留原人工折讓 ReadOfficial 歷史並獨立以 summary-present 標記要求新 V3.2 金額驗證須完整，兩種行為都新增回歸。使用者確認原需求需保留含稅／未稅單價切換，本 Build 已恢復並以 Core 驗證原始含稅輸入。
- 表格帶入原發票商品，允許新增、刪除、修改數量與課稅別；**含稅／未稅單價可即時切換**，來源價格依原發票輸入模式帶入；含稅輸入由程式自動換算未稅單價與未稅金額供後續光貿資料使用，保留負單價折扣及 FixedDecimal 七位小數；每行依選定模式正確計稅，自動計算整張含稅折讓額。
- 官方 invoice_query 必須有完整的 allowance 陣列才能確定剩餘額；已完成且未作廢的折讓才計扣。缺摘要、號碼重複、未確認狀態或資料錯誤一律拒絕，不禁止在有效餘額內再次人工核准合法折讓。
- 員工提交前重新查光貿，Core 依原始輸入模式（含稅／未稅）重算編輯草稿每筆金額、原發票關聯、未稅／稅額及累計總額，納入現有人工折讓待辦。現階段仍未呼叫 g0401/g0501，Cloudflare 原 D1／Worker 不變。合成測試涵蓋負折扣、作廢及超額，WinForms smoke 驗證實際表格。
- 既有 V3.0／V3.1 真人延期驗收仍在唯一 POST_RELEASE_ACCEPTANCE.md，後續正式 Cloudflare 操作交使用者本機 Work MCP。

## V3.1.1 — 2026/10/19（V3.2-00 光貿即時折讓歷史；engineering）

- 3.2 工作的第一個獨立 Patch，依規則 `VERSION=3.1.1`、`BUILD=1`。**上一個正式 Release 仍是 V3.1.0**，本版只產工程包，不新建 tag／Release。
- 「發票詳細資訊」點擊「折讓」前，先以既有 `InvoiceDetailRefreshService` 直接從光貿查回最新 `invoice_query`；只有成功且與本機 Workspace／公司／環境一致，才開啟員工申請視窗。視窗列出官方回覆的已有折讓號、日期、類型、狀態與未稅／稅額；資料不足不冒充可折餘額，申請提交仍由 `SubmitAsync` **重新向光貿查詢**。
- 尚未啟用 g0401、g0501 或新增 D1／Worker 功能；歷史人工折讓與 Local／CYID 模式不退役。WinForms 版面 smoke 增加合成官方折讓摘要斷言。
- [V3.2 分階段路線圖](docs/V3_2_IMPLEMENTATION_PLAN.md) 凍結使用者最新需求：合法已存在折讓可由人再次核准；明確 `4040161/4040162` 撞號須先查回並保留失敗號才能改下一號；timeout／不明回覆不能重送或換號；後續所有 Cloudflare 正式操作只交本機 Work MCP。

## V3.1.0 — 2026/10/19（正式 Minor；非折讓跨機人工待辦）

- Minor 升版理由：已完成 CYID 非折讓跨機 Work Item 工作流及原 D1／Worker schema16 受控切換，不是單純 UI 修正。VERSION 3.0.4→3.1.0、BUILD=1；正式 Release 當次由使用者明確批准，從 main 重新執行安全／Windows／封裝／SHA-256。
- 員工對未收回紙本作廢、非折讓同步例外可手動建立匿名跨機待辦；CYID 管理員跨 Device 查詢、ID 唯讀查回、open→review→closed 覆核、revision CAS、append-only 稽核。權限撤銷、Workspace／Device 隔離、來源摘要資料最小化與結果不明不重送保護保留。人工封存不等於 AMEGO 官方成功；3.2 折讓不納入。
- 使用者本機 MCP 已回報：原 CYInvoice D1 僅一次性套用 canonical 0016，原 Worker 受控啟用 schema16／Work Items，備份、還原演練及前後資料一致性通過。本 repo 只記錄交接摘要，不聲稱本輪獨立連線核對；私人證據留本機。
- **發行後仍待真人驗收**：使用者明確批准將 V3.0 的真 Email、異機不同金鑰災復／邀請、角色／撤銷／中斷／公司卡／UI 等與 V3.1 的雙 Windows、CYID 角色、CAS、斷線及 DPI 一併延後。**全部集中在 [唯一合併待驗收檔](docs/POST_RELEASE_ACCEPTANCE.md)**，正式 ZIP 同時附「發行後待驗收.md」；不以 CI 代替真人操作，也不勾成通過。
- 此版本不重做 D1／Worker 或操作 CYID D1，不擅自簽發復原邀請。V3.0.0 舊 tag／Git 歷史用於追溯；依使用者另項要求，V3.1.0 新 Release 成功後再處理 V3.0.0 發行頁的移除，絕不提前刪除舊版下載。

## V3.0.4 — 2026/10/18（V3.1-D canonical schema16；engineering）

- 新獨立維運準備項目，依規則 Patch +1、BUILD=1；不是 V3.1.0 正式發行。上一正式 Release V3.0.0 保留。
- 非折讓 Work Item／append-only Audit 的 0016 由 drafts 提升為 canonical migration；只修改說明註解，SQL 行為沿用已審查草稿。隔離測試改為由正式 migrations 恰好套用一次，V3.0 歷史演練仍固定在 schema15。
- source storage marker 對齊 16；唯讀 preflight 只辨識明確的 canonical 0016，未完成遷移仍禁止一般部署。原 Worker code-only 部署繼續保留線上 schema／feature flags；此變更本身不啟用 WORK_ITEMS_ENABLED。
- 遠端處理只由另行授權的本機 MCP 核對原 schema15／CYID_ONLY、加密備份可還原、Workspace／Device 連續性後單獨執行 0016，再保留原 Bindings／Secrets 更新原 Worker。不得重跑 0014／0015 或新增 Worker／D1；私有目標與 readback 不進公開 source。
- 本版不改 Windows 業務功能、不含 3.2 折讓 API、不簽發或兌換復原碼；真雙 Windows／角色／DPI 驗收及正式發行仍另行處理。

## V3.0.3 — 2026/10/18（僅允許合格非折讓例外回報跨機；engineering）

- 本批是**新的獨立** Windows 介面安全與操作修正，推進 **Patch +1、BUILD=1**，不視為 V3.0.2 欄寬工作的返修。
- 原「上傳問題」視窗的「回報跨機」對任何未結案清單項目都可能呈現可按；選到折讓、折讓作廢或未知項目時，按下才會出現「V3.1 不支援」。現在直接以同一個 Core 非折讓類型判定限制按鈕，只允許紙本尚未收回的作廢人工覆核及七種支持的同步／回查技術例外，不讓折讓或未知類型誤進 3.1。缺 CYID Employee Authority／Workspace／Device 時也不啟用。
- Core mapper 仍再次嚴格檢查來源，並新增各合法／非法 issue type 的契約測試。行為仍為使用者明確確認、當次 CYID 登入及匿名 metadata；不處理 AMEGO 官方作廢或折讓。
- 同步 V3.0.3 工程說明、包內使用說明、README、TODO、V3.1 handoff。原 D1／Worker 沒有操作，schema16 仍為草稿且 feature flag 預設停用，未宣稱實體雙機／DPI 驗收。上一個正式 Release **V3.0.0** 不變。

## V3.0.2 — 2026/10/18（跨機待辦視窗尺寸適應；engineering）

- 完成下一個**獨立** WinForms 顯示修正，故由 V3.0.1 推進 **Patch +1、BUILD=1**；不是 V3.0.1 同項返修，也不是 3.1 正式 Minor。
- 「跨機待辦」既有七欄固定合計 895px，超過最小視窗 740px 的實際 ListView 可用寬度。改用單一正式版面路徑，在視窗 Resize 時按清單可用寬度分配欄寬，保留垂直捲軸槽與工作項目 ID 欄，不動待辦選取、角色、狀態或 AMEGO 業務邏輯。
- 真的 WinForms form smoke 覆蓋 920×500 與 740×400 兩個視窗尺寸的欄寬／ID 區域，並沿用按鈕及安全清單狀態 smoke。此為 Source／合成驗證，DPI 100／125／150% 與真雙機仍待使用者實機驗收。
- 隨工程版同步 VERSION、當版工程包內容、使用說明、README／單一 TODO／3.1 handoff。Cloudflare 原 D1／Worker、schema16 草稿及功能旗標完全不動，**上一個正式 Release 仍是 V3.0.0**。

## V3.0.1 — 2026/10/18（3.1 非折讓跨機 Source 階段；engineering，未正式發布）

- 依共通版號規則恢復 **Patch +1、BUILD=1** 的工程版追蹤：V3.0.0 是上一個已發布版本，本版 V3.0.1 僅工程測試包，**不是 V3.1.0 正式發行，也不新增 Release／tag**。先前 #482～#497 已合併時仍使用 3.0.0 的漏升版事實保留在 Git；此版一次性對齊目前 Source，不補造不存在的中間 patch 或偽造歷史包。往後**每個獨立修改項目推下一 Patch、同一項返修改推 Build**。
- 累積未逐版標識的 V3.1 Source：非折讓 `VOID`／`UPLOAD_ISSUE` 例外工作項目、CYID 管理員跨機唯讀待辦、人工 `open→review→closed` CAS、資料最小化及 append-only Audit；對應已合併的 #482／#483／#485～#487／#489～#491，及 #493／#494／#497 的 WinForms 版面 smoke、失權清空待辦、結果不明 ID 唯讀查回與 Worker／Windows 回覆最小化。前述為**Source／隔離測試**，不可冒充已啟用跨機服務或 AMEGO 官方成功。
- #495／#496 已清理過時交接及同步單一 TODO／V3.1 交接；本工程版更新 VERSION、CHANGELOG、使用說明及工程包的 V3.0.1 內容。Work Item schema16 SQL 仍只在 `cloud/drafts/0016_non_allowance_work_items.sql`，功能旗標預設關閉；**沒有**連線／套用 Cloudflare D1 或部署 Worker，沒有真雙機／多 DPI 驗收。真實 Email／災復驗收仍依 TODO 與 V3.0.0 已知限制。
- 發版門檻不變：V3.1 完整功能、最後由使用者本機 MCP 處理原 D1／Worker、真機驗收完成後，才能評估 Minor `V3.1.0 Build 0`；正式 Release 仍需使用者**當次明確授權**。

## V3.0.0 — 2026/10/18（Local／CYID、公司卡與裝置復原）

- 依使用者本次明確發布授權升為 V3.0.0、BUILD=1。發行範圍鎖定已合併的 V3.0 與 Unicode 復原修正；不合併 PR #440，不帶入 3.1／3.2／3.3 的 Work Item、跨機審核或新折讓 API。
- 新版僅提供 Local 與 CYID Cloud；舊 Built-in／Self-hosted 不作現役模式，遇到舊身分後端明確拒絕，不以舊帳號降級放行。保留原發票、人工作廢／折讓、匯入、同步與結果不明處理。
- CYID 公司卡提供角色分離的狀態／修改及超管明確確認後套用本機憑證；雲端已設定不等於每台 Windows 已套用。App Key／MO 密碼仍以本機 DPAPI 保護，不自動覆寫其他裝置。
- 裝置災難復原由 CYWEB 超管重新驗證後簽發短效邀請，新機確認原公司、保存 Pending Token 後接管；只有成功接管才撤銷原公司其他舊裝置，結果不明沿用同一 attempt 讀回，不盲目重簽或建立第二個 Token。
- 修正復原密碼的 Unicode 邊界；隔離測試涵蓋權限／scope／Email 已驗證 gate、過期／撤銷、競爭、原子回滾、歷史保留、Pending Token 重啟與遺失回覆。Windows 虛擬機與 CYWEB 合成驗證通過，不接觸正式 CYID。
- 驗收限制明列於發行包：真實 Email 送達與獨立金鑰的實際異機還原尚未實測；單機資料自行保管，裝置接管不等於完整發票資料或整個雲端服務還原。本版不宣稱完整實體雙機災復驗收。

## V2.6.20 — 2026/10/18（V3 復原密碼相容修正，工程測試）

- Windows 復原 Preview／Claim 改按 CYID canonical 的 8–16 個 Unicode 字元檢查密碼；復原密碼輸入框容納最多 32 個 UTF-16 code units，支援合法補充字元，不改 CYID 密碼規則或授權。
- 新增只使用合成資料與注入 HTTP handler 的 Windows client 回歸：合法／不合法 Unicode 邊界、未帶舊 bearer、原 Pending Token 經重啟保留、遺失回覆不自動重送、禁止另建 Token、同一 attempt 讀回、錯 scope 在送出前拒絕、錯 App／過期／撤銷回覆與既有本機資料保留；WinForms smoke 核對密碼欄位容量及遮罩。
- 獨立 bug 修正推進 Patch、BUILD 維持 0。Worker／CYID／D1 不變；工程驗證不等於真 Email 或異機還原驗收，不建立正式 Release。V3.0 的版本與發行仍依其正式驗收及明確發行授權處理。

## V2.6.19 — 2026/10/11（買方名稱查無資料可留空，工程測試）

- 手動開立、MO／酷澎／鼎新匯入遇到光貿正常空結果／code 99，提示可人工輸入買方名稱；留空仍可開立，正式送出及本機紀錄以該張統編作為 BuyerName。API 技術／權限錯誤與未完成查詢仍擋下，不冒充查無資料。
- 匯入確認保留名稱編輯與鼎新套用資料流程；提示與最後開立確認清楚說明留空的送出值。只有使用者人工名稱在成功開立後記憶；統編預設值不寫進人工名稱記憶，避免遮住往後 API 名稱。
- 沿用原 InvoiceService／統編查詢／本機 schema，測試環境仍使用公開測試統編與光貿名稱，不送真實識別資料。獨立需求推進 Patch、BUILD 歸零；CYID／Cloud runtime／Workspace 不變。真實非營利單位送出由使用者驗收。

## V2.6.18 Build 1 — 2026/10/11（舊雙成功修復，工程測試中）

- 使用者核准修復舊重複成功資料，沿用 InvoiceSyncService／InvoiceSyncRepository 的重新整理及 SQLite 寫入交易。限目前公司／環境、兩筆本機已開立、同官方號碼及 API OrderID、完整明細一致，且原生成 record_id 時間與 SentAt 一致、官方開立時間介於兩次送出而能確認先後；不依清單排序選取。缺少證據、第三筆候選、不同內容或人工／作廢／折讓 metadata 均保留原資料。
- 先唯讀回查光貿，再以 SQLite online backup 保存完整原資料庫於 Data/RepairBackups，檢查完整性、關聯及原發票快照並 flush。寫入前重新比對全部發票與明細，期間有變更就停止；更正及本輪同步共用交易，中斷回滾、重試不重複修復。
- 原成功保留，多出的嘗試改為開立失敗並清空發票號碼／上傳完成狀態，只留上傳問題；原送出時間與明細不刪除。成功後沿用既有 issue resolution 解除歧義。只查光貿，不作廢、不重開、不新增資料表或修復 timer。
- 新增正向、差異／證據不足、官方失敗、備份失敗、併發更動、交易中斷後重試及取消回歸。實際使用者 Data 未提供，工程測試通過不代表已修復使用者資料；需以新版在原資料夾重新整理驗收。
- 同一 bug 補修維持 VERSION 2.6.18，BUILD 0→1。Cloud／provider／Workspace／schema 不變；沒有 merge、部署、正式切換或 Release。精確 head Windows CI／工程包以 PR #380 checks 為準。

## V2.6.18 — 2026/10/11（重複開立失敗修正，工程測試中）

- 保留原 API OrderID 再次送出、由光貿判斷重複的流程；未新增本機預先阻擋或重開尾碼。AMEGO code 在共用 decoder 接受明確的整數 number／string，code 15 校時共用同一解析；缺失／null／不合法 code 不默認成功。明確非零錯誤直接記為 Failed，不回查既有發票當成本次成功。
- HTTP 字串重複錯誤的修正前回歸重現「decoder 例外 → 訂單回查 → 舊發票被記為第二筆成功」。這是已驗證的缺口，沒有原實機回覆／Logs，不能宣稱它就是使用者該次事件的唯一原因。
- 回覆或結果不明回查命中已知同環境／API OrderID／發票號碼的既有發票時，本次記為重複失敗；清空本次發票號碼及上傳狀態，保留原成功紀錄。真正首次送出逾時的嚴格回查、結果不明禁止盲目重送及遠端成功／本機保存失敗仍沿用既有流程。
- 依使用者建議，開立失敗僅在「上傳問題」顯示及依原規則清除；主清單及其狀態篩選移除 Failed。資料仍存原 invoices，不建立第二套失敗表；結果不明仍在主清單保留。
- 詳細回查禁止覆寫 Failed；官方同步排除 Failed 比對候選，必要時另同步真正的官方發票，避免用同 OrderID 改寫失敗嘗試。新增真實 HTTP decoder→core、舊發票回覆／回查、官方同步與 Windows 清單實際載入回歸。
- 獨立 bug 推進 Patch、BUILD 歸零。Cloud／CYID API、storage、Workspace、Device Token、部署與正式切換均未變動。既有錯誤雙成功紀錄不靠排序自動改寫／刪除，待原本機識別碼與官方證據確認後更正。

## V2.6.17 Build 2 — 2026-10-10（engineering，Cloud development 已更新／未正式切換）

- 2026-10-11 部署準備：沿用原 development workflow，增加唯讀 preflight、原 DB／authority／migration／FK／歷史 fingerprint 與 private recovery checkpoint；驗收使用 Cloud package version，修正舊 0.9.0 hard-code。此批只改部署工具／測試／文件，Windows 及 Worker 業務 runtime、VERSION／BUILD 均未改；遠端結果見現行交接。
- 2026-10-11 development #12 已套用 0013 並驗證 Cloud 0.9.2／storage 13，新同步 capability、原 Workspace／Token hashes／歷史及 FK 正常；CYID 關閉，Built-in 接續。Source 82080dfa 七項 CI 全綠，Windows #277 工程包可測；CYID development 仍 0.3.5，staging／實機／正式切換／Release 尚未完成。
- Build 2 收斂 self-status parser，缺完整撤銷證據不能 wipe；自己撤銷這台裝置亦先落 marker，等新程序再完整清除包含 Logs。手動 Local／Workspace-disabled reset 原契約保留。
- Build 1 補舊 Gateway 404 的明確升級提示及 unknown token 不清除回歸；先升級保留 Built-in 的 additive Cloud 0.9.2，再升級 Windows／staging，不能拿舊 Gateway 當新同步已完成。Build 0 的治理 PR 標記格式已改為純文字。
- 啟動、60 秒及重連以 POST /v1/runtime/sync 一次刷新 Device／binding／權限；15 秒連線檢查不每輪載入全員工，保留原光貿同步週期。Built-in snapshot 沿用既有 owner，revision 與 rows 同一查詢；拒絕舊 revision 覆寫。
- CYID 採 canonical Contract 1.0.3 private invalidation，中央仍是唯一權威；背景不登入、不保存員工帳密、不建立 Session，只移除失效 proof。同一 Workspace／Device／Token 的 1.0.2 cache 在升版後保持原範圍，不要求重加裝置。
- 人工結案／折讓及 Built-in 中央帳號操作改由最終 core／server 驗證，對話只收輸入；開窗權限不代替最終 mutation。儲存設定重新驗證管理員並防舊 provider 設定覆寫。
- 明確同裝置撤銷先落 durable marker，停止工作後關閉；新程序等待舊程序結束，再由既有 recovery 清除 Data／Cache／Logs 與 portable 所有非發行檔案。marker 最後移除，partial wipe／kill 續清；每個可攜資料夾只允許一個程序寫入。401／一般拒絕／Workspace disabled／503 不誤清除。
- 發行包保存並驗證 package-files.json，保留程式必要 binaries／文件；不刪可攜資料夾以外檔案、不自動重送光貿結果不明請求。版本 Patch +1、BUILD 0，Cloud 0.9.2、CYID 0.3.6／Consumer Impact BACKWARD_COMPATIBLE。Windows／實機驗收依現行交接與 TODO，未建立 tag／Release。

## V2.6.16 — 2026/10/10（服務連線與原有降級，工程版）

- 依使用者最終定案分開檢查光貿及雲端驗證，雲端 transport／timeout／暫時 503 且光貿正常沿用既有降級單機；不另建 Local authority 或修改 Workspace。
- 光貿不可達停用開票、查詢、作廢、折讓確認、PDF、同步；僅 Cloud 正常時雲端功能仍走原權限。雙斷線原生 modal 阻擋全部業務，每 15 秒及手動重新檢查，任一恢復回對應狀態。純 Local 只依光貿檢查正常／阻擋至恢復。
- 帳密／權限／Device 拒絕不能降級；已知 Device 拒絕清相應身分快取，後續 503 不復活。保留原設定／Token／發票及輸入；恢復不自動開票／重送，原結果不明流程保留。
- 共用 ServiceConnectivity 收斂既有光貿 probe，核心在業務請求／本機變更前防護；MainForm 為單一檢查及提示 lifecycle owner。Cloud 0.9.1 authenticated discovery 經現有 private Binding 檢查 CYID canonical health，不建立 Session 或第二 authority。API 1／marker 8／storage 13／Contract 1.0.2 不變，CYID Consumer Impact: NONE。
- 新增四狀態／純 Local／503／HTML 503／deny→outage／恢復／零業務請求與資料保留回歸；Windows 增加原生 modal／不可略過／重新檢查與關閉 smoke。CI／工程包看 PR #380 精確 head，人工 RC AA／AB、staging 及正式切換尚未完成。
- 新獨立 Patch +1／BUILD 0；同步現行文件及 TODO，未部署、migration、建立 tag 或正式 Release。以下 V2.6.15 transport-only／503 拒絕為當時歷史，已由本版最終定案取代。

## V2.6.15 — 2026/10/10（CYID Consumer 工程版，未部署／正式切換）

- 依 CYID canonical Consumer Contract 1.0.2 新增 CyIdIdentityProvider；集中式 provider owner 保留 Local／Built-in 模式，CYID 確認後不自動降回舊 authority。
- 每次員工驗證由 CYInvoice Worker 經 private IDENTITY Service Binding 登入、Resolve、執行、finally Logout；Session 不傳給 Windows、不保存。Device Token 只代表裝置，Employee／App Access 由 CYID 判定，兩種 Workspace 明確綁定。
- 真正 Windows transport 中斷才使用原裝置最後可信的 protected offline cache；Role／scope／本機離線 proof 整筆 DPAPI 保護。重新連線採最新 CYID，拒絕／停用／憑證失效清除該員工快取。HTTP 503、錯誤回應與取消不轉離線。
- 裝置改名、撤銷、配對／邀請沿用既有流程及 audit owner；0013 forward migration 保留 Built-in 邀請歷史，獨立保存外部 CYID 邀請人 identity，不建立假 Built-in 帳號。
- Windows 確認 CYID 綁定後隱藏帳號管理；加入不讀 Built-in Employee snapshot，忘記密碼改提示至 CY Web。未接入的舊 Worker 保持 Built-in。
- Source Cloud 0.9.0／API 1／marker 8／storage 13，新增 cyid-consumer-v1；預設未啟用 CYID。沒有修改 CYID provider／canonical contract，CYID Consumer Impact: NONE。
- 本機 TypeScript、real-provider gateway／onboarding／migration regression、C# Cloud contracts、Void workflow 29／Allowance 12／SQLite 13 通過；Linux WinForms cross-build 只作編譯預檢，真正 Windows CI／封裝及實機 acceptance 另記交接。
- PR #380 整合 source，canonical adoption 另由治理 #381／2.3.34 完成；本輪精確 head CI／測試包以 #380 checks 為準。
- 10/10 補原 Workspace 接續 regression：同一 Device／Token、consumer history 及本機設定／資料在 authority 切換後重開保留，拒絕改綁不同業務 Workspace。文件明定升級／帳號授權／原裝置 staging 驗收先於啟用，不要求重建或重新加入；離線驗證不等於光貿離線開票，HTTP 503 不 fallback。本次只新增測試／文件，不修改產品 VERSION／BUILD 或部署。
- 新獨立功能按 Patch +1、BUILD 0；不宣告既有實機待辦完成，未建立 tag／Release。

## V2.6.14 — 2026/10/10（工程測試版，未正式 Release）

- 裝置管理直接顯示「目前有 N 台使用中的裝置」、原有撤銷說明與 active 清單；移除分頁、狀態及撤銷時間欄。撤銷採共通 Danger button，最後一台防護與目前裝置安全重置維持。
- 新增裝置流程搬到獨立 CloudAddDeviceForm；保留配對／邀請、一次性碼、狀態回查與重開恢復。標籤按文字寬度配置，欄位同欄、垂直置中，避免固定窄欄裁切。
- 「使用版本」為該裝置最近成功回報的 VERSION／BUILD；每次程式啟動只更新自己的版本與 server UTC 最後使用時間。離線或舊版尚未回報時保留既有值，不虛構即時在線狀態。
- 更改裝置名稱使用中央超管執行時驗證，限定同一 Workspace 的 active 裝置；並發撤銷／權限變更會拒絕。改名與稽核同一交易，forward migration 0012 保留既有 audit、增添 device_renamed。
- Cloud source 0.8.9／API 1／compatibility marker 8／storage Schema 12；新 capabilities device-usage-v1／device-rename-v1。撤銷紀錄仍保留雲端資料庫及既有 API，只從程式清單隱藏。
- 本次獨立功能需求推進 Patch、BUILD 歸零；已同步 main 6d997be0，保留先前分流、邀請修正與介面。自動化、部署及工程包證據以現行交接／PR 精確 head 為準；實機 RC Z 尚待驗收，未建立 tag／正式 Release。

- V2.6.14 功能 source db1a3b95 的 Governance #1171／Cloud #377／Windows #266 全部通過，development staged Run #8 attempt 2 已驗證 Cloud 0.8.9／Schema 12／新 capabilities。0012 一次套用，重跑無 pending，Device aggregate／revoked 歷史與 FK 正常。初次立即 health 尚讀到 0.8.8，相同 source 重跑通過；收尾文件更新不另推進 VERSION／BUILD。

## V2.6.13 — 2026/10/09（工程測試中，未正式 Release）

- 依使用者新介面要求統一所有應用程式按鈕的小圓角；共用 RoundedButton 的 state／render／lifecycle，移除舊 NoFocusCueButton 與各自重複繪圖事件。品牌匯入、Primary、Danger、PDF 版型與商品列刪除均納入盤點，保留原生操作及鍵盤行為。
- 處理中／上傳問題共用 NativeListViewHost，零筆顯示「無資料」，依筆數縮放，最多十二列並受工作區限制；不再顯示大量空行。保留原有唯讀／刪除／行政結案核心。
- 作廢／折讓紀錄固定五列，超過以原生捲軸查看；上方發票資訊取得剩餘高度，長文字可捲動。
- 裝置管理改顯示「加入時版本」，移除舊 informational version 的 +Git SHA，新加入使用 VERSION／BUILD；套件攜帶並驗證 BUILD。未新增 Cloud heartbeat，不宣稱歷史資料是目前執行版本。
- Startup smoke 增加原生 Enter／Escape、停用與圖片保留、待辦 0／1／4／20／1／0 列、歷史 0／1／5／8 筆與版本格式／套件身分檢查。96 DPI 實機視覺驗收及 125／150 DPI 仍待測；CI 結果以 PR 精確 head 為準。
- 此為邀請修正以外的新介面需求，Patch 推進、BUILD 歸零。PR #216 尚未合併；未建立 tag／正式 Release。

## V2.6.12 — 2026/10/09（工程測試中，未正式 Release）

- 修正「邀請碼＋超管帳密」首次加入：Cloud claim 已完成後清空密碼觸發 TextChanged，原本會清掉 preview，接著讀取 preview.WorkspaceId 發生 NullReferenceException。本次加入先保存已確認的 Workspace；清密碼仍使可編輯授權失效，執行中的 Workspace 驗證不受影響。
- 保留 Device／Workspace／Employee authority／protected snapshot 全部驗證及 Pending Token 復原。使用者回報舊包第一次錯誤、重試顯示 Invitation is unavailable、關閉後第二次開啟成功；這是已使用邀請與本機完成流程中斷，不代表重新使用邀請成功。
- 首次使用說明改為左對齊的兩點，單機建立本機超管、雲端沿用中央帳號分開顯示，於完整語句處折行。
- Windows startup smoke 增加真實 TextChanged／Next／Join／HTTP client／protected persistence 回歸：邀請首次完成、配對完成、Workspace mismatch 拒絕、claim 後 snapshot 中斷及重開復原、編輯後禁止未確認 claim。全部使用合成 fixture，不呼叫 live Cloud。
- 此項是 V2.6.11 處理中分流以外的新 bug／說明調整，依共通版本規則 Patch + 1、BUILD 歸零。Cloud API／schema／deployment 不變；PR #216 精確 head CI 與 engineering Artifact 以即時結果為準，修正版人工首次加入驗收仍待完成。

## V2.6.11 Build 2 — 2026/10/09（工程測試中，未正式 Release）

- 已開立清單新增與「上傳問題」並排的「處理中」按鈕及數量；兩者共用同一清單／明細實作。
- 正常上傳狀態 1／2／3／31／32、官方確認處理中的作廢及正常等待確認的折讓顯示於處理中；真正查詢失敗、結果不明與需要人工處理／衝突的折讓保留上傳問題。
- 處理中取自現有 invoice metadata／workflow，不另建排程或 pending store；完成後移除，禁止勾選刪除及手動標記成功。
- 行政結案共用同一核心，支援處理中作廢超過保留期後由管理員停止本機追蹤；一般回查失敗不會被誤當作廢待辦。
- 保留本批原有修正：移除作廢確認的黑色發票號碼遮罩、文字與預覽仍隱藏；等待作廢使用半形括號；折讓「已人工處理」採 Danger button。
- 已同步 main 3b22f9f8；功能 commit 66ec3671 的 Governance #1137、Cloud #369、Windows #258 全部通過，含 Employee void workflow 29/29 與完整封裝。PR #216 尚未合併，Windows 實機／AMEGO live 驗收未宣稱完成。
- 10/09 文件彙整：統一現行版本／資料格式／Cloud 0.8.8 與驗收停點，封存舊階段交接為歷史，TODO 分開已完成、待實測、未實作及延後。文件整理不推進 VERSION／BUILD。

## V2.6.7～V2.6.10 — 2026/09/28～29（已整合至 main，未正式 Release）

- V2.6.7 Identity Foundation：AppPrincipal／AppRole／IIdentityProvider、Local／Built-in provider 與集中選擇；USER role 一次性 schema／forward migration。
- V2.6.8 Authority Freshness：Online operation 使用最新中央 credential／role／enabled；僅 transport outage／timeout 可採最後可信 protected offline cache，其餘 fail closed。
- V2.6.9 Device Revoke：inventory／revoke／Token invalidation／history／audit 與 Windows 裝置管理，保留 LAST_ACTIVE_DEVICE。
- V2.6.10 Cloud → Local：雙重確認、關閉程式後 revoke／self-status 確認再 wipe，結果不明保留資料與下次啟動 recovery；Build 1／2 整理 engineering 收尾。
- 四包已經 ede1fa37 整合至 main；main CYInvoice 版本後續為 V2.6.10 Build 2。2026/09/29 development staged Run #7 已驗證 Cloud 0.8.8／API 1／compatibility marker 8／storage Schema 11；部署前無未套用 migration。人工 A/B/C acceptance 仍待完成，證據見 CLOUD_WORK_HANDOFF.md 及 9/29 歷史快照。

## V2.6.6 Build 3 — 2026/09/25（工程測試中，未正式 Release）

- 新機加入改為「立即配對」或「新裝置邀請」兩條路徑；移除 Workspace 識別碼加超管帳密的舊直連方式。邀請寄至已驗證的超管信箱，含 Cloud API 網址和 72 小時一次性開通碼，可撤銷；B 機以開通碼與超管帳密加入。
- A 機顯示 Cloud API 網址及配對／邀請進度，新機成功加入後可回查狀態。Cloud 新增安全操作紀錄資料表，暫不提供查看介面。
- 修正配對端點資料表名稱與既有 D1 migration 不一致；Cloud implementation `0.8.5`、API `1`／Schema `9`。本版尚未部署 Cloudflare，亦未完成 A／B 實機驗收。
- Build 1：重寄邀請時，舊邀請若尚未使用會自動撤銷並寫入安全紀錄；重開 A 機視窗仍優先顯示可撤銷的有效邀請。
- Build 2：邀請碼成功加入或手動撤銷時，裝置狀態與安全操作紀錄以同一 D1 batch 寫入。
- Build 3：縮小首次加入雲端視窗，修正操作按鈕換行遭裁切，並加入視窗尺寸與按鈕邊界的啟動檢查。

## V2.6.5 Build 4 — 2026/09/24（工程測試中，未正式 Release）

- 雲端忘記密碼改為兩步：先核對員工編號與已驗證 Email，成功才寄驗證碼；第二步輸入驗證碼與兩次新密碼。重寄按鈕依 API 回傳的實際 `resendAfter` 倒數。
- 原管理員視窗改名「管理員帳號管理」；一般員工通過密碼驗證後進入自己的「帳號管理」，可用舊密碼更改密碼，或將新 Email 驗證後更新；姓名仍由管理員處理。
- 作廢／折讓紀錄格線、摘要與上傳問題清單的固定欄寬、提示文字改善納入本次測試包。
- Cloud implementation `0.8.4`、API `1`／Schema `8`；此原始碼尚未部署到 development，實際雲端狀態需另行查核。

## V2.6.5 Build 1 — 2026/09/24（工程測試中，未正式 Release）

- 首次啟動選擇單機版或直接加入既有雲端 Workspace；直接加入不建立本機帳號，可選配對碼，或 Workspace 識別碼＋該 Workspace 超管帳密／Email OTP。
- 直接加入後先核對 Device identity、中央 Employee authority 並同步受保護的中央帳號快取，成功後才切換雲端模式；保留 Pending Device Token 供網路不明結果恢復。
- 原本「單機版之後加入雲端」的本機管理員驗證與完整 Employee Transition 保留；配對碼只授權裝置，不授予個人權限。
- Cloud implementation `0.8.3`、API `1`／Schema `8`；已整合併行進度的密碼復原 migration `0008`。新增直接加入端點與超管登入速率限制。待 development 部署及 Windows A／B 實測。
- Build 1 修正首輪 CI 發現的合約測試 JSON 字串語法與新視窗重複 Dispose；裝置管理視窗的啟動尺寸檢查也同步涵蓋新增的 Workspace ID 欄。

## V2.6.4 Build 1 — 2026/09/22（工程測試版，未正式 Release）

- 修正第一個 Workspace 建立 SQL 的欄位和值數量不一致，避免 Email OTP 驗證成功後 D1 整批回滾；新增直接套用 Schema 7 並執行正式 bootstrap SQL 的回歸測試。
- bootstrap 寫入失敗且資料庫仍無 Workspace 時改回報 `BOOTSTRAP_FAILED`，不再誤報 `WORKSPACE_ALREADY_INITIALIZED`；D1 batch 仍維持原子回滾，不留下半套 Workspace／Device。
- 首次雲端初始化視窗增加欄位與狀態區高度，避免高 DPI 下欄位裁切；Email 驗證碼改為靠左顯示。
- 修正第一個 Workspace 寄送 Email OTP 時，未限定範圍的 challenge 將 `scope_key` 空字串寫入 D1 Schema 7 而被 CHECK constraint 拒絕的問題；未限定範圍現在一律以 `NULL` 保存與查詢。
- Worker 主路由等待非同步 handler 完成，使 D1／Email 等執行期錯誤能由既有 JSON error boundary 統一攔截，不再由 Cloudflare 直接回傳純文字 runtime error。
- Windows Cloud client 遇到非 JSON 回應時改回報穩定的 `CLOUD_INVALID_RESPONSE`，不再把原始 JSON parser 訊息或 provider 回應內容顯示給使用者。
- 新增 bootstrap NULL scope、實際 Workspace／Device SQL 與非 JSON Cloud 回應回歸測試；Cloud implementation 推進至 `0.8.2`，API `1`／Schema `7` 不變。
- 本版只提供 engineering 測試包；不得自動建立正式 tag／Release。

## V2.4.2 — 2026/09/19（正式 Release）

- 正式 Release：`cyinvoice-v2.4.2`，target commit `266427abb10e48ee4c1beee55c525159b25c47cd`。
- `CYInvoice Stable Release` workflow 從 `main` 重新執行正式身分檢查、機密資料掃描、restore、warnings-as-errors build、WinForms startup smoke、core parity tests、Windows x64 release build／PE／package 驗證、portable smoke、SHA-256 與 GitHub Release 發布，全部成功。
- 正式包：`CYInvoice_V2.4.2.zip`；SHA-256：`7a65878c52e65d1088cdb298dd2c5e93d76afa9ebd01a0136d2b245d411df502`。
- 測試環境加入 CYInvoice 專用 OrderID namespace，避免光貿共享測試池的不同公司／執行個體互相撞號；一般 UI 仍顯示原始可讀 OrderID，不顯示 technical prefix。
- 測試發票 discovery、開立後回查與 query 流程同步支援 namespaced OrderID，正式環境不套用測試 namespace。
- 設定頁 Enter 導覽再修正，正式環境欄位使用明確鍵盤順序，不讓 Enter 誤觸發不相關操作。
- 已開立紀錄清單加入可點擊排序表頭並整理欄寬；來源與 Order ID 欄位加寬。
- V2.4.2 Build 1～5 持續修整表頭排序、設定／管理視窗、來源與 Order ID 欄寬、已作廢紀錄可讀性、會員載具預覽與作廢狀態呈現。
- 正式發布前依規則將同一工作項目的工程 `BUILD` 從 5 重設為 0；沒有另外改動發票核心安全語意。

## V2.4.1 — 2026/09/18（工程測試版，未單獨正式 Release）

- 「上傳問題」視窗上下兩個 ListView 使用直向／橫向格線；欄位預設寬度不足時可由原生水平 scrollbar 查看完整內容。
- 「開立失敗」來源欄縮窄，主要空間留給失敗原因；光貿 API technical field／code 轉為較易理解的中文摘要，原始 API 訊息仍保留在本機供診斷。
- 「刪除」在未勾選時不顯示 `(0)`，勾選後才顯示 `刪除(N)`。
- 主清單「上傳問題」按鈕與左側操作按鈕統一垂直 margin。
- 來源「同步／更新」改為較緊湊的圓角色塊；同步與更新以不同色系辨識，來源文字仍保持正常可讀字級。
- 本版內容後續併入 V2.4.2，沒有建立獨立正式 tag／Release。

## V2.4.0 — 2026/09/18（工程測試版，後續併入 V2.4.2）

- 本機發票、商品明細與人工買方名稱由 JSON 正式遷移至 `Data/CYInvoice.db`；`settings.json` 繼續保存安全設定。第一次啟動以暫存 SQLite transaction 匯入並驗證舊 `invoices.json`／`buyer_names.json` 後才切換，舊 JSON 保留；損壞資料不得被空白 DB 覆蓋。
- AMEGO／光貿官方資料定為發票內容權威來源；SQLite 定位為本機 Cache 加上 CYInvoice metadata。遠端買受人、統編、金額、商品、作廢與上傳狀態變更都視為正常官方更新，不以內容差異建立衝突。
- 正式環境加入 `/json/invoice_list` 最近 3 天同步；程式啟動、每 5 分鐘與手動重新整理共用同一套同步核心，手動冷卻 30 秒，同步重疊時直接略過、不排隊。
- 每個本機日第一次自動同步會校對目前兩月發票期別與上一期別；測試環境只回查本機當日已知測試資料，不掃描共享測試池。
- 雙擊任一發票開啟詳細資訊前一律再執行 `invoice_query` 更新該張 Cache；若無法確認最新官方內容，不以舊 Cache 冒充最新資料。
- 正式環境本機 Cache 只保留目前及上一個兩月期別，測試環境只保留當日；能確認過期的舊資料會連同對應 PDF／預覽 Cache 與舊 sync issue 清除，舊 `Unknown`／`Changing` 不再永久保留。
- 新增持久化 `sync_issues`：invoice list／query 失敗、光貿查無、結果不明仍無法確認、本機 SQLite 寫入失敗、解析／比對失敗及無法唯一匹配等真正技術問題才記錄；相同未解決問題去重，後續成功同步可自動解決。
- 同一發票號碼或 OrderID 若對到多筆本機紀錄，停止自動猜測並建立 `ambiguous_match`；已開立清單新增「同步問題」視窗，可查看目前帳號未解決問題並手動標記完成、刪除或重新整理。
- Windows CI 已涵蓋 SQLite migration／retention、invoice sync、sync issue、sync coordinator、WinForms startup smoke（含同步問題視窗）、Windows x64 package、PE／layout 及 packaged startup smoke。
- V2.4.0 當時只作為工程測試基準；後續 V2.4.1／V2.4.2 繼續收斂並最終由 V2.4.2 正式發布。

## V2.3.0 — 2026/09/18

- 正式 Release：`cyinvoice-v2.3.0`，由 `main` 的正式 Release workflow 重新執行 source confidentiality scan、warnings-as-errors、WinForms startup smoke、core parity tests、Windows x64 build、PE／package 驗證、portable smoke 與 SHA-256 後發布。
- 新增鼎新 ERP `.xlsx` 直接匯入：固定讀取「單頭資料／單身資料」，一個活頁簿只允許一張銷貨單；單頭使用銷貨單號、統一編號、客戶全名、本幣合計，單身以品名、數量、金額為必要欄位。
- 鼎新單身 `金額` 為權威值，單價由金額 ÷ 數量推導；支援合法負數折扣列，單身金額合計必須等於單頭本幣合計。若有「單位」欄，會保存並送至 AMEGO `ProductItem.Unit`。
- 手動開票與鼎新匯入共用完整 8 碼統編自動查詢、API 名稱基準、人工名稱不一致紅字與欄內 `↻` 強制 API 重查；過時的非同步查詢結果不得覆蓋最新輸入。
- 鼎新確認畫面保留原始買方資料供比對，修改統編／買方名稱後必須先「套用資料」才能開立；檢查狀態改用「可開立／已有開立紀錄，禁止重送／開立結果待確認，禁止重送」等直接語意。
- 手動自動訂單編號改為 `MYYYYMMDDXXX`，與外部來源訂單編號命名空間分離。
- 紙本發票加入直接列印：一般消費者固定 A4 雙面長邊，公司統編先選五種官方版型；使用 CYInvoice 記住的雙面發票印表機，不修改 Windows 全域預設印表機。
- 已開立清單來源加入鼎新 ERP，篩選列重新統一垂直中心線；單擊發票號碼可直接複製，相關提示字級同步調整。
- 測試環境公司戶不送真實買方統編／名稱；改用公開測試統編 `28080623`，送出前向光貿 API 查詢該測試統編名稱，查不到即停止。
- 主視窗底部右側新增 `Copyright © 2026 C.C. Liu, Chihyuan Co. All Rights Reserved.`；鼎新確認列及買方名稱重查按鈕完成多輪實機 UI 對齊修正。

## V2.2.0 — 2026/09/16（工程測試版，未正式 Release）

- 紙本發票詳細資訊會自動取得官方 A4 PDF，將第一頁轉成獨立 PNG 快取圖後嵌入視窗；一般消費者只預覽第一頁，公司統編預覽 A4 整張，列印仍使用未裁切的原始 PDF。
- 公司統編的「列印發票」改為五張去識別化圖像卡片，選擇後才取得 A4 整張、A4（地址＋A5）、A4（A5 內容）、A5 或 QRcode_A4 的真實 PDF。
- 會員載具不呼叫紙本 PDF API、不建立 PDF Cache，也不顯示列印按鈕；詳細資訊改為左側不可掃描的模擬電子發票、右側真實交易明細。
- PDF 列印視窗加大並設定較適合完整 A4 的初始縮放，仍只使用 Edge PDF Viewer 原生縮放、下載與列印工具。
- 新增 `Cache/InvoicePreview` 隔離測試／正式環境、日期與發票號碼，損壞或非 PNG 預覽不得沿用；本版只提供測試包，不建立正式 Release。
- 可攜資料夾將 WebView2 managed DLL 集中至 `Runtime/WebView2`，並新增解壓後套件啟動 smoke test，避免整理依賴後只通過 build、實際包卻無法啟動。

## V2.1.0 Build 1 — 2026/09/16（工程測試版，未正式 Release）

- 已開立、同環境、有發票號碼的紙本發票即可直接嘗試 `/json/invoice_file`，不再等待上傳狀態 `99`；上傳燈號仍維持獨立狀態顯示，光貿尚未提供 PDF 時改為明確提示稍後再試。
- 發票詳細資訊改成緊湊三欄摘要，空白錯誤訊息與備註不占高度，商品明細取得主要顯示空間，底部只保留「檢視 PDF」與「關閉」。
- 一般消費者按「檢視 PDF」後直接開啟 A4；公司統編改為按下後顯示五種官方版型選單，再依選擇開啟。
- PDF 視窗移除自製縮放、下載、列印及狀態工具列，改由 Edge PDF Viewer 原生工具列處理並填滿整個視窗。
- 測試包排除三個不影響執行的 WebView2 API 說明 XML，維持乾淨的可攜資料夾結構。

## V2.1.0 — 2026/09/15

- 新增光貿 `/json/invoice_file` 官方紙本發票 PDF：只允許同環境、已開立、有發票號碼、紙本且上傳狀態 `99` 的紀錄取得。
- 一般消費者固定使用 `download_style=0`；公司統編支援 0、1、2、3、5 五種官方版型，版型選擇位於發票詳細資訊。
- PDF Cache 依測試／正式環境、日期、發票號碼及版型隔離；同日重用、隔日重新取得，壞檔不沿用，下載內容須通過大小及 `%PDF` 檔頭檢查。
- 新增 WebView2 內嵌 PDF Viewer，提供縮小、重設、放大、另存下載與系統列印；官方短效檔案網址只接受光貿 HTTPS 網域。
- 測試包版本標題不再加「工程測試包」，改以套件內 `Channel: engineering` 中繼資料及 Artifact 名稱區分；本版不建立正式 Release。

## V2.0.2 — 2026/09/15（工程測試版，未正式 Release）

- 依既有定案恢復商品鍵盤流程：單價按 Enter 後，若已有下一筆明細就移至該列品名；若目前是最後一筆則自動新增明細並移至新品名欄。Tab 保留原本流程。
- 工程 Artifact 的版本識別文字改為「工程測試包（非正式 Release）」並加入 package channel 驗證，避免測試包被誤標為正式版。
- 全面複核 V2 現行文件：區分現行測試版與最新公開 Release、修正首次設定 Enter 順序與主視窗預設尺寸、更新商品表格實際繪製方式及單價 Enter 驗收文字。
- 正式 Release 改為只有使用者於當次工作明確要求後才啟動；本版只提供測試包。

## V2.0.1 — 2026/09/15

- 修正批次開票逾時後沿用已取消權杖，導致 OrderId 安全回查可能立即取消的問題；改為獨立且最多 20 秒的唯讀回查，無法嚴格確認時仍維持「結果不明、禁止重送」。
- 上傳狀態只接受與指定發票號碼唯一吻合的回覆；`99` 顯示完成、`91` 顯示錯誤，其他未知狀態維持未確認，不得套用其他發票的狀態。
- 補強同時重複開票序列化、測試／正式環境隔離、逾時回查、上傳狀態精確配對與錯誤回覆等安全回歸測試。
- 單價欄按 Enter 後儲存內容並留在原儲存格；Tab 鍵仍依既有鍵盤流程移動。
- 整理 V2 待辦：移除不再需要的舊版 Data 相容驗證，封存 Go 舊版驗收及已完成實際開票的兩項 MO店+ 驗證。

## V2.0.0 — 2026/09/15

- C#／WinForms 完成 Windows 實機驗收並正式取代 Go／Win32，成為 `main` 唯一產品線；版本來源統一為 `VERSION=2.0.0`、`BUILD=1`。
- 正式環境主標題改由 AMEGO API 顯示公司名稱與統編；測試環境仍清楚標示光貿測試公司。
- 修正發票基本資料 Radio 與欄位垂直對齊、統編／買方名稱下緣裁切、商品與已開立清單 scrollbar 及欄界線。
- 匯入確認按鈕移至中間並避免下緣裁切；移除確認按鈕的張數文字，摘要列放大並以紅色強調已選張數。
- 修正商品明細刪除事件被暫時編輯器攔截，需重複點擊才刪除一列的問題。
- 正式 Build／Release 當時改用 .NET Windows runner；V2.0.2 起又收斂為「只有使用者當次明確要求 release 才啟動正式 Release workflow」。
- Go／Win32 V1.1.0 固定保留為上一個公開回退版本，不再保留於 `main` 現行 source。

## V1.1.0 — 2026/09/11

- 移除程式圖示右上折角，保留藍色文件外框、白底、`INV` 字樣與多尺寸 ICO，使 16×16／24×24 等小圖示更容易辨識。
- 公司統編查詢收到光貿 code 99 或成功但沒有相符名稱時，視為「API 已正常回覆但查無統編」；右上 API 狀態不再誤標異常，改提醒「查無此統一編號，請再次確認或自行輸入買方名稱。」
- 已開立清單的統編欄再加寬，欄寬同時量測 8 碼統編與本機既有一般消費者 `0000000000` 顯示值，避免比例字型造成省略號。
- 版本規則簡化：測試／小修正推進 PATCH，正式開發階段推進 MINOR，只有非常大的產品或相容性變更才推進 MAJOR。
- 沿用 rc.9 已完成的固定 scrollbar 槽、斑馬紋、雙擊唯讀詳細資訊、千分位與發票安全規則；invoice core 未改動。

## V1.0.1-rc.9 — 2026/09/11

- 依 Windows 實機結果重做兩個 ListView 的固定垂直捲軸槽：資料未滿時以 disabled 原生 `SCROLLBAR` 子控制項占住 `SM_CXVSCROLL` 系統寬度，資料溢出時原地換成 ListView 自己的原生 scrollbar；欄寬永遠只使用槽左側寬度，不再產生水平 scrollbar 或未命名假欄位。
- 已開立清單補滿不可選取、不可雙擊且不寫入資料檔的 UI 空白列，使沒有資料、少量資料及資料溢出時的整個可見區都維持一致斑馬紋。
- 已開立清單雙擊實際資料列會開啟原生唯讀詳細資訊，僅讀取該筆本機紀錄，不發 API、不修改發票狀態，也不提供重送捷徑。
- 已開立清單的來源欄固定足以完整顯示 `MO店+`、`好賣+`、`iOPEN`；訂單編號固定足以顯示 15 碼 `115102696774269`；統編欄加寬，三欄不參與縮欄。
- 商品單價在編輯時維持純數字，Enter、Tab 或離開儲存格後改以千分位顯示；商品金額、預覽、成功訊息、已開立清單、匯入確認與金額總計同步使用千分位。右下三個金額數值改為相同右緣。
- 設定頁文字定案為 `App Key`；`統編` 與 `App Key` 的標籤左緣及輸入框左緣分別對齊，App Key 保留獨立完整一列。
- 修正 rc.7／rc.8 文件把 `SIF_DISABLENOSCROLL` 描述為已穩定保留捲軸槽的錯誤；實機證明該方式仍會讓 ListView client width 變動，本版改以明確原生占位控制項處理。
- invoice core、開票判定、防重、環境隔離與結果不明禁止重送規則均未變更。

## V1.0.1-rc.8 — 2026/09/11

- 修正藍色底線頁籤切換後粗體文字被裁切：Tab 以選中粗體的實際尺寸建立標頭寬度，增加原生 padding 並移除重複文字縮排。
- 設定按鈕恢復固定尺寸，依原生 Tab item 的實際高度置中並靠右，不再隨主視窗最大化比例放大或向下偏移。
- ListView 與 scrollbar 保留 Windows theme；只有內建 `SysHeader32` 使用原生 classic renderer，使表頭底線與垂直分隔線清楚接合。
- 當時嘗試以 Win32 `SIF_DISABLENOSCROLL` 固定保留垂直 scrollbar；後續 rc.9 實機確認 ListView client width 仍會變動，因此此項不能視為已完成，已由 rc.9 的原生占位控制項取代。
- 商品與已開立清單在資料更新期間暫停重畫，完成欄寬與 scrollbar 狀態後一次更新，避免重新整理時 scrollbar 閃現及表頭錯位。
- 設定視窗加高；「MO店+ Excel 保護密碼」標籤加寬。「APPKEY」移至統編下方獨立一整列，標籤左緣與「統編」完全對齊。
- 保留 `LVS_NOSORTHEADER`、`HDS_NOSIZING`、無假欄位及發票安全規則，並更新 architecture guards。

## V1.0.1-rc.7 — 2026/09/11

- 主頁籤維持原生 `SysTabControl32` 的選取、鍵盤、通知與 page frame，標頭改為矩形中性色底、選中深藍粗體與 3px 藍色底線；移除 rc.6 的斜角／梯形畫法。
- 商品與已開立發票清單恢復 Windows theme 的原生 ListView／Header／scrollbar，不再強制 classic theme。
- 兩個清單當時嘗試在建立時保留垂直 scrollbar 槽；後續 rc.8／rc.9 實機確認此版本仍未穩定達成，最終以 rc.9 的固定原生占位控制項修正。
- 商品清單仍只有七個實際欄位、已開立清單仍只有十個實際欄位；欄寬總和改以實際可見 Header 寬度計算，最後的「操作」／「上傳」直接接到 scrollbar 槽，不建立或藏匿假欄位。
- 兩個清單使用 `LVS_NOSORTHEADER` 與原生 `HDS_NOSIZING`，取消表頭按壓、排序暗示、拖動重排及欄寬拉動。
- 商品清單初始高度增加 14px，完整顯示五列；備註、金額總計與底部按鈕等量下移，維持既有單一 layout。
- 修正 README 候選版號、VERSIONING 儲存庫名稱、測試環境 OrderId 說明、RC 測試清單標題與建置版本說明的歷史錯誤。
- 新增 architecture guards，防止梯形頁籤、classic ListView theme、可按壓／可縮放表頭及不固定 scrollbar 槽回歸。

## V1.0.1-rc.6 — 2026/09/11

- 主畫面與 modal 視窗統一使用 Win32 dialog navigation 支援 Tab／Shift+Tab；首次安全設定與設定密碼驗證保留明確 Enter 行為，避免主畫面 Edit 的 Enter 誤觸發開票。
- 主頁籤仍以原生 `SysTabControl32` 管理選取、鍵盤、通知與 page frame；僅標頭從建立時使用 `TCS_OWNERDRAWFIXED` 繪製舊式斜角／梯形外觀，不增加 refine／normalize 二次修補層。
- 設定按鈕整合至唯一 `relayoutGUI`，依 Tab 實際位置靠右對齊。
- 商品與已開立清單的表頭統一採原生 classic `SysHeader32`；商品欄位總寬貼齊 client width，剩餘寬度全部給品名且不顯示水平 scrollbar。
- 已開立清單新增唯一欄寬配置：開立時間、發票號碼、統編使用實際字寬決定最低寬度，剩餘空間全部給買受人，移除「上傳」右側假欄位。
- 上傳欄恢復紅／黃／綠燈號：99 綠、91 紅、處理中或尚未確認（含 status 0 的已開立紀錄）黃；未知狀態不得顯示為成功。
- 修正 AMEGO `/json/invoice_query` 實機可能以 JSON number 回傳 `invoice_date`／`invoice_time` 的相容問題；僅在 decoder 邊界正規化 string／number，不放寬既有嚴格發票核對規則。
- 新增 numeric date/time 回歸測試並保留原 string 格式測試。
- Windows CI #277 全綠：Go tests、architecture guards、go vet、Windows x64 GUI build、PE/Icon/manifest、ZIP 結構與機密掃描全部通過。

## V1.0.1-rc.5 — 2026/09/11

- 一般按鈕從建立時直接使用原生 Win32 `BUTTON`；含稅／未稅直接使用 `BS_AUTORADIOBUTTON`，移除 owner-draw 建立後再以 `BM_SETSTYLE` 轉換的雙層路徑。
- `SysTabControl32` 建立時直接使用最終 page-frame 尺寸，不再先建立窄 tab strip 再由 refine pass 二次放大。
- 主視窗 resize 收斂為單一 `relayoutGUI`；商品 ListView 欄寬只由唯一函式依 client width 計算，剩餘寬度全部給品名並禁止水平 scrollbar。
- 刪除 `ui_refine_windows.go` 與 `mo_confirm_refine_windows.go`；MO／酷澎匯入確認的最終座標、斑馬紋與 callback 直接合併回正式建立路徑。
- 匯入確認視窗改用共用 `runOwnedModalWindow`；設定視窗只保留單一 compact loader/save 路徑，降低 nested loop、舊 HWND 與重複狀態同步風險。
- 新增 `apps/CYInvoice/AGENTS.md`、`docs/DEVELOPMENT_RULES.md` 與 UI architecture tests，明確規範「Win32 原生優先、單一路徑、paint 不改 state」只適用 CYInvoice，不影響 CYapps 其他程式。

## V1.0.1-rc.2 — 2026/09/09

- 重新整理 Go + Win32 畫面骨架：Tab 只保留頁籤列，不再以大型白色子頁覆蓋原生灰色主背景；區塊標題直接使用 GroupBox 標題，移除造成白底與框線斷裂的假 STATIC 疊圖。
- 商品與已開立清單移除舊 `LVS_OWNERDRAWFIXED` 整列手繪路徑，統一由原生 `SysListView32` 維持表頭、格線與捲軸，僅以 `NM_CUSTOMDRAW` 設定逐格斑馬紋、唯讀金額色與狀態文字。
- 修正自訂訂單欄位啟用外觀、表格刪除文字、右下金額殘影及設定視窗原生灰底；所有可編輯欄位為白底，鎖定欄位為灰底。
- 匯入確認的解析、公司名稱重查與逐張開立均在背景執行；逐張送出保留順序與進度，單筆逾時或錯誤不會凍結 Win32 訊息迴圈。
- 光貿成功判定改為唯一且完整核對 API OrderId、發票號碼、銷售額、稅額、總額、DetailVat 與未作廢狀態；已作廢訂單重開使用 `-R2`、`-R3` API OrderId。
- 加入單一程序鎖及程序內開立序列化；遠端已開立但本機保存失敗改為獨立狀態，禁止誤當一般失敗重新送出。
- 測試環境依最新決定保留原始 OrderId；姓名、真實統編、聯絡資料、商品文字、備註及會員載具識別仍全面去識別化。
- 強化 Excel 讀取上限、workbook relationship 解析與 Excel COM 安全開檔；新管理密碼改用 PBKDF2-HMAC-SHA256 210,000 次並加入登入失敗節流。

## V1.0.1-rc.1 — 2026/09/07

- 設定改回頁籤列右上角的一般按鈕，按下後開啟獨立視窗；每次開啟均先驗證管理密碼，取消或關閉立即重新鎖定。
- 開立發票與已開立發票清單的頁籤改為自繪，選中頁籤使用藍色框線、藍底與粗體，未選中頁籤使用灰底。
- 一般消費者的統編及買方名稱輸入框以灰底、灰字呈現停用狀態；切換公司統編後恢復正常可輸入外觀。
- 商品與已開立發票清單改為逐格實際填色，修正 Common Controls v6 主題覆蓋文字背景而看不到斑馬紋的問題。
- 商品欄寬預留邊框與垂直捲軸空間，預設不再出現橫向捲軸並完整顯示五列；唯讀金額欄使用獨立灰底。
- 新增明細不再預先驗證。最後開立時自動略過完全空白列，部分填寫的列才提示補齊品名、數量與單價。
- 一般靜態文字改用空心筆刷透明繪製，移除金額總計等文字後方的白色矩形。
- 首次安全設定再縮小一級並調整 MO店+ 密碼欄寬；選檔視窗統一標示一般 Excel 格式。

## V1.0.0 — 2026/09/07

- MO店+、酷澎與鼎新 ERP 統一使用同一個匯入確認視窗、逐張勾選、統編名稱處理、開立進度與結果保留流程；各來源只保留 Excel 欄位解析差異。酷澎已接通，鼎新 ERP 在取得實際樣本與欄位對照前會於相同視窗安全停止，不會送出。
- 光貿共用測試池的匯入發票全面去識別化：訂單編號末 7 碼遮蔽，買方名稱固定為「測試消費者」，真實公司統編改用官方公開測試統編，地址、電話、Email、捐贈碼與備註清空，商品名稱改為測試商品，會員載具顯碼／隱碼改用不含訂單資訊的測試 Email。本機紀錄仍保留原始資料，正式環境不套用替換。
- MO店+ 原始 OrderExport 的收件人、規格1／規格2、買方統編與會員載具欄位已逐欄對齊光貿轉檔；載具資料隨發票紀錄保存。
- 測試環境送出匯入發票時 BuyerName 固定為「測試消費者」，OrderId 末 7 碼遮蔽為 `*`，並以相同遮罩值回查。
- 選檔後立即開啟 MO 匯入確認視窗，在視窗內顯示解析、統編查詢及開立進度；開立欄改為原生 checkbox，摘要順序與說明文字依實機意見精簡。
- 匯入按鈕改為深藍圓角動作按鈕；三個原生清單的藍白斑馬紋加深，並在每個子欄繪製階段重新套用。
- 設定改為頁籤列最右側的受保護頁籤，每次進入均先驗證管理密碼、離開即重新鎖定；發票狀態重新整理不再改動 API 健康燈。
- MO店+ 選檔後改為獨立逐張確認視窗；可取消整批、取消單張、核對訂單／統編／名稱／拆稅金額／總額／項目數與狀態，只有按下確認才依勾選順序送出。
- 公司名稱依「本機人工記憶 → 光貿 API → 成功但空名稱才人工輸入」處理；API 技術異常不冒充查無名稱。人工名稱只在確定開立後保存，結果不明則等日後回查確認開立才保存。
- MO 官方金額明定為含稅最高準則；公司訂單保持含稅明細及 DetailVat=1，確認及送出拆為應稅銷售額與 5% 稅額，測試鎖定 1014 = 966 + 48。
- MO店+ 匯入改為直接解析原始 `OrderExport` 79 欄格式；官方「開立發票金額依品項／加總」是最高準則，商品售價及平台費用不得覆蓋，且不建立暫存轉檔。
- 依實際光貿轉檔結果鎖定 `6×113 + 3×112 + 65 - 65 = 1014`；公司統編訂單在送出前以買方統編查詢名稱。
- 修正視窗左上 Icon 資源 ID；首次安全設定置中，設定按鈕回到頁籤列內，匯入按鈕改為淡藍自繪樣式，單行輸入框縮短，商品清單固定五列高度並加深斑馬紋。
- 依 Windows 實機畫面縮小首次安全設定視窗與輸入框、移除文字灰底；管理密碼 Enter 跳至 MO店+ 密碼，MO店+ 密碼 Enter 直接儲存。
- 主畫面一般文字改為白底，開立頁與清單頁字級再縮小一級；加高並加寬原生頁籤，將設定按鈕整齊置於頁籤列最右側。
- API 狀態恢復右上垂直置中；異常原因改為下方更小的灰字，重試間隔由 60 秒改為 10 秒。
- API 健康判斷不再把已成功收到的 code 99 業務驗證回覆誤判成斷線；授權、簽章、帳號、服務、連線或逾時錯誤仍維持異常。
- 發票重新整理相容光貿查詢 `data` 的物件、一層陣列及巢狀陣列回覆，修正實機成功開票後無法重新整理的解碼錯誤。
- 一般消費者送至光貿的 BuyerName 固定為「消費者」。
- 首次安全設定整體字級縮小兩級，加入管理密碼 → MO店+ Excel 密碼 → 完成按鈕的 Enter 鍵盤流程。
- 商品明細移除永久 Edit 控制項疊圖，恢復原生 `SysListView32` 表頭、資料列與單格暫時編輯器；刪除按鈕改為每列命中區。
- 含稅／未稅改為圓點選項，選中項以淡藍色高亮；一般消費者仍固定含稅。
- API 異常時於狀態下方顯示小字原因，以單一計時器重試並避免健康檢查併發。
- 光貿回覆 code 15 時使用官方 `/json/time` 校正簽章時間，原 API 僅安全重試一次。
- 依最後正常 V1.0.0 截圖重建主介面：環境橫幅、API 狀態、原生頁籤、右上設定鈕、Excel 匯入區、發票基本資料、商品明細、備註、金額總計與完整開立清單。
- 主視窗可自由縮放與最大化，設定 1180×850 最小尺寸；縮放時所有控制項與清單欄寬同步重排。
- 已開立發票清單恢復日期、發票號碼、訂單編號、買受人、統編、來源及發票狀態篩選；預設日期為本月第一日至今天。
- 依實際 DeliveryList 與光貿轉檔結果加入酷澎 .xlsx 解析；依欄名辨識，不依檔名或固定欄位位置。
- 酷澎使用「應開立予買家之發票金額」建立買家發票；「應開立予酷澎之發票金額」不列入。
- 酷澎套用同來源＋原始訂單編號防重複規則，開立成功／結果不明禁止重送，明確失敗／已作廢才可重開。
- 酷澎尚無公司統編、數量大於 1 且無法整除單價等實際樣本時，採安全停止，不猜測欄位或金額。
- 依光貿官方規則恢復公司統編含稅／未稅輸入：未稅模式使用 DetailVat=0，銷售額為未稅明細加總、稅額為 5% 四捨五入、總額為銷售額加稅額。
- 一般消費者固定含稅；畫面仍拆分顯示銷售額與 5% 稅額，但送至光貿時依官方規則 TaxAmount 為 0。
- 商品明細改為可捲動的原生表格資料結構，保留 1～50 筆新增、選取、編輯與刪除基礎。
- 已開立清單恢復斑馬紋；失敗紅字、結果不明／資料變更中橘字、已作廢灰字刪除線，以及上傳狀態綠／紅／黃燈號。
- 依正確截圖補回紅框紅字刪除按鈕與金額總計分隔線。
- 公司發票已有明細時，含稅／未稅切換會自動換算；數量、單價與明細金額採固定 7 位小數，避免浮點誤差，並保證 `200 → 190.4761905 → 200`。
- API 指示燈改為實際執行唯讀統編查詢：查詢中灰色、成功綠色、失敗紅色，且在背景執行不凍結主視窗。
- 發行包依最新定案保留根目錄 `使用說明.txt` 與唯一當版 `V版本號.txt`，不建立 `Version` 資料夾，也不放 `todo.txt`。

## V1.0.0-rc.2

- 全新資料或舊測試設定不完整時，強制彈出首次安全設定視窗；管理密碼與 MO店+ Excel 密碼都完成後才能進入主程式。
- 首次判斷以有效的 `Data/settings.json` 安全欄位為準；空資料夾不會被誤判為已設定，損壞檔案也不會被覆蓋。
- 首次判斷會實際驗證 MO 密碼能由目前 Windows 帳號解密；跨電腦／帳號複製造成 DPAPI 無法解密時保留原檔並停止，不誤當成已完成或自動重設。
- 手動發票已接通光貿開立、統編名稱查詢、訂單反查及上傳狀態查詢。
- 送出 API 前先建立本機紀錄；網路或回應結果不明時只查詢、不自動重送。
- 同來源＋原始訂單編號的防重複規則已套用至手動與 MO店+ 開立。
- 手動商品明細可加入與刪除，資料層最多支援 50 筆並允許合法負數折扣明細。
- MO店+ 支援依欄名辨識 `.xlsx`，以及透過已安裝 Microsoft Excel 唯讀開啟受密碼保護的 `.xls`。
- 受密碼保護的 `.xls` 改由 Go 直接操作 Excel COM，不再透過 PowerShell 中介；密碼不寫入暫存檔或 LOG。
- 新增真實 ZIP/XML 結構的 `.xlsx` 解析測試，並相容光貿發票查詢的新舊欄名及物件／陣列回應。
- MO店+ 可依訂單編號分組多項商品、運費及負數補貼，確認後逐筆開立並保留個別結果。
- 正式環境的 MO店+ 會員載具仍維持安全封鎖，待實機驗證後才解鎖。
- 開立清單可依發票號碼、訂單、買受人、統編、來源及狀態查詢。
- 開立清單改為固定標籤的多行摘要，明確區分完成、處理中、結果不明、失敗、作廢與上傳錯誤；結果不明直接標示禁止重送。
- 移除硬編碼預設管理密碼；首次使用由公司自行設定，正式 App Key 與 MO 密碼仍使用 Windows DPAPI。
- Windows x64 CI 已通過 Go 測試、GUI 編譯、PE、icon、manifest 與 ZIP 結構檢查。
- CI 新增模組可重現性、`go vet` 與機密資料檢查；執行資料、公司 Excel/PDF、二進位發行檔、私鑰及疑似明文 App Key／MO 密碼都會阻擋提交。
- 發行包規則定案：根目錄保留當版 TXT 與 `使用說明.txt`，不含 `Version` 或 `todo.txt`。

## V1.0.0-rebuild.3

- 依光貿 2026/06/10 官方 API 文件建立簽章與 HTTP client。
- 支援開立 `/json/f0401`、發票查詢、上傳狀態及公司名稱查詢。
- 同時相容目前最外層開立結果與舊版 `data` 陣列回應。
- 上傳狀態同時相容官方陣列及鍵值物件格式。
- HTTP、回應大小、逾時及成功但缺少發票號碼皆採保守錯誤處理。
- 加入 request 簽章、成功回應、舊回應與狀態格式單元測試。

## V1.0.0-rebuild.2

- 依解除封存的「CY光貿Local發票exe」對話重建 Windows 主畫面。
- 恢復「開立發票／開立清單」頁面及獨立設定畫面。
- 手動開立頁可輸入訂單、紙本買受人、商品、含稅單價與備註。
- 即時計算應稅銷售額、營業稅額與發票總額，預覽前使用核心資料層驗證。
- 設定頁以管理密碼驗證，正式 App Key 與 MO店+ 密碼仍使用 Windows DPAPI。
- 開立清單由 Data/invoices.json 載入；目前不呼叫 API，也不改開立時間。
- 光貿送出與 MO店+ 匯入暫時安全鎖定，避免功能尚未完成時誤開票。

## V1.1.2 — 2026/09/05（歷史失敗線）

- 嘗試重建 Windows x64 PE 結構並保留 INV icon 與 manifest。
- 功能沿用 V1.1.x：紙本 PDF Viewer／Cache、公司統編五種 PDF 版型、會員載具成功訊息、員工作廢驗證及 MO店+ 匯入。
- 雖完成靜態 PE 檢查，仍未取得可信的 Windows 實機啟動結果；後續已決定不再沿此執行檔修補線繼續。

## V1.1.1 — 2026/09/05（歷史失敗線）

- 針對 V1.1.0 無法在 Windows 啟動的 PE 結構問題進行修復。
- 功能不變，仍未成為可信穩定基準。

## V1.1.0 — 2026/09/04（歷史失敗線）

- 新增紙本發票官方 PDF 取得、內嵌檢視、下載、列印及當日 Cache。
- 新增公司統編五種 PDF 版型。
- 新增會員載具發票資訊視窗。
- 新增員工帳密驗證、作廢原因、同發票期別限制及作廢 API 流程。
- 強化網路逾時後的 OrderId 回查與「結果不明」防重開流程。
- MO店+ 加密 Excel 改由 Excel COM 唯讀開啟，不使用 PowerShell。
- 此版發行檔在 Windows 無法啟動，未列為穩定版本。

## V1.0.0 — 2026/09/04（行為參考基準）

- 可用範圍：手動開立、MO店+ 匯入、已開立發票清單。
- 修正 `/json/f0401` 成功回傳解析及發票號碼回填。
- 分離開立時間、送出時間與最後狀態查詢時間。
- 修正 Win32 GDI 色彩及 resize／最大化後重排。
- 商品明細改為 1～50 列並加入斑馬紋、刪除按鈕及輸入全選。
- 強化同來源＋原始訂單編號的防重複開票。
- 此版本只有可執行發行包可供行為比對，完整原始碼未保留。
