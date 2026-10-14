using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using PigDocument = UglyToad.PdfPig.PdfDocument;
using PigOptions = UglyToad.PdfPig.ParsingOptions;

namespace CYERPAutoInput;

/// <summary>
/// MO店+ 寄件單 PDFs (password like the order export) for the thermal printer (user, 2026-10-13:
/// 全家／7-11 come as A4 pages of four slips). A page that is one picture (全家: 2615×4271) is
/// used as it is, pixel for pixel; any other page is drawn by Windows at
/// <c>renderDpi</c>. Each page then goes through <see cref="SlipImage"/> like the 蝦皮 全家 slip.
/// </summary>
internal static class MoSlipPdf
{
    public static async Task<List<(GrayImage Page, string From)>> PagesAsync(string path, string password, int renderDpi)
    {
        var pages = new List<(GrayImage, string)>();
        using var document = PigDocument.Open(path, new PigOptions { Password = password });
        foreach (var page in document.GetPages())
        {
            var picture = page.GetImages().OrderByDescending(i => (long)i.WidthInSamples * i.HeightInSamples).FirstOrDefault();
            if (picture is not null && page.Text.Trim().Length == 0 && Math.Min(picture.WidthInSamples, picture.HeightInSamples) >= 1000 &&
                picture.TryGetPng(out var png) && SlipPrinter.Load(png) is { } image)
            {
                pages.Add((image, $"第 {page.Number} 頁原圖"));
                continue;
            }
            var drawn = await RenderAsync(path, password, page.Number - 1, renderDpi);
            if (drawn is not null) pages.Add((drawn, $"第 {page.Number} 頁以 {renderDpi} dpi 繪出"));
        }
        return pages;
    }

    private static async Task<GrayImage?> RenderAsync(string path, string password, int index, int dpi)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        var pdf = password.Length > 0 ? await PdfDocument.LoadFromFileAsync(file, password) : await PdfDocument.LoadFromFileAsync(file);
        using var page = pdf.GetPage((uint)index);
        using var stream = new InMemoryRandomAccessStream();
        // Page size is in 1/96 inch.
        await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = (uint)Math.Round(page.Size.Width / 96.0 * dpi) });
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var length = (uint)stream.Size;
        await reader.LoadAsync(length);
        var bytes = new byte[length];
        reader.ReadBytes(bytes);
        return SlipPrinter.Load(bytes);
    }
}
