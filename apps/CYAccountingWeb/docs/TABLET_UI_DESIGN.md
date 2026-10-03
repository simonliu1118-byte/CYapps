# CYAccountingWeb 平板介面設計與驗收

更新：2026/10/03（日本時間）。本文件記錄這次設計及實作，不是永久規則。正式功能基準 V0.22.2，PR #292 已合併、部署 #377 成功；目前進行 V0.22.2 Build 1 排版返修，未建立公開 Release。版本／CI 證據見 [WORK_HANDOFF.md](../WORK_HANDOFF.md)。

設計來源為 `cyaccountingweb/docs-tablet-ui-plan` 的 `b71e4108b7b3bd5e165dadeb526db4116c56be18`，原檔 `TABLET_UI_PLAN_2026-10-03.md`。規劃時的自製 picker 建議已依使用者「原生／既有元件優先」指示調整；目前實作用原生帳戶、日期與月份，不複製另一套 picker 或 writer。

## 已確認方向與第一版配置

| 項目 | 橫向 | 直向 |
| --- | --- | --- |
| 結構 | 左記帳約三分之一、右看帳較大 | 看帳主區，底部記帳 rail |
| 表單 | 常駐左欄、欄位分列 | 預設收合只顯示把手；上拉展開、下拉收合，也可點按；展開後可勾原生「保持展開」checkbox |
| 帳本 | 保留表格、完整金額與餘額，獨立捲動 | 保留表格，依可用寬度捲動，底部表單不覆蓋最後一筆 |
| 編輯 | 點編輯後帶入同一左側表單 | 點編輯後展開底部同一表單 |
| 外接輸入 | 鍵盤、滑鼠與觸控並存 | 鍵盤、滑鼠與觸控並存 |

程式目前判斷：手機 `<768px`；平板為 `768–1023px`，或 `1024–1366px` 且 `any-pointer: coarse`；其他寬度沿用桌機。這是第一版可用空間／輸入能力判斷，不是已通過所有裝置的永久門檻。大平板、外接指標及分割視窗需實機驗證，必要時依驗收修正。

方向優先讀 `screen.orientation`／瀏覽器 orientation；`visualViewport` 只調整可視高度。鍵盤出現不能被當成帳務變更或自動送出；缺少 orientation API 的瀏覽器 fallback 仍需實機確認。

## 元件與共用操作

| 能力 | 第一版實作 |
| --- | --- |
| 帳戶／日期／月份 | 原生 select/date/month；仍是既有欄位、資料值及 change listener |
| 收入／支出 | 共用既有 slider、純色畫布、entry kind 與鎖帳狀態 |
| 交易編輯 | 沿用手機 entry editor、`persistTransactionUpdate`、摘要／金額驗證、鎖帳、optimistic 與失敗回復 |
| 編輯返回 | 平板還原帳本獨立捲動位置；同筆再次點擊保留修改，換筆先保留原新增草稿 |
| 取消／換月份 | 共用取消還原草稿；切看帳月份取消未儲存編輯，旋轉則保留編輯與月份／搜尋 |
| 帳戶排序 | 共用手機 pointer handle 及 `applySettingsAccountOrder`，失敗 rollback 不覆蓋並行改名 |
| 科目排序 | 共用階層 renderer、上下分類按鈕、touch/pen handle、跨大分類 reorder／edge scroll |
| 交易入口 | 保留可點的編輯／刪除按鈕，沒有新增平板 swipe engine；刪除使用既有確認及 server gate |
| 設定／彈窗 | 共用既有設定、archive 與 audit 元件／writer，只調尺寸及觸控命中區 |
| Excel | 共用 endpoint／期初 snapshot；支援時原生分享，否則下載；交付方式需真機驗證 |

所有裝置共用 CYID、角色、API、D1、計算與高風險判斷。USER 不顯示 entry rail；ADMIN／SUPER_ADMIN 沿用 server-side 能力。人工期初、刪除、鎖帳、移轉、復原仍按既有確認／成功後回饋，這次沒有修改正式開帳基準。

## 已有證據與實機待辦

本機 23 組回歸、head `8945abf` 的 CI #373／Governance #961 成功。`tests/tablet-ui.mjs` 執行真實 presentation／entry owner 函式，驗證分類、方向、草稿／編輯保留、visual height、保持展開、換筆及切月份不誤存；platform-parity 驗證共用 writer 與 rollback。這些測試沒有證明實際觸控命中或瀏覽器排版已完成驗收。

| 待驗收 | 通過條件 |
| --- | --- |
| 隔離瀏覽器預覽 | 雙方向、欄寬、原生欄位、表格、設定及彈窗皆可達，沒有被遮住的操作 |
| 真實 iPad／Android | 登入、Session、登出、USER 唯讀及讀帳正常 |
| 旋轉／分割視窗 | 新增草稿、目前年月／搜尋／編輯不遺失，不重複送出 |
| 螢幕鍵盤 | 焦點、儲存／取消可達，關閉後版面恢復；不誤改方向 |
| 觸控 | 帳戶／科目排序、列表捲動、原生選擇器及交易操作互不攔截 |
| 延遲／失敗 | 寫入失敗正確回復，舊查詢不覆蓋已儲存值，重複操作被防護 |
| 外接鍵盤／滑鼠 | Enter／Tab／取消與原有操作語意一致 |
| 裝置回歸 | 手機兩頁流程與桌機鍵盤／左右配置保持可用 |
| Excel | 真實瀏覽器分享／下載可取得正確 XLSX |

目前本機 Chromium 下載失敗，沒有 browser layout 截圖證據；保持 Draft，先做隔離預覽及真機驗收，再確認合併／部署。公開 Release 仍為 V0.22.0。

## Build 1 照片返修

平板樣式限定於既有 tablet owner：限制原生日期的 intrinsic width、擴大 slider／工具觸控尺寸、整理月份／期初／鎖帳與搜尋／Excel 列、統計順序改為期初／收入／支出／期末／淨利損、帳本欄寬保留摘要空間。常用項目搬移同一 DOM 至對應欄位旁，離開平板即回到原容器，所有事件／資料來源不變。直式把手上拉展開、下拉收合、點按切換，取消拖曳不變更狀態；不新增平板 writer。

使用 Chromium 153 與合成資料驗證橫式 1194×750、直式 834×1100、手機 390×844、電腦 1440×900；手機／電腦基準與返修截圖在版本文字正規化後相同。真實 Safari、鍵盤及分享仍需裝置驗收。
