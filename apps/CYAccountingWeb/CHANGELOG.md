## V0.25.13 — 2026/10/29

- 將每月期初結果保存為原 D1 共用快照。相同月份的後續檢視、另一裝置及 Excel 只需一次索引查詢；當月一般記帳保留當月期初，不重新掃描歷史交易。
- 正式 migration `0013_opening_balance_snapshots.sql` 新增兩個衍生表與失效 triggers；前期交易的新增／更改／刪除、人工基準與帳戶歷史變更，和快照失效在同一資料庫交易內完成。當月封存／孤立帳戶的顯示名單亦正確更新。
- 發布快照前核對資料版本，避免另一裝置修改前期後寫回舊計算；鎖帳狀態即時讀取。快照不納入可攜帳務備份，schema 6～12 的舊備份可還原至 schema13 並重建快照。
- 合併 V0.25.12 的索引與並行請求修正一併驗證及部署；正式用量、備份／回復與部署結果保存於私人維運證據，不寫入公開來源。
- 部署驗收保留全部資產內容檢查，同一 URL 只下載一次；正式資源識別值先遮蔽再交給部署工具，避免出現在公開 Actions 日誌。

## V0.25.12 — 2026/10/29

- 降低期初餘額查詢成本：以索引逐個取得歷史帳戶名稱，從各帳戶最近的先前手動基準計算淨額；保留零元基準、本月手動值、封存與已移除帳戶，以及帳本／Excel 共用的財務語意。
- 新增正式 migration `0012_transaction_read_indexes.sql`，schema 11→12；三個 covering indexes 支援期初查詢和常用摘要的兩種排序。未重寫交易或稽核資料，每次交易寫入須同步維護三個新增索引。
- 同一瀏覽器的相同期初／摘要並行讀取共用請求；請求完成即清除，寫入或帳本切換後不接受舊讀取結果，重讀最多一次。清空摘要條件也會取消舊結果呈現。
- 以合成交易檢查金額一致性、索引計畫、摘要排序、並行讀取與失效處理。正式 migration、部署及同使用量下的 D1 計費改善仍待受控驗收，不以本機結果宣告已上線。

## V0.25.11 Build 2 — 2026/10/29

- 依使用者決定暫停 CYACCweb 深色模式，移除主頁「系統／淺色／深色」切換、登入頁外觀按鈕，以及獨立主題切換／Cookie 執行程式；固定淺色介面，避免讀取 CYWEB 共用外觀偏好。
- 清理 `cyacc-appearance.css` 中深色變數、選擇器與已廢止的主題元件 CSS；保留 CYWEB 風格的單一帳號元件、淺色 tokens 與原登入／帳務 UI，未以新 CSS 修補層覆蓋。
- 深色功能納入 TODO，未來須明確另行授權；未修改 CYID 登入／登出、帳務權限、D1 或 Worker API。

## V0.25.11 Build 1 — 2026/10/29

- 依 CY Web \`src/app.css\`、\`ThemeToggle\`、\`AccountMenu\` 的正式尺寸、狀態與色票重整 CYACC 主題與右上帳號元件。
- \`cyacc-appearance.css\` 為唯一外觀 token／帳號／主題控制主責；移除舊 \`cy-account-cluster\` 樣式與多次深色覆寫，深色以同一組 CY Web token 驅動表單、帳本、統計、設定和對話框。
- CYACC 右上選單顯示頭像、名稱及角色，僅含登出；保留既有 CYID Session／登出權限邏輯，不增加「我的帳號」、密碼或 Email 管理。
- 登入頁的外觀按鈕使用 CY Web ThemeToggle 循環（系統／淺色／深色），保留首次使用引導、ACC ICON、原登入及密碼復原流程。
- 版本為 V0.25.11 Build 1；僅來源碼／回歸驗證，未合併或部署，實機視覺驗收另行確認。

## V0.25.11 — 2026/10/29

- 參照 CY Web AuthGate、ThemeToggle 及 AccountMenu 的外觀，調整 CYACC 登入頁、右上帳號資訊與系統／淺色／深色切換。
- 保留首次使用 CY 系統的 CY Web Email 驗證入口與現有登入／密碼復原 API；CYACC 帳號選單只提供登出，不提供我的帳號或更改密碼、Email。
- 採用 AITeam 已核准的 CYAccounting ACC SVG 圖示（來源 `shared/cy-visual/icon-family/apps/accounting/ACC.svg`，canonical blob `aa858ae0988bc70726a10d1c258f491ceacd2e3f`），作為登入卡片圖標及分頁 favicon，分頁標題統一為「志遠記帳系統 CYACC Web」。
- 未修改 CYID authority、業務與帳務資料；正式驗收與發布待使用者授權。

## V0.25.10 Build 2 — 2026/10/29

- 桌機改為工作區獨立捲動：上方主導覽、左側輸入與確認區固定，中央交易表格及右側統計各自垂直捲動；中央月份摘要和搜尋列留在卡片頂部。
- 移除同一區域原來的多層 sticky 頁面捲動方案；保留卡片圓角，捲軸採細版，桌機帳本取消水平捲軸。手機、平板及窄版保留原行為。
- 不改帳務、權限或 API；正式畫面操作仍待合併部署後目視驗收。

## V0.25.10 Build 1 — 2026/10/29

- 修正桌機固定表頭相互覆蓋：在既有 ledger-tools owner 內將月份摘要與搜尋列整理成單一固定容器。
- 恢復中央帳本卡片上方圓角，左側輸入卡禁止橫向溢位及水平捲軸；原交易、帳務計算及裝置權限不變。
- 原頁面仍垂直捲動，正式畫面、尺寸與縮放驗收待 PR 通過後確認；未合併部署。

## V0.25.10 — 2026/10/29

- 桌機寬螢幕記帳時保留頁面原垂直捲動，左側輸入卡固定可見；中央月份／摘要、搜尋操作及欄位表頭固定於視窗上方。
- 狹窄桌機與平板、手機維持既有捲動方式；不更動記帳 API、金額計算及資料。
- Source 待 PR 驗證、合併及另行部署，尚未宣稱正式視覺驗收。

## V0.25.9 — 2026/10/29

- 修正桌機期初餘額視窗錯誤縮成 300px，統一對話框與帳戶／調整紀錄雙欄寬度，避免帳戶名稱、金額、稽核記錄逐字換行；不修改帳務資料、API、權限或手機／平板操作。
- 此版本為原始碼修正，待 PR 驗證、合併及另行授權部署。

# CYAccountingWeb 版本里程碑

更新：2026/10/15。此處記錄歷史與開發狀態；目前交接見 WORK_HANDOFF.md，永久規則不放在版本紀錄。

2026/10/13 維護：整理現況／驗收／未合併工作與 GitHub 產物，產品維持 V0.23.1 Build 2；清理執行結果見 WORK_HANDOFF。沒有建立新產品版本或 Stable Release。

| 版本 | 狀態與變更 |
| --- | --- |
| V0.25.5 Build 0 | 備份回讀及隔離還原驗證改每日原排程自動執行；R2／GCS 依 Phase D 各取最新應有副本，損壞／失敗／過期建立事件，CYID 通知目前超管並於登入顯示警告。移除手動驗證 UI；底部還原與緊急入口左右並排。統計移轉科目緊密雙欄；刪除手機平板預覽全鏈及舊 Session 表，D1 schema 11，舊 schema 6～10 備份仍可還原。 |
| V0.25.4 Build 0 | 首次登入共用說明、安全 HTTPS CY Web 入口與只讀 metadata；不轉交員工編號、密碼、Session 或 ticket。FIRST_LOGIN_REQUIRED 明確引導，真實 5xx 保留服務錯誤；部署強制既有 canonical portal authority／Workspace／core App／health gate。依最新主線整合 PR #435，保留 V0.25.3 共用即時記帳及 D1 schema 10。 |
| V0.25.3 Build 0 | 新增記帳共用 optimistic lifecycle：立即顯示待存列及回到摘要，多筆交錯結果各自確認、失敗僅撤回該筆並保留較新草稿。POST 同次回傳實際 row，移除成功後重讀整月；換月／GET 競態沿原 revision guard，合法重複交易不合併。最近十筆確認依 request ID 更新，待存列不允許編輯／刪除。手機／平板版面與 server 權限／鎖帳語意保留，D1 schema 10 不變。 |
| V0.25.2 Build 1 | 收斂首次登入入口前置檢查的部署範圍：入口尚未啟用時只作唯讀盤點並回報無識別值的差異；首次登入版本以 --require-account-portal 強制同 authority／Workspace／health gate，不通過不能部署。既有 CYID runtime guard 始終強制，不改登入 authority 或權限。PR #455／main run 38675354003 成功、正式版本已回讀；portal 預檢指出 AUTHORITY 不一致，首次登入入口仍不啟用。 |
| V0.25.2 Build 0 | 舊 schema 6／7 portable 備份明確 normalization：依 canonical migration 補帳戶色碼及空匯入稽核結構，保留全部既有財務／稽核欄列與原始備份；記憶體驗證與 D1 writer 共用同一 normalizer，支援 6～10。補真實 workerd／SQLite 及 D1 roundtrip 測試。部署 resolver 唯讀核對 CY Web 實際 domain、CYID authority／Workspace／core consumer health，沿用 CYID canonical guard，為首次登入引導提供正式前置證據；未在此版開放新登入流程。桌機再精簡重複保留政策／表格說明。 |
| V0.25.1 Build 0 | 桌機備份立即備份／重新整理同列、卡片文字垂直置中，GCS 未排程欄留白；可還原性檢查改清楚名稱與說明。還原表單由底部按鈕開啟唯一獨立 dialog，關閉取消預覽／確認、忽略晚到回應，送出期間鎖住表單避免重複；緊急復原入口改圖示卡片。設定一般置頂、資料管理倒數第二、月份鎖帳直接顯示目前已鎖年月；共用 settings lifecycle 同步原 picker 並移除舊 header timer。手機／平板原名稱／順序及資訊頁保留，資料與 API 不變。 |
| V0.25.0 Build 1 | 正式 GCS 演練發現復原頁 no-referrer 使 native POST 送出空 Origin，原同來源 guard 正確拒絕；政策改為 same-origin，保留 server Origin 檢查及 CSP，補回歸。修正後正式 native preview／commit 與 GCS → 獨立 D1 全欄列／政策／稽核還原及對帳通過，主站原帳本維持、目標禁止覆寫；未執行 production promotion。 |
| V0.25.0 Build 0 | 新增 SUPER_ADMIN 獨立 D1 受控復原：真實備份驗證、10 分鐘且綁定操作者的預覽、勾選及文字確認、原子寫入空白工作區、完整帳務／政策／稽核回讀與資料庫檢查。schema 10 僅新增 append-only 復原 metadata；格式 2 manifest 新增維運 context，GCS 可在原 D1／R2 不可用時獨立重建。沿用原 Backup API／CYID 權限，新增不依賴原帳本的 server-rendered 復原入口。部署以現有受保護權限準備獨立 D1；不自動切換或覆寫正式帳本，目標保持 hold。加入帳本 epoch 防止未來切換後舊頁誤寫；備份確認收斂到既有 in-page dialog，移除重複 capture 攔截。手機／平板既有版面維持。 |
| V0.24.1 Build 0 | 備份／復原頁於 Phase D 生效後移除 Phase C 驗收進度及已完成的啟用按鈕，「立即備份」取代測試用文字。日期 Ctrl＋↑↓ 收斂由共用 entry form 處理，桌機日期、摘要、金額皆調整同一日期且保留焦點，移除 picker 的重複快捷 handler；觸控原操作維持。單／多月統計、損益表、看帳與 Excel 依正負顯示淨利／淨損，零元顯示損益；財務計算不變。正式還原／DR 接續工作仍列 TODO。 |
| V0.24.0 Build 0 | 新增完整 Phase D 分層備份及隔離還原能力，升 Minor。R2 每日 30 天、GCS 台灣週三／週日 182 天；手動／匯入前維持雙副本，未排程不記失敗。SUPER_ADMIN server gate 檢查 14 次排程及同一份雙副本的有效隔離還原，append-only policy／驗證／操作者 audit，切換當下不清舊備份。以 canonical migrations／exporter 在全新記憶體 SQLite 完整匯入、逐欄逐列比對及 integrity／FK check，來源 schema 8／9，正式帳本 writer 未開放；新增真實 workerd WASM 測試。schema 9 僅維運 metadata；backup app version 改由 VERSION 產生並 CI 檢查，outer／inner format 2 不變。手機／平板不新增控件、不改版面。 |
| V0.23.4 Build 0 | 桌機收支／問號同列、確認狀態靠右並移除十筆說明；成功存檔直接回摘要，移除金額焦點競爭與 timer。桌機清單編輯帶回左側共用表單，清空變取消，未儲存退出回復新增草稿；原筆 PUT／optimistic rollback 共用既有 writer，移除被取代的 inline editor。日期改文字＋日曆按鈕，四碼／八碼、Ctrl＋↑↓、Enter 與無效日期驗證；移除日期 observer／synthetic keyboard bridge，RWD teardown abort listeners。桌機單月統計移除括號路徑，右下新增回到頂端圓形按鈕；移轉排除、手機／平板版面及 schema 不變。 |
| V0.23.3 Build 1 | 同一桌機確認清單驗收：移除 inherited grid gap、修正狀態符號 inline baseline 額外列高，十筆維持單行且更緊湊；存檔提示置中、日期倒序與帳戶分組已在正式瀏覽器確認，餘額不變。 |
| V0.23.3 | 桌機存檔提示移至輸入確認標題列中央；確認改單行無卡片、最新十筆淘汰，保留完整內容與失敗原因。日期表頭切換新到舊／舊到新，分組顯示「帳戶 [分組]」；只改顯示順序，餘額仍按完整月份時間順序計算。既有 owner 使用明確 lifecycle，移除確認模組內部兩個 observer 與重複字級規則；手機／平板原呈現保留。 |
| V0.23.2 | 獨立桌機日期 Bug 修復：HTML data attribute 全部改為小寫 kebab-case，正確對應 dataset；選取日／月／年不再清空真正日期欄位，儲存沿用所選日期，前後月份與跨年恢復。更新 Adaptive asset 語意 revision 及正式驗證；行為回歸涵蓋 required date、submit payload、閏日、今天、外部變更與單一初始化。手機／平板原生日期及帳務 writer 不變。 |
| V0.23.1 Build 2 | 帳號載入隱藏規則移回實際載入的 Adaptive stylesheet，避免未載入的 auth.css 使規則失效；損益表縮減標題、控制列與表列留白，維持可讀字級並增加單頁容量。 |
| V0.23.1 Build 1 | 線上報表驗收續修：單月損益視窗確實隱藏結束月份，修正 flex label 蓋過 hidden 的樣式衝突；月份標籤不斷行，報表金額與科目統一行高並微縮列高，增加一頁可讀容量。分頁仍沿用固定容量，完整科目與整月合計不變。 |
| V0.23.1 | 統計介面調整：單月及多月共用圖表／統計頁籤，桌機統計共用頁面捲動，損益按鈕優先且單月視窗不再切換多月；圓餅圖直接標示名稱／金額／百分比，小區段引線排字，手機平板顯示名稱／百分比及完整圖例。損益表改左收入右支出、底部合計及淨利損、最下方另列移轉；超出可讀高度時分頁保留全部科目，不使用內部捲軸。統計合計、圓餅及分類科目亦排除指定移轉並顯示說明，看帳金額維持。Auth 為唯一帳號 renderer，移除 Adaptive identity 重畫與 role observer，資料完成前隱藏帳戶列。 |
| V0.23.0 Build 1 | 統計初版視覺驗收修正：窄桌機左側記帳區跨兩列，統計直接接在看帳下方，避免被左側高度推遠；窄桌機兩張圓餅圖並排，工具按鈕維持緊湊寬度，寬桌機統計工具上緣對齊既有工具列。統計視窗補齊內容留白，損益表解除帳目表格的最小寬度，避免橫向溢出；手機及平板既有記帳／看帳布局不變。 |
| V0.23.0 Build 0 | 新增完整統計功能，升 Minor：桌機寬視窗採記帳／看帳／單月統計三欄，窄視窗統計置於看帳下方；手機新增底部統計頁籤，平板直／橫式新增入口，共用多月圖表與單月損益表。後端以完整期間 snapshot 彙總分類／科目，保留未分類與封存帳戶交易；損益設定明確指定移轉科目，收支合計不變，損益另列移轉，設定沿用現行備份。桌機超級管理員括號改半形。 |
| V0.22.33 Build 1 | 同一桌機帳號列返修：移除新 ID selector 壓過角色背景的 specificity 衝突，恢復原金色／銅色角色填色；帳號列高度由桌機 presentation 參數控制，文字使用明確行高垂直置中；帳戶管理、科目管理、設定按鈕加高為 34px，與帳號外框一致。手機／平板不變。 |
| V0.22.33 Build 0 | 桌機期初餘額／匯出按鈕統一尺寸與字級；右上使用者姓名與角色字級跟設定一致並垂直置中；月份浮窗靠左定位，避免第一欄被清單容器裁切。手機與平板樣式不變；統計及圖表尚處方案階段。 |
| V0.22.32 Build 0 | 桌機超級管理員 Excel 指定期間替換；先做 R2＋GCS 回讀驗證備份，再以完整 snapshot guard 原子替換，新增 append-only 稽核與 schema 8。鎖帳規則維持，保留期間外交易、期初餘額與相同交易的多次發生。大型壓縮 XML 改 bounded unzip 後仍交同一 XLSX parser，避免 Worker 無法啟動巢狀執行緒。25 組本地測試與 CI 通過，已部署；實際帳務操作紀錄不放公開版本文件。 |
| V0.22.31 Build 3 | 歷史帳目調查續修：月帳與期初工作表 autoFilter 必須先於 mergeCells，修正 Excel 修復提示的結構缺陷；空月、180 筆跨帳戶／收支與模板結構回歸，24 組本地測試通過。已完成授權的唯讀 D1 查詢，清單與資料庫月份分組一致，日期格式與移轉／現行總筆數核對正常；缺帳仍待原始完整 SQLite／WAL 比對。暫時腳本／public key／CI 已移除，真實資料未進 Git；PR #373 與正式部署 成功；未修改正式帳務或手機／平板呈現。 |
| V0.22.31 Build 2 | PR #371 已合併；正式部署成功。 桌機右側年月／鎖帳圖示／五格統計／期初餘額／匯出合併單列；月份與收支上下緣同高，移除三角、箭頭 SVG 置中。共用 quick-lock 邊界規則，桌機非邊界月份開啟既有鎖帳設定；觸控流程不變。歷史月份缺帳／收支不符尚未結案，需比對正式月帳匯出與來源帳本；本次未改動帳務資料。 |
| V0.22.31 Build 1 | PR #369 已合併；正式部署成功。 桌機常用項目框與欄位標籤靠左對齊、pill 微幅放大；收支與月份上緣對齊，月份沿用既有選擇 owner 改為平板式緊湊外觀與下拉提示。科目列壓縮但保留字級；資料移轉直接顯示、警示文字縮小。設定各項目固定 640×532px（小視窗高度受 viewport 限制），維持原寬度；備份資料欄保留 KB 同行。手機／平板版面及資料 owner 不變。 |
| V0.22.31 Build 0 | PR #366 已合併；正式部署成功。 桌機 slider 降高、清單 loading 遮罩與 spinner、窄設定／備份換行、資料移轉精簡；Excel 空白模板下載；CYID expiry／401 共用登入導頁。未改手機／平板版面、D1 schema 或帳務寫入 owner。 |
| V0.22.30 Build 1 | PR #364 已合併；正式部署成功。 右上帳號列恢復平板同款；帳戶標題隱藏，日期／科目／摘要／金額標籤同行、放大黑字；收支控制加寬；常用項目加虛線框及小型 pill；金額／儲存／清空統一 40px。月份控制靠上並加框，空／有資料欄位固定；帳戶改名與封存同行靠右。設定縮為 640px，桌面帳本移轉併入資料管理，清除備份空訊息占位。只調整桌機 presentation，手機／平板尺寸與流程保留。 |
| V0.22.30 Build 0 | 使用者電腦版 10 項實機返修：所有版本移除新增記帳／記帳資料標題；桌機保留原 split 左右版面並將收支靠左、日曆改單一 SVG、表單改成帳戶後依日期／科目＋小常用／摘要＋小常用／金額＋儲存＋清空逐列；全裝置帳本獨立收支欄隱藏，以金額正負號及相應色彩呈現；桌機切月顯示載入中；期初餘額對話窗加寬，手動調整 audit 直接顯示在右半邊；期初／收入／支出／期末／淨利損同平板五格放大；修正編輯列輸入最小寬導致跑版；右上角色標籤改平板式括號純文字，沒有 pill／漸層背景。保留共用 writer 與既有觸控表格欄位 index。PR #362 已合併，Production 驗證 validate/deploy 及 semantic assets 成功，仍待真實裝置畫面驗收。 |
| V0.22.29 Build 0 | 電腦版實機回報恢復：Input Confirmation 建立時間晚於 Adaptive split 初始化，造成桌機雙欄工作區未建立、確認側欄遮住帳本；修正為由正式 Confirmation owner 發出 `cyacc:confirmation-ready`，既有 Adaptive owner 收到後完成左右分欄。Desktop 日期 JS 已改以 `desktopUi-date-*` 命名，原 CSS 卻仍使用 `cy-date-*`，造成原生 date 與自製日期重複顯示；本次直接同步原 CSS selector，不另加相容殼。加入時序與 selector 回歸、更新 production semantic verifier；PR #360 已合併，Production 驗證 成功，真實桌機視覺待確認。 |
| V0.22.28 Build 0 | 平板直式把手移到 rail 上緣，展開向上凸出、收合只留 tab；左側收支沿用 canonical segmented-control 視覺，右側改為「帳戶／科目／常用科目」、「日期／摘要／常用摘要」、「金額／儲存／清空（取消）」三列；平板設定視窗縮至 640px，修正月份鎖帳與備份標題列，資料管理／資料移轉限制 Desktop-only。PR #357、部署驗證調整 PR #358、Production Deploy #547 成功。 |
| V0.22.27 Build 0 | 平板直／橫式實機修正：交易表改為固定欄寬，空月份與有資料月份不再因 tbody 內容改變欄位比例；「操作」表頭雙方向改置中。月摘要改為固定五格並放大，期初／收入／支出／期末／淨利損無論數值長短皆保留相同欄位。Excel 匯出明確共用手機 native file share，平板直／橫式與手機 preview 都走同一 navigator.share/canShare 路徑，File 固定使用 xlsx MIME。直式凸起把手固定白色、不再依收支著色；收合時移除整條殘留 rail，只顯示把手，展開時縮小並與面板邊界融合。左側收入／支出文字改為真正直向排列。 |
| V0.22.26 Build 0 | 修正 V0.22.25 平板直式返修：月摘要恢復到年月／工具列下方的獨立單列，避免與年月選擇器重疊。新增記帳把手依實機示意改成面板上緣中央向上凸出的標籤式拉耳，收合顯示「↑ 展開新增」、展開顯示「↓ 收合隱藏」；把手不再佔用面板內一整列，收合時只保留薄面板邊線與凸起標籤。V0.22.25 的左側垂直收入／支出與三層輸入配置維持不變。 |
| V0.22.25 Build 0 | 平板直式新增記帳區重排：收合把手改為中央「↑ 展開新增」，展開後為「↓ 收合隱藏」，移除舊灰色短槓。收入／支出沿用原本 kind owner，但在直式改為最左側窄版垂直雙段按鈕；右側第一列為帳戶／日期／科目／金額，中間 30px 快捷列並排常用科目／常用摘要，最後一列以寬摘要欄搭配儲存與清空／取消。手機與平板橫式不改，canonical writer／edit state 不變。 |
| V0.22.24 Build 0 | 平板介面收斂：直式與橫式的明細「操作」表頭統一靠左；直式月摘要改與橫式相同的單列同行樣式與分隔線，移除「記帳資料」標題列。直式底部記帳 rail 移除「保持展開」checkbox 與 pinned-open 狀態，只保留把手點按／拖曳展開收合；帳務 writer、month state 與既有快速鎖帳不變。 |
| V0.22.23 Build 0 | 平板直式帳本再收斂：年月選擇器改為有邊界與下拉提示的觸控膠囊；Excel 匯出按鈕移到期初餘額右側，直式與橫式都沿用手機的原生系統分享。修正 iPhone Safari 搜尋欄 15px 觸發 focus zoom 與 visualViewport 壓縮工作區造成的大面積灰區：搜尋欄固定 16px，鍵盤開啟時不再縮短 Tablet workspace。直式明細移除獨立「收支」欄，金額直接以收入綠色 +、支出紅色 − 顯示；操作欄的編輯／刪除改為筆與垃圾桶圖示並保留 aria-label。 |
| V0.22.22 Build 0 | 平板直式補齊手機成熟 touch lifecycle：月份切換顯示「載入中…」並鎖住相關控制項、原生搜尋 Enter 收鍵盤、存檔成功訊息淡出、期初餘額／月份鎖帳共用 compact utility。年月選擇器旁新增快速鎖帳圖示，但不建立第二套鎖帳規則：只允許 `lockedThrough` 下一個月逐月上鎖；只有最新鎖帳月份可逐月解鎖並將 `lockedThrough` 退回前一月；更早已鎖月份與跳月未鎖月份都顯示狀態但停用，沒有鎖帳起點時也必須先從既有月份鎖帳視窗設定。 |
| V0.22.21 Build 0 | 平板直式介面返修：右上維持 canonical inline account cluster，設定按鈕與帳號列高度一致，角色改為姓名後方的純文字括號標示並保留 SUPER_ADMIN 金／ADMIN 銅／USER 中性色；手機 dropdown identity 在平板直／橫式均維持隱藏，返回手機版只屬手機 tablet preview。直式底部記帳 rail 改為較大的「展開記帳／收起記帳」把手，展開內容壓縮為兩列欄位配置以增加帳本可視高度；直式搜尋改用原生 type=search 欄位，移除搜尋／清除按鈕並支援原生清除回復完整清單。 |
| V0.22.20 Build 1 | 同一手機平板測試模式工作項目返修，不升 Patch：平板直式、平板橫式與手機模擬平板恢復原本 inline 帳號列 `員工編號 姓名［角色］｜登出`，不再使用手機下拉帳號元件；角色視覺只共用手機色票，SUPER_ADMIN 金色、ADMIN 銅色、USER 中性色。手機正常模式仍使用原手機帳號下拉，並保留「測試用平板版」；平板 preview 暫時新增獨立「返回手機版」按鈕，兩個測試入口在正式 Release 前移除。 |
| V0.22.20 Build 0 | 新增手機端平板介面測試入口：手機帳號選單在「登出」上方提供「測試用平板版」，啟用後以 session-scoped preview 強制使用平板 presentation，直式以 820 寬度、橫式以 1194 寬度的 viewport 參考值呈現，並沿用同一平板 layout / gesture / month / identity owner；旋轉手機會切換平板直／橫式。測試模式帳號選單提供「返回手機版」，不修改帳務資料 owner、API、D1 或 CYID contract。 |
| V0.22.19 Build 0 | 平板主介面新工作項目：直式移除右上角與記帳無關的 confirmation drawer 把手，底部記帳 rail 保留唯一拖曳把手且首次進入預設展開；平板直／橫式右上帳號改直接共用手機 `mobileAccountMenuButton`／帳號選單 owner，SUPER_ADMIN 金色、ADMIN 銅色與手機一致；直式月份選擇改與已驗收橫式共用同一 `ledgerMonthDisplay`＋原生 `type=month` touch owner，修正年月顯示／點選不一致。無新增第二份帳號、月份或 writer state。 |
| V0.22.18 Build 3 | 同一平板橫式登入／找回密碼工作項目續修，不升 Patch：忘記密碼／重設密碼視窗比照橫式登入版型，將「重設密碼」移到左側「志遠記帳系統」下方，右側只保留員工編號、寄送驗證碼與後續重設欄位／操作；手機、平板直式、CYID recovery 流程與 visualViewport 修正均不變。 |
| V0.22.18 Build 2 | 同一平板橫式登入工作項目繼續實機返修，不升 Patch：將「員工帳號登入」從右側表單頂端移到左側「志遠記帳系統」下方，使左側形成完整品牌／登入標題區，右側只保留員工編號、密碼與操作；手機、平板直式、CYID 登入流程與 visualViewport 修正均不變。 |
| V0.22.18 Build 1 | 同一平板橫式登入工作項目的實機返修，不升 Patch：灰色錯位區塊已於 Build 0 解決後，依實機畫面微調橫式鍵盤狀態的視覺平衡；登入卡在可視 viewport 內改為置中、略縮總寬與欄間距，品牌與表單垂直對齊，訊息／忘記密碼區縮短，維持欄位與登入按鈕的觸控尺寸。手機與平板直式不變。 |
| V0.22.18 Build 0 | 平板橫式登入第三輪實機返修：修正 iPad Safari 第一次叫出鍵盤時 visual viewport 會被瀏覽器平移、同時程式又對 focus 欄位呼叫 `scrollIntoView()`，造成登入卡被二次往上推並露出一條錯位背景區塊；登入 shell 現在直接跟隨 `visualViewport.offsetTop` 與實際可視高度，移除鍵盤開啟時的 `scrollIntoView()`，並在裝置旋轉造成 viewport 寬度明顯改變時重建 resting viewport baseline。手機、平板直式與平板橫式既有尺寸策略不變。 |
| V0.22.17 Build 0 | 登入頁第二輪實機返修：手機固定使用同一套 compact 尺寸，不再因鍵盤開啟才縮放；平板直式保留較大的既有尺寸，鍵盤只影響可視高度／位置，不改卡片字級與尺寸；平板橫式改為專用雙欄 presentation，左側品牌、右側登入表單，員工編號與密碼並排，壓低垂直高度以適應橫式螢幕鍵盤。手機／平板仍共用同一 HTML、登入流程、CYID authority 與 visualViewport owner。 |
| V0.22.16 Build 0 | 手機／平板登入頁鍵盤適應：移除 touch 裝置自動 autofocus，桌機細指標環境仍保留；登入頁改以 `visualViewport` 同步可視高度，頁面本體禁止產生殘留 body scroll，內容由登入 shell 依實際可視高度捲動；鍵盤開啟時自動進入 compact layout，縮小 Logo、標題與間距但維持可觸控欄位高度。手機正常登入時讓兩欄與登入操作盡量留在鍵盤上方；平板橫／直式共用同一 responsive login owner，不建立第二套登入流程。 |
| V0.22.15 Build 0 | 正式 Release：承接 V0.22.1～V0.22.14 的跨裝置與架構收斂，包含手機／平板介面、原生日期、底部儲存／清空或取消、帳戶固定色號 lifecycle、SUPER_ADMIN 手機備份資訊、CYID Session 帳號顯示穩定化，以及 Ledger／Settings／Toolbar 單一 owner 收斂。PR #318、Production Deploy #461、Stable Release #2 均成功；公開 tag 為 `cyaccountingweb-v0.22.15`，Build 歸零；前一個 V0.22.0 Release/tag 已依發布要求移除。 |
| V0.22.14 Build 5 | 手機 V0.22.14 實機返修：修正儲存按鈕仍被 `grid-row:auto` 拉回金額欄下一列的問題，讓金額後的彈性 spacer 真正吸收不同手機高度，操作區順序改為訊息→儲存→清空／取消，使清空／取消只留小間距貼近底部頁籤、儲存緊鄰其上；同時修正右上角帳號載入時先顯示「帳號」placeholder 的閃爍，改為 CYID Session 準備完成且 `currentUser` 有真實內容後才顯示手機帳號按鈕。 |
| V0.22.14 Build 3 | 手機新增／編輯記帳版面微調：將「儲存／清空」與編輯狀態的「儲存修改／取消」推至底部頁籤上方的操作區，不再緊貼金額欄；帳戶、日期、科目、常用科目、摘要、常用摘要與金額列小幅加高，欄位標籤、輸入內容與常用項目字級同步放大；僅調整 Mobile presentation，不新增第二套 writer 或 business flow。 |
| V0.22.13 Build 0 | 手機實機返修：記帳日期移除 `YYYY/MM/DD` 自製顯示遮罩，回到原生 `type=date` 以恢復可靠點選／選日；恢復新增狀態「儲存／清空」與編輯狀態「儲存修改／取消」，取消沿用既有 ledger return context 回到原月份／原位置；手機看帳帳戶與科目增加小幅視覺間距；空資料狀態水平置中並移除 `td.empty` 遺留底線；備份資訊移除多餘說明文字。 |
| V0.22.12 Build 0 | 手機看帳改善：SUPER_ADMIN 的「更多」新增唯讀「備份資訊」，共用既有 `/api/backup/status` 與 Backup UI model，只顯示備份狀態、排程、保留政策、最近有效備份、Provider health 與 Phase C 進度，不提供手動備份／復原操作；同時移除 Backup UI 既有函式覆寫 patch chain。手機空帳本改為純文字「本月尚無記帳資料。」而不再套交易卡片外框。 |
| V0.22.11 Build 0 | PR #311 已合併，production deploy #431 成功；帳戶色彩升級為正式 lifecycle：`accounts.color_slot` 永久跟隨帳戶，改名／排序／封存／解封不換色，SUPER_ADMIN 永久刪除後才釋出，下一個新帳戶優先補最小空 slot；1～20 使用高差異淡色＋深字，21～40 使用對應深色＋淺字，41 起每 40 個循環。移除帳戶名稱 hash 配色，仍不修改看帳欄寬。 |
| V0.22.10 Build 0 | 看帳帳戶辨識：由 shared ledger renderer 依帳戶名稱穩定產生淡色背景，同一帳戶在手機／平板／桌機維持相同色彩；色塊僅以背景 pseudo-element 呈現，不修改既有欄寬、grid column 或帳戶欄 layout。 |
| V0.22.9 Build 0 | 手機記帳：登入後新增記帳預設由支出改為收入；手機日期保留原生 date picker，但畫面固定顯示 `YYYY/MM/DD`，送出資料仍維持 `YYYY-MM-DD`；常用摘要改與 bootstrap 明確同步，不再靠連線文字／DOM observer 延遲補畫，並移除 `src/index.js` 重複的 frequent-summary API owner。 |
| V0.22.8 Build 0 | 架構整理第 5/5 階段：移除現行 frontend 的歷史版本殼與版本式命名；Adaptive UI 的 V20/V21 雙 bootstrap 收斂為單一 `startAdaptiveUi()`，Input Confirmation 移除先建舊控制再由 V09/V091 覆寫的 patch chain，Ledger Tools 移除 V06 bootstrap 與已失效的 group-toggle cleanup，Desktop Migration 的 V19 函式／DOM／CSS 改為語意名稱；asset cache revision 改用模組語意名稱。資料格式／SQLite schema／CYID API 的真實版本 contract 保留。 |
| V0.22.7 Build 0 | 架構整理：Ledger Toolbar 收斂為單一 shared owner；`ledger-tools.js` 一次建立月份導覽、期初／鎖帳、搜尋、匯出與更多工具結構及 action，Mobile／Tablet／Desktop 只保留 layout／native picker 呈現差異；移除 desktop-first 搬移、touch retry toolbar、重複期初 action，以及 Excel 匯入先建在帳本再搬到設定的路徑。 |
| V0.22.6 Build 0 | 架構整理：交易清單收斂為 `ledger-tools.js` 單一 row renderer；移除 `transactionRows` empty-state／quick-entry／inline-edit observer 路徑，改以明確 `cyacc:ledger-rendered` lifecycle 通知存檔後行為；保留 swipe、click 與 inline edit 的正常事件 delegation。 |
| V0.22.5 Build 0 | 架構整理：設定管理收斂為單一 lifecycle/renderer/action owner；移除 quick-entry 的舊排序 DOM injector/observer 與 category-management 的舊科目移動 DOM injector/observer，保留現行 optimistic drag/reorder 與 canonical mutation path。 |
| V0.22.4 Build 0 | 架構整理：移除 V0214 多階段 retry patch；帳戶餘額 popover 改為 DOM ready 單次綁定、移轉完成視窗改為按需建立，設定 favorite/default 回歸既有 canonical mutation owner；同步移除 v0214 runtime/CSS 命名。 |
| V0.22.3 Build 3 | 平板日期／月份樣式收斂：移除多代 tablet date/month CSS override，只保留單一 presentation 規則；native date 與 touch month display 沿用 shared owner，並加入重複規則 regression guard。 |
| V0.22.3 Build 2 | 平板橫式日期／月份共用手機 touch/native owner，不再建立 Tablet 自己的日期／月份元件或第二份 state。 |
| V0.22.3 Build 1 | desktop interaction 判定收斂為單一 `isDesktopInteractionWorkspace()`；1024px 不再直接等同 desktop interaction。 |
| V0.22.3 Build 0 | 架構整理第一階段：移除正式 adaptive UI 路徑中的 v21/v0211/v0215 與 Build 編號式函式、狀態、dataset、CSS class 命名，改為功能語意名稱；本版不改 breakpoint、互動條件或 UI 行為。 |
| V0.22.2 Build 3 | 平板橫式實機返修：修正 iPad 被桌機日期／月份 picker 接管的根因，記帳日期恢復原生 date、看帳月份改用手機式顯示層＋原生 month；記帳區再縮窄、所有輸入置中、常用 pill 比照手機、儲存／清空置底，並移除平板橫式匯入 Excel。 |
| V0.22.2 Build 2 | 平板橫式限定返修：左側記帳區縮窄、原生日期、帳戶／科目置中、常用項目常駐、移除右側把手與兩個區塊標題、帳號角色改為同行純文字、手機式月份／搜尋／清除／Excel 分享、單行統計與更高密度帳本，編輯／刪除改圖示；手機、平板直式及桌機不變。 |
| V0.22.2 Build 1 | 平板排版返修：欄位防溢出、常用項目移至對應欄位、工具列／統計順序、直式拖曳把手；沿用共用元件及 writer，部署待此次 CI |
| V0.22.2 | PR #292 已合併，正式部署 成功；平板雙方向配置、native-first 欄位、共用 entry 編輯及旋轉／草稿保護，未建立公開 Release |
| V0.22.1 Build 1 | 正式部署；跨裝置共用編輯／optimistic／rollback，摘要限制一致，期初調整標籤與間距，保護成功寫入後的舊月份查詢回應 |
| V0.22.0 | 歷史手機穩定版；新增帳戶提示「帳戶名稱最多八字」。原公開 Release/tag 已在 V0.22.15 發布成功後移除；歷史內容仍見 [V0.22.0.txt](V0.22.0.txt) 與 Git history。 |
| V0.21.16 | 封存／編輯／刪除圖示、已封存帳戶入口、設定收支 slider 與純色、科目顯示大分類／科目 |
| V0.21.15 Build 1 | 手機收支改純色；封存視窗動態高度；帳戶設定間距恢復，期初欄位靠近；編輯／垃圾桶圖示 |
| V0.21.14 | 期初只顯示當月調整標籤，移除自動／手動與說明小字；封存帳戶獨立視窗 |
| V0.21.13 | 分類－科目階層清單、分類上下排序、科目 touch/pen reorder／跨分類 |
| V0.21.12 Build 1 | 手機及登入頁 tap zoom 限制與 viewport 處理，保留捲動及滑動操作 |
| V0.21.11 | schema 6、自動期初、理由／append-only audit、CYID actor、Excel parity、備份 inner v2、移轉 override＋audit；零餘額刪除保留歷史 |
| V0.21.10 | 帳戶封存／解封、永久刪除架構、歷史帳戶回填、單一階層科目 renderer、收入在前 |
| V0.21.6 及後續收斂 | 接入 CYID、獨立登入、USER 唯讀；退休版本 runtime 殼與舊 auth overlay。早期 Build 修補不再作現行流程 |
| V0.19–V0.21.4 | SQLite 帳本移轉、D1 批次限制、桌面日期正規化、完成視窗；內容於 2026/10/03 驗收 |
| V0.17–V0.18 | GCS 備份、provider-neutral export-once、R2/GCS 雙副本及 Phase C catalog |

更早的設計／事故快照保留於 [docs/archive/](docs/archive/) 與 Git history。舊描述（手機卡片、漸層、auth overlay、舊版 branch 停點）不可當成現行需求。
