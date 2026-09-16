using CYInvoice.Core;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class RecordDetailForm : Form
{
    private readonly InvoiceRecord record;
    private readonly LocalRepository repository;
    private readonly InvoiceService service;
    private readonly ContextMenuStrip pdfStylesMenu = new();
    private readonly Button viewPdf = UiControls.StandardButton("檢視 PDF");
    private readonly Button close = UiControls.StandardButton("關閉");
    private readonly TableLayoutPanel details = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        ColumnCount = 6,
        RowCount = 4,
        Margin = Padding.Empty,
    };

    public RecordDetailForm(InvoiceRecord record, LocalRepository repository, InvoiceService service)
    {
        this.record = record;
        this.repository = repository;
        this.service = service;
        Text = $"發票詳細資訊－{record.InvoiceNumber}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(880, 600);
        MinimumSize = new Size(760, 520);
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        Icon = ApplicationIcon.Load();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 16, 18, 12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        ConfigureDetails();
        AddFieldRow(0,
            "發票號碼", record.InvoiceNumber,
            "開立時間", IssueTime(record),
            "發票金額", MoneyFormatter.Integer(record.Amount));
        AddFieldRow(1,
            "訂單編號", record.OrderId,
            "來源", record.Source,
            "使用環境", record.Environment == Environments.Production ? "正式" : "測試");
        AddFieldRow(2,
            "買受人", record.BuyerName,
            "統一編號", record.BuyerIdentifier,
            "交付方式", record.Delivery);
        AddFieldRow(3,
            "發票狀態", record.InvoiceState,
            "上傳狀態", record.UploadStatusText,
            "最後確認", record.LastChecked);
        AddOptionalDetail("錯誤訊息", record.ErrorMessage, Color.Firebrick);
        AddOptionalDetail("總備註", record.MainRemark, SystemColors.ControlText);

        var items = UiControls.Grid();
        items.ReadOnly = true;
        items.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        items.Columns.Add(Column("品名", 300, fill: true));
        items.Columns.Add(Column("數量", 90, right: true));
        items.Columns.Add(Column("單價", 120, right: true));
        items.Columns.Add(Column("金額", 120, right: true));
        foreach (var item in record.Items)
        {
            var values = InvoiceCalculator.ItemDecimals(item);
            items.Rows.Add(
                item.Description,
                values.Quantity.ToString(),
                MoneyFormatter.Decimal(values.UnitPrice.ToString()),
                MoneyFormatter.Decimal(values.Amount.ToString()));
        }
        items.ClearSelection();

        var itemSection = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 10, 0, 4),
        };
        itemSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        itemSection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        itemSection.Controls.Add(new Label
        {
            Text = "商品明細",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold),
            Margin = Padding.Empty,
        }, 0, 0);
        itemSection.Controls.Add(items, 0, 1);

        var eligibility = service.GetInvoicePdfEligibility(record);
        viewPdf.Enabled = eligibility.Allowed;
        viewPdf.Click += async (_, _) =>
        {
            if (!eligibility.Allowed) return;
            if (eligibility.CompanyBuyer)
            {
                pdfStylesMenu.Show(viewPdf, new Point(0, viewPdf.Height));
                return;
            }
            await OpenPdfAsync(InvoicePdfStyles.A4);
        };
        if (eligibility.CompanyBuyer)
        {
            foreach (var style in InvoicePdfStyles.Company)
            {
                var menuItem = new ToolStripMenuItem(style.Name) { Tag = style };
                menuItem.Click += async (_, _) => await OpenPdfAsync((InvoicePdfStyle)menuItem.Tag!);
                pdfStylesMenu.Items.Add(menuItem);
            }
        }
        if (!eligibility.Allowed)
        {
            var toolTip = new ToolTip();
            toolTip.SetToolTip(viewPdf, eligibility.Reason);
            Disposed += (_, _) => toolTip.Dispose();
        }

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
        actions.Controls.Add(viewPdf);
        actions.Controls.Add(close);

        root.Controls.Add(details, 0, 0);
        root.Controls.Add(itemSection, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;
    }

    private void ConfigureDetails()
    {
        for (var index = 0; index < 3; index++)
        {
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        }
        for (var row = 0; row < 4; row++)
            details.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
    }

    private void AddFieldRow(
        int row,
        string label1,
        string value1,
        string label2,
        string value2,
        string label3,
        string value3)
    {
        AddField(row, 0, label1, value1);
        AddField(row, 2, label2, value2);
        AddField(row, 4, label3, value3);
    }

    private void AddField(int row, int column, string label, string value)
    {
        details.Controls.Add(FieldLabel(label), column, row);
        details.Controls.Add(ValueLabel(value), column + 1, row);
    }

    private void AddOptionalDetail(string label, string value, Color valueColor)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var row = details.RowCount++;
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.Controls.Add(FieldLabel(label), 0, row);
        var text = ValueLabel(value);
        text.AutoSize = true;
        text.MinimumSize = new Size(0, 31);
        text.ForeColor = valueColor;
        text.AutoEllipsis = false;
        details.Controls.Add(text, 1, row);
        details.SetColumnSpan(text, 5);
    }

    private async Task OpenPdfAsync(InvoicePdfStyle style)
    {
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
            viewPdf.Text = "檢視 PDF";
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
            UploadStatus = CYInvoice.Core.Amego.UploadStatuses.Uploading,
            UploadStatusText = "上傳中",
        };
        using var consumer = new RecordDetailForm(baseRecord, repository, service);
        consumer.PerformLayout();
        consumer.VerifyPdfControls(expectedStyleMenuItems: 0);
        using var company = new RecordDetailForm(
            new InvoiceRecord
            {
                Id = baseRecord.Id,
                Environment = environment,
                InvoiceNumber = baseRecord.InvoiceNumber,
                InvoiceState = baseRecord.InvoiceState,
                Delivery = baseRecord.Delivery,
                UploadStatus = baseRecord.UploadStatus,
                UploadStatusText = baseRecord.UploadStatusText,
                BuyerIdentifier = "12345675",
            },
            repository,
            service);
        company.PerformLayout();
        company.VerifyPdfControls(expectedStyleMenuItems: 5);
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

    private void VerifyPdfControls(int expectedStyleMenuItems)
    {
        if (!viewPdf.Enabled)
            throw new InvalidOperationException("上傳中的已開立紙本發票未開放檢視 PDF");
        if (pdfStylesMenu.Items.Count != expectedStyleMenuItems)
            throw new InvalidOperationException("公司統編 PDF 版型選單不正確");
        if (details.ColumnCount != 6 || details.RowCount < 4)
            throw new InvalidOperationException("發票詳細資訊未使用緊湊三欄配置");
        if (!UiControls.HasLogicalSize(viewPdf, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("檢視 PDF 按鈕未使用標準尺寸");
        if (!UiControls.HasLogicalSize(close, 100, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("關閉按鈕未使用核准尺寸");
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.DimGray,
        Margin = new Padding(0, 1, 4, 1),
        AutoEllipsis = true,
    };

    private static Label ValueLabel(string value) => new()
    {
        Text = value,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = SystemColors.ControlText,
        Margin = new Padding(0, 1, 12, 1),
        AutoEllipsis = true,
    };

    private static DataGridViewTextBoxColumn Column(string title, int width, bool right = false, bool fill = false) => new()
    {
        HeaderText = title,
        Width = width,
        AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
        SortMode = DataGridViewColumnSortMode.NotSortable,
        DefaultCellStyle = { Alignment = right ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft },
    };

    private static string IssueTime(InvoiceRecord record) =>
        record.InvoiceDate.Length == 0 ? record.SentAt : (record.InvoiceDate + " " + record.InvoiceTime).Trim();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            pdfStylesMenu.Dispose();
            Icon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
