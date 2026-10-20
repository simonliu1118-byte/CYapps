using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CYERPAutoInput;

/// <summary>What the CYInvoice step left for the preview and the printing.</summary>
internal sealed class MoInvoiceOutcome
{
    /// <summary>CYInvoice's environment (production／test); empty when it never answered.</summary>
    public string Environment { get; set; } = string.Empty;
    /// <summary>For the later PDF call: CYInvoice.exe, the export, the run's folder.</summary>
    public string Exe { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    /// <summary>CYInvoice's entry: <see cref="CyInvoiceLink.Mo"/> or <see cref="CyInvoiceLink.Coupang"/>.</summary>
    public string Source { get; set; } = CyInvoiceLink.Mo;
    public List<string> Notes { get; } = [];
    /// <summary>Filled orders whose buyer has 統編: a paper invoice to print.</summary>
    public List<CyInvoiceOrder> Paper { get; } = [];
    public bool IsProduction => Environment == CyInvoiceLink.Production;
}

/// <summary>
/// MO店+ 發票 through CYInvoice (user, 2026-10-15; contract in README「MO店+ 發票（CYInvoice）」).
/// CYInvoice is asked first (query, never issues), which also tells its environment: 全自動
/// takes production only, so a test CYInvoice is found out before anything is issued. Only the
/// orders it has no invoice for are then issued, after the user says so; every answer is checked
/// (number, date, amount, 統編) before it goes on the preview, and anything else stays for a
/// person. A paper invoice (a buyer with 統編; a consumer's goes to the member carrier) is printed
/// on the A4 printer from CYInvoice's official PDF: 光貿 finishes an invoice in its own time and
/// the PDF exists only then, so it is fetched in the background without holding the run up, and
/// the paper prompt comes when nobody is typing into ERP (user, 2026-10-15); style from 設定 › 印表機.
/// </summary>
internal sealed partial class MainForm
{
    private Task<MoInvoiceOutcome> InvoiceMoAsync(string title, string file, IReadOnlyList<MoOrder> mo, IReadOnlyList<ImportedOrder> imported, bool fullAuto) =>
        InvoiceAsync(title, CyInvoiceLink.Mo, file, mo.GroupBy(o => o.OrderSn).ToDictionary(g => g.Key, g => InvoiceExpectation.Of(g.First()), StringComparer.Ordinal), imported, fullAuto);

    /// <summary>The invoices of one export through CYInvoice's <paramref name="cySource"/> entry (MO店+ or 酷澎).</summary>
    private async Task<MoInvoiceOutcome> InvoiceAsync(string title, string cySource, string file, IReadOnlyDictionary<string, InvoiceExpectation> source,
        IReadOnlyList<ImportedOrder> imported, bool fullAuto, bool testEntry = false)
    {
        var outcome = new MoInvoiceOutcome { Source = cySource };
        var exe = _settings.CyInvoicePath;
        if (exe.Length == 0 || !File.Exists(exe))
        {
            outcome.Notes.Add("找不到 CYInvoice.exe（設定 › 平台匯入 › CYInvoice），發票請手動填。");
            return outcome;
        }
        // An order a person takes over is not issued automatically (handoff).
        foreach (var o in imported.Where(o => o.HandoffReason.Length > 0))
            outcome.Notes.Add($"訂單 {o.OrderSn} 轉人工（{o.HandoffReason}），發票沒有交給 CYInvoice，請手動處理。");
        var targets = imported.Where(o => o.HandoffReason.Length == 0 && source.ContainsKey(o.OrderSn)).Select(o => o.OrderSn).Distinct().ToList();
        if (targets.Count == 0) return outcome;

        var folder = Path.Combine(MoPrintedDocuments.TempRoot, "invoice-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        outcome.Exe = exe;
        outcome.File = file;
        outcome.Folder = folder;

        SetStatus($"CYInvoice：查詢 {targets.Count} 筆訂單的發票…");
        var (query, queryError) = await RunCyInvoiceAsync(exe, "query", folder, file, targets, source: cySource);
        if (query is null)
        {
            outcome.Notes.Add($"CYInvoice 查詢沒有結果：{queryError}。發票請手動填（請求與紀錄在 {folder}）。");
            return outcome;
        }
        outcome.Environment = query.Environment;
        if (query.Environment is not (CyInvoiceLink.Production or CyInvoiceLink.Test))
        {
            outcome.Notes.Add($"CYInvoice 回傳{query.EnvironmentName}，不使用它的結果；發票請手動填。");
            return outcome;
        }
        // 全自動 issues in either environment (user, 2026-10-15: the whole chain, tested first on
        // CYInvoice's test environment); a test environment's invoices never go into ERP.
        if (fullAuto && !testEntry && !query.IsProduction)
            outcome.Notes.Add($"CYInvoice 是{query.EnvironmentName}：發票照常開立帶入預覽，寄件單照常列印，但不會輸入 ERP。");

        var results = query.Orders.GroupBy(o => o.OrderId).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var toIssue = targets.Where(id => results.TryGetValue(id, out var r) && r.Status == "skipped").ToList();
        var existing = targets.Count(id => results.TryGetValue(id, out var r) && r.Status == "existing");
        if (toIssue.Count > 0)
        {
            var ask = $"CYInvoice：{query.EnvironmentName}（版本 {query.Version}）\n\n" +
                      $"查詢 {targets.Count} 筆：先前已開立 {existing} 筆、查無發票 {toIssue.Count} 筆、需人工 {targets.Count - existing - toIssue.Count} 筆。\n\n" +
                      $"要對查無發票的 {toIssue.Count} 筆開立發票嗎？" +
                      (query.IsProduction ? "\n\n【正式環境】會真的向光貿開立發票。" : string.Empty);
            // 全自動 asks nobody (user, 2026-10-15: the complete chain; ticking 「全自動呼叫 CYInvoice
            // 開立發票」 is the consent); the 測試 entry still asks.
            if (fullAuto) _log.Info("cyinvoice", $"full auto issues orders={toIssue.Count} environment={query.Environment}");
            if (!fullAuto && MessageBox.Show(this, ask, title, MessageBoxButtons.YesNo, query.IsProduction ? MessageBoxIcon.Warning : MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                _log.Info("cyinvoice", $"issue declined orders={toIssue.Count}");
                outcome.Notes.Add($"沒有開立：查無發票的 {toIssue.Count} 筆請手動處理。");
            }
            else
            {
                SetStatus($"CYInvoice：開立 {toIssue.Count} 筆發票…");
                var (issue, issueError) = await RunCyInvoiceAsync(exe, "issue", folder, file, toIssue, source: cySource);
                // No answer is never "not issued" (handoff): never sent again, checked by a query later.
                CyInvoiceOrder Unknown(string id, string why) => new(id, "unknown", "", "", "", "", -1, why, "");
                if (issue is null)
                {
                    foreach (var id in toIssue) results[id] = Unknown(id, $"開立沒有回傳結果：{issueError}；可能已開立，請勿重開，先在 CYInvoice 查詢");
                    outcome.Notes.Add($"CYInvoice 開立沒有回傳結果（{issueError}）。這 {toIssue.Count} 筆可能已開立，請勿重開；請在 CYInvoice 查詢（紀錄在 {folder}）。");
                }
                else if (issue.Environment != query.Environment)
                {
                    foreach (var id in toIssue) results[id] = Unknown(id, $"開立時 CYInvoice 變成{issue.EnvironmentName}");
                    outcome.Notes.Add($"CYInvoice 查詢時是{query.EnvironmentName}、開立時是{issue.EnvironmentName}，結果不使用，請在 CYInvoice 確認。");
                }
                else
                {
                    foreach (var r in issue.Orders) results[r.OrderId] = r;
                    // An unknown one is asked once more (query only).
                    var unknown = toIssue.Where(id => results.TryGetValue(id, out var r) && r.Status == "unknown").ToList();
                    if (unknown.Count > 0)
                    {
                        SetStatus($"CYInvoice：再查詢 {unknown.Count} 筆結果不明的發票…");
                        var (again, _) = await RunCyInvoiceAsync(exe, "query", folder, file, unknown, source: cySource);
                        if (again is not null && again.Environment == query.Environment)
                            foreach (var r in again.Orders.Where(r => r.Status == "existing")) results[r.OrderId] = r;
                    }
                }
            }
        }

        var filled = 0;
        foreach (var order in imported.Where(o => targets.Contains(o.OrderSn)))
        {
            if (!results.TryGetValue(order.OrderSn, out var r))
            {
                outcome.Notes.Add($"訂單 {order.OrderSn}：CYInvoice 的結果裡沒有這筆，發票請手動處理。");
                continue;
            }
            if (CyInvoiceLink.Mismatch(source[order.OrderSn], r) is { } why)
            {
                outcome.Notes.Add($"訂單 {order.OrderSn}：{why}，發票請人工確認。");
                continue;
            }
            CyInvoiceLink.Fill(order, r);
            filled++;
            if (CyInvoiceLink.IsPaper(r, cySource)) outcome.Paper.Add(r);
        }
        _log.Info("cyinvoice", $"done environment={query.Environment} targets={targets.Count} filled={filled} paper={outcome.Paper.Count}");
        outcome.Notes.Insert(0, $"CYInvoice（{query.EnvironmentName}）：{filled}／{targets.Count} 筆發票已帶入預覽" +
                                (outcome.Paper.Count > 0 ? $"，其中 {outcome.Paper.Count} 張有統編要印紙本。" : "。") +
                                (query.IsProduction ? string.Empty : "測試環境的發票不輸入 ERP。"));
        return outcome;
    }

    /// <summary>
    /// One CYInvoice call: a new request.json and result.json in <paramref name="folder"/> (kept
    /// for a person), then wait. No result (exit 2／3, timeout, unreadable) is an error, never
    /// "not issued".
    /// </summary>
    private async Task<(CyInvoiceResult? Result, string? Error)> RunCyInvoiceAsync(string exe, string mode, string folder, string file,
        IReadOnlyCollection<string> orderIds, (string Folder, int Style)? pdf = null, string source = CyInvoiceLink.Mo)
    {
        var id = Guid.NewGuid().ToString("D");
        var request = Path.Combine(folder, $"{mode}-{id}.request.json");
        var resultPath = Path.Combine(folder, $"{mode}-{id}.result.json");
        File.WriteAllText(request, CyInvoiceLink.Request(id, Path.GetFullPath(file), orderIds, pdf, source), new UTF8Encoding(false));
        _log.Info("cyinvoice", $"{mode} orders={orderIds.Count} request={Path.GetFileName(request)}");
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        start.ArgumentList.Add($"--autoinput-{mode}");
        start.ArgumentList.Add(request);
        start.ArgumentList.Add("--result");
        start.ArgumentList.Add(resultPath);
        Process? process;
        try { process = Process.Start(start); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _log.Warn("cyinvoice", $"{mode} start failed: {ex.Message}");
            return (null, $"CYInvoice 啟動失敗：{ex.Message}");
        }
        if (process is null) return (null, "CYInvoice 沒有啟動");
        using (process)
        {
            var timeout = CyInvoiceLink.Timeout(orderIds.Count);
            using var wait = new CancellationTokenSource(timeout);
            try { await process.WaitForExitAsync(wait.Token); }
            catch (OperationCanceledException)
            {
                // Only the waiting stops (handoff): CYInvoice may still finish on its own.
                _log.Warn("cyinvoice", $"{mode} timeout after {timeout.TotalSeconds:0}s");
                return (null, $"等待 CYInvoice 超過 {timeout.TotalMinutes:0} 分鐘");
            }
            var exit = process.ExitCode;
            _log.Info("cyinvoice", $"{mode} exit={exit} result={File.Exists(resultPath)}");
            if (!File.Exists(resultPath))
                return (null, exit switch
                {
                    2 => "請求或訂單檔有問題（結束碼 2）",
                    3 => "CYInvoice 目前無法安全處理（結束碼 3）",
                    _ => $"沒有結果檔（結束碼 {exit}）"
                });
            try
            {
                var result = CyInvoiceLink.ParseResult(File.ReadAllText(resultPath), id);
                _log.Info("cyinvoice", $"{mode} environment={result.Environment} version={result.Version} " +
                                       string.Join(" ", result.Orders.GroupBy(o => o.Status).Select(g => $"{g.Key}={g.Count()}")));
                return (result, null);
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
            {
                _log.Warn("cyinvoice", $"{mode} result unreadable: {ex.Message}");
                return (null, $"結果檔讀不懂：{ex.Message}");
            }
        }
    }

    // ---- 紙本發票: fetched in the background, printed when a person can load paper ----

    /// <summary>One paper invoice waiting for its PDF (光貿 finishes in its own time) or for printing.</summary>
    private sealed class PaperJob(MoInvoiceOutcome outcome, CyInvoiceOrder invoice)
    {
        public MoInvoiceOutcome Outcome { get; } = outcome;
        public CyInvoiceOrder Invoice { get; } = invoice;
        public DateTime Since { get; } = DateTime.Now;
        public string? Pdf { get; set; }
    }

    private readonly List<PaperJob> _paperJobs = [];
    private readonly List<string> _paperNotes = [];
    private System.Windows.Forms.Timer? _paperTimer;
    private DateTime _paperNextAsk = DateTime.MinValue;
    private bool _paperBusy;
    /// <summary>While ERP is being typed into: no paper prompt then.</summary>
    private int _paperHold;
    private static readonly TimeSpan PaperAskEvery = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PaperGiveUp = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The paper invoices of <paramref name="outcome"/> to the background queue (user, 2026-10-15:
    /// never holding up 全自動). Their PDFs are asked for every 20 seconds (<c>--autoinput-pdf</c>,
    /// the style from 設定 › 印表機) while the run goes on; once some are ready, the paper prompt
    /// comes at the next moment nobody is typing into ERP. One printed before is not queued.
    /// </summary>
    private void QueuePaperInvoices(MoInvoiceOutcome outcome)
    {
        if (outcome.Paper.Count == 0) return;
        var ledger = new PaperInvoiceLedger(PaperInvoiceLedger.DefaultPath);
        foreach (var r in outcome.Paper)
        {
            if (ledger.Printed(outcome.Environment, r.InvoiceNumber)) { _log.Info("cyinvoice", "paper invoice printed before; not queued"); continue; }
            if (_paperJobs.Any(j => j.Invoice.InvoiceNumber == r.InvoiceNumber && j.Outcome.Environment == outcome.Environment)) continue;
            _paperJobs.Add(new PaperJob(outcome, r));
        }
        _log.Info("cyinvoice", $"paper invoices queued={_paperJobs.Count}");
        _paperNextAsk = DateTime.MinValue;
        if (_paperTimer is null)
        {
            _paperTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _paperTimer.Tick += async (_, _) => await PaperTickAsync();
        }
        _paperTimer.Start();
    }

    /// <summary>
    /// Nobody is typing into ERP or driving Chrome, and no run holds the mouse: a paper prompt may
    /// come now (it gives the mouse back, a person loads paper). Between a run's steps the session
    /// stays on, so the prompt never cuts a run in two.
    /// </summary>
    private bool PaperMayPrompt => _paperHold == 0 && _automationCts is null && !_shopeeBatchRunning && _automationSession?.Active != true;

    private async Task PaperTickAsync()
    {
        if (_paperBusy) return;
        _paperBusy = true;
        try
        {
            if (DateTime.Now >= _paperNextAsk && _paperJobs.Any(j => j.Pdf is null)) await AskPaperPdfsAsync();
            if (PaperMayPrompt) await PrintReadyPaperAsync(null);
            if (PaperMayPrompt && _paperNotes.Count > 0 && _paperJobs.Count == 0)
            {
                var notes = string.Join("\n", _paperNotes);
                _paperNotes.Clear();
                MessageBox.Show(Form.ActiveForm ?? this, notes, "紙本發票", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _paperBusy = false;
            if (_paperJobs.Count == 0 && _paperNotes.Count == 0) _paperTimer?.Stop();
        }
    }

    /// <summary>One round of <c>--autoinput-pdf</c> for the jobs without a PDF, per run.</summary>
    private async Task AskPaperPdfsAsync()
    {
        _paperNextAsk = DateTime.Now + PaperAskEvery;
        var style = _settings.PaperInvoiceStyle;
        foreach (var group in _paperJobs.Where(j => j.Pdf is null).GroupBy(j => j.Outcome).ToList())
        {
            var outcome = group.Key;
            var jobs = group.ToList();
            var pdfFolder = Path.Combine(outcome.Folder, "paper");
            Directory.CreateDirectory(pdfFolder);
            var (answer, error) = await RunCyInvoiceAsync(outcome.Exe, "pdf", outcome.Folder, outcome.File, jobs.Select(j => j.Invoice.OrderId).ToList(), (pdfFolder, style), outcome.Source);
            if (answer is null || answer.Environment != outcome.Environment)
            {
                var why = answer is null ? error : $"CYInvoice 變成{answer.EnvironmentName}";
                foreach (var j in jobs) Drop(j, $"沒有拿到 PDF（{why}）");
                continue;
            }
            var byId = answer.Orders.GroupBy(o => o.OrderId).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
            foreach (var j in jobs)
            {
                byId.TryGetValue(j.Invoice.OrderId, out var a);
                // The same invoice as issued: number and amount checked again (CYInvoice V2.6.21 handoff).
                if (a is { Status: "ready" } && a.InvoiceNumber == j.Invoice.InvoiceNumber && a.TotalAmount == j.Invoice.TotalAmount &&
                    a.PdfFile.Length > 0 && File.Exists(a.PdfFile)) j.Pdf = a.PdfFile;
                else if (a is { Status: "ready" }) Drop(j, $"PDF 的發票（{a.InvoiceNumber}，{a.TotalAmount} 元）與開立結果不同，沒有印");
                else if (a is { Status: "pending" })
                {
                    if (DateTime.Now - j.Since > PaperGiveUp) Drop(j, $"光貿 {PaperGiveUp.TotalMinutes:0} 分鐘還沒開立完成，沒有拿到 PDF");
                }
                else Drop(j, $"沒有拿到 PDF（{(a is null ? "結果沒有這筆" : $"{CyInvoiceLink.StatusName(a.Status)} {a.Message}".Trim())}）");
            }
        }
        _log.Info("cyinvoice", $"paper pdfs ready={_paperJobs.Count(j => j.Pdf is not null)} waiting={_paperJobs.Count(j => j.Pdf is null)}");
    }

    /// <summary>Every queued paper invoice dropped (Esc): listed for CYInvoice, never printed later.</summary>
    private void CancelPaperInvoices(string why)
    {
        foreach (var job in _paperJobs.ToList()) Drop(job, $"{why}，沒有印");
    }

    private void Drop(PaperJob job, string why)
    {
        _paperJobs.Remove(job);
        _paperNotes.Add($"紙本發票 {job.Invoice.InvoiceNumber}（訂單 {job.Invoice.OrderId}）{why}，請在 CYInvoice 列印。");
        _log.Warn("cyinvoice", $"paper invoice dropped: {why}");
    }

    /// <summary>
    /// The paper invoices whose PDF is ready: the prompt for plain A4 paper, then the A4 printer.
    /// 停止 leaves them for CYInvoice; a printer error is listed (no stop). <paramref name="printed"/>
    /// takes the lines for a run's result when given, else they go to the later notice.
    /// </summary>
    private async Task PrintReadyPaperAsync(List<string>? printed)
    {
        var ready = _paperJobs.Where(j => j.Pdf is not null).ToList();
        if (ready.Count == 0) return;
        var report = printed ?? _paperNotes;
        var printer = SlipPrinter.Installed(_settings.A4Printer);
        if (printer is null)
        {
            foreach (var j in ready) Drop(j, "沒有印：找不到 A4 印表機（設定 › 印表機）");
            return;
        }
        var styleName = CyInvoiceLink.PaperStyles.FirstOrDefault(p => p.Code == _settings.PaperInvoiceStyle).Name ?? string.Empty;
        _log.Info("cyinvoice", $"paper invoices wait for paper count={ready.Count}");
        Alarm(needsPerson: true);
        // Which kinds are in the pile (user, 2026-10-19: a 酷澎 consumer's invoice was described as
        // 有統編): with 統編 in the set style; without (酷澎) always A4, both sides.
        var withBan = ready.Count(j => j.Invoice.BuyerBan.Length > 0);
        var kinds = new List<string>();
        if (withBan > 0) kinds.Add($"有統編 {withBan} 張（{styleName}）");
        if (ready.Count > withBan) kinds.Add($"無統編 {ready.Count - withBan} 張（A4 雙面）");
        using (var prompt = new PaperPromptForm("A4 白紙（紙本發票）", printer, $"共 {ready.Count} 張發票：{string.Join("、", kinds)}"))
            if (prompt.ShowDialog(Form.ActiveForm ?? this) != DialogResult.OK)
            {
                foreach (var j in ready) Drop(j, "在放紙提醒按了停止，沒有印");
                return;
            }
        var ledger = new PaperInvoiceLedger(PaperInvoiceLedger.DefaultPath);
        foreach (var j in ready)
        {
            _paperJobs.Remove(j);
            var r = j.Invoice;
            try
            {
                var pages = await MoSlipPdf.RenderPagesAsync(j.Pdf!, string.Empty, 300);
                // A consumer's paper invoice (酷澎) goes on both sides, long edge (CYInvoice 2026-10-15).
                var duplex = j.Outcome.Source == CyInvoiceLink.Coupang && r.BuyerBan.Length == 0;
                _log.Info("cyinvoice", $"paper invoice pdf pages={pages.Count} duplex={duplex}");
                try { await Task.Run(() => SlipPrinter.PrintPages(printer, pages, $"發票 {r.InvoiceNumber}", duplex)); }
                finally { foreach (var page in pages) page.Dispose(); }
                ledger.Record(j.Outcome.Environment, r.InvoiceNumber);
                printed?.Add($"紙本發票 {r.InvoiceNumber}");
                _log.Info("cyinvoice", $"paper invoice printed pages={pages.Count}");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                _log.Warn("cyinvoice", $"paper invoice print failed: {ex.GetType().Name}: {ex.Message}");
                report.Add($"紙本發票 {r.InvoiceNumber} 列印失敗（{ex.Message}），請在 CYInvoice 重印。");
            }
        }
    }

    /// <summary>
    /// MO店+ › 匯入訂單匯出檔 (user, 2026-10-19: the former 測試：發票 replaces the plain import, it
    /// is the more complete one): the two 訂單匯出檔 → CYInvoice queries, and issues the orders
    /// without an invoice after asking → the paper invoices print in the background → the preview
    /// → ERP. Orders CYInvoice already issued keep their invoice number (a rerun issues nothing).
    /// </summary>
    private async Task ImportMoAsync()
    {
        const string title = "MO店+ 訂單匯入";
        if (_automationCts is not null || _shopeeBatchRunning) return;
        if (!ErpWindowReady()) return;
        var missing = new[]
        {
            _settings.MoOrderType.Length == 0 ? "MO店+ 銷貨單別" : null,
            _settings.MoCustomerCode.Length == 0 ? "MO店+ 客戶代號" : null,
            _settings.DiscountItemCode.Length == 0 ? "折價券品號" : null,
            _settings.ShippingItemCode.Length == 0 ? "運費品號" : null
        }.Where(x => x is not null).ToList();
        if (missing.Count > 0)
        {
            MessageBox.Show(this, $"請先在「設定」填入{string.Join("、", missing)}。", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_settings.CyInvoicePath.Length == 0 || !File.Exists(_settings.CyInvoicePath))
        {
            using var dialog = new OpenFileDialog { Title = "選擇 CYInvoice.exe", Filter = "CYInvoice 程式 (CYInvoice.exe)|CYInvoice.exe|所有程式 (*.exe)|*.exe", CheckFileExists = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _settings.CyInvoicePath = dialog.FileName;
            _settingsStore.Save(_settings);
        }

        var password = LocalSecret.Unprotect(_settings.MoPasswordProtected);
        List<string[]> Read(string path)
        {
            using var stream = File.OpenRead(path);
            return Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? XlsxReader.ReadFirstSheet(stream)
                : XlsReader.ReadFirstSheet(stream, password);
        }
        using var files = new MoImportForm(Read);
        if (files.ShowDialog(this) != DialogResult.OK || files.BatchPath is null) return;
        var mapping = new MoMapping(_settings.MoOrderType, _settings.MoCustomerCode, _settings.MoNotePrefix,
            _settings.DiscountItemCode, _settings.ShippingItemCode, _settings.FreightTypes, _settings.PromotionItemCode);
        var orders = files.Orders.Select(o => MoOrderImport.ToImported(o, mapping)).ToList();
        _log.Info("mo", $"exports orders={files.Orders.Count} errors={files.Errors.Count} tracking_filled={files.Orders.Count(o => o.TrackingNumber.Length > 0)} notes={files.Notes.Count}");

        var invoice = await InvoiceMoAsync(title, files.BatchPath, files.Orders, orders, fullAuto: false);
        // In the background: the paper prompt comes once 光貿 has the PDFs, over the preview.
        QueuePaperInvoices(invoice);
        SetStatus(string.Empty);
        // ERP whatever CYInvoice's environment (user, 2026-10-15: they switch it); the
        // environment is shown when asking before issuing and in the preview's notes.
        await PreviewAndRunAsync(MoOrderImport.Source, files.FileName, orders, files.Errors, [.. invoice.Notes, .. files.Notes]);
    }

    /// <summary>
    /// 酷澎 › 全自動 (user, 2026-10-19 screenshots): Wing in a Chrome window of its own — 訂單確認
    /// accepted, 配送準備 arranged per 配送通路 with the 寄件單 printed on the thermal printer,
    /// 配送指示's 下載Excel into Temp (<see cref="ShopeeBatchRunner.RunCoupangAsync"/>) — then
    /// CYInvoice's 酷澎 entry issues (always paper; printed in the background), and every order
    /// clean goes straight into ERP, otherwise the preview.
    /// </summary>
    private async Task RunCoupangBatchAsync()
    {
        const string title = "酷澎全自動";
        if (_automationCts is not null || _shopeeBatchRunning) return;
        if (!ErpWindowReady()) return;
        var missing = new[]
        {
            _settings.CoupangOrderType.Length == 0 ? "酷澎銷貨單別" : null,
            _settings.CoupangCustomerCode.Length == 0 ? "酷澎客戶代號" : null,
            _settings.DiscountItemCode.Length == 0 ? "折價券品號" : null,
            _settings.ShippingItemCode.Length == 0 ? "運費品號" : null,
            _settings.CyInvoicePath.Length == 0 || !File.Exists(_settings.CyInvoicePath) ? "CYInvoice.exe" : null,
            _settings.ShopeeLabelPrinter.Length == 0 ? "熱感標籤印表機（寄件單）" : null,
            _settings.A4Printer.Length == 0 ? "A4 印表機（紙本發票）" : null,
            ShopeeScreenshot.Compact(_settings.HctPickupAddress).Length < 6 ? "新竹物流取件地址（平台匯入 › 蝦皮）" : null
        }.Where(x => x is not null).ToList();
        if (missing.Count > 0)
        {
            MessageBox.Show(this, $"請先在「設定」填入{string.Join("、", missing)}。", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this,
                "程式會開自己的 Chrome 視窗到酷澎 Wing「我的訂單」（請先在 Chrome 登入 Wing）：\n\n" +
                "1. 訂單確認：全選 → 確認訂單 → 核對訂單編號 → 確認\n" +
                "2. 配送準備：依配送通路選配送方式 → 全選 → 批次安排配送 → 核對取貨地址 → 提交 → 列印寄件單（熱感機，宅配 " + _settings.CoupangHctLabelScale + "%、超商 100%）\n" +
                "3. 配送指示：全選 → 下載 Excel\n" +
                "4. CYInvoice 開立發票（酷澎一律紙本，拿到 PDF 後請你放 A4 白紙）\n" +
                "5. 全部沒問題就直接輸入 ERP，否則開預覽\n\n" +
                "確認訂單、安排配送後無法自動復原。網頁操作期間請勿操作電腦，按 Esc 可停止。要開始嗎？",
                title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        MoPrintedDocuments.Prune(_log);
        var folder = Path.Combine(MoPrintedDocuments.TempRoot, "coupang-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
        _shopeeBatchRunning = true;
        Session.Pin(AutomationSession.BatchTitle);
        Session.Begin(AutomationSession.BatchTitle);
        CoupangBatchResult? result = null;
        string? stopped = null;
        var shipped = false;
        var runner = new ShopeeBatchRunner(_automation.Ocr, _log,
            new ShopeeBatchPrint(_settings.ShopeeLabelPrinter, _settings.ShopeeLabelScale, _settings.SevenLabelScale, _settings.FamilyLabelScale, _settings.FamilyLabelPages,
                _settings.HctLabelScale, _settings.HctPickupAddress, _settings.CoupangHctLabelScale), Session.EscapeToken);
        try { result = await runner.RunCoupangAsync(folder, SetStatus); }
        catch (ShopeeBatchStop stop) { stopped = stop.Message; shipped = stop.Shipped; }
        catch (OperationCanceledException) { stopped = "已按 Esc 停止。"; }
        finally
        {
            // Straight on to CYInvoice and ERP (no 「已暫停」 in between).
            Session.Unpin();
            if (stopped is not null || result?.Excel is null) Session.End(stopped is null ? AutomationOutcome.Done : AutomationOutcome.Stopped);
            _shopeeBatchRunning = false;
        }
        if (stopped is not null)
        {
            _log.Warn("coupang-batch", $"stopped: {stopped}");
            Alarm(needsPerson: true);
            MessageBox.Show(this, $"酷澎全自動停止：{stopped}" + (shipped ? "\n\n已確認或安排配送的訂單請到 Wing 確認；寄件單與打單可在配送指示下載 Excel 後用「匯入訂單匯出檔」接續。" : string.Empty),
                title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (result!.Excel is not { } file)
        {
            MessageBox.Show(this, $"已確認 {result.Accepted.Count} 筆、安排配送 {result.Arranged.Count} 筆；{string.Join(" ", result.Notes)}", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var runNotes = new List<string>(result.Notes);
        if (result.Accepted.Count > 0 || result.Arranged.Count > 0)
            runNotes.Insert(0, $"Wing：確認訂單 {result.Accepted.Count} 筆、安排配送並列印寄件單 {result.Arranged.Count} 筆。");
        await CoupangToErpAsync(title, file, runNotes);
    }

    /// <summary>酷澎 › 列印寄件單 (user, 2026-10-20): the Wing 寄件詳情 the user opened, printed.</summary>
    private async Task PrintCoupangSlipAsync()
    {
        const string title = "酷澎列印寄件單";
        if (_automationCts is not null || _shopeeBatchRunning) return;
        if (_settings.ShopeeLabelPrinter.Length == 0)
        {
            MessageBox.Show(this, "請先在「設定 › 印表機」勾選熱感標籤印表機。", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _shopeeBatchRunning = true;
        Session.Pin(AutomationSession.BatchTitle);
        Session.Begin(AutomationSession.BatchTitle);
        string? done = null, stopped = null;
        var runner = new ShopeeBatchRunner(_automation.Ocr, _log,
            new ShopeeBatchPrint(_settings.ShopeeLabelPrinter, _settings.ShopeeLabelScale, _settings.SevenLabelScale, _settings.FamilyLabelScale, _settings.FamilyLabelPages,
                _settings.HctLabelScale, _settings.HctPickupAddress, _settings.CoupangHctLabelScale), Session.EscapeToken);
        try { done = await runner.PrintOpenCoupangSlipAsync(); }
        catch (ShopeeBatchStop stop) { stopped = stop.Message; }
        catch (OperationCanceledException) { stopped = "已按 Esc 停止。"; }
        finally
        {
            Session.Unpin();
            Session.End(stopped is null ? AutomationOutcome.Done : AutomationOutcome.Stopped);
            _shopeeBatchRunning = false;
        }
        if (stopped is not null)
        {
            _log.Warn("coupang-batch", $"print only stopped: {stopped}");
            Alarm(needsPerson: true);
            MessageBox.Show(this, $"酷澎列印寄件單停止：{stopped}", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        SetStatus(done!);
    }

    /// <summary>A downloaded 酷澎 DeliveryList → CYInvoice (酷澎 entry, paper in the background) → ERP or the preview.</summary>
    private async Task CoupangToErpAsync(string title, string file, List<string> runNotes)
    {
        CoupangImportResult parsed;
        try
        {
            await using var stream = File.OpenRead(file);
            parsed = CoupangOrderImport.Parse(XlsxReader.ReadFirstSheet(stream));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            _log.Warn("coupang-batch", $"export read failed: {ex.GetType().Name}");
            Alarm(needsPerson: true);
            MessageBox.Show(this, $"下載的 Excel 讀不到：{ex.Message}\n檔案在：{file}", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _log.Info("coupang-batch", $"export parsed orders={parsed.Orders.Count} errors={parsed.Errors.Count}");
        if (parsed.Orders.Count == 0 && parsed.Errors.Count == 0)
        {
            Session.End(AutomationOutcome.Done);
            MessageBox.Show(this, "下載的 Excel 沒有訂單。", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var mapping = new CoupangMapping(_settings.CoupangOrderType, _settings.CoupangCustomerCode, _settings.CoupangNotePrefix,
            _settings.DiscountItemCode, _settings.ShippingItemCode, _settings.FreightTypes, _settings.PromotionItemCode);
        var orders = parsed.Orders.Select(o => CoupangOrderImport.ToImported(o, mapping)).ToList();
        var ledger = LoadLedger();
        if (ledger is null) { Session.End(AutomationOutcome.Stopped); return; }
        // 配送指示 keeps an order until the carrier picks it up, so an earlier run's orders come
        // again: the ones already in ERP are left out (their invoice is not asked for again).
        var entered = orders.Where(o => ledger.Find(o.LedgerKey) is not null).ToList();
        if (entered.Count > 0)
        {
            orders = orders.Except(entered).ToList();
            runNotes.Add($"{entered.Count} 筆先前已輸入 ERP（配送指示仍列著），這次略過：{string.Join("、", entered.Select(o => o.OrderSn))}");
            _log.Info("coupang-batch", $"already entered skipped={entered.Count}");
        }
        if (orders.Count == 0 && parsed.Errors.Count == 0)
        {
            Session.End(AutomationOutcome.Done);
            MessageBox.Show(this, string.Join("\n", runNotes), title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var keep = orders.Select(o => o.OrderSn).ToHashSet(StringComparer.Ordinal);
        var expect = parsed.Orders.Where(o => keep.Contains(o.OrderSn)).GroupBy(o => o.OrderSn)
            .ToDictionary(g => g.Key, g => InvoiceExpectation.Of(g.First()), StringComparer.Ordinal);
        var invoice = await InvoiceAsync(title, CyInvoiceLink.Coupang, file, expect, orders, fullAuto: true, testEntry: true);
        QueuePaperInvoices(invoice);
        // A test environment's invoices are not entered without a look (the preview opens).
        var production = invoice.Environment == CyInvoiceLink.Production;
        if (!production && invoice.Environment.Length > 0)
            runNotes.Add($"CYInvoice 是{CyInvoiceLink.EnvironmentName(invoice.Environment)}：不直接輸入 ERP，請在預覽確認。");
        if (EscPressed)
        {
            CancelPaperInvoices("已按 Esc 停止");
            MessageBox.Show(this, $"已按 Esc 停止：發票已處理，尚未輸入 ERP。檔案在：{file}", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var notes = new List<string>([.. runNotes, .. invoice.Notes]);
        var clean = production && _settings.AutoSave && orders.Count > 0 && parsed.Errors.Count == 0 &&
                    orders.All(o => o.HandoffReason.Length == 0 && o.InvoiceNo.Trim().Length > 0 && o.ValidationError().Length == 0 &&
                                    !o.MissingInvoiceName && ledger.Find(o.LedgerKey) is null);
        if (clean)
        {
            _log.Info("coupang-batch", $"all {orders.Count} orders ready; no preview");
            _paperHold++;
            try { if (await RunImportBatchAsync(CoupangOrderImport.Source, orders, ledger, notes)) return; }
            finally { _paperHold--; }
        }
        SetStatus($"{title}：請在預覽確認");
        Alarm(needsPerson: true);
        await PreviewAndRunAsync(CoupangOrderImport.Source, Path.GetFileName(file), orders, parsed.Errors, notes);
    }
}
