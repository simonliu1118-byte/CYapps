using System.Globalization;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class InvoiceOperationHistoryControl : UserControl
{
    private readonly ListView list = new HistoryListView
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        GridLines = true,
        Scrollable = true,
        BorderStyle = BorderStyle.FixedSingle,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
    };

    public InvoiceOperationHistoryControl()
    {
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        list.Columns.Add("類型", 48, HorizontalAlignment.Left);
        list.Columns.Add("日期", 82, HorizontalAlignment.Left);
        list.Columns.Add("狀態", 58, HorizontalAlignment.Center);
        list.Columns.Add("摘要", 92, HorizontalAlignment.Left);
        list.DoubleClick += (_, _) => OpenSelected();
        Controls.Add(list);
    }

    public void LoadRecord(InvoiceRecord record)
    {
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            if (record.InvoiceState == InvoiceStates.Voided && InvoiceOfficialMetadata.CancelDate(record) > 0)
            {
                var cancelText = InvoiceOfficialMetadata.CancelDateText(record);
                var parsed = VoidOperationSessionCache.Parse(VoidOperationSessionCache.ReasonFor(record.InvoiceNumber));
                var row = NewRow("作廢", ShortDate(cancelText), "已完成", parsed?.Reason ?? "已作廢");
                row.Tag = new VoidHistoryItem(record.InvoiceNumber, cancelText, parsed);
                list.Items.Add(row);
            }

            foreach (var allowance in InvoiceAllowanceMetadata.ReadOfficial(record)
                         .Where(item => item.InvoiceStatus == UploadStatuses.Complete))
            {
                var inclusive = InclusiveAmount(allowance);
                var row = NewRow("折讓", FormatAllowanceDate(allowance.AllowanceDate), "已完成", "$" + inclusive);
                row.Tag = new AllowanceHistoryItem(record, allowance);
                list.Items.Add(row);
            }
        }
        finally
        {
            list.EndUpdate();
        }
    }

    private void OpenSelected()
    {
        if (list.SelectedItems.Count != 1) return;
        switch (list.SelectedItems[0].Tag)
        {
            case VoidHistoryItem item:
                using (var form = new VoidHistoryDetailForm(item)) form.ShowDialog(FindForm());
                break;
            case AllowanceHistoryItem item:
            {
                var repository = LocalRepository.Open(AppContext.BaseDirectory, new DpapiSecretProtector());
                using var form = new AllowanceHistoryDetailForm(item.Record, item.Allowance, repository);
                form.ShowDialog(FindForm());
                break;
            }
        }
    }

    private static ListViewItem NewRow(string type, string date, string state, string summary)
    {
        var row = new ListViewItem(type);
        row.SubItems.Add(date);
        row.SubItems.Add(state);
        row.SubItems.Add(summary);
        return row;
    }

    private static string ShortDate(string value)
    {
        if (value.Length >= 10) return value[..10];
        return value;
    }

    internal static string FormatAllowanceDate(string value)
    {
        value = (value ?? string.Empty).Trim();
        if (value.Length == 8 && value.All(char.IsAsciiDigit))
            return value[..4] + "/" + value.Substring(4, 2) + "/" + value.Substring(6, 2);
        return value;
    }

    internal static string InclusiveAmount(InvoiceAllowanceResult allowance)
    {
        try
        {
            return FixedDecimal.Add(FixedDecimal.Parse(allowance.TotalAmount), FixedDecimal.Parse(allowance.TaxAmount)).ToString();
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            return "?";
        }
    }

    internal sealed record VoidHistoryItem(string InvoiceNumber, string CancelDate, ParsedVoidReason? ParsedReason);
    internal sealed record AllowanceHistoryItem(InvoiceRecord Record, InvoiceAllowanceResult Allowance);

    private sealed class HistoryListView : ListView
    {
        public HistoryListView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();
        }
    }
}

internal sealed class VoidHistoryDetailForm : Form
{
    public VoidHistoryDetailForm(InvoiceOperationHistoryControl.VoidHistoryItem item)
    {
        Text = "作廢詳細資訊";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(360, item.ParsedReason?.Reviewed == true ? 250 : 220);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);

        var rows = new List<KeyValuePair<string, string>>
        {
            new("發票號碼", item.InvoiceNumber),
            new("作廢時間", item.CancelDate),
        };
        if (item.ParsedReason is { } parsed)
        {
            rows.Add(new("使用者", parsed.UserEmployeeNo));
            if (parsed.Reviewed) rows.Add(new("覆核管理員", parsed.ReviewerEmployeeNo));
            rows.Add(new("作廢原因", parsed.Reason));
        }
        else
        {
            rows.Add(new("作廢原因", "本次資料未取得"));
        }
        Build(rows);
    }

    private void Build(IReadOnlyList<KeyValuePair<string, string>> rows)
    {
        var body = HistoryDetailLayout(rows, Font);
        var close = UiControls.StandardButton("關閉");
        close.Width = 90;
        close.DialogResult = DialogResult.OK;
        var actions = CenteredActions(close);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.Controls.Add(body, 0, 0);
        root.Controls.Add(actions, 0, 1);
        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;
    }

    internal static Panel HistoryDetailLayout(IReadOnlyList<KeyValuePair<string, string>> rows, Font font)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 0, Margin = Padding.Empty };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var row in rows)
        {
            var index = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = row.Key, AutoSize = true, Font = new Font(font, FontStyle.Bold), ForeColor = Color.DimGray, Margin = new Padding(0, 5, 8, 5) }, 0, index);
            table.Controls.Add(new Label { Text = row.Value, AutoSize = true, MaximumSize = new Size(220, 0), Margin = new Padding(0, 5, 0, 5) }, 1, index);
        }
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(10), BackColor = Color.White };
        panel.Controls.Add(table);
        return panel;
    }

    internal static FlowLayoutPanel CenteredActions(params Button[] buttons)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
        foreach (var button in buttons) panel.Controls.Add(button);
        panel.SizeChanged += (_, _) =>
        {
            var width = panel.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
            panel.Padding = new Padding(Math.Max(0, (panel.ClientSize.Width - width) / 2), 4, 0, 0);
        };
        return panel;
    }
}

internal sealed class AllowanceHistoryDetailForm : Form
{
    private readonly InvoiceRecord record;
    private readonly InvoiceAllowanceResult allowance;
    private readonly LocalRepository repository;
    private readonly AllowancePdfService pdfService;
    private readonly EmployeeAllowanceVoidWorkflowService voidWorkflow;
    private readonly Button viewPdf = UiControls.StandardButton("檢視 PDF");
    private readonly Button voidAllowance = UiControls.DangerButton("折讓作廢");
    private bool busy;

    public AllowanceHistoryDetailForm(
        InvoiceRecord record,
        InvoiceAllowanceResult allowance,
        LocalRepository repository)
    {
        this.record = record ?? throw new ArgumentNullException(nameof(record));
        this.allowance = allowance ?? throw new ArgumentNullException(nameof(allowance));
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        pdfService = new AllowancePdfService(repository);
        voidWorkflow = new EmployeeAllowanceVoidWorkflowService(repository);

        Text = "折讓詳細資訊";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(430, 320);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);

        var rows = new List<KeyValuePair<string, string>>
        {
            new("折讓單號", allowance.AllowanceNumber),
            new("折讓日期", InvoiceOperationHistoryControl.FormatAllowanceDate(allowance.AllowanceDate)),
            new("狀態", allowance.InvoiceStatus == UploadStatuses.Complete ? "已完成" : allowance.InvoiceStatus.ToString(CultureInfo.InvariantCulture)),
            new("未稅金額", Money(allowance.TotalAmount)),
            new("稅額", Money(allowance.TaxAmount)),
            new("含稅總額", Money(InvoiceOperationHistoryControl.InclusiveAmount(allowance))),
        };
        var body = VoidHistoryDetailForm.HistoryDetailLayout(rows, Font);
        var close = UiControls.StandardButton("關閉");
        close.Width = 90;
        close.DialogResult = DialogResult.OK;
        viewPdf.Width = 100;
        voidAllowance.Width = 100;
        viewPdf.Click += async (_, _) => await ViewPdfAsync();
        voidAllowance.Click += async (_, _) => await RequestVoidAsync();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.Controls.Add(body, 0, 0);
        root.Controls.Add(VoidHistoryDetailForm.CenteredActions(viewPdf, voidAllowance, close), 0, 1);
        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;
    }

    private async Task ViewPdfAsync()
    {
        if (busy) return;
        var styles = AllowancePdfStyles.Official
            .Select(style => new InvoicePdfStyle(style.Code, style.Name))
            .ToArray();
        using var selector = new PdfStyleSelectionForm(styles, "折讓單", "檢視");
        if (selector.ShowDialog(this) != DialogResult.OK || selector.SelectedStyle is null) return;

        SetBusy(true, "取得 PDF 中…");
        try
        {
            var document = await pdfService.GetAsync(allowance.AllowanceNumber, selector.SelectedStyle.Code);
            using var viewer = new InvoicePdfViewerForm(document, Path.Combine(repository.CacheDirectory, "WebView2"));
            viewer.ShowDialog(this);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "取得折讓 PDF 失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed) SetBusy(false, "檢視 PDF");
        }
    }

    private async Task RequestVoidAsync()
    {
        if (busy) return;
        using var request = new AllowanceVoidRequestForm();
        if (request.ShowDialog(this) != DialogResult.OK) return;

        SetBusy(true, "申請中…");
        try
        {
            var result = await voidWorkflow.SubmitAsync(
                record,
                allowance.AllowanceNumber,
                request.EmployeeNo,
                request.Password,
                request.Reason);
            MessageBox.Show(
                this,
                result.AlreadyQueued
                    ? result.Message
                    : "折讓作廢申請已建立。\n\n請通知管理員查看並完成操作。",
                "折讓作廢人工處理",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (EmployeeVoidAuthenticationDelayException error)
        {
            MessageBox.Show(this, error.Message, "驗證暫停", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (InvalidOperationException error) when (error.Message == "員工編號或密碼錯誤")
        {
            MessageBox.Show(this, error.Message, "驗證失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "折讓作廢申請未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed) SetBusy(false, "檢視 PDF");
        }
    }

    private void SetBusy(bool value, string viewText)
    {
        busy = value;
        viewPdf.Enabled = !value;
        voidAllowance.Enabled = !value;
        viewPdf.Text = value ? viewText : "檢視 PDF";
        if (!value) voidAllowance.Text = "折讓作廢";
    }

    private static string Money(string value)
    {
        try { return MoneyFormatter.Decimal(FixedDecimal.Parse(value).ToString()); }
        catch (Exception error) when (error is FormatException or OverflowException) { return value; }
    }
}
