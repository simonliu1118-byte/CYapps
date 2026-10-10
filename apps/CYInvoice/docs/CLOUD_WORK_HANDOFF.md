# CYInvoice 現行工作交接

更新日期：2026-10-10（Asia/Taipei）

本文件保存目前工作停點與驗證證據；唯一待辦清單為 [TODO.md](TODO.md)，實機步驟為 [RC_TEST.md](RC_TEST.md)。永久規則仍依 repository REPOSITORY_RULES.md → REPO_POLICY.md → apps/CYInvoice/PROJECT_RULES.md；本文件不是額外規則層。

## 1. Git／版本／發布狀態

| 項目 | 已核對狀態 |
| --- | --- |
| Repository／專案 | simonliu1118-byte/CYapps／apps/CYInvoice/ |
| main | `94f559cdb74a250453cce078a5f5c4e4d9a51df7`；CYInvoice 仍 V2.6.10 Build 2 |
| 前置介面 PR | [#216](https://github.com/simonliu1118-byte/CYapps/pull/216)，open、尚未合併 |
| 本輪分支 | `cyinvoice/feature-cyid-consumer`；整合最新 main、PR #216 head 1e6d4137 與 PR #379 handoff c0fe1b84 |
| 工程版本 | **V2.6.15**；來源為 ../VERSION、../BUILD |
| 先前分流已驗證 commit | `66ec3671d5812efce9c97d6b6796585eed9a638f` |
| 最新正式 Release | `cyinvoice-v2.4.2`；本批未建立新 tag／Release |

前置獨立裝置管理需求推進 V2.6.14／BUILD 0；保留 V2.6.11～13。CI／工程包請核對 [PR #216 精確 head checks](https://github.com/simonliu1118-byte/CYapps/pull/216/checks)，不能拿 V2.6.13 Windows #265 的綠燈當新版通過。正式 Release 未授權、未建立。

## V2.6.15 CYID Consumer 現行停點

使用者 2026-10-10 授權開始 consumer 開發；新獨立 Patch／BUILD 0。整合 main 與兩支未合 PR 以保留最新介面和設計，不自動合併／關閉前置 PR。前置 PR #216 精確 head 1e6d4137 的 Governance #1172／Cloud #378／Windows #267 全綠只是 V2.6.14 證據，不能冒充本輪 CI。

本輪完成 CyIdIdentityProvider、集中選擇及 authenticated discovery／DPAPI binding、private IDENTITY gateway、Device 與 Employee／App Access／Role 分離、兩 Workspace 固定綁定、transport-only offline cache／reconnect 更新。Rename／revoke／pairing／invitation 保留原 handler 及 audit owner；0013 新增 external invitation actor 並保留 Built-in FK／歷史，沒有匯入或讀 CYID D1。帳號管理隱藏／復原提示 CY Web；CYID 不讀 Built-in employee snapshot。詳見 CY_ID_INTEGRATION §14。

Source Cloud 0.9.0／API 1／marker 8／storage 13／cyid-consumer-v1；預設 CYID 關閉，runtime binding 與實際 Application enablement 未核對。本輪沒有遠端 migration、部署、正式切換、tag／Release；目前最後有效 development deployment 仍是下方 Cloud 0.8.9／storage 12 Run #8 attempt 2。

本機驗證：TypeScript、完整 npm test（bootstrap／lifecycle／真實 CYID provider，各自 synthetic DB 與 migrations）與 bundle dry-run 通過；C# Core Release build 與 Cloud contracts 通過，Void workflow 29／Allowance 12／SQLite migration 13 通過。WinForms Linux cross-build 只作編譯預檢，Windows-only smoke／DPAPI／PE／封裝仍由本輪 PR CI 驗證，不引用舊包。新增 Windows join smoke 包含 CYID invitation／pairing／mismatched binding，確認不下載 Built-in verifier。

Architecture Exception：依使用者保留既有 last-trusted offline 行為，整筆 scope／principal／本機 proof DPAPI 保護，沒有新增 TTL；離線無法即時觀察中央撤銷，server mutation 仍在線驗證。Logout loss 不 replay、不假稱已撤銷，provider expiry 負責 orphaned Session（目前 default 8h）。每個 server mutation 前 Resolve；CYID／consumer D1 不構成跨庫 atomic transaction。Local／Built-in 是獨立正式模式，沒有在 CYID 失敗時自動啟用。Rolling-deployment 404 discovery reader 只供未確認 CYID 的舊 Built-in Worker。

剩餘驗收只在 TODO §7.3、RC AA 追蹤：精確 head CI／工程包、Windows 實機及舊 A/B/C、staging bindings、受控 migration／actor audit／rollback。0-active-Device recovery 未做，LAST_ACTIVE_DEVICE 保護維持；125／150 DPI 仍 Deferred。

## V2.6.14 裝置管理

主視窗只管 active inventory／selection／rename／revoke；原 tabs 及新增流程已移除。獨立 CloudAddDeviceForm 是新增／ticket／timer／lifetime owner，保留同一 CloudClient、既有 pairing／invitation authorization 與重開狀態恢復。返回主窗重新載入 inventory；不建立第二套加入路徑。

主窗上方顯示 active 數量及原撤銷說明；四欄為裝置名稱／使用版本／加入時間／最後使用時間，撤銷與確認撤銷採共通 Danger。Cloud inventory API 保留歷史供管理查詢，Windows 只篩選 active。雲端不刪除 revoked Device，既有 self-reset／LAST_ACTIVE_DEVICE 不變。

MainForm 啟動在既有 lifetime 下回報自己的 VERSION／BUILD；POST /v1/devices/usage 只依 token 身分更新本 Device，server UTC 記錄最後使用。離線不阻擋正常工作、不修改原 authority/cache；舊版或未成功回報資料保留，不宣稱 heartbeat 或目前在線。POST /v1/devices/rename 經中央 SUPER_ADMIN 執行時驗證，同一 Workspace active Device、actor／workspace／credential 在 mutation 交易再檢查，並與 device_renamed audit 原子保存。0012 forward migration 只擴充 audit vocabulary，保留歷史；沒有新增 Device metadata table。

本機 TypeScript、bootstrap SQL、實際 lifecycle handler／SQL 回歸及 bundle dry-run 已通過。回歸含 own-device 更新、server time、空白／控制字元／長度、撤銷／disabled Workspace、credential race、audit failure rollback 及歷史保留；C# client 增加 wire contract／錯誤回傳拒絕，Windows smoke 增加四欄／active filtering／最後一台防護／新增視窗標籤測量。Windows CI 與工程包結果以 PR 精確 head 為準；實機 RC Z、125／150 DPI 尚未取得證據。

Development staged [Run #8 attempt 2](https://github.com/simonliu1118-byte/CYapps/actions/runs/37983363509) 已成功；deploy commit 3b7325ea 與功能 source db1a3b95 的 tree 相同。先 capture bookmark／aggregate audit，0012 一次套用；Device 總量及 active／revoked 分組前後保持，仍存在 revoked 歷史，FK 無異常。第一次 immediate health 尚讀到 0.8.8；完全相同 source 重跑後無 pending migration，health 已驗證 Cloud 0.8.9／API 1／marker 8／storage 12／storage ok 及 usage／rename capabilities。未重跑 0010／0011，未操作真人裝置名稱或憑證。

### V2.6.14 自動化與工程包

功能 source **db1a3b9560510a94400ffb308a1536a3a1ef8754**：

- [Governance #1171 通過](https://github.com/simonliu1118-byte/CYapps/actions/runs/37982871612)。
- [Cloud #377 通過](https://github.com/simonliu1118-byte/CYapps/actions/runs/37982871621)，含 lifecycle SQL／migration／Linux 及 Windows client contracts。
- [Windows #266 通過](https://github.com/simonliu1118-byte/CYapps/actions/runs/37982871637)，含編譯、全部回歸、startup／packaged smoke、PE／VERSION／BUILD 及 public-package scan。
- 當輪 [V2.6.14 工程包](https://github.com/simonliu1118-byte/CYapps/actions/runs/37982871637/artifacts/11642360855)，SHA-256 4f150a845bb8eaa119e9ab5a8d348dfa278e12d814e1393aab3e4c6a592052c6，原 Artifact 至 2026-10-13 03:53（Asia/Taipei）。

本次收尾只更新部署／CI 證據文件，不推進 VERSION／BUILD；下載最新版及精確 head checks 以 PR #216 為準。功能 source 的 CI 不冒充後續文件 head 已完成檢查。

## V2.6.13 歷史介面停點（版本快照已由 V2.6.14 取代）

共用 RoundedButton 取代舊無焦點點線 Button 及重複品牌／Primary／Danger renderer，保留 native Button 的 action／DialogResult／keyboard／accessibility owner，PDF 版型只自訂顏色，商品列刪除共用同一 surface。使用者要求所有方形按鈕圓角，屬本次 Secondary 外觀例外，不變更共通永久規則。

SyncIssuesForm 是兩清單的唯一 state／height owner，重用 NativeListViewHost 的原生列高與捲軸，零筆顯示無資料、依列數縮放最多十二列。InvoiceOperationHistoryControl 重用同一 host，RecordDetailForm 分配固定五列，其餘給發票資訊；刪除原有 Buffered／History ListView 重複類別與 header cursor。

版本 owner 收斂為 ApplicationVersion（VERSION／BUILD）；Cloud 加入 client 改傳可讀版本，Device Management 明確標示加入時快照。舊 +SHA 去除，舊 Build 不補猜。Cloud API／schema 不變，不提供 heartbeat 或當前版本刷新。

最新 CI／engineering Artifact 以 [PR #216 checks](https://github.com/simonliu1118-byte/CYapps/pull/216/checks) 精確 head 為準；V2.6.12 Windows #260 通過是上一版證據，不能當新介面驗收。自動 smoke 覆蓋動態列數、固定五列原生捲軸、鍵盤／disabled／圖片與版本身分；實機仍待 RC Y，125／150 DPI 尚未驗收。

## 2. V2.6.11 已完成的變更

「上傳問題」旁新增「處理中」按鈕與數量，兩者使用同一清單／雙擊明細實作。

| 分類 | 顯示內容 |
| --- | --- |
| 處理中 | 正常官方上傳狀態 1／2／3／31／32、光貿明確確認處理中的作廢、人工操作完成且正常等待官方確認的折讓 |
| 上傳問題 | 查詢／同步／解析失敗、結果不明、開立失敗、尚需人工操作的作廢／折讓／折讓作廢、折讓候選衝突或金額不符 |
| 完成 | 從處理中移除；歷史已解決 issue 仍依既有流程保留 |

`InvoiceWorkQueue` 只投影目前 invoice metadata／workflow／sync issues，不保存第二套 pending store，不新增同步或重送路徑。真正 unresolved 技術問題優先於可能過時的正常等待狀態。兩清單限目前公司／環境；舊折讓沒有明確分類欄位時先留上傳問題，正常回查後再分類。

處理中不能勾選刪除或手動標成功。超過兩期的作廢／折讓仍可由 ADMIN／SUPER_ADMIN 經既有核心結案；只停止本機追蹤，不能宣稱官方成功。已修正舊行政結案測試依賴 void_pending_confirmation 必須出現在 active issue 的過時契約；一般 QueryFailed 不能藉結案清掉業務 pending。

保留原 PR 修正：作廢確認移除黑色號碼遮罩但文字／預覽仍隱藏，等待作廢使用半形括號，折讓「已人工處理」使用共通 Danger button。

## 3. V2.6.12 邀請加入停點

舊包第一次 claim 已讓 Cloud 建立 Device 並使用邀請；password.Clear 觸發 TextChanged → ResetAuthorization，把 preview 清空，下一行讀取 preview.WorkspaceId 發生空物件錯誤。再次 preview 已使用邀請因而 unavailable；關閉再開沿用 protected Pending Token 找回原 Device，使用者已回報成功。

修正由 CloudDirectJoinForm 保留每次操作已確認的 Workspace，仍清密碼與使可編輯授權失效，保留原本 Device／authority／snapshot 與 protected persistence 驗證。State／render／lifecycle／mutation owner 仍為同一 Form 與既有 client/store；不新增 retry／第二加入路徑／第二 authority。HTTP transport 與原生通知僅在 offline smoke fixture 替換，正常程式仍使用同一流程。

首次說明改為左對齊兩點並於語句邊界折行；Windows startup smoke 覆蓋邀請首次完成、配對完成、Workspace mismatch、claim 後 snapshot 中斷及重開復原、修改輸入後禁止未確認 claim。修正版實機首次成功仍需 RC X；舊包重開成功不視為修正版已驗收。

目前 V2.6.12 精確 source CI／engineering Artifact 請看 [PR #216 checks](https://github.com/simonliu1118-byte/CYapps/pull/216/checks)，不得使用下段 V2.6.11 舊包驗收此修正。未改 Cloud source，不需為本次 Windows UI 修正部署 Worker／migration。

## 4. 先前 V2.6.11 自動化證據與測試包

以下全部屬功能 commit 66ec3671，不是 Windows 人工／AMEGO live 驗收：

| Check | 結果／連結 |
| --- | --- |
| Governance Check #1137 | [通過](https://github.com/simonliu1118-byte/CYapps/actions/runs/37916154874) |
| CYInvoice Cloud Check #369 | [通過](https://github.com/simonliu1118-byte/CYapps/actions/runs/37916154522) |
| CYInvoice Windows Build #258 | [通過](https://github.com/simonliu1118-byte/CYapps/actions/runs/37916154524) |

Windows 已完成 warnings-as-errors build、startup smoke、Core parity 62／Void 13／Employee void workflow 29／Allowance 12／SQLite 13／Sync 15／SyncCoordinator 15 項回歸、x64 package、PE／layout、packaged startup smoke 與 public-package safety scan。Cloud check 已完成 Worker／D1 local migration／contract 與 Windows Cloud contract／package 驗證。

Windows Artifact：[CYInvoice_V2.6.11_Build2_engineering-run258](https://github.com/simonliu1118-byte/CYapps/actions/runs/37916154524/artifacts/11608879449)。Actions 保留 3 天；到期時間 **2026-10-12 18:15（Asia/Taipei）**。下載 ZIP SHA-256：`b08fb736fecb91a12361a20b11d41a8de6d03bc0b4cb29c0cbb25bb70dfc6158`。這包出自功能 commit，尚未包含本次文件整理；可執行程式與本次文件整理後的 source 相同。

## 5. Cloud 工程與遠端部署的界線

Reference backend source：Cloud **0.8.9**／API **1**／legacy compatibility schemaVersion=8／actual storage Schema **12**；forward migrations 0001～0012。不可把 compatibility marker 與 storage migration progress 當同一欄位。

最新 development 遠端證據為 2026-10-10 staged [Run #8 attempt 2](https://github.com/simonliu1118-byte/CYapps/actions/runs/37983363509)，詳見上方 V2.6.14。9/29 Run #7／Cloud 0.8.8／storage 11 保留為 [歷史快照](NEXT_CHAT_HANDOFF_2026-09-29.md)，不再當最新部署基準。

以下四包已合併進 main，不再列為未實作：

1. V2.6.7：AppPrincipal／AppRole／IIdentityProvider、Local／Built-in provider、集中選擇，正式 role 為 SUPER_ADMIN / ADMIN / USER；Local／cache 與 Cloud forward migration 已處理舊 role。
2. V2.6.8：Online operation 取最新中央 credential／role／enabled；只有真正 transport outage／timeout 可用最後可信 protected cache。HTTP 拒絕、revoked Device、malformed／Workspace mismatch／caller cancellation fail closed。
3. V2.6.9：Device inventory／revoke／history／audit 與 Windows 裝置管理；最後一台 Built-in active Device 保留 LAST_ACTIVE_DEVICE。
4. V2.6.10：Cloud → Local 雙重確認、UI／同步停止後 revoke／self-status 確認再清目前安裝的 Data／Cache／identity；不明結果保留資料／Token，下一次啟動恢復。Windows 不停用／刪除中央 Workspace。

2026-09-28～29 的 A 機連線與 B 機 Run255 配對加入已有人工作業證據；**最新版 A/B authority freshness 與 A/B/C revoke／reset 尚未取得人工驗收結果**。Run229／255／343 僅為歷史驗證編號，舊 Actions Artifact 已逾保留期，不能當目前下載包。

## 6. CY ID 與後續實作停點

CYID 已有 canonical [Consumer Integration Standard](../../CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md)，contract **1.0.2**、minimum **1.0.0**。不再以「尚未發布共同 contract」作為唯一等待理由。同 repo 直接讀 canonical files，不複製另一份 shared contract。

CYInvoice 尚未實作 CyIdIdentityProvider、Workspace binding、CYID App Access／Session 接線、Windows offline 協定及 0-active-Device recovery。先完成 desktop／per-operation transport 適配與 acceptance 設計；不把 Web Session／HttpOnly-cookie 語意直接套進 WinForms，不自訂第二套 CYID role／access／credential contract。帳號管理 visibility 與 business／Device ownership 依 [CY_ID_INTEGRATION.md](CY_ID_INTEGRATION.md)。

## 7. 接續工作順序

唯一可勾選的進度表為 [TODO.md](TODO.md)。先驗收 RC X 的邀請首次完成／說明，再驗收 RC W 的分流與舊結案，再做 Q／R 的 A/B 基線／即時權限，之後用可拋棄 C 做 S／T／U 的撤銷、重置與不明結果恢復。再處理 invitation／Employee identity matrix／OTP／transfer 的跨機驗收。

尚未實作的後續主線為 CYID adapter、all-device-loss recovery、Cloud Work Item 原子轉移／revision、多機離線 OrderID 防撞、audit viewer、正式折讓 API 與自架手冊。Offline cache 完整性簽章、125%／150% DPI、多公司與營運摘要保持原定延後範圍，不自動升為本次阻塞。

正式發布仍依三層規則及使用者當次明確 release 指示處理；PR／CI／文件更新不代表完成正式發布。
