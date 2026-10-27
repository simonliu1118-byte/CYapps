using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Drawing.Text;

namespace CYERPAutoInput;

/// <summary>
/// Draws a <see cref="ShipSheet"/> on the thermal printer's 100×150 mm label (user, 2026-10-26:
/// one 出貨明細 per 寄件單 instead of the 二聯報表紙): the shop's logo and name, 貨運 as a tag,
/// 銷貨單／訂單編號／寄件編號 with their last three characters large, one line a product (a box to
/// tick, 品號, 品名, 數量 單位, 單價, 金額), the total, then the platform's 註記. Products that do not fit go
/// on to another label.
/// </summary>
internal static class ShipSheetPrinter
{
    private const string FontName = "Microsoft JhengHei";
    private const float LabelWidthMm = 100, LabelHeightMm = 150, MarginMm = 3;

    /// <param name="title">The shop's name at the top (設定 › 出貨明細抬頭); empty for none.</param>
    /// <param name="logo">The shop's logo left of it (設定 › Logo); null for none.</param>
    /// <returns>Labels printed.</returns>
    public static int Print(string printer, ShipSheet sheet, string title = "", Image? logo = null)
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
                // The shop (logo, name) on the left, 「出貨明細」 on the right (user, 2026-10-26).
                var head = Math.Max(LineHeight(g, 13), logo is null ? 0 : 9);
                var x = left;
                if (logo is not null)
                {
                    var w = 9f * logo.Width / logo.Height;
                    g.DrawImage(logo, x, y + (head - 9) / 2, w, 9);
                    x += w + 2;
                }
                if (title.Trim().Length > 0) Text(g, title.Trim(), 13, true, x, y + (head - LineHeight(g, 13)) / 2, right - x - 22);
                Text(g, "出貨明細", title.Trim().Length > 0 || logo is not null ? 11 : 13, true, left, y + (head - LineHeight(g, 13)) / 2, right - left, StringAlignment.Far);
                y += head + 0.8f;
                // 貨運 as a black tag on the left, the time printed on the right (user, 2026-10-26:
                // 「貨運：…」 on a line of its own stood out too much).
                var tag = sheet.Carrier.Length > 0 ? Tag(g, sheet.Carrier, left, y, right - left - 34) : LineHeight(g, 8.5f);
                Text(g, "印單 " + sheet.PrintedAt.ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture), 8.5f, false, left, y + (tag - LineHeight(g, 8.5f)) / 2, right - left, StringAlignment.Far);
                y += tag + 1;
                y += Numbered(g, "銷貨單", sheet.Document, left, y);
                if (sheet.OrderSn.Length > 0) y += Numbered(g, sheet.Platform, sheet.OrderSn, left, y);
                if (sheet.Tracking.Length > 0) y += Numbered(g, "寄件", sheet.Tracking, left, y);
            }
            else y += Text(g, $"出貨明細（續）銷貨單 {sheet.Document}", 10, true, left, y, right - left);
            g.DrawLine(pen, left, y + 1, right, y + 1);
            y += 1.8f;

            // One line a product (user, 2026-10-26): a box to tick, 品號, 品名, 數量 (larger, bold) 單位,
            // 單價, 金額, dashed lines between.
            const float box = 3.4f;
            float codeX = left + box + 1.6f, nameX = codeX + 14, amountR = right, priceR = right - 14, quantityR = right - 27, nameW = quantityR - 18 - nameX;
            var small = LineHeight(g, 9.5f);
            var large = LineHeight(g, 12);
            Text(g, "品號", 8, false, codeX, y, 14);
            Text(g, "品名", 8, false, nameX, y, nameW);
            Text(g, "數量", 8, false, quantityR - 18, y, 18, StringAlignment.Far);
            Text(g, "單價", 8, false, priceR - 12, y, 12, StringAlignment.Far);
            Text(g, "金額", 8, false, amountR - 13, y, 13, StringAlignment.Far);
            y += LineHeight(g, 8) + 0.6f;
            using var dash = new Pen(Color.Black, 0.25f) { DashStyle = DashStyle.Dash };
            using var thin = new Pen(Color.Black, 0.3f);
            // Room kept at the foot for 「續下一張」 while products remain.
            const float more = 6;
            var rowHeight = large + 1.4f;
            while (next < sheet.Lines.Count)
            {
                if (y + rowHeight > bottom - more) break;
                var line = sheet.Lines[next];
                // The smaller words sit on the large 數量's baseline, near enough.
                var textY = y + (large - small) * 0.75f;
                g.DrawRectangle(thin, left, y + (large - box) / 2, box, box);
                Text(g, line.Code, 9.5f, false, codeX, textY, nameX - codeX - 1);
                Text(g, line.Name, 9.5f, false, nameX, textY, nameW);
                var unitWidth = line.Unit.Length == 0 ? 0 : Width(g, " " + line.Unit, 9.5f);
                if (unitWidth > 0) Text(g, " " + line.Unit, 9.5f, false, quantityR - unitWidth, textY, unitWidth + 1);
                Text(g, ShipSheet.Show(line.Quantity), 12, true, quantityR - 18, y, 18 - unitWidth, StringAlignment.Far);
                Text(g, line.Price.Length > 0 ? ShipSheet.Show(line.Price) : string.Empty, 9.5f, false, priceR - 12, textY, 12, StringAlignment.Far);
                Text(g, line.Amount.Length > 0 ? ShipSheet.Show(line.Amount) : string.Empty, 9.5f, true, amountR - 13, textY, 13, StringAlignment.Far);
                y += rowHeight;
                next++;
                if (next < sheet.Lines.Count) g.DrawLine(dash, left, y - 0.7f, right, y - 0.7f);
            }
            if (next < sheet.Lines.Count)
            {
                Text(g, "（續下一張）", 9, false, left, bottom - 4.5f, right - left, StringAlignment.Far);
                e.HasMorePages = true;
                return;
            }

            // Total and 註記 on the last label; a 註記 that does not fit goes on one more.
            // 註記 a little larger (user, 2026-10-26).
            const float notePoints = 10.5f;
            var noteHeight = sheet.Note.Trim().Length == 0 ? 0 : Measure(g, sheet.Note.Trim(), notePoints, right - left) + 2;
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
                using var font = new Font(FontName, notePoints, FontStyle.Regular, GraphicsUnit.Point);
                g.DrawString(sheet.Note.Trim(), font, Brushes.Black, new RectangleF(left, y + 1, right - left, bottom - y - 1));
            }
            e.HasMorePages = false;
        };
        doc.Print();
        PrintWatch.Watch(doc.PrinterSettings.PrinterName, doc.DocumentName);
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

    /// <summary>White words on a black tag (貨運); its height.</summary>
    private static float Tag(Graphics g, string text, float x, float y, float maxWidth)
    {
        using var font = new Font(FontName, 10, FontStyle.Bold, GraphicsUnit.Point);
        var size = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
        var width = Math.Min(maxWidth, size.Width + 3);
        var height = font.GetHeight(g) + 1;
        g.FillRectangle(Brushes.Black, x, y, width, height);
        using var format = new StringFormat(StringFormat.GenericTypographic) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(text, font, Brushes.White, new RectangleF(x + 1.5f, y + 0.5f, width - 3, height - 1), format);
        return height;
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

    private static float Width(Graphics g, string text, float points)
    {
        using var font = new Font(FontName, points, FontStyle.Regular, GraphicsUnit.Point);
        return g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic).Width;
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
