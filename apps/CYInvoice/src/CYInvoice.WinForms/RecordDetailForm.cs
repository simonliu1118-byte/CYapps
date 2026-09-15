using CYInvoice.Core;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class RecordDetailForm : Form
{
    private readonly InvoiceRecord record;
    private readonly LocalRepository repository;
    private readonly InvoiceService service;
    private readonly ComboBox pdfStyle = new()
    {
        Width = 170,
        DropDownStyle = ComboBoxStyle.DropDownList,
        DisplayMember = nameof(InvoicePdfStyle.Name),
        Margin = new Padding(6, 5, 6, 5),
    };
    private readonly Button viewPdf = UiControls.StandardButton("檢視官方 PDF");
    private readonly Label pdfStatus = new()
    {
        AutoSize = false,
        Width = 200,
        Height = UiControls.StandardButtonHeight,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(8, 10, 8, 0),
        ForeColor = Color.DimGray,
    };

    public RecordDetailForm(InvoiceRecord record, LocalRepository repository, InvoiceService service)
    {
        this.record = record;
        this.repository = repository;
        this.service = service;
        Text = $"發票詳細資訊－{record.InvoiceNumber}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(920, 650);
        MinimumSize = new Size(800, 560);
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        Icon = ApplicationIcon.Load();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(16) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 7 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddField(fields, 0, "開立時間", IssueTime(record), "發票號碼", record.InvoiceNumber);
        AddField(fields, 1, "來源", record.Source, "訂單編號", record.OrderId);
        AddField(fields, 2, "統一編號", record.BuyerIdentifier, "買受人", record.BuyerName);
        AddField(fields, 3, "發票金額", MoneyFormatter.Integer(record.Amount), "交付方式", record.Delivery);
        AddField(fields, 4, "發票狀態", record.InvoiceState, "上傳狀態", record.UploadStatusText);
        AddField(fields, 5, "使用環境", record.Environment == Environments.Production ? "正式" : "測試", "最後確認", record.LastChecked);
        AddField(fields, 6, "錯誤訊息", record.ErrorMessage, "總備註", record.MainRemark);
        var items = UiControls.Grid();
        items.ReadOnly = true;
        items.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        items.Columns.Add(Column("品名", 300, fill: true));
        items.Columns.Add(Column("數量", 100, right: true));
        items.Columns.Add(Column("單價", 130, right: true));
        items.Columns.Add(Column("金額", 130, right: true));
        foreach (var item in record.Items)
        {
            var values = InvoiceCalculator.ItemDecimals(item);
            items.Rows.Add(item.Description, values.Quantity.ToString(), MoneyFormatter.Decimal(values.UnitPrice.ToString()), MoneyFormatter.Decimal(values.Amount.ToString()));
        }
        items.ClearSelection();

        var eligibility = service.GetInvoicePdfEligibility(record);
        var styles = service.GetInvoicePdfStyles(record);
        pdfStyle.Items.AddRange(styles.Cast<object>().ToArray());
        pdfStyle.SelectedIndex = 0;
        pdfStyle.Enabled = eligibility.CompanyBuyer;
        viewPdf.Enabled = eligibility.Allowed;
        viewPdf.Click += async (_, _) => await OpenPdfAsync();
        pdfStatus.Text = eligibility.Allowed
            ? eligibility.CompanyBuyer ? "公司統編可選 5 種版型" : "一般消費者固定使用 A4 整張"
            : eligibility.Reason;
        if (!eligibility.Allowed)
        {
            var toolTip = new ToolTip();
            toolTip.SetToolTip(viewPdf, eligibility.Reason);
            toolTip.SetToolTip(pdfStatus, eligibility.Reason);
            Disposed += (_, _) => toolTip.Dispose();
        }
        var close = UiControls.StandardButton("關閉");
        close.Width = 100;
        close.DialogResult = DialogResult.OK;
        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        actions.Controls.Add(UiControls.Label("PDF 版型"));
        actions.Controls.Add(pdfStyle);
        actions.Controls.Add(viewPdf);
        actions.Controls.Add(close);
        actions.Controls.Add(pdfStatus);
        root.Controls.Add(fields, 0, 0);
        root.Controls.Add(items, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;
    }

    private async Task OpenPdfAsync()
    {
        if (pdfStyle.SelectedItem is not InvoicePdfStyle style) return;
        viewPdf.Enabled = false;
        viewPdf.Text = "取得 PDF 中…";
        try
        {
            var document = await service.GetInvoicePdfAsync(record, style.Code);
            using var viewer = new InvoicePdfViewerForm(
                document,
                Path.Combine(repository.CacheDirectory, "WebView2"));
            viewer.ShowDialog(this);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "取得 PDF 失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            viewPdf.Text = "檢視官方 PDF";
            viewPdf.Enabled = service.GetInvoicePdfEligibility(record).Allowed;
        }
    }

    internal static void VerifySmokeLayout(LocalRepository repository, InvoiceService service)
    {
        var environment = repository.Settings.LoadOrCreate().Environment;
        var baseRecord = new InvoiceRecord
        {
            Id = "pdf-smoke",
            Environment = environment,
            InvoiceNumber = "AA12345678",
            InvoiceState = InvoiceStates.Opened,
            Delivery = InvoiceService.DeliveryPaper,
            UploadStatus = CYInvoice.Core.Amego.UploadStatuses.Complete,
        };
        using var consumer = new RecordDetailForm(baseRecord, repository, service);
        consumer.PerformLayout();
        consumer.VerifyPdfControls(1, enabledStyle: false);
        using var company = new RecordDetailForm(
            new InvoiceRecord
            {
                Id = baseRecord.Id,
                Environment = environment,
                InvoiceNumber = baseRecord.InvoiceNumber,
                InvoiceState = baseRecord.InvoiceState,
                Delivery = baseRecord.Delivery,
                UploadStatus = baseRecord.UploadStatus,
                BuyerIdentifier = "12345675",
            },
            repository,
            service);
        company.PerformLayout();
        company.VerifyPdfControls(5, enabledStyle: true);
        using var viewer = new InvoicePdfViewerForm(
            new InvoicePdfDocument(
                Path.Combine(repository.InvoicePdfCacheDirectory, "smoke.pdf"),
                baseRecord.InvoiceNumber,
                InvoicePdfStyles.A4,
                FromCache: true),
            Path.Combine(repository.CacheDirectory, "WebView2"));
        viewer.PerformLayout();
        viewer.VerifySmokeLayout();
    }

    private void VerifyPdfControls(int styleCount, bool enabledStyle)
    {
        if (pdfStyle.Items.Count != styleCount || pdfStyle.Enabled != enabledStyle)
            throw new InvalidOperationException("紙本發票 PDF 版型選項不正確");
        if (!UiControls.HasLogicalSize(viewPdf, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("檢視官方 PDF 按鈕未使用標準尺寸");
    }

    private static DataGridViewTextBoxColumn Column(string title, int width, bool right = false, bool fill = false) => new()
    {
        HeaderText = title, Width = width, AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
        SortMode = DataGridViewColumnSortMode.NotSortable,
        DefaultCellStyle = { Alignment = right ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft },
    };

    private static void AddField(TableLayoutPanel panel, int row, string label1, string value1, string label2, string value2)
    {
        panel.Controls.Add(UiControls.Label(label1), 0, row);
        panel.Controls.Add(Value(value1), 1, row);
        panel.Controls.Add(UiControls.Label(label2), 2, row);
        panel.Controls.Add(Value(value2), 3, row);
    }

    private static TextBox Value(string value) => new() { Text = value, ReadOnly = true, Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(3, 4, 8, 4) };
    private static string IssueTime(InvoiceRecord record) => record.InvoiceDate.Length == 0 ? record.SentAt : (record.InvoiceDate + " " + record.InvoiceTime).Trim();

    protected override void Dispose(bool disposing)
    {
        if (disposing) Icon?.Dispose();
        base.Dispose(disposing);
    }
}
