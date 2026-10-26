# CYERPAutoQuery — ERP 行程診斷與安全重啟：實作報告暨 Claude 交接

> **2026-10-26 更新**：本報告描述的 Build 1「親自啟動才可終止」做法已由 V1.0.1 Build 2 取代（以 ERP 視窗與工作階段挑選行程、閒置關閉恢復原行為），現況見 `WORK_HANDOFF.md` 與 `CHANGELOG.md`。

- 整理日期：2026-10-23（Asia/Taipei）
- 交接方向：GPT 完成程式修改及 CI 驗證 → Claude 審查、追蹤實機驗收、決定後續修正與是否發行
- Repository：[simonliu1118-byte/CYapps](https://github.com/simonliu1118-byte/CYapps)
- 專案：`apps/CYERPAutoQuery/`（SMART ERP 自動查詢工具）
- 工作分支：`cyerp-auto-query/feature-process-diagnostics`
- PR：[ #555 — CYERPAutoQuery V1.0.1 Build 1：唯讀行程診斷與精準行程保護](https://github.com/simonliu1118-byte/CYapps/pull/555)（Draft；**未合併**）
- 最後驗證原始碼 SHA：`385590c30f9c5dc6b3577f3c16cfa948c8953158`（不含本次交接文件 commit）
- 工程版本：`VERSION=1.0.1`、`BUILD=1`；正式 Release 仍為 `cyerp-auto-query-v1.0.0`
- **狀態：程式修改與 Windows CI 完成；安全機制的實機驗收／Claude 審查未完成；未部署、未 Release。**

> 此檔為一次性工作報告／交接紀錄，不構成新的永久規則。優先依 `REPOSITORY_RULES.md`、`REPO_POLICY.md`、`apps/CYERPAutoQuery/PROJECT_RULES.md`；共通規則 `2.10.0` 已與 AITeam main 核對一致。使用者要求「不要動到其他功能」，所以不擴大至 CY Web、CYERPAutoInput、查詢業務邏輯或 CI workflow。

## 1. 背景與原始需求

使用者的本機 SMART ERP 僅有約兩人低頻查詢，擔心新增兩項自動功能影響 Local ERP 伺服器、誤關閉共用行程；希望能**精準處理 ERP 的兩個 Windows 行程**，並先做診斷。使用者在 2026-10-22 明確同意：

1. 新增獨立**唯讀**行程診斷，不修改 ERP 資料，也不透過診斷介面 Kill／重啟。
2. 查看 `MainMenu`（工作管理員描述為 `MainMenu 64-bit(Release)`）與 `SMLeaderWorkCenter.exe` 的實際關係。
3. 根據實機診斷修正行程辨識與異常重啟，避免影響其他使用者；不動原有查詢／Web Agent。
4. 修改後用 Windows CI 工程包驗證；不逕行合併、部署或正式發行。

先前程式的 `ItemQueryService.Login.cs::RestartErpAsync` 會以 `Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Login.ErpPath))` 列出所有同名行程，逐一 `Kill(entireProcessTree: true)`；這不能區分同機其他 ERP 實例，是本次優先排除的風險。原本閒置關閉亦未區分是否屬 AutoQuery 所擁有的 Windows 行程。

效能面：CY Web Agent 閒置時每 3 秒向 Web Worker 輪詢一次，**不是每 3 秒查 ERP／資料庫**；只有接到查詢工作才操作 ERP UI。兩人低頻使用預期額外查詢壓力有限，但**未對 ERP 伺服器做 CPU／RAM／DB 性能實測**，不能把此判斷當效能保證。ERP GUI 自動化需互動式 Windows 工作階段，避免共用操作桌面及未經驗證的 RDP 情境。

## 2. 使用者實機診斷：已取得的證據

第一版 V1.0.1（Build 0）工程包的診斷工具由使用者在實際 ERP 電腦執行，使用者**確認採用「人工正常關閉 ERP，再正常重新啟動」**，不是用工作管理員強制結束。

使用者提供的一份遮蔽診斷文字檔記錄了 28 次掃描，觀察到：

| 時間（臺灣） | 觀察 |
| --- | --- |
| 2026-10-22 17:23:59 | `SMLeaderWorkCenter` 上的 `INVI02` 視窗類型發生變化 |
| 17:24:12 | 原有 `MainMenu` 與 `SMLeaderWorkCenter` 兩個行程都不再可見 |
| 17:24:16 | 新的 `MainMenu` 與 `SMLeaderWorkCenter` 都出現，位於同一 Windows Session 1 |
| 17:24:18 | `MainMenu` 出現 ERP 主畫面候選視窗 |

其他已知：二者 **Parent PID 不同**；`INVI02` 作業視窗出現在 `SMLeaderWorkCenter`。正常關閉／開啟時兩者呈一致的生命週期，**但不等於具有直接父子關係、互為專屬行程、或可安全一併強制終止**。

第一版報告同時把 `CYERPAutoQuery` 自己列為 ERP 候選：原因是掃描器以視窗標題包含「SMART ERP」當候選條件。使用者回報後，Build 1 已修正。診斷報告的 Windows 執行帳戶原本均顯示「無法讀取」，所以**沒有足夠證據做跨帳戶／跨 RDP 工作階段的歸屬推斷**。

**資料保護：** 實機診斷原始 LOG、PID、實際帳戶、完整路徑及其他 runtime 資料不提交 Public repo；上述為不含機敏值的分析摘要。若 Claude 需要原檔，應請使用者在對話或允許的私有管道重新提供。

## 3. 已實作功能（按版本）

### 3.1 V1.0.1 Build 0 — 唯讀行程診斷

- `ErpProcessDiagnosticForm.cs`：在既有「ERP 登入」設定窗內提供「行程診斷（唯讀）」入口；手動掃描、每 2 秒輪詢觀察、清單呈現、匯出文字報告。
- `ErpProcessDiagnosticScanner.cs`：讀取本機可存取 Windows 行程、父 PID／Session／CPU／記憶體與 ERP 視窗類型；以 WMI 嘗試讀取父行程與帳戶，權限不足時降級。
- `ErpProcessDiagnosticModels.cs`：快照與變化比對；PID 搭配啟動時間辨別重新出現／PID 重用；匯出時不包含帳戶原文、完整路徑、命令列或原始視窗標題。
- `ErpLoginSettingsForm.cs`：只新增診斷入口，無需改 ERP 查詢入口。
- `CYERPAutoQuery.csproj`：新增 `System.Management` 相依；`ErpProcessDiagnosticTests.cs` 驗證變化比較與報告遮蔽。

**已發現並在 Build 1 修正的缺陷：** 第一版候選行程把 AutoQuery 本身誤列成 ERP。

### 3.2 V1.0.1 Build 1 — 精準歸屬與 fail-closed 安全修正

- `ErpProcessDiagnosticScanner.cs`：排除目前 AutoQuery 行程與 `CYERPAutoQuery` 名稱；以明確 `MainMenu`、`SMLeaderWorkCenter` 與設定的 EXE 名稱找 ERP 候選，不再僅憑視窗標題加入任意行程。
- 新 `ErpManagedProcessIdentity.cs`：儲存 AutoQuery **親自 Process.Start** 取得的 PID、UTC 啟動時間、Windows Session ID、實際 EXE 完整路徑；驗證時完整比對，另檢查啟動時間是否符合本次 launch（排除沿用很久以前的單例行程）。此歸屬只存記憶體，不跨 AutoQuery 重新啟動繼承。
- `ItemQueryService.Login.cs::StartAndLoginAsync`：在啟動 ERP 時記錄可驗證的行程歸屬。啟動／登入流程本身不重新設計。
- `ItemQueryService.Login.cs::RestartErpAsync`：不再 `GetProcessesByName` 全部 Kill，也不使用 `Kill(entireProcessTree: true)`；透過開啟指定行程的 handle 重新比對 PID、開始時間、session、路徑，且確認該行程是 ERP 主程式（`MainMenu` 名稱或所持有的主視窗）及沒有其他 ERP 主視窗時，才可只對**這一個行程**呼叫 `Kill()`。不符則停止自動重啟、記 LOG 並要求人工檢查。
- `ItemQueryService.Login.cs::CloseKeptFormsAsync`／`CloseErpAsync`：閒置關閉同樣須通過行程歸屬驗證；不同 PID 的作業視窗一律不送 `WM_CLOSE`，主視窗也只作用於已核對 PID。
- `ErpManagedProcessIdentityTests.cs`：驗證正確 identity／不同 session／PID 重用／路徑變更或缺失／舊單例行程不得被承接。

**刻意改變的行為（請 Claude 審查）：**

1. **人工／其他程式先啟動的 ERP 仍可被 AutoQuery 查詢，但本版不負責其強制重啟及閒置關閉。** 因此原先「ERP 當機就一定自動重啟」的使用體驗縮小了，屬保護性取捨。
2. `SMLeaderWorkCenter` 的歸屬未經證實，不能直接 Kill，也不直接對它不同 PID 的作業窗發送閒置 `WM_CLOSE`。如果 `MainMenu` 異常終止後 helper 沒有跟著退出，可能需要人工介入；**沒有實機驗證 helper 在此情境的行為**。
3. 目前未解決多個 RDP 工作階段同時操作 ERP、所有跨 Session 情境與其他 CY 自動化程式跨 session 互斥。原 `Local\Chihyuan.SmartErp.UiAutomation` mutex 仍只在相同 Windows Session 有效；不能把它當整臺機器全域鎖。
4. `CanRestartErp` 仍以 ERP 自動登入設定是否齊全為準；實際遇到未持有行程歸屬的當機情境，現在會在 `RestartErpAsync` 中 fail-closed 並顯示錯誤，而不是強制終止。需實機檢查錯誤是否清楚、是否會造成重複嘗試。

## 4. 變更檔案與未修改範圍

本次 PR #555 主要檔案：

| 範圍 | 檔案 |
| --- | --- |
| 唯讀 UI 與掃描 | `ErpProcessDiagnosticForm.cs`、`ErpProcessDiagnosticScanner.cs`、`ErpProcessDiagnosticModels.cs`、`ErpLoginSettingsForm.cs` |
| 執行個體歸屬與安全關閉 | `ErpManagedProcessIdentity.cs`、`ItemQueryService.Login.cs` |
| 編譯與測試 | `CYERPAutoQuery.csproj`、`tests/CYERPAutoQuery.Tests/CYERPAutoQuery.Tests.csproj`、`ErpProcessDiagnosticTests.cs`、`ErpManagedProcessIdentityTests.cs` |
| 版本與狀態 | `VERSION`、`BUILD`、`CHANGELOG.md`、`README.md`、`WORK_HANDOFF.md` |

未修改 `ItemQueryService` 主要查詢流程、RESTOCK 報表查詢、登入時帳密輸入序列、`ErpAgent`／CY Web 協定、`CYERPAutoInput`、任何 GitHub Actions workflow；未執行 DB 變更、正式 ERP Kill、伺服器部署、PR 合併或正式 Release。

## 5. 驗證證據與可取得的工程包

| 項目 | 結果與可查來源 |
| --- | --- |
| 共通規則 | CYapps `COMMON_RULES_VERSION` = AITeam main `2.10.0` |
| Build 0 Windows CI | [CYERPAutoQuery Build Run #57](https://github.com/simonliu1118-byte/CYapps/actions/runs/39748508265)：成功，含測試、編譯、封裝、掃描、上傳 |
| Build 0 工程包 | Artifact ID `12411853931`，`CYERPAutoQuery-v1.0.1-windows-x64-run57`；已用來收集第一次實機行程診斷 |
| Build 1 最終 Windows CI | [CYERPAutoQuery Build Run #68](https://github.com/simonliu1118-byte/CYapps/actions/runs/39767604478)，HEAD `385590c…`：**成功**；Unit tests、Restore、Build、Publish、package structure、public safety scan、Artifact upload 全通過 |
| Build 1 工程包 | [`CYERPAutoQuery-v1.0.1-build1-windows-x64-run68`](https://github.com/simonliu1118-byte/CYapps/actions/runs/39767604478/artifacts/12421151692)，Artifact ID `12421151692`（約 149.6 MB；GitHub 3 天保留，預計於 2026-10-25 19:05 臺灣時間到期） |
| 治理檢查 | 最新 source HEAD 的 Governance Check：成功 |
| 實機正常關閉／重啟 | **已驗證在 Build 0 診斷工具上記錄到兩個行程一同消失／重新出現**；使用者明確認定測試是人工正常關閉，不是強制 Kill |
| Build 1 ERP 異常重啟／閒置關閉實機驗收 | **未執行／未通過**；CI 成功不代表這兩項真實 ERP 行為已通過 |

注意：Artifacts 有時間限制，過期後要依正式 source 重新產生；不能把工程包當正式 Release。

## 6. 尚未證實、需審查的安全邊界

1. **真正的 ERP EXE 歸屬：** `Process.Start` 在使用者實機上回傳的 PID／路徑，是否就是持有 `MainMenu` 主視窗的程序？是否存在 launcher、單例轉交或快速啟動→換 PID？如果不是，Build 1 預期會 fail-closed，不應為了恢復便利性而放寬到按名稱 Kill。
2. **兩行程關聯：** `MainMenu` 正常 WM_CLOSE 之後 `SMLeaderWorkCenter` 是否同步退出？強制終止唯一主行程時 helper 是否殘留？另有其他使用者時 helper 是否共用？正常關閉 LOG 不能作這些問題的答案。
3. **閒置關閉回歸：** 原來 10 分鐘自動關閉作業窗／30 分鐘自動關閉 ERP 的行為，Build 1 會略過不同 PID helper 視窗與人工啟動的 ERP；Claude 應確認這與使用者原意一致，以及 UI／LOG 是否足夠告知。
4. **多 Session／權限：** 同臺伺服器其他使用者透過 RDP 登入時，`Local\...` mutex、視窗列舉與行程比對是否都能防止跨 session 互相干擾？不能只用同 Session 正常測試推論。
5. **觀察限制：** 原診斷工具仍有 Windows 帳戶「無法讀取」情況；部分系統行程的完整路徑／啟動時間也可能因權限無法讀取。報告採樣每 2 秒，只能指出掃描間的可見變化，不等於 Kernel 級連續事件紀錄。
6. **專案規則文字差異：** `PROJECT_RULES.md` §2 仍描述既有自動強制重啟及預設閒置關閉行為；本 PR 收斂了可操作對象。此檔為永久規則來源，**本輪未擅自更改**。Claude 應檢查是否需依治理程序修訂及附對應版本、變更紀錄；不能僅以本交接文件覆蓋規則。
7. **疑慮與代碼審查：** `IsFreshLaunch` 採本次啟動時間附近的容差檢查，仍需審查競態／單例啟動；`Process.Handle` 與 `Process.MainModule` 的例外處理、窗口歸屬判斷、`WM_CLOSE` 確認對話框行為都需在真實 Windows 權限情境覆核。

## 7. Claude 接手順序與驗收建議

**第一優先（不觸及正式 ERP）**

- [ ] 從 repo 最新 `main`、三層規則與 [PR #555](https://github.com/simonliu1118-byte/CYapps/pull/555) 開始，確認基準、版本身分及 diff。依 PR 當前 head SHA 重新檢查 CI，勿直接以此文件宣告驗收通過。
- [ ] 審查 `ErpManagedProcessIdentity`／`CaptureStartedErp`／`OpenOwnedErp`／`RestartErpAsync`／兩個 idle-close 方法的競態、歸屬失敗路徑與其他 session 防護。
- [ ] 驗證修正後診斷不再包含 `CYERPAutoQuery` 自身，且帳戶／路徑遮蔽有效。
- [ ] 優先確認人工啟動 ERP 的正常查詢及 Web Agent 未受回歸影響（單次品號查詢即可作基本 smoke）。

**第二優先（在隔離 Windows 測試機／專用桌面作正常操作）**

- [ ] 由 AutoQuery 在 ERP 未開啟的條件下自行啟動並登入；觀察 LOG 是否成功擷取啟動 PID、時刻及主視窗歸屬。
- [ ] 在沒有其他 ERP 使用者、沒有未儲存 ERP 作業的前提下測試正常關閉／自動閒置關閉；確認 MainMenu 與 helper 的退出狀況。
- [ ] 開啟另一個 Windows 使用者工作階段，確認被管理行程的歸屬驗證不會選中其他 session／其他人工啟動的 ERP。
- [ ] 只有上述確認後，才**在隔離且經使用者許可的測試環境**模擬失去回應並檢查異常重啟。**不要在共用正式 ERP 伺服器上刻意製造當機或 Kill。**
- [ ] 如不安全或實機不符：在 PR 記錄能重現的測試結果、LOG 類別／脫敏差異，再做最小範圍修正；同一 V1.0.1 項目若返修依規則使用 `Build N`，不是再無故升版。

**第三優先（交付／治理）**

- [ ] 核對 `PROJECT_RULES` 的原敘述與安全收斂是否需正式更新；若需治理變更，依 repo 規則走指定分支及版本紀錄，不在 PR 裡順手改。
- [ ] 更新 `WORK_HANDOFF.md` 實測狀態、PR 審查紀錄與 CHANGELOG；如驗收未全過，維持 Draft，不做正式 Release。
- [ ] 合併、發行或部署只能按使用者後續指示執行；不得以 CI 成功自行推進。

## 8. 給 Claude 的結論

本輪交付的**已證實成果**是：唯讀診斷可以收集 Windows 行程資訊；使用者透過 Build 0 正常關閉／重啟實測，證明 MainMenu 與 SMLeaderWorkCenter 在同一 Session 中同時消失／再出現，但兩者不同父 PID；Build 1 已把危險的「同名全部 Kill／Kill 全程式樹」改成「只操作本次 AutoQuery 已核對身分的單一主行程」，並修正候選行程誤判。Windows CI **已通過**。

**尚未證實**的是：Build 1 在真實 ERP 當機、閒置與多人 Session 時的行為、helper 的可安全終止性、以及伺服器資源負載。Claude 下一步應以安全審查與實機分階段驗收為主，保留 fail-closed 原則，不要把「兩個行程看起來會一起正常退出」視為「可以一鍵 Kill 兩個行程」。
