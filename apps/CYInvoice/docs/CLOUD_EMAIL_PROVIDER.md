# CYInvoice Cloud Email Provider Note

此文件只記錄非敏感的 Email Provider 工程狀態與切換原則。不得寫入 API Key、SMTP 密碼、真實收件人 Email、OTP、Cloudflare Secret 或其他機密資料。

## 目前狀態

- 已建立 Resend 帳號，登入方式為 GitHub Sign-In。
- 目前尚未持有可供 CYInvoice 驗證寄件的自有網域，因此 Resend 暫不作為正式 OTP 寄件來源。
- Resend API Key 尚不得提交至 repository；未來若啟用，只能放在 Cloudflare Worker Secret 或等價的後端 Secret Store。

## 目前 V3.0 原則

CYInvoice 的 OTP lifecycle 由 CYInvoice Cloud 自己管理；Email Provider 只負責寄送郵件。

因此 Windows Client 與 Cloud API contract 不得綁定特定寄信廠商。Email Sender 必須維持 provider-neutral，可由 reference backend 切換不同供應商，而不影響 Windows 端的 Workspace／Device／SUPER_ADMIN／OTP 流程。

## 未來切回 Resend 的條件

當日後取得自己的網域時：

1. 在 Resend 驗證該網域與必要 DNS 記錄。
2. 建立 CYInvoice 專用寄件地址，例如 `verify@<owned-domain>` 或專用子網域。
3. 將 Resend API Key 與寄件人資料設定到 Cloudflare Worker Secret／runtime configuration。
4. 將 reference backend 的 Email Provider 切換成 Resend。
5. Windows Client 與 OTP API contract 不需因此修改。

此切換只屬於後端 transport 變更，不應改變 OTP 的有效期限、錯誤次數限制、重寄冷卻、單次使用、hash-at-rest、Recovery Email 或 SUPER_ADMIN 驗證規則。
