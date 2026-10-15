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
    public List<string> Notes { get; } = [];
    /// <summary>Filled orders whose buyer has 統編: a paper invoice to print.</summary>
    public List<CyInvoiceOrder> Paper { get; } = [];
    public bool IsProduction => Environment == CyInvoiceLink.Production;
}

/// <summary>
/// MO店+ 發票 through CYInvoice (user, 2026-10-15; contract in docs/CYINVOICE_IMPLEMENTATION_HANDOFF.md).
/// CYInvoice is asked first (query, never issues), which also tells its environment: 全自動
/// takes production only, so a test CYInvoice is found out before anything is issued. Only the
/// orders it has no invoice for are then issued, after the user says so; every answer is checked
/// (number, date, amount, 統編) before it goes on the preview, and anything else stays for a
/// person. A paper invoice (a buyer with 統編; a consumer's goes to the member carrier) is printed
/// on the A4 printer from CYInvoice's official PDF, asked for only when printing: 光貿 finishes an
/// invoice in its own time and the PDF exists only then (user, 2026-10-15), in the style chosen in
/// 設定 › 印表機.
/// </summary>
internal sealed partial class MainForm
{
    private async Task<MoInvoiceOutcome> InvoiceMoAsync(string title, string file, IReadOnlyList<MoOrder> mo, IReadOnlyList<ImportedOrder> imported, bool fullAuto)
    {
        var outcome = new MoInvoiceOutcome();
        var exe = _settings.CyInvoicePath;
        if (exe.Length == 0 || !File.Exists(exe))
        {
            outcome.Notes.Add("找不到 CYInvoice.exe（設定 › 平台匯入 › CYInvoice），發票請手動填。");
            return outcome;
        }
        var source = mo.GroupBy(o => o.OrderSn).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
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
        var (query, queryError) = await RunCyInvoiceAsync(exe, "query", folder, file, targets);
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
        if (fullAuto && !query.IsProduction)
        {
            outcome.Notes.Add($"CYInvoice 目前是{query.EnvironmentName}，全自動只接受正式環境，沒有開立；發票請手動填。");
            return outcome;
        }

        var results = query.Orders.GroupBy(o => o.OrderId).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var toIssue = targets.Where(id => results.TryGetValue(id, out var r) && r.Status == "skipped").ToList();
        var existing = targets.Count(id => results.TryGetValue(id, out var r) && r.Status == "existing");
        if (toIssue.Count > 0)
        {
            var ask = $"CYInvoice：{query.EnvironmentName}（版本 {query.Version}）\n\n" +
                      $"查詢 {targets.Count} 筆：先前已開立 {existing} 筆、查無發票 {toIssue.Count} 筆、需人工 {targets.Count - existing - toIssue.Count} 筆。\n\n" +
                      $"要對查無發票的 {toIssue.Count} 筆開立發票嗎？" +
                      (query.IsProduction ? "\n\n【正式環境】會真的向光貿開立發票。" : string.Empty);
            if (MessageBox.Show(this, ask, title, MessageBoxButtons.YesNo, query.IsProduction ? MessageBoxIcon.Warning : MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                _log.Info("cyinvoice", $"issue declined orders={toIssue.Count}");
                outcome.Notes.Add($"沒有開立：查無發票的 {toIssue.Count} 筆請手動處理。");
            }
            else
            {
                SetStatus($"CYInvoice：開立 {toIssue.Count} 筆發票…");
                var (issue, issueError) = await RunCyInvoiceAsync(exe, "issue", folder, file, toIssue);
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
                        var (again, _) = await RunCyInvoiceAsync(exe, "query", folder, file, unknown);
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
            if (CyInvoiceLink.IsPaper(r)) outcome.Paper.Add(r);
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
        IReadOnlyCollection<string> orderIds, (string Folder, int Style)? pdf = null)
    {
        var id = Guid.NewGuid().ToString("D");
        var request = Path.Combine(folder, $"{mode}-{id}.request.json");
        var resultPath = Path.Combine(folder, $"{mode}-{id}.result.json");
        File.WriteAllText(request, CyInvoiceLink.Request(id, Path.GetFullPath(file), orderIds, pdf), new UTF8Encoding(false));
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

    /// <summary>
    /// The paper invoices on the A4 printer: their official PDFs fetched from CYInvoice first
    /// (<c>--autoinput-pdf</c>, the style from 設定 › 印表機), asked again while 光貿 has not
    /// finished; then a prompt for plain A4 paper, then printing. One printed before (same
    /// environment and number) is not printed again. False only for 停止 on the prompt; a missing
    /// PDF or a printer error is listed and the run goes on.
    /// </summary>
    private async Task<bool> PrintPaperInvoicesAsync(MoInvoiceOutcome outcome, string printer, List<string> printed)
    {
        if (outcome.Paper.Count == 0) return true;
        var ledger = new PaperInvoiceLedger(PaperInvoiceLedger.DefaultPath);
        var wanted = new List<CyInvoiceOrder>();
        foreach (var r in outcome.Paper)
            if (ledger.Printed(outcome.Environment, r.InvoiceNumber)) printed.Add($"紙本發票 {r.InvoiceNumber} 先前已印過，未重印");
            else wanted.Add(r);
        if (wanted.Count == 0) return true;

        var style = _settings.PaperInvoiceStyle;
        var styleName = CyInvoiceLink.PaperStyles.FirstOrDefault(p => p.Code == style).Name ?? style.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var pdfFolder = Path.Combine(outcome.Folder, "paper");
        Directory.CreateDirectory(pdfFolder);
        var ready = new List<(CyInvoiceOrder Invoice, string Pdf)>();
        var pending = wanted.ToList();
        var watch = Stopwatch.StartNew();
        var limit = TimeSpan.FromMinutes(5);
        while (pending.Count > 0)
        {
            SetStatus($"取得紙本發票 PDF（{pending.Count} 張，{styleName}）…");
            var (answer, error) = await RunCyInvoiceAsync(outcome.Exe, "pdf", outcome.Folder, outcome.File, pending.Select(p => p.OrderId).ToList(), (pdfFolder, style));
            if (answer is null || answer.Environment != outcome.Environment)
            {
                var why = answer is null ? error : $"CYInvoice 變成{answer.EnvironmentName}";
                foreach (var r in pending) printed.Add($"紙本發票 {r.InvoiceNumber}（訂單 {r.OrderId}）沒有拿到 PDF（{why}），請在 CYInvoice 列印");
                break;
            }
            var byId = answer.Orders.GroupBy(o => o.OrderId).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
            var still = new List<CyInvoiceOrder>();
            foreach (var r in pending)
            {
                byId.TryGetValue(r.OrderId, out var a);
                if (a is { Status: "ready" } && a.InvoiceNumber == r.InvoiceNumber && a.PdfFile.Length > 0 && File.Exists(a.PdfFile)) ready.Add((r, a.PdfFile));
                else if (a is { Status: "pending" }) still.Add(r);
                else printed.Add($"紙本發票 {r.InvoiceNumber}（訂單 {r.OrderId}）沒有拿到 PDF（{(a is null ? "結果沒有這筆" : $"{CyInvoiceLink.StatusName(a.Status)} {a.Message}".Trim())}），請在 CYInvoice 列印");
            }
            pending = still;
            if (pending.Count == 0) break;
            if (watch.Elapsed > limit)
            {
                if (MessageBox.Show(this, $"還有 {pending.Count} 張發票光貿尚未開立完成，拿不到 PDF（已等 {watch.Elapsed.TotalMinutes:0} 分鐘）。\n\n要再等 5 分鐘嗎？按「否」會先印已拿到的，其餘請之後在 CYInvoice 列印。",
                        "紙本發票", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    foreach (var r in pending) printed.Add($"紙本發票 {r.InvoiceNumber}（訂單 {r.OrderId}）光貿尚未完成、沒有拿到 PDF，請之後在 CYInvoice 列印");
                    break;
                }
                watch.Restart();
            }
            SetStatus($"等待光貿開立完成（{pending.Count} 張），稍後再取 PDF…");
            await Task.Delay(TimeSpan.FromSeconds(20));
        }
        if (ready.Count == 0) return true;

        _log.Info("cyinvoice", $"paper invoices wait for paper count={ready.Count} style={style}");
        Alarm(needsPerson: true);
        using (var prompt = new PaperPromptForm("A4 白紙（紙本發票）", printer, $"共 {ready.Count} 張發票（有統編抬頭的訂單，{styleName}）"))
            if (prompt.ShowDialog(this) != DialogResult.OK) return false;
        foreach (var (r, pdf) in ready)
        {
            SetStatus($"列印紙本發票 {r.InvoiceNumber}…");
            try
            {
                var pages = await MoSlipPdf.RenderPagesAsync(pdf, string.Empty, 300);
                try { await Task.Run(() => SlipPrinter.PrintPages(printer, pages, $"發票 {r.InvoiceNumber}")); }
                finally { foreach (var page in pages) page.Dispose(); }
                ledger.Record(outcome.Environment, r.InvoiceNumber);
                printed.Add($"紙本發票 {r.InvoiceNumber}");
                _log.Info("cyinvoice", $"paper invoice printed pages={pages.Count}");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                _log.Warn("cyinvoice", $"paper invoice print failed: {ex.GetType().Name}: {ex.Message}");
                printed.Add($"紙本發票 {r.InvoiceNumber} 列印失敗（{ex.Message}），請在 CYInvoice 重印");
            }
        }
        return true;
    }

    /// <summary>
    /// MO店+ › 測試：發票 (user, 2026-10-15): a real 訂單匯出檔 → CYInvoice (the user opens its test
    /// environment) issues and answers → the paper invoices print → the preview, where it stops:
    /// a test environment's invoices do not go into ERP.
    /// </summary>
    private async Task TestMoInvoiceAsync()
    {
        const string title = "MO店+ 發票測試";
        if (_automationCts is not null || _shopeeBatchRunning) return;
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
            using var dialog = new OpenFileDialog { Title = "選擇 CYInvoice.exe", Filter = "CYInvoice.exe|CYInvoice.exe|程式 (*.exe)|*.exe", CheckFileExists = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _settings.CyInvoicePath = dialog.FileName;
            _settingsStore.Save(_settings);
        }
        if (MessageBox.Show(this,
                "請先開好 CYInvoice 的測試環境。\n\n" +
                "1. 放入 MO店+ 訂單匯出檔（出貨前那份；出貨後那份可省略）\n" +
                "2. 程式呼叫 CYInvoice 先查詢，再對查無發票的訂單開立（開立前會再問一次，並顯示 CYInvoice 的環境）\n" +
                "3. 有統編抬頭的訂單印紙本發票（A4 印表機，印前會請你放紙）；一般消費者是會員載具，不印\n" +
                "4. 停在預覽畫面，發票資料已帶入；測試環境的發票不會輸入 ERP\n\n" +
                $"CYInvoice：{_settings.CyInvoicePath}\n\n要開始嗎？",
                title, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
            return;

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
        _log.Info("cyinvoice", $"test orders={orders.Count}");

        var invoice = await InvoiceMoAsync(title, files.BatchPath, files.Orders, orders, fullAuto: false);
        var printed = new List<string>();
        if (invoice.Paper.Count > 0)
        {
            var a4 = SlipPrinter.Installed(_settings.A4Printer);
            if (a4 is null) printed.Add("沒有設定或找不到 A4 印表機（設定 › 印表機），紙本發票沒有印");
            else if (!await PrintPaperInvoicesAsync(invoice, a4, printed)) printed.Add("已在放紙提醒按停止，紙本發票沒有印");
        }
        SetStatus(string.Empty);
        await PreviewAndRunAsync(MoOrderImport.Source, files.FileName, orders, files.Errors, [.. invoice.Notes, .. printed, .. files.Notes],
            () =>
            {
                if (invoice.IsProduction) return Task.FromResult(true);
                MessageBox.Show(this, "這是發票測試：CYInvoice 不是正式環境，發票不輸入 ERP。", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return Task.FromResult(false);
            });
    }
}
