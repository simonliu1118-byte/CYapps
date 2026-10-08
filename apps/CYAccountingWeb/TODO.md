# CYAccountingWeb TODO

更新：2026/10/08（日本時間）。只記錄尚未完成或尚未完整驗收的工作；目前正式版本與證據見 [WORK_HANDOFF.md](WORK_HANDOFF.md)，已上線能力見 [README.md](README.md)，版本歷史見 [CHANGELOG.md](CHANGELOG.md)，永久規則只依根規則鏈與本專案 `PROJECT_RULES.md`。

## 裝置與 UI 驗收

- [ ] 真實 iPad／Android 完整交叉驗收：直向、橫向、分割視窗、原生 picker、觸控排序／捲動、螢幕鍵盤與外接鍵盤。
- [ ] 真實平板登入、Session、登出、USER 唯讀及 Excel 分享／下載完整驗收。
- [ ] **下一次公開 Stable Release 前**移除手機「測試用平板版」、preview「返回手機版」及其 session-scoped tablet preview bootstrap／測試專用 UI；正式平板本身的 inline identity 與直／橫式 presentation 保留。
- [ ] 手機、桌機以實際記帳資料完成最終交叉驗收；目前 V0.22.30 Build 0 的自動回歸與 production semantic verification 不等於所有真機情境已逐項驗收；優先檢查電腦記帳四段式、常用科目／摘要排列與縮小、單一日曆 icon、金額正負色彩、隱藏收支欄、切月 Loading、期初餘額右側 audit、五格月摘要、行內編輯欄位及右上角色無 pill，同時確保手機／平板版未受影響。

## 交易互動

- [ ] 一般**新增記帳**的共用 optimistic update 與失敗 rollback 尚未實作；目前各裝置共用既有等待送出流程。 先前未合併 `cyaccountingweb/optimistic-transaction-create` 分支已暫停，沒有部署或取得完整驗證；續作須從 main 正式基準重新檢視。
- [ ] 若未來加入 PWA、掃碼、拍照、離線或 Push，再由具體需求評估；不得為行動裝置另造第二套 business/data frontend。

## 帳務與 Identity

- [ ] 使用者補齊帳務資料並提供自動計算起始年月後，再評估開帳基準調整與歷史保留；目前未修改正式資料。
- [ ] Password Recovery 真實 Email delivery／browser 完整驗收。
- [ ] CYID post-cutover 穩定後，以獨立 forward migration 退休 `web_sessions` 實體表；現行 authority 已是 CYID，沒有舊表 fallback。

## 備份、復原與維運

- [ ] Phase C：讀正式 catalog／UI 確認連續排程雙副本 `x/14`；只計 scheduled、同 backupId、有效 digest 且兩 provider 成功。**尚未取得最新進度，不按日期推算。**
- [ ] Phase D：僅在 Phase C gate 通過後，評估 R2 每日／GCS 每週三與週日、GCS 26 週 retention；現行仍為 R2 30 天、GCS 每日 14 天。
- [ ] Restore：SUPER_ADMIN server gate、雙重確認、格式／schema／digest 驗證、受控 D1 寫入、對帳及操作者／結果 audit。
- [ ] DR：R2 不可用時使用有效 GCS copy，以及從 GCS 重建新 D1 的完整演練。
- [ ] Google Cloud 每月低額 budget 警示（目標 NT$100）；provider lifecycle 若啟用，需長於 application retention。
- [ ] Phase E／shared Backup Service 與 CYWEB 主控協調；尚未啟用，不共用廣權限憑證或取消 App dataset 隔離。
- [ ] 未來若要停用 `workers.dev` 備援，另做明確切換決策與驗收；目前保留。

## 後續整合

- [ ] 若納入 Chihyuan 企業管理系統，先協調入口／權限／shared service；目前 CYACC 仍獨立部署、帳務 D1 與備份 dataset。
- [ ] Identity authority、跨 App ownership／routing／shared Backup 底層調整先同步 CYWEB／CYID 最新決策，沿用 canonical consumer contract。

下一個獨立開發項目應從目前 main 建新 branch，不延續未合併舊 branch。已完成項目不再堆在 TODO；請回 README／CHANGELOG 查已上線能力與版本歷史。
