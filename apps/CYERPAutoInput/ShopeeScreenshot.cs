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
/// <param name="SortKey">For the parts of a bundle: the bundle's first code, so its lines stay together.</param>
/// <param name="Gift">A part marked 「(贈)」: free, its quantity goes to 贈／備品量 (user, 2026-10-20).</param>
internal sealed record ShopeeShotItem(int Group, string ItemCode, decimal UnitPrice, decimal Quantity, decimal Amount, string? SortKey = null,
    bool Gift = false);

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

    /// <summary>The box under 「訂單編號」 that holds the order number, or null.</summary>
    public static OcrBox? OrderSnBox(IReadOnlyList<OcrBox> lines)
    {
        var label = lines.FirstOrDefault(l => Compact(l.Text).Contains("訂單編號", StringComparison.Ordinal));
        if (label is null) return null;
        return lines
            .Where(l => l.Top > label.Top + label.Height / 2 && l.Top - label.Bottom < 80)
            .OrderBy(l => l.Top)
            .FirstOrDefault(l => OrderSnPattern().IsMatch(Compact(l.Text).ToUpperInvariant()));
    }

    public static ShopeeShotOrder ParseOrder(IReadOnlyList<OcrBox> lines, List<string> problems)
    {
        var orderSn = OrderSnBox(lines) is { } snBox ? OrderSnDateDigits(Compact(snBox.Text).ToUpperInvariant()) : string.Empty;
        if (orderSn.Length == 0) problems.Add("訂單資訊截圖讀不到訂單編號。");
        // A Shopee order number is 6 date digits and 8 letters or digits; the OCR once read
        // 「QV」 as 「QWW」 (user's PC, 2026-10-11), so anything else is flagged.
        else if (!OrderSnFormatPattern().IsMatch(orderSn))
            problems.Add($"訂單編號「{orderSn}」不是蝦皮的 14 碼格式（6 碼日期＋8 碼英數），可能讀錯，請核對。");

        var carrier = string.Empty;
        var tracking = string.Empty;
        foreach (var line in lines)
        {
            // The OCR reads 「包裹」 as 「包裏」 or 「包里」 (2026-10-11): any one glyph after 包,
            // then the package number and a colon, marks the line.
            var text = PackagePattern().Replace(Compact(line.Text).Replace('＃', '#'), "包裹");
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
                if (candidate.Length >= 8) tracking = TrackingDigits(candidate);
            }
        }
        if (carrier.Length == 0) problems.Add("訂單資訊截圖讀不到物流方式（包裹）。");
        if (tracking.Length == 0) problems.Add("訂單資訊截圖讀不到物流單號；可在表單的送貨地址(一) 補上。");
        return new ShopeeShotOrder(orderSn, carrier, tracking);
    }

    /// <summary>
    /// An order number starts with its date (YYMMDD), where an 「O」 can only be a 0
    /// (user, 2026-10-12: the OCR mixes up 0 and O).
    /// </summary>
    internal static string OrderSnDateDigits(string orderSn) =>
        orderSn.Length < 6 ? orderSn : orderSn[..6].Replace('O', '0').Replace('I', '1').Replace('L', '1') + orderSn[6..];

    /// <summary>
    /// 0 and O where the tracking number's format decides them: Shopee 店到店 numbers are
    /// TW, 12 digits and one more character (seen as a letter, not always); carrier numbers
    /// (新竹, 黑貓…) are digits only. The last character of a TW number is left as read.
    /// </summary>
    internal static string TrackingDigits(string tracking)
    {
        // I／L read for 1 likewise (user, 2026-10-12: 1 and I look alike).
        static string Digits(string s) => s.Replace('O', '0').Replace('I', '1').Replace('L', '1');
        if (TwTrackingPattern().IsMatch(tracking))
            return "TW" + Digits(tracking[2..^1]) + tracking[^1];
        return tracking.All(c => char.IsAsciiDigit(c) || c is 'O' or 'I' or 'L') && tracking.Any(char.IsAsciiDigit) ? Digits(tracking) : tracking;
    }

    /// <summary>
    /// Decides 0 or O in the order number's last 8 letters and digits from the glyphs' widths
    /// (user, 2026-10-12): in Shopee's font a 0 is as wide as the other digits and an O is about
    /// a third wider. <paramref name="runWidths"/> are the ink runs of the box, left to right;
    /// touching glyphs make one run, so the runs are first aligned to the characters by their
    /// usual widths. Only a 0／O that stands alone in its run, with at least two other digits
    /// alone to compare with, is decided; otherwise the OCR's reading is kept.
    /// </summary>
    internal static (string Text, List<string> Decisions) ResolveZeroO(string text, IReadOnlyList<int> runWidths)
    {
        var decisions = new List<string>();
        var ambiguous = Enumerable.Range(6, Math.Max(0, text.Length - 6)).Where(i => text[i] is '0' or 'O').ToList();
        if (ambiguous.Count == 0 || runWidths.Count == 0) return (text, decisions);

        static double Expected(char c) => c switch
        {
            '1' or 'I' => 3.5,
            'M' or 'W' => 11,
            >= '0' and <= '9' => 7,
            'J' or 'L' or 'F' or 'T' or 'S' or 'E' or 'P' or 'Z' or 'Y' => 7.5,
            >= 'A' and <= 'Z' => 8.5,
            _ => 7
        };
        var scale = runWidths.Sum() / text.Sum(Expected);
        var n = text.Length;
        var m = runWidths.Count;
        // cost[i, j]: the first i characters drawn by the first j runs; a run holds 1–3 characters.
        var cost = new double[n + 1, m + 1];
        var take = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++)
            for (var j = 0; j <= m; j++) cost[i, j] = double.PositiveInfinity;
        cost[0, 0] = 0;
        for (var j = 1; j <= m; j++)
            for (var i = 1; i <= n; i++)
                for (var k = 1; k <= Math.Min(3, i); k++)
                {
                    if (double.IsPositiveInfinity(cost[i - k, j - 1])) continue;
                    var expected = 0.0;
                    for (var c = i - k; c < i; c++) expected += Expected(text[c]) * scale;
                    var candidate = cost[i - k, j - 1] + Math.Abs(runWidths[j - 1] - expected) + (k - 1) * 0.5;
                    if (candidate < cost[i, j]) { cost[i, j] = candidate; take[i, j] = k; }
                }
        if (double.IsPositiveInfinity(cost[n, m])) return (text, decisions);

        var single = new int?[n]; // width of each character that has a run to itself
        for (int i = n, j = m; i > 0; j--)
        {
            var k = take[i, j];
            if (k == 1) single[i - 1] = runWidths[j - 1];
            i -= k;
        }
        var reference = Enumerable.Range(0, n)
            .Where(i => text[i] is >= '2' and <= '9' && single[i] is not null)
            .Select(i => (double)single[i]!.Value).OrderBy(w => w).ToList();
        if (reference.Count < 2) return (text, decisions);
        var digitWidth = reference[reference.Count / 2];

        var chars = text.ToCharArray();
        foreach (var i in ambiguous)
        {
            if (single[i] is not { } width) continue;
            var ratio = width / digitWidth;
            var decided = ratio <= 1.12 ? '0' : ratio >= 1.25 ? 'O' : chars[i];
            decisions.Add($"pos={i + 1} read={chars[i]} width={width} digit={digitWidth:0.#} ratio={ratio:0.00} → {decided}");
            chars[i] = decided;
        }
        return (new string(chars), decisions);
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
        var raw = lines;
        lines = WithLoneGlyphDigits(lines);
        var items = new List<ShopeeShotItem>();
        var groups = new List<ShopeeShotGroup>();
        var anchors = IndexAnchors(raw, imageWidth).OrderBy(l => l.Top).ToList();

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

        // No 編號 read at all (a lone 「1」 is often missed; user's PC, 2026-10-12): a table whose
        // 小計 column holds one group's price (and struck original) is that one group, 第 1 項.
        var singleGroup = anchors.Count == 0 && subtotalColumn.Count is >= 1 and <= 2;
        int GroupOf(int centerY) => anchors.Count == 0 ? (singleGroup ? 1 : 0)
            : int.Parse(Compact(anchors.OrderBy(a => Math.Abs(a.CenterY - centerY)).First().Text), CultureInfo.InvariantCulture);

        var codeLines = lines.Where(l => CodeLabelPattern().IsMatch(Compact(l.Text))).OrderBy(l => l.Top).ToList();
        var previousBottom = int.MinValue;
        var used = new HashSet<OcrBox>();
        var noQuantity = new List<(int Group, string RawCode, decimal Unit)>();
        void AddItem(int group, string rawCode, decimal unit, decimal quantity)
        {
            // A part marked 「(贈)」 (A00175+A00173(贈); user, 2026-10-20) is free: its quantity
            // goes to 贈／備品量, and the rest is read as if listed alone.
            var all = BundleParts(rawCode);
            foreach (var free in all.Select(Bundles.SplitGift).Where(p => p.Gift))
            {
                var (freeCode, freeTimes) = SplitSet(free.Code);
                items.Add(new ShopeeShotItem(group, freeCode, 0m, quantity * freeTimes, 0m, Gift: true));
            }
            if (all.Any(p => Bundles.SplitGift(p).Gift))
            {
                var sold = all.Where(p => !Bundles.SplitGift(p).Gift).ToList();
                if (sold.Count == 0) return;
                rawCode = string.Join("+", sold);
            }
            // A bundle 「C00244+C00011」 (user, 2026-10-12): every part goes out as many times as
            // bundles were sold; the first part carries the bundle's price and the others are 0.
            var parts = BundleParts(rawCode);
            if (parts.Count > 1)
            {
                for (var i = 0; i < parts.Count; i++)
                {
                    var (partCode, partTimes) = SplitSet(parts[i]);
                    var partUnit = i == 0 ? Math.Round(unit / partTimes, 2, MidpointRounding.AwayFromZero) : 0m;
                    items.Add(new ShopeeShotItem(group, partCode, partUnit, quantity * partTimes, i == 0 ? unit * quantity : 0m, parts[0]));
                }
                return;
            }
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

        if (singleGroup)
        {
            var values = subtotalColumn.OrderBy(n => n.Box.Top).Select(n => n.Value).ToList();
            groups.Add(new ShopeeShotGroup(1, values[0], values[^1]));
        }
        foreach (var anchor in anchors)
        {
            var index = int.Parse(Compact(anchor.Text), CultureInfo.InvariantCulture);
            var values = subtotalColumn.Where(n => GroupOf(n.Box.CenterY) == index).OrderBy(n => n.Box.Top).Select(n => n.Value).ToList();
            if (values.Count == 0) { problems.Add($"第 {index} 項讀不到小計。"); continue; }
            groups.Add(new ShopeeShotGroup(index, values[0], values[^1]));
        }
        // A discounted 小計 shows the struck-out original below the price, and the OCR can miss
        // that struck text (2,716 read, 2,800 missed; user's PC, 2026-10-11). Products adding up
        // to more than the price shown means there was a discount, so their sum is the original;
        // a missed product would make the sum smaller, which the checks still catch.
        for (var i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            var sum = items.Where(it => it.Group == g.Index).Sum(it => it.Amount);
            // At most half off: a misread quantity (450 × 450) must still fail the checks.
            // An original below the price shown is a misread struck number (480 read as 180;
            // user's PC, 2026-10-11) and is treated as missed.
            if (g.Original <= g.Shown && sum > g.Shown && g.Shown * 2 >= sum) groups[i] = g with { Original = sum };
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

        SuggestQuantity(items, groups, problems);
        return (items, groups);
    }

    /// <summary>
    /// A 數量 read as another digit (「2」 read as 「7」, user's PC, 2026-10-12) makes a group's
    /// 單價×數量 differ from its 小計 original. Nothing is changed (user: a wrong quantity makes
    /// a wrong document): when exactly one product's quantity, as some whole number, would make
    /// the group add up, the problem names that product and number so the check is quick.
    /// </summary>
    private static void SuggestQuantity(List<ShopeeShotItem> items, List<ShopeeShotGroup> groups, List<string> problems)
    {
        foreach (var group in groups)
        {
            var members = items.Where(i => i.Group == group.Index).ToList();
            var sum = members.Sum(i => i.Amount);
            if (members.Count == 0 || sum == group.Original) continue;
            var candidates = new List<(ShopeeShotItem Item, decimal Quantity)>();
            foreach (var item in members)
            {
                if (item.UnitPrice <= 0 || item.UnitPrice * item.Quantity != item.Amount) continue;
                var amount = group.Original - (sum - item.Amount);
                if (amount <= 0 || amount % item.UnitPrice != 0) continue;
                var quantity = amount / item.UnitPrice;
                if (quantity is >= 1 and <= 999 && quantity != item.Quantity) candidates.Add((item, quantity));
            }
            if (candidates.Count != 1) continue;
            var (item1, right) = candidates[0];
            problems.Add($"第 {group.Index} 項 {item1.ItemCode} 數量讀到 {Number(item1.Quantity)}；若是 {Number(right)}，這一項的小計 {Number(group.Original)} 才對得上，請看截圖核對後在表單修正。");
        }
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
        var raw = lines;
        lines = WithLoneGlyphDigits(lines);
        var probes = new List<CellProbe>();
        var numbers = lines.Where(l => l.CenterX > imageWidth * 0.5 && TryNumber(l.Text, out _, digitsOnly: true)).ToList();
        var layout = ResolveColumns(lines, imageWidth);
        if (layout is null) return probes;
        var unitX = layout.Unit;
        var quantityX = layout.Quantity;
        var subtotalX = layout.Subtotal;
        var anchors = IndexAnchors(raw, imageWidth);
        var anchorX = IndexColumnX(lines)
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

    private static double? IndexColumnX(IReadOnlyList<OcrBox> lines) =>
        lines.FirstOrDefault(l => Compact(l.Text) == "編")?.CenterX;

    /// <summary>
    /// The 編號 numbers: short numbers in the leftmost column only. Product thumbnails sit just
    /// right of 編號 and carry small print (「3號杯」) the OCR can read as a lone digit, which
    /// made up a 編號 with no 小計 (「第 4 項讀不到小計」; user's PC, 2026-10-11).
    /// </summary>
    private static List<OcrBox> IndexAnchors(IReadOnlyList<OcrBox> lines, int imageWidth)
    {
        // The 編號 glyph alone is often misread: "1" as "-", "2" as "乙" — and "2" as "-" too
        // (user's PC, 2026-10-11), so a look-alike is only a guess, settled below.
        var guessed = new HashSet<OcrBox>();
        var candidates = new List<OcrBox>();
        foreach (var l in lines.Where(l => l.CenterX < imageWidth * 0.2))
        {
            if (!TryIndexDigits(l.Text, out var digits)) continue;
            var box = l with { Text = digits };
            if (!TryNumber(l.Text, out _, digitsOnly: true)) guessed.Add(box);
            candidates.Add(box);
        }
        // Product thumbnails sit between 編號 and the product text and carry small print
        // (「5號杯」 read as "5"); a 編號 is well left of them, about 6 text heights before the
        // product text, a thumbnail's print about 3.
        var codes = lines.Where(l => CodeLabelPattern().IsMatch(Compact(l.Text))).ToList();
        if (codes.Count > 0)
        {
            var textLeft = codes.Min(c => c.Left);
            var textHeight = codes.Select(c => (double)c.Height).OrderBy(h => h).ElementAt(codes.Count / 2);
            candidates = candidates.Where(c => textLeft - c.CenterX >= textHeight * 4).ToList();
        }
        // Shopee centres the 編號 and the 小計 in the same cell, so a real 編號 lines up with a
        // 小計 number; stray digits elsewhere in the column made up a 「第 5 項」 (user's PC,
        // 2026-10-11). Without a 小計 column, it must at least sit beside a product.
        var headerBottom = lines.Where(l => Compact(l.Text) is "單價" or "數量" or "小計").Select(l => l.Bottom).DefaultIfEmpty(int.MinValue).Max();
        candidates = candidates.Where(c => c.Top > headerBottom).ToList();
        if (ResolveColumns(lines, imageWidth)?.Subtotal is { } subtotalX)
        {
            var subtotals = lines.Where(l => Math.Abs(l.CenterX - subtotalX) <= ColumnTolerance && TryNumber(l.Text, out _, digitsOnly: true)).ToList();
            if (subtotals.Count > 0)
                candidates = candidates
                    .Where(c => subtotals.Any(n => Math.Abs(n.CenterY - c.CenterY) <= Math.Max(25, n.Height * 1.5)))
                    .ToList();
        }
        else
        {
            var codeLines = lines.Where(l => CodeLabelPattern().IsMatch(Compact(l.Text))).ToList();
            candidates = candidates.Where(c => codeLines.Any(code => Math.Abs(code.CenterY - c.CenterY) <= 120)).ToList();
        }
        if (candidates.Count == 0) return candidates;
        var x = IndexColumnX(lines) ?? candidates.Min(c => c.CenterX);
        var anchors = new List<OcrBox>();
        // One 編號 cell can carry two boxes (a guessed glyph and a re-read digit); keep one,
        // preferring a real digit.
        foreach (var c in candidates.Where(c => Math.Abs(c.CenterX - x) <= 15).OrderBy(c => guessed.Contains(c)).ThenBy(c => c.Top))
            if (!anchors.Any(a => Math.Abs(a.CenterY - c.CenterY) < Math.Max(a.Height, c.Height)))
                anchors.Add(c);
        anchors = anchors.OrderBy(c => c.Top).ToList();

        // 編號 run 1, 2, 3… down the screenshot, so a guessed one takes its number from the
        // nearest one read as a real digit; with none, from the one above it.
        var settled = anchors.Select(a => int.Parse(a.Text, CultureInfo.InvariantCulture)).ToArray();
        var sure = Enumerable.Range(0, anchors.Count).Where(i => !guessed.Contains(anchors[i])).ToList();
        for (var i = 0; i < anchors.Count; i++)
        {
            if (!guessed.Contains(anchors[i])) continue;
            if (sure.Count > 0)
            {
                var j = sure.OrderBy(k => Math.Abs(k - i)).First();
                settled[i] = settled[j] + (i - j);
            }
            else if (i > 0) settled[i] = settled[i - 1] + 1;
        }
        return anchors.Select((a, i) => settled[i] >= 1 ? a with { Text = settled[i].ToString(CultureInfo.InvariantCulture) } : a).ToList();
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

    /// <summary>The codes of a bundle 「A+B」 (the OCR may read + as ＋ or 十); one code otherwise.</summary>
    internal static List<string> BundleParts(string rawCode) =>
        Compact(rawCode).Replace('＋', '+').Replace('十', '+')
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

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
        foreach (var group in lines.GroupBy(l => (l.ItemCode, l.UnitPrice, l.SortKey, l.Gift)))
        {
            if (group.Key.Gift) { formItems.Add(Bundles.GiftLine(group.Key.ItemCode, group.Sum(l => l.Quantity))); continue; }
            var quantity = Number(group.Sum(l => l.Quantity));
            // A bundle's parts leave 單價 to ERP; the first part remembers what the bundle sold for.
            formItems.Add(group.Key.SortKey is { } bundle
                ? new ImportedItem(group.Key.ItemCode, quantity, string.Empty)
                {
                    SortKey = bundle,
                    BundleTotal = group.Key.UnitPrice > 0 ? Number(group.Sum(l => l.Amount)) : null
                }
                : new ImportedItem(group.Key.ItemCode, quantity, Number(group.Key.UnitPrice)));
        }
        formItems = Bundles.MergeGifts(formItems);
        var hasBundles = lines.Any(l => l.SortKey is not null);

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
        void AddLine(string code, decimal price, string what, bool takesBundleGap = false)
        {
            if (code.Length == 0) problems.Add($"請在設定填入{what}品號（本張有{what} {Number(price)}）。");
            formItems.Add(new ImportedItem(code, "1", Number(price)) { TakesBundleGap = takesBundleGap });
        }
        // With a bundle the 促銷組合 line is always there: it also takes the gap between ERP's
        // list prices for the bundle's parts and what the bundle sold for.
        if (promotion > 0 || hasBundles) AddLine(mapping.PromotionItemCode, -promotion, "促銷組合", hasBundles);
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
    /// <summary>
    /// A lone "1" or "2" (編號 or 數量) that the OCR read as a look-alike glyph — "一", "-",
    /// "乙" (user's PC, 2026-10-11) — becomes the digit, so it needs no re-read.
    /// </summary>
    private static List<OcrBox> WithLoneGlyphDigits(IReadOnlyList<OcrBox> lines) =>
        lines.Select(l => Compact(l.Text).Length == 1 && !TryNumber(l.Text, out _, digitsOnly: true) && TryIndexDigits(l.Text, out var digits)
            ? l with { Text = digits }
            : l).ToList();

    /// <summary>A 編號 as OCR reads the lone glyph: digits, or a look-alike of 1 or 2.</summary>
    internal static bool TryIndexDigits(string text, out string digits)
    {
        var compact = Compact(text);
        digits = compact switch
        {
            "-" or "一" or "—" or "－" => "1",
            "乙" or "Z" or "z" => "2",
            _ => compact
        };
        return TryNumber(digits, out _, digitsOnly: true) && digits.Length <= 3;
    }

    /// <summary>
    /// The 編號 the numbering implies at <paramref name="centerY"/>: one less than the next 編號
    /// below, one more than the last above (both must agree when both exist). Null when the
    /// numbers read so far do not say.
    /// </summary>
    public static int? ExpectedIndexAt(IReadOnlyList<OcrBox> lines, int imageWidth, int centerY)
    {
        var anchors = IndexAnchors(lines, imageWidth)
            .Select(a => (a.CenterY, Index: int.Parse(Compact(a.Text), CultureInfo.InvariantCulture)))
            .ToList();
        var above = anchors.Where(a => a.CenterY < centerY).OrderByDescending(a => a.CenterY).Select(a => (int?)a.Index + 1).FirstOrDefault();
        var below = anchors.Where(a => a.CenterY > centerY).OrderBy(a => a.CenterY).Select(a => (int?)a.Index - 1).FirstOrDefault();
        var expected = above is not null && below is not null ? (above == below ? above : null) : above ?? below;
        return expected is >= 1 ? expected : null;
    }

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

    /// <summary>
    /// Text without spaces, with the simplified glyphs the OCR model sometimes returns for
    /// Shopee's labels folded to traditional (diagnostic log, 2026-10-12: 「訂單金额」).
    /// </summary>
    internal static string Compact(string text) =>
        new(text.Where(c => !char.IsWhiteSpace(c)).Select(c => SimplifiedGlyphs.TryGetValue(c, out var t) ? t : c).ToArray());

    private static readonly Dictionary<char, char> SimplifiedGlyphs = new()
    {
        ['额'] = '額', ['订'] = '訂', ['单'] = '單', ['总'] = '總', ['费'] = '費', ['运'] = '運', ['优'] = '優',
        ['币'] = '幣', ['虾'] = '蝦', ['实'] = '實', ['计'] = '計', ['价'] = '價', ['数'] = '數',
        ['项'] = '項', ['选'] = '選', ['货'] = '貨', ['号'] = '號', ['规'] = '規', ['编'] = '編',
        ['买'] = '買', ['卖'] = '賣', ['场'] = '場', ['帐'] = '帳', ['账'] = '帳', ['进'] = '進', ['讯'] = '訊',
        ['资'] = '資', ['录'] = '錄', ['细'] = '細', ['隐'] = '隱', ['调'] = '調',
        ['内'] = '內' // 「建議今天内出貨」 (user's PC, 2026-10-13)
    };

    private static string Number(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^[0-9A-Z]{10,20}$")]
    private static partial Regex OrderSnPattern();

    [GeneratedRegex(@"^包\S(?=\d*[:：])")]
    private static partial Regex PackagePattern();

    [GeneratedRegex(@"^\d{6}[0-9A-Z]{8}$")]
    private static partial Regex OrderSnFormatPattern();

    [GeneratedRegex(@"^TW[0-9OIL]{12}[0-9A-Z]$")]
    private static partial Regex TwTrackingPattern();

    // 「商品選項貨號：」; the OCR once dropped the 貨 (「商品選項」 + 「號：A0…」, user's PC,
    // 2026-10-12), so 號 with a colon after 選項 or at a box's start is a label too.
    [GeneratedRegex(@"(貨|货)(號|号)|(^|選項|选项)(號|号)(?=[:：])")]
    private static partial Regex CodeLabelPattern();

    [GeneratedRegex(@"^(?<code>.*[0-9A-Za-z])[xX×*＊](?<n>\d{1,3})$")]
    private static partial Regex SetSuffixPattern();

    [GeneratedRegex(@"^\d{1,7}$")]
    private static partial Regex DigitsPattern();

    [GeneratedRegex(@"(?<neg>-)?NT[\$S]?(?<n>[0-9Oo]+(\.\d+)?)$")]
    private static partial Regex AmountPattern();
}
