# CYAccountingWeb 版本里程碑

更新：2026/10/07。此處記錄歷史與開發狀態；目前交接見 WORK_HANDOFF.md，永久規則不放在版本紀錄。

| 版本 | 狀態與變更 |
| --- | --- |
| V0.22.30 Build 1 | PR #364 已合併；正式部署 #564（run `37823795188`）成功。 右上帳號列恢復平板同款；帳戶標題隱藏，日期／科目／摘要／金額標籤同行、放大黑字；收支控制加寬；常用項目加虛線框及小型 pill；金額／儲存／清空統一 40px。月份控制靠上並加框，空／有資料欄位固定；帳戶改名與封存同行靠右。設定縮為 640px，桌面帳本移轉併入資料管理，清除備份空訊息占位。只調整桌機 presentation，手機／平板尺寸與流程保留。 |
| V0.22.30 Build 0 | 使用者電腦版 10 項實機返修：所有版本移除新增記帳／記帳資料標題；桌機保留原 split 左右版面並將收支靠左、日曆改單一 SVG、表單改成帳戶後依日期／科目＋小常用／摘要＋小常用／金額＋儲存＋清空逐列；全裝置帳本獨立收支欄隱藏，以金額正負號及相應色彩呈現；桌機切月顯示載入中；期初餘額對話窗加寬，手動調整 audit 直接顯示在右半邊；期初／收入／支出／期末／淨利損同平板五格放大；修正編輯列輸入最小寬導致跑版；右上角色標籤改平板式括號純文字，沒有 pill／漸層背景。保留共用 writer 與既有觸控表格欄位 index。PR #362 已合併，Production run `37736085139` validate/deploy 及 semantic assets 成功，仍待真實裝置畫面驗收。 |
| V0.22.29 Build 0 | 電腦版實機回報恢復：Input Confirmation 建立時間晚於 Adaptive split 初始化，造成桌機雙欄工作區未建立、確認側欄遮住帳本；修正為由正式 Confirmation owner 發出 `cyacc:confirmation-ready`，既有 Adaptive owner 收到後完成左右分欄。Desktop 日期 JS 已改以 `desktopUi-date-*` 命名，原 CSS 卻仍使用 `cy-date-*`，造成原生 date 與自製日期重複顯示；本次直接同步原 CSS selector，不另加相容殼。加入時序與 selector 回歸、更新 production semantic verifier；PR #360 已合併，Production run `37733787088` 成功，真實桌機視覺待確認。 |
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
| V0.22.2 | PR #292 已合併，正式部署 #377 成功；平板雙方向配置、native-first 欄位、共用 entry 編輯及旋轉／草稿保護，未建立公開 Release |
| V0.22.1 Build 1 | 正式部署 #370；跨裝置共用編輯／optimistic／rollback，摘要限制一致，期初調整標籤與間距，保護成功寫入後的舊月份查詢回應 |
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
