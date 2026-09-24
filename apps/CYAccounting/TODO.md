# CYAccounting TODO

## 已處理 — 清除功能使用雙重 DELETE 確認

V1.0.27 Build 1 曾移除原固定密碼及清除功能；依使用者 Build 2 的新決定，設定頁原位置恢復完整本機帳本重置入口，採用兩次輸入大寫 `DELETE` 防誤觸。不儲存或比對固定密碼；執行前先建立復原備份，本機與雲端既有備份保留。正式 Windows 實機驗收仍待完成。

此變更已隨 PR #122 進入 `main`，目前原始碼為 V1.0.27 Build 2，尚無正式 Release。正式發布前仍須完成 Windows 實機啟動、原有帳本讀取，以及使用帳本副本驗收雙次確認、備份／還原、自訂資料庫位置與重置後重新開啟。

## 下一輪：Icon 與 UI

使用者將移至 Chat 討論 Icon 與 UI，具體設計尚待提供。先確認 AITeam `main` 的 CY Desktop Visual Guide、Icon Family 和現有 PySide6 畫面，再依使用者截圖與回饋決定修改範圍。淺色標題列／清單的深色 Windows 實機顯示、解壓一次後啟動與 Icon 實機顯示仍須驗收。其他 UI、Google Drive 及匯入待辦見 `WORK_HANDOFF.md`。
