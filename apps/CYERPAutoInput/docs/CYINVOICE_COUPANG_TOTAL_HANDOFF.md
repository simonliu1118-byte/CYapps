# 交接：CYInvoice 酷澎多品項訂單發票金額重複相加

> 狀態（2026-11-10）：CYInvoice 已在分支 `cyinvoice/fix-import-amounts-20261109`（V3.1.48）修正 `CoupangImporter`；CYERPAutoInput V1.2.6 對回報 3.1.48 以上的 CYInvoice 照常自動開立。**尚待確認**：該 V3 線原始碼找不到 `--autoinput-query／issue／pdf` 介面（介面目前只在 V2.6.21 的 `cyinvoice/feat-autoinput-interface`）；請 CYInvoice 確認本程式呼叫的版本同時具備介面與這項修正，確認後本檔彙整回 README／WORK_HANDOFF 並刪除。

> 由 CYERPAutoInput session 撰寫（2026-11-09），請使用者轉交 CYInvoice session。CYInvoice 修正並出工程測試包後，CYERPAutoInput 會拿掉暫時的擋開立，並把本檔彙整回 README／WORK_HANDOFF／CHANGELOG 後刪除。

## 現象（使用者 2026-11-09）

酷澎 DeliveryList 一張訂單有兩個商品、各 120 元，買家實付 240 元；用 CYInvoice 的酷澎入口開立，發票開成 480 元。

## 原因

酷澎 Wing「配送指示」下載的 DeliveryList（.xlsx），多品項訂單是一個商品一列，而「應開立予買家之發票金額」在**每一列都填整張訂單的金額**（範例，金額為示意）：

| 訂單編號 | 公司商品代碼 | 銷售價格 | 數量 | 運費 | 應開立予買家之發票金額 |
|---|---|---|---|---|---|
| 900…01 | X00010 | 120 | 1 | 0 | 240 |
| 900…01 | X00011 | 120 | 1 | 0 | 240 |

`src/CYInvoice.Core/CoupangImporter.cs` 把每一列的「應開立予買家之發票金額」當成該品項金額，並 `TotalAmount += amount` 逐列相加，所以 240 變 480（三個品項就變三倍）。單一品項訂單不受影響。

## 建議修正（請 CYInvoice 判斷）

- 同一訂單編號有多列、且各列「應開立予買家之發票金額」都相同時，視為整張訂單金額，只取一次。
- 品項明細：各品項金額用「銷售價格 × 數量」，最後一項補差額，使合計等於整張金額（有折扣時差額會落在最後一項；或依 CYInvoice 既有規則分攤／另列折讓）。
- 若各列金額不同（未見過的格式），維持逐列相加，或停下請人工確認。
- 已誤開的 480 元發票請依 CYInvoice 的作廢／折讓流程人工處理。

## CYERPAutoInput 這邊已做的（V1.2.5 Build 0）

- 解析 DeliveryList 時，多品項且每列金額相同就當成整張訂單金額只算一次（ERP 代收貨款、折價券正確）。
- 這類訂單**暫時不交給 CYInvoice 自動開立**（預覽備註說明，請在 CYInvoice 手動開正確金額後把發票填入預覽），避免再開成兩倍。CYInvoice 修好後通知 CYERPAutoInput 拿掉這個限制。

## 契約不變

`--autoinput-query／issue／pdf` 的 request／result 格式不變；`totalAmount` 應等於整張訂單的買家發票金額（上例 240）。
