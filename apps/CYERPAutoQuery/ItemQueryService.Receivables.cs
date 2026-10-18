using System.Diagnostics;

namespace CYERPAutoQuery;

/// <summary>
/// 未收款查詢 (user, 2026-10-17): ACRR18 未結案應收明細表, driven like COPR15 (<see cref="RunReportAsync"/>).
/// 基本選項: for one customer (or 業務員) tick 區間選擇 (unticked by default) and fill 起＝迄＝code; for
/// everything leave both groups empty. 帳款截止日期 keeps ERP's default (today).
/// </summary>
internal sealed partial class ItemQueryService
{
    private const uint LB_GETCOUNT = 0x018B;

    public Task<ReceivableResult> QueryReceivablesAsync(ReceivableQuery query, IProgress<string> progress, CancellationToken cancellationToken) =>
        GuardErpMessagesAsync(() => QueryReceivablesCoreAsync(query, progress, cancellationToken));

    private async Task<ReceivableResult> QueryReceivablesCoreAsync(ReceivableQuery query, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var report = ReceivableLayout.Report;
        var sheets = await RunReportAsync(report, query.Describe(), (dialog, ct) => FillReceivableConditionsAsync(dialog, report, query, ct),
            progress, cancellationToken);
        var bills = sheets is null ? [] : ReceivableExport.Parse(sheets, query);
        var result = new ReceivableResult(query, bills, watch.Elapsed);
        _log.Info("receivable", $"scope={query.Scope} bills={bills.Count} customers={result.CustomerCount} ms={watch.ElapsedMilliseconds}");
        return result;
    }

    private async Task FillReceivableConditionsAsync(nint dialog, ErpReport report, ReceivableQuery query, CancellationToken cancellationToken)
    {
        await ShowReportTabAsync(dialog, report, ReceivableLayout.BasicTab, cancellationToken);
        var (target, other) = query.Scope switch
        {
            ReceivableScope.Customer => (ReceivableLayout.CustomerGroup, ReceivableLayout.SalespersonGroup),
            ReceivableScope.Salesperson => (ReceivableLayout.SalespersonGroup, ReceivableLayout.CustomerGroup),
            _ => ((string?)null, (string?)null),
        };
        if (target is null)
        {
            // Everything: neither group may hold a value left in ERP, or the report would be narrowed.
            EnsureGroupEmpty(dialog, report, ReceivableLayout.CustomerGroup);
            EnsureGroupEmpty(dialog, report, ReceivableLayout.SalespersonGroup);
            return;
        }
        EnsureGroupEmpty(dialog, report, other!);
        var boxes = await OpenRangeAsync(dialog, report, target, cancellationToken);
        var name = target == ReceivableLayout.CustomerGroup ? "客戶代號" : "業務員代號";
        await SetReportTextAsync(dialog, boxes[0], query.Code, $"{report.Title}的{name}起", cancellationToken);
        await SetReportTextAsync(dialog, boxes[1], query.Code, $"{report.Title}的{name}迄", cancellationToken);
    }

    private static WindowControl? FindGroup(nint dialog, string caption) =>
        Win32Automation.EnumerateChildren(dialog).FirstOrDefault(c => c.Visible && Invi02Layout.NormalizeCaption(c.Text) == caption);

    /// <summary>The 起／迄 boxes a ticked 區間選擇 shows (unticked the group holds a list instead).</summary>
    private static List<WindowControl> RangeBoxes(WindowControl group) =>
        Win32Automation.InputControls(group.Handle, visibleOnly: true)
            .Where(c => !Invi02Layout.IsCheckBox(c.ClassName))
            .OrderBy(c => c.Rect.Top).ThenBy(c => c.Rect.Left).ToList();

    /// <summary>
    /// Ticks the group's 區間選擇 when its 起／迄 boxes are not shown (user, 2026-10-17: unticked by
    /// default in ACRR18). Ticked means the two boxes appeared; the check box itself is not read.
    /// </summary>
    private async Task<List<WindowControl>> OpenRangeAsync(nint dialog, ErpReport report, string caption, CancellationToken cancellationToken)
    {
        var group = FindGroup(dialog, caption);
        if (group is null)
        {
            LogChildren(dialog, report.Code);
            throw new InvalidOperationException($"{report.Title}找不到「{caption}」。");
        }
        if (RangeBoxes(group) is { Count: 2 } shown) return shown;

        var check = Win32Automation.EnumerateChildren(group.Handle).FirstOrDefault(c => c.Visible && Invi02Layout.IsCheckBox(c.ClassName));
        if (check is null)
        {
            LogChildren(dialog, report.Code);
            throw new InvalidOperationException($"{report.Title}的「{caption}」找不到「區間選擇」。");
        }
        SafeClick(dialog, new Point(check.Rect.Left + 8, (check.Rect.Top + check.Rect.Bottom) / 2));
        _log.Info("report", $"{report.Code} {caption} 區間選擇 clicked class={check.ClassName}");
        List<WindowControl> boxes = [];
        if (!await WaitUntilAsync(() => (boxes = RangeBoxes(group)).Count == 2, AfterActionWaitMs, cancellationToken))
        {
            LogChildren(dialog, report.Code);
            throw new InvalidOperationException($"已勾選{report.Title}「{caption}」的區間選擇，但沒有出現起、迄欄位（找到 {boxes.Count} 個）。");
        }
        return boxes;
    }

    /// <summary>A group that must not narrow the report: its 起／迄 boxes empty, its list without items.</summary>
    private void EnsureGroupEmpty(nint dialog, ErpReport report, string caption)
    {
        var group = FindGroup(dialog, caption);
        if (group is null)
        {
            LogChildren(dialog, report.Code);
            throw new InvalidOperationException($"{report.Title}找不到「{caption}」。");
        }
        // A masked box shows 「    /  /  」 or 「    -」 when empty: only letters and digits count as a value.
        var filled = RangeBoxes(group).Select(b => NativeMethods.ControlText(b.Handle) ?? string.Empty).Where(t => t.Any(char.IsLetterOrDigit)).ToList();
        var items = Win32Automation.EnumerateChildren(group.Handle)
            .Where(c => c.Visible && c.ClassName.Contains("ListBox", StringComparison.OrdinalIgnoreCase))
            .Sum(c => Math.Max(0, (int)(NativeMethods.QueryNumber(c.Handle, LB_GETCOUNT) ?? 0)));
        _log.Info("report", $"{report.Code} {caption} empty-check boxes={filled.Count} list_items={items}");
        if (filled.Count > 0 || items > 0)
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"{report.Title}的「{caption}」已有條件，這次沒有指定。\n請在 ERP 清空後再查詢。");
    }
}
