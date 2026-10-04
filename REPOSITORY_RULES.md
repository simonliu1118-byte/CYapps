# Shared Repository Rules

本文件是納入此治理體系之 repositories 的**共通永久規則母本**。

- 唯一母本：`simonliu1118-byte/AITeam` 的 `main:/REPOSITORY_RULES.md`。
- 共通規則版本：`main:/COMMON_RULES_VERSION`。
- 共通變更紀錄：`main:/COMMON_RULES_CHANGELOG.md`。
- 採用本治理體系的其他 repository 只保存同步副本，不得自行分叉修改共通規則。
- Public／Private、組織／個人用途、命名、授權、機密與 repository 個別差異寫在各 repo 的 `REPO_POLICY.md`。
- 個別產品／工具的永久例外寫在唯一 `PROJECT_RULES.md`：單一專案 repo 放根目錄；monorepo 放 `apps/<Project>/PROJECT_RULES.md`。

## 1. 規則層級與唯一來源

永久規則只允許存在於下列三層：

1. `REPOSITORY_RULES.md`：採用此治理體系之 repositories 的共通規則，AITeam 為母本。
2. `REPO_POLICY.md`：目前 repository 的用途、Public／Private、命名、授權、機密、CI 成本等 repo-specific 規則。
3. `PROJECT_RULES.md` 或 `apps/<Project>/PROJECT_RULES.md`：個別專案必要的補充或例外。

規則衝突時依下列優先順序：

1. 使用者當次明確指示。
2. 目標專案 `PROJECT_RULES.md`。
3. 目前 repository 的 `REPO_POLICY.md`。
4. 本 `REPOSITORY_RULES.md`。
5. 其他文件。

`README.md`、`WORK_HANDOFF.md`、`PROJECT_STATUS.md`、`TODO.md`、`CHANGELOG.md`、`VERSIONING.md`、版本紀錄與設計文件只描述用途、狀態、歷史、需求或待辦，**不得自行成為新的永久規則來源**。`AGENTS.md` 只可作為 AI 入口與規則索引，不得重複或新增另一套規則。

## 2. 共通規則與治理規則的變更

- 下游 repository 不得直接分叉修改共通規則。要改共通規則，必須先在 AITeam 的 `governance/*` branch 修改母本，更新 `COMMON_RULES_VERSION` 與 `COMMON_RULES_CHANGELOG.md`，完成檢查後合併 AITeam `main`，再同步到所有採用本治理體系的 repo。
- 各 repo 自己的 `REPO_POLICY.md`、`PROJECT_RULES.md`、治理基礎設施變更仍使用該 repo 的 `governance/*` branch，更新該 repo 的 `GOVERNANCE_VERSION` 與 `GOVERNANCE_CHANGELOG.md`。
- `RULES_INDEX.md` 列出的檔案才是允許存在的治理／規則檔；新增第四層規則檔視為錯誤。
- 如果只是一次性任務例外，應寫在該次 PR／Issue／工作說明，不應直接變成永久規則。只有會反覆適用、且使用者同意的內容才升格為規則。
- 同步 workflow 可自動建立共通規則同步 PR，但不得自動修改 repo-specific policy 或 project rules。
- 治理 CI 應檢查未授權的 `*RULES*`、`*POLICY*`、`*INSTRUCTION*`、`*GOVERNANCE*` 類規則檔、版本檔與母本同步狀態。

## 3. 正式基準、branch 與 Pull Request

- `main` 是該 repository 的正式基準，只保存可追溯、可重建、已完成必要檢查的狀態。
- 日常開發使用獨立 branch；一般命名建議為 `<project>/<type>-<summary>`，例如 `myapp/fix-layout`、`tool/feature-backup`。治理工作使用 `governance/<summary>`。
- 一個 PR 原則上只處理一個專案或一個明確主題；不得順手修改無關專案。
- 合併前 PR 說明至少包含：變更目的、主要影響、測試／驗證結果、已知風險；高風險資料或 API 流程需另外說明。
- 已完成的短期 branch 合併後應刪除；長期實驗線、相容性線或歷史封存 branch 可由 `PROJECT_RULES.md` 明確保留。
- 不得任意 force-push `main`。只有歷史清理、機密移除等使用者明確同意的維護作業可例外執行，完成後必須重新驗證 refs。

## 4. Commit 身分與命名

- 新 commit 的 author／committer email 一律使用 GitHub private noreply：`286269326+simonliu1118-byte@users.noreply.github.com`；GitHub 官方 bot 自身的 noreply 身分除外。
- 不得再使用個人 Gmail 作為新 commit metadata。
- **凡中文名稱需要轉寫為羅馬拼音時，一律採 Wade–Giles（威妥瑪）**；不得自行改用 Hanyu Pinyin 或其他拼音系統。既有正式英文名、品牌名、產品名或特定固定 spelling 仍依各 repo 的 `REPO_POLICY.md`／`PROJECT_RULES.md` 為準。
- 組織、品牌、產品的固定英文 spelling 等命名慣例屬 repo-specific 規則，應由該 repo 的 `REPO_POLICY.md` 或專案 `PROJECT_RULES.md` 定義，不得硬寫入共通母本。
- 專案、資料夾、檔名與程式識別優先沿用既有正式名稱，避免無必要更名造成相容性與追蹤問題。

## 5. 原始碼、執行資料與機密

- 原始碼、建置設定、必要資源、測試與維護文件應納入 Git。
- EXE、DLL、ZIP、7z、MSI、LOG、Cache、暫存檔、使用者資料、正式執行資料與本機設定原則上不得提交至 Git；個別專案例外必須由 `PROJECT_RULES.md` 明確列出。
- 可公開 repository 與其 Git 歷史不得包含正式密碼、API Key、token、OAuth client secret、private key、組織／個人敏感資料、客戶／交易／帳務資料或其他機密。
- 公開測試憑證只有在來源本身已由供應商公開，且 `PROJECT_RULES.md` 明確記錄其用途時才可進入 Public source。
- 任何可能公開的程式不得依賴寫死在原始碼中的固定管理密碼、清除密碼或其他秘密；應使用安全的本機設定、雜湊或作業系統安全儲存。
- `.gitignore` 只能防止未來誤提交，不能視為已清除歷史。秘密一旦進入 Git，必須另做 history cleanup／rotation／風險處理。

### 5.1 Binary asset source integrity 與衍生資源 SOP

本節適用於會影響產品輸出的必要二進位資源，例如 PNG／JPG／WAV／ICO、字型、韌體映像或其他專案核准納入 Git 的 binary source。若個別專案禁止某類 binary 進 Git，仍以該專案 `PROJECT_RULES.md` 為準。

- 正式採用 binary asset 前，必須先確認 intended source 與 canonical repository path；至少記錄或可重建其 byte size 與 SHA-256。圖片另應確認 dimensions；其他格式依專案需要記錄可驗證屬性。
- 需要人工判斷的視覺／聲音素材，先完成 intended source 的人工目視／試聽確認，再進行轉檔、縮放、ICO 生成或其他衍生處理；CI 不得把「檔案可解析」冒充成「內容已被使用者接受」。
- Base64、chunk、hex dump 或其他文字化表示只能作為傳輸手段。除非 `PROJECT_RULES.md` 明確把該格式本身定義為正式 source，repository 最終必須保存真正可直接使用的 binary 檔，不得把傳輸用 `.b64`／chunk 當成永久素材來源。
- Binary asset 寫入 Git 後，必須從 repository read-back，再核對 byte size／SHA-256 與 intended source 完全一致；只看到正確檔名、GitHub preview、圖片外觀或上傳成功訊息，不足以視為 source gate 通過。
- 任何 SHA-256／size 不一致、來源不明或 read-back 失敗的 binary asset，不得進入正式 build／package／Release；先修正 source 再繼續，不得用重新產生近似素材來掩蓋來源差異。
- ICO、縮圖、resized image、resource blob 等 derived resource 原則上只能在 source gate 通過後產生；能自動產生者應優先由可重現的 script／build pipeline 建立，避免把不可追溯的手工轉檔當成唯一來源。
- Windows icon/resource 若有專案要求的 native sizes、bit depth 或 container 結構，CI 應直接檢查該衍生檔的實際 entries；不得只檢查副檔名或單一 preview。
- 對 self-contained／single-file EXE，icon/resource 應優先透過正式 compiler／resource build pipeline 嵌入。不得把可能截斷、覆寫或破壞附加 payload 的 post-publish binary resource patch 當成一般做法；若專案確有不可避免的 post-processing，必須有明確專案例外與完整 binary integrity 驗證。
- 最終 Windows binary 應依風險加入合理驗證，例如 expected-size lower bound、PE/resource 可解析性、OS associated icon extraction、dependency／manifest 檢查與 startup smoke；這些檢查要驗證**最終產物**，不能只驗證中間 source。
- Explorer、taskbar、window icon、shell cache 或 DPI 顯示等受真實 Windows UI／cache 影響的項目，CI 不能完全取代實機目視；專案需要時仍保留 real-machine acceptance。
- Binary asset 的新增、替換或 manifest/hash 變更若會影響產品輸出，應納入相關 CI 的 path trigger／驗證範圍；不得因素材不是 source code 就繞過必要 build/package gate。

## 6. 版本號、Build 與版本來源

- 每個可發行專案根目錄必須有 `VERSION`，內容只放基礎版本 `X.Y.Z`，作為程式、CI、封裝與 Release 的版本來源。
- 專案以 `BUILD` 整數保存同一工作項目的返修次數：`0` 代表不顯示 Build；`1` 代表 `Build 1`；依此類推。既有專案首次導入時預設 `0`。
- 使用者可見版本格式為 `VX.Y.Z`；當 `BUILD > 0` 時為 `VX.Y.Z Build N`。
- `X`（Major）代表重大產品世代。**只有使用者可以決定升 X**；AI 不得自行升 Major。
- `Y`（Minor）代表使用者能明顯感受到的新能力、完整功能階段或具份量的功能升級。負責開發的 AI 可依實際工作內容自行判斷是否升 Y，升 Y 時 Z 歸零、Build 歸零；PR／版本說明需簡要說明升 Y 理由。一般修正、小改善或單一 UI 調整不得濫用 Y。
- `Z`（Patch）是日常開發的預設版本遞增單位。開始新的獨立修改項目、新 bug、新需求或小型功能時，通常 Z + 1，Build 歸零。
- 同一個 Z 所代表的工作項目若第一次交付／測試後仍未達成原要求，繼續修正**不再升 Z**，改為 Build + 1。例如：`V1.0.2` → `V1.0.2 Build 1` → `V1.0.2 Build 2`。
- 上一個工作項目完成後，開始另一個獨立項目時再升下一個 Z；若新工作本身達到 Minor 標準，AI 可改升 Y。
- 「同一項目返修」包含上一版尚未修好的同一 bug、同一功能驗收失敗、同一原需求未完整達成；「新項目」包含原要求完成後提出的新修改、不同 bug、不同功能或新增需求。界線不清楚時，AI 必須先詢問使用者。
- 單純重跑完全相同 source 的 CI、runner／網路失敗後 retry、重新下載同一 artifact，不改 `VERSION` 也不改 `BUILD`；GitHub workflow run number 只用來識別 CI 執行批次，**不是**本規則中的 Build N。
- 尚未達 1.0 的專案可使用 `0.Y.Z`；由 `0.x.x` 升為 `1.0.0` 視為 Major 決策，由使用者決定。
- preview／RC／獨立實驗線等特殊版本身分可以由 `PROJECT_RULES.md` 定義，但不得與正式產品線混淆。
- 正式 tag 預設使用專案明確可辨識的 `vX.Y.Z` 或 monorepo `<project>-vX.Y.Z`；各專案既有 tag 慣例可在 `PROJECT_RULES.md` 固定。
- 若準備正式 Release 時目前 `BUILD > 0`，不得擅自把 Build 身分消失或覆寫成不同內容的同版；應先向使用者確認正式發布的版本身分／升版方式。
- 已存在的正式 tag／Release 不覆寫；需要後續修改時依上述 X/Y/Z/Build 規則建立新的版本身分。
- 測試包／工程 Artifact 必須顯示 `VERSION` 與適用的 `Build N`，並可另外附 workflow run number 或 short SHA 作技術追蹤。

## 7. CI / GitHub Actions / 協作 AI

- CI 的目的，是以合理自動化成本提高品質與可重建性；不得為省少量資源而增加大量人工步驟，也不得把 CI 當作每個小修改的試錯迴圈。
- Public 與 Private repo 都可正常使用自動 CI；Private 需更留意 Actions minutes，但不以犧牲便利性與可靠性換取小幅節省。
- **一般 Build／Test workflow 預設同時支援 `pull_request` 與 `workflow_dispatch`。** `pull_request` 是 AI 與日常開發的標準遠端驗收入口；`workflow_dispatch` 是人工或特殊情況的備援入口。若專案因技術或成本確有不同需求，必須由 `PROJECT_RULES.md` 明確列為例外。
- Draft PR 可以正常執行 Build／Test；Draft 只表示「尚未準備合併」，**不得兼任 CI 開關**。不得為了觸發驗證而要求 AI 或使用者反覆切換 Draft／Ready 狀態。
- PR 建立或更新後，應依實際變更路徑自動執行該專案必要驗證；使用 `paths`／`paths-ignore` 避免不相關專案一起跑。
- 一般開發 branch 的單純 `push` 不預設另外重複跑完整昂貴 CI；同一份變更若已有 PR 驗證，不應再因 branch push 重複跑第二套完整 Build／Test。
- 節省 CI 次數的主要方法是 Local-first、集中完成一輪相關修改後再 push、path filter 與 `concurrency`，而不是取消 PR 自動驗收或把所有 Build/Test 改成只能手動觸發。
- 同一 PR 新 commit 應以 `concurrency` 取消尚未完成的舊 run。
- Build／Test 預設只授予 `contents: read`；只有確實需要建立 tag／Release／寫入 repo 的 workflow 才給 `contents: write`。
- 純測試 CI 與 Codex、Claude 等計量式 AI 審查應盡量解耦；不得每次 push 都重新啟動昂貴 AI 審查。
- GitHub 官方 Actions 使用仍受支援的穩定 major 版本；不為追新而無意義頻繁升級。

### 7.1 Local-first 與 Token／工具成本控制

核心原則：**Local-first development, GitHub-final verification。** 目標是減少不必要的上下文、GitHub 往返與 Actions 消耗，但不得降低必要驗證或發布可靠度。

- 新對話、新工作階段或重新接手專案時，仍須完整確認正式規則鏈與目前基準；同一工作階段內，已確認且未變更的治理文件、README、CHANGELOG、workflow 或完整 source 不應無理由反覆重讀。
- 只有 Governance／`PROJECT_RULES.md` 更新、`main`／工作 branch 基準有重大變更、需要重新建立上下文、或使用者明確要求完整審查時，才重新做全面確認。
- 日常修改先讀本次需求真正相關的檔案與相依區段；不得形成「完整讀 repo → 小改 → push → 再完整讀 repo」的高成本循環。
- 優先在目前工作環境完成 source 修改、可用的 unit test、static check、lint 與可行的 build；Go／Win32 專案若環境可行，可先做 Windows cross-build，但 cross-build 不取代真正 Windows-specific 驗證。
- 同一輪相關修正應先集中完成與本地檢查，再形成合理的一個 commit／push 單位；除非需要遠端資訊才能繼續，不應每修一個小問題就立即 push 或觸發 CI。
- 一個合理修改批次完成後的 push，可視為一次遠端驗收邊界；若該 branch 已有 PR，應由 PR workflow 自動進行必要驗收，不要求 AI 另外具備手動 Run workflow 能力。
- GitHub Windows CI 主要作為一輪修改完成後的正式 Windows 驗收層。Win32／WinForms／WPF、icon/resource/manifest、Windows DLL linkage、Registry、printer API、WebView2、PowerShell packaging、Release build，以及本地環境無法可靠驗證的 Windows-specific 項目，仍應使用真正 Windows runner 驗證。
- GitHub Actions 成功時，預設只確認 workflow/job/step 成功、tests 結果與必要 artifact／EXE／ZIP 是否產生；不得無理由讀取完整成功 log。
- GitHub Actions 失敗時，先讀失敗 step、error 與其前後必要區段；只有原因仍無法判斷時才逐步擴大 log 範圍，不預設把整份長 log 載入上下文。
- push 後預設只核對本次 commit／diff、必要檔案與 CI 結果；除非基準或治理已變更，不重新掃描整個 repository。
- 編譯、測試、ZIP、封裝與必要驗收本身不得為了省 Token 而省略；要節省的是重複讀取、無效 GitHub 往返、過度細碎 push 與不必要完整 log。
- 個別專案若因技術特性不適合完全採用此流程，可由 `PROJECT_RULES.md` 補充例外；例外應維持同一原則：**先減少重複工作，再談減少必要驗證。**

## 8. 測試包、Artifact 與上傳規則

- 開發中供驗證的 Windows x64 包預設使用 Actions artifact；正式版使用 GitHub Release，除非 `PROJECT_RULES.md` 明確定義不同流程。
- 工程 artifact 預設保留 14 天；需要更長保存時由專案規則或該次工作明確決定。
- 正式 Windows 發行預設提供 portable package，不要求安裝器；若專案需要 MSI／installer，必須由 `PROJECT_RULES.md` 另行規定。
- 正式 ZIP／EXE 必須可由 repository 的正式 source、依賴與 build script 重建。
- 封裝物不得含 runtime 個資、正式資料、local settings、log、cache 或未授權秘密。
- 正式發行至少提供 SHA-256；若只有單一 EXE，也應對可下載檔提供 hash。
- Git 不作為 binary release 倉庫；正式 binary 放 Release，短期驗證 binary 放 Artifact。

## 9. 正式 Release

- 正式 Release 屬低頻且具外部影響的動作，原則上以 `workflow_dispatch` 或其他明確人工啟動方式執行，不因一般 branch push 自動發布；專案若有明確例外，必須寫入 `PROJECT_RULES.md`。
- 正式 Release 預設只能由 `main` 建置；專案若有例外，必須寫入 `PROJECT_RULES.md`。
- Release workflow 必須重新核對 `VERSION`、`BUILD`、必要測試、敏感資料掃描、建置／封裝、SHA-256 與 tag，不得只依賴先前某次 CI 成功。
- Release title、tag、asset 必須與版本身分一致；若經使用者明確批准帶 Build 發布，不得冒充無 Build 的同版。
- 每個正式 Release 應保留該版變更摘要；專案可使用 `CHANGELOG.md`、`VX.Y.Z.txt` 或兩者，但內容不得與 `VERSION`、`BUILD`、tag、Release title 不一致。
- Public repo 的正式 Release 可公開下載；公開下載不代表取得根 `LICENSE` 以外的權利。Private repo 的 Release 必須維持 private。

## 10. Copyright、License 與年份

- Copyright notice、著作權人、品牌名稱與授權文字由各 repo 的 `REPO_POLICY.md` 或根 `LICENSE` 定義；共通母本不得假設公司或個人身分。
- `<YEAR>` 使用該 Release 實際發布年份；Repository 根 `LICENSE` 可使用起始年份或年份區間，例如 `2026`、`2026–2027`。
- README、發行說明、使用說明與 package 內文件不得寫出與根 `LICENSE` 相衝突的權利或散布條款。

## 11. AI 開發與交接

- 接手專案前，AI 必須先讀：根 `REPOSITORY_RULES.md` → 根 `REPO_POLICY.md` → 專案 `PROJECT_RULES.md` → 再讀 `WORK_HANDOFF`／`PROJECT_STATUS`／README／TODO 等狀態文件。
- 若 AI 或自動化系統正在協作其他已具治理規則的 repository，必須先讀目標 repository 的規則，**不得用來源 repo 自己的專案流程覆蓋目標 repo 的版本、Release、命名或安全規則**。
- 不得因舊對話、舊 branch 或記憶與 `main` 不一致就直接覆寫正式 source；應先確認目前正式基準。
- 不得自行大改架構、重寫 UI 或更換技術棧，除非使用者明確同意或現有方案已證明無法安全維護。
- 發現規則矛盾、資料不明確、不可逆操作、可能外洩或版本身分混淆時，必須先說明差異、風險與建議，再處理。
- 專案特例只寫入唯一 `PROJECT_RULES.md`；禁止因一次 bug 或一次需求新增新的規則檔。

### 11.1 一致性優先與例外流程控制

- 設計與實作預設採**單一路徑、單一語意、單一規則來源**。相同的資料值、狀態碼、欄位語意、權限判斷或 UI 樣式，在不同畫面、入口與流程中應維持相同解讀；不得只因某一流程特殊，就改變既有值本身的意義或視覺規則。
- 能以既有共用流程、共用 service、共用資料模型或一般規則處理的需求，不應另外建立平行流程、局部特判、第二套狀態或重複實作。新增功能時應先判斷是否能延伸既有主路徑，而不是先增加例外。
- 例外流程不是禁止項目；當一般流程確實無法完整涵蓋，且例外能解決明確的業務、資料覆蓋、安全或技術限制時，可以提出。例外應保持**最小範圍**，並避免再衍生第二層、第三層例外。
- AI 認為需要新增例外流程時，必須先向使用者說明：**為什麼一般流程不足、例外解決什麼問題、影響哪些範圍、增加哪些維護／測試成本、是否有更簡單替代方案**，由使用者決定是否採用；不得自行默默加入長期特判。
- 使用者提出的需求若會造成既有資料語意、狀態規則、UI 規則或主流程出現新的特例，AI 也必須先明確提醒「這會形成例外流程／特判」，再依使用者決定繼續；不得因需求由使用者提出就省略這個提醒。
- 已核准的例外若屬單一專案永久行為，才寫入該專案唯一 `PROJECT_RULES.md`；若只是當次工作需要，記錄在該次 PR／Issue／設計說明即可。不得為每個例外新增新的規則檔或平行規則層。
- 測試應優先驗證共通規則本身，並對核准例外補最小必要覆蓋，防止後續修改把例外擴散成新的主路徑，或反過來讓例外改寫共通資料語意。
- **不得以版本殼、wrapper chain、patch-on-patch 或動態載入舊版本檔作為一般演進方式。** 新功能、修正與重構應收斂到目前正式主路徑與依功能命名的模組；不得以 `app-v1 -> app-v2 -> app-v3`、`vNNN.js/css` 疊載、Build 專屬 wrapper、或「先保留舊實作再包一層新實作」來取代正常整併。
- 確有外部 backward compatibility、資料格式過渡、第三方 API 遷移、rolling deployment 或不可同步升級的 consumer 需求時，可以建立**最小必要相容層**，但必須同時滿足：單一明確入口、責任範圍可界定、不形成第二套 authority／business logic、不得再疊第二層 wrapper，且 PR／設計說明需記錄存在理由、影響範圍、owner／主責、移除條件與可驗證的退場時機。
- 相容層不得以版本號本身作為長期架構邊界；若保留的是正式 protocol／file-format version reader，應以「格式／contract 相容」命名與測試，而不是保留整套歷史 application runtime。資料格式相容與 source/runtime wrapper 相容必須分開處理。
- 若某次修改發現同一功能已出現多層 wrapper、overlay、patch chain 或版本檔互相覆寫，優先任務是先收斂為單一路徑再繼續除錯；不得在未釐清主路徑前再加下一層 hotfix。必要緊急修復若暫時無法先重構，PR 必須明確標示暫時性、風險、後續收斂工作與移除 gate，且不得把臨時層視為完成架構。
- 對已完成收斂的專案，CI／architecture test 應在合理範圍內防止版本殼、歷史 wrapper 與已淘汰 patch loader 被重新引入；測試應驗證現行功能與 contract，不得因舊測試依賴歷史載入順序而要求把已移除的版本殼加回來。
### 11.2 Canonical Owner、Replacement 與架構例外

本節補充 11.1 的單一路徑原則。核心目標是**限制架構結果，不機械限制程式語法**：允許合理的 observer、timer、fallback、wrapper 或 device-specific presentation，但不得讓它們演變成永久第二套 owner 或 patch-on-patch。

- 每一個可辨識的功能 concern 應能明確指出目前的 **State owner、Business／Mutation owner、Render owner、Lifecycle owner**；同一 concern 原則上各只有一個 canonical owner。若無法指出唯一 owner，新增功能或修 Bug 前應先釐清 ownership。
- Desktop／Tablet／Mobile 可以有不同 layout、CSS、gesture 或 presentation component，但同一 business capability 應共用相同 state、mutation、authorization 與 API owner。不得只因 breakpoint 不同就複製第二套 business/data flow。
- 新 implementation 取代舊 implementation 時，原則上應在同一 PR 移除被取代的 renderer、listener、observer、wrapper、retry、CSS selector、DOM id/class、state 與 dead cleanup。不得以「保險先留著」或「之後再清」為理由讓舊路徑無限期共存。
- 若因 database migration、API rollout、external consumer compatibility、file-format migration、rolling deployment 或 feature-flag transition 必須暫時雙路徑，PR 必須標示 **Architecture Exception**，至少記錄：primary path、legacy path 使用條件、存在理由、scope、owner、風險、移除條件／milestone 與 regression coverage。沒有可驗證退場條件的 temporary path 視同永久架構，不得以暫時層名義加入。
- 對 repository 自己控制的 DOM／component，預設不得以 post-render MutationObserver、DOM 搬移、刪除／替換元素、重新綁 action 或 staged retry 來代替正式 lifecycle。需要 render 後協作時，優先由 canonical owner 提供 event、callback、hook 或 explicit API。
- `MutationObserver` 不全面禁止。監看第三方、瀏覽器控制或本專案無法提供 lifecycle 的 external DOM 可以合理使用；若監看的是本專案自己的 component，PR 必須說明為何 canonical owner 無法提供 event／callback，且不得形成第二個 renderer 或 mutation owner。
- `setTimeout`／retry 不全面禁止。debounce、animation、toast、network backoff、browser layout／paint 等用途可合理使用；但不得用 retry 解決本專案自己的 module load order、DOM readiness 或 owner dependency。這類問題應修正 load order、startup owner 或 lifecycle。
- Wrapper／abstraction／fallback 不全面禁止。新增 abstraction 應集中責任、減少 coupling 或提供明確共用能力；不得只是把舊 implementation 再包一層。Fallback 必須有唯一 primary path、明確觸發條件，且不得讓兩套 implementation 同時都成為 authority。
- 修 Bug 採 **Root Cause First**：先找 canonical owner，再檢查是否已有第二 renderer/listener、observer、retry、wrapper、device duplicate 或 CSS override chain；能直接修 canonical owner 或刪除舊層時，不得先新增下一層 workaround。
- 若修正開始需要新增第二 renderer／mutation handler、MutationObserver、retry bootstrap、compatibility wrapper、DOM relocation、duplicate device component 或新的 CSS override 層，視為 **Architecture Review Trigger**。實作前至少回答：canonical owner 為何不能直接修改、是否形成第二 owner、是否可改用 shared lifecycle/action、是否有舊層可刪、若屬 temporary layer 何時退場。
- CSS 可有 breakpoint 與必要的 `!important`，但不得用 duplicate DOM + hide、持續提高 specificity 或疊加 override 來掩蓋 ownership 問題。樣式規則應保護 presentation 差異，不應成為保存舊 component 的手段。
- 涉及 component/state/lifecycle/business action 的架構 PR，說明應列出修改前後的 State／Render／Lifecycle／Mutation owner，並註明是否新增 observer、retry、wrapper、fallback、device-specific component、transitional legacy path，以及本 PR 移除了哪些舊路徑。修改後 owner 或執行路徑數量增加時，必須說明其必要性。
- Architecture complexity 採 **replacement/removal budget**：新增一層 abstraction 時，應能指出它取代、合併或簡化了哪些既有責任。若修改後 observer、wrapper、renderer、startup 或 state owner 數量增加，預設視為高風險，需要重新檢查是否真的無法收斂。
- CI／architecture test 應保護已確立的 ownership contract，例如「某 DOM 只有一個 renderer」「某 toolbar 只有一個 structure/action owner」「已移除版本殼不得回流」；**不得只因 API 名稱出現就粗暴全面禁止 `MutationObserver`、`setTimeout`、fallback 或 device renderer**。
- Application release number 不得作為長期 runtime architecture boundary、function/class/id/dataset/module 或 cache revision 命名。真正的 API、database schema、migration、backup/file-format、protocol contract version 與使用者可見產品版本仍應保留並依其正式 contract 管理。

最終判斷標準：下一位維護者應能快速回答「資料在哪裡、business/mutation 在哪裡、renderer 在哪裡、lifecycle 在哪裡；裝置差異只在哪個 presentation 層」。若必須依序追查多個 enhancer、observer、retry、wrapper 或裝置專用 owner 才能理解同一功能，表示架構尚未完成收斂。
