using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Drawing.Text;

namespace CYERPAutoInput;

/// <summary>
/// Draws a <see cref="ShipSheet"/> on the thermal printer's 100×150 mm label (user, 2026-10-26:
/// one 出貨明細 per 寄件單 instead of the 二聯報表紙): 銷貨單／訂單編號／寄件編號 with their last
/// three characters large, 貨運, each product on two lines (品名; 品號 數量 單位 × 單價 and its
/// 金額), the total, then the platform's 註記. Products that do not fit go on to another label.
/// </summary>
internal static class ShipSheetPrinter
{
    private const string FontName = "Microsoft JhengHei";
    private const float LabelWidthMm = 100, LabelHeightMm = 150, MarginMm = 3;

    /// <returns>Labels printed.</returns>
    public static int Print(string printer, ShipSheet sheet)
    {
        using var doc = new PrintDocument();
        doc.PrinterSettings.PrinterName = printer;
        if (!doc.PrinterSettings.IsValid) throw new InvalidOperationException($"找不到印表機 {printer}");
        doc.DocumentName = "出貨明細 " + sheet.Document;
        doc.PrintController = new StandardPrintController();
        doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        doc.OriginAtMargins = false;
        var next = 0;
        var pages = 0;
        doc.PrintPage += (_, e) =>
        {
            pages++;
            var g = e.Graphics!;
            g.PageUnit = GraphicsUnit.Millimeter;
            g.SmoothingMode = SmoothingMode.None;
            // Pure black dots on the thermal head.
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            var pageW = e.PageBounds.Width * 0.254f;
            var pageH = e.PageBounds.Height * 0.254f;
            var width = Math.Min(LabelWidthMm, pageW);
            var height = Math.Min(LabelHeightMm, pageH);
            // The graphics start at the printable area's corner; the label is centred on the paper.
            var left = (pageW - width) / 2 - e.PageSettings.HardMarginX * 0.254f + MarginMm;
            var top = (pageH - height) / 2 - e.PageSettings.HardMarginY * 0.254f + MarginMm;
            var right = left + width - 2 * MarginMm;
            var bottom = top + height - 2 * MarginMm;
            using var pen = new Pen(Color.Black, 0.4f);
            var y = top;

            if (pages == 1)
            {
                y += Text(g, "出貨明細", 13, true, left, y, right - left);
                Text(g, "印單 " + sheet.PrintedAt.ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture), 9, false, left, y - 5.2f, right - left, StringAlignment.Far);
                y += 1;
                y += Numbered(g, "銷貨單", sheet.Document, left, y);
                if (sheet.OrderSn.Length > 0) y += Numbered(g, sheet.Platform, sheet.OrderSn, left, y);
                if (sheet.Tracking.Length > 0) y += Numbered(g, "寄件", sheet.Tracking, left, y);
                if (sheet.Carrier.Length > 0) y += Text(g, "貨運：" + sheet.Carrier, 11, true, left, y, right - left) + 0.5f;
            }
            else y += Text(g, $"出貨明細（續）銷貨單 {sheet.Document}", 10, true, left, y, right - left);
            g.DrawLine(pen, left, y + 1, right, y + 1);
            y += 2.5f;

            // Room kept at the foot for 「續下一張」 while products remain.
            const float more = 6;
            while (next < sheet.Lines.Count)
            {
                var line = sheet.Lines[next];
                var need = LineHeight(g, 10.5f) + LineHeight(g, 10) + 1.5f;
                if (y + need > bottom - more) break;
                y += Text(g, line.Name.Length > 0 ? line.Name : line.Code, 10.5f, true, left, y, right - left);
                var detail = $"{line.Code}　{ShipSheet.Show(line.Quantity)} {line.Unit}".TrimEnd() +
                    (line.Price.Length > 0 ? $" × {ShipSheet.Show(line.Price)}" : string.Empty);
                var amount = line.Amount.Length > 0 ? ShipSheet.Show(line.Amount) : string.Empty;
                Text(g, amount, 10, true, left, y, right - left, StringAlignment.Far);
                y += Text(g, detail, 10, false, left + 2, y, right - left - 22) + 1.5f;
                next++;
            }
            if (next < sheet.Lines.Count)
            {
                Text(g, "（續下一張）", 9, false, left, bottom - 4.5f, right - left, StringAlignment.Far);
                e.HasMorePages = true;
                return;
            }

            // Total and 註記 on the last label; a 註記 that does not fit goes on one more.
            var noteHeight = sheet.Note.Trim().Length == 0 ? 0 : Measure(g, sheet.Note.Trim(), 9, right - left) + 2;
            if (y + 9 + noteHeight > bottom && y > top + 30)
            {
                Text(g, "（續下一張）", 9, false, left, bottom - 4.5f, right - left, StringAlignment.Far);
                e.HasMorePages = true;
                return;
            }
            g.DrawLine(pen, left, y, right, y);
            y += 1;
            y += Text(g, "合計 " + ShipSheet.Show(sheet.Total), 13, true, left, y, right - left, StringAlignment.Far) + 1.5f;
            if (noteHeight > 0)
            {
                g.DrawLine(pen, left, y, right, y);
                using var font = new Font(FontName, 9, FontStyle.Regular, GraphicsUnit.Point);
                g.DrawString(sheet.Note.Trim(), font, Brushes.Black, new RectangleF(left, y + 1, right - left, bottom - y - 1));
            }
            e.HasMorePages = false;
        };
        doc.Print();
        return pages;
    }

    /// <summary>「銷貨單 234-20261026」 and its last three characters large and bold, on one line; its height.</summary>
    private static float Numbered(Graphics g, string label, string number, float x, float y)
    {
        var (head, tail) = ShipSheet.SplitTail(number);
        using var small = new Font(FontName, 11, FontStyle.Regular, GraphicsUnit.Point);
        using var big = new Font(FontName, 22, FontStyle.Bold, GraphicsUnit.Point);
        var format = StringFormat.GenericTypographic;
        var bigHeight = big.GetHeight(g);
        var smallHeight = small.GetHeight(g);
        // The small text sits on the large one's baseline, near enough.
        var smallY = y + (bigHeight - smallHeight) * 0.78f;
        g.DrawString(label, small, Brushes.Black, x, smallY, format);
        var at = x + Math.Max(16, g.MeasureString(label, small, PointF.Empty, format).Width + 2);
        g.DrawString(head, small, Brushes.Black, at, smallY, format);
        at += g.MeasureString(head, small, PointF.Empty, format).Width + 0.5f;
        g.DrawString(tail, big, Brushes.Black, at, y, format);
        return bigHeight * 0.95f;
    }

    /// <summary>One line, cut with … when too long; its height.</summary>
    private static float Text(Graphics g, string text, float points, bool bold, float x, float y, float width, StringAlignment align = StringAlignment.Near)
    {
        using var font = new Font(FontName, points, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = align,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        var height = font.GetHeight(g);
        if (text.Length > 0) g.DrawString(text, font, Brushes.Black, new RectangleF(x, y, width, height), format);
        return height;
    }

    private static float LineHeight(Graphics g, float points)
    {
        using var font = new Font(FontName, points, FontStyle.Regular, GraphicsUnit.Point);
        return font.GetHeight(g);
    }

    private static float Measure(Graphics g, string text, float points, float width)
    {
        using var font = new Font(FontName, points, FontStyle.Regular, GraphicsUnit.Point);
        return g.MeasureString(text, font, (int)Math.Ceiling(width)).Height;
    }
}
