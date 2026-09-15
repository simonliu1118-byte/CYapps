# CYInvoice V2 遷移紀錄

- Go／Win32 V1.1.0 是上一個正式回退基線。
- `cyinvoice/csharp-remake` 曾用 `V1.1.0-cs.N` 進行獨立 C#／WinForms parity、Windows CI 與實機 UI 驗收。
- 使用者於 2026/09/15 完成驗收並批准 Major 升級；C#／WinForms 自 V2.0.0 起取代 Go，成為 `main` 唯一正式產品線。
- 原 `VERSION-CS`、永久 C# 分支與 Go 現行 source 已停止使用；Go V1.1.0 由歷史 tag／Release／commit 保存。
- V2.0.0 保留原有發票成功判定、防重、結果不明禁止重送、測試／正式環境隔離、DPAPI 與固定 7 位小數安全語意。
