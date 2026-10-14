using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
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
    /// Prints the slip as one page: each of its pixels one printer dot (GraphicsUnit.Pixel,
    /// nearest neighbour), centred on the page. No progress window.
    /// </summary>
    public static (int DpiX, int DpiY) Print(string printer, GrayImage slip, string documentName)
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
            var y = (int)Math.Round((pageH - bmp.Height) / 2 - e.PageSettings.HardMarginY / 100f * g.DpiY);
            g.DrawImage(bmp, new Rectangle(Math.Max(0, x), Math.Max(0, y), bmp.Width, bmp.Height), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel);
            e.HasMorePages = false;
        };
        doc.Print();
        return used;
    }
}
