using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace CYERPAutoInput;

internal enum MoPdfKind { Labels, Summary, Image, Unknown }

/// <summary>One MO店+ shipping PDF: the 寄件單 pairs it carries (訂單編號 → 宅配單號), or what else it is.</summary>
internal sealed record MoLabelFile(string FileName, MoPdfKind Kind, IReadOnlyList<(string OrderSn, string Tracking)> Labels,
    IReadOnlyList<string> Problems);

/// <summary>
/// MO店+ orders get their tracking numbers after the export (user, 2026-10-13): 批次出貨 makes a
/// 寄件單 PDF (LogisticsLabel, 新竹物流, password like the export) whose labels print
/// "訂單編號:…" under each label pair, after the "D…D" barcode text of its 宅配單號. The
/// 到廠取貨總表 (LogisticsData) lists only 宅配單號 and 出貨單號, no 訂單編號, so it cannot be
/// matched; it is recognised and named, because both PDFs come down together and either may be
/// picked (user). Parsing is pure and unit tested; <see cref="Read"/> only extracts the text.
/// </summary>
internal static class MoShippingLabels
{
    private static readonly Regex Token = new(@"D(?<barcode>\d{8,12})D|訂單編號\s*[:：]?\s*(?<order>\d{14})|宅配單號\s*(?<tracking>\d{8,12})",
        RegexOptions.CultureInvariant);

    public static MoLabelFile Read(string path, string password)
    {
        var pages = new List<string>();
        using (var document = PdfDocument.Open(path, new ParsingOptions { Password = password }))
            foreach (var page in document.GetPages())
                pages.Add(page.Text);
        return Parse(Path.GetFileName(path), pages);
    }

    /// <summary>
    /// Pairs each 訂單編號 with the barcode before it (the text runs label by label: barcode,
    /// …, 訂單編號, then the receipt half). A pair counts only when the same number is also
    /// printed as 宅配單號 on that page; a barcode without an order is reported.
    /// </summary>
    public static MoLabelFile Parse(string fileName, IReadOnlyList<string> pages)
    {
        var all = string.Join("\n", pages);
        // 全家 labels are one picture per page, without text (user's sample, 2026-10-13).
        if (all.Trim().Length == 0) return new(fileName, MoPdfKind.Image, [], []);
        if (all.Contains("到廠取貨總表", StringComparison.Ordinal))
            return new(fileName, MoPdfKind.Summary, [], []);

        var labels = new List<(string, string)>();
        var problems = new List<string>();
        foreach (var page in pages)
        {
            var printed = new HashSet<string>(StringComparer.Ordinal);
            var barcodes = new List<string>();
            var paired = new HashSet<string>(StringComparer.Ordinal);
            string? last = null;
            foreach (Match m in Token.Matches(page))
            {
                if (m.Groups["tracking"].Success) printed.Add(m.Groups["tracking"].Value);
                else if (m.Groups["barcode"].Success) { last = m.Groups["barcode"].Value; barcodes.Add(last); }
                else if (last is null) problems.Add($"訂單 {m.Groups["order"].Value} 找不到宅配單號。");
                else
                {
                    labels.Add((m.Groups["order"].Value, last));
                    paired.Add(last);
                    last = null;
                }
            }
            foreach (var (order, tracking) in labels.ToList())
                if (paired.Contains(tracking) && !printed.Contains(tracking))
                {
                    labels.Remove((order, tracking));
                    problems.Add($"訂單 {order} 的宅配單號 {tracking} 與寄件單上印的不一致。");
                }
            foreach (var barcode in barcodes.Distinct().Where(b => !paired.Contains(b)))
                problems.Add($"宅配單號 {barcode} 找不到訂單編號。");
        }
        var kind = labels.Count > 0 || problems.Count > 0 ? MoPdfKind.Labels : MoPdfKind.Unknown;
        return new(fileName, kind, labels, problems);
    }

    /// <summary>
    /// Fills the orders' tracking numbers from the labels. Anything that does not match one to
    /// one is listed, never guessed: an order on two labels with different numbers, a label
    /// whose order is not in the export, a tracking number the export already holds otherwise.
    /// </summary>
    public static (List<MoOrder> Orders, List<string> Messages) Apply(IReadOnlyList<MoOrder> orders, IReadOnlyList<MoLabelFile> files)
    {
        var messages = new List<string>();
        var byOrder = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            switch (file.Kind)
            {
                case MoPdfKind.Summary:
                    messages.Add($"{file.FileName}：這是到廠取貨總表，上面沒有訂單編號，已略過（物流單號請用寄件單 LogisticsLabel）。");
                    continue;
                case MoPdfKind.Image:
                    messages.Add($"{file.FileName}：這個 PDF 是圖片（例如全家寄件單），目前只支援新竹物流寄件單，已略過；物流單號請自行填寫。");
                    continue;
                case MoPdfKind.Unknown:
                    messages.Add($"{file.FileName}：不是 MO店+ 寄件單，已略過。");
                    continue;
            }
            messages.AddRange(file.Problems.Select(p => $"{file.FileName}：{p}"));
            foreach (var (order, tracking) in file.Labels)
            {
                if (!byOrder.TryGetValue(order, out var set)) byOrder[order] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(tracking);
            }
        }
        if (!files.Any(f => f.Kind == MoPdfKind.Labels)) return (orders.ToList(), messages);

        var known = orders.Select(o => o.OrderSn).ToHashSet(StringComparer.Ordinal);
        foreach (var order in byOrder.Keys.Where(k => !known.Contains(k)))
            messages.Add($"寄件單上的訂單 {order} 不在匯出檔裡。");

        var result = new List<MoOrder>();
        foreach (var order in orders)
        {
            if (!byOrder.TryGetValue(order.OrderSn, out var numbers))
            {
                if (order.TrackingNumber.Length == 0) messages.Add($"訂單 {order.OrderSn} 沒有寄件單，物流單號空白。");
                result.Add(order);
            }
            else if (numbers.Count > 1)
            {
                messages.Add($"訂單 {order.OrderSn} 在寄件單上有不同的宅配單號（{string.Join("、", numbers)}），請自行填寫。");
                result.Add(order);
            }
            else if (order.TrackingNumber.Length > 0 && order.TrackingNumber != numbers.First())
            {
                messages.Add($"訂單 {order.OrderSn} 匯出檔的物流單號 {order.TrackingNumber} 與寄件單 {numbers.First()} 不同，保留匯出檔的。");
                result.Add(order);
            }
            else result.Add(order with { TrackingNumber = numbers.First() });
        }
        return (result, messages);
    }
}
