using System.Globalization;

namespace CYERPAutoQuery;

/// <summary>
/// Pure rules about SMART ERP 庫存狀況查詢作業 (INVQ01), opened from INVI02 through 串查 for the
/// item shown there (user, 2026-10-16). No Win32 calls, so they are unit tested on any OS.
/// </summary>
internal static class Invq01Layout
{
    public const string FormTitle = "庫存狀況查詢作業";
    public const string FormCode = "INVQ01";

    /// <summary>INVI02 Ribbon drop-down that lists the linked programs.</summary>
    public const string LinkMenu = "串查";
    /// <summary>Ribbon group holding 串查 (INVQ01's own Ribbon has it in 導覽; assumed the same in INVI02, else the whole Ribbon is read).</summary>
    public const string LinkMenuGroup = "導覽";
    /// <summary>Ribbon group holding 開始查詢 and 轉出到Excel (V0.3.0 LOG, 2026-10-16).</summary>
    public const string RunGroup = "執行";
    public const string StartQuery = "開始查詢";
    /// <summary>INVQ01's Ribbon export button (INVI02's is 「Excel」 → 「轉出至 Excel」).</summary>
    public const string ExportButton = "轉出到";

    public const string BatchTab = "查各批庫存量";
    public const string HistoryTab = "查歷史異動";
    public const string ResultTab = "查詢結果";
    /// <summary>查歷史異動's group box holding the 起 and 迄 date boxes.</summary>
    public const string DateGroup = "選擇日期";

    // The lower tabs are as wide as their caption. Centers measured from the user's two
    // screenshots (2026-10-16, identical in both), px right of the page control's left edge.
    public static readonly TabStrip Tabs = new(
        ["查各庫庫存量", "查庫存可用量", "查未來異動量", HistoryTab, BatchTab, "查批號異動", "暫出/入狀況查詢", "品號廠商價格查詢", ResultTab],
        Origin: 0, Pitch: 0,
        Centers: [59, 148, 237, 320, 403, 486, 578, 689, 778]);

    /// <summary>
    /// 「庫存狀況查詢作業(INVQ01)[公司]」 before a query, 「庫存狀況查詢作業[公司]-查各批庫存量」 after
    /// (the code disappears), so only the program name is required.
    /// </summary>
    public static bool IsInvq01Title(string title) =>
        title.Contains(FormTitle, StringComparison.Ordinal);

    /// <summary>After 開始查詢 ERP appends the query tab to the caption: 「…-查各批庫存量」.</summary>
    public static bool TitleShowsQuery(string title, string tab) =>
        IsInvq01Title(title) && title.TrimEnd().EndsWith("-" + tab, StringComparison.Ordinal);

    /// <summary>
    /// 批號管理 other than 「N:不需要」 means the item is tracked by batch, so its batches are
    /// looked up (user, 2026-10-16). An empty value counts as not tracked.
    /// </summary>
    public static bool IsBatchManaged(string? batchSetting)
    {
        var value = (batchSetting ?? string.Empty).Trim();
        return value.Length > 0 && !value.StartsWith('N');
    }

    /// <summary>Human-readable date shown in status and messages.</summary>
    public static string ErpDate(DateOnly? date) => date?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "未指定";

    /// <summary>One date to type into INVQ01: which box, whether to click it first, whether Enter follows.</summary>
    internal sealed record HistoryDateAction(int BoxIndex, DateOnly Date, bool Click, bool EnterAfter);

    /// <summary>
    /// The INVQ01 keystroke order confirmed by the user (2026-10-16):
    /// both: click 起, type 起 (yyyyMMdd), Enter (jumps to 迄, no click), type 迄;
    /// 起 only: click 起, type 起; 迄 only: click 迄, type 迄. Unspecified dates are not touched.
    /// </summary>
    public static IReadOnlyList<HistoryDateAction> DateActions(HistoryRange range)
    {
        var actions = new List<HistoryDateAction>(2);
        if (range.From is { } from)
            actions.Add(new HistoryDateAction(0, from, Click: true, EnterAfter: range.To is not null));
        if (range.To is { } to)
            actions.Add(new HistoryDateAction(1, to, Click: range.From is null, EnterAfter: false));
        return actions;
    }

    /// <summary>INVQ01 segmented date edit: physical digit keys without separators, when appropriate.</summary>
    public static string ErpDateKeys(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>True when a date box shows <paramref name="date"/>, whatever separators ERP uses.</summary>
    public static bool ShowsDate(string? shown, DateOnly date) =>
        new string((shown ?? string.Empty).Where(char.IsAsciiDigit).ToArray()) ==
        date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
}

/// <summary>
/// Optional dates for a local INVQ01 history query. An unchecked date picker means
/// do not type that date into ERP. CY Web supplies both dates for Agent jobs.
/// </summary>
internal sealed record HistoryRange(DateOnly? From, DateOnly? To)
{
    /// <summary>
    /// Validate only dates the user supplied. Never invent a one-year range:
    /// an unchecked local date picker stays untouched in INVQ01. There is no maximum span: the
    /// Agent offers the open query and CY Web's module limits the range (user, 2026-10-16).
    /// </summary>
    public static HistoryRange Resolve(DateOnly? from, DateOnly? to, DateOnly today)
    {
        if (from is { } start && start > today)
            throw new ArgumentException("歷史異動的起日不可晚於今天。");
        if (to is { } end && end > today)
            throw new ArgumentException("歷史異動的迄日不可晚於今天。");
        if (from is { } beginning && to is { } ending && beginning > ending)
            throw new ArgumentException("歷史異動的起日不可晚於迄日。");
        return new HistoryRange(from, to);
    }

    /// <summary>yyyy-MM-dd, as CY Web sends and receives it.</summary>
    public static string Iso(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    public static DateOnly? ParseIso(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}

/// <summary>What a query looks up besides INVI02: 查歷史異動 only when asked (user, 2026-10-16).</summary>
internal sealed record ItemQueryOptions(HistoryRange? History)
{
    public static readonly ItemQueryOptions Default = new((HistoryRange?)null);
}
