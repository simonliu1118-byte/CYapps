# CYAccountingWeb TODO

更新：2026/10/03（日本時間）。只記錄待辦及驗收狀態；目前版本與證據見 [WORK_HANDOFF.md](WORK_HANDOFF.md)，永久規則見 PROJECT_RULES.md。

## 平板 V0.22.2（Draft PR #292）

- [x] 第一版橫向左記帳／右看帳、直向看帳主區＋底部收合／保持展開記帳欄。
- [x] 共用原生 account/date/month、手機 entry edit owner、交易 writer、設定排序及失敗還原。
- [x] 自動回歸：旋轉保留草稿與編輯、鍵盤只改可視高度、重複選取及切月份取消、USER 唯讀邊界。
- [x] 本機 23 組回歸及 PR head `8945abf` 的應用 CI #373／Governance #961 成功。
- [ ] 隔離預覽中驗證實際瀏覽器版面；本機 Chromium 下載失敗，尚無實際 browser layout 證據。
- [ ] 真實 iPad／Android 直向、橫向、分割視窗、觸控排序／捲動、原生選擇器、螢幕鍵盤與外接鍵盤驗收。
- [ ] 真實平板登入、Session、登出、USER 唯讀及 Excel 分享／下載驗收。
- [ ] 真機驗收後確認合併／正式部署；目前保持 Draft，不發布 Release。

## 其他裝置及互動

- [ ] 手機、桌機以實際記帳資料完成最終交叉驗收；V0.22.0 手機穩定 Release 已發布，不代表每一裝置情境已逐項驗收。
- [ ] 一般新增記帳的共用 optimistic 更新與失敗回復尚未實作；現行各裝置共用既有等待送出流程，不能因名稱「新增」就當成高風險例外。
- [ ] 若需 PWA、掃碼、拍照、離線或 Push，再由具體需求評估；沒有第二套行動前端。

## 帳務及 Identity

- [x] 桌面 SQLite production 移轉內容：使用者於 2026/10/03 確認 OK，內容驗收已結案。
- [ ] 使用者補齊帳務資料並提供自動計算起始年月後，再評估開帳基準調整與歷史保留；目前未修改正式資料。
- [ ] Password Recovery 真實 Email delivery／browser 完整驗收。
- [ ] CYID post-cutover 驗收穩定後，以獨立 forward migration 退休 `web_sessions` 實體表；現行權威已是 CYID，沒有舊表 fallback。

## 備份、復原及維運

- [x] GCS production 備份與完整性驗證已於 2026/09/26 通過；R2＋GCS 手動 paired 驗收於 2026/09/27 通過。
- [ ] Phase C：讀正式 catalog／UI 確認連續排程雙副本 `x/14`；只計 scheduled、同 backupId、有效 digest 及兩 provider 成功。尚未取得最新進度，不按日期推算。
- [ ] Phase D：僅在 Phase C 驗收後，評估 R2 每日／GCS 每週三與週日、GCS 26 週 retention；現行仍為 R2 30 天、GCS 每日 14 天。
- [ ] 復原：SUPER_ADMIN server gate、雙重確認、格式／schema／digest 驗證、受控 D1 寫入、對帳及操作者／結果 audit。
- [ ] 災難復原：R2 不可用時使用有效 GCS copy，以及從 GCS 重建新 D1 的完整演練。
- [ ] Google Cloud 每月低額 budget 警示（目標 NT$100）；provider lifecycle 若啟用，需長於當時 application retention。
- [ ] Phase E／shared Backup Service 與 CYWEB 主控協調；尚未啟用，不共用廣權限憑證或取消 App dataset 隔離。
- [ ] 未來若要停用 workers.dev 備援，另做明確切換決策與驗收；目前保留。

## 後續整合

- [ ] 若納入 Chihyuan 企業管理系統，先協調入口／權限／shared service；目前 CYACC 仍獨立部署、帳務 D1 與備份 dataset。
- [ ] Identity authority、跨 App ownership／routing／shared Backup 底層調整先同步 CYWEB／CYID 最新決策，沿用 canonical consumer contract。

已完成能力集中於 README；版本歷史集中於 CHANGELOG，不再把所有歷史完成項目當成目前待辦。
