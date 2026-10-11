using System.Drawing;

namespace CYERPAutoInput;

/// <summary>One section of the Shopee order page, re-based to its own top-left corner.</summary>
internal sealed record ShopeePageSection(ShopeeShotKind Kind, Rectangle PageArea, List<OcrBox> Lines);

/// <summary>
/// Pure parts of the automatic Shopee page scan (user, 2026-10-11): the order page is a
/// column of cards that always come in the same order — 訂單編號／運送資訊, 進帳資訊 (the
/// product table, then 隱藏入帳明細 and the income breakdown), …, 買家實付金額. CY scrolls the
/// browser from the top, measures each scroll from the pixels, places every capture's OCR
/// boxes on one page, and cuts out the three parts the parser needs.
/// </summary>
internal static class ShopeePageScan
{
    /// <summary>
    /// How far the page moved between two captures, from per-row brightness signatures of the
    /// content column: <paramref name="next"/> row i shows what <paramref name="previous"/>
    /// row i + d showed. Rows before <paramref name="from"/> (browser bar, Shopee's fixed
    /// header) are ignored. Returns 0 when the page did not move and -1 when no shift fits.
    /// </summary>
    public static int FindScrollOffset(IReadOnlyList<long> previous, IReadOnlyList<long> next, int from)
    {
        // Rows are compared exactly, but only rows with content (an edge against the row above)
        // count, and most of them — not all — must match: after a scroll Shopee's header grows a
        // shadow and hover effects change a few rows (user's PC, 2026-10-11).
        var rows = Math.Min(previous.Count, next.Count);
        var best = -1;
        var bestScore = 0.0;
        for (var d = 0; d < rows - from - 40; d++)
        {
            int informative = 0, equal = 0;
            for (var i = from + 1; i < rows - d; i++)
            {
                if (next[i] == next[i - 1]) continue;
                informative++;
                if (next[i] == previous[i + d]) equal++;
            }
            if (informative < 8) continue; // a blank stretch matches any shift
            var score = equal / (double)informative;
            if (score > bestScore) { bestScore = score; best = d; }
        }
        return bestScore >= 0.6 ? best : -1;
    }

    /// <summary>
    /// The scroll measured from the OCR text instead: boxes with the same text and left edge in
    /// both captures, below the fixed band, moved up by the same distance. The most common
    /// distance wins when at least three boxes agree; -1 otherwise.
    /// </summary>
    public static int FindScrollOffsetByText(IReadOnlyList<OcrBox> previous, IReadOnlyList<OcrBox> next, int fixedTop)
    {
        var shifts = new List<int>();
        foreach (var n in next.Where(b => b.Top >= fixedTop && Compact(b.Text).Length >= 3))
            foreach (var p in previous.Where(b => b.Top >= fixedTop && Compact(b.Text) == Compact(n.Text) && Math.Abs(b.Left - n.Left) <= 4))
                if (p.Top - n.Top >= 0) shifts.Add(p.Top - n.Top);
        var best = shifts
            .GroupBy(s => shifts.Count(o => Math.Abs(o - s) <= 2))
            .OrderByDescending(g => g.Key)
            .FirstOrDefault();
        if (best is null || best.Key < 3) return -1;
        var centre = best.OrderBy(s => s).ElementAt(best.Count() / 2);
        return (int)Math.Round(shifts.Where(s => Math.Abs(s - centre) <= 2).Average());
    }

    /// <summary>
    /// Top rows that did not move between two captures (browser bar, fixed header). A few
    /// changed rows (a blinking caret) are tolerated; a run of 8 changed rows ends the band.
    /// </summary>
    public static int StaticTopRows(IReadOnlyList<long> previous, IReadOnlyList<long> next)
    {
        var rows = Math.Min(previous.Count, next.Count);
        var end = 0;
        var changedRun = 0;
        for (var i = 0; i < rows; i++)
        {
            if (previous[i] == next[i]) { end = i + 1; changedRun = 0; }
            else if (++changedRun >= 8) break;
        }
        return end;
    }

    /// <summary>Two captures show the same page position (the top 15 %, with the browser bar, is ignored).</summary>
    public static bool SameScreen(IReadOnlyList<long> a, IReadOnlyList<long> b)
    {
        if (a.Count != b.Count) return false;
        var differing = 0;
        for (var i = a.Count * 15 / 100; i < a.Count; i++)
            if (a[i] != b[i] && ++differing > 3) return false;
        return true;
    }

    /// <summary>
    /// Adds one capture's boxes to the page at <paramref name="pageTop"/>, leaving out the
    /// fixed top band and boxes already placed (the overlap with the previous capture).
    /// </summary>
    public static void AddCapture(List<OcrBox> page, IEnumerable<OcrBox> capture, int pageTop, int fixedTop)
    {
        foreach (var box in capture)
        {
            if (box.Top < fixedTop) continue;
            var placed = box with { Top = box.Top + pageTop };
            // The overlap is read twice and the OCR may read it differently each time
            // (「商品選項貨號」 vs 「商品項货號」), so an earlier box at the same place wins.
            if (page.Any(p => Overlap(p, placed) >= 0.5)) continue;
            page.Add(placed);
        }
    }

    /// <summary>True once the 買家實付金額 card's 買家總支付金額 line and its amount are on the page.</summary>
    public static bool HasBuyerTotal(IReadOnlyList<OcrBox> page)
    {
        var card = page.FirstOrDefault(b => Compact(b.Text).Contains("買家實付金額", StringComparison.Ordinal));
        if (card is null) return false;
        var total = page.FirstOrDefault(b => b.Top > card.Top && Compact(b.Text).Contains("買家總支付金額", StringComparison.Ordinal));
        return total is not null && page.Any(b => b.Left > total.Right && Math.Abs(b.CenterY - total.CenterY) <= Math.Max(12, total.Height) &&
                                               Compact(b.Text).Contains("NT", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The three parts of the page: 訂單編號 to 共 N 個商品; the product table from its header
    /// to 隱藏入帳明細; the 買家實付金額 card to 買家總支付金額. Each comes back in its own
    /// coordinates, as a screenshot of just that part would read.
    /// </summary>
    public static List<ShopeePageSection> Sections(IReadOnlyList<OcrBox> page, List<string> problems)
    {
        var sections = new List<ShopeePageSection>();
        OcrBox? Find(Func<string, bool> match, int below = int.MinValue) =>
            page.Where(b => b.Top > below && match(Compact(b.Text))).OrderBy(b => b.Top).FirstOrDefault();

        // 訂單編號 … 共 N 個商品 (the 運送資訊 block sits between them).
        var orderLabel = Find(t => t.Contains("訂單編號", StringComparison.Ordinal));
        if (orderLabel is null) problems.Add("頁面上找不到「訂單編號」。");
        else
        {
            var end = Find(t => t.Contains("個商品", StringComparison.Ordinal), orderLabel.Top)?.Bottom ?? orderLabel.Bottom + 260;
            sections.Add(Cut(ShopeeShotKind.Order, Rectangle.FromLTRB(orderLabel.Left - 50, orderLabel.Top - 12, orderLabel.Left + 900, end + 12)));
        }

        // Product table: header 單價／數量／小計 … 隱藏入帳明細. The OCR may drop a header glyph
        // (「小計」 read as 「小」; user's PC, 2026-10-11), so 單價 alone anchors the header and
        // the right edge falls back to the rightmost number column below it.
        var unit = Find(t => t == "單價");
        if (unit is null) problems.Add("頁面上找不到商品表的表頭（單價）。");
        else
        {
            bool OnHeaderRow(OcrBox b) => Math.Abs(b.CenterY - unit.CenterY) <= 20 && b.Left > unit.Right;
            var quantity = page.FirstOrDefault(b => OnHeaderRow(b) && Compact(b.Text) is "數量" or "數" or "量");
            var subtotal = page.Where(b => OnHeaderRow(b) && Compact(b.Text) is "小計" or "小" or "計").OrderByDescending(b => b.Right).FirstOrDefault();
            var number = page.Where(b => Compact(b.Text) == "編" && b.Right < unit.Left && Math.Abs(b.CenterY - unit.CenterY) <= 40).FirstOrDefault();
            var codes = page.Where(b => b.Top > unit.Top && Compact(b.Text).Contains("貨號", StringComparison.Ordinal)).ToList();
            var left = number is not null ? number.Left - 25 : codes.Count > 0 ? codes.Min(c => c.Left) - 140 : unit.Left - 700;
            var stop = Find(t => t.Contains("入帳明細", StringComparison.Ordinal) || t.StartsWith("訂單金額", StringComparison.Ordinal), unit.Top);
            if (stop is null) problems.Add("頁面上找不到商品表的結尾（隱藏入帳明細）。");
            var bottom = stop?.Top - 4 ?? (codes.Count > 0 ? codes.Max(c => c.Bottom) + 20 : unit.Bottom + 200);
            // Numbers of the table only: right of 單價, within about three column widths.
            var columnStep = quantity is not null ? quantity.Left - unit.Left : 80;
            var tableNumbers = page.Where(b => b.Top > unit.Bottom && b.Bottom < bottom && b.Left > unit.Left - 20 &&
                                               b.Left < unit.Left + columnStep * 3 && ShopeeScreenshot.TryNumber(b.Text, out _, digitsOnly: true)).ToList();
            var right = subtotal?.Right ?? (tableNumbers.Count > 0 ? tableNumbers.Max(b => b.Right) : unit.Right + columnStep * 2);
            sections.Add(Cut(ShopeeShotKind.Items, Rectangle.FromLTRB(left, unit.Top - 30, right + 40, bottom)));
        }

        // 買家實付金額 … 買家總支付金額 and its amount.
        var buyerCard = Find(t => t.Contains("買家實付金額", StringComparison.Ordinal));
        var total = buyerCard is null ? null : Find(t => t.Contains("買家總支付金額", StringComparison.Ordinal), buyerCard.Top);
        if (buyerCard is null || total is null) problems.Add("頁面上找不到「買家實付金額」與「買家總支付金額」。");
        else
        {
            var right = page.Where(b => b.Top >= buyerCard.Top && b.Bottom <= total.Bottom + 30 && b.Left > total.Right).Select(b => b.Right).DefaultIfEmpty(total.Right + 300).Max();
            sections.Add(Cut(ShopeeShotKind.Amounts, Rectangle.FromLTRB(buyerCard.Left - 40, buyerCard.Top - 12, right + 30, total.Bottom + 30)));
        }
        return sections;

        ShopeePageSection Cut(ShopeeShotKind kind, Rectangle area)
        {
            var lines = page
                .Where(b => b.Left >= area.Left && b.Right <= area.Right && b.Top >= area.Top && b.Bottom <= area.Bottom)
                .OrderBy(b => b.Top).ThenBy(b => b.Left)
                .Select(b => b with { Left = b.Left - area.Left, Top = b.Top - area.Top })
                .ToList();
            return new ShopeePageSection(kind, area, lines);
        }
    }

    private static double Overlap(OcrBox a, OcrBox b)
    {
        var w = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        var h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        if (w <= 0 || h <= 0) return 0;
        return w * (double)h / Math.Max(1, Math.Min(a.Width * a.Height, b.Width * b.Height));
    }

    private static string Compact(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
}
