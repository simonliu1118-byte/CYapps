# CYAccountingWeb 平板介面設計與驗收

更新：2026/10/07。本文件記錄平板設計、已合併實作與尚未完成的真機驗收，不是永久規則。現在正式產品基準為 **V0.22.21 Build 0**；V0.22.21 已再次收斂平板 identity ownership、直式兩列記帳 rail 與原生搜尋，並由 Production Deploy #509 驗證。

原始設計來源為 `cyaccountingweb/docs-tablet-ui-plan` 的 `b71e4108b7b3bd5e165dadeb526db4116c56be18`。規劃初期的自製 picker 已被後續實作收斂為 native/shared owner；目前不得把歷史設計稿中的自製 picker 建議當成現行需求。

## 現行配置

| 項目 | 橫向 | 直向 |
| --- | --- | --- |
| 結構 | 左記帳、右看帳；記帳區已在 Build 2/3 縮窄 | 看帳主區，底部記帳 rail |
| 表單 | 常駐左欄；帳戶／日期／科目／摘要／金額沿用共用欄位 | 底部記帳 rail 預設展開；唯一把手加大並顯示「展開記帳／收起記帳」，仍可點按／拖曳；展開內容濃縮為兩列，右上 confirmation drawer 把手不顯示 |
| 帳本 | 保留表格、完整金額與餘額，獨立捲動 | 保留表格；底部表單不應覆蓋最後一筆 |
| 編輯 | 點編輯後帶入同一 entry owner | 點編輯後展開底部同一表單 |
| 日期／月份 | 與手機 touch path 共用原生 date/month owner；月份使用 shared `ledgerMonthDisplay`＋原生 `type=month` | 同一 owner、同一月份顯示層；摘要搜尋使用原生 `type=search`，無額外搜尋／清除按鈕 |
| 輸入方式 | 觸控、鍵盤、滑鼠／trackpad 並存 | 觸控、鍵盤、滑鼠／trackpad 並存 |

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

目前 V0.22.21 Build 0 的 PR #342／#343 均已合併；Production Deploy #509（run `37495315805`）已完成 application tests、D1 migration、Worker/static assets、secure login 與 semantic frontend asset verification。

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

歷史平板回歸曾驗證方向判斷、旋轉草稿、編輯保留、visual viewport、保持展開、換筆及切月份不誤存。Build 1 使用 Chromium 153 與合成資料做過 1194×750 橫式、834×1100 直式、390×844 手機、1440×900 桌機檢查；當時手機／桌機基準在版本文字正規化後一致。

後續 V0.22.3 Build 1～3 加入互動判定、shared picker 與重複 CSS 規則 regression。V0.22.21 又新增 identity ownership、preview-only return、兩列直式 entry、原生搜尋與桌面不受影響的 regression guards；PR #342 Validate #506、PR #343 Validate #508 及 Production Deploy #509 均成功。

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
