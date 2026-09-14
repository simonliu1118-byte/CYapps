using CYInvoice.Core;
using CYInvoice.Core.Imports.Coupang;
using CYInvoice.Core.Imports.Mo;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class ImportConfirmationForm : Form
{
    private sealed class Entry
    {
        public required string OrderId { get; init; }
        public required string BuyerBan { get; init; }
        public required List<InvoiceItem> Items { get; init; }
        public required long TotalAmount { get; init; }
        public required Action<string> ApplyBuyerName { get; init; }
        public required Func<NameLookup, CancellationToken, Task<IssueResult>> IssueAsync { get; init; }
        public string BuyerName { get; set; } = string.Empty;
        public NameLookup Lookup { get; set; } = new();
        public Exception? LookupError { get; set; }
        public bool Selected { get; set; } = true;
        public bool Finished { get; set; }
        public string Status { get; set; } = "可開立";
        public bool Company => BuyerBan.Trim().Length != 0 && BuyerBan.Trim() != "0000000000";
    }

    private readonly string source;
    private readonly LocalRepository repository;
    private readonly InvoiceService service;
    private readonly Action recordsChanged;
    private readonly Func<CancellationToken, Task<IReadOnlyList<Entry>>> loadOrders;
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<Entry> entries = [];
    private readonly DataGridView grid = UiControls.Grid();
    private readonly TextBox buyerName = UiControls.TextBox(200);
    private readonly Button applyName = new() { Text = "套用名稱", Width = 110, Height = 31 };
    private readonly Button retryLookup = new() { Text = "重新查詢統編", Width = 140, Height = 31 };
    private readonly Label buyerHint = UiControls.Label("");
    private readonly Label summary = UiControls.Label("");
    private readonly Label progressText = UiControls.Label("");
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 40 };
    private readonly Button cancel = new() { Text = "取消匯入", Width = 140, Height = 36 };
    private readonly Button issue = new() { Text = "確認開立", Width = 180, Height = 36 };
    private bool refreshing;
    private bool issuing;
    private bool closing;

    private ImportConfirmationForm(
        string source,
        LocalRepository repository,
        InvoiceService service,
        Action recordsChanged,
        Func<CancellationToken, Task<IReadOnlyList<Entry>>> loadOrders)
    {
        this.source = source;
        this.repository = repository;
        this.service = service;
        this.recordsChanged = recordsChanged;
        this.loadOrders = loadOrders;
        Text = $"CYInvoice｜{source} 開立發票匯入確認｜{EnvironmentName()}";
        Font = new Font("Microsoft JhengHei UI", 10F);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1080, 610);
        ClientSize = new Size(1160, 610);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        BuildLayout();
        ConfigureEvents();
        Shown += async (_, _) => await LoadAsync();
    }

    public static ImportConfirmationForm ForMo(
        LocalRepository repository,
        InvoiceService service,
        Action recordsChanged,
        string filePath,
        string password) =>
        new(InvoiceSources.Mo, repository, service, recordsChanged, async cancellationToken =>
        {
            var orders = await PlatformImportReader.ReadMoOrderExportAsync(filePath, password, cancellationToken);
            return orders.Select(order => new Entry
            {
                OrderId = order.OrderId,
                BuyerBan = order.BuyerBan,
                BuyerName = order.BuyerName,
                Items = order.Items,
                TotalAmount = order.TotalAmount,
                ApplyBuyerName = value => order.BuyerName = value,
                IssueAsync = (lookup, token) => service.IssueMoWithLookupAsync(order, lookup, token),
            }).ToArray();
        });

    public static ImportConfirmationForm ForCoupang(
        LocalRepository repository,
        InvoiceService service,
        Action recordsChanged,
        string filePath) =>
        new(InvoiceSources.Coupang, repository, service, recordsChanged, async cancellationToken =>
        {
            var orders = await PlatformImportReader.ReadCoupangExportAsync(filePath, cancellationToken);
            return orders.Select(order => new Entry
            {
                OrderId = order.OrderId,
                BuyerBan = order.BuyerBan,
                BuyerName = order.BuyerName,
                Items = order.Items,
                TotalAmount = order.TotalAmount,
                ApplyBuyerName = value => order.BuyerName = value,
                IssueAsync = (lookup, token) => service.IssueCoupangWithLookupAsync(order, lookup, token),
            }).ToArray();
        });

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 14), RowCount = 6, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 306));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.Controls.Add(new Label
        {
            Text = $"{source} 開立發票匯入確認", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold),
        }, 0, 0);
        heading.Controls.Add(UiControls.Label("請先逐張核對；取消、未勾選或資料有問題的訂單都不會送出。"), 1, 0);

        ConfigureGrid();
        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6 };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
        editor.Controls.Add(UiControls.Label("買方名稱"), 0, 0);
        editor.Controls.Add(buyerName, 1, 0);
        editor.Controls.Add(applyName, 2, 0);
        editor.Controls.Add(retryLookup, 3, 0);
        editor.Controls.Add(buyerHint, 4, 0);

        summary.Font = new Font(Font, FontStyle.Bold);
        var progressArea = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        progressArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        progressArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        progressArea.Controls.Add(progressText, 0, 0);
        progressArea.Controls.Add(progress, 0, 1);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 7, 0, 0) };
        actions.Controls.Add(issue);
        actions.Controls.Add(cancel);

        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(grid, 0, 1);
        root.Controls.Add(editor, 0, 2);
        root.Controls.Add(summary, 0, 3);
        root.Controls.Add(progressArea, 0, 4);
        root.Controls.Add(actions, 0, 5);
        Controls.Add(root);
    }

    private void ConfigureGrid()
    {
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.EditMode = DataGridViewEditMode.EditOnEnter;
        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "開立", Width = 58, SortMode = DataGridViewColumnSortMode.NotSortable,
            FlatStyle = FlatStyle.Standard,
        });
        grid.Columns.Add(Column("訂單編號", 155));
        grid.Columns.Add(Column("統一編號", 98));
        grid.Columns.Add(Column("買方名稱", 170));
        grid.Columns.Add(Column("課稅別", 66));
        grid.Columns.Add(Column("應稅銷售額", 105, true));
        grid.Columns.Add(Column("營業稅額", 88, true));
        grid.Columns.Add(Column("發票總額", 100, true));
        grid.Columns.Add(Column("項目數", 66, true));
        grid.Columns.Add(Column("檢查狀態", 210));
        UiControls.ReserveVerticalScrollBar(grid, 9);
    }

    private void ConfigureEvents()
    {
        cancel.Click += (_, _) => Close();
        issue.Click += async (_, _) => await IssueSelectionAsync();
        applyName.Click += (_, _) => ApplyBuyerName(true);
        retryLookup.Click += async (_, _) => await RetryLookupAsync();
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell?.ColumnIndex == 0) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellValueChanged += (_, eventArgs) => SelectionChanged(eventArgs.RowIndex, eventArgs.ColumnIndex);
        grid.SelectionChanged += (_, _) => LoadBuyerEditor();
        grid.CellFormatting += (_, eventArgs) => FormatStatus(eventArgs);
        FormClosing += (_, eventArgs) =>
        {
            if (issuing)
            {
                eventArgs.Cancel = true;
                MessageBox.Show(this, "發票正在逐張處理，為避免狀態遺失，完成前不能關閉此視窗。", "正在開立", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            closing = true;
            lifetime.Cancel();
        };
    }

    private async Task LoadAsync()
    {
        SetBusy(true, $"正在唯讀解析 {source} 原始 Excel…");
        try
        {
            entries.AddRange(await loadOrders(lifetime.Token));
            if (closing) return;
            if (entries.Count == 0) throw new InvalidDataException("Excel 沒有可匯入的訂單");
            await PrepareBuyerNamesAsync();
            if (closing) return;
            RefreshGrid();
            SetBusy(false, string.Empty);
        }
        catch (OperationCanceledException) when (closing)
        {
        }
        catch (Exception error)
        {
            var actual = ExcelComRows.Unwrap(error);
            WriteImportError(actual);
            SetBusy(false, "匯入失敗：" + ShortError(actual));
            MessageBox.Show(this, actual.Message, $"{source} 匯入失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void WriteImportError(Exception error)
    {
        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "Logs");
            Directory.CreateDirectory(directory);
            var record = $"[{DateTimeOffset.Now:O}] {source} 匯入失敗{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(directory, "import-error.log"), record);
        }
        catch (Exception)
        {
            // 診斷紀錄失敗不可遮蔽原始匯入錯誤。
        }
    }

    private async Task PrepareBuyerNamesAsync()
    {
        var cache = new Dictionary<string, (NameLookup Lookup, Exception? Error)>(StringComparer.Ordinal);
        for (var index = 0; index < entries.Count; index++)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            var entry = entries[index];
            progressText.Text = $"正在查詢公司統編與準備逐張確認資料…（{index + 1}/{entries.Count}）";
            if (!entry.Company)
            {
                entry.BuyerName = entry.BuyerName.Trim().Length == 0 ? "消費者" : entry.BuyerName.Trim();
                entry.ApplyBuyerName(entry.BuyerName);
                continue;
            }
            var ban = entry.BuyerBan.Trim();
            if (!cache.TryGetValue(ban, out var result))
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    result = (await service.LookupBuyerNameAsync(ban, timeout.Token), null);
                }
                catch (Exception error)
                {
                    result = (new NameLookup(), error);
                }
                cache.Add(ban, result);
            }
            ApplyLookup(entry, result.Lookup, result.Error);
        }
    }

    private void ApplyLookup(Entry entry, NameLookup lookup, Exception? error)
    {
        entry.Lookup = lookup;
        entry.LookupError = error;
        if (error is not null)
        {
            entry.Selected = false;
            entry.BuyerName = string.Empty;
            entry.Status = "統編查詢異常：" + ShortError(error);
        }
        else if (lookup.Local)
        {
            SetResolvedName(entry, lookup.Name, "本機記憶名稱");
        }
        else if (lookup.ApiName.Trim().Length != 0)
        {
            SetResolvedName(entry, lookup.ApiName, "光貿查詢完成");
        }
        else
        {
            entry.BuyerName = string.Empty;
            entry.ApplyBuyerName(string.Empty);
            entry.Status = "請輸入買方名稱";
        }
    }

    private void SetResolvedName(Entry entry, string name, string status)
    {
        entry.BuyerName = name.Trim();
        entry.ApplyBuyerName(entry.BuyerName);
        entry.Status = status;
    }

    private async Task RetryLookupAsync()
    {
        var entry = ActiveEntry();
        if (entry is null || !entry.Company || entry.Finished) return;
        SetBusy(true, $"正在重新查詢 {entry.BuyerBan}…");
        NameLookup lookup;
        Exception? error = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            lookup = await service.LookupBuyerNameAsync(entry.BuyerBan, timeout.Token);
        }
        catch (Exception caught)
        {
            lookup = new NameLookup();
            error = caught;
        }
        if (closing) return;
        foreach (var other in entries.Where(value => value.Company && !value.Finished && value.BuyerBan.Trim() == entry.BuyerBan.Trim()))
            ApplyLookup(other, lookup, error);
        RefreshGrid();
        SetBusy(false, string.Empty);
    }

    private void ApplyBuyerName(bool showMessage)
    {
        var entry = ActiveEntry();
        if (entry is null || !CanEditName(entry)) return;
        var name = buyerName.Text.Trim();
        if (name.Length == 0)
        {
            if (showMessage) MessageBox.Show(this, "請輸入買方名稱。", "尚未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        foreach (var other in entries.Where(value =>
                     !value.Finished && value.LookupError is null && value.Lookup.LookupSucceeded &&
                     value.Lookup.ApiName.Trim().Length == 0 && value.BuyerBan.Trim() == entry.BuyerBan.Trim()))
        {
            SetResolvedName(other, name, "人工名稱待成功後記憶");
        }
        RefreshGrid();
        if (showMessage)
            MessageBox.Show(this, "已套用到本批次相同統編的訂單；只有成功開立後才會記憶。", "名稱已套用", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task IssueSelectionAsync()
    {
        ApplyBuyerName(false);
        var selected = entries.Where(entry => entry.Selected && !entry.Finished).ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "目前沒有勾選要開立的發票。", "尚未選擇", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var invalid = selected.FirstOrDefault(entry => entry.LookupError is not null || entry.Company && entry.BuyerName.Trim().Length == 0);
        if (invalid is not null)
        {
            SelectEntry(invalid);
            MessageBox.Show(this,
                invalid.LookupError is not null ? "仍有統編查詢異常，請重新查詢或取消勾選該張發票。" : "公司發票尚未輸入並套用買方名稱。",
                "資料尚未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var formal = repository.Settings.LoadOrCreate().Environment == Environments.Production;
        var total = selected.Sum(entry => entry.TotalAmount);
        var confirmation = MessageBox.Show(this,
            $"即將依勾選順序開立 {selected.Length} 張{(formal ? "正式" : "測試")}發票\n\n選擇總額：{MoneyFormatter.Integer(total)}\n\n每張會逐一送出；等待期間請勿關閉視窗或重複操作。",
            "確認批次開立", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.OK) return;

        issuing = true;
        SetBusy(true, "準備逐張開立…");
        var success = 0;
        var failed = 0;
        for (var index = 0; index < selected.Length; index++)
        {
            var entry = selected[index];
            entry.Status = $"開立中（{index + 1}/{selected.Length}）";
            progressText.Text = $"正在開立第 {index + 1}/{selected.Length} 張發票…";
            RefreshGrid();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(90));
                var result = await entry.IssueAsync(entry.Lookup, timeout.Token);
                entry.Selected = false;
                entry.Finished = result.Opened;
                entry.Status = result.Opened ? "成功：" + result.Record.InvoiceNumber : "未確認成功";
                success += result.Opened ? 1 : 0;
                failed += result.Opened ? 0 : 1;
            }
            catch (UnknownInvoiceResultException)
            {
                entry.Selected = false;
                entry.Finished = true;
                entry.Status = "結果不明（禁止重送）";
                failed++;
            }
            catch (LocalPersistenceException)
            {
                entry.Selected = false;
                entry.Finished = true;
                entry.Status = "已開立；本機保存失敗";
                failed++;
            }
            catch (Exception error)
            {
                entry.Selected = false;
                entry.Status = "已擋下：" + ShortError(error);
                failed++;
            }
            recordsChanged();
            RefreshGrid();
        }

        issuing = false;
        SetBusy(false, string.Empty);
        recordsChanged();
        if (failed == 0)
        {
            MessageBox.Show(this, $"{source} 批次開立完成\n\n成功：{success} 張\n未勾選的訂單未送出。", "開立完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
            return;
        }
        MessageBox.Show(this,
            $"批次處理完成。\n\n成功：{success} 張\n未成功：{failed} 張\n\n視窗會保留，請依每列狀態處理；結果不明不可重送。",
            "部分項目未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void RefreshGrid()
    {
        var active = ActiveEntry();
        refreshing = true;
        grid.Rows.Clear();
        foreach (var entry in entries)
        {
            var totals = Totals(entry);
            grid.Rows.Add(entry.Selected && !entry.Finished && entry.LookupError is null, entry.OrderId,
                entry.Company ? entry.BuyerBan.Trim() : string.Empty, entry.BuyerName, "應稅",
                MoneyFormatter.Integer(totals.SalesAmount), MoneyFormatter.Integer(totals.TaxAmount),
                MoneyFormatter.Integer(totals.TotalAmount), entry.Items.Count, entry.Status);
        }
        refreshing = false;
        if (active is not null) SelectEntry(active);
        else if (grid.Rows.Count != 0) grid.Rows[0].Selected = true;
        RefreshSummary();
        LoadBuyerEditor();
    }

    private static InvoiceTotals Totals(Entry entry)
    {
        if (!entry.Company) return new InvoiceTotals(entry.TotalAmount, 0, entry.TotalAmount);
        try { return InvoiceCalculator.CalculateTotals(entry.Items, companyBuyer: true, pricesExcludeTax: false); }
        catch { return new InvoiceTotals(0, 0, entry.TotalAmount); }
    }

    private void SelectionChanged(int rowIndex, int columnIndex)
    {
        if (refreshing || columnIndex != 0 || rowIndex < 0 || rowIndex >= entries.Count) return;
        var entry = entries[rowIndex];
        var selected = Convert.ToBoolean(grid.Rows[rowIndex].Cells[0].Value);
        entry.Selected = !entry.Finished && entry.LookupError is null && selected;
        if (selected && !entry.Selected) grid.Rows[rowIndex].Cells[0].Value = false;
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        var selected = entries.Where(entry => entry.Selected && !entry.Finished).ToArray();
        summary.Text = $"訂單共 {entries.Count} 張　｜　已選擇 {selected.Length} 張　｜　選擇總額 {MoneyFormatter.Integer(selected.Sum(entry => entry.TotalAmount))}";
        issue.Text = $"確認開立（{selected.Length} 張）";
    }

    private void LoadBuyerEditor()
    {
        var entry = ActiveEntry();
        buyerName.Text = entry?.BuyerName ?? string.Empty;
        var editable = entry is not null && CanEditName(entry) && !issuing;
        buyerName.Enabled = editable;
        applyName.Enabled = editable;
        retryLookup.Enabled = entry is { Company: true, Finished: false } && !issuing;
        buyerHint.Text = entry?.LookupError is null ? string.Empty : "統編查詢失敗，可重新查詢";
    }

    private static bool CanEditName(Entry entry) =>
        entry.Company && !entry.Finished && entry.LookupError is null && entry.Lookup.LookupSucceeded && entry.Lookup.ApiName.Trim().Length == 0;

    private Entry? ActiveEntry()
    {
        if (grid.CurrentRow is null || grid.CurrentRow.Index < 0 || grid.CurrentRow.Index >= entries.Count) return null;
        return entries[grid.CurrentRow.Index];
    }

    private void SelectEntry(Entry entry)
    {
        var index = entries.IndexOf(entry);
        if (index < 0 || index >= grid.Rows.Count) return;
        grid.ClearSelection();
        grid.Rows[index].Selected = true;
        grid.CurrentCell = grid.Rows[index].Cells[1];
    }

    private void SetBusy(bool busy, string message)
    {
        grid.Enabled = !busy;
        cancel.Enabled = !busy || !issuing;
        issue.Enabled = !busy && entries.Count != 0;
        progress.Visible = busy;
        progressText.Text = message;
        if (!busy) progress.Style = ProgressBarStyle.Blocks;
        else { progress.Style = ProgressBarStyle.Marquee; progress.MarqueeAnimationSpeed = 40; }
        LoadBuyerEditor();
    }

    private void FormatStatus(DataGridViewCellFormattingEventArgs eventArgs)
    {
        if (eventArgs.RowIndex < 0 || eventArgs.RowIndex >= entries.Count) return;
        var status = entries[eventArgs.RowIndex].Status;
        if (status.Contains("成功", StringComparison.Ordinal)) eventArgs.CellStyle.ForeColor = Color.FromArgb(0, 135, 45);
        else if (status.Contains("異常", StringComparison.Ordinal) || status.Contains("失敗", StringComparison.Ordinal) || status.Contains("擋下", StringComparison.Ordinal))
            eventArgs.CellStyle.ForeColor = Color.FromArgb(190, 0, 0);
        else if (status.Contains("請輸入", StringComparison.Ordinal) || status.Contains("結果不明", StringComparison.Ordinal))
            eventArgs.CellStyle.ForeColor = Color.FromArgb(190, 100, 0);
    }

    private string EnvironmentName() => repository.Settings.LoadOrCreate().Environment == Environments.Production ? "正式公司環境" : "光貿測試環境";
    private static string ShortError(Exception error) => error.Message.Length <= 80 ? error.Message : error.Message[..77] + "…";
    private static DataGridViewTextBoxColumn Column(string title, int width, bool right = false) => new()
    {
        HeaderText = title, Width = width, MinimumWidth = 50, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable,
        DefaultCellStyle = { Alignment = right ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft },
    };
}
