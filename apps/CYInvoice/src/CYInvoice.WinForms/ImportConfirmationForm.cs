using CYInvoice.Core;
using CYInvoice.Core.Imports.Coupang;
using CYInvoice.Core.Imports.Digiwin;
using CYInvoice.Core.Imports.Mo;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class ImportConfirmationForm : Form
{
    private sealed class Entry
    {
        public required string OrderId { get; init; }
        public required string BuyerBan { get; set; }
        public required List<InvoiceItem> Items { get; init; }
        public required long TotalAmount { get; init; }
        public required Action<string> ApplyBuyerName { get; init; }
        public required Func<NameLookup, CancellationToken, Task<IssueResult>> IssueAsync { get; init; }
        public Action<string, string>? ApplyBuyerData { get; init; }
        public string BuyerName { get; set; } = string.Empty;
        public string OriginalBuyerBan { get; init; } = string.Empty;
        public string OriginalBuyerName { get; init; } = string.Empty;
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
    private readonly TextBox buyerBan = UiControls.TextBox(8);
    private readonly BuyerNameField buyerNameField = new(200);
    private TextBox buyerName => buyerNameField;
    private readonly Button applyName = UiControls.StandardButton("套用名稱");
    private readonly Button retryLookup = UiControls.StandardButton("重新查詢統編");
    private readonly Label buyerHint = UiControls.Label("");
    private readonly Label originalBuyerReference = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(118, 118, 118),
        AutoEllipsis = true,
        Margin = new Padding(3, 0, 3, 0),
    };
    private readonly ToolTip originalReferenceToolTip = new();
    private readonly TableLayoutPanel summary = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
    private readonly Label summaryLead = new();
    private readonly Label summarySelected = new();
    private readonly Label summaryTail = new();
    private readonly Label progressText = UiControls.Label("");
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 40 };
    private readonly Button cancel = UiControls.StandardButton("取消匯入");
    private readonly Button issue = UiControls.PrimaryIssueButton();
    private readonly Panel actions = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly bool digiwinMode;
    private bool refreshing;
    private bool issuing;
    private bool closing;
    private bool loadingBuyerEditor;
    private bool buyerEditorDirty;

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
        digiwinMode = source == InvoiceSources.Digiwin;
        Text = $"CYInvoice｜{source} 開立發票匯入確認｜{EnvironmentName()}";
        Font = new Font("Microsoft JhengHei UI", 10F);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1080, digiwinMode ? 640 : 610);
        ClientSize = new Size(1160, digiwinMode ? 640 : 610);
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

    public static ImportConfirmationForm ForDigiwin(
        LocalRepository repository,
        InvoiceService service,
        Action recordsChanged,
        string filePath) =>
        new(InvoiceSources.Digiwin, repository, service, recordsChanged, async cancellationToken =>
        {
            var order = await PlatformImportReader.ReadDigiwinExportAsync(filePath, cancellationToken);
            return
            [
                new Entry
                {
                    OrderId = order.OrderId,
                    BuyerBan = order.BuyerBan,
                    BuyerName = order.BuyerName,
                    OriginalBuyerBan = order.OriginalBuyerBan,
                    OriginalBuyerName = order.OriginalBuyerName,
                    Items = order.Items,
                    TotalAmount = order.TotalAmount,
                    ApplyBuyerName = value => order.BuyerName = value,
                    ApplyBuyerData = (ban, name) =>
                    {
                        order.BuyerBan = ban;
                        order.BuyerName = name;
                    },
                    IssueAsync = (lookup, token) => service.IssueDigiwinWithLookupAsync(order, lookup, token),
                },
            ];
        });

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 14), RowCount = 6, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 306));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, digiwinMode ? 78 : 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

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
        issue.Text = IssueButtonCaption();
        UiControls.ApplyIssueButtonTheme(issue,
            repository.Settings.LoadOrCreate().Environment == Environments.Production);
        var editor = digiwinMode ? BuildDigiwinEditor() : BuildStandardEditor();

        ConfigureSummary();
        var progressArea = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        progressArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        progressArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        progressArea.Controls.Add(progressText, 0, 0);
        progressArea.Controls.Add(progress, 0, 1);

        actions.Controls.Add(cancel);
        actions.Controls.Add(issue);
        actions.Resize += (_, _) => PositionActions();

        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(grid, 0, 1);
        root.Controls.Add(editor, 0, 2);
        root.Controls.Add(summary, 0, 3);
        root.Controls.Add(progressArea, 0, 4);
        root.Controls.Add(actions, 0, 5);
        Controls.Add(root);
        originalBuyerReference.Resize += (_, _) => UpdateOriginalReferenceTooltip();
        PositionActions();
    }

    private Control BuildStandardEditor()
    {
        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6 };
        buyerNameField.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        applyName.Anchor = AnchorStyles.None;
        retryLookup.Anchor = AnchorStyles.None;
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
        editor.Controls.Add(UiControls.Label("買方名稱"), 0, 0);
        editor.Controls.Add(buyerNameField, 1, 0);
        editor.Controls.Add(applyName, 2, 0);
        editor.Controls.Add(retryLookup, 3, 0);
        editor.Controls.Add(buyerHint, 4, 0);
        return editor;
    }

    private Control BuildDigiwinEditor()
    {
        applyName.Text = "套用資料";
        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2, Margin = Padding.Empty };
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buyerBan.BorderStyle = BorderStyle.FixedSingle;
        buyerNameField.BorderStyle = BorderStyle.FixedSingle;
        buyerNameField.Margin = buyerBan.Margin;
        applyName.Anchor = AnchorStyles.None;
        editor.Controls.Add(UiControls.Label("統一編號"), 0, 0);
        editor.Controls.Add(buyerBan, 1, 0);
        editor.Controls.Add(UiControls.Label("買方名稱"), 2, 0);
        editor.Controls.Add(buyerNameField, 3, 0);
        editor.Controls.Add(applyName, 4, 0);
        editor.Controls.Add(buyerHint, 5, 0);
        editor.Controls.Add(originalBuyerReference, 0, 1);
        editor.SetColumnSpan(originalBuyerReference, 6);
        retryLookup.Visible = false;
        return editor;
    }

    private void ConfigureSummary()
    {
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        summary.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        ConfigureSummaryLabel(summaryLead, 12F, SystemColors.ControlText);
        ConfigureSummaryLabel(summarySelected, 13F, Color.FromArgb(196, 0, 0));
        ConfigureSummaryLabel(summaryTail, 12F, SystemColors.ControlText);
        summary.Controls.Add(summaryLead, 0, 0);
        summary.Controls.Add(summarySelected, 1, 0);
        summary.Controls.Add(summaryTail, 2, 0);
    }

    private void ConfigureSummaryLabel(Label label, float size, Color color)
    {
        label.AutoSize = true;
        label.Anchor = AnchorStyles.Left;
        label.Margin = Padding.Empty;
        label.Font = new Font(Font.FontFamily, size, FontStyle.Bold);
        label.ForeColor = color;
        label.TextAlign = ContentAlignment.MiddleLeft;
    }

    private void PositionActions()
    {
        const int gap = 14;
        var contentWidth = cancel.Width + gap + issue.Width;
        var left = Math.Max(0, (actions.ClientSize.Width - contentWidth) / 2);
        cancel.SetBounds(left, Math.Max(0, (actions.ClientSize.Height - cancel.Height) / 2), cancel.Width, cancel.Height);
        issue.SetBounds(cancel.Right + gap, Math.Max(0, (actions.ClientSize.Height - issue.Height) / 2), issue.Width, issue.Height);
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
        applyName.Click += async (_, _) =>
        {
            if (digiwinMode) await ApplyDigiwinBuyerDataAsync(true);
            else ApplyBuyerName(true);
        };
        retryLookup.Click += async (_, _) => await RetryLookupAsync();
        if (digiwinMode)
        {
            buyerNameField.BindLookup(
                buyerBan,
                service,
                () => !loadingBuyerEditor && !closing && !issuing,
                ResolveDigiwinBuyerName,
                lifetime.Token);
            buyerNameField.BuyerBanChanged += DigiwinBuyerBanChanged;
            buyerNameField.LookupStarted += _ =>
            {
                buyerHint.Text = "正在自動查詢買受人名稱…";
                UpdateIssueEnabled();
            };
            buyerNameField.LookupCompleted += DigiwinBuyerLookupCompleted;
            buyerNameField.LookupFailed += DigiwinBuyerLookupFailed;
            buyerName.TextChanged += (_, _) => BuyerEditorChanged(clearApiName: false);
        }
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

    private string ResolveDigiwinBuyerName(string ban, NameLookup lookup)
    {
        var resolved = lookup.Name.Trim();
        var entry = ActiveEntry();
        if (resolved.Length == 0 && entry is not null && string.Equals(entry.OriginalBuyerBan.Trim(), ban, StringComparison.Ordinal))
            resolved = entry.OriginalBuyerName.Trim();
        return resolved;
    }

    private void DigiwinBuyerBanChanged(string ban)
    {
        if (!digiwinMode || loadingBuyerEditor) return;
        buyerEditorDirty = true;
        buyerHint.Text = ban.Length == 0
            ? "買方資料已修改，請先套用資料"
            : ban.Length == 8 && ban.All(char.IsAsciiDigit)
                ? "正在自動查詢買受人名稱…"
                : "輸入完整 8 碼統編後會自動查詢買受人名稱";
        UpdateIssueEnabled();
    }

    private void DigiwinBuyerLookupCompleted(string ban, NameLookup lookup)
    {
        if (!digiwinMode || loadingBuyerEditor || !string.Equals(buyerBan.Text.Trim(), ban, StringComparison.Ordinal)) return;
        buyerEditorDirty = true;
        buyerHint.Text = lookup.Local
            ? "已找到本機記憶名稱，請套用資料"
            : lookup.ApiName.Trim().Length != 0
                ? "已查詢買受人名稱，請套用資料"
                : "光貿查無名稱，請輸入買方名稱後套用資料";
        UpdateIssueEnabled();
    }

    private void DigiwinBuyerLookupFailed(string ban, Exception error)
    {
        if (!digiwinMode || loadingBuyerEditor || !string.Equals(buyerBan.Text.Trim(), ban, StringComparison.Ordinal)) return;
        buyerHint.Text = "統編自動查詢失敗，請確認後再試";
        UpdateIssueEnabled();
        MessageBox.Show(this, error.Message, "統編查詢失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void BuyerEditorChanged(bool clearApiName)
    {
        if (!digiwinMode || loadingBuyerEditor || buyerNameField.IsApplyingLookup) return;
        buyerEditorDirty = true;
        if (clearApiName) buyerNameField.ClearLookupState();
        buyerHint.Text = "買方資料已修改，請先套用資料";
        UpdateIssueEnabled();
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
                if (digiwinMode)
                {
                    entry.BuyerBan = string.Empty;
                    entry.BuyerName = string.Empty;
                    entry.Lookup = new NameLookup();
                    entry.LookupError = null;
                    entry.ApplyBuyerData?.Invoke(string.Empty, string.Empty);
                    entry.Status = "可開立";
                }
                else
                {
                    entry.BuyerName = entry.BuyerName.Trim().Length == 0 ? "消費者" : entry.BuyerName.Trim();
                    entry.ApplyBuyerName(entry.BuyerName);
                }
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
            if (!digiwinMode)
            {
                entry.BuyerName = string.Empty;
                entry.ApplyBuyerName(string.Empty);
            }
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
        else if (digiwinMode)
        {
            var fallback = entry.BuyerName.Trim().Length == 0 ? entry.OriginalBuyerName.Trim() : entry.BuyerName.Trim();
            entry.BuyerName = fallback;
            entry.ApplyBuyerData?.Invoke(entry.BuyerBan.Trim(), fallback);
            entry.Status = fallback.Length == 0 ? "請輸入買方名稱" : "光貿查無名稱，請確認買方名稱";
            entry.Selected = fallback.Length != 0;
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
        if (digiwinMode) entry.ApplyBuyerData?.Invoke(entry.BuyerBan.Trim(), entry.BuyerName);
        else entry.ApplyBuyerName(entry.BuyerName);
        entry.Status = status;
        entry.Selected = true;
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

    private async Task ApplyDigiwinBuyerDataAsync(bool showMessage)
    {
        var entry = ActiveEntry();
        if (!digiwinMode || entry is null || entry.Finished) return;
        var ban = buyerBan.Text.Trim();
        var name = buyerName.Text.Trim();
        if (ban.Length == 0 && name.Length == 0)
        {
            entry.BuyerBan = string.Empty;
            entry.BuyerName = string.Empty;
            entry.Lookup = new NameLookup();
            entry.LookupError = null;
            entry.Status = "可開立";
            entry.Selected = true;
            entry.ApplyBuyerData?.Invoke(string.Empty, string.Empty);
            buyerEditorDirty = false;
            RefreshGrid();
            return;
        }
        if (ban.Length == 0 || name.Length == 0)
        {
            if (showMessage) MessageBox.Show(this, "統一編號與買方名稱必須同時填寫或同時留空。", "買方資料不完整", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            UpdateIssueEnabled();
            return;
        }
        if (ban.Length != 8 || !ban.All(char.IsAsciiDigit))
        {
            if (showMessage) MessageBox.Show(this, "公司統編必須為 8 碼數字。", "統一編號格式錯誤", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            UpdateIssueEnabled();
            return;
        }

        SetBusy(true, $"正在確認公司統編 {ban}…");
        NameLookup lookup;
        Exception? error = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            lookup = await service.LookupBuyerNameAsync(ban, timeout.Token);
        }
        catch (Exception caught)
        {
            lookup = new NameLookup();
            error = caught;
        }
        if (closing) return;

        entry.BuyerBan = ban;
        entry.BuyerName = name;
        entry.Lookup = lookup;
        entry.LookupError = error;
        entry.ApplyBuyerData?.Invoke(ban, name);
        buyerNameField.SetLookupState(lookup);
        if (error is not null)
        {
            entry.Selected = false;
            entry.Status = "統編查詢異常：" + ShortError(error);
        }
        else
        {
            entry.Selected = true;
            entry.Status = lookup.Local
                ? string.Equals(name, lookup.Name.Trim(), StringComparison.Ordinal) ? "本機記憶名稱" : "人工名稱將覆寫本機記憶"
                : lookup.ApiName.Trim().Length == 0
                    ? "人工名稱待成功後記憶"
                    : string.Equals(name, lookup.ApiName.Trim(), StringComparison.Ordinal)
                        ? "光貿查詢完成"
                        : "名稱與光貿查詢不同";
        }
        buyerEditorDirty = false;
        RefreshGrid();
        SetBusy(false, string.Empty);
        if (error is not null && showMessage)
            MessageBox.Show(this, error.Message, "統編查詢失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private async Task IssueSelectionAsync()
    {
        if (digiwinMode && buyerEditorDirty)
        {
            MessageBox.Show(this, "買方資料已修改，請先套用資料。", "資料尚未套用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!digiwinMode) ApplyBuyerName(false);
        var selected = entries.Where(entry => entry.Selected && !entry.Finished).ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "目前沒有勾選要開立的發票。", "尚未選擇", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var invalid = selected.FirstOrDefault(EntryInvalid);
        if (invalid is not null)
        {
            SelectEntry(invalid);
            MessageBox.Show(this,
                invalid.LookupError is not null ? "仍有統編查詢異常，請重新查詢或取消勾選該張發票。" : "公司發票的統一編號與買方名稱尚未完成確認。",
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
                entry.Status = "開立結果待確認，禁止重送";
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
                entry.Status = StatusForIssueError(error);
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

    private bool EntryInvalid(Entry entry)
    {
        if (entry.LookupError is not null) return true;
        if (digiwinMode)
        {
            var ban = entry.BuyerBan.Trim();
            var name = entry.BuyerName.Trim();
            if ((ban.Length == 0) != (name.Length == 0)) return true;
            if (ban.Length == 0) return false;
            return ban.Length != 8 || !ban.All(char.IsAsciiDigit) || (!entry.Lookup.Local && !entry.Lookup.LookupSucceeded);
        }
        return entry.Company && entry.BuyerName.Trim().Length == 0;
    }

    private void RefreshGrid()
    {
        var active = ActiveEntry();
        refreshing = true;
        grid.Rows.Clear();
        foreach (var entry in entries)
        {
            var totals = Totals(entry);
            var rowIndex = grid.Rows.Add(entry.Selected && !entry.Finished && entry.LookupError is null, entry.OrderId,
                entry.Company ? entry.BuyerBan.Trim() : string.Empty, entry.BuyerName, "應稅",
                MoneyFormatter.Integer(totals.SalesAmount), MoneyFormatter.Integer(totals.TaxAmount),
                MoneyFormatter.Integer(totals.TotalAmount), entry.Items.Count, entry.Status);
            var nameCell = grid.Rows[rowIndex].Cells[3];
            var name = entry.BuyerName.Trim();
            if (name.Length != 0 && TextRenderer.MeasureText(name, grid.Font).Width > grid.Columns[3].Width - 12)
                nameCell.ToolTipText = name;
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
        UpdateIssueEnabled();
    }

    private void RefreshSummary()
    {
        var selected = entries.Where(entry => entry.Selected && !entry.Finished).ToArray();
        summaryLead.Text = $"訂單共 {entries.Count} 張　｜　已選擇 ";
        summarySelected.Text = selected.Length.ToString();
        summaryTail.Text = $" 張　｜　選擇總額 {MoneyFormatter.Integer(selected.Sum(entry => entry.TotalAmount))}";
        issue.Text = IssueButtonCaption();
    }

    private void LoadBuyerEditor()
    {
        var entry = ActiveEntry();
        loadingBuyerEditor = true;
        try
        {
            if (digiwinMode) buyerBan.Text = entry?.BuyerBan ?? string.Empty;
            buyerName.Text = entry?.BuyerName ?? string.Empty;
            buyerNameField.SetLookupState(entry?.Lookup ?? new NameLookup());
            buyerEditorDirty = false;
        }
        finally { loadingBuyerEditor = false; }
        UpdateEditorAvailability();
        UpdateIssueEnabled();
    }

    private void UpdateEditorAvailability()
    {
        var entry = ActiveEntry();
        if (digiwinMode)
        {
            var editable = entry is { Finished: false } && !issuing && grid.Enabled;
            buyerBan.Enabled = editable;
            buyerNameField.SetLocked(!editable);
            applyName.Enabled = editable;
            retryLookup.Visible = false;
            if (!buyerEditorDirty)
                buyerHint.Text = entry?.LookupError is null ? string.Empty : "統編查詢失敗；修正後請再套用資料";
            var showReference = entry is not null && entry.OriginalBuyerBan.Trim().Length != 0;
            originalBuyerReference.Visible = showReference;
            originalBuyerReference.Text = showReference
                ? $"鼎新原始資料：統編 {entry!.OriginalBuyerBan.Trim()}｜買受人 {entry.OriginalBuyerName.Trim()}"
                : string.Empty;
            UpdateOriginalReferenceTooltip();
        }
        else
        {
            var editable = entry is not null && CanEditName(entry) && !issuing && grid.Enabled;
            buyerNameField.SetLocked(!editable);
            applyName.Enabled = editable;
            retryLookup.Enabled = entry is { Company: true, Finished: false } && !issuing && grid.Enabled;
            retryLookup.Visible = true;
            buyerHint.Text = entry?.LookupError is null ? string.Empty : "統編查詢失敗，可重新查詢";
        }
    }

    private void UpdateOriginalReferenceTooltip()
    {
        var text = originalBuyerReference.Text;
        if (!originalBuyerReference.Visible || text.Length == 0 || originalBuyerReference.ClientSize.Width <= 0)
        {
            originalReferenceToolTip.SetToolTip(originalBuyerReference, null);
            return;
        }
        originalReferenceToolTip.SetToolTip(
            originalBuyerReference,
            TextRenderer.MeasureText(text, originalBuyerReference.Font).Width > originalBuyerReference.ClientSize.Width - 6 ? text : null);
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
        progress.Visible = busy;
        progressText.Text = message;
        if (!busy) progress.Style = ProgressBarStyle.Blocks;
        else { progress.Style = ProgressBarStyle.Marquee; progress.MarqueeAnimationSpeed = 40; }
        UpdateEditorAvailability();
        UpdateIssueEnabled(busy);
    }

    private void UpdateIssueEnabled(bool busy = false)
    {
        if (busy || issuing || entries.Count == 0)
        {
            issue.Enabled = false;
            return;
        }
        if (digiwinMode && buyerEditorDirty)
        {
            issue.Enabled = false;
            return;
        }
        issue.Enabled = !entries.Where(entry => entry.Selected && !entry.Finished).Any(EntryInvalid);
    }

    private void FormatStatus(DataGridViewCellFormattingEventArgs eventArgs)
    {
        if (eventArgs.RowIndex < 0 || eventArgs.RowIndex >= entries.Count) return;
        var status = entries[eventArgs.RowIndex].Status;
        if (status.Contains("成功", StringComparison.Ordinal)) eventArgs.CellStyle.ForeColor = Color.FromArgb(0, 135, 45);
        else if (status.Contains("異常", StringComparison.Ordinal) || status.Contains("失敗", StringComparison.Ordinal) ||
                 status.Contains("未開立", StringComparison.Ordinal) || status.Contains("禁止重送", StringComparison.Ordinal))
            eventArgs.CellStyle.ForeColor = Color.FromArgb(190, 0, 0);
        else if (status.Contains("請輸入", StringComparison.Ordinal) || status.Contains("待確認", StringComparison.Ordinal) || status.Contains("不同", StringComparison.Ordinal))
            eventArgs.CellStyle.ForeColor = Color.FromArgb(190, 100, 0);
    }

    private bool IsProductionEnvironment() =>
        repository.Settings.LoadOrCreate().Environment == Environments.Production;

    private string IssueButtonCaption() => IssueButtonCaption(IsProductionEnvironment());

    private static string IssueButtonCaption(bool production) => production ? "確認開立" : "確認測試開立";

    internal static void VerifySmokeLayout(LocalRepository repository, InvoiceService service)
    {
        using var form = new ImportConfirmationForm(
            "測試",
            repository,
            service,
            () => { },
            _ => Task.FromResult<IReadOnlyList<Entry>>([]));
        form.CreateControl();
        form.PerformLayout();
        form.RefreshSummary();
        form.PositionActions();

        if (IssueButtonCaption(false) != "確認測試開立" || IssueButtonCaption(true) != "確認開立" ||
            form.issue.Text.Contains('張'))
            throw new InvalidOperationException("匯入確認按鈕的測試／正式環境文字不正確，或仍含張數");
        var actionCenter = (form.cancel.Left + form.issue.Right) / 2;
        if (Math.Abs(actionCenter - form.actions.ClientSize.Width / 2) > 1 ||
            form.cancel.Top < 0 || form.issue.Top < 0 ||
            form.cancel.Bottom > form.actions.ClientSize.Height || form.issue.Bottom > form.actions.ClientSize.Height)
            throw new InvalidOperationException("匯入確認按鈕未置中，或按鈕上下緣遭裁切");
        if (Math.Abs(form.summaryLead.Font.SizeInPoints - 12F) > 0.1F ||
            Math.Abs(form.summaryTail.Font.SizeInPoints - 12F) > 0.1F ||
            Math.Abs(form.summarySelected.Font.SizeInPoints - 13F) > 0.1F ||
            form.summarySelected.ForeColor != Color.FromArgb(196, 0, 0))
            throw new InvalidOperationException("匯入確認摘要字級或已選張數顏色不正確");

        using var digiwinForm = new ImportConfirmationForm(
            InvoiceSources.Digiwin,
            repository,
            service,
            () => { },
            _ => Task.FromResult<IReadOnlyList<Entry>>([]));
        digiwinForm.CreateControl();
        digiwinForm.PerformLayout();
        if (digiwinForm.buyerBan.BorderStyle != digiwinForm.buyerNameField.BorderStyle ||
            Math.Abs(digiwinForm.buyerBan.Height - digiwinForm.buyerNameField.Height) > 1 ||
            digiwinForm.buyerBan.Margin != digiwinForm.buyerNameField.Margin)
            throw new InvalidOperationException("鼎新匯入的統編與買方名稱欄位外觀不一致");
    }

    private string EnvironmentName() => IsProductionEnvironment() ? "正式公司環境" : "光貿測試環境";

    private static string StatusForIssueError(Exception error)
    {
        var message = ShortError(error);
        if (message.Contains("不可重送", StringComparison.Ordinal) ||
            message.Contains("禁止重送", StringComparison.Ordinal) ||
            message.Contains("重複開立", StringComparison.Ordinal))
            return "已有開立紀錄，禁止重送";
        return "未開立：" + message;
    }

    private static string ShortError(Exception error) => error.Message.Length <= 80 ? error.Message : error.Message[..77] + "…";
    private static DataGridViewTextBoxColumn Column(string title, int width, bool right = false) => new()
    {
        HeaderText = title, Width = width, MinimumWidth = 50, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable,
        DefaultCellStyle = { Alignment = right ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft },
    };
}
