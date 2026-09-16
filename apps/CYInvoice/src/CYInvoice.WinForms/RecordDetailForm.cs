using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class RecordDetailForm : Form
{
    private readonly InvoiceRecord record;
    private readonly LocalRepository repository;
    private readonly InvoiceService service;
    private readonly bool paperInvoice;
    private readonly Button printPdf = UiControls.StandardButton("列印發票");
    private readonly Button close = UiControls.StandardButton("關閉");
    private readonly TableLayoutPanel details = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        ColumnCount = 6,
        RowCount = 4,
        Margin = Padding.Empty,
    };
    private readonly PictureBox paperPreview = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(54, 54, 54),
        Margin = Padding.Empty,
    };
    private readonly Label previewStatus = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.DimGray,
        AutoEllipsis = true,
        Margin = Padding.Empty,
    };
    private readonly LinkLabel retryPreview = new()
    {
        Text = "重新載入預覽",
        AutoSize = true,
        Anchor = AnchorStyles.Right,
        Visible = false,
        Margin = new Padding(10, 0, 0, 0),
    };
    private readonly CancellationTokenSource previewCancellation = new();
    private Bitmap? activePreview;
    private bool previewLoading;

    public RecordDetailForm(InvoiceRecord record, LocalRepository repository, InvoiceService service)
    {
        this.record = record;
        this.repository = repository;
        this.service = service;
        paperInvoice = string.Equals(record.Delivery, InvoiceService.DeliveryPaper, StringComparison.Ordinal);
        Text = $"發票詳細資訊－{record.InvoiceNumber}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1080, 720);
        MinimumSize = new Size(900, 620);
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        Icon = ApplicationIcon.Load();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 14, 18, 10),
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

        var eligibility = service.GetInvoicePdfEligibility(record);
        Control content;
        if (paperInvoice)
        {
            content = BuildPaperPreview(eligibility);
            printPdf.Enabled = eligibility.Allowed;
            printPdf.Click += async (_, _) => await PrintPaperInvoiceAsync(eligibility.CompanyBuyer);
            Shown += async (_, _) =>
            {
                if (eligibility.Allowed) await LoadPaperPreviewAsync();
            };
        }
        else
        {
            content = BuildCarrierContent();
            printPdf.Visible = false;
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
        if (paperInvoice) actions.Controls.Add(printPdf);
        actions.Controls.Add(close);

        root.Controls.Add(details, 0, 0);
        root.Controls.Add(content, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;
        retryPreview.LinkClicked += async (_, _) => await LoadPaperPreviewAsync();
        FormClosed += (_, _) => previewCancellation.Cancel();
    }

    private Control BuildPaperPreview(InvoicePdfEligibility eligibility)
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 8, 0, 4),
        };
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        previewStatus.Text = eligibility.Allowed
            ? (eligibility.CompanyBuyer ? "官方 A4 發票預覽" : "官方 A4 發票第一頁預覽")
            : eligibility.Reason;
        header.Controls.Add(previewStatus, 0, 0);
        header.Controls.Add(retryPreview, 1, 0);

        var previewFrame = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(54, 54, 54),
            Padding = new Padding(12),
            Margin = Padding.Empty,
        };
        previewFrame.Controls.Add(paperPreview);
        section.Controls.Add(header, 0, 0);
        section.Controls.Add(previewFrame, 0, 1);
        return section;
    }

    private Control BuildCarrierContent()
    {
        activePreview = CarrierInvoicePreview.Render(record, repository.Settings.LoadOrCreate());
        var receipt = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(238, 240, 242),
            Image = activePreview,
            Margin = Padding.Empty,
        };
        var receiptFrame = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(238, 240, 242),
            Padding = new Padding(8),
            Margin = new Padding(0, 8, 8, 4),
        };
        receiptFrame.Controls.Add(receipt);

        var items = CreateItemsGrid();
        var itemSection = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(8, 8, 0, 4),
        };
        itemSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        itemSection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        itemSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        itemSection.Controls.Add(new Label
        {
            Text = "交易明細",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold),
            Margin = Padding.Empty,
        }, 0, 0);
        itemSection.Controls.Add(items, 0, 1);
        itemSection.Controls.Add(new Label
        {
            Text = $"共 {record.Items.Count} 項｜發票總額  {MoneyFormatter.Integer(record.Amount)}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font(Font, FontStyle.Bold),
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 4, 0),
        }, 0, 2);

        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Tag = "carrier-preview",
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.Controls.Add(receiptFrame, 0, 0);
        split.Controls.Add(itemSection, 1, 0);
        return split;
    }

    private DataGridView CreateItemsGrid()
    {
        var items = UiControls.Grid();
        items.ReadOnly = true;
        items.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        items.Columns.Add(Column("品名", 260, fill: true));
        items.Columns.Add(Column("數量", 72, right: true));
        items.Columns.Add(Column("單價", 105, right: true));
        items.Columns.Add(Column("金額", 110, right: true));
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
        return items;
    }

    private async Task LoadPaperPreviewAsync()
    {
        if (previewLoading || previewCancellation.IsCancellationRequested) return;
        previewLoading = true;
        retryPreview.Visible = false;
        previewStatus.Text = "正在取得官方 A4 預覽…";
        try
        {
            var bitmap = await InvoicePdfPreview.LoadFirstPageAsync(record, repository, service, previewCancellation.Token);
            if (previewCancellation.IsCancellationRequested || IsDisposed)
            {
                bitmap.Dispose();
                return;
            }
            var old = activePreview;
            activePreview = bitmap;
            paperPreview.Image = bitmap;
            old?.Dispose();
            previewStatus.Text = service.GetInvoicePdfEligibility(record).CompanyBuyer
                ? "官方 A4 發票預覽"
                : "官方 A4 發票第一頁預覽";
        }
        catch (OperationCanceledException) when (previewCancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!IsDisposed)
            {
                previewStatus.Text = "預覽尚未取得：" + error.Message;
                retryPreview.Visible = true;
            }
        }
        finally
        {
            previewLoading = false;
        }
    }

    private async Task PrintPaperInvoiceAsync(bool companyBuyer)
    {
        InvoicePdfStyle style;
        if (companyBuyer)
        {
            using var selector = new PdfStyleSelectionForm();
            if (selector.ShowDialog(this) != DialogResult.OK) return;
            var selected = selector.SelectedStyle;
            if (selected is null) return;
            style = selected;
        }
        else
        {
            style = InvoicePdfStyles.A4;
        }
        await OpenPdfAsync(style);
    }

    private async Task OpenPdfAsync(InvoicePdfStyle style)
    {
        printPdf.Enabled = false;
        printPdf.Text = "取得 PDF 中…";
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
            printPdf.Text = "列印發票";
            printPdf.Enabled = service.GetInvoicePdfEligibility(record).Allowed;
        }
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
            UploadStatus = UploadStatuses.Uploading,
            UploadStatusText = "上傳中",
            Source = InvoiceSources.Manual,
            OrderId = "20260916001",
            Amount = 105,
            Items = [new InvoiceItem { Description = "測試商品", Quantity = 1, UnitPrice = 105, Amount = 105 }],
        };
        using var consumer = new RecordDetailForm(baseRecord, repository, service);
        consumer.PerformLayout();
        consumer.VerifyLayout(companyBuyer: false, carrier: false);
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
                Source = baseRecord.Source,
                OrderId = baseRecord.OrderId,
                Amount = baseRecord.Amount,
                Items = baseRecord.Items,
            },
            repository,
            service);
        company.PerformLayout();
        company.VerifyLayout(companyBuyer: true, carrier: false);
        using var carrier = new RecordDetailForm(
            new InvoiceRecord
            {
                Id = "carrier-smoke",
                Environment = environment,
                InvoiceNumber = "BB87654321",
                InvoiceState = InvoiceStates.Opened,
                Delivery = "會員載具",
                CarrierType = "amego",
                CarrierId1 = "motmp_20260916001",
                Source = InvoiceSources.Mo,
                OrderId = "20260916001",
                InvoiceDate = "2026/09/16",
                InvoiceTime = "12:34:56",
                Amount = 105,
                Items = baseRecord.Items,
            },
            repository,
            service);
        carrier.PerformLayout();
        carrier.VerifyLayout(companyBuyer: false, carrier: true);
        using var selector = new PdfStyleSelectionForm();
        selector.PerformLayout();
        selector.VerifySmokeLayout();
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

    private void VerifyLayout(bool companyBuyer, bool carrier)
    {
        if (details.ColumnCount != 6 || details.RowCount < 4)
            throw new InvalidOperationException("發票詳細資訊未使用緊湊三欄配置");
        if (!UiControls.HasLogicalSize(close, 100, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("關閉按鈕未使用核准尺寸");
        if (carrier)
        {
            if (paperInvoice || activePreview is null)
                throw new InvalidOperationException("會員載具未建立模擬發票預覽");
            return;
        }
        if (!paperInvoice || !printPdf.Enabled)
            throw new InvalidOperationException("已開立紙本發票未開放列印");
        if (!UiControls.HasLogicalSize(printPdf, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("列印發票按鈕未使用標準尺寸");
        if (service.GetInvoicePdfEligibility(record).CompanyBuyer != companyBuyer)
            throw new InvalidOperationException("紙本發票買方類型判斷錯誤");
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
            previewCancellation.Cancel();
            paperPreview.Image = null;
            activePreview?.Dispose();
            Icon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
