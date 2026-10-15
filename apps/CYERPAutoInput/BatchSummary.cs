using System.Text.RegularExpressions;

namespace CYERPAutoInput;

/// <summary>One part of a batch's closing list: a heading and its lines (left out when empty).</summary>
internal sealed record BatchSummarySection(string Heading, IReadOnlyList<string> Items);

/// <summary>One row of the closing list's table: what happened, the order, the rest.</summary>
internal sealed record BatchSummaryRow(string Status, string OrderSn, string Detail)
{
    /// <summary>Done rows start with 已 (已完成, 已用聊聊通知…); every other row needs a person.</summary>
    public bool NeedsPerson => !Status.StartsWith("已", StringComparison.Ordinal);
}

/// <summary>
/// The closing list as table rows (user, 2026-10-15: a table rather than plain text, easier to
/// read). A section's heading gives the status ("已完成 2 張：" → 已完成); a line is split into
/// the order number and the rest at its first 「：」 or 「 → 」 ("訂單 X：…", "X → T01-…"); a line
/// without an order number keeps the whole text as detail. Pure, unit tested.
/// </summary>
internal static class BatchSummary
{
    private static readonly Regex Line = new(@"^(?:訂單\s*)?(?<sn>[0-9A-Z]{6,20})(?:\s*→\s*|：|:|\s*(?=（)|$)(?<rest>.*)$", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public static string Status(string heading)
    {
        var cut = heading.IndexOfAny([' ', '：', '（', ':']);
        var status = (cut > 0 ? heading[..cut] : heading).Trim();
        return Regex.Replace(status, @"\d+$", string.Empty);
    }

    public static List<BatchSummaryRow> Rows(IEnumerable<BatchSummarySection> sections)
    {
        var rows = new List<BatchSummaryRow>();
        foreach (var section in sections.Where(s => s.Items.Count > 0))
        {
            var status = Status(section.Heading);
            foreach (var item in section.Items)
            {
                var text = item.Replace("\r\n", " ").Replace('\n', ' ').Trim();
                var m = Line.Match(text);
                rows.Add(m.Success
                    ? new BatchSummaryRow(status, m.Groups["sn"].Value, m.Groups["rest"].Value.Trim())
                    : new BatchSummaryRow(status, string.Empty, text));
            }
        }
        return rows;
    }

    /// <summary>The table as tab-separated text for 複製 (pastes into Excel as columns).</summary>
    public static string Tsv(string headline, IEnumerable<BatchSummaryRow> rows, string? stoppedAt) =>
        string.Join(Environment.NewLine,
            new[] { headline, "狀態\t訂單編號\t說明" }
                .Concat(rows.Select(r => $"{r.Status}\t{r.OrderSn}\t{r.Detail}"))
                .Concat(stoppedAt is null ? [] : [string.Empty, stoppedAt]));
}
