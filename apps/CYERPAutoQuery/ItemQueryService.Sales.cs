using System.Diagnostics;

namespace CYERPAutoQuery;

/// <summary>
/// ERP 報表 (明細表) queries. 銷貨銷退單 (user, 2026-10-16/17): a customer's 銷貨單 from COPR15 銷貨單明細表 and 銷退單 from
/// COPR16 銷退單明細表, both driven the same way: 作業速查 → 基本選項 客戶代號 起＝迄＝code, 單據日期 →
/// 進階選項 確認狀態／結帳狀態 「全部」 → 即時報表 → report window → XLSX → 存成EXCEL檔 (no thousands
/// separator) → 確認 → ERP writes into C:\My Documents → the file is moved to TEMP, read and
/// deleted. Both report windows are closed afterwards (user, 2026-10-16). Read-only: only report
/// conditions are set.
/// </summary>
internal sealed partial class ItemQueryService
{
    // 即時報表 shows a progress bar first; a long range can take a while.
    private const int ReportRunWaitMs = 180000;
    private const int SaveDialogWaitMs = 5000;
    private const int ReportCloseWaitMs = 10000;
    /// <summary><see cref="WaitReportViewerAsync"/>'s answer when ERP's box asks for the report to be run again.</summary>
    private const nint RunAgain = -1;

    public Task<SalesQueryResult> QuerySalesAsync(SalesQuery query, IProgress<string> progress, CancellationToken cancellationToken) =>
        GuardErpMessagesAsync(() => QuerySalesCoreAsync(query, progress, cancellationToken));

    private async Task<SalesQueryResult> QuerySalesCoreAsync(SalesQuery query, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var orders = new List<SalesOrder>();
        foreach (var report in SalesReport.All)
            orders.AddRange(await RunSalesReportAsync(report, query, progress, cancellationToken));
        _log.Info("sales", $"customer={query.CustomerCode} sales={orders.Count(o => o.Kind == SalesReport.Sales.Kind)} returns={orders.Count(o => o.Kind == SalesReport.Returns.Kind)} ms={watch.ElapsedMilliseconds}");
        return new SalesQueryResult(query, orders.OrderBy(o => o, SalesExport.Newest).ToList(), watch.Elapsed);
    }

    /// <summary>One 銷貨單 or 銷退單 by number (its report chosen by 單別), for a 未收款 憑證.</summary>
    public Task<SalesQueryResult> QueryOrderAsync(SalesQuery query, IProgress<string> progress, CancellationToken cancellationToken) =>
        GuardErpMessagesAsync(async () =>
        {
            var watch = Stopwatch.StartNew();
            var report = SalesReport.ForOrder(query.OrderNo ?? string.Empty)
                ?? throw new ArgumentException("單號不是銷貨單或銷退單。");
            var orders = await RunSalesReportAsync(report, query, progress, cancellationToken);
            _log.Info("sales", $"order query orders={orders.Count} ms={watch.ElapsedMilliseconds}");
            return new SalesQueryResult(query, orders, watch.Elapsed);
        });

    private async Task<IReadOnlyList<SalesOrder>> RunSalesReportAsync(SalesReport sales, SalesQuery query, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var report = sales.Form;
        var describe = query.OrderNo is { } no ? $"單號 {no}"
            : $"客戶 {query.CustomerCode}，{Invq01Layout.ErpDate(query.From)}～{Invq01Layout.ErpDate(query.To)}";
        var sheets = await RunReportAsync(report, describe,
            async (dialog, ct) =>
            {
                if (query.OrderNo is not null) await FillReportOrderAsync(dialog, report, sales, query, ct);
                else await FillReportBasicAsync(dialog, report, query, ct);
                await FillReportAdvancedAsync(dialog, report, ct);
                await VerifyReportAdvancedAsync(dialog, report, ct);
            }, progress, cancellationToken);
        var orders = sheets is null ? [] : SalesExport.Parse(sheets, sales, query);
        _log.Info("sales", $"{report.Code} orders={orders.Count} lines={orders.Sum(o => o.Lines.Count)}");
        return orders;
    }

    /// <summary>
    /// One report run: close leftovers → open through 作業速查 → <paramref name="fill"/> the conditions
    /// → 即時報表 → XLSX export → read → close the report and condition windows (also after a
    /// failure or Esc). The export is deleted from TEMP once read. Null when ERP said nothing matched.
    /// </summary>
    /// <param name="prepare">Runs on the report window before the export (INVR11 shows hidden columns there).</param>
    private async Task<IReadOnlyList<(string Name, List<string[]> Rows)>?> RunReportAsync(ErpReport report, string conditions,
        Func<nint, CancellationToken, Task> fill, IProgress<string> progress, CancellationToken cancellationToken,
        Func<nint, CancellationToken, Task>? prepare = null)
    {
        await CloseReportWindowsAsync(report, progress, leftover: true);
        progress.Report($"ERP：開啟{report.Title}（作業速查）…");
        await OpenFormAsync(report.Code, report.Title, () => FindReportConditions(report).Count > 0, cancellationToken);
        var found = FindReportConditions(report);
        if (found.Count != 1)
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"開啟{report.Code}後找到 {found.Count} 個{report.Title}條件視窗，已停止。");
        var dialog = found[0];
        _log.Info("report", $"{report.Code} conditions class={NativeMethods.ClassName(dialog)} title=\"{NativeMethods.WindowText(dialog)}\"");

        var closed = false;
        try
        {
            if (!Win32Automation.PrepareForeground(dialog, _log))
                throw new InvalidOperationException($"無法把{report.Title}帶到前景。");
            progress.Report($"ERP：設定{report.Title}條件（{conditions}）…");
            await fill(dialog, cancellationToken);

            progress.Report($"ERP：產生{report.Title}（即時報表）…");
            // ERP sometimes answers 即時報表 with a box holding only 「E」 (user, 2026-10-19): then the
            // report is run again, up to three times in all.
            nint viewer = RunAgain;
            for (var run = 1; run <= 3 && viewer == RunAgain; run++)
            {
                var before = FindReportWindows(report).ToHashSet();
                await ClickButtonAsync(dialog, SalesReportLayout.RunButton, $"{report.Title}的「即時報表」", cancellationToken);
                _log.Info("report", $"{report.Code} 即時報表 clicked run={run}");
                viewer = await WaitReportViewerAsync(dialog, report, before, cancellationToken);
            }
            if (viewer == RunAgain)
                throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"{report.Title}按了三次「即時報表」都只出現「E」訊息，查詢已停止。");
            if (viewer == 0)
            {
                closed = await CloseReportWindowsAsync(report, null, leftover: false);
                if (!closed) throw new InvalidOperationException($"{report.Title}的視窗沒有全部關閉，請人工確認 ERP 畫面。");
                return null;
            }

            if (prepare is not null) await prepare(viewer, cancellationToken);
            progress.Report($"ERP：{report.Title}轉出 Excel…");
            var path = ExportFiles.NewPath(report.Code);
            try
            {
                await ExportReportAsync(viewer, report, path, cancellationToken);
                var sheets = await ExportFiles.ReadAsync(path, cancellationToken);
                progress.Report($"ERP：關閉{report.Title}…");
                closed = await CloseReportWindowsAsync(report, null, leftover: false);
                if (!closed) throw new InvalidOperationException($"{report.Title}的視窗沒有全部關閉，請人工確認 ERP 畫面。");
                return sheets;
            }
            finally
            {
                ExportFiles.Delete(path, _log);
            }
        }
        finally
        {
            if (!closed && !await CloseReportWindowsAsync(report, null, leftover: false))
                _log.Warn("report", $"{report.Code} windows left open after a failure");
        }
    }

    // ---- windows ---------------------------------------------------------------------

    /// <summary>Visible top-level windows captioned with the report's name (conditions and report), message boxes excluded.</summary>
    private static List<nint> FindReportWindows(ErpReport report)
    {
        var found = new List<nint>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd) && report.IsTitle(NativeMethods.WindowText(hwnd)) &&
                !Invi02Layout.IsMessageDialogClass(NativeMethods.ClassName(hwnd)))
                found.Add(hwnd);
            return true;
        }, 0);
        return found;
    }

    /// <summary>The condition window: the one holding the 「選擇客戶代號」 group or the 即時報表 button.</summary>
    private static bool IsReportConditions(nint hwnd) =>
        Win32Automation.EnumerateChildren(hwnd).Any(c => Invi02Layout.NormalizeCaption(c.Text) == SalesReportLayout.CustomerGroup) ||
        FindButton(hwnd, c => c.StartsWith(SalesReportLayout.RunButton, StringComparison.Ordinal)) is not null;

    private static List<nint> FindReportConditions(ErpReport report) =>
        FindReportWindows(report).Where(IsReportConditions).ToList();

    /// <summary>
    /// Closes the report windows (report first, then conditions) with WM_CLOSE; not cancellable so it
    /// also runs after Esc. <paramref name="leftover"/>: ones left by the user or an earlier run.
    /// </summary>
    private async Task<bool> CloseReportWindowsAsync(ErpReport report, IProgress<string>? progress, bool leftover)
    {
        // ERP message boxes captioned with the report, left by an earlier stop (V0.9.0 Build 6 LOG:
        // three hidden ones after three failures), are closed first.
        var boxes = new List<nint>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            // Any 明細表's box (COPR16's left behind would cover COPR15's report too).
            if (Invi02Layout.IsMessageDialogClass(NativeMethods.ClassName(hwnd)) &&
                (report.IsTitle(NativeMethods.WindowText(hwnd)) || NativeMethods.WindowText(hwnd).Contains("明細表", StringComparison.Ordinal)))
                boxes.Add(hwnd);
            return true;
        }, 0);
        foreach (var box in boxes)
        {
            _log.Info("sales", $"closing {report.Code} message box visible={NativeMethods.IsWindowVisible(box)}");
            NativeMethods.PostMessage(box, NativeMethods.WM_CLOSE, 0, 0);
        }
        if (boxes.Count > 0) await Task.Delay(300);
        var open = FindReportWindows(report).OrderBy(IsReportConditions).ToList();
        if (open.Count == 0) return true;
        if (leftover) progress?.Report($"ERP：關閉開著的{report.Title}…");
        _log.Info("sales", $"closing {report.Code} windows={open.Count} leftover={leftover}");
        foreach (var hwnd in open)
        {
            if (!NativeMethods.IsWindow(hwnd)) continue;
            NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, 0, 0);
            var deadline = Environment.TickCount64 + ReportCloseWaitMs;
            while (Environment.TickCount64 < deadline && NativeMethods.IsWindow(hwnd) && NativeMethods.IsWindowVisible(hwnd))
                await Task.Delay(100);
        }
        var left = FindReportWindows(report);
        if (left.Count == 0) return true;
        foreach (var hwnd in left) LogChildren(hwnd, $"{report.Code}-not-closed");
        if (leftover)
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"開著的{report.Title}無法自動關閉，請先手動關閉後再查詢。");
        return false;
    }

    /// <summary>For on-site diagnosis: every top-level window whose caption contains <paramref name="word"/>, any process.</summary>
    private void LogTitledWindows(string word, string context)
    {
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            var title = NativeMethods.WindowText(hwnd);
            if (title.Contains(word, StringComparison.Ordinal))
                _log.Info("sales", $"{context}: window class={NativeMethods.ClassName(hwnd)} pid={Win32Automation.ProcessId(hwnd)} visible={NativeMethods.IsWindowVisible(hwnd)} title=\"{title}\"");
            return true;
        }, 0);
    }

    // ---- conditions --------------------------------------------------------------------

    /// <summary>Shows a condition tab: measured position, then OCR of the tab strip (via <see cref="ShowLazyTabAsync"/>).</summary>
    private async Task ShowReportTabAsync(nint dialog, ErpReport report, string tab, CancellationToken cancellationToken)
    {
        WindowControl? sheet = null;
        await WaitUntilAsync(() => (sheet = report.Tabs.Select(t => Win32Automation.FindTabSheet(dialog, t)).FirstOrDefault(t => t is not null)) is not null,
            AfterActionWaitMs, cancellationToken);
        if (sheet is null)
        {
            LogChildren(dialog, report.Code);
            throw new InvalidOperationException($"{report.Title}找不到「{tab}」頁籤。");
        }
        var pageHandle = NativeMethods.GetParent(sheet.Handle);
        var page = Win32Automation.EnumerateChildren(dialog).FirstOrDefault(c => c.Handle == pageHandle)
            ?? throw new InvalidOperationException($"{report.Title}找不到頁籤所在的頁面。");
        await ShowLazyTabAsync(dialog, page, report.Strip, tab, report.Title, cancellationToken);
    }

    /// <summary>The visible group box captioned <paramref name="caption"/> and its input boxes, top to bottom.</summary>
    private List<WindowControl> GroupBoxes(nint dialog, ErpReport report, string caption)
    {
        var group = Win32Automation.EnumerateChildren(dialog)
            .FirstOrDefault(c => c.Visible && Invi02Layout.NormalizeCaption(c.Text) == caption);
        var boxes = group is null ? [] : Win32Automation.InputControls(group.Handle, visibleOnly: true)
            .Where(c => !Invi02Layout.IsCheckBox(c.ClassName))
            .OrderBy(c => c.Rect.Top).ThenBy(c => c.Rect.Left).ToList();
        if (boxes.Count != 2)
        {
            LogChildren(dialog, report.Code);
            throw new InvalidOperationException($"{report.Title}找不到「{caption}」的起、迄欄位（找到 {boxes.Count} 個）。");
        }
        return boxes;
    }

    /// <summary>
    /// 基本選項: 客戶代號 起 and 迄 both the customer (區間選擇 is ticked by default, user 2026-10-16),
    /// then 單據日期 (<see cref="FillReportDatesAsync"/>).
    /// </summary>
    private async Task FillReportBasicAsync(nint dialog, ErpReport report, SalesQuery query, CancellationToken cancellationToken)
    {
        await ShowReportTabAsync(dialog, report, SalesReportLayout.BasicTab, cancellationToken);
        var customer = GroupBoxes(dialog, report, SalesReportLayout.CustomerGroup);
        if (query.AllCustomers)
        {
            await ClearReportTextAsync(dialog, customer[0], $"{report.Title}的客戶代號起", cancellationToken);
            await ClearReportTextAsync(dialog, customer[1], $"{report.Title}的客戶代號迄", cancellationToken);
        }
        else
        {
            await SetReportTextAsync(dialog, customer[0], query.CustomerCode, $"{report.Title}的客戶代號起", cancellationToken);
            await SetReportTextAsync(dialog, customer[1], query.CustomerCode, $"{report.Title}的客戶代號迄", cancellationToken);
        }
        await FillReportDatesAsync(dialog, report, query.Dates, cancellationToken);
    }

    /// <summary>
    /// 選擇單據日期 typed as in INVQ01 (COPR15／COPR16／PURR18). An unasked date must be empty: a date
    /// left in the box would narrow the report without anyone noticing.
    /// </summary>
    private async Task FillReportDatesAsync(nint dialog, ErpReport report, HistoryRange range, CancellationToken cancellationToken)
    {
        var dates = GroupBoxes(dialog, report, SalesReportLayout.DateGroup);
        for (var i = 0; i < 2; i++)
        {
            var asked = i == 0 ? range.From : range.To;
            var shown = NativeMethods.ControlText(dates[i].Handle) ?? string.Empty;
            if (asked is null && shown.Any(char.IsAsciiDigit))
                throw new QueryFailure(ErpAgentErrorCode.ERP_STATE,
                    $"{report.Title}的單據日期{(i == 0 ? "起" : "迄")}已有日期「{shown.Trim()}」，但這次沒有指定。\n請在 ERP 清空後再查詢。");
        }
        foreach (var action in Invq01Layout.DateActions(range))
            await SetDateAsync(dialog, dates[action.BoxIndex], action.Date, action.BoxIndex == 0 ? "起" : "迄",
                click: action.Click, enterAfter: action.EnterAfter, cancellationToken: cancellationToken, field: $"{report.Title}單據日期");
        for (var i = 0; i < 2; i++)
            if ((i == 0 ? range.From : range.To) is { } expected && !Invq01Layout.ShowsDate(NativeMethods.ControlText(dates[i].Handle), expected))
                throw new InvalidOperationException($"{report.Title}的單據日期{(i == 0 ? "起" : "迄")}讀回不一致，已停止。");
    }

    /// <summary>
    /// 基本選項 for one order (user, 2026-10-17): 選擇銷貨單號 (銷退單號) 起 and 迄 both the number —
    /// click, Home (the mask must be filled from its first cell), paste the number without the dash,
    /// read back. The customer and date groups must stay empty.
    /// </summary>
    private async Task FillReportOrderAsync(nint dialog, ErpReport report, SalesReport sales, SalesQuery query, CancellationToken cancellationToken)
    {
        await ShowReportTabAsync(dialog, report, SalesReportLayout.BasicTab, cancellationToken);
        EnsureGroupEmpty(dialog, report, SalesReportLayout.CustomerGroup);
        EnsureGroupEmpty(dialog, report, SalesReportLayout.DateGroup);
        var boxes = GroupBoxes(dialog, report, sales.OrderGroup);
        for (var i = 0; i < 2; i++)
        {
            var what = $"{report.Title}的{sales.NoColumn}{(i == 0 ? "起" : "迄")}";
            var box = boxes[i];
            var focus = await ClickUntilFocusedAsync(dialog, box, new Point(box.Rect.Left + 8, (box.Rect.Top + box.Rect.Bottom) / 2), what, cancellationToken);
            InputSender.Press(NativeMethods.VK_HOME);
            await Pace.Delay(60, cancellationToken);
            await PasteIntoAsync(focus, query.OrderKeys, cancellationToken, selectAll: false);
            var shown = new string((NativeMethods.ControlText(box.Handle) ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
            if (!string.Equals(shown, query.OrderKeys, StringComparison.OrdinalIgnoreCase))
            {
                _log.Warn("sales", $"{what} read-back mismatch length={shown.Length}");
                throw new InvalidOperationException($"{what}輸入後讀回不一致，已停止。");
            }
        }
    }

    /// <summary>
    /// Clicks the box, pastes the text and reads it back. A click that does not give the box focus
    /// is tried again (V0.9.0 Build 3 LOG: 客戶代號迄 right after 起, on a condition window opened a
    /// moment before): the window is brought to the front and the box clicked up to three times.
    /// </summary>
    private async Task SetReportTextAsync(nint dialog, WindowControl box, string text, string what, CancellationToken cancellationToken)
    {
        var focus = await ClickUntilFocusedAsync(dialog, box, Win32Automation.Center(box.Rect), what, cancellationToken);
        await PasteIntoAsync(focus, text, cancellationToken);
        var shown = (NativeMethods.ControlText(box.Handle) ?? string.Empty).Trim();
        if (!ItemCodeRules.SameCode(shown, text))
        {
            _log.Warn("sales", $"{what} read-back mismatch shown={_log.Value(shown)}");
            throw new InvalidOperationException($"{what}輸入後讀回不一致，已停止。");
        }
    }

    /// <summary>
    /// An empty box (every customer, 進貨輔助): a value left from an earlier query is selected and
    /// deleted, and the box must read back empty (a code left there would narrow the report).
    /// </summary>
    private async Task ClearReportTextAsync(nint dialog, WindowControl box, string what, CancellationToken cancellationToken)
    {
        static bool Empty(WindowControl b) => !(NativeMethods.ControlText(b.Handle) ?? string.Empty).Any(char.IsLetterOrDigit);
        if (Empty(box)) return;
        await ReplaceTextAsync(dialog, box, string.Empty, what, cancellationToken);
        if (!Empty(box))
        {
            _log.Warn("sales", $"{what} not cleared shown={_log.Value(NativeMethods.ControlText(box.Handle) ?? string.Empty)}");
            throw new InvalidOperationException($"{what}清不掉原本的內容，已停止。");
        }
    }

    /// <summary>
    /// 進階選項 (user, 2026-10-16): 確認狀態 and 結帳狀態 「全部」 — the two drop-downs, top to bottom,
    /// 全部 the last item of both. 列印單價金額 must stay ticked; the export is refused without 單價.
    /// </summary>
    private async Task FillReportAdvancedAsync(nint dialog, ErpReport report, CancellationToken cancellationToken)
    {
        await ShowReportTabAsync(dialog, report, SalesReportLayout.AdvancedTab, cancellationToken);
        var sheet = await WaitForTabSheetAsync(dialog, SalesReportLayout.AdvancedTab, AfterActionWaitMs, cancellationToken, visible: true)
            ?? throw new InvalidOperationException($"{report.Title}的「進階選項」沒有顯示。");
        var combos = Win32Automation.InputControls(sheet.Handle, visibleOnly: true)
            .Where(c => Invi02Layout.IsCombo(c.ClassName)).OrderBy(c => c.Rect.Top).ToList();
        if (combos.Count < 2)
        {
            LogChildren(dialog, report.Code);
            throw new InvalidOperationException($"{report.Title}的進階選項找不到確認狀態與結帳狀態（找到 {combos.Count} 個下拉選單）。");
        }
        await SetComboLastAsync(dialog, combos[0], SalesReportLayout.AllStatus, $"{report.Title}的確認狀態", cancellationToken);
        await SetComboLastAsync(dialog, combos[1], SalesReportLayout.AllStatus, $"{report.Title}的結帳狀態", cancellationToken);
        var check = Win32Automation.EnumerateChildren(sheet.Handle)
            .FirstOrDefault(c => c.Visible && Invi02Layout.IsCheckBox(c.ClassName));
        _log.Info("sales", $"{report.Code} 列印單價金額 class={check?.ClassName ?? "-"} text=\"{check?.Text}\"");
    }

    /// <summary>
    /// Selects <paramref name="target"/>, the last item: the list is the new popup window of the
    /// ERP process after the click; with it open End + Enter commits (Enter only then, else it
    /// would press 確定); without it End changes the closed box directly. Read back here and
    /// again before 即時報表 (<see cref="VerifyReportAdvancedAsync"/>).
    /// </summary>
    private async Task SetComboLastAsync(nint dialog, WindowControl combo, string target, string what, CancellationToken cancellationToken)
    {
        if (ComboText(combo) == target) return;
        var before = Win32Automation.VisibleProcessWindows(dialog);
        SafeClick(dialog, Win32Automation.Center(combo.Rect));
        nint popup = 0;
        await WaitUntilAsync(() => (popup = Win32Automation.VisibleProcessWindows(dialog).FirstOrDefault(h => !before.Contains(h))) != 0, 800, cancellationToken);
        _log.Info("sales", $"{what} list open={popup != 0} class={(popup != 0 ? NativeMethods.ClassName(popup) : "-")}");
        InputSender.Press(NativeMethods.VK_END);
        await Pace.Delay(120, cancellationToken);
        if ((popup != 0 && NativeMethods.IsWindowVisible(popup)) || IsDropDownOpen(dialog, combo))
        {
            InputSender.Press(NativeMethods.VK_RETURN);
            await WaitUntilAsync(() => popup == 0 || !NativeMethods.IsWindowVisible(popup), 1000, cancellationToken);
        }
        await Pace.Delay(150, cancellationToken);
        var shown = ComboText(combo);
        _log.Info("sales", $"{what} shown=\"{shown}\"");
        if (shown != target)
            throw new InvalidOperationException($"無法把{what}設為「{target}」（目前顯示「{shown}」）。");
    }

    /// <summary>Both 進階選項 drop-downs still show 「全部」 after focus has left them; else ERP dropped the choice.</summary>
    private async Task VerifyReportAdvancedAsync(nint dialog, ErpReport report, CancellationToken cancellationToken)
    {
        await Pace.Delay(200, cancellationToken);
        var sheet = Win32Automation.FindTabSheet(dialog, SalesReportLayout.AdvancedTab);
        var combos = sheet is null ? [] : Win32Automation.InputControls(sheet.Handle, visibleOnly: false)
            .Where(c => Invi02Layout.IsCombo(c.ClassName)).OrderBy(c => c.Rect.Top).Take(2).ToList();
        var shown = combos.Select(ComboText).ToList();
        _log.Info("sales", $"{report.Code} status before 即時報表 shown=\"{string.Join("/", shown)}\"");
        if (combos.Count != 2 || shown.Any(t => t != SalesReportLayout.AllStatus))
            throw new InvalidOperationException($"{report.Title}的確認狀態／結帳狀態沒有維持「全部」（目前「{string.Join("／", shown)}」），已停止。");
    }

    // ---- report and export -------------------------------------------------------------

    /// <summary>
    /// The report window opened by 即時報表 (「銷貨單明細表(COPR15)[A]」), or 0 when ERP said nothing
    /// matched (<see cref="SalesReportLayout.IsNoData"/>, the box is closed), looked for among all
    /// top-level windows: V0.6.0 Build 2 LOG (2026-10-17) never found it among the ERP process's
    /// own windows although it was on screen. An ERP message instead stops with that message.
    /// </summary>
    private async Task<nint> WaitReportViewerAsync(nint dialog, ErpReport report, IReadOnlySet<nint> before, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + ReportRunWaitMs;
        var started = Environment.TickCount64;
        var logged = false;
        while (Environment.TickCount64 < deadline)
        {
            await Pace.Delay(200, cancellationToken);
            var viewer = FindReportWindows(report).FirstOrDefault(h => !before.Contains(h) && !IsReportConditions(h));
            if (viewer != 0)
            {
                var same = Win32Automation.ProcessId(viewer) == Win32Automation.ProcessId(dialog);
                _log.Info("sales", $"{report.Code} report window after_ms={Environment.TickCount64 - started} class={NativeMethods.ClassName(viewer)} same_process={same} title=\"{NativeMethods.WindowText(viewer)}\"");
                await Pace.Delay(800, cancellationToken); // let it finish painting its toolbar
                return viewer;
            }
            if (FindErpMessageBox(dialog) is var box && box != 0)
            {
                // Nothing matched: ERP says so in a box instead of opening a report. V0.9.0 Build 5
                // LOG (COPR16 without 銷退單): the box holds no text at all, only the report's title
                // in its caption and one OK button; any text in its body is a real message.
                var text = await ReadErpMessageAsync(box);
                var body = await ReadMessageBodyAsync(box);
                var buttons = Win32Automation.EnumerateChildren(box).Count(c => c.Visible && IsButtonClass(c.ClassName));
                var notice = SalesReportLayout.ClassifyNotice(text, body, buttons, report.IsTitle(NativeMethods.WindowText(box)));
                _log.Info("sales", $"{report.Code} message after 即時報表 text={_log.Value(text)} body={_log.Value(body)} buttons={buttons} notice={notice}");
                if (notice == SalesReportLayout.ReportNotice.RunAgain)
                {
                    await DismissMessageAsync(box, cancellationToken);
                    return RunAgain;
                }
                if (notice == SalesReportLayout.ReportNotice.Error)
                {
                    // Close it before stopping: a box left behind (V0.9.0 Build 6 LOG) covered the next
                    // report window and the next query could not click it.
                    var shown = string.Join(" ", new[] { body, text }.Where(t => t.Length > 0 && t != "?"));
                    await DismissMessageAsync(box, cancellationToken);
                    throw new QueryFailure(ErpAgentErrorCode.ERP_STATE,
                        $"{report.Title}按「即時報表」後，ERP 顯示了訊息：「{shown}」\n查詢已停止（訊息已按 OK 關閉）。");
                }
                await DismissMessageAsync(box, cancellationToken);
                _log.Info("sales", $"{report.Code} no data; message closed");
                return 0;
            }
            if (!logged && Environment.TickCount64 - started > 10000)
            {
                logged = true;
                LogTitledWindows("明細表", $"{report.Code}-waiting");
            }
        }
        LogTitledWindows("明細表", $"after-{report.Code}-即時報表");
        throw new InvalidOperationException($"{report.Title}按「即時報表」後等了 {ReportRunWaitMs / 1000} 秒仍沒有出現報表視窗。");
    }

    /// <summary>
    /// XLSX → 存成EXCEL檔 (xlsx, no thousands separator) → 確認 → ERP's 「匯出成功」 box → Yes; the
    /// workbook ERP wrote is moved to <paramref name="path"/>. 「匯出失敗 … 系統找不到指定的路徑」
    /// means ERP's folder is missing (user, 2026-10-16): it is created and the export done once more.
    /// </summary>
    private async Task ExportReportAsync(nint viewer, ErpReport report, string path, CancellationToken cancellationToken)
    {
        // ERP writes the workbook where its own setting says: C:\My Documents on the user's PC, the
        // Desktop on the ERP server (V1.1.0 LOG and screenshot, 2026-10-27). The folder in ERP's
        // 「匯出成功」 message is added when it can be read; the usual places are always searched,
        // for a workbook of this report written after the export started.
        var folders = SalesReportLayout.ExportFolderCandidates(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)).ToList();
        EnsureFolder(SalesReportLayout.ErpExportFolder);
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var known = ReportFiles(folders, report);
            var started = DateTime.Now.AddSeconds(-2);
            var save = await OpenSaveDialogAsync(viewer, report, cancellationToken);
            await FillSaveDialogAsync(save, report, cancellationToken);

            // Wait for ERP's answer: a message box, normally 「匯出成功: C:\My Documents\….xlsx」.
            nint box = 0;
            if (!await WaitUntilAsync(() => (box = FindErpMessageBox(viewer)) != 0, ExportRunWaitMs, cancellationToken))
                throw new InvalidOperationException($"{report.Title}按「確認」後沒有出現匯出結果訊息。");
            var message = await ReadErpMessageAsync(box);
            _log.Info("sales", $"{report.Code} export message={_log.Value(message)}");
            if (SalesReportLayout.PathIn(message) is { } named && Path.GetDirectoryName(named) is { Length: > 0 } dir &&
                !folders.Contains(dir, StringComparer.OrdinalIgnoreCase))
                folders.Add(dir);
            await DismissMessageAsync(box, cancellationToken);

            if (SalesReportLayout.IsExportFailed(message))
            {
                if (attempt == 1 && SalesReportLayout.IsMissingFolder(message))
                {
                    var target = SalesReportLayout.PathIn(message) is { } p ? Path.GetDirectoryName(p) : null;
                    _log.Warn("sales", $"{report.Code} export folder missing; creating dir={_log.Value(target)}");
                    if (!string.IsNullOrEmpty(target)) EnsureFolder(target);
                    continue;
                }
                throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"{report.Title}轉出 Excel 失敗，ERP 顯示：「{message}」") { FromErpMessage = true };
            }

            var file = await WaitReportFileAsync(folders, report, known, started, message, cancellationToken)
                ?? throw new InvalidOperationException($"{report.Title}顯示匯出成功，但在 {string.Join("、", folders)} 找不到匯出的檔案。");
            await MoveWhenReadyAsync(file, path, cancellationToken);
            _log.Info("sales", $"{report.Code} export moved to TEMP");
            return;
        }
        throw new InvalidOperationException($"{report.Title}轉出 Excel 兩次都沒有成功。");
    }

    private void EnsureFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn("sales", $"export folder not created: {ex.GetType().Name}");
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"ERP 匯出需要資料夾「{folder}」，但無法自動建立（{ex.Message}）。請手動建立後再查詢。");
        }
    }

    private static HashSet<string> ReportFiles(IEnumerable<string> folders, ErpReport report) =>
        folders.Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f, "*.xlsx"))
            .Where(f => SalesReportLayout.IsReportFile(Path.GetFileName(f), report))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The new workbook: the path ERP named when it exists, else the one new report file in the folders.</summary>
    private static async Task<string?> WaitReportFileAsync(List<string> folders, ErpReport report, IReadOnlySet<string> known,
        DateTime started, string message, CancellationToken cancellationToken)
    {
        string? found = null;
        await WaitUntilAsync(() =>
        {
            if (SalesReportLayout.PathIn(message) is { } named && File.Exists(named)) { found = named; return true; }
            var fresh = ReportFiles(folders, report).Where(f => !known.Contains(f) && File.GetLastWriteTime(f) >= started).ToList();
            if (fresh.Count == 1) { found = fresh[0]; return true; }
            return false;
        }, 10000, cancellationToken);
        return found;
    }

    /// <summary>Moves ERP's workbook to TEMP once ERP has let go of it; nothing stays in ERP's folder.</summary>
    private static async Task MoveWhenReadyAsync(string source, string target, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + 10000;
        while (true)
        {
            try
            {
                File.Move(source, target);
                return;
            }
            catch (IOException) when (Environment.TickCount64 < deadline)
            {
                await Pace.Delay(200, cancellationToken);
            }
        }
    }

    /// <summary>
    /// The report window's toolbar button 「XLSX」 (second from the left, user's screenshot
    /// 2026-10-16): its measured position first, OCR of the toolbar when no dialog follows.
    /// </summary>
    private async Task<nint> OpenSaveDialogAsync(nint viewer, ErpReport report, CancellationToken cancellationToken)
    {
        if (!Win32Automation.PrepareForeground(viewer, _log))
            throw new InvalidOperationException($"無法把{report.Title}的報表視窗帶到前景。");
        if (!NativeMethods.GetWindowRect(viewer, out var w))
            throw new InvalidOperationException($"讀不到{report.Title}報表視窗的位置。");

        bool IsSave(nint h) => Invi02Layout.NormalizeCaption(NativeMethods.WindowText(h)).Contains(SalesReportLayout.SaveDialogTitle, StringComparison.Ordinal);
        var before = Win32Automation.VisibleProcessWindows(viewer);

        // Window frame 8 px, caption 23 px; the button centre about 95, 22 px into the client area.
        var point = new Point(w.Left + 8 + 95, w.Top + 31 + 22);
        SafeClick(viewer, point);
        _log.Info("sales", $"{report.Code} XLSX clicked by=geometry");
        if (await WaitNewWindowAsync(viewer, before, IsSave, SaveDialogWaitMs, cancellationToken) is { } dialog) return dialog;

        _log.Warn("sales", $"{report.Code} XLSX geometry click opened no dialog; trying OCR");
        LogChildren(viewer, $"{report.Code}-report");
        if (!Win32Automation.PrepareForeground(viewer, _log))
            throw new InvalidOperationException($"無法把{report.Title}的報表視窗帶到前景。");
        var strip = new Rectangle(w.Left, w.Top, Math.Min(w.Width, 420), 90);
        IReadOnlyList<OcrToken> tokens;
        using (var image = ScreenCapture.Capture(strip))
            tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: false);
        if (OcrWordFinder.FindWord(tokens, "XLSX") is not { } r)
        {
            _log.Warn("sales", $"OCR did not find XLSX tokens=\"{string.Join(" ", tokens.Select(t => t.Text))}\"");
            throw new InvalidOperationException($"{report.Title}的報表視窗找不到「XLSX」按鈕。");
        }
        SafeClick(viewer, new Point(strip.Left + r.Left + r.Width / 2, strip.Top + r.Top + r.Height / 2));
        _log.Info("sales", $"{report.Code} XLSX clicked by=ocr");
        return await WaitNewWindowAsync(viewer, before, IsSave, SaveDialogWaitMs, cancellationToken)
            ?? throw new InvalidOperationException($"{report.Title}按「XLSX」後沒有出現「存成EXCEL檔」視窗。");
    }

    /// <summary>
    /// 存成EXCEL檔: format xlsx, 「顯示數值欄位無千分符號」 (user, 2026-10-16: plain numbers are
    /// easier to read), then 確認. xlsx was ERP's default on the user's PC; on the ERP server it
    /// showed 「Excel 97-2003 試算表(*.xls)」 (V1.0.2 LOG, 2026-10-27), so it is picked when needed.
    /// </summary>
    private async Task FillSaveDialogAsync(nint dialog, ErpReport report, CancellationToken cancellationToken)
    {
        var format = Win32Automation.InputControls(dialog, visibleOnly: true).FirstOrDefault(c => Invi02Layout.IsCombo(c.ClassName));
        var formatText = format is null ? string.Empty : ComboText(format);
        _log.Info("sales", $"{report.Code} save format=\"{formatText}\" class={format?.ClassName ?? "-"}");
        if (format is not null && !IsXlsx(formatText))
            await SelectXlsxFormatAsync(dialog, format, report, cancellationToken);

        var plain = Win32Automation.EnumerateChildren(dialog)
            .FirstOrDefault(c => c.Visible && Invi02Layout.NormalizeCaption(c.Text).StartsWith(SalesReportLayout.PlainNumbers, StringComparison.Ordinal));
        if (plain is not null) SafeClick(dialog, new Point(plain.Rect.Left + 8, (plain.Rect.Top + plain.Rect.Bottom) / 2));
        else
        {
            if (!NativeMethods.GetWindowRect(dialog, out var d))
                throw new InvalidOperationException("讀不到「存成EXCEL檔」視窗的位置。");
            await OcrClickAsync(dialog, d.ToRectangle(), "無千分符號", "「存成EXCEL檔」的「顯示數值欄位無千分符號」", cancellationToken);
        }
        await Pace.Delay(150, cancellationToken);
        await ClickButtonAsync(dialog, SalesReportLayout.SaveConfirm, "「存成EXCEL檔」的「確認」", cancellationToken);
        _log.Info("sales", $"{report.Code} save 確認 clicked");
    }

    private static bool IsXlsx(string format) => format.Contains("xlsx", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Picks the xlsx item of the 存成EXCEL檔 format box, a standard Windows combo box (TComboBox
    /// with a ComboLBox list, V1.0.5 LOG 2026-10-27). Its items are read with CB_GETLBTEXT; the list
    /// is opened with a click and the item reached with Up／Down from the current one, then Enter,
    /// the way a person picks it (ERP sees its usual change notification). Read back afterwards.
    /// </summary>
    private async Task SelectXlsxFormatAsync(nint dialog, WindowControl format, ErpReport report, CancellationToken cancellationToken)
    {
        var items = NativeMethods.ComboItems(format.Handle) ?? [];
        var current = (int)(NativeMethods.QueryNumber(format.Handle, NativeMethods.CB_GETCURSEL) ?? -1);
        var target = items.ToList().FindIndex(IsXlsx);
        _log.Info("sales", $"{report.Code} format items=\"{string.Join(" | ", items)}\" current={current} xlsx={target}");
        if (target < 0 || current < 0)
            throw new InvalidOperationException($"「存成EXCEL檔」的檔案格式清單找不到 xlsx（目前「{ComboText(format)}」），已停止。");

        var before = Win32Automation.VisibleProcessWindows(dialog);
        SafeClick(dialog, Win32Automation.Center(format.Rect));
        nint popup = 0;
        await WaitUntilAsync(() => (popup = Win32Automation.VisibleProcessWindows(dialog).FirstOrDefault(h => !before.Contains(h))) != 0 || IsDropDownOpen(dialog, format), 800, cancellationToken);
        var key = target > current ? NativeMethods.VK_DOWN : NativeMethods.VK_UP;
        for (var i = 0; i < Math.Abs(target - current); i++)
        {
            InputSender.Press(key);
            await Pace.Delay(100, cancellationToken);
        }
        if (IsDropDownOpen(dialog, format) || (popup != 0 && NativeMethods.IsWindowVisible(popup)))
            InputSender.Press(NativeMethods.VK_RETURN);
        await Pace.Delay(200, cancellationToken);
        var shown = ComboText(format);
        _log.Info("sales", $"{report.Code} format shown=\"{shown}\"");
        if (!IsXlsx(shown))
            throw new InvalidOperationException($"「存成EXCEL檔」的檔案格式無法改成 xlsx（目前「{shown}」），已停止。");
    }

    /// <summary>Presses the message box's only button (Yes): the button window, else Enter on the box.</summary>
    private async Task DismissMessageAsync(nint box, CancellationToken cancellationToken)
    {
        if (FindButton(box, _ => true) is { } button) SafeClick(box, Win32Automation.Center(button.Rect));
        else if (Win32Automation.PrepareForeground(box, _log)) InputSender.Press(NativeMethods.VK_RETURN);
        if (!await WaitUntilAsync(() => !NativeMethods.IsWindow(box) || !NativeMethods.IsWindowVisible(box), 3000, cancellationToken))
            throw new InvalidOperationException("ERP 的匯出結果訊息沒有關閉，已停止。");
    }
}
