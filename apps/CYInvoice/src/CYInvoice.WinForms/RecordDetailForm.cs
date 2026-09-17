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
    private readonly Button viewPdf = UiControls.StandardButton("檢視 PDF");
    private readonly Button printPdf = UiControls.StandardButton("列印發票");
    private readonly Button changePrinter = UiControls.StandardButton("更換印表機…");
    private readonly Button close = UiControls.StandardButton("關閉");
    private readonly TableLayoutPanel details = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        ColumnCount = 2,
        RowCount = 0,
        Margin = Padding.Empty,
    };
    private readonly PictureBox paperPreview = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.White,
        Margin = Padding.Empty,
    };
    private readonly Panel paperPreviewHost = new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(54, 54, 54),
        Margin = Padding.Empty,
    };
    private readonly Panel a4PreviewFrame = new()
    {
        BackColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
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
    private readonly Label printerStatus = ValueLabel(string.Empty);
    private readonly CancellationTokenSource previewCancellation = new();
    private Bitmap? activePreview;
    private bool previewLoading;
    private bool pdfBusy;

    public RecordDetailForm(InvoiceRecord record, LocalRepository repository, InvoiceService service)
    {
        this.record = record;
        this.repository = repository;
        this.service = service;
        paperInvoice = string.Equals(record.Delivery, InvoiceService.DeliveryPaper, StringComparison.Ordinal);
        Text = $"發票詳細資訊－{record.InvoiceNumber}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = paperInvoice ? new Size(940, 720) : new Size(1180, 690);
        MinimumSize = paperInvoice ? new Size(820, 640) : new Size(980, 620);
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        Icon = ApplicationIcon.Load();

        ConfigureDetails();
        AddDetail("發票號碼", record.InvoiceNumber);
        AddDetail("開立時間", IssueTime(record));
        AddDetail("訂單編號", record.OrderId);
        AddDetail("來源", record.Source);
        AddDetail("買受人", record.BuyerName);
        AddDetail("統一編號", record.BuyerIdentifier);
        AddDetail("發票金額", MoneyFormatter.Integer(record.Amount));
        AddDetail("使用環境", record.Environment == Environments.Production ? "正式" : "測試");
        AddDetail("交付方式", record.Delivery);
        AddDetail("發票狀態", record.InvoiceState);
        AddDetail("上傳狀態", record.UploadStatusText);
        AddDetail("最後確認", record.LastChecked);
        AddOptionalDetail("錯誤訊息", record.ErrorMessage, Color.Firebrick);
        AddOptionalDetail("總備註", record.MainRemark, SystemColors.ControlText);
        if (paperInvoice)
        {
            AddDetail("發票印表機", string.Empty, printerStatus);
            UpdatePrinterStatus();
        }

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(18, 14, 18, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        var eligibility = service.GetInvoicePdfEligibility(record);
        Control content;
        if (paperInvoice)
        {
            content = BuildPaperContent(eligibility);
            viewPdf.Enabled = eligibility.Allowed;
            printPdf.Enabled = eligibility.Allowed;
            viewPdf.Click += async (_, _) => await ViewPaperInvoiceAsync(eligibility.CompanyBuyer);
            printPdf.Click += async (_, _) => await PrintPaperInvoiceAsync(eligibility.CompanyBuyer);
            changePrinter.Click += (_, _) => ChangeInvoicePrinter();
            Shown += async (_, _) =>
            {
                if (eligibility.Allowed) await LoadPaperPreviewAsync();
            };
        }
        else
        {
            content = BuildCarrierContent();
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
        if (paperInvoice)
        {
            actions.Controls.Add(viewPdf);
            actions.Controls.Add(printPdf);
            actions.Controls.Add(changePrinter);
        }
        actions.Controls.Add(close);

        root.Controls.Add(content, 0, 0);
        root.Controls.Add(actions, 0, 1);
        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;
        retryPreview.LinkClicked += async (_, _) => await LoadPaperPreviewAsync();
        paperPreviewHost.Layout += (_, _) => LayoutA4Preview();
        FormClosed += (_, _) => previewCancellation.Cancel();
    }

    private Control BuildPaperContent(InvoicePdfEligibility eligibility)
    {
        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Tag = "paper-detail",
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.Controls.Add(BuildInformationSection(), 0, 0);
        split.Controls.Add(BuildPaperPreview(eligibility), 1, 0);
        return split;
    }

    private Control BuildInformationSection()
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 12, 4),
        };
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        section.Controls.Add(new Label
        {
            Text = "發票資訊",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold),
            Margin = Padding.Empty,
        }, 0, 0);
        var scroller = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        scroller.Controls.Add(details);
        section.Controls.Add(scroller, 0, 1);
        return section;
    }

    private Control BuildPaperPreview(InvoicePdfEligibility eligibility)
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(12, 0, 0, 4),
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

        a4PreviewFrame.Controls.Add(paperPreview);
        paperPreviewHost.Controls.Add(a4PreviewFrame);
        section.Controls.Add(header, 0, 0);
        section.Controls.Add(paperPreviewHost, 0, 1);
        return section;
    }

    private void LayoutA4Preview()
    {
        const double a4Ratio = 210D / 297D;
        var availableWidth = Math.Max(1, paperPreviewHost.ClientSize.Width - 20);
        var availableHeight = Math.Max(1, paperPreviewHost.ClientSize.Height - 20);
        int width;
        int height;
        if (availableWidth / (double)availableHeight > a4Ratio)
        {
            height = availableHeight;
            width = Math.Max(1, (int)Math.Round(height * a4Ratio));
        }
        else
        {
            width = availableWidth;
            height = Math.Max(1, (int)Math.Round(width / a4Ratio));
        }
        a4PreviewFrame.Bounds = new Rectangle(
            Math.Max(0, (paperPreviewHost.ClientSize.Width - width) / 2),
            Math.Max(0, (paperPreviewHost.ClientSize.Height - height) / 2),
            width,
            height);
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
            Margin = new Padding(8, 0, 8, 4),
        };
        receiptFrame.Controls.Add(receipt);

        var receiptSection = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
        };
        receiptSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        receiptSection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        receiptSection.Controls.Add(new Label
        {
            Text = "模擬電子發票",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(8, 0, 0, 0),
        }, 0, 0);
        receiptSection.Controls.Add(receiptFrame, 0, 1);

        var items = CreateItemsGrid();
        var itemSection = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(8, 0, 0, 4),
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
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Tag = "carrier-preview",
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.Controls.Add(BuildInformationSection(), 0, 0);
        split.Controls.Add(receiptSection, 1, 0);
        split.Controls.Add(itemSection, 2, 0);
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

    private async Task ViewPaperInvoiceAsync(bool companyBuyer)
    {
        var style = ChoosePaperStyle(companyBuyer, "檢視");
        if (style is null) return;
        SetPdfBusy(true, "檢視");
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
            SetPdfBusy(false, "檢視");
        }
    }

    private async Task PrintPaperInvoiceAsync(bool companyBuyer)
    {
        var style = ChoosePaperStyle(companyBuyer, "列印");
        if (style is null) return;
        var printerName = EnsureInvoicePrinter();
        if (printerName is null) return;

        SetPdfBusy(true, "列印");
        try
        {
            var document = await service.GetInvoicePdfAsync(record, style.Code);
            await InvoicePdfPrinter.PrintAsync(
                document,
                Path.Combine(repository.CacheDirectory, "WebView2Print"),
                printerName,
                consumerPaper: !companyBuyer,
                previewCancellation.Token);
        }
        catch (OperationCanceledException) when (previewCancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "列印發票失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdatePrinterStatus();
        }
        finally
        {
            SetPdfBusy(false, "列印");
        }
    }

    private InvoicePdfStyle? ChoosePaperStyle(bool companyBuyer, string action)
    {
        if (!companyBuyer) return InvoicePdfStyles.A4;
        using var selector = new PdfStyleSelectionForm(action);
        if (selector.ShowDialog(this) != DialogResult.OK) return null;
        return selector.SelectedStyle;
    }

    private string? EnsureInvoicePrinter()
    {
        var settings = repository.Settings.LoadOrCreate();
        var remembered = settings.InvoicePrinterName.Trim();
        if (remembered.Length != 0 && InvoicePdfPrinter.IsInstalled(remembered)) return remembered;
        if (remembered.Length != 0)
        {
            MessageBox.Show(
                this,
                $"先前設定的發票印表機「{remembered}」目前不存在。\n請重新選擇印表機後再列印。",
                "需要重新選擇印表機",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        return SelectInvoicePrinter(settings, remembered);
    }

    private void ChangeInvoicePrinter()
    {
        var settings = repository.Settings.LoadOrCreate();
        SelectInvoicePrinter(settings, settings.InvoicePrinterName);
    }

    private string? SelectInvoicePrinter(Settings settings, string currentPrinter)
    {
        using var selector = new InvoicePrinterSelectionForm(currentPrinter);
        if (selector.ShowDialog(this) != DialogResult.OK || selector.SelectedPrinterName.Length == 0) return null;
        settings.InvoicePrinterName = selector.SelectedPrinterName;
        repository.Settings.Save(settings);
        UpdatePrinterStatus();
        return settings.InvoicePrinterName;
    }

    private void UpdatePrinterStatus()
    {
        if (!paperInvoice) return;
        var name = repository.Settings.LoadOrCreate().InvoicePrinterName.Trim();
        if (name.Length == 0)
        {
            printerStatus.Text = "尚未設定（首次列印時選擇）";
            printerStatus.ForeColor = Color.DimGray;
            return;
        }
        if (!InvoicePdfPrinter.IsInstalled(name))
        {
            printerStatus.Text = name + "（目前不存在）";
            printerStatus.ForeColor = Color.Firebrick;
            return;
        }
        printerStatus.Text = name + (InvoicePdfPrinter.CanDuplex(name) ? "｜支援雙面" : "｜單面");
        printerStatus.ForeColor = SystemColors.ControlText;
    }

    private void SetPdfBusy(bool busy, string action)
    {
        pdfBusy = busy;
        var allowed = !busy && service.GetInvoicePdfEligibility(record).Allowed;
        viewPdf.Enabled = allowed;
        printPdf.Enabled = allowed;
        changePrinter.Enabled = !busy;
        viewPdf.Text = busy && action == "檢視" ? "取得 PDF 中…" : "檢視 PDF";
        printPdf.Text = busy && action == "列印" ? "列印中…" : "列印發票";
    }

    private void ConfigureDetails()
    {
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    }

    private void AddDetail(string label, string value, Label? target = null)
    {
        var row = details.RowCount++;
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        details.Controls.Add(FieldLabel(label), 0, row);
        var text = target ?? ValueLabel(value);
        if (target is not null) text.Text = value;
        details.Controls.Add(text, 1, row);
    }

    private void AddOptionalDetail(string label, string value, Color valueColor)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var row = details.RowCount++;
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        details.Controls.Add(FieldLabel(label), 0, row);
        var text = ValueLabel(value);
        text.ForeColor = valueColor;
        details.Controls.Add(text, 1, row);
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
        using var selector = new PdfStyleSelectionForm("列印");
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
        if (details.ColumnCount != 2 || details.RowCount < 12)
            throw new InvalidOperationException("發票詳細資訊未使用左側直向資訊配置");
        if (!UiControls.HasLogicalSize(close, 100, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("關閉按鈕未使用核准尺寸");
        if (carrier)
        {
            if (paperInvoice || activePreview is null)
                throw new InvalidOperationException("會員載具未建立模擬發票預覽");
            return;
        }
        if (!paperInvoice || !viewPdf.Enabled || !printPdf.Enabled)
            throw new InvalidOperationException("已開立紙本發票未開放檢視或列印");
        if (!UiControls.HasLogicalSize(viewPdf, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight) ||
            !UiControls.HasLogicalSize(printPdf, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight) ||
            !UiControls.HasLogicalSize(changePrinter, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("紙本發票操作按鈕未使用標準尺寸");
        if (service.GetInvoicePdfEligibility(record).CompanyBuyer != companyBuyer)
            throw new InvalidOperationException("紙本發票買方類型判斷錯誤");
        if (paperPreviewHost.Controls.Count != 1 || !ReferenceEquals(paperPreviewHost.Controls[0], a4PreviewFrame))
            throw new InvalidOperationException("紙本預覽未使用單一 A4 比例容器");
        if (pdfBusy)
            throw new InvalidOperationException("紙本詳細資訊初始狀態不應處於 PDF 忙碌狀態");
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
        Margin = new Padding(0, 1, 4, 1),
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
