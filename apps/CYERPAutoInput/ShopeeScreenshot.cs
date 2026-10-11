using System.Globalization;
using System.Text.RegularExpressions;

namespace CYERPAutoInput;

/// <summary>One recognized text box of a screenshot (pixels of the original image).</summary>
internal sealed record OcrBox(string Text, int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public int CenterX => Left + Width / 2;
    public int CenterY => Top + Height / 2;
}

internal enum ShopeeShotKind { Unknown, Order, Items, Amounts }

/// <summary>A cell to read again with the recognizer alone (see <see cref="ShopeeScreenshot.MissingCellProbes"/>).</summary>
internal sealed record CellProbe(int Left, int Top, int Width, int Height, string Purpose);

internal sealed record ShopeeShotOrder(string OrderSn, string Carrier, string TrackingNumber);

/// <summary>
/// One product of the order detail: its 商品選項貨號 with a set suffix (Y0033x2) resolved
/// into 數量 × 2 and 單價 ÷ 2, and the product's amount before promotions (單價 × 數量 as
/// shown). <see cref="Group"/> is the 編號 it is listed under (0 when no 編號 was visible).
/// </summary>
internal sealed record ShopeeShotItem(int Group, string ItemCode, decimal UnitPrice, decimal Quantity, decimal Amount);

/// <summary>A 編號 of the detail: several products of one promotion share one 小計 (shown, and the struck-out original).</summary>
internal sealed record ShopeeShotGroup(int Index, decimal Shown, decimal Original);

/// <summary>買家實付金額 block. Coupons and 蝦幣 are negative.</summary>
internal sealed record ShopeeShotAmounts(decimal OrderAmount, decimal Shipping, decimal Coupons, decimal Coins, decimal Total,
    IReadOnlyList<string> OtherDeductions);

internal sealed record ShopeeShotMapping(
    string OrderType,
    string CustomerCode,
    string NotePrefix,
    string DiscountItemCode,
    string ShippingItemCode,
    string PromotionItemCode,
    IReadOnlyDictionary<string, string> FreightTypes);

internal sealed record ShopeeShotResult(ImportedOrder? Order, IReadOnlyList<string> Problems);

/// <summary>
/// Reads a Shopee order from screenshots of the seller center order page (user,
/// 2026-10-11): the order block (訂單編號, 運送資訊), one or more detail blocks (編號, 商品,
/// 單價, 數量, 小計, 商品選項貨號) and the 買家實付金額 block. Rules (user): 優惠券 of every kind
/// become one 折價券 line; 蝦幣折抵 is not a line but lowers 代收貨款 (＝買家總支付金額);
/// 運費 becomes the shipping line and 運費 field; struck-out promotion prices add up to one
/// 促銷組合 line (Σ單價×數量 − 訂單金額). Pure: no OCR engine or UI, unit tested on recognized boxes.
/// </summary>
internal static partial class ShopeeScreenshot
{
    /// <summary>Joins word-level boxes into phrases: same row and only a small gap apart.</summary>
    public static List<OcrBox> MergeLines(IEnumerable<OcrBox> boxes)
    {
        var rows = new List<List<OcrBox>>();
        foreach (var box in boxes.Where(b => b.Text.Trim().Length > 0).OrderBy(b => b.CenterY))
        {
            var row = rows.FirstOrDefault(r => Math.Abs(r.Average(b => b.CenterY) - box.CenterY) <= Math.Max(6, box.Height / 2));
            if (row is null) rows.Add([box]);
            else row.Add(box);
        }
        var phrases = new List<OcrBox>();
        foreach (var row in rows)
        {
            OcrBox? current = null;
            foreach (var box in row.OrderBy(b => b.Left))
            {
                if (current is not null && box.Left - current.Right <= Math.Max(10, box.Height * 0.9))
                {
                    var left = Math.Min(current.Left, box.Left);
                    var top = Math.Min(current.Top, box.Top);
                    current = new OcrBox(current.Text + box.Text, left, top,
                        Math.Max(current.Right, box.Right) - left, Math.Max(current.Bottom, box.Bottom) - top);
                }
                else
                {
                    if (current is not null) phrases.Add(current);
                    current = box with { Text = box.Text.Trim() };
                }
            }
            if (current is not null) phrases.Add(current);
        }
        return phrases.OrderBy(p => p.Top).ThenBy(p => p.Left).ToList();
    }

    public static ShopeeShotKind Classify(IReadOnlyList<OcrBox> lines)
    {
        bool Has(string text) => lines.Any(l => Compact(l.Text).Contains(text, StringComparison.Ordinal));
        if (Has("買家實付金額") || Has("買家總支付金額")) return ShopeeShotKind.Amounts;
        if (Has("訂單編號")) return ShopeeShotKind.Order;
        if (lines.Any(l => CodeLabelPattern().IsMatch(Compact(l.Text))) || (Has("單價") && Has("數量"))) return ShopeeShotKind.Items;
        return ShopeeShotKind.Unknown;
    }

    public static ShopeeShotOrder ParseOrder(IReadOnlyList<OcrBox> lines, List<string> problems)
    {
        var orderSn = string.Empty;
        var label = lines.FirstOrDefault(l => Compact(l.Text).Contains("訂單編號", StringComparison.Ordinal));
        if (label is not null)
        {
            orderSn = lines
                .Where(l => l.Top > label.Top + label.Height / 2 && l.Top - label.Bottom < 80)
                .OrderBy(l => l.Top)
                .Select(l => Compact(l.Text).ToUpperInvariant())
                .FirstOrDefault(t => OrderSnPattern().IsMatch(t)) ?? string.Empty;
        }
        if (orderSn.Length == 0) problems.Add("訂單資訊截圖讀不到訂單編號。");

        var carrier = string.Empty;
        var tracking = string.Empty;
        foreach (var line in lines)
        {
            var text = Compact(line.Text).Replace('＃', '#');
            var hash = text.IndexOf('#');
            var head = hash >= 0 ? text[..hash] : text;
            if (head.StartsWith("包裹", StringComparison.Ordinal) && carrier.Length == 0)
            {
                var colon = head.IndexOfAny([':', '：']);
                carrier = colon >= 0 ? head[(colon + 1)..] : head.TrimStart('包', '裹').TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            }
            if (hash >= 0 && tracking.Length == 0)
            {
                var candidate = new string(text[(hash + 1)..].Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
                if (candidate.Length >= 8) tracking = candidate;
            }
        }
        if (carrier.Length == 0) problems.Add("訂單資訊截圖讀不到物流方式（包裹）。");
        if (tracking.Length == 0) problems.Add("訂單資訊截圖讀不到物流單號；可在表單的送貨地址(一) 補上。");
        return new ShopeeShotOrder(orderSn, carrier, tracking);
    }

    /// <summary>
    /// Detail screenshot. Every 商品選項貨號 line is one product; its 單價 and 數量 are the
    /// numbers in those columns between the previous product's code line and its own. 編號
    /// (left) groups products: a promotion lists several products under one 編號 with one
    /// 小計 (the upper number; a struck-out original below it). The 單價／數量／小計 columns
    /// are found from the numbers' positions, so the header row may be off-screen.
    /// </summary>
    public static (List<ShopeeShotItem> Items, List<ShopeeShotGroup> Groups) ParseItems(
        IReadOnlyList<OcrBox> lines, int imageWidth, List<string> problems)
    {
        var items = new List<ShopeeShotItem>();
        var groups = new List<ShopeeShotGroup>();
        var anchors = lines
            .Where(l => l.CenterX < imageWidth * 0.2 && TryNumber(l.Text, out _, digitsOnly: true) && Compact(l.Text).Length <= 3)
            .OrderBy(l => l.Top)
            .ToList();

        // Numbers in the right half, each assigned to the nearest 單價／數量／小計 column.
        var numbers = lines
            .Where(l => l.CenterX > imageWidth * 0.5 && TryNumber(l.Text, out _, digitsOnly: true))
            .Select(l => (Box: l, Value: decimal.Parse(Compact(l.Text), CultureInfo.InvariantCulture)))
            .ToList();
        var layout = ResolveColumns(lines, imageWidth);
        if (layout is null)
        {
            problems.Add("明細截圖讀不到單價與數量欄；截圖請包含右側的單價、數量、小計。");
            return (items, groups);
        }
        List<(OcrBox Box, decimal Value)> InColumn(double? x) => x is not { } cx ? []
            : numbers.Where(n => Math.Abs(n.Box.CenterX - cx) <= ColumnTolerance && layout.Nearest(n.Box.CenterX) == cx).ToList();
        var unitColumn = InColumn(layout.Unit);
        var quantityColumn = InColumn(layout.Quantity);
        var subtotalColumn = InColumn(layout.Subtotal);

        int GroupOf(int centerY) => anchors.Count == 0 ? 0
            : int.Parse(Compact(anchors.OrderBy(a => Math.Abs(a.CenterY - centerY)).First().Text), CultureInfo.InvariantCulture);

        var codeLines = lines.Where(l => CodeLabelPattern().IsMatch(Compact(l.Text))).OrderBy(l => l.Top).ToList();
        var previousBottom = int.MinValue;
        var used = new HashSet<OcrBox>();
        var noQuantity = new List<(int Group, string RawCode, decimal Unit)>();
        void AddItem(int group, string rawCode, decimal unit, decimal quantity)
        {
            var (code, multiplier) = SplitSet(rawCode);
            if (code.Length == 0) problems.Add("有一項商品讀不到商品選項貨號。");
            // A set price that does not divide evenly is entered to 2 decimals (119 ÷ 3 → 39.67;
            // user, 2026-10-11); checks keep using the screenshot's own amount.
            var setUnit = Math.Round(unit / multiplier, 2, MidpointRounding.AwayFromZero);
            items.Add(new ShopeeShotItem(group, code, setUnit, quantity * multiplier, unit * quantity));
        }
        foreach (var codeLine in codeLines)
        {
            var bandTop = previousBottom;
            var bandBottom = codeLine.Bottom;
            previousBottom = codeLine.Bottom;
            (OcrBox Box, decimal Value)? Pick(List<(OcrBox Box, decimal Value)> column) =>
                column.Where(n => n.Box.CenterY > bandTop && n.Box.CenterY <= bandBottom).OrderBy(n => n.Box.Top).Cast<(OcrBox, decimal)?>().FirstOrDefault();
            var unit = Pick(unitColumn);
            var quantity = Pick(quantityColumn);
            var rawCode = CodeAfterLabel(codeLine, lines);
            if (unit is null)
            {
                problems.Add($"商品「{rawCode}」讀不到單價或數量。");
                continue;
            }
            used.Add(unit.Value.Box);
            if (quantity is null)
            {
                // Settled below from the 小計 once the groups are known.
                noQuantity.Add((GroupOf(unit.Value.Box.CenterY), rawCode, unit.Value.Value));
                continue;
            }
            used.Add(quantity.Value.Box);
            AddItem(GroupOf(unit.Value.Box.CenterY), rawCode, unit.Value.Value, quantity.Value.Value);
        }
        if (unitColumn.Any(n => !used.Contains(n.Box)))
            problems.Add("明細有商品讀不到商品選項貨號，請核對截圖。");

        foreach (var anchor in anchors)
        {
            var index = int.Parse(Compact(anchor.Text), CultureInfo.InvariantCulture);
            var values = subtotalColumn.Where(n => GroupOf(n.Box.CenterY) == index).OrderBy(n => n.Box.Top).Select(n => n.Value).ToList();
            if (values.Count == 0) { problems.Add($"第 {index} 項讀不到小計。"); continue; }
            groups.Add(new ShopeeShotGroup(index, values[0], values[^1]));
        }

        // A 數量 the detector and the re-read both missed (a lone "1"; user's PC, 2026-10-11).
        // When the product is alone under its 編號, its 小計 (original) ÷ 單價 is the quantity;
        // otherwise the user fills it in on the form.
        foreach (var (group, rawCode, unit) in noQuantity)
        {
            var subtotal = groups.FirstOrDefault(g => g.Index == group);
            var alone = items.All(i => i.Group != group) && noQuantity.Count(n => n.Group == group) == 1;
            if (alone && subtotal is not null && unit > 0 && subtotal.Original % unit == 0 && subtotal.Original / unit is > 0 and var derived)
            {
                AddItem(group, rawCode, unit, derived);
                problems.Add($"商品「{rawCode}」讀不到數量，已依小計推算為 {Number(derived)}，請核對。");
            }
            else problems.Add($"商品「{rawCode}」讀不到數量，請在表單補上該商品。");
        }
        return (items, groups);
    }

    /// <summary>
    /// Cells where a number must be but none was read: the 數量 on a row that has a 單價,
    /// and the 編號 beside a 小計. The text detector misses lone short glyphs such as "1"
    /// (seen on the user's PC, 2026-10-11), so the caller reads these cells again with the
    /// recognizer alone. Columns come from the header (單價／數量／小計／編號) or, without
    /// one, from the numbers already read.
    /// </summary>
    public static List<CellProbe> MissingCellProbes(IReadOnlyList<OcrBox> lines, int imageWidth, int imageHeight)
    {
        var probes = new List<CellProbe>();
        var numbers = lines.Where(l => l.CenterX > imageWidth * 0.5 && TryNumber(l.Text, out _, digitsOnly: true)).ToList();
        var layout = ResolveColumns(lines, imageWidth);
        if (layout is null) return probes;
        var unitX = layout.Unit;
        var quantityX = layout.Quantity;
        var subtotalX = layout.Subtotal;
        var anchors = lines.Where(l => l.CenterX < imageWidth * 0.2 && TryNumber(l.Text, out _, digitsOnly: true) && Compact(l.Text).Length <= 3).ToList();
        var anchorX = lines.FirstOrDefault(l => Compact(l.Text) == "編")?.CenterX
            ?? (anchors.Count > 0 ? anchors.Average(a => a.CenterX) : imageWidth * 0.05);

        bool HasNumberAt(IEnumerable<OcrBox> boxes, double x, int y, int tolerance) =>
            boxes.Any(b => TryNumber(b.Text, out _, digitsOnly: true) && Math.Abs(b.CenterX - x) <= 40 && Math.Abs(b.CenterY - y) <= tolerance);
        CellProbe Probe(double x, int centerY, int height, int halfWidth, string purpose)
        {
            var left = Math.Clamp((int)x - halfWidth, 0, Math.Max(0, imageWidth - 1));
            var top = Math.Clamp(centerY - height, 0, Math.Max(0, imageHeight - 1));
            return new CellProbe(left, top, Math.Min(halfWidth * 2, imageWidth - left), Math.Min(height * 2, imageHeight - top), purpose);
        }

        foreach (var unit in numbers.Where(n => Math.Abs(n.CenterX - unitX) <= ColumnTolerance && layout.Nearest(n.CenterX) == unitX))
        {
            var tolerance = Math.Max(10, unit.Height);
            if (quantityX is { } qx && !HasNumberAt(lines, qx, unit.CenterY, tolerance))
                probes.Add(Probe(qx, unit.CenterY, Math.Max(12, unit.Height), 35, "數量"));
        }
        if (subtotalX is { } sx)
        {
            var cells = numbers.Where(n => Math.Abs(n.CenterX - sx) <= ColumnTolerance).OrderBy(n => n.Top).ToList();
            // The upper number of a 小計 cell (a struck-out original sits just below it).
            var tops = cells.Where(c => !cells.Any(o => o != c && o.Top < c.Top && c.Top - o.Top <= 30)).ToList();
            foreach (var top in tops)
                if (!HasNumberAt(anchors, anchorX, top.CenterY, Math.Max(15, top.Height)))
                    probes.Add(Probe(anchorX, top.CenterY, Math.Max(12, top.Height), 25, "編號"));
        }
        return probes;
    }

    private const int ColumnTolerance = 40;

    /// <summary>Center x of the 單價／數量／小計 columns; 數量 or 小計 is null when not on the screenshot.</summary>
    internal sealed record ColumnLayout(double Unit, double? Quantity, double? Subtotal)
    {
        public double Nearest(double x) =>
            new[] { Unit, Quantity, Subtotal }.OfType<double>().OrderBy(c => Math.Abs(c - x)).First();
    }

    /// <summary>
    /// The 單價／數量／小計 columns, from the header when it is on the screenshot. Without a
    /// header they come from the numbers' positions, and a missing column must not shift the
    /// others: with only two number columns the right one is 小計 when it carries a struck-out
    /// original or sits about two column widths from 單價 — a 數量 "1" the detector missed made
    /// the 小計 read as the quantity (450 × 450; user's PC, 2026-10-11).
    /// </summary>
    internal static ColumnLayout? ResolveColumns(IReadOnlyList<OcrBox> lines, int imageWidth)
    {
        double? Header(string name) => lines.FirstOrDefault(l => Compact(l.Text) == name && l.CenterX > imageWidth * 0.5)?.CenterX;
        var numbers = lines.Where(l => l.CenterX > imageWidth * 0.5 && TryNumber(l.Text, out _, digitsOnly: true)).ToList();
        var clusters = new List<List<OcrBox>>();
        foreach (var n in numbers.OrderBy(n => n.CenterX))
        {
            var cluster = clusters.FirstOrDefault(c => Math.Abs(c.Average(x => x.CenterX) - n.CenterX) <= ColumnTolerance);
            if (cluster is null) clusters.Add([n]);
            else cluster.Add(n);
        }

        var unitHeader = Header("單價");
        var quantityHeader = Header("數量");
        var subtotalHeader = Header("小計");
        if (unitHeader is { } hu && (quantityHeader is not null || subtotalHeader is not null))
        {
            // Header columns are spaced alike; one the OCR missed sits midway or one step on.
            var hq = quantityHeader ?? (hu + subtotalHeader!.Value) / 2;
            var hs = subtotalHeader ?? hq + (hq - hu);
            return new ColumnLayout(hu, hq, hs);
        }

        if (clusters.Count == 0) return null;
        double X(List<OcrBox> c) => c.Average(b => b.CenterX);
        if (clusters.Count >= 3) return new ColumnLayout(X(clusters[0]), X(clusters[1]), X(clusters[2]));
        if (clusters.Count == 1) return null;

        var (left, right) = (clusters[0], clusters[1]);
        var gap = X(right) - X(left);
        var height = numbers.Select(n => (double)n.Height).OrderBy(h => h).ElementAt(numbers.Count / 2);
        // A 小計 cell with a promotion shows the price and the struck-out original stacked.
        var stacked = right.Any(c => right.Any(o => o != c && o.Top < c.Top && c.Top - o.Top <= Math.Max(30, height * 2.5)));
        // Neighbouring columns are about 5 text heights apart (單價 → 數量 → 小計).
        var wide = gap > height * 7.5;
        return stacked || wide
            ? new ColumnLayout(X(left), X(left) + gap / 2, X(right))
            : new ColumnLayout(X(left), X(right), null);
    }

    /// <summary>The code after 「商品選項貨號：」, or the next box on the row when OCR split it off.</summary>
    private static string CodeAfterLabel(OcrBox codeLine, IReadOnlyList<OcrBox> lines)
    {
        var text = Compact(codeLine.Text);
        var match = CodeLabelPattern().Match(text);
        var code = text[(match.Index + match.Length)..].TrimStart(':', '：');
        if (code.Length > 0) return code;
        return lines
            .Where(l => l.Left >= codeLine.Right - 4 && Math.Abs(l.CenterY - codeLine.CenterY) <= Math.Max(10, codeLine.Height / 2))
            .OrderBy(l => l.Left)
            .Select(l => Compact(l.Text))
            .FirstOrDefault() ?? string.Empty;
    }

    /// <summary>
    /// A set listed as "Y0033x2", "Y0033*2" or "Y0033 x 2" is item Y0033 two times (user,
    /// 2026-10-11): quantity × 2 and unit price ÷ 2.
    /// </summary>
    internal static (string Code, decimal Multiplier) SplitSet(string code)
    {
        var match = SetSuffixPattern().Match(Compact(code));
        if (!match.Success) return (Compact(code), 1);
        var count = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        return count > 1 ? (match.Groups["code"].Value, count) : (match.Groups["code"].Value, 1);
    }

    public static ShopeeShotAmounts? ParseAmounts(IReadOnlyList<OcrBox> lines, List<string> problems)
    {
        decimal? orderAmount = null, total = null;
        decimal shipping = 0, coupons = 0, coins = 0;
        var others = new List<string>();
        // Each amount belongs to the nearest text on its left in the same row ("(53蝦幣)-NT$53"
        // is read as one box, so a value is matched at the end of a box).
        foreach (var value in lines)
        {
            if (!TryNumber(value.Text, out var amount, digitsOnly: false)) continue;
            var label = lines
                .Where(l => l != value && l.Right <= value.Left + 4 && Math.Abs(l.CenterY - value.CenterY) <= Math.Max(14, value.Height) &&
                            l.Text.Any(c => c > 0x3000) && !TryNumber(l.Text, out _, digitsOnly: false))
                .OrderByDescending(l => l.Right)
                .FirstOrDefault();
            if (label is null) continue;
            var name = Compact(label.Text) + Compact(value.Text);
            if (name.Contains("總支付", StringComparison.Ordinal)) total = amount;
            else if (name.Contains("訂單金額", StringComparison.Ordinal)) orderAmount = amount;
            else if (name.Contains("運費", StringComparison.Ordinal) && amount >= 0) shipping += amount;
            else if (name.Contains("蝦幣", StringComparison.Ordinal)) coins += amount;
            else if (name.Contains("優惠券", StringComparison.Ordinal)) coupons += amount;
            else if (amount < 0)
            {
                coupons += amount;
                others.Add($"{Compact(label.Text)} {Number(amount)}");
            }
        }
        if (orderAmount is null) problems.Add("金額截圖讀不到訂單金額。");
        if (total is null) problems.Add("金額截圖讀不到買家總支付金額。");
        if (orderAmount is null || total is null) return null;
        if (others.Count > 0) problems.Add($"金額截圖有其他折抵（{string.Join("、", others)}），已併入折價券，請確認。");
        if (orderAmount.Value + shipping + coupons + coins != total.Value)
            problems.Add($"金額對不上：訂單金額 {Number(orderAmount.Value)} ＋ 運費 {Number(shipping)} − 優惠 {Number(-coupons)} − 蝦幣 {Number(-coins)} ≠ 買家總支付金額 {Number(total.Value)}，請核對截圖。");
        return new ShopeeShotAmounts(orderAmount.Value, shipping, coupons, coins, total.Value, others);
    }

    /// <summary>Combines the parsed blocks into a form-ready order plus everything to check by hand.</summary>
    public static ShopeeShotResult Combine(ShopeeShotOrder? order, IReadOnlyList<ShopeeShotItem> items, IReadOnlyList<ShopeeShotGroup> groups,
        ShopeeShotAmounts? amounts, ShopeeShotMapping mapping, List<string> problems)
    {
        if (order is null) problems.Add("缺少訂單資訊截圖（訂單編號、運送資訊）。");
        if (amounts is null && !problems.Any(p => p.StartsWith("金額截圖", StringComparison.Ordinal)))
            problems.Add("缺少金額截圖（買家實付金額）。");
        if (items.Count == 0 && !problems.Any(p => p.StartsWith("明細截圖", StringComparison.Ordinal)))
            problems.Add("缺少明細截圖。");
        if (order is null || amounts is null || items.Count == 0) return new ShopeeShotResult(null, problems);

        // Overlapping screenshots show the same product or 編號 twice; keep one of each.
        var lines = items.Distinct().ToList();
        var subtotals = new List<ShopeeShotGroup>();
        foreach (var group in groups.GroupBy(g => g.Index).OrderBy(g => g.Key))
        {
            if (group.Distinct().Count() > 1) problems.Add($"第 {group.Key} 項在兩張截圖讀到的小計不同，請核對。");
            subtotals.Add(group.First());
        }
        var missing = subtotals.Count == 0 ? []
            : Enumerable.Range(1, subtotals.Max(g => g.Index)).Except(subtotals.Select(g => g.Index)).ToList();
        if (missing.Count > 0) problems.Add($"明細缺第 {string.Join("、", missing)} 項，請補截圖或在表單補上。");
        else if (subtotals.Count > 0)
        {
            if (subtotals.Sum(g => g.Shown) != amounts.OrderAmount)
                problems.Add($"明細小計合計 {Number(subtotals.Sum(g => g.Shown))} 與訂單金額 {Number(amounts.OrderAmount)} 不同，請核對。");
            if (subtotals.Sum(g => g.Original) != lines.Sum(l => l.Amount))
                problems.Add($"商品單價×數量合計 {Number(lines.Sum(l => l.Amount))} 與小計（原價）合計 {Number(subtotals.Sum(g => g.Original))} 不同，可能漏讀商品，請核對。");
        }

        var formItems = new List<ImportedItem>();
        foreach (var group in lines.GroupBy(l => (l.ItemCode, l.UnitPrice)))
            formItems.Add(new ImportedItem(group.Key.ItemCode, Number(group.Sum(l => l.Quantity)), Number(group.Key.UnitPrice)));

        var promotion = lines.Sum(l => l.Amount) - amounts.OrderAmount;
        if (promotion < 0) problems.Add($"單價×數量合計低於訂單金額 {Number(-promotion)}，請核對明細。");
        // A 促銷組合 is only as right as the products' 單價×數量: when that disagrees with the
        // 小計 originals (a misread quantity), leave the line out rather than enter a wild amount.
        var itemsAgree = missing.Count == 0 && subtotals.Count > 0 && subtotals.Sum(g => g.Original) == lines.Sum(l => l.Amount);
        if (promotion > 0 && !itemsAgree)
        {
            problems.Add($"明細數字對不起來，沒有加入促銷組合（推算 {Number(promotion)}），請核對數量後在表單補上。");
            promotion = 0;
        }
        void AddLine(string code, decimal price, string what)
        {
            if (code.Length == 0) problems.Add($"請在設定填入{what}品號（本張有{what} {Number(price)}）。");
            formItems.Add(new ImportedItem(code, "1", Number(price)));
        }
        if (promotion > 0) AddLine(mapping.PromotionItemCode, -promotion, "促銷組合");
        if (amounts.Coupons < 0) AddLine(mapping.DiscountItemCode, amounts.Coupons, "折價券");
        if (amounts.Shipping > 0) AddLine(mapping.ShippingItemCode, amounts.Shipping, "運費");

        var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["order_type"] = mapping.OrderType,
            ["customer_code"] = mapping.CustomerCode,
            ["trade_note"] = mapping.NotePrefix + order.OrderSn,
            ["cod"] = Number(amounts.Total)
        };
        var freightType = FreightCarriers.Match(order.Carrier, mapping.FreightTypes);
        if (freightType.Length > 0) header["freight_type"] = freightType;
        else if (order.Carrier.Length > 0) problems.Add($"物流「{order.Carrier}」沒有設定貨運別，請在表單填寫。");
        if (amounts.Shipping > 0) header["freight_fee"] = Number(amounts.Shipping);

        var imported = new ImportedOrder
        {
            Source = ShopeeOrderImport.Source,
            OrderSn = order.OrderSn,
            Header = header,
            Items = formItems,
            Carrier = order.Carrier,
            TrackingNumber = order.TrackingNumber
        };
        return new ShopeeShotResult(order.OrderSn.Length > 0 ? imported : null, problems);
    }

    /// <summary>
    /// An amount as OCR reads it: "NT$594", "-NT$25", "-NTS12", "NT$O" (zero read as O),
    /// "1,200". With <paramref name="digitsOnly"/> the text must be a plain number.
    /// </summary>
    internal static bool TryNumber(string text, out decimal value, bool digitsOnly)
    {
        value = 0;
        var t = Compact(text).Replace('－', '-').Replace(",", string.Empty);
        if (digitsOnly) return DigitsPattern().IsMatch(t) && decimal.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        var match = AmountPattern().Match(t);
        if (!match.Success) return false;
        var digits = match.Groups["n"].Value.Replace('O', '0').Replace('o', '0');
        if (!decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) return false;
        if (match.Groups["neg"].Success) value = -value;
        return true;
    }

    /// <summary>A re-read cell as digits: OCR's look-alikes of 1 and 0 are mapped; anything else is rejected.</summary>
    internal static bool TryProbeDigits(string text, out string digits)
    {
        var mapped = new string(Compact(text).Select(c => c switch
        {
            'I' or 'l' or '|' or 'i' or '丨' => '1',
            'O' or 'o' or 'Q' or 'D' => '0',
            _ => c
        }).ToArray());
        digits = mapped;
        return DigitsPattern().IsMatch(mapped) && mapped.Length <= 3;
    }

    private static string Compact(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static string Number(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^[0-9A-Z]{10,20}$")]
    private static partial Regex OrderSnPattern();

    [GeneratedRegex(@"(貨|货)(號|号)")]
    private static partial Regex CodeLabelPattern();

    [GeneratedRegex(@"^(?<code>.*[0-9A-Za-z])[xX×*＊](?<n>\d{1,3})$")]
    private static partial Regex SetSuffixPattern();

    [GeneratedRegex(@"^\d{1,7}$")]
    private static partial Regex DigitsPattern();

    [GeneratedRegex(@"(?<neg>-)?NT[\$S]?(?<n>[0-9Oo]+(\.\d+)?)$")]
    private static partial Regex AmountPattern();
}
