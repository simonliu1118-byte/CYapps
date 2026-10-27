using System.Drawing.Printing;

namespace CYERPAutoInput;

/// <summary>One saved document of the 理貨表.</summary>
internal sealed record PackingRow(string Document, string Platform, string OrderSn, string Tracking);

/// <summary>
/// 理貨表 (user, 2026-10-27): with 全熱感紙列印 on, a 全自動 batch ends with an A4 list of what it
/// saved — titled with the day; 銷貨單號／平台／訂單編號／寄件編號 and empty 包貨人員／包貨時間
/// to be written in; as many rows a page as stay easy to read and write in; the page and page
/// count at the foot. Printed on one side (the A4 printer's own setting is not used).
/// </summary>
internal static class PackingSheet
{
    private const string FontName = "Microsoft JhengHei";
    /// <summary>Row height: room to write by hand.</summary>
    private const float RowMm = 7.5f;

    /// <returns>Pages printed.</returns>
    public static int Print(string printer, IReadOnlyList<PackingRow> rows, DateTime day)
    {
        using var doc = new PrintDocument();
        doc.PrinterSettings.PrinterName = printer;
        if (!doc.PrinterSettings.IsValid) throw new InvalidOperationException($"找不到印表機 {printer}");
        doc.PrinterSettings.Duplex = Duplex.Simplex;
        doc.DocumentName = "理貨表";
        doc.PrintController = new StandardPrintController();
        // 12 mm all round (in 1/100 inch).
        doc.DefaultPageSettings.Margins = new Margins(47, 47, 47, 47);
        using var font = new Font(FontName, 10, FontStyle.Regular, GraphicsUnit.Point);
        using var bold = new Font(FontName, 10, FontStyle.Bold, GraphicsUnit.Point);
        using var title = new Font(FontName, 16, FontStyle.Bold, GraphicsUnit.Point);
        (string Title, float Share)[] columns = [("#", 5), ("銷貨單號", 19), ("平台", 8), ("訂單編號", 19), ("寄件編號", 20), ("包貨人員", 14), ("包貨時間", 15)];
        var next = 0;
        var page = 0;
        var pages = 0;
        doc.PrintPage += (_, e) =>
        {
            page++;
            var g = e.Graphics!;
            g.PageUnit = GraphicsUnit.Millimeter;
            var b = e.MarginBounds;
            float left = (b.Left - e.PageSettings.HardMarginX) * 0.254f, right = (b.Right - e.PageSettings.HardMarginX) * 0.254f;
            float top = (b.Top - e.PageSettings.HardMarginY) * 0.254f, bottom = (b.Bottom - e.PageSettings.HardMarginY) * 0.254f;
            var tableTop = top + 12;
            var footer = 7f;
            var perPage = Math.Max(1, (int)((bottom - footer - tableTop) / RowMm) - 1);
            if (pages == 0) pages = Math.Max(1, (rows.Count + perPage - 1) / perPage);
            using var pen = new Pen(Color.Black, 0.25f);
            using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var cell = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(day.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture), title, Brushes.Black, new RectangleF(left, top, right - left, 10), center);
            var total = columns.Sum(c => c.Share);
            var xs = new List<float> { left };
            foreach (var c in columns) xs.Add(xs[^1] + (right - left) * c.Share / total);
            var y = tableTop;
            void Row(IReadOnlyList<string> values, Font f, bool middle)
            {
                g.DrawRectangle(pen, left, y, right - left, RowMm);
                for (var i = 0; i < columns.Length; i++)
                {
                    if (i > 0) g.DrawLine(pen, xs[i], y, xs[i], y + RowMm);
                    var area = new RectangleF(xs[i] + 1, y, xs[i + 1] - xs[i] - 2, RowMm);
                    g.DrawString(i < values.Count ? values[i] : string.Empty, f, Brushes.Black, area, middle || i == 0 ? center : cell);
                }
                y += RowMm;
            }
            Row(columns.Select(c => c.Title).ToList(), bold, middle: true);
            for (var n = 0; n < perPage && next < rows.Count; n++, next++)
            {
                var r = rows[next];
                Row([(next + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), r.Document, r.Platform, r.OrderSn, r.Tracking], font, middle: false);
            }
            g.DrawString($"第 {page} / {pages} 頁", font, Brushes.Black, new RectangleF(left, bottom - footer, right - left, footer), center);
            e.HasMorePages = next < rows.Count;
        };
        doc.Print();
        PrintWatch.Watch(doc.PrinterSettings.PrinterName, doc.DocumentName, 60);
        return page;
    }
}
