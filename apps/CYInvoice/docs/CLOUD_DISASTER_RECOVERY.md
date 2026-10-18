# CYInvoice V3 多機協同與 CYWEB 裝置復原設計

更新日期：2026-10-18（Asia/Taipei）。

**V3.0 現行狀態**：Windows V3.0.0 Build 0 已正式發布；使用者確認 3.0 原 Cloudflare 安全升級已由本機 MCP 完成，這一輪未從本機 MCP 重新 readback，**不得依本文重跑 0014／0015／CYID_ONLY 退役**。CYWEB #192 復原 Source 已合併，私有 Binding 的實際可用性尚未在本輪查驗；真 Email／雙 Windows／DR-01～13 尚待實測。3.1 只做非折讓跨機 Work Item，3.2 才做折讓，其餘在 3.3。現行待辦以 [TODO.md](TODO.md) 最新章節、3.1 工程以 [V3_1_HANDOFF.md](V3_1_HANDOFF.md)、真人驗收以 [RC_TEST.md](RC_TEST.md) 為準。

## 1. 本次決策與範圍

使用者要求災難復原與 V3 多機協同一起做，並由 CYID 超級管理員從 CYWEB 簽發復原邀請碼。CYWEB 是中央管理入口；CYID 驗證身分、Workspace Role 與 Application Access；CYInvoice Worker／業務 D1 負責原業務 Workspace、Device、邀請、Work Item／revision 及業務 Audit。

V3 的裝置管理與復原共用同一份 Device registry、邀請 lifecycle 與 audit owner。CYInvoice 的相關協同資料可以上自己的業務 D1；不將 CYID Credential、Session authority 或身分資料庫合併進來，也不以 Employee No 推定兩種 Workspace 是同一筆資料。

本段設計當時採用的歷史基準為 Windows V2.6.19、Cloud0.9.3、CYID_ENABLED=true、CYID_ONLY；現行正式 Windows 版本已升為 V3.0.0。其他公司 Local／獨立 Built-in D1 的既有功能保留，這項 CYWEB 復原入口不套用於 Built-in 模式。

## 2. 現有能力與真正缺口

| 原始碼／現況 | 已有能力 | 本次設計需補的部分 |
| --- | --- | --- |
| `cloud/src/index.ts` 的 issueInvitation／createPairing | 現行 Device + 超管授權後簽發邀請／配對 | 沒有可用舊 Device Token 時無法簽發 |
| previewInvitation／claimInvitation／claimPairing | 既有有效加入碼可讓新裝置加入，不必帶舊 Token | 不能把失效、過期或已消耗的碼當復原能力；新入口需完整重驗授權與 scope |
| `cloud/src/device-lifecycle.ts` | LAST_ACTIVE_DEVICE 保護、Device inventory／revoke | D1 的 active 筆數不代表有人仍持有可用 Token；不能用 active=0 作唯一復原條件 |
| `CloudDirectJoinForm.cs`、`SettingsStore.cs` | 發送前先保護 Pending Device Token，結果不明可用同一 Token 查回 | 復原需把預期 business／identity Workspace 與 Application scope 一起固定在 pending attempt |
| 現行 D1 maintenance 私有備份 | DPAPI 加密、原電腦解密／SQLite 還原及逐筆核對已驗證 | 不構成異機備份，不代表已完成 Cloudflare 遠端還原 |

現有 `device_invitations.issued_by_device_id` 是 NOT NULL／Device FK；不能為 CYWEB 簽發偽造一台 issuer Device，也不能借用遺失或 revoked Device 當管理入口。現有 audit event-type CHECK 與 deployment snapshot 也需隨 forward migration 一併適配，不能只新增 handler。

## 3. 情境分流

| 情境 | 接續方式 | 資料回復範圍 |
| --- | --- | --- |
| 至少一台舊裝置仍可用 | 沿用裝置端邀請／配對與管理 | 原 D1 不還原 |
| 所有舊 Token 遺失，但仍有有效未使用加入碼 | 核對原目標後沿用現行加入流程 | 新 Device 加入原 Workspace |
| 所有舊 Token 與有效加入碼都不可用，CYWEB／CYID／CYInvoice 可用 | CYWEB 超管簽發 V3 復原邀請，新裝置加入原 Workspace | 不重建 Workspace、不還原舊 Token、不回滾 D1 |
| CYID 帳密遺失或帳號尚未完成 Email 驗證 | CYWEB／CYID 的既有帳號復原／首次 Email 驗證 | CYInvoice 不另造帳號復原權威 |
| CYWEB、CYID、CYInvoice、D1 或 bindings 故障／資料毀損 | 維運端先恢復服務與資料，再做裝置復原 | 私有備份／相容程式／資料重整 |

CYWEB 的 core entry 可供有效員工進入 account self-service，不能推論 CYWEB 服務永遠在線。CYInvoice Application 未啟用、身分 Workspace disabled、CYID 不可用或 scope 不符時，不能簽發／兌換復原邀請；超管可在中央既有管理流程處理權限，復原動作不自動替他啟用 App。

## 4. CYWEB 簽發路徑與授權

建議第一版採 **CYWEB 當下登入確認 + 簽發時超管帳密再驗證**，直接沿用現有 CYID Login → Resolve → finally Logout，不新增跨 Application 的 Session 交換契約。

1. CYWEB server 經既有 canonical resolve 確認當下 core account Session 的有效 SUPER_ADMIN、已驗證 Email 與身分 Workspace；UI 顯示固定的 CYInvoice 復原目標及邀請有效期，操作者重新輸入正式密碼。first-login ticket 不能授權。
2. CYWEB 的 CSRF／Origin 防護由其正式 request owner 執行；Employee No／預期 Employee ID 由當下有效 principal 取得，不採信 browser 傳入 Role 或任意 Workspace。
3. CYWEB server 透過 deployment-injected **CYInvoice 專用 named private Service Binding** 呼叫簽發能力。瀏覽器不直接呼叫 named RPC，CYWEB 的 raw core Session 不傳到 CYInvoice、URL、D1 或 log。
4. CYInvoice private entrypoint 使用配置內的 Application／identity Workspace／business Workspace；以當次帳密建立 CYInvoice Application scoped Session，沿用 `withCyIdSession` 驗證 Resolve 的 SUPER_ADMIN／Email／App Access，並核對 stable Employee ID 與 CYWEB 預期 actor、身分 Workspace 一致。密碼與 provider token 僅存在該次受控 server transport。
5. 寫入前再次 resolve 最新權限、核對原 active business Workspace／CYID_ONLY profile／固定 binding。第一版沿用現有已驗證超管 Email 寄送：先建立 pending 邀請，送信成功後才受限批次轉為 sent 並記成功 Audit；送信失敗的碼不能 claim。provider 與 Email delivery 不可能和業務 D1 做跨服務原子交易，送信結果不明只查該次狀態，不自動重寄／重簽；未 active 的 pending 保持不可使用。終結該次 Session，Logout 失敗只記安全代碼，不重播簽發。
6. 只有這個私有、Employee-authorized 簽發入口不要求舊 Device Token。公開的裝置管理、一般邀請、業務 API 與既有 protected operations 仍依自己的 Device＋Employee 檢查執行；不能用 recovery flag 在公用 authenticateDevice 旁建立泛用 bypass。

Service Binding 證明是已配置的管理路徑，不能單獨取代員工授權。每次列狀態／撤銷／重寄也是獨立 protected operation，沿用同一授權 owner；不能以開窗時的舊 principal 或本地 offline cache 簽發。

本方案沿用 canonical 1.0.4 已有的 consumer auth 語意，未修改 shared contract。若後續改成「不用再次輸入帳密、直接委派 CYWEB core Session」，必須先在 CYID 正式定義 app-scoped delegation，更新 canonical/version/changelog／consumer 驗證；不能把 core token 改一個 Application header 就當授權。

本輪已核對 CYID canonical、session-auth 與 CYInvoice source；尚未取得 CYWEB 本輪實際 source／CSRF owner／部署 parity。跨專案實作前必須以其實際 repository 與 canonical owner 核對上述接線，不宣稱 CYWEB integration 已驗證。

## 5. 邀請、Device 與 Audit 的 D1 模型

優先擴充現有 `device_invitations` lifecycle，不建立第二套常態邀請管理。未套用的下一個 forward migration 才能加入來源／用途及 issuer constraint：

- `issuer_channel` 區分 DEVICE 與 CYWEB；`purpose` 區分正常加入與 RECOVERY。歷史 Invitation 保留全部原 ID、狀態與 actor，按既有來源可證的欄位設定 channel；無法證明的 actor provider 保留 UNKNOWN_LEGACY，不猜成 CYID。
- DEVICE issuer 保持原 `issued_by_device_id` 與 FK；CYWEB／RECOVERY issuer 的 Device 欄位可為 NULL，但必須有原 business Workspace、`issued_by_cyid_employee_id`，且不能填 Built-in issuer 欄位。這是資料來源語意，不是撤掉既有正常 Device 授權。
- 邀請保存不可逆 code hash、用途、簽發／到期／撤銷／消耗時間、issuer stable ID／credentialVersion／employeeRevision、claim 的新 Device reference。原始碼不提供固定有效邀請；建議沿用安全隨機碼，復原有效期先設短效 10 分鐘，在實作／UX 驗證時確認。
- 簽發成功只新增 invitation 與該 lifecycle 的 Audit；Device 在 claim 成功時才建立。每個 invitation 最多一台 Device，沿用唯一索引與同一原子 claim lifecycle。
- 管理 RPC 只允許固定 scope 的簽發、撤銷與查該次結果，不接受任意 SQL、provider override、Workspace reset 或通用管理代理。
- Audit 將 issuer channel、actor provider、stable Employee ID、事件／結果、invitation／Device reference 與 request ID 分清楚；擴充現有 event constraints 及 owner。不得新增密碼、provider token、邀請原碼、OTP 或完整發票內容。
- 一般 migration 不移除 Built-in 帳號功能；雙 storage-profile fixture／deployment fingerprint 需證明正常 Built-in 新 D1 與既有 CYID_ONLY D1 都能升級。

D1 不保存 Windows Device Token 明文，仍只保存 hash；Device Token 不是 Employee Session。Work Item／Audit 的外部 actor ownership 沿用同一 provider/source 語意，不由員編猜同人。

## 6. 新電腦兌換與結果不明

新電腦由現有加入流程的擴充入口輸入 endpoint、復原碼與當次超管帳密；復原邀請不要求借用舊 Device Token。第一版僅讓有效 current SUPER_ADMIN 使用 RECOVERY 邀請，避免把超管密碼交給一般員工；正常多機加入保留其正式授權流程。

兌換前顯示原業務 Workspace 並固定 endpoint／Application／identity Workspace／business Workspace。Windows 產生新的隨機 Device Token，經既有 protected storage 與 pending owner 保存後才送出；不保存超管密碼或 provider Session，也不複製舊機 Device Token／DPAPI settings 到新電腦。

claim owner 在每次送出、包括同 Token retry 前，重新驗證：

- 邀請 RECOVERY 用途、原目標 scope、已送達／可交付狀態、expiry、revocation、issuer principal 與當下 Role／App enablement；issuer 轉移／停用或 credential／revision 變動後重新簽發，不默認舊 grant 仍可用。
- Token hash 與既有 Device 的關係、Workspace active、CYID profile；已 revoked／外 Workspace 的 Device 不可因 `recovered=true` 復活。
- 新 Device insert、單次 invitation consume 與成功 Audit 為一個受 guard 的 transaction；競爭失敗不能留下未授權 orphan Device 或成功 audit。

若 response 遺失，保留同一 Pending Token，先透過 authenticated current-device/discovery 讀回原 Device／Workspace／provider scope。未確認時不產生第二個 Token、重簽 invitation 或重播業務；若確定尚未完成且邀請仍有效，才以原 attempt 重試。新身份正式寫入前核對 scope 全部一致；不使用 current claim 的「同 token 找到舊列」分支當作充分證據。

若是原安裝只遺失 Token 而本地資料仍在，需提供原 Workspace 的受控復原入口，不能要求 destructive reset 才能進加入畫面；核對原 scope 並保留 Data／Cache／Logs／光貿與本機設定，僅提交新的 Device identity。已收到明確 revoked 證據且進入既有 durable wipe 的安裝仍須完成原撤銷清除，復原不得取消 wipe marker 或復活舊 Device。全新電腦依新裝置流程處理，兩種情況分開測試。

成功後新 Device 接回**原 Workspace**。恢復一般雲端協同與從光貿查回的官方資料；不自動開票、不重送結果不明的 AMEGO mutation。若原機曾保存尚未上傳的輸入／本地 pending 且原機已遺失，不能承諾靠 D1／AMEGO 補回；V3 資料上雲的範圍與離線中斷另依 Work Item 設計確認。

## 7. 舊裝置管理與 V3 協同

**V3.0 實作採一次性的公司級裝置復原**：CYWEB 超管以當次密碼授權短效邀請；只有新 Device 完成經 SQL guard 的單次 claim 後，才在同一交易內撤銷**同 Workspace 所有其他舊 active Device**，並保留原 Device registry、Token hash／撤銷及安全 Audit 歷史。發碼、寄信、Preview、過期、自動 `AUTO_EXPIRED` 清理、未完成或失敗 claim **均不可提前撤銷舊 Device**。這是復原專用、可明確確認的不可逆權限切換，與正常裝置管理的 `LAST_ACTIVE_DEVICE` 禁止撤銷最後裝置是不同流程；絕不可對仍在業務中的唯一可信裝置進行未核准測試。

CYWEB 管理頁只顯示 invitation ID、到期與狀態，不將原碼放 URL／log；密碼／復原碼只用於當次操作或發給 CYID 當前已驗證的超管 Email。懷疑 Token 外洩但不打算新 Device 全面接管時，應依獨立事件維運流程處理，不能拿災復邀請替代一般 Device revoke。

V3 Work Item、revision／原子狀態轉移、跨機 OrderID 防撞、正式折讓／逐筆歷史與業務 Audit，共用原 Workspace／Device／actor 模型。裝置加入只恢復協同資格；本地 pending、Cloud Work Item 與官方 AMEGO 結果的重整不能因登入成功而直接標成完成。

## 8. 備份與服務故障回復

CYWEB 邀請解決「可信新 Device 加入原 Workspace」，不解決 D1 資料毀損或全雲端故障。備份回復是維運流程，不能由邀請碼觸發，也不授予超管在 UI 一鍵覆寫 D1。

可攜備份建議採成熟的 recipient encryption 工具 **age**，避免自行設計加密格式。只在私有維運環境使用經版本／來源驗證的工具；本文件沒有安裝、產生實際金鑰或重包現有敏感備份。[age 官方專案](https://github.com/FiloSottile/age) 提供 Windows 支援與加解密方式。

| 資產 | 保存與用途 |
| --- | --- |
| 業務 D1 邏輯備份 | 應用表、schema／index／sequence／migration history／storage profile；完整私有加密。不能用只有 counts 的報告代替 |
| 加密內部 manifest | capture UTC、固定 resource／scope、Worker source／active版本、bindings checkpoint、schema／profile、檔案 checksum 及精確比較資料；不放公開 metadata |
| 解密 identity／金鑰 | 由有權責人員在與備份、原 Windows／Cloudflare 帳戶分離的位置保管；加密端只需核准 recipient public key |
| Cloudflare 部署 Secret | 使用既有受控 secret 管理／重建方式，備份程式不從 Worker 讀取明文、不把 Secret 打包 |
| CYID authority／CYWEB 自有資料 | 由各自 owner 的備份／回復流程管理；不在 CYInvoice 私有 dump 偷渡備份或還原 CYID D1 |
| AMEGO 官方資料 | 以官方查詢重建可信狀態；無法回查的 V3 pending／actor 歷史需自己的 D1 保留與回復策略 |

維護前 DPAPI 備份保留其原用途；它包含退役前 Built-in schema，不能直接匯入目前 CYID_ONLY 當作最新版。可攜常態備份應取當前 profile 的一致 snapshot；未完成金鑰異地保管與另一台電腦的完整 decrypt／restore drill 前，異機災復不勾選完成。

Cloudflare [Time Travel](https://developers.cloudflare.com/d1/reference/time-travel/) 是有限期的整庫就地回復，會覆寫資料並中斷 in-flight queries；官方目前列 Free 7 天／Paid 30 天，實際窗口須在當次操作核對。不能把本機還原、bookmark 存在或 health 成功宣稱成遠端 restore 成功。

回復程序先固定目標與事故時間，記錄當前 private checkpoint／新資料，停止會寫入的服務與有權限的外部 writer；讀回確認停寫後才確定 snapshot。使用 read-only logical dump 時必須驗證擷取的一致性，不以跨多次 query 的結果自稱原子備份。

先在受控本機還原並核對完整 schema／rows／FK／quick_check，列出回復點之後會遺失或需重整的資料，核對 Worker 與 profile、bindings／CYID enablement。真實還原再以具體目標、資料損失範圍與新資料處置另行批准執行；response loss 時先查實際狀態，不盲目重播 restore。

舊 snapshot 可能復活已撤銷 Device、舊邀請或稍後已消耗的 code。對外恢復前須以回復前的可信撤銷／消耗證據和 incident decision 重整這些能力；查不到證據時維持封鎖並重新授權。CYID credential／Role／App Access 仍以當下 provider 為準，不能隨業務 D1 回滾。

不擴充為本機發票永久備份／匯出產品；既有 TODO 的單機不排入決定保留。備份保管人、獨立儲存位置、RPO／RTO、保留期限與定期 drill 尚需維運定案，不能宣稱已達服務承諾。此處描述備份設計，不建立排程或自動化。

## 9. 實作順序與驗收條件

V3 同一工作流分批接續，避免獨立災復產品先行：

1. 核對 CYWEB 正式 source／core Session／CSRF owner、canonical 契約及兩種 Workspace binding；對齊 Device registry、Invitation 與 Work Item actor 模型。
2. 以 forward migration 擴充既有 invitation／Audit 來源語意，保留舊 rows／FK；完成 private issuer 與 per-operation CYID 授權。
3. 接 CYWEB 超管簽發／撤銷／結果查核及 Windows 同一 pending owner 的復原加入，保留現有正常加入與 Built-in。
4. 接 V3 Work Item／revision／OrderID／正式折讓／不可覆寫逐筆歷史，完成雲端 pending 與官方結果重整。
5. 維運可攜備份工具、金鑰保管與隔離還原 drill 可隨 V3 並行；不是把 D1 restore 暴露到 CYWEB。
6. 本地／CI 通過後依批准範圍升級原 Worker／bindings／schema；完整實機證據只在實際執行後填寫。禁止遠端測試 Worker／handler 的既有決定維持。

| 驗收 ID | 必須證明 |
| --- | --- |
| DR-01 | 所有舊 Token 不可用、D1 舊 Device 仍 active 時，超管可從 CYWEB 簽發並讓一台新 Device 加入原 Workspace |
| DR-02 | 已有有效邀請可走原流程；失效／過期／已用碼不能旁路，不能 reset/bootstrap 原 Workspace |
| DR-03 | USER、ADMIN、Identity Admin、first-login ticket、過期／revoked core Session、未驗證 Email 均不能簽發復原邀請 |
| DR-04 | CYWEB core 授權與 CYInvoice App-scoped authority 均重驗；錯 Employee／identity Workspace／Application／business Workspace／profile 拒絕 |
| DR-05 | Provider outage、Workspace/App disabled、轉移超管、密碼／revision 改變後 fail closed；offline cache 不簽發或兌換 |
| DR-06 | 兩台／兩個 Token 同時兌換只有一台成功；失敗無 orphan Device、無成功 Audit；Device registry、issuer 與 actor 正確 |
| DR-07 | claim 已提交但 response 遺失、Windows 重啟／settings 寫入失敗，沿用同 Pending Token 找回；原安裝遺失 Token 保留同 Workspace 的本地資料，revoked wipe／外 scope 不旁路 |
| DR-08 | 簽發 response 遺失可查同一次 request 結果，不盲目重簽；過期／撤銷／重寄及雙重提交按同一 lifecycle 收斂 |
| DR-09 | **成功 claim** 後保留原 Workspace／業務資料／Device history，同一原子 D1 transaction 撤銷所有其他舊 active Device；未成功 claim、僅過期或錯碼一律不得撤銷；正常管理路徑仍維持 LAST_ACTIVE_DEVICE |
| DR-10 | 正常 Device issuer、配對／邀請與獨立 Built-in fixture 可升級；audit／history fingerprint／migration readback 仍成立 |
| DR-11 | 無帳密、provider token、原邀請碼或 Device Token 明文進入 D1／public source／logs；CSRF／rate/body limits 與 private capability 防濫用 |
| DR-12 | 異機使用獨立保管的金鑰還原完整私有備份；錯 key、改動／截斷密文、錯 profile／scope 拒絕，回復後撤銷／消耗狀態不倒退 |
| DR-13 | 3.0 新機復原不得重送任何 AMEGO 開票／作廢／人工折讓；未上雲的舊機資料遺失有明確提示。Work Item／跨機 revision／新折讓 API 的完整重整驗收**延至 3.1／3.2**，不得誤列為 3.0 未完工開發 |

以上均為後續驗收需求，本輪只完成 source／schema／canonical 與設計核對，未將 DR-01～DR-13 勾為通過。實機記錄入口見 [RC_TEST.md](RC_TEST.md)，工程停點見 [V3_1_HANDOFF.md](V3_1_HANDOFF.md)。

## 10. 原始碼與 canonical 參考

- [CYID Consumer Integration Standard](../../CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md)：Identity authority、core／consumer App Access、Session scope、consumer business authorization。
- [CYID session-auth](../../CYCloudIdentity/src/session-auth.ts)：provider Session 查詢綁定原 Application，不因改 header 變成跨 App Session。
- [CYInvoice cyid.ts](../cloud/src/cyid.ts)：固定配置、withCyIdSession、Login／Resolve／finally Logout、Device scope。
- [Worker dispatch](../cloud/src/app.ts)、[onboarding owner](../cloud/src/index.ts)、[Device lifecycle](../cloud/src/device-lifecycle.ts)：現行 Device／Employee gate、邀請／配對與最後 active Device。
- [Invitation／Audit schema](../cloud/migrations/0009_device_invites_and_audit.sql)、[CYID issuer migration](../cloud/migrations/0013_cyid_invitation_actor.sql)：既有來源／FK／constraints 與不可改寫的歷史 migrations。
- [Windows direct join](../src/CYInvoice.WinForms/CloudDirectJoinForm.cs)、[SettingsStore](../src/CYInvoice.Core/SettingsStore.cs)：protected Pending Token、scope discovery 與正式設定寫入。
