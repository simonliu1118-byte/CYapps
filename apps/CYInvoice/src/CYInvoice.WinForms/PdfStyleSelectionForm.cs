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
                BackColor = SystemColors.Control,
                UseVisualStyleBackColor = false,
                AccessibleName = $"{action}版型 " + DisplayName(style),
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(224, 244, 253);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(202, 235, 250);
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
        if (styleButtons.Any(button => button.FlatAppearance.BorderSize != 0))
            throw new InvalidOperationException("公司發票版型卡片仍顯示常態外框");

        var canvas = new Size(164, 220);
        var a4 = PdfStyleThumbnail.PageBounds(InvoicePdfStyles.A4, canvas);
        var a5 = PdfStyleThumbnail.PageBounds(InvoicePdfStyles.A5, canvas);
        if (a4.Height <= a4.Width || a5.Width <= a5.Height ||
            Math.Abs(a4.Width / (double)a4.Height - 210D / 297D) > 0.03 ||
            Math.Abs(a5.Width / (double)a5.Height - 210D / 148D) > 0.03 ||
            a5.Top != a4.Top || Math.Abs(a5.Width - a4.Width) > 1)
            throw new InvalidOperationException("公司發票 A4／橫式 A5 示意圖比例或位置不正確");
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
    private static readonly Color Canvas = Color.FromArgb(242, 242, 242);
    private static readonly Color PaperEdge = Color.FromArgb(205, 205, 205);
    private static readonly Color Ink = Color.FromArgb(86, 86, 86);
    private static readonly Color Faint = Color.FromArgb(188, 188, 188);

    public static Bitmap Create(InvoicePdfStyle style, Size size)
    {
        var image = new Bitmap(size.Width, size.Height);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Canvas);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var page = PageBounds(style, size);
        using var paperBrush = new SolidBrush(Color.White);
        using var edge = new Pen(PaperEdge, 1F);
        using var ink = new Pen(Ink, 1);
        using var faint = new Pen(Faint, 1);
        graphics.FillRectangle(paperBrush, page);
        graphics.DrawRectangle(edge, page.X, page.Y, page.Width - 1, page.Height - 1);

        switch (style.Code)
        {
            case 0:
                DrawFullA4(graphics, ink, faint, page);
                break;
            case 1:
                DrawAddressA5(graphics, ink, faint, page);
                break;
            case 2:
                DrawA5Content(graphics, ink, faint, page);
                break;
            case 3:
                DrawA5(graphics, ink, faint, page);
                break;
            case 5:
                DrawQrA4(graphics, ink, faint, page);
                break;
        }
        return image;
    }

    internal static Rectangle PageBounds(InvoicePdfStyle style, Size size)
    {
        var availableWidth = Math.Max(1, size.Width - 18);
        var availableHeight = Math.Max(1, size.Height - 14);
        const double a4Ratio = 210D / 297D;
        int a4Width;
        int a4Height;
        if (availableWidth / (double)availableHeight > a4Ratio)
        {
            a4Height = availableHeight;
            a4Width = Math.Max(1, (int)Math.Round(a4Height * a4Ratio));
        }
        else
        {
            a4Width = availableWidth;
            a4Height = Math.Max(1, (int)Math.Round(a4Width / a4Ratio));
        }

        var left = (size.Width - a4Width) / 2;
        var top = 6;
        if (style.Code != InvoicePdfStyles.A5.Code)
            return new Rectangle(left, top, a4Width, a4Height);

        var a5Height = Math.Max(1, (int)Math.Round(a4Width * 148D / 210D));
        return new Rectangle(left, top, a4Width, a5Height);
    }

    private static void DrawFullA4(Graphics graphics, Pen ink, Pen faint, Rectangle page)
    {
        DrawHeader(graphics, ink, faint, page, page.Top + 8);
        DrawSimpleTable(graphics, ink, faint, new Rectangle(page.Left + 6, page.Top + page.Height * 24 / 100, page.Width - 12, page.Height * 58 / 100));
        graphics.DrawLine(faint, page.Left + 7, page.Bottom - 18, page.Right - 7, page.Bottom - 18);
    }

    private static void DrawAddressA5(Graphics graphics, Pen ink, Pen faint, Rectangle page)
    {
        DrawHeader(graphics, ink, faint, page, page.Top + 8);
        DrawTextLines(graphics, faint, page.Left + 10, page.Top + page.Height * 18 / 100, page.Width * 55 / 100, 3, 7);
        graphics.DrawLine(faint, page.Left + 7, page.Top + page.Height / 2, page.Right - 7, page.Top + page.Height / 2);
        var lower = new Rectangle(page.Left + 6, page.Top + page.Height * 55 / 100, page.Width - 12, page.Height * 36 / 100);
        DrawHeader(graphics, ink, faint, lower, lower.Top + 3);
        DrawSimpleTable(graphics, ink, faint, new Rectangle(lower.Left + 2, lower.Top + lower.Height * 38 / 100, lower.Width - 4, lower.Height * 48 / 100));
    }

    private static void DrawA5Content(Graphics graphics, Pen ink, Pen faint, Rectangle page)
    {
        var upper = new Rectangle(page.Left + 6, page.Top + 6, page.Width - 12, page.Height * 43 / 100);
        DrawHeader(graphics, ink, faint, upper, upper.Top + 2);
        DrawSimpleTable(graphics, ink, faint, new Rectangle(upper.Left + 2, upper.Top + upper.Height * 36 / 100, upper.Width - 4, upper.Height * 50 / 100));
    }

    private static void DrawA5(Graphics graphics, Pen ink, Pen faint, Rectangle page)
    {
        DrawHeader(graphics, ink, faint, page, page.Top + 5);
        DrawSimpleTable(graphics, ink, faint, new Rectangle(page.Left + 7, page.Top + page.Height * 34 / 100, page.Width - 14, page.Height * 47 / 100));
    }

    private static void DrawQrA4(Graphics graphics, Pen ink, Pen faint, Rectangle page)
    {
        var receiptWidth = Math.Max(32, page.Width * 40 / 100);
        var receiptHeight = page.Height * 46 / 100;
        var receipt = new Rectangle(
            page.Left + page.Width * 12 / 100,
            page.Top + page.Height * 8 / 100,
            receiptWidth,
            receiptHeight);
        graphics.DrawRectangle(faint, receipt);
        DrawTextLines(graphics, faint, receipt.Left + 5, receipt.Top + 7, receipt.Width - 10, 3, 5);
        var barcode = new Rectangle(receipt.Left + 5, receipt.Top + receipt.Height * 38 / 100, receipt.Width - 10, Math.Max(5, receipt.Height * 10 / 100));
        DrawBarcode(graphics, ink, barcode);
        var qrSize = Math.Max(10, (receipt.Width - 14) / 2);
        DrawQr(graphics, ink, new Rectangle(receipt.Left + 4, receipt.Top + receipt.Height * 55 / 100, qrSize, qrSize));
        DrawQr(graphics, ink, new Rectangle(receipt.Right - 4 - qrSize, receipt.Top + receipt.Height * 55 / 100, qrSize, qrSize));
    }

    private static void DrawHeader(Graphics graphics, Pen ink, Pen faint, Rectangle area, int top)
    {
        var titleWidth = area.Width * 46 / 100;
        var titleLeft = area.Left + (area.Width - titleWidth) / 2;
        graphics.DrawLine(ink, titleLeft, top + 2, titleLeft + titleWidth, top + 2);
        graphics.DrawLine(faint, titleLeft + 6, top + 8, titleLeft + titleWidth - 6, top + 8);
        DrawTextLines(graphics, faint, area.Left + 6, top + 16, area.Width * 42 / 100, 2, 5);
    }

    private static void DrawSimpleTable(Graphics graphics, Pen ink, Pen faint, Rectangle rectangle)
    {
        if (rectangle.Width < 8 || rectangle.Height < 8) return;
        graphics.DrawRectangle(ink, rectangle);
        foreach (var percentage in new[] { 54, 72, 85 })
        {
            var x = rectangle.Left + rectangle.Width * percentage / 100;
            graphics.DrawLine(faint, x, rectangle.Top, x, rectangle.Bottom);
        }
        var header = rectangle.Top + Math.Max(6, rectangle.Height / 5);
        graphics.DrawLine(ink, rectangle.Left, header, rectangle.Right, header);
        graphics.DrawLine(faint, rectangle.Left, rectangle.Top + rectangle.Height * 58 / 100, rectangle.Right, rectangle.Top + rectangle.Height * 58 / 100);
    }

    private static void DrawTextLines(Graphics graphics, Pen pen, int left, int top, int width, int rows, int spacing)
    {
        width = Math.Max(4, width);
        for (var row = 0; row < rows; row++)
        {
            var lineWidth = row == rows - 1 ? width * 72 / 100 : width;
            graphics.DrawLine(pen, left, top + row * spacing, left + lineWidth, top + row * spacing);
        }
    }

    private static void DrawBarcode(Graphics graphics, Pen pen, Rectangle rectangle)
    {
        if (rectangle.Width < 5 || rectangle.Height < 5) return;
        for (var x = rectangle.Left; x < rectangle.Right; x += 3)
            graphics.DrawLine(pen, x, rectangle.Top, x, rectangle.Bottom);
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
                if (!finder && (x * 3 + y * 5 + x * y) % 7 >= 3) continue;
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
