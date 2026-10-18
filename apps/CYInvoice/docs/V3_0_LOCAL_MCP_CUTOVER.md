# CYInvoice 3.0｜Cloudflare 本機 MCP 切換與驗收交接

更新日期：2026-10-18（Asia/Taipei）

## 2026-10-18 本機 MCP 實際切換紀錄（取代較早的 Source-only 狀態）

- 已完成原 CYInvoice 業務 D1 定址、CYID_ONLY 唯一 Workspace、完整原庫檢查；一致加密備份已在本機解密／隔離 SQLite 還原，所有應用 schema、完整舊欄位資料、索引／sequence／遷移歷史核對通過。這是同機維護備份驗證，**異機 DR-12 尚未完成**。
- 2026-10-18 12:53（Asia/Taipei）前後，canonical `0014_invitation_recovery_metadata.sql`、`0015_company_settings.sql` 及獨立 `enable-cyweb-recovery-issuer.sql` 各執行一次；每階段在本機比對加密 readback，原 Device Token hashes／狀態／邀請／Audit 全部舊欄位一致、FK clean、quick_check=ok，結構與本機演練一致。
- 原 Worker 已透過本機 MCP 切到 #476 exact-head CI 所驗證的業務 source（`e0c35b38`；合併後 cloud tree 相同），100% 流量 readback 通過；API1／compatibility marker8／storage15／`identityProvider=CYID`、原 D1／既有 service bindings／rate limits／Secret 名稱／路由保留。私有 named export `CyInvoiceRecoveryEntrypoint` 已從部署內容確認；跨服務實際 RPC 尚未驗證。
- `COMPANY_SETTINGS_ENABLED=false`、`RECOVERY_ENABLED=false`；公司卡與 Recovery 公開路由均回 503、舊 Built-in 路由回 409、無 Device 的 protected request 回 401。公司卡 Secret 尚未設定，未產生／公開任何實際金鑰。
- **新的對齊阻塞**：`sameIdentityProvider=false`。當次唯讀 metadata 顯示 CYWEB 使用正式 CYID，Invoice 仍保留原 development CYID；相同 Workspace 字串不代表相同權威資料。使用者已明確批准「先驗證再對齊正式 CYID」，但需先以正式 CYWEB SUPER_ADMIN 驗證 CYInvoice App enablement／角色／Session 與回退條件；**未直接讀寫 CYID D1，未切換身分來源**。正式 provider 的部署 metadata 沒有 Invoice-specific scope 設定，這本身**不能證明** App registry 是否已註冊，須由 canonical 管理介面驗證。
- CYWEB 當次實際服務為 V0.9.1／`e8665d65`（#193，包含 #192 ancestry），不退回舊 UI；Recovery binding 仍不存在。待身分對齊、私密公司卡金鑰與各 flag gate 完成，再接既有最新 CYWEB 的 named RPC。
- 全部原始識別資訊、指紋、bookmark、部署／回退憑據與備份留在本機加密紀錄；本頁只保存時間、版本與通過狀態。Windows 真機／真 Email／雙機競爭／DR-01～13、可攜異機還原均未勾選通過；Windows V2.6.19 Release 不變。

## 責任界線與來源

- **GitHub 原始碼已交付**：CYInvoice [PR #476](https://github.com/simonliu1118-byte/CYapps/pull/476)（CI exact head `e0c35b38`，已合併 `main` → `f546b2a5`）；CYWEB 復原 [PR #192](https://github.com/simonliu1118-byte/chihyuan-web/pull/192) 已合併 CYWEB `main`（`7ba87b8`）。**兩者只合併 Source，沒有據此啟用正式 Recovery/Company Card 或建立 3.0 Release。**CYWEB 舊 #151 僅供追溯，**不可再合併整個舊分支**。
- **本機 Cloudflare MCP 負責**：唯一原 CYInvoice 業務 D1 的唯讀查核、私有備份／還原演練、0014／0015 forward migrations、獨立的復原 issuer 維護 SQL、保留原 bindings/secrets 的受控 Worker 切換，以及 CYWEB `CYINVOICE_RECOVERY` named RPC binding 與服務啟用。這些步驟**未因程式合併或 CI 綠燈自動完成**。
- **禁止**建立 Cloudflare 測試 Worker／handler，重跑 2026-10-13 已完成的 CYID-only 帳號表退役，遷移 CYID 身分 D1，或為測試撤銷最後一台真實授權裝置。
- **明確區分**：原 Worker 0.9.3／API1／marker8／schema13／`CYID_ENABLED=true`／`CYID_ONLY` 是 3.0 升級前的已知歷史基準，**不是即時遠端查詢結果**；以本機 MCP 當次的原始憑據／Worker active deployment 為準，不得將其他資料庫認成原 D1。
- 不把 API Token、D1 ID、Workspace/Device/Employee ID、雜湊、明文金鑰、原始 Email 或還原資料放在 GitHub、公開 PR、AI 對話或未加密日誌。證據留在受控本機私人紀錄，GitHub 只記是否通過、時間及非敏感版本。

## 本機 MCP 的安全順序（逐項留下私人證據）

1. [x] **只讀定址**：確認 Cloudflare 帳號、原 CYInvoice 業務 D1、當前 100% 流量 Worker 版本與 environment；再次確認不是 CYID D1、不是另一家公司或錯 Worker。若資源有爭議即停止。
2. [x] **只讀前置庫況**：核對 0013 已套用且 0014／0015 尚未執行、`CYID_ONLY` 唯一 Workspace profile、原 `workspaces`／`devices`／`device_invitations`／`security_audit_events`、其他業務表、FK／quick_check、現在的 bindings／secret 名稱／`CYID_ENABLED`。查不到完整舊基準就停止；不要嘗試重新建立退役帳號表。
3. [x] **私人備份與回復證據**：建立核准範圍、可驗證一致性的完整加密 D1 備份及遷移前 bookmark；在隔離環境解密與還原，核對 table/index/view、migration history、token hashes／Device status／邀請與 Audit fingerprint、FK 與 quick_check。只有 counts、Time Travel bookmark 或「匯出成功」不足以放行。
4. [ ] **方案／回退審核**：確認原 Worker 不會因 schema15 拒絕舊 API 1 Windows、舊 Device Token 仍有效；保留可回退的舊 Worker version 與精確 bindings 記錄；確認新寫入後**不能直接用舊 D1 備份覆蓋**，以免復活已撤銷 Device／已使用邀請。未知結果先 readback、不重試不可逆命令。
5. [x] **在核准維護時段執行一次 forward D1**：以 `cloud/migrations/0014_invitation_recovery_metadata.sql` 再 `cloud/migrations/0015_company_settings.sql` 的 canonical source 升級**同一原 D1**；不要套用 #440 的舊 0015～0019 草稿。每步後唯讀檢查 schema15、歷史 row fingerprint、profile、FK／quick_check，重跑不可接受。
6. [x] **復原 issuer gate 獨立審核**：`cloud/maintenance/enable-cyweb-recovery-issuer.sql` 不是一般 migration，必須確認對應唯一 CYID_ONLY Workspace、原邀請歷史與 FK 後單獨執行；確認 `cyweb_recovery_storage_profile` 和 guard／單一 outstanding 邀請 index。此維護 SQL 內含 invitation 表重建；沒有完整還原驗證前絕不執行。
7. [ ] **受控部署原 CYInvoice Worker（業務程式切換已完成；私密公司卡金鑰與跨服務驗證待續）**：只使用 #476 經精確 head CI 驗證過的 source／受控配置；保留原 CYID `IDENTITY`／`IDENTITY_AUTHORITY` 指向同一 canonical provider、原 D1、rate limit、所有既有秘密與路由。核對 `SCHEMA_VERSION=15`、`CYID_ENABLED=true`、新的 health `identityProvider=CYID`；確認 `CyInvoiceRecoveryEntrypoint` 私有 named RPC 已匯出。公司卡密鑰 `COMPANY_SETTINGS_KEY_HEX` 必須由本機私密維運流程安全產生／設定，**不得由 AI 代填或寫進 Repo**。
8. [ ] **分別檢驗及開啟 feature flags**：起初 `COMPANY_SETTINGS_ENABLED`、`RECOVERY_ENABLED` 都關閉；核對未啟用時無法取得任何憑證或簽發碼。確認原 D1 Schema15、CYID-only、加密金鑰與 issuer gate 都就緒後，分別受控啟用，失敗立即停在唯讀診斷。
9. [ ] **CYWEB 私有接線與驗證**：在已合併 #192 的 CYWEB 設定可選 `CF_CYINVOICE_RECOVERY_SERVICE`，只指向同一原 CYInvoice Worker 的 `CyInvoiceRecoveryEntrypoint`；不改 CYWEB 的 CYID D1 或部署其他應用。Binding 缺失時必須回 503，不能降級到公開 HTTP。受控部署後再用有權限的實際 CYWEB Session 測試。
10. [ ] **安全實機驗收**：使用明確核准的可拋棄／預備裝置、當次 CYID SUPER_ADMIN 與真 Email 做 [DR-01～DR-13](CLOUD_DISASTER_RECOVERY.md#9-實作順序與驗收條件)，尤其 issuer Email／超管當次密碼、10 分鐘失效及 `AUTO_EXPIRED`、結果不明 readback、雙機競爭、原 Device Token 無法復活、新 Device 成功後**全部其他舊裝置**撤銷並保留 Audit；不拿仍在業務使用的唯一裝置製造事故。
11. [ ] **Windows 本機與 V2 回歸**：備份原 portable 資料；確認舊 Device／Settings／本機發票清單不被換新、Local／CYID 雙模式、MO Excel、手動／匯入開票、重複 OrderID、查詢／作廢、V2 人工折讓（3.2 才升級）無迴歸；無網路時不得以舊 Built-in 帳號降級，已撤銷 Device 不得繼續執行有權限的雲端業務。
12. [ ] **發行授權與版本**：只有在最新 head CI、所有阻塞項驗收與當次明確正式 Release 授權到位後，才調整正式 `VERSION=3.0.0`／`BUILD=0`，從 `main` 走既有 `CYInvoice Stable Release` workflow 建立 Windows ZIP／SHA-256/tag。**現在既有 V2.6.19 發行不變**，工程包不可假裝是 Release。

## 停止／失敗處理

- 原 D1 身分、Workspace/Profile、備份可還原性、active Worker bindings、Secret 名稱或任何 checksum 不一致：**立即停止所有寫入**，保留原始證據。
- Migration 途中出錯：只讀核對已套狀態，不盲目重新 apply；**不自動 restore 整庫**（可能復活已撤銷授權）。
- 發信結果不明：保留同一 `operationId` 查詢，絕不自動重寄；過期邀請在**下次新 operation 簽發的同一筆 D1 交易**中自動結清，以 `AUTO_EXPIRED` 審計，不需人工先撤銷；查詢保持唯讀。
- Recovery claim 成功但回覆遺失：保留原 DPAPI Pending Token、原 scope，先讀回確認再決定是否重試；**不另生成 Token**。
- 3.0 不包含 3.1 Work Item／跨機審核、3.2 光貿 g0401/g0501、人工折讓歷史重做、3.3 其他事項；不可因後續版本功能延誤擋住本版。
- 本文件是操作交接，不代表已執行任一步驟；執行／授權證據由本機 Cloudflare MCP owner 私下保管。

