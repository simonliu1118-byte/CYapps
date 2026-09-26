using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CYEnvelope;

// All positions are millimetres from the top-left of the physical envelope.
// This drawing function is used by both the preview and the printer.
public static class EnvelopeRenderer
{
    public const double DipPerMm = 96.0 / 25.4;
    private const double DipPerPoint = 96.0 / 72.0;
    private static readonly Brush Ink = Brushes.Black;
    private static readonly Pen Red = new(new SolidColorBrush(Color.FromRgb(176, 61, 64)), .24 * DipPerMm);
    private static readonly Pen Black = new(Ink, .35 * DipPerMm);

    public static DrawingVisual Draw(EnvelopeFormat format, PrintData data, bool preview, string? highlight = null)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        var width = format.WidthMm * DipPerMm;
        var height = format.HeightMm * DipPerMm;
        if (preview)
        {
            dc.DrawRectangle(Brushes.White, new Pen(Brushes.LightGray, 1), new Rect(0, 0, width, height));
            DrawPrintedEnvelopeReference(dc, format);
        }
        dc.PushTransform(new TranslateTransform(format.OffsetX * DipPerMm, format.OffsetY * DipPerMm));
        Write(dc, data.Recipient, format.Recipient);
        Write(dc, data.Address, format.Address);
        Write(dc, data.Phone, format.Phone);
        DrawPostal(dc, data.PostalCode, format.PostalCode);
        foreach (var item in format.Delivery.Where(x => data.DeliveryIds.Contains(x.Id)))
            WriteAt(dc, "✓", item.X, item.Y, 11, "Microsoft JhengHei UI");
        if (data.ShowFrame && data.FrameText.Length != 0)
        {
            var frame = Dip(format.Frame);
            dc.DrawRectangle(null, Black, frame);
            var text = new TextPlacement
            {
                Rect = new RectMm(format.Frame.X, format.Frame.Y, format.Frame.Width, format.Frame.Height),
                FontFamily = "DFKai-SB", FontSize = 11, Vertical = true
            };
            Write(dc, data.FrameText, text);
        }
        dc.Pop();
        if (preview && highlight is not null) DrawHighlight(dc, format, highlight);
        return visual;
    }

    private static void DrawPrintedEnvelopeReference(DrawingContext dc, EnvelopeFormat f)
    {
        // Red ink belongs to the purchased envelope. It is never sent to the printer.
        var w = f.WidthMm;
        var h = f.HeightMm;
        Line(dc, 0, 0, w, 0);
        Line(dc, w, 0, w, h);
        Line(dc, w, h, 0, h);
        Line(dc, 0, h, 0, 0);
        Line(dc, 0, 17, w / 2, 5);
        Line(dc, w / 2, 5, w, 17);
        dc.DrawRectangle(null, Red, new Rect((w - 23) * DipPerMm, 7 * DipPerMm, 17 * DipPerMm, 14 * DipPerMm));
        Label(dc, "貼郵票處", w - 21, 10, 7, Brushes.IndianRed);
        for (var i = 0; i < 3; i++)
            dc.DrawRectangle(null, Red, new Rect((f.PostalCode.Rect.X + i * 8) * DipPerMm,
                f.PostalCode.Rect.Y * DipPerMm, 8 * DipPerMm, 8 * DipPerMm));
        Label(dc, "郵件種類", 5, 57, 7, Brushes.IndianRed);
        foreach (var item in f.Delivery)
        {
            dc.DrawRectangle(null, Red, new Rect((item.X - 1) * DipPerMm, (item.Y - .8) * DipPerMm, 3 * DipPerMm, 3 * DipPerMm));
            Label(dc, item.Label, item.X + 3, item.Y, 7, Brushes.IndianRed);
        }
        dc.DrawRectangle(null, Red, new Rect(40 * DipPerMm, 43 * DipPerMm, 56 * DipPerMm, 151 * DipPerMm));
        for (var i = 0; i < 3; i++)
            dc.DrawRectangle(null, Red, new Rect((37 + i * 8) * DipPerMm, (h - 14) * DipPerMm, 8 * DipPerMm, 8 * DipPerMm));
        Label(dc, "正聯", 6, 22, 7, Brushes.IndianRed);
    }

    private static void DrawPostal(DrawingContext dc, string code, TextPlacement position)
    {
        var x = position.Rect.X;
        foreach (var (ch, index) in code.Take(3).Select((c, i) => (c, i)))
            WriteAt(dc, ch.ToString(), x + index * 8 + 1.2, position.Rect.Y + .8,
                position.FontSize, position.FontFamily);
    }

    private static void Write(DrawingContext dc, string value, TextPlacement p)
    {
        if (string.IsNullOrEmpty(value)) return;
        var r = Dip(p.Rect);
        dc.PushClip(new RectangleGeometry(r));
        var glyphs = StringInfo.GetTextElementEnumerator(value);
        var line = 0;
        var column = 0;
        var rowHeight = Math.Max(4, p.FontSize * 1.2 * DipPerPoint);
        var columnWidth = Math.Max(4, p.FontSize * DipPerPoint);
        while (glyphs.MoveNext())
        {
            var glyph = glyphs.GetTextElement();
            if (glyph == "\n" || (p.Vertical && (line + 1) * rowHeight > r.Height))
            {
                column++;
                line = 0;
                if (glyph == "\n") continue;
            }
            if (column >= Math.Max(1, p.Columns)) break;
            var x = p.Vertical ? p.Rect.X + p.Rect.Width - (column + 1) * columnWidth / DipPerMm
                               : p.Rect.X + line * columnWidth / DipPerMm;
            var y = p.Vertical ? p.Rect.Y + line * rowHeight / DipPerMm : p.Rect.Y;
            WriteAt(dc, glyph, x, y, p.FontSize, p.FontFamily);
            line++;
        }
        dc.Pop();
    }

    private static void WriteAt(DrawingContext dc, string value, double x, double y, double size, string family)
    {
        var font = new Typeface(new FontFamily(family), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var text = new FormattedText(value, CultureInfo.GetCultureInfo("zh-TW"), FlowDirection.LeftToRight,
            font, size * DipPerPoint, Ink, 1);
        dc.DrawText(text, new Point(x * DipPerMm, y * DipPerMm));
    }

    private static void Label(DrawingContext dc, string value, double x, double y, double size, Brush brush)
    {
        var text = new FormattedText(value, CultureInfo.GetCultureInfo("zh-TW"), FlowDirection.LeftToRight,
            new Typeface("Microsoft JhengHei UI"), size * DipPerPoint, brush, 1);
        dc.DrawText(text, new Point(x * DipPerMm, y * DipPerMm));
    }

    private static void DrawHighlight(DrawingContext dc, EnvelopeFormat f, string field)
    {
        RectMm? box = field switch
        {
            "收件人" => f.Recipient.Rect,
            "地址" => f.Address.Rect,
            "電話" => f.Phone.Rect,
            "郵遞區號" => f.PostalCode.Rect,
            "方框文字" => f.Frame,
            _ => null
        };
        if (box is not null)
            dc.DrawRectangle(null, new Pen(Brushes.RoyalBlue, 2), Dip(box));
    }

    private static void Line(DrawingContext dc, double x1, double y1, double x2, double y2) =>
        dc.DrawLine(Red, new Point(x1 * DipPerMm, y1 * DipPerMm), new Point(x2 * DipPerMm, y2 * DipPerMm));
    private static Rect Dip(RectMm r) => new(r.X * DipPerMm, r.Y * DipPerMm, r.Width * DipPerMm, r.Height * DipPerMm);
}
