using System.Drawing.Drawing2D;

namespace CYERPAutoInput;

internal static class VisionSelfTest
{
    public static async Task<int> RunAsync(AppLogger log)
    {
        try
        {
            log.Info("selftest", "vision self-test begin");
            using var image = new Bitmap(900, 320);
            using (var g = Graphics.FromImage(image))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using var font = new Font("Segoe UI", 34, FontStyle.Bold, GraphicsUnit.Pixel);
                using var pen = new Pen(Color.FromArgb(90, 90, 90), 1);
                g.DrawString("VISION TEST", font, Brushes.Black, new PointF(55, 22));
                foreach (var y in new[] { 105, 150, 195, 240, 285 })
                    g.DrawLine(pen, 15, y, 880, y);
            }

            var lines = GridVisionService.FindHorizontalLines(image, 80);
            if (lines.Count < 4)
                throw new InvalidOperationException($"Synthetic grid-line detector returned only {lines.Count} lines.");

            var joined = GridVisionService.FindPhrase(
            [
                new OcrToken("VIS", new Rectangle(20, 20, 45, 24)),
                new OcrToken("ION", new Rectangle(67, 20, 45, 24))
            ], ["VISION"]);
            if (joined is null)
                throw new InvalidOperationException("Joined OCR-token phrase matching failed.");

            var ocr = new WindowsOcrService(log);
            var tokens = await ocr.RecognizeAsync(image, CancellationToken.None);
            if (!tokens.Any(t => t.Text.Replace(" ", string.Empty).Contains("VISION", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Windows OCR ran but did not recognize synthetic VISION text (tokens={tokens.Count}).");

            log.Info("selftest", $"vision self-test passed lines={lines.Count} tokens={tokens.Count}");
            return 0;
        }
        catch (Exception ex)
        {
            log.Error("selftest", ex);
            return 10;
        }
    }
}
