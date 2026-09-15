# CYInvoice Project Rules

本文件是 `apps/CYInvoice/**` 唯一的專案永久規則來源。共通規則依根 `REPOSITORY_RULES.md`，Public repo 規則依根 `REPO_POLICY.md`。其他 CYInvoice 文件只能描述需求、狀態、歷史、測試或待辦，不得建立另一套永久規則。

## 1. 產品身分與正式線

- CYInvoice 是光貿（AMEGO）電子發票 Windows 工具；發票開立、查詢、作廢與本機紀錄屬高重要性流程，可靠性與可預測性高於視覺特效。
- **C#／WinForms 自 `V2.0.0` 起為唯一正式產品線**，正式 source 直接由 `main` 維護。
- 版本唯一來源為 `apps/CYInvoice/VERSION`；不得再建立 `VERSION-CS` 或其他平行版本身分。
- `cyinvoice/csharp-remake` 已完成其一次性遷移目的，合併後停止使用；後續工作依共通規則由 `main` 建立短期工作分支與 PR。
- Go／Win32 `V1.1.0` 固定為上一個可回退的公開版本；只保留 tag、Release、commit 與下載檔，不再保留於 `main` 的現行 source，也不得覆寫其歷史資產。
- 若 repository 尚無 `cyinvoice-v1.1.0` Release，允許由 V2 合併前固定 `main` commit `4c2335e00173368540fe10a641fccffcb251f999` 一次性重建、完整驗證並建立歷史回退 Release；建立後此例外即告完成，不得再次發布或修改同版。

## 2. CYInvoice 正式 Release

- 正式 Release 只允許由 Public `CYapps/main` 的 CYInvoice C#／WinForms 正式 source 建置。
- 只有使用者於當次工作明確要求 `release` 後，才可從 `main` 明確啟動正式 Release workflow；不得把過去的發布要求延伸成後續版本的持續授權。
- `VERSION`／`BUILD` 依規則推進、PR 通過與合併，都不代表已授權正式發布；一般版本只產生 preview／engineering 測試包，不得自動建立 tag 或公開 Release，也不需要每個小版本都正式發布。
- tag 格式固定 `cyinvoice-vX.Y.Z`；Release title 使用 `CYInvoice VX.Y.Z`。
- 手動輸入／確認的版本必須與 `apps/CYInvoice/VERSION` 完全一致；已存在 tag／Release 不覆寫。
- 正式 ZIP 名稱為 `CYInvoice_VX.Y.Z.zip`；解壓後根資料夾固定 `CYInvoice`，資料夾名稱本身不含版號。
- 正式 Release 至少附 ZIP 與 SHA-256；package 根目錄保留當版 `VX.Y.Z.txt` 與 `使用說明.txt`。
- Release workflow 必須重新執行必要測試、source confidentiality scan、Windows x64 build、PE／manifest／icon、package 驗證與 hash，不得只依賴較早的 CI 成功。
- 開發測試 package 必須明確標示 preview／engineering，不得與正式 Release asset 混淆。

## 3. 憑證、設定與正式資料

- 正式 AMEGO App Key、MO店+ Excel 密碼、管理密碼、正式公司設定與發票資料不得寫死在 source、Issue、PR、Actions log 或 Release package。
- 正式 App Key 與 MO 密碼必須使用目前 Windows 安全儲存設計（例如 DPAPI）或經使用者批准的同等／更安全機制。
- 管理密碼只保存 salted password hash 或等效安全表示，不保存明文。
- AMEGO 官方文件已公開的測試公司統編／Test AppKey 可保留在 source，僅限測試環境；不得把其他看似測試的秘密自行列入白名單。
- `settings.json`、`invoices.json`、`buyer_names.json`、實際 Excel/PDF、Data、Cache、Logs 等 runtime／營運資料不得提交或放入 Public Release。

## 4. 發票安全不變量

- 「AMEGO 已成功開立」與「本機保存成功」是兩個不同狀態，不能混為一談。
- 相同 OrderID 不等於本次開立成功；必須核對發票狀態與必要欄位。
- API 結果不明、逾時或本機保存失敗時不得盲目自動重送，以免重複開票。
- 作廢後重開、防重複與測試／正式環境隔離不得因 UI 或架構重構而弱化。
- AMEGO 合法 wire-format 差異可在 decoder 邊界正規化（例如日期／時間為 JSON string 或 number），但不得因相容格式而放寬成功判定。
- 上傳狀態只有 AMEGO 明確回報 `99` 時視為完成／綠燈；`91` 為錯誤／紅燈；處理中或未確認狀態維持黃燈。未知狀態不得等同成功。
- 統編名稱查詢正常空結果／code 99 表示查無資料，可人工輸入，不得誤標成 API 系統異常；只有連線、逾時、授權、簽章、帳號或服務錯誤才標示 API 異常。

## 5. 金額與匯入核心

- 金額計算不得使用會造成不可控二進位浮點誤差的流程取代既有固定精度邏輯；含稅／未稅換算必須維持可逆與既有 7 位小數精度要求。
- MO店+ 匯入對消費者的正式開票金額，以平台官方「請依此金額開立予消費者」欄位／語意為準；公司戶依既有規則轉為未稅處理。
- 各來源匯入在送出前必須經使用者可見的逐張確認流程；不得因批次匯入而跳過必要核對。

## 6. C# / WinForms UI 架構規則

本節只適用目前 C#／WinForms 正式線，不得推廣成其他 CYApps 專案的共通 UI 政策。

### 6.1 單一路徑

- Win32 原生 control 能完成需求時，建立時直接使用最終 native style；禁止先 owner-draw 再轉 native。
- 同一 control 只能有一套正式 layout/state 路徑；禁止新增 `refine*`、`normalize*`、callback wrapper、`init()` 偷換 WndProc 等二次 patch layer。
- 需要重構時直接修正式建立／layout 路徑並刪除被取代實作，不留無期限 fallback。

### 6.2 Paint 與 state 分離

- `WM_PAINT`、`WM_CTLCOLOR*`、`WM_DRAWITEM`、`NM_CUSTOMDRAW` 等繪圖路徑只能決定外觀，不得 Enable/Disable control、改 Radio checked state、切換 buyer/tax mode、修改資料模型或發 API。
- WinForms control 建立、銷毀與 UI 更新只在 UI thread；API／檔案解析等長工作在背景執行，再以明確方式回 UI thread，並避免更新已失效視窗。
- Modal 視窗不得各自複製互不一致的 nested message loop；使用既有共通 modal 路徑。

### 6.3 ListView / Tab 穩定性

- 商品與已開立發票 ListView 必須各自使用唯一欄寬演算法，永久保留系統垂直 scrollbar 槽；不得因 scrollbar 出現而造成水平 scrollbar。
- 商品 ListView 剩餘寬度給品名；已開立清單的開立時間、發票號碼、來源、15 碼訂單編號與統編必須完整顯示，剩餘寬度給買受人。
- 未滿一頁時可用 disabled、Windows themed 原生 scrollbar 子控制項占位；資料溢出時由 ListView 原生 scrollbar 使用同一位置。不得恢復 `SIF_DISABLENOSCROLL`／動態 Header 寬度推算舊路徑。
- 空白斑馬紋列只屬 UI 顯示，不得寫進資料、被選取／雙擊或參與任何安全判斷。
- 主 Tab 維持 WinForms 原生 `TabControl`；允許 owner-draw 的範圍只限已核准的標頭外觀，選取、鍵盤、通知與 page frame 仍由原生 control 管理。

### 6.4 允許的 custom-draw 例外

目前允許：ListView 斑馬紋、發票狀態字色／作廢樣式、商品 subitem 刪除按鈕 hit-test、subitem 暫時原生 EDIT，以及主 Tab 標頭外觀。新增其他 custom-draw／subclass 架構前必須證明原生方案不足；不得因此形成第二套 control/state 系統。

## 7. UI 產品行為不變量

- 一般消費者只能含稅輸入；未稅選項 disabled。公司統編才可切換含稅／未稅。
- 設定頁空白欄位不得意外覆寫既有有效 App Key／密碼。
- 主畫面 Edit 的 Enter 不得意外觸發開立發票；只有明確流程可將 Enter 綁定確認。
- UI 重構不得改變發票成功判斷、環境隔離、防重複或資料保存安全語意。

## 8. 舊版本與測試包

- `cyinvoice/csharp-remake` 與 `cyinvoice/fix-four-group-titles` 都不是後續正式開發線；不得再合併到 `main` 或作為新版本基準。
- Go／Win32 `V1.1.0` 只作歷史回退用途；新功能、修正及 Release 一律以 `main` 的 C#／WinForms source 為準。
- 測試 ZIP／Artifact 提供給使用者前，至少應通過對應 Windows build、核心測試、啟動 smoke test、公開安全掃描與 PE 資源檢查。
