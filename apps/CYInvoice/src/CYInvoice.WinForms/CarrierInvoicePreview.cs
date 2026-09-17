using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;
using ZXing;
using ZXing.Common;

namespace CYInvoice.WinForms;

internal static class CarrierInvoicePreview
{
    private const string AmegoInvoiceSite = "https://invoice.amego.tw/";
    private static readonly Color Accent = Color.FromArgb(31, 168, 123);

    public static Bitmap Render(InvoiceRecord record, Settings settings)
    {
        var image = new Bitmap(760, 980);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.FromArgb(238, 240, 242));
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var paper = new Rectangle(20, 8, 720, 964);
        using var paperBrush = new SolidBrush(Color.White);
        using var accentBrush = new SolidBrush(Accent);
        using var inkBrush = new SolidBrush(Color.FromArgb(20, 20, 20));
        using var grayBrush = new SolidBrush(Color.FromArgb(78, 78, 78));
        using var faintPen = new Pen(Color.FromArgb(170, 170, 170), 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        graphics.FillRectangle(paperBrush, paper);

        const int sideWidth = 30;
        graphics.FillRectangle(accentBrush, paper.Left, paper.Top, sideWidth, paper.Height);
        graphics.FillRectangle(accentBrush, paper.Right - sideWidth, paper.Top, sideWidth, paper.Height);
        foreach (var y in new[] { paper.Top + 330, paper.Top + 700 })
        {
            graphics.FillRectangle(paperBrush, paper.Left, y, sideWidth, 18);
            graphics.FillRectangle(paperBrush, paper.Right - sideWidth, y, sideWidth, 18);
        }

        using var noticeFont = new Font("Microsoft JhengHei UI", 18F, FontStyle.Regular, GraphicsUnit.Pixel);
        using var titleFont = new Font("Microsoft JhengHei UI", 56F, FontStyle.Bold, GraphicsUnit.Pixel);
        using var subtitleFont = new Font("Microsoft JhengHei UI", 35F, FontStyle.Regular, GraphicsUnit.Pixel);
        using var periodFont = new Font("Microsoft JhengHei UI", 42F, FontStyle.Regular, GraphicsUnit.Pixel);
        using var numberFont = new Font("Microsoft JhengHei UI", 52F, FontStyle.Regular, GraphicsUnit.Pixel);
        using var bodyFont = new Font("Microsoft JhengHei UI", 27F, FontStyle.Regular, GraphicsUnit.Pixel);
        using var smallFont = new Font("Microsoft JhengHei UI", 19F, FontStyle.Regular, GraphicsUnit.Pixel);
        using var footerFont = new Font("Microsoft JhengHei UI", 16F, FontStyle.Regular, GraphicsUnit.Pixel);

        var contentLeft = paper.Left + sideWidth + 24;
        var contentWidth = paper.Width - sideWidth * 2 - 48;
        DrawCentered(graphics, "本明細為模擬畫面僅供參考", noticeFont, grayBrush, contentLeft, paper.Top + 18, contentWidth, 32);
        DrawCentered(graphics, SellerTitle(record, settings), titleFont, inkBrush, contentLeft, paper.Top + 58, contentWidth, 74);
        DrawCentered(graphics, "電子發票證明聯", subtitleFont, inkBrush, contentLeft, paper.Top + 142, contentWidth, 48);
        DrawCentered(graphics, InvoicePeriod(record), periodFont, inkBrush, contentLeft, paper.Top + 198, contentWidth, 56);
        DrawCentered(graphics, FormatInvoiceNumber(record.InvoiceNumber), numberFont, inkBrush, contentLeft, paper.Top + 260, contentWidth, 68);

        var issued = IssueTime(record);
        graphics.DrawString(issued, bodyFont, inkBrush, contentLeft + 8, paper.Top + 342);
        graphics.DrawString($"隨機碼：{SimulationRandomCode(record.InvoiceNumber)}    總計：${MoneyFormatter.Integer(record.Amount)}", bodyFont, inkBrush, contentLeft + 8, paper.Top + 382);
        graphics.DrawString($"賣方：{SellerBan(settings)}", bodyFont, inkBrush, contentLeft + 8, paper.Top + 422);
        graphics.DrawString($"載具：{Mask(record.CarrierId1)}", smallFont, grayBrush, contentLeft + 8, paper.Top + 462);

        var barcodeRect = new Rectangle(contentLeft, paper.Top + 506, contentWidth, 92);
        DrawBarcode(graphics, barcodeRect);
        graphics.DrawLine(faintPen, contentLeft + 18, paper.Top + 626, contentLeft + contentWidth - 18, paper.Top + 626);

        var qrSize = 210;
        var qrTop = paper.Top + 654;
        DrawQr(graphics, new Rectangle(contentLeft + 26, qrTop, qrSize, qrSize));
        DrawQr(graphics, new Rectangle(contentLeft + contentWidth - 26 - qrSize, qrTop, qrSize, qrSize));
        DrawCentered(graphics, "掃描條碼將開啟光貿電子發票網站", footerFont, grayBrush, contentLeft, paper.Top + 874, contentWidth, 28);
        DrawCentered(graphics, "模擬畫面｜非正式憑證", noticeFont, grayBrush, contentLeft, paper.Top + 910, contentWidth, 30);
        return image;
    }

    private static void DrawBarcode(Graphics graphics, Rectangle rectangle)
    {
        using var barcode = CreateBarcode(BarcodeFormat.CODE_128, rectangle.Size, margin: 0);
        graphics.DrawImage(barcode, rectangle);
    }

    private static void DrawQr(Graphics graphics, Rectangle rectangle)
    {
        using var qr = CreateBarcode(BarcodeFormat.QR_CODE, rectangle.Size, margin: 1);
        graphics.DrawImage(qr, rectangle);
    }

    private static Bitmap CreateBarcode(BarcodeFormat format, Size size, int margin)
    {
        var writer = new ZXing.Windows.Compatibility.BarcodeWriter
        {
            Format = format,
            Options = new EncodingOptions
            {
                Width = size.Width,
                Height = size.Height,
                Margin = margin,
                PureBarcode = true,
                NoPadding = true,
            },
        };
        return writer.Write(AmegoInvoiceSite);
    }

    private static string SimulationRandomCode(string invoiceNumber)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(invoiceNumber.Trim().ToUpperInvariant()));
        var value = ((hash[0] << 8) | hash[1]) % 10_000;
        return value.ToString("0000", CultureInfo.InvariantCulture);
    }

    private static void DrawCentered(Graphics graphics, string text, Font font, Brush brush, int x, int y, int width, int height)
    {
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
        };
        graphics.DrawString(text, font, brush, new RectangleF(x, y, width, height), format);
    }

    private static string SellerTitle(InvoiceRecord record, Settings settings) =>
        record.Environment == Environments.Test || settings.Environment == Environments.Test ? "光貿測試公司" : "會員載具發票";

    private static string SellerBan(Settings settings) => settings.Environment == Environments.Production
        ? settings.ProductionInvoice.Trim()
        : AmegoDefaults.TestInvoice;

    private static string IssueTime(InvoiceRecord record)
    {
        var value = record.InvoiceDate.Length == 0 ? record.SentAt : (record.InvoiceDate + " " + record.InvoiceTime).Trim();
        return value.Length == 0 ? "開立時間未記錄" : value;
    }

    private static string InvoicePeriod(InvoiceRecord record)
    {
        var text = record.InvoiceDate.Length == 0 ? record.SentAt : record.InvoiceDate;
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return "電子發票";
        var firstMonth = date.Month % 2 == 0 ? date.Month - 1 : date.Month;
        return $"{date.Year - 1911}年 {firstMonth:00}-{firstMonth + 1:00}月";
    }

    private static string FormatInvoiceNumber(string value)
    {
        value = value.Trim();
        return value.Length == 10 ? value[..2] + "-" + value[2..] : value;
    }

    private static string Mask(string value)
    {
        value = value.Trim();
        if (value.Length == 0) return "會員載具";
        if (value.Length <= 4) return new string('*', value.Length);
        return value[..2] + new string('*', Math.Min(8, value.Length - 4)) + value[^2..];
    }
}
