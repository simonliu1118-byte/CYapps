# CYAccountingWeb 平板介面設計與驗收

更新：2026/10/08。本文件記錄平板設計、已合併實作與尚未完成的真機驗收，不是永久規則。現在正式產品基準為 **V0.22.28 Build 0**；V0.22.28 已修正直式記帳 rail 把手 owner、三列輸入、canonical 直式收支、設定視窗／鎖帳／備份排版與 Desktop-only 設定項目，由 Production Deploy #547 驗證。

原始設計來源為 `cyaccountingweb/docs-tablet-ui-plan` 的 `b71e4108b7b3bd5e165dadeb526db4116c56be18`。規劃初期的自製 picker 已被後續實作收斂為 native/shared owner；目前不得把歷史設計稿中的自製 picker 建議當成現行需求。

## 現行配置

| 項目 | 橫向 | 直向 |
| --- | --- | --- |
| 結構 | 左記帳、右看帳；記帳區已在 Build 2/3 縮窄 | 看帳主區，底部記帳 rail |
| 表單 | 常駐左欄；帳戶／日期／科目／摘要／金額沿用共用欄位 | 底部記帳 rail 預設展開；把手由 rail 上緣向上凸出，收合顯示「↑ 展開新增」、展開顯示「↓ 收合隱藏」，收合時只剩凸起 tab；左側收入／支出沿用 canonical segmented-control 視覺，只改為 42px 窄、132px 高直式排列；右側三列為帳戶／科目／常用科目、日期／摘要／常用摘要、金額／儲存／清空或取消 |
| 帳本 | 保留表格、完整金額與餘額，獨立捲動；固定欄寬，空月份與有資料月份一致；操作表頭置中 | 不顯示「記帳資料」標題；月摘要固定五格並放大；不顯示獨立收支欄，金額直接以綠色 `+`／紅色 `−` 表達；編輯／刪除為圖示按鈕且操作表頭置中；底部表單不應覆蓋最後一筆 |
| 編輯 | 點編輯後帶入同一 entry owner | 點編輯後展開底部同一表單 |
| 日期／月份 | 與手機 touch path 共用原生 date/month owner；月份使用 shared `ledgerMonthDisplay`＋原生 `type=month` | 同一 owner、同一月份顯示層；月份外觀為 bounded touch capsule；摘要搜尋使用原生 `type=search`、至少 16px，無額外搜尋／清除按鈕 |
| 月份狀態／鎖帳 | canonical `lockedThrough` 與既有鎖帳設定 | 切月份顯示「載入中…」並暫停當月控制項；年月旁快速鎖帳只能將 `lockedThrough` 邊界逐月推進或退回，跳月、歷史已鎖與無初始邊界時按鈕停用 |
| 輸入方式 | 觸控、鍵盤、滑鼠／trackpad 並存 | 觸控、鍵盤、滑鼠／trackpad 並存 |
| 設定 | Settings 縮至 640px；月份鎖帳／備份使用 compact 排版；不建立資料管理與資料移轉頁 | 同一 Tablet 設定 owner；同樣不建立資料管理與資料移轉頁 |

程式目前仍以 adaptive UI 判斷 Mobile／Tablet／Desktop presentation，但 business/data owner 不依 breakpoint 分叉。平板日期／月份已不再建立自己的資料 state 或第二套 picker owner。

## 版本收斂紀錄

- **V0.22.2 / PR #292**：第一版橫向左記帳／右看帳、直向底部 rail、共用 entry edit 與既有 writer。
- **V0.22.2 Build 1 / PR #294**：照片返修、欄位防溢出、常用項目位置、工具列／統計及直式拖曳把手。
- **V0.22.2 Build 2 / PR #295**：橫式記帳區縮窄、帳戶／科目置中、常用項目常駐、工具與帳本密度調整。
- **V0.22.2 Build 3 / PR #296**：修正 iPad 被 desktop 日期／月份 picker 接管，橫式日期恢復原生 date、月份改用 touch/mobile display path。
- **PR #297 / V0.22.2 Build 4：未合併。** 不得作為目前 source 或交接基準。
- **V0.22.3 / PR #298**：開始移除 adaptive UI 版本殼命名。
- **V0.22.3 Build 1 / PR #299**：以單一 `isDesktopInteractionWorkspace()` 收斂 desktop interaction 判定，1024px 不再直接等於 desktop interaction。
- **V0.22.3 Build 2 / PR #300**：平板橫式日期／月份共用手機 touch/native owner，不再建立平板自己的日期／月份元件。
- **V0.22.19 Build 0 / PR #336**：直式底部記帳 rail 預設展開並保留唯一把手；平板雙方向移除 confirmation drawer edge handle；直式月份比照橫式共用 `ledgerMonthDisplay`＋原生 month owner。該版曾把平板帳號切成手機下拉元件，後續由 V0.22.20 Build 1 修正。
- **V0.22.3 Build 3 / PR #301**：移除多代 tablet date/month CSS override，只保留單一 presentation 規則，補 architecture regression。
- 後續 **V0.22.4～V0.22.8** 繼續移除 retry、重複 owner、toolbar relocation 與版本殼；因此平板後續修改也必須遵守現行 canonical owner，而不是把早期 patch 路徑加回。

- **V0.22.20 Build 0 / PR #338**：手機帳號選單新增「測試用平板版」；使用 session-scoped preview 直接切入正式 tablet presentation，直式 reference viewport 820px、橫式 1194px，旋轉手機即可檢查兩個方向；preview 不建立第二套 business/data owner。
- **V0.22.20 Build 1 / PR #340**：平板直式／橫式與手機 preview 都恢復原本 inline identity：`員工編號 姓名［角色］｜登出`。角色只共用手機色票（SUPER_ADMIN 金、ADMIN 銅、USER 中性），不共用手機 dropdown 結構；preview 暫時另加「返回手機版」。下一次公開 Stable Release 前要移除兩個 preview 測試入口與 session bootstrap。
- **V0.22.21 Build 0 / PR #342**：清除 V0.22.19 identity ownership 的殘留風險，平板直式／橫式固定使用 `.cy-account-cluster`，手機 account trigger/menu 在 Tablet 隱藏；角色改為姓名後方同行純文字，不做獨立 pill，設定按鈕與帳號列等高；SUPER_ADMIN／ADMIN／USER 沿用手機金／銅／中性色。手機 preview 的「返回手機版」預設隱藏，只在 `data-tablet-preview=true` 且按鈕非 hidden 時顯示，真實 iPad 不顯示。直式 rail 把手放大並加入展開／收起文字，表單濃縮為兩列；直式搜尋改原生 searchfield。Tablet／Adaptive regression tests 同步鎖定 ownership。
- **V0.22.22 Build 0 / PR #345**：平板直式沿用手機既有 busy/search/save-message/compact-utility lifecycle。切月份時 visible loading 並鎖住相關操作；搜尋 Enter 後收鍵盤，成功訊息淡出；期初餘額與月份鎖帳共用 compact utility。快速鎖帳圖示不新增 lock owner，只呼叫 canonical `saveLock()`：`lockedThrough` 的下一月可快速上鎖，只有 `lockedThrough` 本月可快速解鎖並退回一月，更早鎖定月與跳月未鎖月份皆停用；沒有初始 `lockedThrough` 時也必須先進既有月份鎖帳視窗設定。
- **V0.22.23 Build 0 / PR #347**：直式年月 selector 改為有邊界與下拉提示的 touch capsule，但 value/change 仍由原生 month input 擁有；搜尋 input 固定 16px，鍵盤開啟時 workspace 不再跟 visualViewport 縮高，避免 iPhone Safari focus zoom 與大片灰色空區。Excel 按鈕在直式移到期初餘額右側，Tablet 雙方向共用手機 native share。交易表直式隱藏「收支」，金額直接顯示收入綠 `+`／支出紅 `−`，操作欄改筆／垃圾桶圖示並保留 `aria-label`。
- **V0.22.24 Build 0 / PR #349**：平板雙方向「操作」表頭統一靠左；直式月摘要改用橫式同一套單列同行＋分隔線 presentation，並移除「記帳資料」標題。直式記帳 rail 移除「保持展開」checkbox 與 pinned-open 狀態，只保留把手點按／拖曳控制；canonical entry fields／writer 不變。
- **V0.22.25 Build 0 / PR #351**：直式把手改成中央「↑ 展開新增／↓ 收合隱藏」，移除灰色短槓。收入／支出仍共用原 `.kind-button` 與同一 entry kind state，只在直式變成最左側窄版垂直雙段；右側 12 欄 grid 排成帳戶／日期／科目／金額、常用科目／常用摘要快捷列、寬摘要＋儲存＋清空／取消三層。主要 input/select 維持 16px，手機與橫式 presentation 不改。
- **V0.22.26 Build 0 / PR #353**：依實機與手繪示意返修。月摘要固定在年月／工具列下方的獨立第三列，不再與月份 selector／工具按鈕共列。直式把手改為 entry panel 上邊框中央向上凸出的 tab，border-bottom 移除並以同背景補 seam，形成與面板一體的拉耳；收合時只留 10px 薄面板邊與凸起標籤。
- **V0.22.27 Build 0 / PR #355**：直／橫式交易表改為固定欄寬，empty row 不再改變表頭比例；操作表頭置中。月摘要改固定五格且放大，所有數值保留固定槽位。Excel 直／橫式與 tablet preview 明確共用手機 native file share，xlsx File 固定 MIME。直式把手固定白色，收合 rail 完全透明且無殘線；收入／支出文字使用 `writing-mode: vertical-rl`＋upright。
- **V0.22.28 Build 0 / PR #357**：直式把手從 entry-card 內部移到 `.cy-entry-rail` 上緣，展開時真正由面板上邊線往上凸出，收合只留下 tab。收入／支出保留 canonical segmented-control 的 gradient、active 白底、綠／紅邊框與陰影，只改成較窄較高的直式幾何。輸入 grid 固定三列：帳戶／科目／常用科目；日期／摘要／常用摘要；金額／儲存／清空或取消。Tablet Settings 寬度縮至 640px；月份鎖帳回到穩定三欄並收斂逐月控制；備份標題列／重新整理控制縮整；資料管理與資料移轉以 Desktop interaction guard 排除所有 Tablet presentation。PR #358 再更新 production semantic verifier 到 V0.22.28 cache rev 與新 marker。

目前 V0.22.28 Build 0 的 PR #357 與部署驗證修正 PR #358 已合併；Production Deploy #547（run `37714030732`）已完成 application tests、D1 migration、Worker/static assets、secure login 與 V0.22.28 semantic frontend asset verification。

## 元件與共用操作

| 能力 | 現行主路徑 |
| --- | --- |
| 帳戶／日期／月份 | 原生／shared touch 元件；同一資料值與 change owner |
| 收入／支出 | 共用既有 slider、純色畫布、entry kind 與鎖帳狀態 |
| 交易編輯 | 共用 entry editor + canonical `persistTransactionUpdate` |
| 交易新增 | 共用既有 submit path；optimistic create 尚未實作 |
| 編輯取消／換月份 | 共用取消／草稿還原；不得因旋轉或鍵盤誤送資料 |
| 帳戶排序 | 共用 Settings owner、pointer/touch sorting 與 rollback |
| 科目排序 | 共用階層 renderer、分類按鈕、touch/pen reorder／跨分類 |
| 交易列 | `public/ledger-tools.js` 唯一 canonical renderer |
| 設定／彈窗 | 共用既有 Settings surface；Tablet 只調 presentation |
| Excel | 共用 endpoint／期初 snapshot；分享或下載依瀏覽器能力 |
| 權限 | CYID + Worker server-side authorization；Tablet 無獨立 authority |

所有裝置共用 CYID、Role/App Access、API、D1、期初計算、帳戶色號與高風險判斷。USER 不因 Tablet layout 取得任何寫入能力。

## 已有自動證據

歷史平板回歸曾驗證方向判斷、旋轉草稿、編輯保留、visual viewport、展開收合、換筆及切月份不誤存。Build 1 使用 Chromium 153 與合成資料做過 1194×750 橫式、834×1100 直式、390×844 手機、1440×900 桌機檢查；當時手機／桌機基準在版本文字正規化後一致。

後續 V0.22.3 Build 1～3 加入互動判定、shared picker 與重複 CSS 規則 regression。V0.22.21 新增 identity ownership、preview-only return、兩列直式 entry、原生搜尋與桌面不受影響的 guards；V0.22.22 直接測試 `lockedThrough` 逐月推進／退回；V0.22.23 鎖定 16px iOS-safe search、穩定 Tablet layout height、Excel native share、收支欄移除、正負號與 icon-only actions；V0.22.25 鎖定垂直 kind switch、三層 entry grid 與文字／箭頭狀態；V0.22.26 鎖定凸起 tab 與摘要位置；V0.22.27 再鎖定 fixed ledger columns、五格摘要、操作表頭置中、tablet native file share、白色 tab／無殘 rail 與直向 kind labels；V0.22.28 再鎖定 rail-owned 凸起把手、42×132 canonical 直式 kind control、三列 entry grid、640px Tablet Settings、lock/backup compact layout，以及 Data Management／Migration desktop-only guards。PR #357 Validate #544、PR #358 Validate #546 及 Production Deploy #547 均成功。

自動測試與模擬瀏覽器仍不取代真機。

## 尚待真機驗收

| 待驗收 | 通過條件 |
| --- | --- |
| iPad／Android 雙方向 | 欄位、表格、工具、設定與彈窗皆可達，無重疊或被遮住 |
| 分割視窗／旋轉 | 草稿、目前年月、搜尋、編輯不遺失、不重複送出 |
| 螢幕鍵盤 | 焦點、儲存／取消可達；鍵盤只改可視高度，不誤判方向 |
| 原生選擇器 | date/month/select 可正常開啟、選值與收回，沒有 desktop picker 接管 |
| 觸控 | 帳戶／科目排序、列表捲動、原生 picker、交易操作互不攔截 |
| USER 權限 | 平板 presentation 不繞過 Worker server-side read-only gate |
| Excel | 真實瀏覽器分享／下載取得正確 XLSX |
| 外接輸入 | 鍵盤、滑鼠／trackpad 與觸控切換時操作語意一致 |

後續若平板需要修正，優先改 `public/adaptive-ui.js` 的 presentation 或現行 shared owner；不要恢復 PR #297 或更早版本殼／重複 picker。
