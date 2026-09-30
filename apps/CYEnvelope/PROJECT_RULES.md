# CYEnvelope Project Rules

本文件只記錄 `apps/CYEnvelope/**` 的專案補充與例外。共通規則依根 `REPOSITORY_RULES.md`，Public repo 規則依根 `REPO_POLICY.md`。

## 1. 正式基準

- 專案名稱固定為 `CYEnvelope`，不得再使用舊誤名 `CYEnvelop`。
- 唯一正式實作為 C#／WPF（.NET 10）；Go／Win32 版（V0.1.x）已依使用者決定停止開發，不再維護、建置或作為行為基準。
- 版本身分來源為本目錄 `VERSION` 與 `BUILD`；目前尚無正式 Release。
- 專案是 Windows 信封列印工具，正式發行以 Windows x64 portable package 為原則：解壓縮一次得到 `CYEnvelope` 資料夾，根目錄為啟動 EXE、`VERSION`、`BUILD`、`FONT_LICENSES.txt` 與 `Runtime`，不需另裝 .NET。
- 例外：本專案 CI 測試 Artifact 與畫面證據保留 3 天（共通規則預設 14 天）；需要更長保存時由該次工作另行決定。

## 2. 資料與列印安全

- 聯絡人、地址、電話、列印紀錄、格式設定等實際使用者資料不得提交至 Public Git 或放進正式 Release 包。
- `Data/`、`Cache/`、`Logs/` 可保留 `.gitkeep` 作目錄骨架；實際內容必須被忽略。
- 列印版面、15K 尺寸、直／橫式、直排文字、方框文字與郵遞區號判斷屬列印核心；修改後需以實際 Windows 列印／預覽尺寸驗證，不得只靠畫面看起來合理。

## 3. UI / 使用行為

- 收件者或地址輸入時不得任意重設游標位置。
- 地址欄離開焦點後應重新判斷郵遞區號；無法判斷時不得偽造郵遞區號。
- 方框文字的黑色方框屬真正列印內容，不是只存在於畫面預覽的套版裝飾。
- 預覽與實際列印應共用同一套版面參數來源，避免形成兩套互相漂移的 layout。

## 3.1 內建字體

- 信封字體由「設定」單一選擇，套用到全部信封文字，預覽與列印一致。可選字體：Windows 內建的標楷體、新細明體（不隨程式散布，僅供選用）與程式內建的三款字體。
- 內建字體僅限授權允許隨軟體散布者，目前為思源黑體 Noto Sans TC、思源宋體 Noto Serif TC 與霞鶩文楷 TC，皆為 SIL Open Font License 1.1。不內建教育部標準楷書（CC BY-ND 3.0，內嵌散布屬需另行申請的用途）。
- 字體檔**不進 Git**：`tools/fonts.json` 記錄固定提交的下載網址、大小、SHA-256、授權與版權；`tools/fetch-fonts.ps1` 在建置前下載並驗證，不符即失敗。字體嵌入程式，可攜資料夾根目錄附授權全文 `FONT_LICENSES.txt`。
- 新增、更換內建字體，或更新其固定提交／雜湊，屬本節變更，須經使用者確認來源與授權，並在 CI 的畫面證據中目視確認字體外觀（共通規則 §5.1）。

## 4. 發行驗證

- 正式 Release 前除共通檢查外，至少驗證 Windows x64 啟動、icon/manifest、信封預覽、列印輸出、直排文字、地址／電話格式與資料保存流程。
- 不因本專案採用 C#／WPF 就把相同 UI 技術規則強加到其他 CYApps 專案。
