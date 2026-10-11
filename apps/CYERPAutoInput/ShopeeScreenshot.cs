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

internal sealed record ShopeeShotOrder(string OrderSn, string Carrier, string TrackingNumber);

/// <summary>One line of the order detail: 編號, 商品選項貨號, 單價, 數量 and the shown 小計 (after promotions).</summary>
internal sealed record ShopeeShotItem(int Index, string ItemCode, decimal UnitPrice, decimal Quantity, decimal Subtotal);

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
    /// Detail rows anchored on the 編號 number at the left; each phrase belongs to the
    /// nearest anchor. Prices are the numbers in the right half: 單價, 數量, then 小計 (the
    /// upper one when a struck-out original is shown below it).
    /// </summary>
    public static List<ShopeeShotItem> ParseItems(IReadOnlyList<OcrBox> lines, int imageWidth, List<string> problems)
    {
        var numeric = lines.Where(l => TryNumber(l.Text, out _, digitsOnly: true)).ToList();
        var anchors = numeric
            .Where(l => l.CenterX < imageWidth * 0.2 && l.Text.Trim().Length <= 3)
            .OrderBy(l => l.Top)
            .ToList();
        var items = new List<ShopeeShotItem>();
        if (anchors.Count == 0)
        {
            problems.Add("明細截圖讀不到商品編號（最左邊的 1、2、3…）。");
            return items;
        }

        foreach (var anchor in anchors)
        {
            OcrBox Nearest(OcrBox l) => anchors.OrderBy(a => Math.Abs(a.CenterY - l.CenterY)).First();
            var block = lines.Where(l => Nearest(l) == anchor).ToList();
            var index = int.Parse(Compact(anchor.Text), CultureInfo.InvariantCulture);

            // 「商品選項貨號：A00001」; OCR may read 貨 as 货 and split the code into its own box.
            var codeLine = block.FirstOrDefault(l => CodeLabelPattern().IsMatch(Compact(l.Text)));
            var code = string.Empty;
            if (codeLine is not null)
            {
                var text = Compact(codeLine.Text);
                code = text[(CodeLabelPattern().Match(text).Index + CodeLabelPattern().Match(text).Length)..].TrimStart(':', '：');
                if (code.Length == 0)
                    code = block
                        .Where(l => l.Left >= codeLine.Right - 4 && Math.Abs(l.CenterY - codeLine.CenterY) <= Math.Max(10, codeLine.Height / 2))
                        .OrderBy(l => l.Left)
                        .Select(l => Compact(l.Text))
                        .FirstOrDefault() ?? string.Empty;
            }

            var columns = new List<List<(OcrBox Box, decimal Value)>>();
            foreach (var box in block.Where(l => l != anchor && l.CenterX > imageWidth * 0.5).OrderBy(l => l.Left))
            {
                if (!TryNumber(box.Text, out var value, digitsOnly: true)) continue;
                var column = columns.FirstOrDefault(c => Math.Abs(c[0].Box.CenterX - box.CenterX) <= 40);
                if (column is null) columns.Add([(box, value)]);
                else column.Add((box, value));
            }
            if (columns.Count < 3)
            {
                problems.Add($"第 {index} 項讀不到單價、數量、小計。");
                continue;
            }
            columns = columns.OrderBy(c => c[0].Box.CenterX).ToList();
            var unit = columns[0].OrderBy(c => c.Box.Top).First().Value;
            var quantity = columns[1].OrderBy(c => c.Box.Top).First().Value;
            var subtotals = columns[2].OrderBy(c => c.Box.Top).Select(c => c.Value).ToList();
            var shown = subtotals[0];
            var original = subtotals[^1];
            if (unit * quantity != original)
                problems.Add($"第 {index} 項單價 {Number(unit)} × 數量 {Number(quantity)} 與小計 {Number(original)} 不符，請核對。");
            if (code.Length == 0) problems.Add($"第 {index} 項讀不到商品選項貨號。");
            items.Add(new ShopeeShotItem(index, code, unit, quantity, shown));
        }
        return items;
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
    public static ShopeeShotResult Combine(ShopeeShotOrder? order, IReadOnlyList<ShopeeShotItem> items, ShopeeShotAmounts? amounts,
        ShopeeShotMapping mapping, List<string> problems)
    {
        if (order is null) problems.Add("缺少訂單資訊截圖（訂單編號、運送資訊）。");
        if (amounts is null && !problems.Any(p => p.StartsWith("金額截圖", StringComparison.Ordinal)))
            problems.Add("缺少金額截圖（買家實付金額）。");
        if (items.Count == 0 && !problems.Any(p => p.StartsWith("明細截圖", StringComparison.Ordinal)))
            problems.Add("缺少明細截圖。");
        if (order is null || amounts is null || items.Count == 0) return new ShopeeShotResult(null, problems);

        // The same line can appear on two overlapping screenshots; keep one per 編號.
        var lines = new List<ShopeeShotItem>();
        foreach (var group in items.GroupBy(i => i.Index).OrderBy(g => g.Key))
        {
            if (group.Select(i => (i.ItemCode, i.UnitPrice, i.Quantity, i.Subtotal)).Distinct().Count() > 1)
                problems.Add($"第 {group.Key} 項在兩張截圖讀到的內容不同，請核對。");
            lines.Add(group.First());
        }
        var missing = Enumerable.Range(1, lines.Max(l => l.Index)).Except(lines.Select(l => l.Index)).ToList();
        if (missing.Count > 0) problems.Add($"明細缺第 {string.Join("、", missing)} 項，請補截圖或在表單補上。");
        else if (lines.Sum(l => l.Subtotal) != amounts.OrderAmount)
            problems.Add($"明細小計合計 {Number(lines.Sum(l => l.Subtotal))} 與訂單金額 {Number(amounts.OrderAmount)} 不同，請核對。");

        var formItems = new List<ImportedItem>();
        foreach (var group in lines.GroupBy(l => (l.ItemCode, l.UnitPrice)))
            formItems.Add(new ImportedItem(group.Key.ItemCode, Number(group.Sum(l => l.Quantity)), Number(group.Key.UnitPrice)));

        var promotion = lines.Sum(l => l.UnitPrice * l.Quantity) - amounts.OrderAmount;
        if (promotion < 0) problems.Add($"單價×數量合計低於訂單金額 {Number(-promotion)}，請核對明細。");
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

    private static string Compact(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static string Number(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^[0-9A-Z]{10,20}$")]
    private static partial Regex OrderSnPattern();

    [GeneratedRegex(@"(貨|货)(號|号)")]
    private static partial Regex CodeLabelPattern();

    [GeneratedRegex(@"^\d{1,7}$")]
    private static partial Regex DigitsPattern();

    [GeneratedRegex(@"(?<neg>-)?NT[\$S]?(?<n>[0-9Oo]+(\.\d+)?)$")]
    private static partial Regex AmountPattern();
}
