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

            var joinedLatin = GridVisionService.FindPhrase(
            [
                new OcrToken("VIS", new Rectangle(20, 20, 45, 24)),
                new OcrToken("ION", new Rectangle(67, 20, 45, 24))
            ], ["VISION"]);
            if (joinedLatin is null)
                throw new InvalidOperationException("Joined OCR-token Latin phrase matching failed.");

            var joinedChinese = GridVisionService.FindPhrase(
            [
                new OcrToken("換算", new Rectangle(120, 20, 42, 24)),
                new OcrToken("單位", new Rectangle(166, 20, 42, 24))
            ], ["換算單位"]);
            if (joinedChinese is null)
                throw new InvalidOperationException("Joined OCR-token Chinese phrase matching failed.");

            var unitCandidate = GridVisionService.FindBestUnitCandidate(
            [
                new OcrToken("換算", new Rectangle(120, 20, 42, 24)),
                new OcrToken("單位", new Rectangle(166, 20, 42, 24)),
                new OcrToken("支", new Rectangle(171, 75, 20, 24)),
                new OcrToken("箱", new Rectangle(171, 112, 20, 24)),
                new OcrToken("箱", new Rectangle(500, 150, 20, 24))
            ], "箱");
            if (unitCandidate is null || unitCandidate.Rect.Top != 112)
                throw new InvalidOperationException("F2 requested-unit candidate selection failed.");

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
