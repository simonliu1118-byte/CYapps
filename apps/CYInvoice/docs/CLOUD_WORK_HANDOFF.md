# CYInvoice 現行工作交接

更新日期：2026-10-09（Asia/Taipei）

本文件保存目前工作停點與驗證證據；唯一待辦清單為 [TODO.md](TODO.md)，實機步驟為 [RC_TEST.md](RC_TEST.md)。永久規則仍依 repository REPOSITORY_RULES.md → REPO_POLICY.md → apps/CYInvoice/PROJECT_RULES.md；本文件不是額外規則層。

## 1. Git／版本／發布狀態

| 項目 | 已核對狀態 |
| --- | --- |
| Repository／專案 | simonliu1118-byte/CYapps／apps/CYInvoice/ |
| main | `3b22f9f8c9f070454148da89d1986882985b1f92`；CYInvoice V2.6.10 Build 2 |
| 現行 PR | [#216](https://github.com/simonliu1118-byte/CYapps/pull/216)，open、尚未合併 |
| 分支 | `cyinvoice/fix-void-workflow-ui`；已同步上述 main |
| 工程版本 | **V2.6.11 Build 2**；來源為 ../VERSION、../BUILD |
| 已驗證功能 commit | `66ec3671d5812efce9c97d6b6796585eed9a638f` |
| 最新正式 Release | `cyinvoice-v2.4.2`；本批未建立新 tag／Release |

本次文件整理不更動 source、VERSION／BUILD、Cloud deployment 或正式發布身分。後續文件 commit 的 head／CI 以 PR 即時狀態為準，不能把上表功能 commit 寫成永遠最新的 branch head。

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

## 3. 自動化證據與測試包

以下全部屬功能 commit 66ec3671，不是 Windows 人工／AMEGO live 驗收：

| Check | 結果／連結 |
| --- | --- |
| Governance Check #1137 | [通過](https://github.com/simonliu1118-byte/CYapps/actions/runs/37916154874) |
| CYInvoice Cloud Check #369 | [通過](https://github.com/simonliu1118-byte/CYapps/actions/runs/37916154522) |
| CYInvoice Windows Build #258 | [通過](https://github.com/simonliu1118-byte/CYapps/actions/runs/37916154524) |

Windows 已完成 warnings-as-errors build、startup smoke、Core parity 62／Void 13／Employee void workflow 29／Allowance 12／SQLite 13／Sync 15／SyncCoordinator 15 項回歸、x64 package、PE／layout、packaged startup smoke 與 public-package safety scan。Cloud check 已完成 Worker／D1 local migration／contract 與 Windows Cloud contract／package 驗證。

Windows Artifact：[CYInvoice_V2.6.11_Build2_engineering-run258](https://github.com/simonliu1118-byte/CYapps/actions/runs/37916154524/artifacts/11608879449)。Actions 保留 3 天；到期時間 **2026-10-12 18:15（Asia/Taipei）**。下載 ZIP SHA-256：`b08fb736fecb91a12361a20b11d41a8de6d03bc0b4cb29c0cbb25bb70dfc6158`。這包出自功能 commit，尚未包含本次文件整理；可執行程式與本次文件整理後的 source 相同。

## 4. Cloud 工程與遠端部署的界線

Reference backend source：Cloud **0.8.8**／API **1**／legacy compatibility schemaVersion=8／actual storage Schema **11**；forward migrations 0001～0011。不可把 compatibility marker 與 storage migration progress 當同一欄位。

最後可引用的 development 遠端證據是 **2026-09-29 staged deploy Run #7**：health／storage／device-revoke-v1／device-self-status-v1 通過；部署前已無未套用 migration，沒有重跑 0010／0011。前後 aggregate audit／FK 正常。詳見 [9/29 歷史快照](NEXT_CHAT_HANDOFF_2026-09-29.md)。本次未連線重查 live Worker／D1，不把當時資料筆數或健康狀態宣稱為今日即時狀態。

以下四包已合併進 main，不再列為未實作：

1. V2.6.7：AppPrincipal／AppRole／IIdentityProvider、Local／Built-in provider、集中選擇，正式 role 為 SUPER_ADMIN / ADMIN / USER；Local／cache 與 Cloud forward migration 已處理舊 role。
2. V2.6.8：Online operation 取最新中央 credential／role／enabled；只有真正 transport outage／timeout 可用最後可信 protected cache。HTTP 拒絕、revoked Device、malformed／Workspace mismatch／caller cancellation fail closed。
3. V2.6.9：Device inventory／revoke／history／audit 與 Windows 裝置管理；最後一台 Built-in active Device 保留 LAST_ACTIVE_DEVICE。
4. V2.6.10：Cloud → Local 雙重確認、UI／同步停止後 revoke／self-status 確認再清目前安裝的 Data／Cache／identity；不明結果保留資料／Token，下一次啟動恢復。Windows 不停用／刪除中央 Workspace。

2026-09-28～29 的 A 機連線與 B 機 Run255 配對加入已有人工作業證據；**最新版 A/B authority freshness 與 A/B/C revoke／reset 尚未取得人工驗收結果**。Run229／255／343 僅為歷史驗證編號，舊 Actions Artifact 已逾保留期，不能當目前下載包。

## 5. CY ID 與後續實作停點

CYID 已有 canonical [Consumer Integration Standard](../../CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md)，contract **1.0.2**、minimum **1.0.0**。不再以「尚未發布共同 contract」作為唯一等待理由。同 repo 直接讀 canonical files，不複製另一份 shared contract。

CYInvoice 尚未實作 CyIdIdentityProvider、Workspace binding、CYID App Access／Session 接線、Windows offline 協定及 0-active-Device recovery。先完成 desktop／per-operation transport 適配與 acceptance 設計；不把 Web Session／HttpOnly-cookie 語意直接套進 WinForms，不自訂第二套 CYID role／access／credential contract。帳號管理 visibility 與 business／Device ownership 依 [CY_ID_INTEGRATION.md](CY_ID_INTEGRATION.md)。

## 6. 接續工作順序

唯一可勾選的進度表為 [TODO.md](TODO.md)。先驗收 RC W 的分流與舊結案，再做 Q／R 的 A/B 基線／即時權限，之後用可拋棄 C 做 S／T／U 的撤銷、重置與不明結果恢復。再處理 invitation／Employee identity matrix／OTP／transfer 的跨機驗收。

尚未實作的後續主線為 CYID adapter、all-device-loss recovery、Cloud Work Item 原子轉移／revision、多機離線 OrderID 防撞、audit viewer、正式折讓 API 與自架手冊。Offline cache 完整性簽章、125%／150% DPI、多公司與營運摘要保持原定延後範圍，不自動升為本次阻塞。

正式發布仍依三層規則及使用者當次明確 release 指示處理；PR／CI／文件更新不代表完成正式發布。
