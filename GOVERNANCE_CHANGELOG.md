# CYApps Governance Changelog

## 2.3.19 — 2026/09/30

- 建立 CYCloud Identity 唯一 shared consumer technical standard：`apps/CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md`。所有接入 CYID 的 App 必須以該 standard 與 CYID canonical contracts 為共同 Identity contract；consumer-specific handoff 只可保存 app 差異／遷移／例外／驗收，不得複製共同規範成第二套 authority。
- 新增 `CONSUMER_CONTRACT_VERSION` 與 `CONSUMER_MIN_COMPATIBLE_VERSION`，分離 CYID product version 與 consumer contract version；完成接入的 consumer 以自己的 `CYID_CONSUMER_VERSION` 宣告所採用 revision，且部署版本必須落在 provider 支援窗內。
- CYID PR 新增 mandatory `CYID Consumer Impact: NONE / BACKWARD_COMPATIBLE / CONSUMER_UPDATE_REQUIRED` 分類。Consumer-visible 變更必須同步更新 standard、contract version 與 consumer contract changelog；提高最低相容版本只允許在 update-required migration 下進行。
- 明確禁止 provider 單邊 breaking cutover：受影響 production consumer 尚未遷移時，CYID 必須保留 compatibility path 或先完成協調 migration，不得先提高最低相容版本造成既有 consumer 中斷。
- CYAccountingWeb handoff 收斂為 CYACC-specific migration map；shared Role／Session／App Access／first-login／recovery 規範改由 consumer standard 單一維護。

## 2.3.18 — 2026/09/30

- 依使用者最終確認，CYCloud Identity 新 Employee 首次使用流程對外統一稱 **Email 驗證**；不再以獨立「啟用帳號」流程作產品模型。
- 新 Employee 建立後由 CYID 自動寄出 Email 驗證郵件，內含具 expiry 的一次性首次登入密碼；temporary credential 只能進 CY Web 核心帳號流程，不得建立一般 Identity Session，也不得登入其他 CY App。
- 有效首次登入密碼只可換取短效 first-login ticket；使用者設定正式密碼後，CYID 完成 Email 驗證、正式 credential 建立與 temporary credential/ticket 作廢，但**不得直接發 normal Session**，必須回到 CY Web 一般登入頁重新以正式密碼登入。
- 首次登入密碼逾期、管理員「重寄驗證 Email」或 pending Email 修改時，舊 temporary credential 必須立即失效並重新產生 credential／expiry；寄送失敗保留 pending Employee。
- CYCloud Identity active 文件正式收斂為 project rules、Role/Access、Auth、UI、Architecture、OTP/security 與單一 TODO；dated handoff 與舊 Application Role Mapping 不再留在 active tree，歷史由 Git history 追溯。

## 2.3.17 — 2026/09/29

- 依使用者最終確認，CYCloud Identity Workspace Role 正式收斂為 `SUPER_ADMIN / ADMIN / USER` 三層；`Identity Admin` 改定義為附掛於 ADMIN 的特殊 Identity-management capability，而不是第四個 Role。
- 一般 ADMIN 僅管理 USER lifecycle，不再具 App／CY Web Module Access 設定權；Identity Admin／Super Admin 才可管理 eligible Employee Access，且 Identity Admin 不得自行擴權、不得授予或撤銷 Identity Admin capability、不得修改 Super Admin protected state。
- CY Web 正式定義為核心帳號管理 App：所有有效 Employee 的 CY Web entry access 固定為 TRUE／不可取消；CY Web Module Access 仍由 CY Web 自己管理，Super Admin 全模組自動允許，Identity Admin／Super Admin 可管理其他 eligible Employee 的模組 Access。
- 所有接入 CYID 的 CY App 暫時直接採用 CYID 三層 Role：`SUPER_ADMIN -> SUPER_ADMIN`、`ADMIN -> ADMIN`、`USER -> USER`；既有 Identity Group + `USER_ADMIN` compatibility-role projection 降為 legacy implementation，後續不得再擴充其產品語意。
- Employee 新增時直接指定 Role：普通 ADMIN 只能新增 USER；Identity Admin／Super Admin 可新增 USER 或 ADMIN。建立成功後應自動寄第一封啟用信，信內含可直接開啟 CY Web 啟用流程的連結；寄信失敗不回滾 Employee，保留 pending 並提供重寄。
- 已啟用 Employee 的 Email 若失效，Identity Admin／Super Admin 可強制變更 Email 並重新驗證；帳號維持 activated、密碼保留、Session 撤銷，不回到第一次待啟用。只有從未完成第一次啟用的 Employee 可實體刪除。
- 保留 future HR 備註：若未來需要非 ADMIN 的專職 HR 具 Identity lifecycle 能力，再把相關操作 capability 化；目前不新增第四種 Workspace Role。

## 2.3.16 — 2026/09/28

- CYCloud Identity 的 Password 長度固定為 8–16 字元；所有 consumer App 必須遵循 Shared Identity 的同一 credential 驗證邊界，不得自行放寬或縮限。
- Runtime implementation 與 boundary tests 同步採用 8／16 字元有效、7／17 字元拒絕，並以 Unicode code point 計算字元數。

## 2.3.15 — 2026/09/28

- CYCloud Identity 的普通身分組改為資料驅動，可新增、重新命名、停用或調整，不再以固定 `SUPER_ADMIN / ADMIN / EMPLOYEE` enum 或 schema `CHECK` 封死；Workspace 最高管理 authority 與可編輯身分組分離。
- Application registry 與實際「哪些 Workspace／Employee 可以進哪些 App」改明確視為 deployment/runtime data；Public migration／fixture 不預置公司目前實際 App catalog 或 access matrix。
- 強化 CYCloud Identity 的 Public repo 資料最小化：Public source 只保存 generic schema/contract/placeholder，正式 Workspace、Employee、Email、App access、Cloudflare resource ID、provider target 與 secrets 均不得進 Git／PR／Actions log／Artifact metadata。

## 2.3.14 — 2026/09/28

- 新增正式維護專案 `CYCloudIdentity`，作為 CY Web、CYAccountingWeb 與後續 CYInvoice 共用的 Workspace／Employee／Credential／Application Access／Session／Email OTP／Recovery 身分服務。
- 新增 `apps/CYCloudIdentity/PROJECT_RULES.md`，固定 Workspace scoped Employee、每 Workspace 恰好一名啟用 `SUPER_ADMIN`、Recovery Email、provider-neutral Email、Public source／secret 邊界與目前免費額度優先的資源原則。
- 同步建立 `VERSION=0.1.0`、`BUILD=0`，並將 `CYCloudIdentity` 加入 `REPO_POLICY.md` 正式維護專案清單；CYInvoice runtime 與 Device lifecycle 不因本次治理登錄而變更。

## 2.3.13 — 2026/09/27

- Public package safety scanner only interprets real contiguous UTF-16 ASCII
  strings, instead of removing every NUL from native binaries. This preserves
  ASCII/UTF-16 token detection while avoiding false positives in .NET/WPF
  runtime DLLs and self-contained EXEs.
- Add synthetic credential and binary false-positive regression tests; final
  public packages still require the scanner before upload or Release.
- Pin the exact SHA-256 of the known .NET 10 WPF native dependency that still
  contains a token-shaped UTF-16 string; modified copies receive full scanning.

## 2.3.12 — 2026/09/25

- Public Build／Artifact／Release 與 Production Deploy 正式分離；公開產物不得取得、注入或烘焙正式 Secret、Token、Private Key、OAuth Client Secret、Refresh Token、正式帳密或可直接取得正式服務權限的憑證。
- Cloudflare deployment-specific resource identifiers（D1 database ID／name、Worker／Service Binding 實際 service 名稱、R2／KV／Queue 等）改採 Deployment Environment 注入原則；公開設定只能使用 placeholder／template，Runner 暫存的正式 deploy config 不得 commit、上傳 Artifact 或發布 Release。
- 新增 repository 共用 `.github/scripts/scan-public-package.py`；所有包含 `actions/upload-artifact` 或 `gh release create` 的 workflow 都必須在公開前掃描最終 package，Governance Check 會阻止未接 safety gate 的發行流程。
- CYAccountingWeb `PROJECT_RULES.md` 同步固定 `DB`／`IDENTITY` 只保存 binding contract，正式 Cloudflare resource metadata 由部署環境注入；Google OAuth／Drive 執行期機密只允許存在 Cloudflare Secrets／受保護 runtime storage。

## 2.3.11 — 2026/09/24

- 依使用者新決定，CYAccounting 設定頁恢復本機帳本清除功能；改為兩次分開輸入 `DELETE` 確認，取消原先「不提供清除入口」規則，仍不得使用固定管理密碼。
- 清除範圍包含交易、期初、帳戶、科目、鎖帳與本機偏好及 Google Drive 本機連結；保留本機和雲端既有備份、自訂資料庫位置，且清除前必須建立可驗證的復原備份。

## 2.3.10 — 2026/09/24

- 依使用者決定，CYAccounting 單機版不再提供整批清除交易與期初餘額入口；取消先前 P0 指定的自訂密碼替代方案，要求移除固定密碼及清除功能。
- 說明可攜版預設資料夾、自訂資料庫與雲端備份各自的保存位置；保留歷史帳本、備份及原本的設定頁鎖帳調整功能。
- CYAccounting 版本基準指向 `main` 中的 `VERSION`，不把舊版號寫成永久規則。

## 2.3.9 — 2026/09/24

- 正式採用 AITeam `main/shared/cy-visual/desktop/CY_DESKTOP_VISUAL_GUIDE.md` 為 CY Windows 桌面專案共用視覺 canonical source，並採用 `shared/cy-visual/icon-family/` 為 Icon Family 唯一 family-level source。
- 適用專案固定為 `CYAccounting`、`CYEnvelope`、`CYInvoice`、`CYERPAutoInput`、`TriINVCalc`、`SMARTCOPIConverter`；`CYAccountingWeb` 不自動套用 Windows Desktop Visual Guide。
- AI／開發者進行新 UI、UI 重構、Theme、Table/List、Dialog、Shell、Icon 等視覺工作前，必須先讀 AITeam canonical source；個別 App 永久例外仍只能進該 App 唯一 `PROJECT_RULES.md`。
- 不在 CYapps mirror 整套共用視覺文件；既有穩定 UI 不因本規則立即全面重製，後續新增／修改區域依 canonical direction 與 native-first guardrails 收斂。

## 2.3.8 — 2026/09/22

- 依使用者命名決定，將 `CYSmartERP` 正式改名為 `CYERPAutoInput`，專案路徑由 `apps/CYSmartERP/` 移至 `apps/CYERPAutoInput/`。
- 專案中文顯示名稱同步固定為「SMART ERP 自動輸入工具」，並更新 `PROJECT_RULES.md`、README、Go module、Windows Build workflow 與 Artifact／EXE 名稱。
- `REPO_POLICY.md` 正式維護專案清單同步改用 `CYERPAutoInput`；版本維持 `0.0.10`，本次不因純命名調整重置產品版本。

## 2.3.7 — 2026/09/22

- 新增正式維護專案 `CYSmartERP`（SMART ERP 自動打單工具），放置於 `apps/CYSmartERP/`。
- 新增 `PROJECT_RULES.md`，固定 COPI08 自動化流程為「新增 -> 輸入 -> 儲存」、禁止對 ERP 送出 `Ctrl+A`、支援 `Esc` 緊急停止、不得自行輸入銷貨單號，並要求下拉選項與公司實際設定只存在使用者本機。
- 同步建立 `VERSION=0.0.10`、`BUILD=0` 基礎 metadata，並將 `CYSmartERP` 加入 `REPO_POLICY.md` 正式維護專案清單。

## 2.3.6 — 2026/09/22

- 新增正式維護專案 `CYAccountingWeb`（志遠記帳系統 Web），作為 `CYAccounting` Windows 版之外的獨立 Web 產品線。
- 新增 `apps/CYAccountingWeb/PROJECT_RULES.md`，固定 Cloudflare Workers + Static Assets + D1 架構、D1 migration 追蹤、7 位數金額限制、鍵盤高效率輸入與 Public repo 安全要求。
- 同步建立 `VERSION=0.1.0`、`BUILD=0` 基礎 metadata，並將 `CYAccountingWeb` 加入 `REPO_POLICY.md` 正式維護專案清單。

## 2.3.5 — 2026/09/15

- 依使用者最新規範取消 CYInvoice 自動正式 Release 例外；只有使用者於當次工作明確要求 `release` 後，才可從 `main` 啟動正式 Release workflow。
- `VERSION`／`BUILD` 仍依既有規則推進，但一般 PR 驗證與合併只產生 preview／engineering 測試包，不得自動建立 tag 或公開 Release，也不要求每個小版本都正式發布。
- 已依舊規則發布的 CYInvoice V2.0.1 保留為既有正式歷史，不刪除、不覆寫；後續未正式發布的版本持續累積，直到使用者明確要求 Release。

## 2.3.4 — 2026/09/15

- 補齊 CYInvoice Go／Win32 V1.1.0 的歷史公開回退 Release：repository 當時尚未建立 CYInvoice Release，允許由 V2 合併前的固定 `main` commit `4c2335e00173368540fe10a641fccffcb251f999` 一次性重建、驗證並發布 `cyinvoice-v1.1.0`。
- 歷史 Release 建立後不得更新、覆寫或再將 Go source 加回現行 `main`；V2.0.0 仍是唯一正式產品線與 latest Release。

## 2.3.3 — 2026/09/15

- CYInvoice 依使用者新規範明列正式 Release 自動化例外：含 `VERSION` 變更的 PR 經必要 CI 通過並合併 `main` 後，由 Release workflow 自動重新驗證、建置、打包、建立 tag 與公開 Release。
- 使用者不需要手動按 Merge 或 Run workflow；AI 仍須先確認 PR 驗證結果再代為合併，Release 仍只允許從 `main` 建置。
- `workflow_dispatch` 不再是 CYInvoice 正式發布的必要入口；既有正式 tag／Release 仍不得覆寫。

## 2.3.2 — 2026/09/15

- 使用者完成 Windows 實機驗收並明確批准 Major 升級：CYInvoice 自 `V2.0.0` 起改以 C#／WinForms 為唯一正式產品線，直接由 `main` 維護。
- `cyinvoice/csharp-remake` 完成合併後停止使用；後續不得再以永久 C# 分支或 `VERSION-CS` 建立平行版本身分。
- Go／Win32 `V1.1.0` 固定為上一個可回退的公開版本，不再保留於 `main` 的現行 source；既有 tag、Release、commit 與下載檔不得覆寫。
- CYInvoice 正式 Build、測試、打包及 Release 規則改以 .NET／WinForms Windows x64 實作為準，版本唯一來源維持 `apps/CYInvoice/VERSION`。

## 2.3.1 — 2026/09/14

- 新增 `apps/SMARTCOPIConverter/PROJECT_RULES.md`，補齊 SMARTCOPIConverter 的 project-specific 永久規則層。
- 同步建立專案 `VERSION=1.0.0`、`BUILD=0` 基礎 metadata，讓治理檢查在正式 source 匯入前即可辨識為有效專案。
- 本次不修改共通 `REPOSITORY_RULES.md`，也不重複既有 Public repo 或版本共通規則。

## 2.3.0 — 2026/09/13

- 共通規則同步至 2.4.0：一般 Build／Test workflow 統一採 `pull_request` + `workflow_dispatch`；Draft PR 也可正常驗收，不再把 Draft／Ready 當 CI 開關。
- CYAccounting、CYEnvelope、CYInvoice Go 的 Windows Build workflow 移除 Draft 阻擋；TriINVCalc 移除多餘的 `ready_for_review` 觸發，避免單純切換 PR 狀態重跑 CI。
- CYInvoice Go workflow 收斂 path filter，只在 Go source／module／scripts／assets／VERSION／BUILD 或 workflow 本身變更時執行，避免 C# preview 變更同時浪費一次 Go Windows CI。
- `cyinvoice-csharp-build.yml` 正式加入 `main`，以 Draft PR #2 的 C# solution／source／tests／VERSION-CS 變更自動觸發 Windows 驗收；`workflow_dispatch` 保留人工備援。
- 正式 Release workflow 不變，仍維持明確人工啟動；本次只調整開發 Build／Test 驗收方式。

## 2.2.0 — 2026/09/13

- 共通規則同步至 2.3.0：母本改為公司／個人 repository 可共用的中性規則，並把 Wade–Giles（威妥瑪）定為全域羅馬拼音規則。
- 志遠固定英文名與縮寫改由本 repo policy 保存：`Chihyuan`／`Chih-yuan`、`CY`，不得使用 `Zhiyuan`。
- 公司正式 copyright notice 改由本 repo policy 保存：`Copyright © <YEAR> C.C. Liu, Chihyuan Co. All Rights Reserved.`。
- 共通母本仍由 AITeam 維護；本次沒有修改任何 APP 的功能規則或 source。

## 2.1.1 — 2026/09/13

- 新增正式維護專案 `SMARTCOPIConverter`（SMART 銷貨單格式轉換工具）至 `CYapps` 專案清單。
- 本次只調整 repository-specific 專案登錄；共通規則與其他專案規則不重複、不變更。

## 2.1.0 — 2026/09/13

- 共通規則同步不再依賴每日 GitHub Actions 排程；AITeam 母本變更後，同一輪治理工作直接以 Git／GitHub API／治理 PR 同步本 repo。
- `sync-common-rules.yml` 改為 manual fallback；Actions 不可用時仍必須直接比對／同步，不得把 workflow 當成唯一一致性來源。
- `REPO_POLICY.md` 新增共通規則同步責任；任何 AI 接手 APP 前須先比對本 repo `COMMON_RULES_VERSION` 與 AITeam `main`。
- 保留 Governance 2.0.1 新增的 `TriINVCalc` 正式專案與其 project rules，不因本次治理整合倒退。

## 2.0.1 — 2026/09/13

- 新增正式維護專案 `TriINVCalc`，顯示名稱固定為「三聯式發票開立計算機」。
- 新增 `apps/TriINVCalc/PROJECT_RULES.md`，定義 Windows x64 portable、公開安全、發票計算核心與發行驗證要求。
- `REPO_POLICY.md` 的正式維護專案清單加入 `TriINVCalc`。

## 2.0.0 — 2026/09/13

- 永久規則固定為三層：共通 `REPOSITORY_RULES.md`、repo-specific `REPO_POLICY.md`、project-specific `apps/<Project>/PROJECT_RULES.md`。
- AITeam 成為共通規則唯一母本；本 repo 新增 `COMMON_RULES_VERSION`、`COMMON_RULES_CHANGELOG.md` 與自動同步 workflow。
- Governance Check 會逐字比對 AITeam `main` 的共通母本；只要本 repo 落後，其他 PR 就不能通過治理檢查。
- 根 `AGENTS.md` 成為唯一 AI 規則入口；舊平行規則入口已清理。
- README、WORK_HANDOFF、PROJECT_STATUS、TODO、CHANGELOG、REQUIREMENTS、RC_TEST 等只保存狀態／需求／測試／歷史，不再具有永久規則優先權。
- 導入 `X.Y.Z + Build N` 版本制度：Major 只由使用者決定；Minor 可由 AI 依明顯功能階段判斷；Patch 為日常新工作項目；Build 僅用於同一項目未完成的返修。
- CI 採 Ready PR 自動驗證 + manual dispatch、path filter、concurrency cancellation；正式 Release 與一般 Build/Test 分離。
- Commit metadata 固定使用 GitHub private noreply。
- Public repo 的正式秘密、API key、token、runtime data 禁止進入 source/history。