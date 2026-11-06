# CYERPAutoQuery V1.3.1 盤點完整清單配套交接

- 日期：2026-11-07（Asia/Taipei）；執行：GPT；分支 `cyerp/stocktake-complete-list-131`，基準 main `57fe609b93097999e8a83fe4a75ff84e31579210`。
- 使用者要求 CY Web 完整同步後，把 ERP 已清除的盤點單轉入盤點紀錄。此 PR 只補 ERP 清單完整性證明，歸檔規則及資料保存由 CY Web V0.12.1 實作；**不執行 ERP 清除、不改 STOCKTAKE_WRITE**。
- VERSION 1.3.1、BUILD 0；目前正式 Release 仍為 V1.3.0。本輪未合併、未建立 Release、未操作真實 ERP，待 Claude 審查及使用者發布授權。

## 原因及變更

- `ItemQueryService.Stocktake.cs` 舊流程在 OCR 讀不到總筆數時立即接受第一頁，不能作為「ERP 已不存在」的依據；讀到零張又一律失敗，無法處理 ERP 最後一張清除後的空清單。
- 新流程有上限地重讀 OCR；未知總數不翻頁、不把已讀頁當完整。總數變動即停止；確定數量相符才成功。讀到明確 0/0、零張時可成功回報，未知或部分清單仍失敗。`advanced` 只在實際 PageDown 後才開啟，避免等待 footer 後把同頁重讀誤判成翻頁無進度。
- `StocktakeQuery.RequireCompleteInventory` 集中驗證已知總數、0–200 上限、實讀筆數完全相符；`StocktakeTests.cs` 新增未知、缺頁、多頁超額、空清單及 outbound 協定測試。
- `ErpAgentProtocol.Stocktake.cs` 成功 `STOCKTAKE_SYNC` 的 body 增加 `listComplete:true`，包含全部 skip、沒有新明細的 NOT_FOUND 及真正 0/0 空清單；只有已通過 driver 完整性驗證的結果會走這個成功序列化。
- `VERSION`、`CHANGELOG.md`、`WORK_HANDOFF.md`／README 及本檔記錄配套依賴；沒有修改帳號、憑證、排程、更新器或其他 ERP 查詢。

## 跨 Repo 與相容性

- CY Web V0.12.1 的 full sync 可帶 server-only `knownSheets` 快照，**不在 claim 傳給 Agent**。只有 full sync、成功且 `listComplete:true` 才會把快照內已消失的同代底稿歸檔；單張 refresh 不歸檔。
- 舊 Agent 沒有 `listComplete`：新 WEB 照常同步，但不做歸檔。新 Agent 對舊 WEB 多一個欄位，舊 WEB 可忽略。因此可以先部署任一端，兩端都更新後才啟用歸檔；這是 rollout 的最小相容欄位，不是第二套盤點流程。正式支援的 Agent 最低版本提升並停用舊 Agent 後，可取消接受「未帶完整旗標」的相容窗口。
- CYID Consumer Impact: NONE。盤點資料不得出現在 Public source／log／artifact；測試僅合成資料。

## 驗證及待辦

- 本機 `dotnet test` 無法執行：系統只有 .NET runtime，沒有 SDK。未將這個結果稱為通過；由本次 PR 的 Linux／Windows CI 執行單元測試及 Windows GUI 建置，結果以最新提交為準。
- 未做正式 ERP OCR／翻頁／空清單實機驗收；尤其 0/0 footer 需在實際 ERP 版本核對。無法確認 footer 時會停止，不會誤送完整旗標。
- Claude 最終審查仍待填；執行者不得代填通過。Agent 正式發行仍須另行授權，不能因 CI 通過就觸發自動更新。
