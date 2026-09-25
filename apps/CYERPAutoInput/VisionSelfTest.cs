using System.Drawing.Drawing2D;

namespace CYERPAutoInput;

internal static class VisionSelfTest
{
    public static async Task<int> RunAsync(AppLogger log)
    {
        try
        {
            Console.WriteLine("CYERPAutoInput vision self-test begin");
            log.Info("selftest", "vision self-test begin");

            using var image = new Bitmap(900, 320);
            using (var g = Graphics.FromImage(image))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using var font = new Font("Microsoft JhengHei UI", 42, FontStyle.Bold, GraphicsUnit.Pixel);
                using var pen = new Pen(Color.FromArgb(90, 90, 90), 1);
                g.DrawString("換算單位  箱", font, Brushes.Black, new PointF(55, 18));
                foreach (var y in new[] { 105, 150, 195, 240, 285 })
                    g.DrawLine(pen, 15, y, 880, y);
            }

            var lines = GridVisionService.FindHorizontalLines(image, 80);
            if (lines.Count < 4)
                throw new InvalidOperationException($"Synthetic grid-line detector returned only {lines.Count} horizontal lines.");

            using var verticalImage = new Bitmap(900, 120);
            using (var g = Graphics.FromImage(verticalImage))
            {
                g.Clear(Color.White);
                using var pen = new Pen(Color.FromArgb(90, 90, 90), 1);
                foreach (var x in new[] { 15, 90, 205, 340, 485, 620, 755, 880 })
                    g.DrawLine(pen, x, 2, x, 105);
            }
            var vertical = GridVisionService.FindVerticalLines(verticalImage, 110);
            if (vertical.Count < 6)
                throw new InvalidOperationException($"Synthetic grid-line detector returned only {vertical.Count} vertical lines.");

            using var ribbon = new Bitmap(220, 160);
            using (var g = Graphics.FromImage(ribbon))
            {
                g.Clear(Color.White);
                using var green = new SolidBrush(Color.FromArgb(50, 160, 70));
                g.FillRectangle(green, 0, 0, 220, 32);
                g.FillRectangle(green, 20, 68, 5, 28);
                g.FillRectangle(green, 9, 80, 28, 5);
            }
            var addIcon = OpticalTextLocator.FindGreenAddIconCandidate(ribbon);
            if (addIcon is null || addIcon.Value.X is < 10 or > 35 || addIcon.Value.Y is < 65 or > 100)
                throw new InvalidOperationException("Synthetic Ribbon green-plus New detection failed.");

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

            var noHeaderCandidate = GridVisionService.FindBestUnitCandidate(
            [new OcrToken("箱", new Rectangle(171, 112, 20, 24))], "箱");
            if (noHeaderCandidate is not null)
                throw new InvalidOperationException("F2 safety failed: a unit was accepted without recognizing the 換算單位 header.");

            var farColumnCandidate = GridVisionService.FindBestUnitCandidate(
            [
                new OcrToken("換算單位", new Rectangle(120, 20, 90, 24)),
                new OcrToken("箱", new Rectangle(520, 112, 20, 24))
            ], "箱");
            if (farColumnCandidate is not null)
                throw new InvalidOperationException("F2 safety failed: a same-text token outside the unit column was accepted.");

            // Exercise the actual bundled PP-OCRv5 runtime, including Traditional
            // Chinese recognition. This intentionally does not depend on Windows OCR
            // language packs anymore.
            var ocr = new WindowsOcrService(log);
            var tokens = await ocr.RecognizeAsync(image, CancellationToken.None, requireChinese: true);
            var joinedText = string.Concat(tokens.Select(t => t.Text)).Replace(" ", string.Empty).Replace("　", string.Empty);
            if (!joinedText.Contains("箱", StringComparison.Ordinal))
                throw new InvalidOperationException($"PaddleOCR ran but did not recognize synthetic Traditional Chinese target 箱 (tokens={tokens.Count}, text={joinedText}).");

            var message = $"vision self-test passed engine=PP-OCRv5 horizontal={lines.Count} vertical={vertical.Count} tokens={tokens.Count} text={joinedText}";
            Console.WriteLine(message);
            log.Info("selftest", message);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            log.Error("selftest", ex);
            return 10;
        }
    }
}
