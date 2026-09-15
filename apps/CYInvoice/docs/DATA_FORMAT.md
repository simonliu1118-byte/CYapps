# CYInvoice 本機資料格式

本文件記錄 V2 C# 正式線目前使用的本機資料欄位與安全寫入方式。三個 JSON 檔皆位於程式同層的 `Data` 資料夾；已移除的 Go 舊版 Data 不列入現行驗收或遷移承諾。

## 安全寫入

- 先在同一資料夾寫入暫存檔、同步落盤，再取代正式檔，避免程式中斷留下半份 JSON。
- 讀到損壞或包含多個根值的 JSON 時停止並回報，不用空白資料覆蓋原檔。
- Windows 取代檔案使用 `MoveFileExW` 的 replace-existing 與 write-through。
- 每次啟動會自行建立 `Data`、`Cache/InvoicePDF`；不依賴 ZIP 保存空資料夾。

## settings.json

相容欄位：

- `environment`：`test` 或 `prod`（與 V1.0.0 相容）。
- `prod_invoice`：正式公司 8 碼統編。
- `prod_app_key_enc`：以目前 Windows 使用者的 DPAPI 加密後再做 Base64 編碼。
- `mo_password_enc`：以相同方式加密；原始密碼不寫入 JSON 或原始碼。
- `password_salt`、`password_hash`：管理密碼的隨機 salt 與 PBKDF2-HMAC-SHA256 210,000 次衍生結果。舊版單次 SHA-256 雜湊仍可驗證，使用者下次設定新密碼時改存新格式。

新資料不內建 MO店+ 密碼或正式 App Key，必須由使用者在本機設定。切換正式環境以前，統編與 App Key 兩者都必須存在。

## invoices.json

- 根值固定為陣列；沒有紀錄時寫成 `[]`。
- 使用 snake_case 欄位，包括 `sent_at`、`invoice_date`、`invoice_time`、`last_checked`、`original_order_id` 與狀態欄位。
- 現有 V2 紀錄若沒有 `sent_at`，第一次載入會以既有開立日期／時間固定補入。
- 狀態回查只能更新正式開立時間與最後查詢時間，不能修改 `sent_at`。
- 讀到尚未納入目前模型的欄位時會原樣保留，避免 V2 資料往返寫入造成遺失。
- MO店+ 紀錄另保存 `api_order_id`、`carrier_type`、`carrier_id1`、`carrier_id2`、`npo_ban`；現有紀錄缺少這些選填欄位時仍可正常載入。
- `api_order_id` 保存實際送至光貿的 OrderId，用於結果不明時精確回查；測試環境依最新定案不再遮蔽原始 OrderId。已作廢訂單重新開立時依序使用 `-R2`、`-R3` 尾碼，`original_order_id` 始終保留原始平台訂單編號供本機核對與防重複。

防重複鍵為「來源＋原始平台訂單編號」。只有明確「開立失敗」或「已作廢」允許重開；「已開立」、「結果不明」、「資料變更中」及未知狀態一律鎖定。

## buyer_names.json

- 根值為 `統編 -> 人工公司名稱` 的 JSON object。
- 光貿查到名稱時不保存。
- API 連線失敗時不保存。
- 只有光貿查詢成功但名稱空白、使用者人工輸入，且該張發票確認開立成功後才保存。

測試資料只使用虛構值；正式公司資料、發票歷史、App Key 與平台密碼不得進入 Git。
