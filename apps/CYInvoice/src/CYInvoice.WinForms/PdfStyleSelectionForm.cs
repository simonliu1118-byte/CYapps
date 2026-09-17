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
            var thumbnail = PdfStyleThumbnail.Create(style, new Size(150, 214));
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
    public static Bitmap Create(InvoicePdfStyle style, Size size)
    {
        var image = new Bitmap(size.Width, size.Height);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.White);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var border = new Pen(Color.FromArgb(34, 166, 225), 2);
        using var ink = new Pen(Color.FromArgb(76, 76, 76), 1);
        using var faint = new Pen(Color.FromArgb(178, 178, 178), 1);
        graphics.DrawRectangle(border, 1, 1, size.Width - 3, size.Height - 3);

        var page = Rectangle.Inflate(new Rectangle(12, 10, size.Width - 24, size.Height - 20), -2, -2);
        DrawHeader(graphics, ink, faint, page);
        switch (style.Code)
        {
            case 0:
                DrawTable(graphics, ink, new Rectangle(page.Left + 5, page.Top + 50, page.Width - 10, page.Height - 68), 5);
                break;
            case 1:
                graphics.DrawRectangle(faint, page.Left + 5, page.Top + 48, page.Width - 10, 36);
                DrawTable(graphics, ink, new Rectangle(page.Left + 5, page.Top + 98, page.Width - 10, page.Height - 116), 3);
                break;
            case 2:
                DrawTable(graphics, ink, new Rectangle(page.Left + 5, page.Top + 50, page.Width - 10, 68), 4);
                graphics.DrawLine(faint, page.Left + 5, page.Top + 135, page.Right - 5, page.Top + 135);
                break;
            case 3:
                DrawTable(graphics, ink, new Rectangle(page.Left + 5, page.Top + 50, page.Width - 10, 78), 4);
                break;
            case 5:
                DrawReceipt(graphics, ink, faint, new Rectangle(page.Left + 40, page.Top + 40, page.Width - 80, page.Height - 60));
                break;
        }
        return image;
    }

    private static void DrawHeader(Graphics graphics, Pen ink, Pen faint, Rectangle page)
    {
        graphics.DrawLine(ink, page.Left + 12, page.Top + 12, page.Right - 12, page.Top + 12);
        graphics.DrawLine(faint, page.Left + 22, page.Top + 22, page.Right - 22, page.Top + 22);
        graphics.DrawLine(faint, page.Left + 7, page.Top + 34, page.Left + page.Width / 2, page.Top + 34);
    }

    private static void DrawTable(Graphics graphics, Pen pen, Rectangle rectangle, int rows)
    {
        graphics.DrawRectangle(pen, rectangle);
        graphics.DrawLine(pen, rectangle.Left + rectangle.Width / 2, rectangle.Top, rectangle.Left + rectangle.Width / 2, rectangle.Bottom);
        graphics.DrawLine(pen, rectangle.Left + rectangle.Width * 3 / 4, rectangle.Top, rectangle.Left + rectangle.Width * 3 / 4, rectangle.Bottom);
        for (var row = 1; row < rows; row++)
        {
            var y = rectangle.Top + rectangle.Height * row / rows;
            graphics.DrawLine(pen, rectangle.Left, y, rectangle.Right, y);
        }
    }

    private static void DrawReceipt(Graphics graphics, Pen ink, Pen faint, Rectangle rectangle)
    {
        graphics.DrawRectangle(ink, rectangle);
        for (var row = 0; row < 5; row++)
        {
            var y = rectangle.Top + 12 + row * 9;
            graphics.DrawLine(faint, rectangle.Left + 8, y, rectangle.Right - 8, y);
        }
        var qrSize = Math.Max(16, rectangle.Width / 3);
        graphics.DrawRectangle(ink, rectangle.Left + 7, rectangle.Bottom - qrSize - 8, qrSize, qrSize);
        graphics.DrawRectangle(ink, rectangle.Right - qrSize - 7, rectangle.Bottom - qrSize - 8, qrSize, qrSize);
    }
}
