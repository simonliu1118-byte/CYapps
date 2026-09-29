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

    // printerOrigin: the printer's imageable-area origin (DIP), removed so millimetres stay paper-relative.
    public static DrawingVisual Draw(EnvelopeFormat format, PrintData data, bool preview, string? highlight = null,
        Vector printerOrigin = default)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        dc.PushTransform(new TranslateTransform(-printerOrigin.X, -printerOrigin.Y));
        if (preview)
        {
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
            dc.DrawRectangle(null, Black, Dip(format.Frame));
            Write(dc, data.FrameText, FrameText(format));
        }
        dc.Pop();
        if (preview && highlight is not null) DrawHighlight(dc, format, highlight);
        dc.Pop();
        return visual;
    }

    private static void DrawPrintedEnvelopeReference(DrawingContext dc, EnvelopeFormat f)
    {
        // Fixed preprinted artwork: never derive the paper's red ink from editable text boxes.
        // Visible geometry traced proportionally from the user's 393px 15K product photograph.
        // The label obscures the lower section; the frame's lower end remains an approximation.
        dc.PushTransform(new ScaleTransform(f.WidthMm / (f.Landscape ? 222 : 105),
            f.HeightMm / (f.Landscape ? 105 : 222)));
        if (f.Landscape)
        {
            dc.PushTransform(new TranslateTransform(222 * DipPerMm, 0));
            dc.PushTransform(new RotateTransform(90));
        }
        var outline = new Pen(new SolidColorBrush(Color.FromRgb(209, 213, 219)), .2 * DipPerMm);
        dc.DrawRectangle(Brushes.White, outline, new Rect(0, 18 * DipPerMm, 105 * DipPerMm, 204 * DipPerMm));
        // The neutral trapezoid is the open flap, not red envelope artwork.
        var flap = new StreamGeometry();
        using (var g = flap.Open())
        {
            g.BeginFigure(new Point(0, 18 * DipPerMm), true, true);
            g.LineTo(new Point(7 * DipPerMm, 0), true, false);
            g.LineTo(new Point(98 * DipPerMm, 0), true, false);
            g.LineTo(new Point(105 * DipPerMm, 18 * DipPerMm), true, false);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(249, 248, 245)), outline, flap);
        Box(6, 30, 20, 20);
        Label(dc, "郵 票", 9, 33, 6, Red.Brush);
        Label(dc, "黏貼處", 8, 41, 6, Red.Brush);
        for (var i = 0; i < 6; i++) Box(49 + i * 8, 31, 6, 9);
        Label(dc, "收件人郵遞區號", 51, 43, 5, Red.Brush);
        // The narrow central name frame is distinct from the address printed to its right.
        Box(37, 58, 32, 142);
        var labels = new[] { "平信", "限時", "掛號", "限時掛號", "印刷品", "航空", "其他" };
        Label(dc, "郵件種類", 6, 60, 5, Red.Brush);
        Box(6, 64, 21, 28);
        Line(dc, 10, 64, 10, 92);
        for (var i = 0; i < labels.Length; i++)
        {
            if (i > 0) Line(dc, 6, 64 + i * 4, 27, 64 + i * 4);
            Label(dc, labels[i], 11, 64 + i * 4, 5, Red.Brush);
        }
        if (f.Landscape) { dc.Pop(); dc.Pop(); }
        dc.Pop();
        void Box(double x, double y, double w, double h) =>
            dc.DrawRectangle(null, Red, new Rect(x * DipPerMm, y * DipPerMm, w * DipPerMm, h * DipPerMm));
    }

    private static void DrawPostal(DrawingContext dc, string code, TextPlacement position)
    {
        var x = position.Rect.X;
        foreach (var (ch, index) in code.Take(3).Select((c, i) => (c, i)))
            WriteAt(dc, ch.ToString(), x + (position.Vertical ? 1.2 : index * 8 + 1.2),
                position.Rect.Y + (position.Vertical ? index * 8 + .8 : .8),
                position.FontSize, position.FontFamily);
    }

    // Long text first shrinks towards this share of the configured size (never below the floor).
    private const double ShrinkLimit = .7;
    private const double MinimumPoint = 7;

    // Field names whose text cannot fit in its box even after shrinking; the rest would be cut off.
    public static List<string> Overflows(EnvelopeFormat format, PrintData data)
    {
        var result = new List<string>();
        foreach (var (name, value, placement) in new[]
        {
            ("收件人", data.Recipient, format.Recipient), ("地址", data.Address, format.Address),
            ("電話", data.Phone, format.Phone),
            ("方框文字", data.ShowFrame ? data.FrameText : "", FrameText(format))
        })
            if (!Layout(value, placement).Complete) result.Add(name);
        return result;
    }

    private static TextPlacement FrameText(EnvelopeFormat format) => new()
    {
        Rect = new RectMm(format.Frame.X, format.Frame.Y, format.Frame.Width, format.Frame.Height),
        FontFamily = "DFKai-SB", FontSize = 11, Vertical = true
    };

    private static (List<(string Glyph, double X, double Y)> Glyphs, double Size, bool Complete) Layout(
        string value, TextPlacement p)
    {
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext()) elements.Add(enumerator.GetTextElement());
        var minimum = Math.Min(p.FontSize, Math.Max(MinimumPoint, p.FontSize * ShrinkLimit));
        for (var size = p.FontSize; ; size -= .5)
        {
            var (glyphs, complete) = LayoutAt(elements, p, size);
            if (complete || size - .5 < minimum) return (glyphs, size, complete);
        }
    }

    private static (List<(string Glyph, double X, double Y)>, bool) LayoutAt(List<string> elements, TextPlacement p, double size)
    {
        var r = Dip(p.Rect);
        var glyphs = new List<(string, double, double)>();
        var line = 0;
        var column = 0;
        var rowHeight = Math.Max(4, size * 1.2 * DipPerPoint);
        var columnWidth = Math.Max(4, size * DipPerPoint);
        foreach (var glyph in elements)
        {
            if (glyph == "\n" || (p.Vertical && (line + 1) * rowHeight > r.Height) ||
                (!p.Vertical && (line + 1) * columnWidth > r.Width))
            {
                column++;
                line = 0;
                if (glyph == "\n") continue;
            }
            if (column >= Math.Max(1, p.Columns) ||
                (p.Vertical && (column + 1) * columnWidth > r.Width) ||
                (!p.Vertical && (column + 1) * rowHeight > r.Height)) return (glyphs, false);
            var x = p.Vertical ? p.Rect.X + p.Rect.Width - (column + 1) * columnWidth / DipPerMm
                               : p.Rect.X + line * columnWidth / DipPerMm;
            var y = p.Vertical ? p.Rect.Y + line * rowHeight / DipPerMm
                               : p.Rect.Y + column * rowHeight / DipPerMm;
            glyphs.Add((glyph, x, y));
            line++;
        }
        return (glyphs, true);
    }

    private static void Write(DrawingContext dc, string value, TextPlacement p)
    {
        if (string.IsNullOrEmpty(value)) return;
        var (glyphs, size, _) = Layout(value, p);
        dc.PushClip(new RectangleGeometry(Dip(p.Rect)));
        foreach (var (glyph, x, y) in glyphs) WriteAt(dc, glyph, x, y, size, p.FontFamily);
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
