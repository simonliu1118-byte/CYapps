# CYAccountingWeb TODO

更新：2026/10/29（台灣時間）。只記錄尚未完成或尚未完整驗收的工作；目前正式版本與證據見 [WORK_HANDOFF.md](WORK_HANDOFF.md)，已上線能力見 [README.md](README.md)，版本歷史見 [CHANGELOG.md](CHANGELOG.md)，永久規則只依根規則鏈與本專案 `PROJECT_RULES.md`。

## D1 讀取成本修正

- [ ] V0.25.13 完成本機與必要 CI、當次用量估算及完整備份／回復核對後，依使用者當次授權合併並受控部署 0012／0013 與原 Worker；正式結果只以維運回讀為準，不把 PR／dry-run 當成上線證據。
- [ ] 上線後以相同條件驗收期初快照命中／冷計算與摘要實際 `rows_read`，觀察索引、失效 triggers 及首次快照的寫入成本。程式回退不會自動回退 D1；schema13 備份須使用支援 schema13 的還原程式，不直接刪索引或改寫 schema marker。

## 部署模式切換驗收

- [ ] 部署流程 PR 合併後，確認 main 合併不自動部署；下一次收到發布指示時，依 [README 正式部署操作](README.md#正式部署操作) 推進發布分支或從 main dispatch，回讀相同 SHA 的 validate／deploy 成功與既有 production smoke。此待辦不授權現在部署或建立 Stable Release。

## 外觀主題（暫緩，須重新提出需求）

- [ ] CYACCweb 目前固定使用淺色介面，已移除「系統／淺色／深色」切換、登入頁外觀按鈕與主題偏好讀寫程式。此項目**不授權自動恢復**。
- [ ] 未來只有在使用者明確提出需求後，才重新評估參照 CYWEB 的深色模式；先建立統一 CSS token 主責，涵蓋帳本餘額、帳戶餘額浮窗、輸入確認、統計圖表、登入／設定／備份／期初對話框和各裝置，完成對比度與人工視覺驗收後才發布。

## 裝置與 UI 驗收

- [ ] 真實 iPad／Android 完整交叉驗收：直向、橫向、分割視窗、原生 picker、觸控排序／捲動、螢幕鍵盤與外接鍵盤。
- [ ] 真實平板登入、Session、登出、USER 唯讀及 Excel 分享／下載完整驗收。
- [ ] 手機、桌機以實際記帳資料完成最終交叉驗收；目前版本的自動回歸與既有桌機調整的自動回歸及 production semantic verification 不等於所有真機情境已逐項驗收；優先檢查 32px 收支控制、月份清單遮罩／轉圈、快速輸入與備份完整顯示、同級資料管理區塊、Excel 模板填寫匯入及 Session 逾時導回登入，同時確保手機／平板版未受影響。

## 統計與報表驗收

- [ ] 手機、平板直／橫式真機確認統計入口、圖表／統計頁籤、原生月份及圓餅名稱／百分比／完整圖例。
- [ ] 桌機不同視窗高度與長名稱／多科目月份驗收：引線標籤可讀、損益分頁無遺漏或重複、整月合計固定、無內部水平／垂直捲軸。
- [ ] 以私人資料確認移轉指定符合業務需要；統計收支及損益排除移轉，看帳現金流保留，未分類與封存帳戶資料完整。

## 交易互動

- [ ] 若未來加入 PWA、掃碼、拍照、離線或 Push，再由具體需求評估；不得為行動裝置另造第二套 business/data frontend。

## 帳務與 Identity


- [ ] 開帳基準／首期期初的後續工作以私人檢查點及使用者當次指示為準；先確認已完成操作與待補資料，再評估調整及歷史保留，不以公開文件推定帳務狀態。
- [ ] CYACC 密碼復原入口及完成後返回登入的銜接驗收；共用寄信／帳號啟用由 CYWEB 維護，使用者已於 2026/10/15 確認收信及啟用完成。

## 備份、復原與維運

- [ ] 2026/10/20 排程缺件：以有 Cloudflare MCP 的環境限量讀 actual trigger、Scheduled event、backup catalog／copies／health，分辨未觸發、建包前中止與 provider 失敗；查明前不以手動成功取代排程驗收。詳細診斷見私人維運交接檔；V0.25.6 警告框修正另待 PR 驗證、明確發布指示及正式回讀。
- [ ] V0.25.5 上線後首次每日自動可還原性檢查的正式排程回讀；異常寄信及登入警告已用 synthetic failure 驗證，不為驗收破壞正式備份或發假異常信。

- [ ] 觀察 Phase D 首次 GCS 週三／週日排程：2026/10/15 已回讀首次 R2-only 自動備份成功、GCS 本日未排程；新政策後的 GCS 排程仍待實際驗收，不以手動雙副本成功代替。
- [ ] Provider lifecycle 設定仍待核對；若啟用，期限需長於 application retention。Google Cloud 帳戶層級每月低額 budget 已由使用者建立並選擇包含優惠，建立證據見 WORK_HANDOFF。
- [ ] Phase E／shared Backup Service 與 CYWEB 主控協調；尚未啟用，不共用廣權限憑證或取消 App dataset 隔離。
- [ ] 未來若要停用 `workers.dev` 備援，另做明確切換決策與驗收；目前保留。

## 後續整合

- [ ] 若納入 Chihyuan 企業管理系統，先協調入口／權限／shared service；目前 CYACC 仍獨立部署、帳務 D1 與備份 dataset。
- [ ] Identity authority、跨 App ownership／routing／shared Backup 底層調整先同步 CYWEB／CYID 最新決策，沿用 canonical consumer contract。

下一個獨立開發項目應從目前 main 建新 branch，不延續未合併舊 branch。已完成項目不再堆在 TODO；請回 README／CHANGELOG 查已上線能力與版本歷史。
