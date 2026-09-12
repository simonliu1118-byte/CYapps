# CYInvoice 專案開發規範

本檔案的規範 **只適用於 `apps/CYInvoice/**`**。

不得把 CYInvoice 的「原生 Win32 優先」原則套用到 CYAccounting、CYEnvelope、下載器或 CYapps 其他專案。CYInvoice 採取此策略的原因，是電子發票開立、查詢、作廢等流程需要把穩定性、可預測性與可維護性放在 UI 特效之前。

完整規範：`docs/DEVELOPMENT_RULES.md`

## 修改 CYInvoice 前必須遵守

- Win32 原生 control 能完成的功能，建立時就直接使用最終原生 style；禁止先 owner-draw 再轉回 native。
- 同一個 control 只能有一套正式 layout/state 路徑；禁止新增 `refine*`、`normalize*`、callback wrapper 等二次修補層。
- 不得用 `init()` 偷換 WndProc/callback，再於 `WM_CREATE` 後重新搬動或重畫控制項。
- `WM_PAINT`、`WM_CTLCOLOR*`、`WM_DRAWITEM`、`NM_CUSTOMDRAW` 等 paint 路徑只負責外觀，不得 Enable/Disable、改 Radio state、切換含稅/未稅或修改 business state。
- 一般消費者只能以含稅模式開立：含稅選項保持可用且選中；未稅選項 disabled。公司統編才可切換含稅/未稅。
- 商品 ListView 不得出現水平 scrollbar；resize 時由唯一欄寬函式計算，永久保留系統垂直 scrollbar 槽，剩餘寬度全部給品名。
- 已開立發票 ListView 由唯一欄寬函式計算；開立時間、發票號碼、來源（至少完整顯示 `MO店+`／`好賣+`／`iOPEN`）、15 碼訂單編號與統編（含 `0000000000`）必須保有固定完整寬度，永久保留系統垂直 scrollbar 槽，剩餘寬度全部給買受人，不得在「上傳」右側留下假欄位。
- ListView 資料未滿時可用 disabled 原生 `SCROLLBAR` 子控制項占住固定槽；資料溢出時原地換成 ListView 自己的原生 scrollbar。禁止再用 `SIF_DISABLENOSCROLL` 推算欄寬，也不得因捲軸出現而產生水平 scrollbar。
- 自繪只保留 Win32 無乾淨原生方案或本專案明確指定外觀的部分：ListView 斑馬紋、狀態色/作廢刪除線、ListView subitem 刪除按鈕、subitem 暫時原生 EDIT，以及 **SysTabControl32 的頁籤標頭外觀**。Tab 例外只可畫標頭形狀；選取、鍵盤、通知與 page frame 仍必須由原生 `SysTabControl32` 管理。
- UI thread 只處理 HWND/UI；API 與檔案解析放 worker goroutine，完成後 PostMessage 回 UI thread。
- 不得因 UI 重構弱化發票安全規則：結果不明禁止重送、OrderID 不能單獨視為開立成功、正式/測試環境防重需隔離、光貿成功與本機保存失敗需分離。
- AMEGO API 的合法 wire-format 差異（例如日期/時間為 JSON string 或 number）應在 API decoder 邊界做相容正規化；不得為了相容格式而放寬開票成功與查詢核對條件。
- 統編查詢的 code 99／正常空結果屬於查無資料，不得標成 API 異常；連線、逾時、授權、簽章、帳號或服務錯誤才可改變 API 健康狀態。
- 被新實作取代的舊路徑要刪除，不得保留「暫時 fallback」造成未來疊床架屋。
- 提供測試 ZIP 前必須讓 CYInvoice Windows CI 全綠。
