using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using RapidOCRSharpOnnx;
using RapidOCRSharpOnnx.Configurations;
using RapidOCRSharpOnnx.Providers;
using RapidOCRSharpOnnx.Utils;

namespace CYERPAutoQuery;

// Copied from CYERPAutoInput VisionServices.cs (full-image recognition only) by user decision
// 2026-10-16; same engine, models and settings so both tools read ERP screens the same way.

internal static class ScreenCapture
{
    /// <summary>
    /// Like <see cref="Capture"/> but includes layered windows (CAPTUREBLT): ERP's Ribbon
    /// drop-down is one, and a plain copy shows the form behind it (user's PC, 2026-10-11).
    /// </summary>
    public static Bitmap CaptureWithLayers(Rectangle screenRect)
    {
        // CopyFromScreen rejects SourceCopy|CaptureBlt (Build 5 on the user's PC), so BitBlt it.
        var bitmap = new Bitmap(screenRect.Width, screenRect.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var target = graphics.GetHdc();
        var screen = GetDC(0);
        try
        {
            const int SrcCopy = 0x00CC0020, CaptureBlt = 0x40000000;
            BitBlt(target, 0, 0, screenRect.Width, screenRect.Height, screen, screenRect.Left, screenRect.Top, SrcCopy | CaptureBlt);
        }
        finally
        {
            ReleaseDC(0, screen);
            graphics.ReleaseHdc(target);
        }
        return bitmap;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool BitBlt(nint hdcDest, int xDest, int yDest, int width, int height, nint hdcSrc, int xSrc, int ySrc, int rop);

    public static Bitmap Capture(Rectangle screenRect)
    {
        if (screenRect.Width <= 0 || screenRect.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(screenRect));
        var bitmap = new Bitmap(screenRect.Width, screenRect.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(screenRect.Left, screenRect.Top, 0, 0, screenRect.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }
}

// Local PaddleOCR PP-OCRv5 inference (ONNX Runtime, CPU) shared by all optical locators.
internal sealed class PaddleOcrService
{
    private readonly AppLogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RapidOCRSharp? _engine;

    public PaddleOcrService(AppLogger log) => _log = log;

    public async Task<IReadOnlyList<OcrToken>> RecognizeAsync(
        Bitmap bitmap,
        CancellationToken cancellationToken,
        bool requireChinese = false,
        double maxScale = 2.0)
    {
        await _gate.WaitAsync(cancellationToken);
        var temp = Path.Combine(Path.GetTempPath(), $"CYERPAutoQuery_paddle_{Guid.NewGuid():N}.png");
        try
        {
            // The engine itself can throw on a particular image (V0.5.0 LOG, 2026-10-16:
            // IndexOutOfRangeException reading COPI01's Ribbon); the same picture at another scale
            // is read fine, so a failure is retried at other scales before giving up.
            var scales = new[] { maxScale, 1.0, 1.5 }.Distinct().ToArray();
            Bitmap? prepared = null;
            double scale = 1;
            OcrResult? result = null;
            for (var attempt = 0; attempt < scales.Length; attempt++)
            {
                prepared?.Dispose();
                prepared = PrepareForOcr(bitmap, scales[attempt], out scale);
                prepared.Save(temp, ImageFormat.Png);
                cancellationToken.ThrowIfCancellationRequested();
                var engine = EnsureEngine();
                try
                {
                    result = await Task.Run(() => engine.RecognizeText(temp), cancellationToken);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.Warn("vision", $"PaddleOCR failed attempt={attempt + 1} max_scale={scales[attempt]:0.##} image={bitmap.Width}x{bitmap.Height}: {ex.GetType().Name} at {AppLogger.Where(ex)}");
                    if (attempt == scales.Length - 1)
                    {
                        prepared.Dispose();
                        throw new InvalidOperationException("文字辨識（OCR）失敗，請再查詢一次；若一直發生，請把 LOG 傳給開發人員。", ex);
                    }
                }
            }
            using var _ = prepared!;
            cancellationToken.ThrowIfCancellationRequested();

            var tokens = new List<(int Line, OcrToken Token)>();
            if (result!.WordResults is not null)
            {
                foreach (var word in result.WordResults)
                {
                    if (word.Box is null || word.Box.Length == 0 || string.IsNullOrWhiteSpace(word.Word))
                        continue;

                    var minX = word.Box.Min(p => p.X);
                    var minY = word.Box.Min(p => p.Y);
                    var maxX = word.Box.Max(p => p.X);
                    var maxY = word.Box.Max(p => p.Y);
                    var rect = new Rectangle(
                        Math.Max(0, (int)Math.Floor(minX / scale)),
                        Math.Max(0, (int)Math.Floor(minY / scale)),
                        Math.Max(1, (int)Math.Ceiling((maxX - minX) / scale)),
                        Math.Max(1, (int)Math.Ceiling((maxY - minY) / scale)));
                    tokens.Add((word.LineId, new OcrToken(word.Word.Trim(), rect)));
                }
            }

            var ordered = tokens
                .OrderBy(x => x.Line)
                .ThenBy(x => x.Token.Rect.Top)
                .ThenBy(x => x.Token.Rect.Left)
                .Select(x => x.Token)
                .ToArray();

            _log.Info("vision", $"PaddleOCR completed engine=PP-OCRv5_mobile_rec tokens={ordered.Length} image={bitmap.Width}x{bitmap.Height} prepared={prepared.Width}x{prepared.Height} scale={scale:0.##} require_chinese={requireChinese}");
            return ordered;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            _gate.Release();
        }
    }

    private RapidOCRSharp EnsureEngine()
    {
        if (_engine is not null) return _engine;

        var modelDir = Path.Combine(AppContext.BaseDirectory, "runtime", "ocr");
        var detector = Path.Combine(modelDir, "ch_PP-OCRv5_det_mobile.onnx");
        var recognizer = Path.Combine(modelDir, "ch_PP-OCRv5_rec_mobile.onnx");
        var classifier = Path.Combine(modelDir, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx");
        var missing = new[] { detector, recognizer, classifier }.Where(path => !File.Exists(path)).Select(Path.GetFileName).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"PaddleOCR runtime model 缺少：{string.Join("、", missing)}。請使用完整 CYERPAutoQuery 測試包。");

        var config = new OcrConfig(detector, recognizer, LangRec.CH, OCRVersion.PPOCRV5, classifier)
        {
            MinHeight = 4,
            MinSideLen = 8,
            MaxSideLen = 3200,
            ReturnWordBox = true
        };
        config.DetectorConfig.LimitType = LimitType.Max;
        config.DetectorConfig.LimitSideLen = 1800;
        config.DetectorConfig.Thresh = 0.20f;
        config.DetectorConfig.BoxThresh = 0.32f;
        config.DetectorConfig.UnclipRatio = 1.45f;
        config.DetectorConfig.UseDilation = true;
        config.RecognizerConfig.TextScore = 0.30f;
        config.RecognizerConfig.RecBatchNum = 6;

        _engine = new RapidOCRSharp(new ExecutionProviderCPU(config));
        _log.Info("vision", "PaddleOCR initialized model=PP-OCRv5_mobile_rec provider=CPU detector=PP-OCRv5_mobile classifier=PP-LCNet_mobile");
        return _engine;
    }

    private static Bitmap PrepareForOcr(Bitmap source, double maxScale, out double scale)
    {
        var maxSide = Math.Max(source.Width, source.Height);
        scale = maxSide <= 1100 ? 2.0 : maxSide <= 1800 ? 1.5 : 1.0;
        scale = Math.Min(scale, 3000.0 / Math.Max(1, maxSide));
        scale = Math.Clamp(scale, 1.0, Math.Max(1.0, maxScale));

        if (Math.Abs(scale - 1.0) < 0.01)
            return source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);

        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(output);
        g.Clear(Color.White);
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, width, height), new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
        return output;
    }
}
