using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal static class CarrierInvoicePreview
{
    private static readonly Color Accent = Color.FromArgb(31, 168, 123);

    public static Bitmap Render(InvoiceRecord record, Settings settings)
    {
        var image = new Bitmap(520, 720);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.FromArgb(238, 240, 242));
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var paper = new Rectangle(28, 12, 464, 696);
        using var paperBrush = new SolidBrush(Color.White);
        using var accentBrush = new SolidBrush(Accent);
        using var inkBrush = new SolidBrush(Color.FromArgb(28, 28, 28));
        using var grayBrush = new SolidBrush(Color.FromArgb(92, 92, 92));
        using var faintPen = new Pen(Color.FromArgb(178, 178, 178), 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        graphics.FillRectangle(paperBrush, paper);
        graphics.FillRectangle(accentBrush, paper.Left, paper.Top, 18, paper.Height);
        graphics.FillRectangle(accentBrush, paper.Right - 18, paper.Top, 18, paper.Height);

        using var noticeFont = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold, GraphicsUnit.Pixel);
        using var titleFont = new Font("Microsoft JhengHei UI", 31F, FontStyle.Bold, GraphicsUnit.Pixel);
        using var subtitleFont = new Font("Microsoft JhengHei UI", 24F, FontStyle.Regular, GraphicsUnit.Pixel);
        using var periodFont = new Font("Microsoft JhengHei UI", 27F, FontStyle.Regular, GraphicsUnit.Pixel);
        using var numberFont = new Font("Microsoft JhengHei UI", 34F, FontStyle.Regular, GraphicsUnit.Pixel);
        using var bodyFont = new Font("Microsoft JhengHei UI", 17F, FontStyle.Regular, GraphicsUnit.Pixel);
        using var smallFont = new Font("Microsoft JhengHei UI", 13F, FontStyle.Regular, GraphicsUnit.Pixel);

        DrawCentered(graphics, "本畫面為模擬預覽，非正式憑證", noticeFont, grayBrush, paper.Left + 24, paper.Top + 12, paper.Width - 48, 26);
        DrawCentered(graphics, SellerTitle(record, settings), titleFont, inkBrush, paper.Left + 28, paper.Top + 45, paper.Width - 56, 48);
        DrawCentered(graphics, "電子發票證明聯", subtitleFont, inkBrush, paper.Left + 28, paper.Top + 96, paper.Width - 56, 38);
        DrawCentered(graphics, InvoicePeriod(record), periodFont, inkBrush, paper.Left + 28, paper.Top + 134, paper.Width - 56, 42);
        DrawCentered(graphics, FormatInvoiceNumber(record.InvoiceNumber), numberFont, inkBrush, paper.Left + 28, paper.Top + 176, paper.Width - 56, 48);

        var issued = IssueTime(record);
        graphics.DrawString(issued, bodyFont, inkBrush, paper.Left + 42, paper.Top + 230);
        graphics.DrawString($"隨機碼：----    總計：${MoneyFormatter.Integer(record.Amount)}", bodyFont, inkBrush, paper.Left + 42, paper.Top + 258);
        graphics.DrawString($"賣方：{SellerBan(settings)}", bodyFont, inkBrush, paper.Left + 42, paper.Top + 286);
        graphics.DrawString($"載具：{Mask(record.CarrierId1)}", smallFont, grayBrush, paper.Left + 42, paper.Top + 315);

        DrawBarcode(graphics, record.InvoiceNumber, new Rectangle(paper.Left + 42, paper.Top + 348, paper.Width - 84, 70));
        graphics.DrawLine(faintPen, paper.Left + 42, paper.Top + 438, paper.Right - 42, paper.Top + 438);

        var qrSize = 142;
        DrawPseudoQr(graphics, record.InvoiceNumber + "L", new Rectangle(paper.Left + 58, paper.Top + 464, qrSize, qrSize));
        DrawPseudoQr(graphics, record.InvoiceNumber + "R", new Rectangle(paper.Right - 58 - qrSize, paper.Top + 464, qrSize, qrSize));
        DrawCentered(graphics, "示意圖形｜不可掃描", smallFont, grayBrush, paper.Left + 42, paper.Top + 615, paper.Width - 84, 24);
        DrawCentered(graphics, "會員載具｜發票資料已保存於雲端", noticeFont, grayBrush, paper.Left + 42, paper.Top + 652, paper.Width - 84, 26);
        return image;
    }

    private static void DrawBarcode(Graphics graphics, string seed, Rectangle rectangle)
    {
        using var background = new SolidBrush(Color.White);
        using var ink = new SolidBrush(Color.Black);
        graphics.FillRectangle(background, rectangle);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var x = rectangle.Left + 4;
        var index = 0;
        while (x < rectangle.Right - 4)
        {
            var width = 1 + hash[index % hash.Length] % 4;
            var gap = 1 + hash[(index + 7) % hash.Length] % 3;
            graphics.FillRectangle(ink, x, rectangle.Top + 4, width, rectangle.Height - 8);
            x += width + gap;
            index++;
        }
    }

    private static void DrawPseudoQr(Graphics graphics, string seed, Rectangle rectangle)
    {
        const int cells = 25;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        using var background = new SolidBrush(Color.White);
        using var ink = new SolidBrush(Color.Black);
        graphics.FillRectangle(background, rectangle);
        var cell = Math.Max(1, rectangle.Width / cells);
        for (var y = 0; y < cells; y++)
        {
            for (var x = 0; x < cells; x++)
            {
                var finder = InFinder(x, y, 1, 1) || InFinder(x, y, cells - 8, 1) || InFinder(x, y, 1, cells - 8);
                var bit = (hash[(x * 7 + y * 13) % hash.Length] & (1 << ((x + y) % 8))) != 0;
                if (finder || bit) graphics.FillRectangle(ink, rectangle.Left + x * cell, rectangle.Top + y * cell, cell, cell);
            }
        }
        using var warning = new SolidBrush(Color.FromArgb(225, Color.White));
        graphics.FillRectangle(warning, rectangle.Left + 20, rectangle.Top + rectangle.Height / 2 - 10, rectangle.Width - 40, 20);
        using var font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold, GraphicsUnit.Pixel);
        using var gray = new SolidBrush(Color.FromArgb(70, 70, 70));
        DrawCentered(graphics, "示意", font, gray, rectangle.Left + 20, rectangle.Top + rectangle.Height / 2 - 10, rectangle.Width - 40, 20);
    }

    private static bool InFinder(int x, int y, int left, int top)
    {
        if (x < left || x >= left + 7 || y < top || y >= top + 7) return false;
        var localX = x - left;
        var localY = y - top;
        return localX is 0 or 6 || localY is 0 or 6 || (localX is >= 2 and <= 4 && localY is >= 2 and <= 4);
    }

    private static void DrawCentered(Graphics graphics, string text, Font font, Brush brush, int x, int y, int width, int height)
    {
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
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
