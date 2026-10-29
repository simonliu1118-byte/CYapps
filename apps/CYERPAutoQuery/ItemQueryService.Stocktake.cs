using System.Drawing;

namespace CYERPAutoQuery;

/// <summary>
/// 庫存盤點 reading (WO-1.3.0, user's screenshots 2026-10-29): INVR11 品號盤點清單 through 作業速查 →
/// 基本選項 「選擇盤點底稿編號」 → F2 (「F2開窗查詢」 lists 底稿編號／盤點日期) → pick the sheet → 確定 →
/// 選擇列印 「二者皆列印」 → 即時報表 → the report's first toolbar button opens 欄位資訊, where a
/// single click on N turns 儲位／批號／盤點數量 to Y → the usual XLSX export. Read-only.
/// </summary>
internal sealed partial class ItemQueryService
{
    // Tabs 64 px wide, the first centred about 48 px into the page control (screenshot 104).
    private static readonly ErpReport StocktakeReport = new(
        "INVR11", "品號盤點清單", ["基本選項", "進階選項", "系統選項"],
        new TabStrip(["基本選項", "進階選項", "系統選項"], Origin: 16, Pitch: 64));

    private const int F2WaitMs = 10000;
    private static readonly (string Name, string Order)[] StocktakeColumns = [("儲位", "7"), ("批號", "11"), ("盤點數量", "14")];

    /// <summary>
    /// Every 盤點底稿 in the F2 list. The condition window stays open for the sheets read next
    /// (user 2026-10-29: INVR11 was opened twice); <see cref="CloseStocktakeReportAsync"/> closes it.
    /// </summary>
    public Task<IReadOnlyList<StocktakeSheet>> ListStocktakeSheetsAsync(IProgress<string> progress, CancellationToken cancellationToken) =>
        GuardErpMessagesAsync(async () =>
        {
            var report = StocktakeReport;
            await CloseReportWindowsAsync(report, progress, leftover: true);
            progress.Report("ERP：開啟品號盤點清單（作業速查）…");
            await OpenFormAsync(report.Code, report.Title, () => FindReportConditions(report).Count > 0, cancellationToken);
            var found = FindReportConditions(report);
            if (found.Count != 1)
                throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"開啟 INVR11 後找到 {found.Count} 個品號盤點清單條件視窗，已停止。");
            try
            {
                progress.Report("ERP：讀取盤點底稿清單（F2）…");
                var (popup, sheets) = await OpenStocktakeF2Async(found[0], cancellationToken);
                await ClickButtonAsync(popup, "取消", "「F2開窗查詢」的「取消」", cancellationToken);
                await WaitUntilAsync(() => !NativeMethods.IsWindow(popup) || !NativeMethods.IsWindowVisible(popup), 3000, cancellationToken);
                _log.Info("stocktake", $"F2 sheets={sheets.Count}");
                return (IReadOnlyList<StocktakeSheet>)sheets.Select(s => s.Sheet).ToList();
            }
            catch
            {
                await CloseStocktakeReportAsync();
                throw;
            }
        });

    /// <summary>Closes INVR11 (condition window and report) left by the list or a failure.</summary>
    public async Task CloseStocktakeReportAsync()
    {
        if (!await CloseReportWindowsAsync(StocktakeReport, null, leftover: false))
            _log.Warn("stocktake", "INVR11 windows left open");
    }

    /// <summary>One sheet's lines from the INVR11 report.</summary>
    public Task<StocktakeDetail> ReadStocktakeSheetAsync(string sheetNo, IProgress<string> progress, CancellationToken cancellationToken) =>
        GuardErpMessagesAsync(async () =>
        {
            var sheets = await RunReportAsync(StocktakeReport, $"盤點底稿 {sheetNo}",
                (dialog, ct) => FillStocktakeReportAsync(dialog, sheetNo, ct), progress, cancellationToken,
                prepare: ShowStocktakeColumnsAsync, reuseConditions: true)
                ?? throw new InvalidDataException($"INVR11 盤點底稿 {sheetNo} 沒有資料。");
            var detail = StocktakeQuery.ParseExport(sheetNo, sheets);
            _log.Info("stocktake", $"INVR11 sheet={sheetNo} lines={detail.Rows.Count}");
            return detail;
        });

    /// <summary>基本選項: the sheet picked from F2 (read back from the box), 選擇列印 二者皆列印.</summary>
    private async Task FillStocktakeReportAsync(nint dialog, string sheetNo, CancellationToken cancellationToken)
    {
        var (popup, sheets) = await OpenStocktakeF2Async(dialog, cancellationToken);
        if (!NativeMethods.GetWindowRect(popup, out var p))
            throw new InvalidOperationException("讀不到「F2開窗查詢」的位置。");
        var row = sheets.FirstOrDefault(s => s.Sheet.SheetNo == sheetNo);
        if (row.Sheet is null)
        {
            await ClickButtonAsync(popup, "取消", "「F2開窗查詢」的「取消」", cancellationToken);
            throw new InvalidDataException($"INVR11 的盤點底稿清單沒有 {sheetNo}。");
        }
        // The 底稿編號 cell, left part of the row's line.
        SafeClick(popup, new Point(p.Left + row.Rect.Left + 20, p.Top + row.Rect.Top + row.Rect.Height / 2));
        await Pace.Delay(200, cancellationToken);
        await ClickButtonAsync(popup, "確定", "「F2開窗查詢」的「確定」", cancellationToken);
        if (!await WaitUntilAsync(() => !NativeMethods.IsWindow(popup) || !NativeMethods.IsWindowVisible(popup), 3000, cancellationToken))
            throw new InvalidOperationException("按「確定」後「F2開窗查詢」沒有關閉。");
        await Pace.Delay(300, cancellationToken);

        var (box, print) = StocktakeConditionBoxes(dialog);
        var shown = (NativeMethods.ControlText(box.Handle) ?? "").Trim();
        _log.Info("stocktake", $"INVR11 sheet box shown={shown}");
        if (shown != sheetNo)
            throw new InvalidOperationException($"INVR11 的「選擇盤點底稿編號」帶回「{shown}」，不是 {sheetNo}。");
        // Items: 非批號盤點 | 批號盤點 | 二者皆列印 (screenshot 106); 二者皆列印 is the last.
        await SetComboLastAsync(dialog, print, "二者皆列印", "INVR11 的「選擇列印」", cancellationToken);
    }

    /// <summary>
    /// 基本選項's 「選擇盤點底稿編號」 box (the top input on the left) and 「選擇列印」 (the drop-down on
    /// the right; 分類方式 is the one on the left), screenshot 104.
    /// </summary>
    private (WindowControl SheetBox, WindowControl Print) StocktakeConditionBoxes(nint dialog)
    {
        var sheet = Win32Automation.FindTabSheet(dialog, "基本選項")
            ?? throw new InvalidOperationException("INVR11 找不到「基本選項」頁籤。");
        var middle = (sheet.Rect.Left + sheet.Rect.Right) / 2;
        var inputs = Win32Automation.InputControls(sheet.Handle, visibleOnly: true)
            .Where(c => !Invi02Layout.IsCheckBox(c.ClassName)).ToList();
        var box = inputs.Where(c => !Invi02Layout.IsCombo(c.ClassName) && c.Rect.Right < middle)
            .OrderBy(c => c.Rect.Top).FirstOrDefault();
        var print = inputs.Where(c => Invi02Layout.IsCombo(c.ClassName) && c.Rect.Left > middle)
            .OrderBy(c => c.Rect.Top).FirstOrDefault();
        if (box is null || print is null)
        {
            LogChildren(dialog, "INVR11");
            throw new InvalidOperationException("INVR11 找不到「選擇盤點底稿編號」或「選擇列印」欄位。");
        }
        return (box, print);
    }

    /// <summary>
    /// 「F2開窗查詢」 from the 選擇盤點底稿編號 box, and every sheet in it. Its footer 「1 / 4」 is
    /// record 1 of 4 (screenshot 105): when OCR found fewer, the grid is paged down until all are read.
    /// </summary>
    private async Task<(nint Popup, List<(StocktakeSheet Sheet, Rectangle Rect)> Sheets)> OpenStocktakeF2Async(
        nint dialog, CancellationToken cancellationToken)
    {
        await ShowReportTabAsync(dialog, StocktakeReport, "基本選項", cancellationToken);
        var (box, _) = StocktakeConditionBoxes(dialog);
        var before = Win32Automation.VisibleProcessWindows(dialog);
        bool IsF2(nint h) => NativeMethods.WindowText(h).Contains("開窗查詢", StringComparison.Ordinal) ||
                             (NativeMethods.GetWindowRect(h, out var r) && r.Width > 300 && r.Height > 200 && !Invi02Layout.IsMessageDialogClass(NativeMethods.ClassName(h)));
        SafeClick(dialog, Win32Automation.Center(box.Rect));
        await Pace.Delay(200, cancellationToken);
        InputSender.Press(NativeMethods.VK_F2);
        var popup = await WaitNewWindowAsync(dialog, before, IsF2, 3000, cancellationToken);
        if (popup is null)
        {
            // The button right of the box opens the same list.
            SafeClick(dialog, new Point(box.Rect.Right + 14, (box.Rect.Top + box.Rect.Bottom) / 2));
            popup = await WaitNewWindowAsync(dialog, before, IsF2, F2WaitMs, cancellationToken);
        }
        if (popup is not { } f2)
        {
            LogProcessWindows(dialog, "INVR11-F2");
            throw new InvalidOperationException("INVR11 按 F2 後沒有出現「F2開窗查詢」。");
        }
        await Pace.Delay(500, cancellationToken);
        if (!Win32Automation.PrepareForeground(f2, _log) || !NativeMethods.GetWindowRect(f2, out var p))
            throw new InvalidOperationException("無法把「F2開窗查詢」帶到前景。");

        var sheets = new List<(StocktakeSheet Sheet, Rectangle Rect)>();
        int? total = null;
        for (var page = 0; page < 30; page++)
        {
            IReadOnlyList<OcrToken> tokens;
            using (var image = ScreenCapture.Capture(p.ToRectangle()))
                tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: false);
            total ??= StocktakeQuery.ParseF2Total(tokens);
            if (total is null && page == 0) // the footer 「1 / 4」 was not read (V1.3.0 LOG: total=?)
                _log.Info("stocktake", "F2 bottom lines=\"" + string.Join(" | ", StocktakeQuery.Lines(tokens).TakeLast(3).Select(l => l.Text)) + "\"");
            var read = StocktakeQuery.ParseF2(tokens);
            var added = 0;
            foreach (var row in read)
            {
                var at = sheets.FindIndex(s => s.Sheet.SheetNo == row.Sheet.SheetNo);
                if (at < 0) { sheets.Add(row); added++; }
                else sheets[at] = row; // where it is on screen now
            }
            _log.Info("stocktake", $"F2 page={page} read={read.Count} total={total?.ToString() ?? "?"}");
            if (total is null || sheets.Count >= total || (page > 0 && added == 0)) break;
            // Page down from the last row read.
            var last = read.LastOrDefault();
            if (last.Sheet is null) break;
            SafeClick(f2, new Point(p.Left + last.Rect.Left + 20, p.Top + last.Rect.Top + last.Rect.Height / 2));
            InputSender.Press(NativeMethods.VK_NEXT);
            await Pace.Delay(400, cancellationToken);
        }
        if (total is { } n && sheets.Count < n)
            throw new InvalidDataException($"「F2開窗查詢」共 {n} 張盤點底稿，只讀到 {sheets.Count} 張。");
        if (sheets.Count == 0)
            throw new InvalidDataException("「F2開窗查詢」讀不到任何盤點底稿。");
        return (f2, sheets);
    }

    /// <summary>
    /// The report's first toolbar button (not its ▼, screenshot 107) opens 欄位資訊 (screenshot 108):
    /// a grid 順序／名稱／顯示／寬度 where one click on N makes it Y. 儲位, 批號 and 盤點數量 are N by
    /// default; each N is clicked, read again, then 確定.
    /// </summary>
    private async Task ShowStocktakeColumnsAsync(nint viewer, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(viewer, _log) || !NativeMethods.GetWindowRect(viewer, out var w))
            throw new InvalidOperationException("無法把品號盤點清單報表帶到前景。");
        var before = Win32Automation.VisibleProcessWindows(viewer);
        // Same frame as 「XLSX」 (OpenSaveDialogAsync): the first button's icon is about 26 px in.
        SafeClick(viewer, new Point(w.Left + 8 + 26, w.Top + 31 + 22));
        var dialog = await WaitNewWindowAsync(viewer, before,
            h => NativeMethods.WindowText(h).Contains("欄位資訊", StringComparison.Ordinal), SaveDialogWaitMs, cancellationToken);
        if (dialog is not { } columns)
        {
            LogProcessWindows(viewer, "INVR11-欄位資訊");
            throw new InvalidOperationException("按報表左上第一個按鈕後沒有出現「欄位資訊」。");
        }
        await Pace.Delay(400, cancellationToken);
        if (!Win32Automation.PrepareForeground(columns, _log) || !NativeMethods.GetWindowRect(columns, out var d))
            throw new InvalidOperationException("無法把「欄位資訊」帶到前景。");

        async Task<Dictionary<string, (string Shown, Point Cell)>> ReadAsync()
        {
            await ThrowIfErpMessageAsync(viewer); // SMReportPreviewC's own error box (V1.3.0: Access violation)
            IReadOnlyList<OcrToken> tokens;
            using (var image = ScreenCapture.Capture(d.ToRectangle()))
                tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: true);
            if (OcrWordFinder.FindWord(tokens, "顯示") is not { } header)
            {
                _log.Warn("stocktake", $"欄位資訊 tokens=\"{string.Join(" ", tokens.Select(t => t.Text))}\"");
                throw new InvalidOperationException("「欄位資訊」找不到「顯示」欄。");
            }
            var order = OcrWordFinder.FindWord(tokens, "順序");
            var x = header.Left + header.Width / 2;
            var result = new Dictionary<string, (string, Point)>();
            foreach (var (name, number) in StocktakeColumns)
            {
                // By name; OCR may misread it (V1.3.0 LOG: 「儲位」 missed), then by its 順序 number
                // (screenshot 108: 儲位 7, 批號 11, 盤點數量 14).
                var row = OcrWordFinder.FindWord(tokens, name) ?? (order is { } o
                    ? tokens.Where(t => t.Text.Trim() == number && Math.Abs(t.Rect.Left + t.Rect.Width / 2 - (o.Left + o.Width / 2)) < 30)
                        .Select(t => (Rectangle?)t.Rect).FirstOrDefault()
                    : null);
                if (row is not { } label)
                {
                    _log.Warn("stocktake", $"欄位資訊 tokens=\"{string.Join(" ", tokens.Select(t => t.Text))}\"");
                    throw new InvalidOperationException($"「欄位資訊」找不到「{name}」。");
                }
                var y = label.Top + label.Height / 2;
                var shown = StocktakeQuery.CellText(tokens, y, x, Math.Max(12, header.Width / 2)).ToUpperInvariant();
                result[name] = (shown, new Point(d.Left + x, d.Top + y));
            }
            _log.Info("stocktake", "欄位資訊 " + string.Join(" ", result.Select(r => $"{r.Key}={r.Value.Item1}")));
            return result;
        }

        // One cell at a time, slowly, each read back before the next: the report program crashed
        // (Access violation in SMReportPreviewC) when three were clicked 0.25 s apart (V1.3.0, 2026-10-29).
        foreach (var (name, _) in StocktakeColumns)
        {
            var (shown, cell) = (await ReadAsync())[name];
            if (shown == "Y") continue;
            if (shown != "N")
                throw new InvalidOperationException($"「欄位資訊」的「{name}」顯示欄讀不出 Y 或 N（讀到「{shown}」）。");
            SafeClick(columns, cell);
            _log.Info("stocktake", $"欄位資訊 {name} N clicked");
            await Pace.Delay(1000, cancellationToken);
            if ((await ReadAsync())[name].Shown != "Y")
                throw new InvalidOperationException($"「欄位資訊」的「{name}」點了之後沒有變成 Y。");
        }
        await ClickButtonAsync(columns, "確定", "「欄位資訊」的「確定」", cancellationToken);
        if (!await WaitUntilAsync(() => !NativeMethods.IsWindow(columns) || !NativeMethods.IsWindowVisible(columns), 3000, cancellationToken))
            throw new InvalidOperationException("按「確定」後「欄位資訊」沒有關閉。");
        await Pace.Delay(800, cancellationToken); // the report redraws with the new columns
    }
}
