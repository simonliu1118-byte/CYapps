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
    private readonly CyPrimaryButton _apply = new();

    public ShopeeShotResult? Result { get; private set; }

    public ShopeeScreenshotForm(PaddleOcrService ocr, ShopeeShotMapping mapping, AppLogger log)
    {
        _ocr = ocr;
        _mapping = mapping;
        _log = log;
        Text = "蝦皮訂單截圖輸入";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowIcon = false; // secondary dialogs do not repeat the app icon (CY Desktop Visual Guide §11.1)
        Size = new Size(860, 580);
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

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "用 Win+Shift+S 截取蝦皮訂單頁，在這裡按 Ctrl+V 貼上（也可拖曳圖片檔）。\n需要：訂單資訊（訂單編號、運送資訊）、明細（可多張）、買家實付金額；順序不限。",
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _cards.Dock = DockStyle.Fill;
        _cards.AutoScroll = true;
        _cards.BorderStyle = BorderStyle.FixedSingle;
        _cards.Padding = new Padding(6);
        root.Controls.Add(_cards, 0, 1);

        _summary.Dock = DockStyle.Fill;
        _summary.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(_summary, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(4, 8, 4, 0) };
        _apply.Text = "帶入表單";
        _apply.Size = new Size(120, 34);
        _apply.Click += (_, _) => Apply();
        var cancel = new CyButton { Text = "取消", Size = new Size(92, 34), DialogResult = DialogResult.Cancel };
        var clear = new CyButton { Text = "全部清除", Size = new Size(100, 34), Margin = new Padding(3, 3, 12, 3) };
        var help = new CyButton { Text = "截圖說明", Size = new Size(100, 34) };
        help.Click += (_, _) => { using var form = new ShopeeScreenshotHelpForm(); form.ShowDialog(this); };
        clear.Click += (_, _) => { foreach (var shot in _shots) shot.Image.Dispose(); _shots.Clear(); _cards.Controls.Clear(); UpdateSummary(); };
        buttons.Controls.Add(_apply);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(clear);
        buttons.Controls.Add(help);
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

    private async Task AddAsync(Bitmap image)
    {
        var shot = new Shot { Image = image };
        _shots.Add(shot);
        AddCard(shot);
        UpdateSummary();
        try
        {
            var tokens = await _ocr.RecognizeAsync(image, CancellationToken.None);
            shot.Lines = ShopeeScreenshot.MergeLines(tokens.Select(t => new OcrBox(t.Text, t.Rect.Left, t.Rect.Top, t.Rect.Width, t.Rect.Height)));
            shot.Kind = ShopeeScreenshot.Classify(shot.Lines);
            if (shot.Kind == ShopeeShotKind.Items) await ReadMissingCellsAsync(shot);
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
    /// Reads again, with the recognizer alone, the detail cells where a number must be but
    /// the detector found none (a lone "1" in 數量 or 編號).
    /// </summary>
    private async Task ReadMissingCellsAsync(Shot shot)
    {
        var probes = ShopeeScreenshot.MissingCellProbes(shot.Lines, shot.Image.Width, shot.Image.Height);
        var added = 0;
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
                continue;
            }
            shot.Lines.Add(new OcrBox(digits, probe.Left, probe.Top, probe.Width, probe.Height));
            added++;
        }
        _log.Info("shopee-shot", $"detail cells re-read probes={probes.Count} recovered={added}");
    }

    private void AddCard(Shot shot)
    {
        var card = new Panel { Size = new Size(190, 170), Margin = new Padding(6) };
        var picture = new PictureBox { Image = shot.Image, SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(0, 0, 190, 118), BorderStyle = BorderStyle.FixedSingle };
        var caption = new Label { Bounds = new Rectangle(0, 122, 190, 22), TextAlign = ContentAlignment.MiddleLeft };
        var remove = new LinkLabel { Text = "移除", Bounds = new Rectangle(0, 146, 60, 20) };
        remove.LinkClicked += (_, _) =>
        {
            _shots.Remove(shot);
            _cards.Controls.Remove(card);
            card.Dispose();
            shot.Image.Dispose();
            UpdateSummary();
        };
        card.Controls.Add(picture);
        card.Controls.Add(caption);
        card.Controls.Add(remove);
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

    private void Apply()
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
            var unknown = _shots.Count(s => s.Kind == ShopeeShotKind.Unknown);
            MessageBox.Show(this,
                "一張蝦皮訂單需要訂單資訊、商品明細、買家實付金額三種截圖，目前缺少：\n\n" +
                string.Join("\n", missingParts.Select(p => "・" + p)) +
                (unknown > 0 ? $"\n\n另有 {unknown} 張無法判斷種類，請確認截圖範圍（可按「截圖說明」）。" : string.Empty),
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
            MessageBox.Show(this, "無法組成訂單：\n\n" + string.Join("\n", result.Problems.Select(p => "・" + p)), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Result = result;
        DialogResult = DialogResult.OK;
    }
}
