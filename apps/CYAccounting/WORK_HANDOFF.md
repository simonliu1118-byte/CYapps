# CYAccounting — Work 接手文件

> 最後整理：2026-09-26
>
> 專案：志遠記帳系統 / CYAccounting
>
> 目前正式基準：**V1.3.0 / BUILD 0**
>
> 專案狀態：**暫時告一段落，無進行中開發工作**

## 1. 現況

- 正式版本：`V1.3.0`
- `VERSION=1.3.0`
- `BUILD=0`
- 正式 tag：`cyaccounting-v1.3.0`
- 正式 Release：<https://github.com/simonliu1118-byte/CYapps/releases/tag/cyaccounting-v1.3.0>
- Release 目標 commit：`6a3d3f72a3bd23ae8d9fd720a6d72ca9f9a6ed4a`
- 正式 Windows x64 portable package 已由 `CYAccounting Stable Release` workflow 從 `main` 重新建置、測試、驗證、掃描並發布。
- V1.3.0 視覺與主要實機回饋已由使用者接受。

本文件只作接手摘要，不取代治理規則。重新開工前，先讀最新 `main` 與正式規則鏈。

## 2. 重新開工時的固定讀取順序

1. 根 `REPOSITORY_RULES.md`
2. 根 `REPO_POLICY.md`
3. `apps/CYAccounting/PROJECT_RULES.md`
4. 最新 `main`
5. `apps/CYAccounting/VERSION`、`BUILD`、`V1.3.0.txt`
6. `README.md`、`TODO.md`
7. `tests/`、`.github/workflows/cyaccounting-build.yml`、`.github/workflows/cyaccounting-release.yml`
8. 實際 PySide6 / SQLite / Go launcher source
9. 若涉及視覺，再讀 AITeam canonical CY Desktop Visual Guide 與 ACC Icon Family

若聊天記憶、舊文件與 source 不一致，以最新使用者明確指示、正式規則與 `main` source 為準。

## 3. 命名與產品識別

- 程式名稱：**志遠記帳系統**
- 專案識別：`CYAccounting`
- 內部縮寫：`CY`
- 羅馬拼音：Wade–Giles
- 志遠：`Chihyuan` / `Chih-yuan` / `CY`，不得使用 `Zhiyuan`
- 目標環境：Windows 10/11 x64 portable
- UI：繁體中文

## 4. V1.3.0 已接受的視覺決策

- 主 UI 使用 Blue Theme；ACC 綠色只作 Icon identity，不作整體 UI 主色。
- 輸入記帳頁：輸入是主要任務，因此主要欄位與文字保持較大、較好輸入。
- 記帳資料表：高密度掃描／檢視；資料列 23px、表頭 24px。
- Basic Information、Income、Expense、Input Confirmation、Settings、Import 使用一致、低裝飾的 framed section 語言。
- 一般 ComboBox 與 Ledger 內嵌 ComboBox 使用同一家族 chevron；Ledger 保留窄版 geometry。
- 收入／支出科目管理最終使用 V1.1.0 Release 已驗證的**原生 `QTabWidget`**。不要恢復 Build 6 / Build 7 那些自訂 category-manager tab geometry / QSS。
- 子視窗正常繼承 ACC application icon。不要恢復 Build 6 / Build 7 的 Win32 title-bar no-icon workaround、`MSWindowsFixedSizeDialogHint` 或 HWND 清 icon hack。
- ACC Icon Family 維持已核准正式資產，不重新生成另一套。
- 本版接受基準為 100% / 96 DPI；125% / 150% DPI 仍 deferred。

## 5. 不得誤改的產品行為

- 日期顯示 `YYYY/MM/DD`；直接輸入 8 碼需先格式化再獨立驗證日期合法性。
- 收入／支出金額新輸入上限為 7 位數，最大 `9,999,999`；既有超額歷史資料不可因一般編輯被自動改寫或刪除。
- 歷史交易保存當時的帳戶／科目文字快照；master 改名或刪除不得回寫歷史交易文字。
- 常用摘要依帳戶＋收支＋科目統計；空白不參與；最多 10 個；次數優先、同次數最近使用優先。
- 設定頁可直接向前或向後調整鎖帳月份，屬既有刻意設計。
- 本機帳本清除必須連續兩次各自輸入完全相同的大寫 `DELETE`；取消或任一步錯誤都不得清除。
- 清除前先建立可驗證本機復原備份；既有本機／雲端備份保留；自訂 DB 位置保持指向目前位置。
- Google Drive OAuth 憑證由使用者自行匯入；client JSON、refresh token、使用者設定與帳務資料不得進 Public Git 或 Release。
- 本機 SQLite 是 source of truth；Google Drive 不作 live database。
- 正式 package 不含使用者 Data、設定、OAuth、token、log 或備份。

## 6. 主要程式結構

- GUI：PySide6
- DB：SQLite
- Windows GUI launcher：Go
- 主要 source：
  - `app/main.py`
  - `app/db.py`
  - `app/dialogs.py`
  - `app/widgets.py`
  - `app/import_dialog.py`
  - `app/gdrive.py`
  - `app/background.py`
  - `app/models.py`
  - `app/util.py`
- Tests：`tests/test_app.py`、`tests/test_safety_unittest.py`、`tests/test_gdrive_unittest.py`、Go launcher tests
- Portable build：`tools/build_portable.ps1`
- Package verify：`tools/verify_portable.ps1`
- Source archive：`tools/create_source_archive.py`

## 7. 發行流程

正常開發：branch → PR → Windows CI / Governance → 使用者實機驗收 → merge `main`。

正式 Release 只有在使用者明確要求時執行：

- `VERSION` 必須是穩定 SemVer。
- 正式 Release 必須 `BUILD=0`。
- 必須存在對應 `Vx.y.z.txt`。
- `.github/workflows/cyaccounting-release.yml` 從 `main` 重新測試並建立正式 portable package、SHA-256、source verification archive 與 GitHub Release。
- 測試 Artifact 不等於正式 Release。

## 8. 目前沒有進行中的工作

V1.3.0 發布後，使用者已決定此專案暫時告一段落。下一次重新開工時，不要延續舊 Draft PR、舊 Build workaround 或聊天中的未採用視覺嘗試；應從當時最新 `main` 重新確認現況。

目前唯一明確保留的 deferred 項目是：

- 125% / 150% DPI 視覺實機驗收與必要調整。

除此之外沒有已授權的下一階段功能開發。新的功能、UI 或資料行為變更，等使用者重新提出後再建立新的 branch / PR。
