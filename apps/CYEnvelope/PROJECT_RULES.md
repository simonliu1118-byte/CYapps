# CYEnvelope Project Rules

本文件只記錄 `apps/CYEnvelope/**` 的專案補充與例外。共通規則依根 `REPOSITORY_RULES.md`，Public repo 規則依根 `REPO_POLICY.md`。

## 1. 正式基準

- 專案名稱固定為 `CYEnvelope`，不得再使用舊誤名 `CYEnvelop`。
- 目前正式版本基準：`0.1.1`；唯一正式版本來源為本目錄 `VERSION`。
- 專案是 Windows 信封列印工具，正式發行以 Windows x64 portable package 為原則。

## 2. 資料與列印安全

- 聯絡人、地址、電話、列印紀錄、格式設定等實際使用者資料不得提交至 Public Git 或放進正式 Release 包。
- `Data/`、`Cache/`、`Logs/` 可保留 `.gitkeep` 作目錄骨架；實際內容必須被忽略。
- 列印版面、15K 尺寸、直／橫式、直排文字、方框文字與郵遞區號判斷屬列印核心；修改後需以實際 Windows 列印／預覽尺寸驗證，不得只靠畫面看起來合理。

## 3. UI / 使用行為

- 收件者或地址輸入時不得任意重設游標位置。
- 地址欄離開焦點後應重新判斷郵遞區號；無法判斷時不得偽造郵遞區號。
- 方框文字的黑色方框屬真正列印內容，不是只存在於畫面預覽的套版裝飾。
- 預覽與實際列印應共用同一套版面參數來源，避免形成兩套互相漂移的 layout。

## 4. 發行驗證

- 正式 Release 前除共通檢查外，至少驗證 Windows x64 啟動、icon/manifest、信封預覽、列印輸出、直排文字、地址／電話格式與資料保存流程。
- 不因本專案採用 Go／Win32 就把相同 UI 技術規則強加到其他 CYApps 專案。
