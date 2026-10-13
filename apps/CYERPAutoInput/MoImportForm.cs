namespace CYERPAutoInput;

/// <summary>
/// MO店+ import, step one (user, 2026-10-13): the export the invoices are made from (no tracking
/// numbers yet) and the export downloaded after 批次出貨 (tracking numbers, but also every
/// earlier order). Files are told apart by their content, never their name or the box they
/// were dropped on, because either may be picked by mistake; a PDF is refused with a word on
/// what is wanted. 下一步 fills the batch's tracking numbers and hands over to the preview.
/// </summary>
internal sealed class MoImportForm : Form
{
    private sealed class Slot
    {
        public string? Path;
        public MoImportResult? Parsed;
        public Dictionary<string, HashSet<string>>? Tracking;
        public readonly Label File = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        public readonly Label Status = new() { AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
    }

    private readonly Func<string, List<string[]>> _read;
    private readonly Slot _batch = new();
    private readonly Slot _shipped = new();
    private readonly Label _message = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly CyPrimaryButton _next = new() { Text = "下一步", Size = new Size(120, 34), Enabled = false };

    /// <summary>The batch with its tracking numbers filled, after 下一步.</summary>
    public List<MoOrder> Orders { get; private set; } = [];
    public IReadOnlyList<string> Errors { get; private set; } = [];
    public List<string> Notes { get; private set; } = [];
    public string FileName => System.IO.Path.GetFileName(_batch.Path ?? string.Empty);

    /// <param name="read">Reads an export's first sheet (the password is the caller's).</param>
    public MoImportForm(Func<string, List<string[]>> read)
    {
        _read = read;
        Text = "MO店+訂單匯入";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowIcon = false; // secondary dialogs do not repeat the app icon (CY Desktop Visual Guide §11.1)
        Size = new Size(820, 520);
        MinimumSize = new Size(640, 460);
        Font = new Font("Microsoft JhengHei UI", 9.5F);
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(root);

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "放入 MO店+ 後台下載的兩個訂單匯出檔（.xls），可以按「選擇檔案」或直接拖曳到這個視窗。\n放錯格也沒關係，程式會看內容自動分好；不需要寄件單 PDF。",
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        var help = new CyButton { Text = "說明", Size = new Size(92, 34), Anchor = AnchorStyles.Right, Margin = new Padding(8, 7, 0, 0) };
        help.Click += (_, _) => { using var form = new MoImportHelpForm(); form.ShowDialog(this); };
        top.Controls.Add(help, 1, 0);
        root.Controls.Add(top, 0, 0);

        root.Controls.Add(Box(_batch, "① 出貨前的訂單匯出檔（必要）",
            "還沒產生運送資訊時下載、拿去開發票的那一份。這一份裡的訂單就是這次要輸入 ERP 的訂單。"), 0, 1);
        root.Controls.Add(Box(_shipped, "② 出貨後的訂單匯出檔（帶入物流單號）",
            "批次出貨之後再下載一次。裡面有以前的訂單也沒關係，只會用來查 ① 的訂單的物流單號（新竹、全家、7-11 都適用）。"), 0, 2);

        _message.ForeColor = CyVisualTheme.Danger;
        root.Controls.Add(_message, 0, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(4, 8, 4, 0) };
        _next.Click += (_, _) => Next();
        var cancel = new CyButton { Text = "取消", Size = new Size(92, 34), DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(_next);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 4);
        CancelButton = cancel;
        AcceptButton = _next;

        ShowSlot(_batch);
        ShowSlot(_shipped);
        CyVisualTheme.Apply(this);
    }

    private Control Box(Slot slot, string title, string text)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(10, 6, 10, 6),
            Margin = new Padding(0, 4, 0, 4),
            AllowDrop = true
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.DragEnter += OnDragEnter;
        panel.DragDrop += OnDragDrop;
        var lines = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AllowDrop = true };
        lines.DragEnter += OnDragEnter;
        lines.DragDrop += OnDragDrop;
        lines.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold) });
        lines.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = CyVisualTheme.TextSecondary, Margin = new Padding(0, 2, 0, 0) });
        lines.Controls.Add(slot.File);
        lines.Controls.Add(slot.Status);
        panel.Controls.Add(lines, 0, 0);
        var pick = new CyButton { Text = "選擇檔案", Size = new Size(110, 34), Anchor = AnchorStyles.Right | AnchorStyles.Top, Margin = new Padding(8, 4, 0, 0) };
        pick.Click += (_, _) => Pick();
        panel.Controls.Add(pick, 1, 0);
        return panel;
    }

    private void Pick()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "選擇 MO店+ 訂單匯出檔",
            Filter = "MO店+ 訂單匯出檔 (*.xls;*.xlsx)|*.xls;*.xlsx|所有檔案 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) Add(dialog.FileNames);
    }

    private static void OnDragEnter(object? sender, DragEventArgs e) =>
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) Add(files);
    }

    private void Add(IEnumerable<string> paths)
    {
        var messages = new List<string>();
        foreach (var path in paths) messages.AddRange(Load(path));
        _message.Text = string.Join("  ", messages);
        ShowSlot(_batch);
        ShowSlot(_shipped);
    }

    private IEnumerable<string> Load(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".pdf")
            return [$"「{name}」是 PDF，不需要寄件單；請放出貨後再下載一次的訂單匯出檔（.xls）。"];
        if (extension is not ".xls" and not ".xlsx")
            return [$"「{name}」不是 MO店+ 訂單匯出檔（.xls）。"];

        List<string[]> rows;
        try
        {
            rows = _read(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            return [$"無法讀取「{name}」：{ex.Message}"];
        }
        var tracking = MoOrderImport.ReadTracking(rows);
        if (tracking is null) return [$"「{name}」不是 MO店+ 訂單匯出檔（找不到訂單編號、物流單號欄）。"];

        var slot = MoOrderImport.IsShippedExport(tracking) ? _shipped : _batch;
        var replaced = slot.Path is not null && !string.Equals(slot.Path, path, StringComparison.OrdinalIgnoreCase);
        slot.Path = path;
        slot.Tracking = tracking;
        slot.Parsed = slot == _batch ? MoOrderImport.Parse(rows) : null;
        return replaced ? [$"已用「{name}」取代原本的{(slot == _batch ? "①" : "②")}。"] : [];
    }

    private void ShowSlot(Slot slot)
    {
        slot.File.Text = slot.Path is null ? "（尚未放入）" : $"檔案：{System.IO.Path.GetFileName(slot.Path)}";
        slot.Status.ForeColor = CyVisualTheme.TextSecondary;
        if (slot.Path is null)
            slot.Status.Text = slot == _batch && _shipped.Path is not null
                ? "放入的檔案已經有物流單號，所以放在 ②；① 請放出貨前下載、拿去開發票的那一份。"
                : string.Empty;
        else if (slot == _batch)
        {
            var parsed = _batch.Parsed!;
            slot.Status.Text = $"{_batch.Tracking!.Count} 張訂單，可匯入 {parsed.Orders.Count} 張" +
                (parsed.Errors.Count > 0 ? $"，{parsed.Errors.Count} 筆有問題（下一步會列出）" : string.Empty);
            if (parsed.Orders.Count == 0) slot.Status.ForeColor = CyVisualTheme.Danger;
        }
        else
        {
            var shipped = _shipped.Tracking!;
            var text = $"{shipped.Count} 張訂單，{shipped.Values.Count(v => v.Count > 0)} 張有物流單號";
            if (_batch.Parsed is { } batch)
            {
                var found = batch.Orders.Count(o => shipped.TryGetValue(o.OrderSn, out var n) && n.Count == 1);
                text += $"；① 的 {batch.Orders.Count} 張訂單找到 {found} 張的物流單號";
                if (found < batch.Orders.Count) slot.Status.ForeColor = CyVisualTheme.Warning;
            }
            slot.Status.Text = text;
        }
        _next.Enabled = _batch.Parsed is not null;
    }

    private void Next()
    {
        if (_batch.Parsed is not { } batch) return;
        if (_shipped.Tracking is not { } shipped)
        {
            if (MessageBox.Show(this, "還沒有放入 ② 出貨後的訂單匯出檔，物流單號會是空白（可在下一步自己填）。\n\n要繼續嗎？",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            Orders = batch.Orders.ToList();
            Notes = [];
        }
        else
        {
            if (batch.Orders.Count > 0 && !batch.Orders.Any(o => shipped.ContainsKey(o.OrderSn)))
            {
                MessageBox.Show(this, "② 裡找不到任何一張 ① 的訂單，請確認 ② 是這批訂單出貨後下載的匯出檔。",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            (Orders, Notes) = MoOrderImport.FillTracking(batch.Orders, shipped);
        }
        Errors = batch.Errors;
        DialogResult = DialogResult.OK;
    }
}
