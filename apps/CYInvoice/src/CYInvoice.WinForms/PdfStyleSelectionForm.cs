using CYInvoice.Core.Invoicing;

namespace CYInvoice.WinForms;

internal sealed class PdfStyleSelectionForm : Form
{
    private readonly List<Bitmap> thumbnails = [];
    private readonly List<Button> styleButtons = [];

    public PdfStyleSelectionForm(string action = "列印")
    {
        action = action == "檢視" ? "檢視" : "列印";
        Text = $"選擇{action}版型";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1060, 410);
        MinimumSize = new Size(940, 390);
        MaximumSize = new Size(1280, 520);
        ShowInTaskbar = false;
        KeyPreview = true;
        Font = new Font("Microsoft JhengHei UI", 10F);
        Icon = ApplicationIcon.Load();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(20, 14, 20, 18),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label
        {
            Text = $"請選擇要{action}的公司發票版型",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            Margin = Padding.Empty,
        }, 0, 0);

        var choices = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = InvoicePdfStyles.Company.Count,
            RowCount = 1,
            Margin = Padding.Empty,
        };
        foreach (var _ in InvoicePdfStyles.Company)
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        choices.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        for (var index = 0; index < InvoicePdfStyles.Company.Count; index++)
        {
            var style = InvoicePdfStyles.Company[index];
            var thumbnail = PdfStyleThumbnail.Create(style, new Size(164, 220));
            thumbnails.Add(thumbnail);
            var button = new NoFocusCueButton
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(8, 4, 8, 4),
                Text = DisplayName(style),
                Tag = style,
                Image = thumbnail,
                ImageAlign = ContentAlignment.TopCenter,
                TextAlign = ContentAlignment.BottomCenter,
                TextImageRelation = TextImageRelation.ImageAboveText,
                Font = new Font(Font.FontFamily, 10F),
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = true,
                AccessibleName = $"{action}版型 " + DisplayName(style),
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(34, 166, 225);
            button.FlatAppearance.BorderSize = 1;
            button.Click += (_, _) =>
            {
                SelectedStyle = (InvoicePdfStyle)button.Tag!;
                DialogResult = DialogResult.OK;
                Close();
            };
            styleButtons.Add(button);
            choices.Controls.Add(button, index, 0);
        }

        root.Controls.Add(choices, 0, 1);
        Controls.Add(root);
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Escape) Close();
        };
    }

    public InvoicePdfStyle? SelectedStyle { get; private set; }

    internal void VerifySmokeLayout()
    {
        if (styleButtons.Count != 5 || styleButtons.Any(button => button.Image is null || button.Tag is not InvoicePdfStyle))
            throw new InvalidOperationException("公司發票圖像版型選擇未建立五個有效選項");
        if (styleButtons.Select(button => ((InvoicePdfStyle)button.Tag!).Code).Distinct().Count() != 5)
            throw new InvalidOperationException("公司發票圖像版型選項重複");

        var canvas = new Size(164, 220);
        var a4 = PdfStyleThumbnail.PageBounds(InvoicePdfStyles.A4, canvas);
        var a5 = PdfStyleThumbnail.PageBounds(InvoicePdfStyles.A5, canvas);
        if (a4.Height <= a4.Width || a5.Width <= a5.Height ||
            Math.Abs(a4.Width / (double)a4.Height - 210D / 297D) > 0.03 ||
            Math.Abs(a5.Width / (double)a5.Height - 210D / 148D) > 0.03)
            throw new InvalidOperationException("公司發票 A4／橫式 A5 示意圖比例不正確");
    }

    private static string DisplayName(InvoicePdfStyle style) => style.Code switch
    {
        0 => "A4 整張",
        1 => "A4（地址＋A5）",
        2 => "A4（A5 內容）",
        3 => "A5",
        5 => "QRcode_A4",
        _ => style.Name,
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var button in styleButtons) button.Image = null;
            foreach (var thumbnail in thumbnails) thumbnail.Dispose();
            Icon?.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class PdfStyleThumbnail
{
    private static readonly Color BorderBlue = Color.FromArgb(34, 166, 225);
    private static readonly Color Ink = Color.FromArgb(72, 72, 72);
    private static readonly Color Faint = Color.FromArgb(176, 176, 176);

    public static Bitmap Create(InvoicePdfStyle style, Size size)
    {
        var image = new Bitmap(size.Width, size.Height);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.White);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        var page = PageBounds(style, size);
        var shadow = page;
        shadow.Offset(2, 2);
        using var shadowBrush = new SolidBrush(Color.FromArgb(228, 228, 228));
        using var paperBrush = new SolidBrush(Color.White);
        using var border = new Pen(BorderBlue, 2);
        using var ink = new Pen(Ink, 1);
        using var faint = new Pen(Faint, 1);
        graphics.FillRectangle(shadowBrush, shadow);
        graphics.FillRectangle(paperBrush, page);
        graphics.DrawRectangle(border, page.X, page.Y, page.Width - 1, page.Height - 1);

        switch (style.Code)
        {
            case 0:
                DrawA4Full(graphics, ink, faint, page);
                break;
            case 1:
                DrawAddressAndA5(graphics, ink, faint, page);
                break;
            case 2:
                DrawA5ContentOnA4(graphics, ink, faint, page);
                break;
            case 3:
                DrawLandscapeA5(graphics, ink, faint, page);
                break;
            case 5:
                DrawQrCodeA4(graphics, ink, faint, page);
                break;
        }
        return image;
    }

    internal static Rectangle PageBounds(InvoicePdfStyle style, Size size)
    {
        var availableWidth = Math.Max(1, size.Width - 12);
        var availableHeight = Math.Max(1, size.Height - 12);
        var ratio = style.Code == InvoicePdfStyles.A5.Code ? 210D / 148D : 210D / 297D;
        int width;
        int height;
        if (availableWidth / (double)availableHeight > ratio)
        {
            height = availableHeight;
            width = Math.Max(1, (int)Math.Round(height * ratio));
        }
        else
        {
            width = availableWidth;
            height = Math.Max(1, (int)Math.Round(width / ratio));
        }
        return new Rectangle(
            (size.Width - width) / 2,
            (size.Height - height) / 2,
            width,
            height);
    }

    private static void DrawA4Full(Graphics graphics, Pen ink, Pen faint, Rectangle page)
    {
        DrawOfficialHeader(graphics, ink, faint, page, page.Top + 7, compact: false);
        var table = new Rectangle(
            page.Left + 7,
            page.Top + page.Height * 27 / 100,
            page.Width - 14,
            page.Height * 56 / 100);
        DrawInvoiceTable(graphics, ink, faint, table, 7);
        DrawTotals(graphics, ink, faint, new Rectangle(page.Left + 7, table.Bottom + 4, page.Width - 14, page.Bottom - table.Bottom - 11));
    }

    private static void DrawAddressAndA5(Graphics graphics, Pen ink, Pen faint, Rectangle page)
    {
        DrawOfficialHeader(graphics, ink, faint, page, page.Top + 6, compact: true);
        var addressTop = page.Top + page.Height * 20 / 100;
        DrawLineCluster(graphics, faint, page.Left + 12, addressTop, page.Width * 58 / 100, 4, 7);
        graphics.DrawLine(faint, page.Left + 8, page.Top + page.Height / 2, page.Right - 8, page.Top + page.Height / 2);

        var lower = new Rectangle(
            page.Left + 7,
            page.Top + page.Height * 54 / 100,
            page.Width - 14,
            page.Height * 39 / 100);
        DrawOfficialHeader(graphics, ink, faint, lower, lower.Top + 2, compact: true);
        var table = new Rectangle(lower.Left + 2, lower.Top + lower.Height * 35 / 100, lower.Width - 4, lower.Height * 48 / 100);
        DrawInvoiceTable(graphics, ink, faint, table, 4);
        DrawTotals(graphics, ink, faint, new Rectangle(lower.Left + 2, table.Bottom + 2, lower.Width - 4, lower.Bottom - table.Bottom - 2));
    }

    private static void DrawA5ContentOnA4(Graphics graphics, Pen ink, Pen faint, Rectangle page)
    {
        var upper = new Rectangle(
            page.Left + 7,
            page.Top + 7,
            page.Width - 14,
            page.Height * 44 / 100);
        DrawOfficialHeader(graphics, ink, faint, upper, upper.Top + 1, compact: true);
        var table = new Rectangle(upper.Left + 2, upper.Top + upper.Height * 31 / 100, upper.Width - 4, upper.Height * 51 / 100);
        DrawInvoiceTable(graphics, ink, faint, table, 5);
        DrawTotals(graphics, ink, faint, new Rectangle(upper.Left + 2, table.Bottom + 2, upper.Width - 4, upper.Bottom - table.Bottom - 2));
    }

    private static void DrawLandscapeA5(Graphics graphics, Pen ink, Pen faint, Rectangle page)
    {
        DrawOfficialHeader(graphics, ink, faint, page, page.Top + 5, compact: true);
        var table = new Rectangle(
            page.Left + 8,
            page.Top + page.Height * 34 / 100,
            page.Width - 16,
            page.Height * 45 / 100);
        DrawInvoiceTable(graphics, ink, faint, table, 4);
        DrawTotals(graphics, ink, faint, new Rectangle(page.Left + 8, table.Bottom + 3, page.Width - 16, page.Bottom - table.Bottom - 9));
    }

    private static void DrawQrCodeA4(Graphics graphics, Pen ink, Pen faint, Rectangle page)
    {
        var receiptWidth = Math.Max(28, page.Width * 43 / 100);
        var receiptHeight = page.Height * 55 / 100;
        var receipt = new Rectangle(
            page.Left + (page.Width - receiptWidth) / 2,
            page.Top + page.Height * 8 / 100,
            receiptWidth,
            receiptHeight);
        graphics.DrawRectangle(faint, receipt);
        DrawLineCluster(graphics, faint, receipt.Left + 5, receipt.Top + 7, receipt.Width - 10, 5, 5);
        var barcode = new Rectangle(receipt.Left + 5, receipt.Top + receipt.Height * 42 / 100, receipt.Width - 10, Math.Max(6, receipt.Height * 12 / 100));
        DrawBarcode(graphics, ink, barcode);
        var qrSize = Math.Max(12, (receipt.Width - 14) / 2);
        DrawQr(graphics, ink, new Rectangle(receipt.Left + 4, receipt.Top + receipt.Height * 58 / 100, qrSize, qrSize));
        DrawQr(graphics, ink, new Rectangle(receipt.Right - 4 - qrSize, receipt.Top + receipt.Height * 58 / 100, qrSize, qrSize));
        DrawLineCluster(graphics, faint, receipt.Left + 5, receipt.Bottom + 8, receipt.Width, 5, 6);
    }

    private static void DrawOfficialHeader(Graphics graphics, Pen ink, Pen faint, Rectangle area, int top, bool compact)
    {
        var centerWidth = compact ? area.Width * 45 / 100 : area.Width * 52 / 100;
        var centerLeft = area.Left + (area.Width - centerWidth) / 2;
        graphics.DrawLine(ink, centerLeft, top + 3, centerLeft + centerWidth, top + 3);
        graphics.DrawLine(faint, centerLeft + 5, top + 8, centerLeft + centerWidth - 5, top + 8);
        graphics.DrawLine(faint, centerLeft + 10, top + 13, centerLeft + centerWidth - 10, top + 13);

        var metaTop = top + 18;
        DrawLineCluster(graphics, faint, area.Left + 6, metaTop, area.Width * 42 / 100, compact ? 3 : 4, 5);
        DrawLineCluster(graphics, faint, area.Left + area.Width * 62 / 100, metaTop, area.Width * 31 / 100, compact ? 2 : 3, 5);
    }

    private static void DrawInvoiceTable(Graphics graphics, Pen ink, Pen faint, Rectangle rectangle, int rows)
    {
        if (rectangle.Width < 8 || rectangle.Height < 8) return;
        graphics.DrawRectangle(ink, rectangle);
        var columns = new[] { 52, 70, 83 };
        foreach (var percentage in columns)
        {
            var x = rectangle.Left + rectangle.Width * percentage / 100;
            graphics.DrawLine(ink, x, rectangle.Top, x, rectangle.Bottom);
        }
        var headerHeight = Math.Max(5, rectangle.Height / Math.Max(7, rows + 2));
        graphics.DrawLine(ink, rectangle.Left, rectangle.Top + headerHeight, rectangle.Right, rectangle.Top + headerHeight);
        var bodyHeight = rectangle.Height - headerHeight;
        for (var row = 1; row < rows; row++)
        {
            var y = rectangle.Top + headerHeight + bodyHeight * row / rows;
            graphics.DrawLine(faint, rectangle.Left, y, rectangle.Right, y);
        }
    }

    private static void DrawTotals(Graphics graphics, Pen ink, Pen faint, Rectangle rectangle)
    {
        if (rectangle.Width < 8 || rectangle.Height < 5) return;
        var y = rectangle.Top + Math.Max(1, rectangle.Height / 3);
        graphics.DrawLine(faint, rectangle.Left, y, rectangle.Right, y);
        graphics.DrawLine(ink, rectangle.Left + rectangle.Width * 62 / 100, rectangle.Top, rectangle.Left + rectangle.Width * 62 / 100, rectangle.Bottom);
        graphics.DrawLine(faint, rectangle.Left + rectangle.Width * 80 / 100, rectangle.Top, rectangle.Left + rectangle.Width * 80 / 100, rectangle.Bottom);
    }

    private static void DrawLineCluster(Graphics graphics, Pen pen, int left, int top, int width, int rows, int spacing)
    {
        width = Math.Max(4, width);
        for (var row = 0; row < rows; row++)
        {
            var shrink = row % 3 * Math.Max(2, width / 9);
            graphics.DrawLine(pen, left, top + row * spacing, left + Math.Max(4, width - shrink), top + row * spacing);
        }
    }

    private static void DrawBarcode(Graphics graphics, Pen pen, Rectangle rectangle)
    {
        if (rectangle.Width < 5 || rectangle.Height < 5) return;
        var x = rectangle.Left;
        var widths = new[] { 1, 2, 1, 3, 1, 1, 2, 3, 2, 1, 1, 3 };
        var index = 0;
        while (x < rectangle.Right)
        {
            var width = widths[index % widths.Length];
            graphics.DrawLine(pen, x, rectangle.Top, x, rectangle.Bottom);
            if (width > 1 && x + 1 < rectangle.Right)
                graphics.DrawLine(pen, x + 1, rectangle.Top, x + 1, rectangle.Bottom);
            x += width + 1;
            index++;
        }
    }

    private static void DrawQr(Graphics graphics, Pen pen, Rectangle rectangle)
    {
        if (rectangle.Width < 9 || rectangle.Height < 9) return;
        const int cells = 9;
        var cell = Math.Max(1, Math.Min(rectangle.Width, rectangle.Height) / cells);
        using var brush = new SolidBrush(pen.Color);
        for (var y = 0; y < cells; y++)
        {
            for (var x = 0; x < cells; x++)
            {
                var finder = InFinder(x, y, 0, 0) || InFinder(x, y, 6, 0) || InFinder(x, y, 0, 6);
                var fill = finder || ((x * 3 + y * 5 + x * y) % 7 < 3);
                if (!fill) continue;
                graphics.FillRectangle(brush, rectangle.Left + x * cell, rectangle.Top + y * cell, cell, cell);
            }
        }
    }

    private static bool InFinder(int x, int y, int left, int top)
    {
        if (x < left || x >= left + 3 || y < top || y >= top + 3) return false;
        var localX = x - left;
        var localY = y - top;
        return localX is 0 or 2 || localY is 0 or 2;
    }
}
