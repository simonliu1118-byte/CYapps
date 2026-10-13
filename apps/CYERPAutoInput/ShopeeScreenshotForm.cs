using System.Globalization;
namespace CYERPAutoInput;

/// <summary>
/// Paste (Ctrl+V) or drop the screenshots of one Shopee order: the order block, one or
/// more detail blocks and the 買家實付金額 block, in any order (user, 2026-10-11). Each image
/// is recognized with the local OCR and classified; 帶入表單 combines them into one order
/// for the main form. No batch: the user checks the form and starts the input.
/// </summary>
internal sealed class ShopeeScreenshotForm : Form
{
    private sealed class Shot
    {
        public required Bitmap Image { get; init; }
        public ShopeeShotKind Kind { get; set; } = ShopeeShotKind.Unknown;
        public List<OcrBox> Lines { get; set; } = [];
        public bool Done { get; set; }
        public string Error { get; set; } = string.Empty;
        public Label? Caption { get; set; }
    }

    private readonly PaddleOcrService _ocr;
    private readonly ShopeeShotMapping _mapping;
    private readonly AppLogger _log;
    private readonly List<Shot> _shots = [];
    private readonly FlowLayoutPanel _cards = new();
    private readonly Label _summary = new();
    private readonly ProgressBar _progress = new() { Visible = false, Style = ProgressBarStyle.Continuous, Width = 260, Height = 16, Anchor = AnchorStyles.Right };
    private readonly Label _progressText = new() { Visible = false, AutoSize = true, Anchor = AnchorStyles.Right, ForeColor = CyVisualTheme.TextSecondary };
    private readonly CyPrimaryButton _apply = new();
    private readonly AutomationSession? _session;
    private readonly nint _browser;

    public ShopeeShotResult? Result { get; private set; }

    /// <param name="autoStart">Shopee full auto (user, 2026-10-11): start the page scan on opening, without 開始.</param>
    /// <param name="session">Shopee full auto: keeps the mouse locked from the scan on (user, 2026-10-12).</param>
    /// <param name="browser">蝦皮全自動 (batch): its own Chrome window, instead of looking for one.</param>
    public ShopeeScreenshotForm(PaddleOcrService ocr, ShopeeShotMapping mapping, AppLogger log, bool autoStart = false, AutomationSession? session = null, nint browser = 0)
    {
        _browser = browser;
        _ocr = ocr;
        _mapping = mapping;
        _log = log;
        _session = session;
        Text = "蝦皮訂單光學辨識輸入";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowIcon = false; // secondary dialogs do not repeat the app icon (CY Desktop Visual Guide §11.1)
        Size = new Size(980, 600);
        MinimumSize = new Size(640, 420);
        Font = new Font("Microsoft JhengHei UI", 9.5F);
        KeyPreview = true;
        AllowDrop = true;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(root);

        // 說明 sits top right beside the instructions (user, 2026-10-11).
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnCount = 4;
        top.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "先在瀏覽器開好蝦皮訂單明細頁，按「開始」（或在瀏覽器按 F8）：自動捲動擷取、判讀並帶回（Esc 停止）。\n擷取有問題時改用「框選截圖」補上（也接受 Ctrl+V 貼上），不要的圖按紅色 × 移除，再按「帶入表單」。",
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        var help = new CyButton { Text = "說明", Size = new Size(92, 34), Anchor = AnchorStyles.Right, Margin = new Padding(8, 7, 0, 0) };
        help.Click += (_, _) => { using var form = new ShopeeScreenshotHelpForm(); form.ShowDialog(this); };
        var scan = new CyPrimaryButton { Text = "開始", Size = new Size(100, 34), Anchor = AnchorStyles.Right, Margin = new Padding(8, 7, 0, 0) };
        scan.Click += async (_, _) => await AutoScanAsync();
        var capture = new CyButton { Text = "框選截圖", Size = new Size(100, 34), Anchor = AnchorStyles.Right, Margin = new Padding(8, 7, 0, 0) };
        capture.Click += async (_, _) => await CaptureLoopAsync();
        top.Controls.Add(scan, 1, 0);
        top.Controls.Add(capture, 2, 0);
        top.Controls.Add(help, 3, 0);
        root.Controls.Add(top, 0, 0);

        _cards.Dock = DockStyle.Fill;
        _cards.AutoScroll = true;
        _cards.BorderStyle = BorderStyle.FixedSingle;
        _cards.Padding = new Padding(6);
        root.Controls.Add(_cards, 0, 1);

        _summary.Dock = DockStyle.Fill;
        _summary.TextAlign = ContentAlignment.MiddleLeft;
        // Summary on the left; while the auto scan reads, a progress bar on the right.
        var status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        status.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        status.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        status.Controls.Add(_summary, 0, 0);
        _progressText.Margin = new Padding(8, 7, 8, 0);
        _progress.Margin = new Padding(0, 7, 0, 0);
        status.Controls.Add(_progressText, 1, 0);
        status.Controls.Add(_progress, 2, 0);
        root.Controls.Add(status, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(4, 8, 4, 0) };
        _apply.Text = "帶入表單";
        _apply.Size = new Size(120, 34);
        _apply.Click += (_, _) => Apply(automatic: false);
        var cancel = new CyButton { Text = "取消", Size = new Size(92, 34), DialogResult = DialogResult.Cancel };
        var clear = new CyButton { Text = "全部清除", Size = new Size(100, 34), Margin = new Padding(3, 3, 12, 3) };
        clear.Click += (_, _) => { foreach (var shot in _shots) shot.Image.Dispose(); _shots.Clear(); _cards.Controls.Clear(); UpdateSummary(); };
        buttons.Controls.Add(_apply);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(clear);
        root.Controls.Add(buttons, 0, 3);
        CancelButton = cancel;

        KeyDown += async (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.V)
            {
                e.Handled = true;
                await PasteAsync();
            }
        };
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += async (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) await AddFilesAsync(files);
        };
        FormClosed += (_, _) => { foreach (var shot in _shots) shot.Image.Dispose(); };
        if (autoStart) Shown += async (_, _) => { if (_shots.Count == 0 && !UiSnapshot.Active) await AutoScanAsync(); };

        CyVisualTheme.Apply(this);
        UpdateSummary();
    }

    private async Task PasteAsync()
    {
        if (Clipboard.ContainsImage() && Clipboard.GetImage() is Image image)
            await AddAsync(new Bitmap(image));
        else if (Clipboard.ContainsFileDropList())
            await AddFilesAsync(Clipboard.GetFileDropList().Cast<string>().ToArray());
        else
            MessageBox.Show(this, "剪貼簿裡沒有圖片。請先用 Win+Shift+S 截圖，再按 Ctrl+V。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task AddFilesAsync(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            try
            {
                using var loaded = Image.FromFile(file);
                await AddAsync(new Bitmap(loaded));
            }
            catch (Exception ex) when (ex is OutOfMemoryException or IOException or ArgumentException)
            {
                MessageBox.Show(this, $"無法開啟圖片：{Path.GetFileName(file)}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    private bool _capturing;

    /// <summary>
    /// Captures one region after another (user, 2026-10-11): CY's windows step aside, the
    /// frozen desktop is framed, and each selection is added (OCR runs in the background)
    /// before the next capture opens. Esc ends the loop and brings this window back.
    /// </summary>
    private async Task CaptureLoopAsync()
    {
        if (_capturing) return;
        _capturing = true;
        var owner = Owner;
        var ownerOpacity = owner?.Opacity ?? 1;
        try
        {
            while (!IsDisposed)
            {
                Opacity = 0;
                if (owner is not null) owner.Opacity = 0;
                await Task.Delay(250); // let the desktop repaint without CY's windows
                var image = RegionCaptureForm.Capture(this);
                if (image is null) break;
                _log.Info("shopee-shot", $"region captured size={image.Width}x{image.Height}");
                _ = AddAsync(image);
            }
        }
        finally
        {
            if (owner is not null && !owner.IsDisposed) owner.Opacity = ownerOpacity;
            if (!IsDisposed)
            {
                Opacity = 1;
                Activate();
            }
            _capturing = false;
        }
    }

    /// <summary>
    /// Scrolls the browser's Shopee order page from the top and adds its three parts
    /// (user, 2026-10-11). CY's windows step aside and the physical mouse is ignored while the
    /// browser scrolls; Esc stops. When it cannot finish, the manual ways stay available.
    /// </summary>
    private async Task AutoScanAsync()
    {
        if (_capturing) return;
        var browser = _browser != 0 ? _browser : ShopeePageScanner.FindBrowser();
        if (browser == 0)
        {
            MessageBox.Show(this, "找不到開著蝦皮訂單頁的瀏覽器視窗。\n請先在瀏覽器開好訂單明細頁，再按「自動擷取」；也可以用「框選截圖」或 Ctrl+V 貼上。",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _capturing = true;
        var owner = Owner;
        var ownerOpacity = owner?.Opacity ?? 1;
        ShopeePageScanner.Result? result = null;
        var mouseLock = (PhysicalMouseLock?)null;
        // Esc stops the scan the moment the hook sees it, also while the cards are being read.
        using var stop = new CancellationTokenSource();
        void Stop() { try { stop.Cancel(); } catch (ObjectDisposedException) { } }
        var stopped = false;
        void Restore()
        {
            // In full auto the mouse stays locked after the scan, until the run ends or stops.
            mouseLock?.Dispose();
            mouseLock = null;
            if (owner is not null && !owner.IsDisposed) owner.Opacity = ownerOpacity;
            if (!IsDisposed) { Opacity = 1; Activate(); }
        }
        try
        {
            Opacity = 0;
            if (owner is not null) owner.Opacity = 0;
            CancellationTokenRegistration? sessionStop = null;
            if (_session is not null)
            {
                _session.Begin(AutomationSession.FullAutoTitle);
                sessionStop = _session.EscapeToken.Register(Stop);
            }
            else
            {
                mouseLock = PhysicalMouseLock.Engage(_log);
                mouseLock.EscapePressed += Stop;
            }
            using var unregister = sessionStop;
            result = await new ShopeePageScanner(_ocr, _log).ScanAsync(browser,
                () => stop.IsCancellationRequested || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0, stop.Token,
                // Back to this window as soon as the page is captured, with a progress bar for
                // the reading still to do (user, 2026-10-11).
                scrolled: () => { Restore(); ShowProgress("辨識中… 0%", 0, 100); },
                readProgress: new Progress<double>(p => ShowProgress($"辨識中… {(int)(p * 100)}%", (int)(p * 100), 100)));
        }
        catch (OperationCanceledException)
        {
            _log.Info("page-scan", "stopped by Esc");
            stopped = true;
            _session?.End(AutomationOutcome.Cancelled);
        }
        catch (Exception ex)
        {
            _log.Error("page-scan", ex);
            _session?.End();
            MessageBox.Show(this, $"自動擷取失敗：{ex.Message}\n可以改用「框選截圖」或 Ctrl+V 貼上。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            Restore();
            _capturing = false;
        }
        if (stopped && !IsDisposed)
        {
            HideProgress();
            MessageBox.Show(this, "已停止自動擷取。可以按「開始」重來，或用「框選截圖」補上缺的部分後按「帶入表單」。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (result is null || IsDisposed) { _session?.End(); HideProgress(); DisposeCaptures(result); return; }

        ShowProgress("判讀中…", 1, 1);
        foreach (var section in result.Sections)
            await AddAsync(ShopeePageScanner.Stitch(result, section.PageArea), section.Lines, section.Kind);
        DisposeCaptures(result);
        HideProgress();
        if (result.Cancelled) _session?.End(AutomationOutcome.Cancelled);
        else if (result.Problems.Count > 0) _session?.End();
        if (result.Cancelled)
            MessageBox.Show(this, "已停止自動擷取。可以按「開始」重來，或用「框選截圖」補上缺的部分後按「帶入表單」。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        else if (result.Problems.Count > 0)
            MessageBox.Show(this, "自動擷取沒有完整讀到：\n\n" + string.Join("\n", result.Problems.Select(p => "・" + p)) +
                "\n\n請用「框選截圖」補上缺的部分（不要的圖按紅色 × 移除），完成後按「帶入表單」。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        else
            // A clean scan is judged and brought back at once (user, 2026-10-11).
            Apply(automatic: true);

        static void DisposeCaptures(ShopeePageScanner.Result? r)
        {
            if (r is null) return;
            foreach (var c in r.Captures) c.Image.Dispose();
        }
    }

    private void ShowProgress(string text, int value, int maximum)
    {
        if (IsDisposed) return;
        _progress.Maximum = Math.Max(1, maximum);
        // The themed bar animates toward a new value and lags far behind short steps; stepping
        // past and back makes it draw the value at once.
        var target = Math.Clamp(value, 0, _progress.Maximum);
        if (target < _progress.Maximum) { _progress.Value = target + 1; _progress.Value = target; }
        else { _progress.Maximum++; _progress.Value = _progress.Maximum; _progress.Maximum--; }
        _progressText.Text = text;
        _progress.Visible = _progressText.Visible = true;
        UseWaitCursor = true;
        _progress.Refresh();
        _progressText.Refresh();
    }

    private void HideProgress()
    {
        if (IsDisposed) return;
        _progress.Visible = _progressText.Visible = false;
        UseWaitCursor = false;
    }

    /// <summary>
    /// Adds one screenshot; <paramref name="lines"/> are given when the auto scan already read
    /// it, so only the classification and the re-read of missing cells run here.
    /// </summary>
    /// <param name="kind">The part the page scan cut this as; its text alone does not decide then.</param>
    private async Task AddAsync(Bitmap image, List<OcrBox>? lines = null, ShopeeShotKind? kind = null)
    {
        var shot = new Shot { Image = image };
        _shots.Add(shot);
        AddCard(shot);
        UpdateSummary();
        try
        {
            if (lines is not null) shot.Lines = lines;
            else
            {
                var tokens = await _ocr.RecognizeAsync(image, CancellationToken.None);
                shot.Lines = ShopeeScreenshot.MergeLines(tokens.Select(t => new OcrBox(t.Text, t.Rect.Left, t.Rect.Top, t.Rect.Width, t.Rect.Height)));
            }
            shot.Kind = kind ?? ShopeeScreenshot.Classify(shot.Lines);
            if (shot.Kind == ShopeeShotKind.Items) await ReadMissingCellsAsync(shot);
            if (shot.Kind == ShopeeShotKind.Order) ResolveOrderSnZeroO(shot);
            _log.Info("shopee-shot", $"screenshot recognized kind={shot.Kind} lines={shot.Lines.Count} size={image.Width}x{image.Height}");
            // Diagnostic mode only (local log): what the OCR read and where, to explain a misparse.
            if (_log.Diagnostic)
                foreach (var line in shot.Lines)
                    _log.Info("shopee-shot", $"  box x={line.Left} y={line.Top} w={line.Width} h={line.Height} text={_log.Value(line.Text)}");
        }
        catch (Exception ex)
        {
            _log.Error("shopee-shot", ex);
            shot.Error = ex.Message;
        }
        shot.Done = true;
        if (IsDisposed) return;
        UpdateCaption(shot);
        UpdateSummary();
    }

    /// <summary>
    /// 0 or O in the order number's last 8 characters, from the glyph widths in the screenshot
    /// (user, 2026-10-12). The decisions are logged so a wrong call can be traced.
    /// </summary>
    private void ResolveOrderSnZeroO(Shot shot)
    {
        if (ShopeeScreenshot.OrderSnBox(shot.Lines) is not { } box) return;
        var text = ShopeeScreenshot.OrderSnDateDigits(ShopeeScreenshot.Compact(box.Text).ToUpperInvariant());
        if (!text.Skip(6).Any(c => c is '0' or 'O')) return;
        var widths = GlyphRunWidths(shot.Image, new Rectangle(box.Left, box.Top, box.Width, box.Height));
        var (resolved, decisions) = ShopeeScreenshot.ResolveZeroO(text, widths);
        _log.Info("shopee-shot", $"order number 0/O runs={string.Join(",", widths)} decisions={decisions.Count}");
        foreach (var decision in decisions) _log.Info("shopee-shot", $"  {decision}");
        if (resolved == text) return;
        shot.Lines[shot.Lines.IndexOf(box)] = box with { Text = resolved };
    }

    /// <summary>
    /// Widths of the ink runs (columns with text) across a box, left to right. Ink is what
    /// differs clearly from the box's most common shade, so dark text on white and white text
    /// on a grey tag both work. One-pixel runs (box edges, specks) are left out.
    /// </summary>
    private static List<int> GlyphRunWidths(Bitmap image, Rectangle box)
    {
        var area = Rectangle.Intersect(Rectangle.Inflate(box, 1, 0), new Rectangle(Point.Empty, image.Size));
        var widths = new List<int>();
        if (area.Width < 4 || area.Height < 4) return widths;
        using var crop = image.Clone(area, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        var shade = new int[crop.Width, crop.Height];
        var counts = new int[256];
        for (var y = 0; y < crop.Height; y++)
            for (var x = 0; x < crop.Width; x++)
            {
                var c = crop.GetPixel(x, y);
                var v = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                shade[x, y] = v;
                counts[v]++;
            }
        var background = Array.IndexOf(counts, counts.Max());
        var run = 0;
        for (var x = 0; x <= crop.Width; x++)
        {
            var ink = false;
            if (x < crop.Width)
                for (var y = 0; y < crop.Height && !ink; y++) ink = Math.Abs(shade[x, y] - background) > 70;
            if (ink) { run++; continue; }
            if (run > 1) widths.Add(run);
            run = 0;
        }
        return widths;
    }

    /// <summary>
    /// Reads again, with the recognizer alone, the detail cells where a number must be but
    /// the detector found none (a lone "1" in 數量 or 編號).
    /// </summary>
    private async Task ReadMissingCellsAsync(Shot shot)
    {
        var probes = ShopeeScreenshot.MissingCellProbes(shot.Lines, shot.Image.Width, shot.Image.Height);
        var added = 0;
        var doubtfulIndexes = new List<(CellProbe Probe, string Digits)>();
        foreach (var probe in probes)
        {
            if (probe.Width < 4 || probe.Height < 4) continue;
            using var cell = shot.Image.Clone(new Rectangle(probe.Left, probe.Top, probe.Width, probe.Height), shot.Image.PixelFormat);
            using var normalized = F2BatchCellLocator.NormalizeCell(cell);
            using var glyphs = F2BatchCellLocator.TrimToGlyphs(normalized);
            if (glyphs is null) continue;
            var line = await _ocr.RecognizeLineAsync(glyphs, CancellationToken.None);
            // A doubtful read is worse than none: a "1" re-read as "7" at 0.64 made up a 第 7 項
            // (user's PC, 2026-10-11); 0.95 is typical for a clean digit.
            if (line.Confidence < 0.8f || !ShopeeScreenshot.TryProbeDigits(line.Text, out var digits))
            {
                _log.Info("shopee-shot", $"  probe {probe.Purpose} rejected text={_log.Value(line.Text)} confidence={line.Confidence:0.000}");
                if (probe.Purpose == "編號" && ShopeeScreenshot.TryProbeDigits(line.Text, out var doubtful)) doubtfulIndexes.Add((probe, doubtful));
                continue;
            }
            shot.Lines.Add(new OcrBox(digits, probe.Left, probe.Top, probe.Width, probe.Height));
            added++;
        }
        // A doubtful 編號 is kept only when it is exactly the number its neighbours imply: a faint
        // 「1」 above 編號 2 was read as 「1」 at 0.29 and dropped, losing 第 1 項 (user's PC,
        // 2026-10-12). A misread digit (「7」 for 「1」) does not match and stays out.
        foreach (var (probe, doubtful) in doubtfulIndexes)
        {
            var expected = ShopeeScreenshot.ExpectedIndexAt(shot.Lines, shot.Image.Width, probe.Top + probe.Height / 2);
            if (expected is null || doubtful != expected.Value.ToString(CultureInfo.InvariantCulture)) continue;
            shot.Lines.Add(new OcrBox(doubtful, probe.Left, probe.Top, probe.Width, probe.Height));
            added++;
            _log.Info("shopee-shot", $"  probe 編號 accepted by numbering text={doubtful}");
        }
        _log.Info("shopee-shot", $"detail cells re-read probes={probes.Count} recovered={added}");
    }

    private void AddCard(Shot shot)
    {
        var card = new Panel { Size = new Size(190, 148), Margin = new Padding(6) };
        var picture = new PictureBox { Image = shot.Image, SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(0, 0, 190, 118), BorderStyle = BorderStyle.FixedSingle };
        var caption = new Label { Bounds = new Rectangle(0, 122, 190, 22), TextAlign = ContentAlignment.MiddleLeft };
        // A round red × on the picture's top-right corner (user, 2026-10-11).
        var remove = new RemoveBadge { Location = new Point(picture.Width - 26, 4) };
        new ToolTip().SetToolTip(remove, "移除這張截圖");
        remove.Click += (_, _) =>
        {
            _shots.Remove(shot);
            _cards.Controls.Remove(card);
            card.Dispose();
            shot.Image.Dispose();
            UpdateSummary();
        };
        picture.Controls.Add(remove);
        card.Controls.Add(picture);
        card.Controls.Add(caption);
        shot.Caption = caption;
        _cards.Controls.Add(card);
        UpdateCaption(shot);
    }

    private static string KindText(ShopeeShotKind kind) => kind switch
    {
        ShopeeShotKind.Order => "訂單資訊",
        ShopeeShotKind.Items => "明細",
        ShopeeShotKind.Amounts => "買家實付金額",
        _ => "無法判斷（請確認截圖範圍）"
    };

    private static void UpdateCaption(Shot shot)
    {
        if (shot.Caption is null) return;
        shot.Caption.Text = !shot.Done ? "辨識中…" : shot.Error.Length > 0 ? "辨識失敗" : KindText(shot.Kind);
        shot.Caption.ForeColor = shot.Done && (shot.Error.Length > 0 || shot.Kind == ShopeeShotKind.Unknown)
            ? CyVisualTheme.Danger : CyVisualTheme.TextPrimary;
    }

    private void UpdateSummary()
    {
        var pending = _shots.Count(s => !s.Done);
        var order = _shots.Count(s => s.Done && s.Kind == ShopeeShotKind.Order);
        var items = _shots.Count(s => s.Done && s.Kind == ShopeeShotKind.Items);
        var amounts = _shots.Count(s => s.Done && s.Kind == ShopeeShotKind.Amounts);
        string Mark(int n) => n > 0 ? "✓" : "—";
        _summary.Text = $"訂單資訊 {Mark(order)}　明細 {(items > 0 ? $"{items} 張" : "—")}　買家實付金額 {Mark(amounts)}" +
                        (pending > 0 ? $"　（{pending} 張辨識中）" : string.Empty);
        _apply.Enabled = pending == 0 && _shots.Count > 0;
    }

    /// <summary>
    /// Builds the order from the screenshots. <paramref name="automatic"/> (after a clean auto
    /// scan) only brings it back when nothing needs checking; otherwise it says what is wrong
    /// and leaves the manual way open (user, 2026-10-11).
    /// </summary>
    private void Apply(bool automatic)
    {
        // An order needs all three parts (user, 2026-10-11); say which are missing.
        var missingParts = new[]
        {
            _shots.Any(s => s.Kind == ShopeeShotKind.Order) ? null : "訂單資訊（訂單編號、運送資訊）",
            _shots.Any(s => s.Kind == ShopeeShotKind.Items) ? null : "商品明細（表頭與商品）",
            _shots.Any(s => s.Kind == ShopeeShotKind.Amounts) ? null : "買家實付金額"
        }.Where(x => x is not null).ToList();
        if (missingParts.Count > 0)
        {
            _session?.End();
            if (automatic) missingParts.Add("（自動擷取的圖有問題，請用「框選截圖」補上，完成後按「帶入表單」）");
            var unknown = _shots.Count(s => s.Kind == ShopeeShotKind.Unknown);
            MessageBox.Show(this,
                "一張蝦皮訂單需要訂單資訊、商品明細、買家實付金額三種截圖，目前缺少：\n\n" +
                string.Join("\n", missingParts.Select(p => "・" + p)) +
                (unknown > 0 ? $"\n\n另有 {unknown} 張無法判斷種類，請確認截圖範圍（可按右上角「說明」）。" : string.Empty),
                "截圖不完整", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var problems = new List<string>();
        var orderShots = _shots.Where(s => s.Kind == ShopeeShotKind.Order).ToList();
        var amountShots = _shots.Where(s => s.Kind == ShopeeShotKind.Amounts).ToList();
        if (orderShots.Count > 1) problems.Add("貼了不只一張訂單資訊截圖，使用第一張；請確認是同一張訂單。");
        if (amountShots.Count > 1) problems.Add("貼了不只一張金額截圖，使用第一張；請確認是同一張訂單。");

        var order = ShopeeScreenshot.ParseOrder(orderShots[0].Lines, problems);
        var items = new List<ShopeeShotItem>();
        var groups = new List<ShopeeShotGroup>();
        foreach (var shot in _shots.Where(s => s.Kind == ShopeeShotKind.Items))
        {
            var (shotItems, shotGroups) = ShopeeScreenshot.ParseItems(shot.Lines, shot.Image.Width, problems);
            items.AddRange(shotItems);
            groups.AddRange(shotGroups);
        }
        var amounts = ShopeeScreenshot.ParseAmounts(amountShots[0].Lines, problems);
        var result = ShopeeScreenshot.Combine(order, items, groups, amounts, _mapping, problems);
        if (result.Order is null)
        {
            _session?.End();
            MessageBox.Show(this, "無法組成訂單：\n\n" + string.Join("\n", result.Problems.Select(p => "・" + p)), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (automatic && result.Problems.Count > 0)
        {
            _session?.End();
            MessageBox.Show(this, "自動判讀發現問題，已暫停：\n\n" + string.Join("\n", result.Problems.Select(p => "・" + p)) +
                "\n\n請核對截圖：有問題的圖按紅色 × 移除，用「框選截圖」補上，完成後按「帶入表單」。", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Result = result;
        DialogResult = DialogResult.OK;
    }
}
