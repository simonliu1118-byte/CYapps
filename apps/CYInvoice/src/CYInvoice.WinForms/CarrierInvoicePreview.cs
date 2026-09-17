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
        var image = new Bitmap(600, 820);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.FromArgb(238, 240, 242));
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var paper = new Rectangle(20, 8, 560, 804);
        using var paperBrush = new SolidBrush(Color.White);
        using var accentBrush = new SolidBrush(Accent);
        using var inkBrush = new SolidBrush(Color.FromArgb(20, 20, 20));
        using var grayBrush = new SolidBrush(Color.FromArgb(78, 78, 78));
        using var faintPen = new Pen(Color.FromArgb(170, 170, 170), 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        graphics.FillRectangle(paperBrush, paper);

        const int sideWidth = 18;
        graphics.FillRectangle(accentBrush, paper.Left, paper.Top, sideWidth, paper.Height);
        graphics.FillRectangle(accentBrush, paper.Right - sideWidth, paper.Top, sideWidth, paper.Height);
        foreach (var y in new[] { paper.Top + 300, paper.Top + 635 })
        {
            graphics.FillRectangle(paperBrush, paper.Left, y, sideWidth, 14);
            graphics.FillRectangle(paperBrush, paper.Right - sideWidth, y, sideWidth, 14);
        }

        using var noticeFont = new Font("Microsoft JhengHei UI", 13F, FontStyle.Regular, GraphicsUnit.Pixel);
        using var titleFont = new Font("Microsoft JhengHei UI", 37F, FontStyle.Bold, GraphicsUnit.Pixel);
        using var subtitleFont = new Font("Microsoft JhengHei UI", 24F, FontStyle.Regular, GraphicsUnit.Pixel);
        using var periodFont = new Font("Microsoft JhengHei UI", 31F, FontStyle.Regular, GraphicsUnit.Pixel);
        using var numberFont = new Font("Microsoft JhengHei UI", 39F, FontStyle.Regular, GraphicsUnit.Pixel);
        using var bodyFont = new Font("Microsoft JhengHei UI", 20F, FontStyle.Regular, GraphicsUnit.Pixel);
        using var smallFont = new Font("Microsoft JhengHei UI", 15F, FontStyle.Regular, GraphicsUnit.Pixel);

        var contentLeft = paper.Left + sideWidth + 22;
        var contentWidth = paper.Width - sideWidth * 2 - 44;
        DrawCentered(graphics, "本明細為模擬畫面僅供參考", noticeFont, grayBrush, contentLeft, paper.Top + 14, contentWidth, 24);
        DrawCentered(graphics, SellerTitle(record, settings), titleFont, inkBrush, contentLeft, paper.Top + 48, contentWidth, 52);
        DrawCentered(graphics, "電子發票證明聯", subtitleFont, inkBrush, contentLeft, paper.Top + 104, contentWidth, 34);
        DrawCentered(graphics, InvoicePeriod(record), periodFont, inkBrush, contentLeft, paper.Top + 145, contentWidth, 42);
        DrawCentered(graphics, FormatInvoiceNumber(record.InvoiceNumber), numberFont, inkBrush, contentLeft, paper.Top + 194, contentWidth, 50);

        var issued = IssueTime(record);
        graphics.DrawString(issued, bodyFont, inkBrush, contentLeft + 6, paper.Top + 278);
        graphics.DrawString($"隨機碼：{SimulationRandomCode(record.InvoiceNumber)}    總計：${MoneyFormatter.Integer(record.Amount)}", bodyFont, inkBrush, contentLeft + 6, paper.Top + 314);
        graphics.DrawString($"賣方：{SellerBan(settings)}", bodyFont, inkBrush, contentLeft + 6, paper.Top + 350);
        graphics.DrawString($"載具：{Mask(record.CarrierId1)}", smallFont, grayBrush, contentLeft + 6, paper.Top + 386);

        var barcodeRect = new Rectangle(contentLeft, paper.Top + 430, contentWidth, 62);
        DrawBarcode(graphics, barcodeRect);
        graphics.DrawLine(faintPen, contentLeft + 16, paper.Top + 516, contentLeft + contentWidth - 16, paper.Top + 516);

        var qrSize = 190;
        var qrTop = paper.Top + 552;
        DrawQr(graphics, new Rectangle(contentLeft + 18, qrTop, qrSize, qrSize));
        DrawQr(graphics, new Rectangle(contentLeft + contentWidth - 18 - qrSize, qrTop, qrSize, qrSize));
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
