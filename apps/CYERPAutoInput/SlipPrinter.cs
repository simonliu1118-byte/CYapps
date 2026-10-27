using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace CYERPAutoInput;

/// <summary>
/// The Windows half of <see cref="SlipImage"/>: the picture saved from Chrome is loaded,
/// the label's size in the printer's own dots is read from the driver, and the cut slip is
/// printed dot for dot (no scaling by Windows), centred on the label.
/// </summary>
internal static partial class SlipPrinter
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, nint token, out nint path);

    /// <summary>The user's 下載 folder (it can be moved, so asked from Windows).</summary>
    public static string DownloadsFolder()
    {
        if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, 0, out var p) != 0) return string.Empty;
        try { return Marshal.PtrToStringUni(p) ?? string.Empty; }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    /// <summary>The installed printer whose name matches the set one (the set name may be shortened).</summary>
    public static string? Installed(string wanted) =>
        PrinterSettings.InstalledPrinters.Cast<string>().FirstOrDefault(p => string.Equals(p, wanted, StringComparison.OrdinalIgnoreCase))
        ?? PrinterSettings.InstalledPrinters.Cast<string>().FirstOrDefault(p => ShopeeBatchScreens.SamePrinter(p, wanted));

    /// <summary>The width of one <see cref="SlipNote"/> line in mm, in the type it is printed in at <paramref name="points"/>.</summary>
    public static double NoteWidthMm(string line, float points)
    {
        // Fitting measures many trial lines per size (as typed in 設定): one surface, a font per size.
        lock (NoteMeasure)
        {
            return SlipNote.Runs(line).Sum(run =>
            {
                var key = (points, run.Symbol);
                if (!NoteFonts.TryGetValue(key, out var font))
                    NoteFonts[key] = font = NoteFont(run.Symbol, points, GraphicsUnit.Point);
                return (double)NoteMeasure.Value.MeasureString(run.Text, font, PointF.Empty, StringFormat.GenericTypographic).Width;
            });
        }
    }

    private static Font NoteFont(bool symbol, float size, GraphicsUnit unit) =>
        new(symbol ? SlipNote.SymbolFontName : SlipNote.FontName, size, symbol ? FontStyle.Regular : FontStyle.Bold, unit);

    private static readonly Lazy<Graphics> NoteMeasure = new(() =>
    {
        var g = Graphics.FromImage(new Bitmap(1, 1));
        g.PageUnit = GraphicsUnit.Millimeter;
        return g;
    });
    private static readonly Dictionary<(float, bool), Font> NoteFonts = [];

    /// <summary>
    /// The printer's resolution and the label in its dots: the set label size, or the driver's
    /// paper when that is smaller.
    /// </summary>
    public static (int Dpi, int Width, int Height, string Paper) Label(string printer, double widthMm, double heightMm)
    {
        var settings = new PrinterSettings { PrinterName = printer };
        if (!settings.IsValid) throw new InvalidOperationException($"找不到印表機 {printer}");
        int dpi;
        using (var g = settings.CreateMeasurementGraphics()) dpi = (int)Math.Round(g.DpiX);
        var page = settings.DefaultPageSettings;
        var paperW = page.PaperSize.Width / 100.0 * 25.4;
        var paperH = page.PaperSize.Height / 100.0 * 25.4;
        if (page.Landscape) (paperW, paperH) = (paperH, paperW);
        var w = Math.Min(widthMm, paperW > 0 ? paperW : widthMm);
        var h = Math.Min(heightMm, paperH > 0 ? paperH : heightMm);
        return (dpi, (int)(w / 25.4 * dpi), (int)(h / 25.4 * dpi), $"{page.PaperSize.PaperName} {paperW:F0}x{paperH:F0}mm");
    }

    /// <summary>A picture file as gray bytes (transparent counts as white); null when it is not a picture.</summary>
    public static GrayImage? Load(string path)
    {
        try
        {
            using var source = new Bitmap(path);
            return Gray(source);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException or IOException)
        {
            return null;
        }
    }

    public static GrayImage? Load(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            using var source = new Bitmap(stream);
            return Gray(source);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException)
        {
            return null;
        }
    }

    /// <summary>A picture in pure black and white (darker than mid grey is black), for the thermal head.</summary>
    public static GrayImage BlackWhite(Bitmap source)
    {
        var gray = Gray(source);
        for (var i = 0; i < gray.Pixels.Length; i++) gray.Pixels[i] = gray.Pixels[i] < 128 ? (byte)0 : (byte)255;
        return gray;
    }

    private static GrayImage Gray(Bitmap source)
    {
        using var bmp = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.DrawImage(source, 0, 0, source.Width, source.Height);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[bmp.Width * 4];
            var pixels = new byte[bmp.Width * bmp.Height];
            for (var y = 0; y < bmp.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (var x = 0; x < bmp.Width; x++)
                {
                    int b = row[x * 4], gr = row[x * 4 + 1], r = row[x * 4 + 2], a = row[x * 4 + 3];
                    pixels[y * bmp.Width + x] = a < 128 ? (byte)255 : (byte)((r * 299 + gr * 587 + b * 114) / 1000);
                }
            }
            return new GrayImage(bmp.Width, bmp.Height, pixels);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>
    /// The largest picture Chrome saved: picture files in the folder (also the page's _files
    /// folder), and pictures written into a saved page as data: links. Null when there is none
    /// at least 400 pixels each way.
    /// </summary>
    public static (GrayImage Image, string From)? LargestPicture(string folder)
    {
        (GrayImage Image, string From)? best = null;
        void Consider(GrayImage? image, string from)
        {
            if (image is null || Math.Min(image.Width, image.Height) < 400) return;
            if (best is null || (long)image.Width * image.Height > (long)best.Value.Image.Width * best.Value.Image.Height) best = (image, from);
        }
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".htm" or ".html" or ".mhtml")
            {
                var text = File.ReadAllText(file);
                foreach (Match m in DataPicture().Matches(text))
                {
                    try { Consider(Load(Convert.FromBase64String(m.Groups[1].Value)), "data: in " + ext); }
                    catch (FormatException) { }
                }
                // A single-file page (.mhtml) keeps its pictures as base64 parts.
                if (ext == ".mhtml")
                    foreach (Match m in MhtmlPicture().Matches(text))
                    {
                        try { Consider(Load(Convert.FromBase64String(m.Groups[1].Value)), "part in .mhtml"); }
                        catch (FormatException) { }
                    }
            }
            else if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff" or "")
                Consider(Load(file), ext.Length > 0 ? ext : "(no extension)");
        }
        return best;
    }

    [GeneratedRegex(@"data:image/(?:png|jpe?g|gif|bmp);base64,([A-Za-z0-9+/=\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DataPicture();

    [GeneratedRegex(@"Content-Type:\s*image/[^\r\n]*\r?\n(?:[^\r\n]+\r?\n)*?Content-Transfer-Encoding:\s*base64\r?\n(?:[^\r\n]+\r?\n)*?\r?\n([A-Za-z0-9+/=\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex MhtmlPicture();

    /// <summary>
    /// A4 pages (drawn from a PDF) printed as one document, each fitted to the printable area and
    /// centred, as Acrobat's 符合 does (user's screenshot, 2026-10-14: 96 %).
    /// </summary>
    /// <param name="duplex">Both sides, flipped on the long edge (酷澎 一般消費者紙本發票, CYInvoice 2026-10-15); a printer without duplex is an error.</param>
    public static void PrintPages(string printer, IReadOnlyList<Bitmap> pages, string documentName, bool duplex = false)
    {
        using var doc = new PrintDocument();
        doc.PrinterSettings.PrinterName = printer;
        if (!doc.PrinterSettings.IsValid) throw new InvalidOperationException($"找不到印表機 {printer}");
        if (duplex)
        {
            if (!doc.PrinterSettings.CanDuplex) throw new InvalidOperationException($"印表機 {printer} 不支援雙面列印");
            doc.PrinterSettings.Duplex = Duplex.Vertical; // long edge for a portrait page
        }
        doc.DocumentName = documentName;
        doc.PrintController = new StandardPrintController();
        doc.OriginAtMargins = false;
        var next = 0;
        doc.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var page = pages[next];
            var area = e.PageSettings.PrintableArea; // 1/100 inch, from the paper's corner
            if (e.PageSettings.Landscape) area = new RectangleF(area.Y, area.X, area.Height, area.Width);
            var scale = Math.Min(area.Width / page.Width, area.Height / page.Height);
            var w = page.Width * scale;
            var h = page.Height * scale;
            // The graphics start at the printable area's corner.
            g.DrawImage(page, (area.Width - w) / 2, (area.Height - h) / 2, w, h);
            next++;
            e.HasMorePages = next < pages.Count;
        };
        doc.Print();
        PrintWatch.Watch(doc.PrinterSettings.PrinterName, doc.DocumentName, 60);
    }

    /// <summary>
    /// One picture, every pixel one printer dot, its top-left corner <paramref name="leftMm"/>,
    /// <paramref name="topMm"/> from the paper's corner (黑貓 on A4, user 2026-10-26: top left as
    /// in its PDF); what the printer cannot reach at the edge moves it in that far.
    /// </summary>
    public static void PrintAt(string printer, Bitmap picture, double leftMm, double topMm, string documentName)
    {
        using var doc = new PrintDocument();
        doc.PrinterSettings.PrinterName = printer;
        if (!doc.PrinterSettings.IsValid) throw new InvalidOperationException($"找不到印表機 {printer}");
        doc.DocumentName = documentName;
        doc.PrintController = new StandardPrintController();
        doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        doc.OriginAtMargins = false;
        doc.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            g.PageUnit = GraphicsUnit.Pixel;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            // The graphics start at the printable area's corner.
            var x = (int)Math.Round((leftMm / 25.4 - e.PageSettings.HardMarginX / 100.0) * g.DpiX);
            var y = (int)Math.Round((topMm / 25.4 - e.PageSettings.HardMarginY / 100.0) * g.DpiY);
            g.DrawImage(picture, new Rectangle(Math.Max(0, x), Math.Max(0, y), picture.Width, picture.Height), 0, 0, picture.Width, picture.Height, GraphicsUnit.Pixel);
            e.HasMorePages = false;
        };
        doc.Print();
        PrintWatch.Watch(doc.PrinterSettings.PrinterName, doc.DocumentName);
    }

    /// <summary>
    /// Prints the slip as one page: each of its pixels one printer dot (GraphicsUnit.Pixel,
    /// nearest neighbour), centred on the page. With a <paramref name="note"/> (<see cref="SlipNote"/>)
    /// the block sits at the bottom of the 150 mm label and the slip is centred above it; a slip
    /// too tall to leave room for it is printed as before, without the note (Noted false).
    /// No progress window.
    /// </summary>
    public static (int DpiX, int DpiY, bool Noted) Print(string printer, GrayImage slip, string documentName, string note = "", float minPoints = SlipNote.MinPoints)
    {
        using var bmp = new Bitmap(slip.Width, slip.Height, PixelFormat.Format24bppRgb);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[data.Stride];
            for (var y = 0; y < slip.Height; y++)
            {
                for (var x = 0; x < slip.Width; x++)
                {
                    var v = slip[x, y];
                    row[x * 3] = row[x * 3 + 1] = row[x * 3 + 2] = v;
                }
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, data.Stride);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        var used = (0, 0);
        var noted = false;
        var hasNote = SlipNote.Lines(note).Count > 0;
        (float Points, IReadOnlyList<string> Lines)? fit = null;
        using var doc = new PrintDocument();
        doc.PrinterSettings.PrinterName = printer;
        doc.DocumentName = documentName;
        doc.PrintController = new StandardPrintController();
        doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        doc.OriginAtMargins = false;
        doc.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            g.PageUnit = GraphicsUnit.Pixel;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.None;
            used = ((int)Math.Round(g.DpiX), (int)Math.Round(g.DpiY));
            // The graphics start at the printable area's corner; the page is centred on the paper.
            var pageW = e.PageBounds.Width / 100f * g.DpiX;
            var pageH = e.PageBounds.Height / 100f * g.DpiY;
            var x = (int)Math.Round((pageW - bmp.Width) / 2 - e.PageSettings.HardMarginX / 100f * g.DpiX);
            var marginY = e.PageSettings.HardMarginY / 100f * g.DpiY;
            var y = (int)Math.Round((pageH - bmp.Height) / 2 - marginY);
            var dotsPerMm = g.DpiY / 25.4f;
            var areaH = Math.Min(pageH, (float)SlipNote.LabelHeightMm * dotsPerMm);
            var blockH = (float)SlipNote.BlockHeightMm * dotsPerMm;
            var gap = (float)SlipNote.GapMm * dotsPerMm;
            noted = hasNote && bmp.Height + gap + blockH <= areaH;
            var areaTop = (pageH - areaH) / 2 - marginY;
            if (noted)
            {
                // The slip at the top at its own size; the words get all the room under it
                // (user, 2026-10-26: the more room, the larger they print).
                y = (int)Math.Round(areaTop);
                blockH = areaH - bmp.Height - gap;
                fit = SlipNote.Fit(note, NoteWidthMm, blockH / dotsPerMm, minPoints);
                noted = fit is not null;
                if (!noted) y = (int)Math.Round((pageH - bmp.Height) / 2 - marginY);
            }
            g.DrawImage(bmp, new Rectangle(Math.Max(0, x), Math.Max(0, y), bmp.Width, bmp.Height), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel);
            if (noted && fit is { } f)
            {
                using var font = NoteFont(false, f.Points / 72f * g.DpiY, GraphicsUnit.Pixel);
                using var symbolFont = NoteFont(true, f.Points / 72f * g.DpiY, GraphicsUnit.Pixel);
                var lineH = Math.Min((float)SlipNote.LineHeightMm(f.Points) * dotsPerMm, blockH / f.Lines.Count);
                var centreX = pageW / 2 - e.PageSettings.HardMarginX / 100f * g.DpiX;
                // The lines centred in the block, each centred across it.
                var top = areaTop + areaH - blockH + (blockH - lineH * f.Lines.Count) / 2;
                // Pure black dots on the thermal head, no grey edges.
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                for (var i = 0; i < f.Lines.Count; i++)
                {
                    // A run at a time, symbols in their own font, the whole line centred.
                    var runs = SlipNote.Runs(f.Lines[i]).Select(r => (r.Text, Font: r.Symbol ? symbolFont : font)).ToList();
                    var widths = runs.Select(r => g.MeasureString(r.Text, r.Font, PointF.Empty, StringFormat.GenericTypographic).Width).ToList();
                    var penX = centreX - widths.Sum() / 2;
                    for (var r = 0; r < runs.Count; r++)
                    {
                        g.DrawString(runs[r].Text, runs[r].Font, Brushes.Black, penX, top + i * lineH, StringFormat.GenericTypographic);
                        penX += widths[r];
                    }
                }
            }
            e.HasMorePages = false;
        };
        doc.Print();
        PrintWatch.Watch(doc.PrinterSettings.PrinterName, doc.DocumentName);
        return (used.Item1, used.Item2, noted);
    }
}
