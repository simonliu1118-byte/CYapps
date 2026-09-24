namespace CYERPAutoInput;

internal sealed class OpticalTextLocator
{
    private readonly WindowsOcrService _ocr;
    private readonly AppLogger _log;

    public OpticalTextLocator(WindowsOcrService ocr, AppLogger log)
    {
        _ocr = ocr;
        _log = log;
    }

    public async Task<Point?> FindTextAsync(nint hwnd, IReadOnlyList<string> aliases, CancellationToken cancellationToken)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
            return null;

        var normalizedAliases = aliases.Select(Normalize).Where(x => x.Length > 0).ToArray();

        // COPI08's Ribbon sometimes exposes button captions as real child-window text.
        // Use that direct signal first for the New button only. Do not use this shortcut
        // for tab captions because a TcxTabSheet can expose the same text over a large
        // content rectangle whose center is not the actual tab header.
        if (normalizedAliases.Contains("新增", StringComparer.Ordinal))
        {
            var direct = Win32Automation.EnumerateChildren(hwnd)
                .Where(c => c.Visible && c.Rect.Width > 0 && c.Rect.Height > 0)
                .Select(c => new { Control = c, Text = Normalize(c.Text) })
                .Where(x => normalizedAliases.Any(a => x.Text == a || x.Text.Contains(a, StringComparison.Ordinal)))
                .OrderBy(x => x.Control.Rect.Top)
                .ThenBy(x => x.Control.Rect.Left)
                .FirstOrDefault();

            if (direct is not null)
            {
                var p = new Point(
                    (direct.Control.Rect.Left + direct.Control.Rect.Right) / 2,
                    (direct.Control.Rect.Top + direct.Control.Rect.Bottom) / 2);
                _log.Info("vision", $"Win32 text target found aliases={aliases.Count} class={direct.Control.ClassName} point={p.X},{p.Y}");
                return p;
            }
        }

        using (var image = ScreenCapture.Capture(rect.ToRectangle()))
        {
            var tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: true);
            var point = FindPoint(tokens, normalizedAliases, rect.Left, rect.Top);
            if (point is not null)
            {
                _log.Info("vision", $"text target found aliases={aliases.Count} point={point.Value.X},{point.Value.Y}");
                return point;
            }
        }

        if (normalizedAliases.Contains("新增", StringComparer.Ordinal))
        {
            var retryRect = new Rectangle(
                rect.Left,
                rect.Top,
                Math.Min(rect.Width, 720),
                Math.Min(rect.Height, 220));

            using var ribbon = ScreenCapture.Capture(retryRect);
            var ribbonTokens = await _ocr.RecognizeAsync(ribbon, cancellationToken, requireChinese: true);
            var retryPoint = FindPoint(ribbonTokens, normalizedAliases, retryRect.Left, retryRect.Top);
            if (retryPoint is not null)
            {
                _log.Info("vision", $"Ribbon OCR target found aliases={aliases.Count} point={retryPoint.Value.X},{retryPoint.Value.Y}");
                return retryPoint;
            }

            // COPI08's New command has a distinctive green plus icon at the left edge
            // of the Ribbon. If text OCR misses the tiny caption, locate that visual
            // plus rather than guessing a hard-coded click coordinate. EnsureInputModeAsync
            // still verifies that the ERP actually entered INPUT after the click.
            var iconPoint = FindGreenAddIconCandidate(ribbon);
            if (iconPoint is not null)
            {
                var p = new Point(retryRect.Left + iconPoint.Value.X, retryRect.Top + iconPoint.Value.Y);
                _log.Info("vision", $"Ribbon green-plus New candidate found point={p.X},{p.Y}");
                return p;
            }

            var sample = string.Join("|", ribbonTokens.Take(24).Select(t => Normalize(t.Text)).Where(t => t.Length > 0));
            _log.Warn("vision", $"Ribbon OCR/green-plus missed 新增 tokens={ribbonTokens.Count} sample={sample}");
        }

        return null;
    }

    internal static Point? FindGreenAddIconCandidate(Bitmap image)
    {
        if (image.Width < 20 || image.Height < 60) return null;

        // Only inspect the upper-left command area. The green application title bar can
        // occupy the top portion, so oversized horizontal/solid green components are
        // deliberately rejected below.
        var scanWidth = Math.Min(image.Width, 140);
        var scanTop = Math.Min(image.Height - 1, 40);
        var scanBottom = Math.Min(image.Height, 150);
        if (scanBottom - scanTop < 15) return null;

        var scanHeight = scanBottom - scanTop;
        var green = new bool[scanWidth, scanHeight];
        for (var y = 0; y < scanHeight; y++)
        {
            for (var x = 0; x < scanWidth; x++)
            {
                var c = image.GetPixel(x, scanTop + y);
                green[x, y] = c.G >= 80 && c.G >= c.R + 20 && c.G >= c.B + 10;
            }
        }

        var visited = new bool[scanWidth, scanHeight];
        var candidates = new List<(int Count, int Left, int Top, int Right, int Bottom)>();
        var directions = new (int X, int Y)[]
        {
            (-1, -1), (0, -1), (1, -1),
            (-1, 0),             (1, 0),
            (-1, 1),  (0, 1),   (1, 1)
        };

        for (var sy = 0; sy < scanHeight; sy++)
        {
            for (var sx = 0; sx < scanWidth; sx++)
            {
                if (!green[sx, sy] || visited[sx, sy]) continue;

                var queue = new Queue<Point>();
                var pixels = new List<Point>();
                queue.Enqueue(new Point(sx, sy));
                visited[sx, sy] = true;

                var left = sx;
                var right = sx;
                var top = sy;
                var bottom = sy;

                while (queue.Count > 0)
                {
                    var p = queue.Dequeue();
                    pixels.Add(p);
                    left = Math.Min(left, p.X);
                    right = Math.Max(right, p.X);
                    top = Math.Min(top, p.Y);
                    bottom = Math.Max(bottom, p.Y);

                    foreach (var d in directions)
                    {
                        var nx = p.X + d.X;
                        var ny = p.Y + d.Y;
                        if (nx < 0 || ny < 0 || nx >= scanWidth || ny >= scanHeight) continue;
                        if (!green[nx, ny] || visited[nx, ny]) continue;
                        visited[nx, ny] = true;
                        queue.Enqueue(new Point(nx, ny));
                    }
                }

                var width = right - left + 1;
                var height = bottom - top + 1;
                if (pixels.Count < 20 || width < 7 || height < 7 || width > 45 || height > 45) continue;

                var fill = pixels.Count / (double)(width * height);
                if (fill > 0.82) continue;

                var centerX = (left + right) / 2;
                var centerY = (top + bottom) / 2;
                var centralRow = pixels.Count(p => Math.Abs(p.Y - centerY) <= 1);
                var centralColumn = pixels.Count(p => Math.Abs(p.X - centerX) <= 1);
                if (centralRow < width || centralColumn < height) continue;

                candidates.Add((pixels.Count, left, top, right, bottom));
            }
        }

        if (candidates.Count == 0) return null;
        var best = candidates
            .OrderBy(c => c.Left)
            .ThenBy(c => c.Top)
            .ThenByDescending(c => c.Count)
            .First();

        return new Point(
            (best.Left + best.Right) / 2,
            scanTop + (best.Top + best.Bottom) / 2);
    }

    private static Point? FindPoint(
        IReadOnlyList<OcrToken> tokens,
        IReadOnlyList<string> normalizedAliases,
        int offsetX,
        int offsetY)
    {
        foreach (var token in tokens)
        {
            var text = Normalize(token.Text);
            if (normalizedAliases.Any(a => text == a || text.Contains(a, StringComparison.Ordinal)))
            {
                return new Point(
                    offsetX + token.Rect.Left + token.Rect.Width / 2,
                    offsetY + token.Rect.Top + token.Rect.Height / 2);
            }
        }

        var rows = tokens
            .GroupBy(t => (t.Rect.Top + t.Rect.Height / 2) / 8)
            .Select(g => g.OrderBy(t => t.Rect.Left).ToList());
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Count; i++)
            {
                var union = row[i].Rect;
                var text = string.Empty;
                for (var j = i; j < row.Count && j < i + 5; j++)
                {
                    if (j > i && row[j].Rect.Left - union.Right > 40) break;
                    text += Normalize(row[j].Text);
                    union = Rectangle.Union(union, row[j].Rect);
                    if (!normalizedAliases.Any(a => text.Contains(a, StringComparison.Ordinal))) continue;
                    return new Point(
                        offsetX + union.Left + union.Width / 2,
                        offsetY + union.Top + union.Height / 2);
                }
            }
        }

        return null;
    }

    private static string Normalize(string value) => value.Trim().Replace(" ", string.Empty).Replace("　", string.Empty);
}
