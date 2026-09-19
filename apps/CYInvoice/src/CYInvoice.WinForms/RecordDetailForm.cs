using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class RecordDetailForm : Form
{
    private const int DetailWindowWidth = 820;
    private const int DetailActionButtonWidth = 112;
    private const int InformationColumnWidth = 292;
    private const int InformationLabelWidth = 90;
    private const int InformationValueMaxWidth = 190;
    private const int CarrierItemsSectionHeight = 250;
    private const string WaitingVoidDetailText = "(等待 發票作廢)";
    private const string WaitingVoidDetailTag = "waiting-void-detail-state";
    private const string WaitingVoidHighlightTag = "waiting-void-detail-highlight";

    private InvoiceRecord record;
    private readonly LocalRepository repository;
    private readonly InvoiceService service;
    private readonly InvoiceDetailRefreshService detailRefreshService;
    private readonly EmployeeVoidWorkflowService voidWorkflow;
    private readonly EmployeeAllowanceWorkflowService allowanceWorkflow;
    private readonly bool paperInvoice;
    private readonly string sellerCompanyName;
    private readonly Button viewPdf = UiControls.StandardButton("檢視 PDF");
    private readonly Button printPdf = UiControls.StandardButton("列印發票");
    private readonly Button changePrinter = UiControls.StandardButton("更換印表機…");
    private readonly Button voidInvoice = UiControls.StandardButton("作廢");
    private readonly Button allowanceInvoice = UiControls.StandardButton("折讓");
    private readonly Button close = UiControls.StandardButton("關閉");
    private readonly TableLayoutPanel details = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        ColumnCount = 2,
        RowCount = 0,
        Margin = Padding.Empty,
        BackColor = SystemColors.Control,
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
        Margin = new Padding(4, 0, 0, 4),
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
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = SystemColors.ControlText,
        BackColor = Color.White,
        Font = new Font("Microsoft JhengHei UI", 16F, FontStyle.Bold),
        Margin = Padding.Empty,
        Padding = new Padding(18),
    };
    private readonly LinkLabel retryPreview = new()
    {
        Text = "重新載入預覽",
        AutoSize = true,
        LinkColor = SystemColors.HotTrack,
        ActiveLinkColor = SystemColors.Highlight,
        Visible = false,
        BackColor = Color.White,
        Margin = Padding.Empty,
    };
    private readonly Panel previewMessageHost = new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.White,
        Visible = false,
    };
    private readonly Label printerStatus = new()
    {
        AutoSize = true,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.TopLeft,
        ForeColor = SystemColors.ControlText,
        BackColor = SystemColors.Control,
        Margin = Padding.Empty,
        Padding = new Padding(0, 5, 0, 5),
        MaximumSize = new Size(InformationValueMaxWidth, 0),
        AutoEllipsis = false,
    };
    private readonly CancellationTokenSource previewCancellation = new();
    private Bitmap? activePreview;
    private bool previewLoading;
    private bool pdfBusy;
    private bool voidBusy;
    private bool allowanceBusy;
    private bool latestStatusUnknown;

    public RecordDetailForm(
        InvoiceRecord record,
        LocalRepository repository,
        InvoiceService service,
        string sellerCompanyName = "")
    {
        this.record = record;
        this.repository = repository;
        this.service = service;
        detailRefreshService = new InvoiceDetailRefreshService(repository);
        voidWorkflow = new EmployeeVoidWorkflowService(repository);
        allowanceWorkflow = new EmployeeAllowanceWorkflowService(repository);
        this.sellerCompanyName = sellerCompanyName.Trim();
        paperInvoice = string.Equals(record.Delivery, InvoiceService.DeliveryPaper, StringComparison.Ordinal);
        Text = $"發票詳細資訊－{record.InvoiceNumber}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(DetailWindowWidth, 700);
        MinimumSize = new Size(760, 620);
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);

        ConfigureDetails();
        PopulateDetails();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(14, 10, 14, 8),
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

        CompactDetailAction(viewPdf);
        CompactDetailAction(printPdf);
        CompactDetailAction(changePrinter);
        CompactDetailAction(voidInvoice);
        CompactDetailAction(allowanceInvoice);
        close.Width = 100;
        close.Margin = new Padding(3, 2, 3, 2);
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

        if (CanShowVoidAction())
        {
            voidInvoice.Click += async (_, _) => await StartVoidAsync();
            actions.Controls.Add(voidInvoice);
        }
        if (CanShowAllowanceAction())
        {
            allowanceInvoice.Click += async (_, _) => await StartAllowanceAsync();
            actions.Controls.Add(allowanceInvoice);
        }
        actions.Controls.Add(close);

        root.Controls.Add(content, 0, 0);
        root.Controls.Add(actions, 0, 1);
        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;
        retryPreview.LinkClicked += async (_, _) => await LoadPaperPreviewAsync();
        previewMessageHost.Layout += (_, _) => LayoutPreviewMessage();
        paperPreviewHost.Layout += (_, _) => LayoutA4Preview();
        FormClosed += (_, _) => previewCancellation.Cancel();
        UpdateActionAvailability();
    }

    private bool CanShowVoidAction() =>
        record.InvoiceNumber.Trim().Length != 0 &&
        record.InvoiceState == InvoiceStates.Opened &&
        record.UploadStatus == UploadStatuses.Complete;

    private bool CanShowAllowanceAction() => CanShowVoidAction();

    private async Task StartVoidAsync()
    {
        if (voidBusy || allowanceBusy) return;
        using var reasonForm = new VoidReasonForm();
        if (reasonForm.ShowDialog(this) != DialogResult.OK) return;
        var reason = reasonForm.Reason;
        var refreshAfterFlow = false;

        using (var privacyMask = VoidConfirmationPrivacyMask.Apply(this, record.InvoiceNumber))
        {
            while (!IsDisposed)
            {
                using var confirm = new VoidConfirmationForm(paperInvoice);
                if (confirm.ShowDialog(this) != DialogResult.OK) return;

                SetVoidBusy(true);
                try
                {
                    var result = await voidWorkflow.SubmitAsync(
                        record,
                        confirm.EnteredInvoiceNumber,
                        confirm.EmployeeNo,
                        confirm.Password,
                        reason,
                        confirm.PaperReceiptState);

                    if (result.ManualReviewRequired)
                    {
                        MessageBox.Show(
                            this,
                            "已送交人工確認，尚未向光貿送出作廢。\n\n請由管理員或超級管理員至「上傳問題」處理。",
                            "人工確認",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        refreshAfterFlow = true;
                    }
                    else
                    {
                        var voidResult = result.VoidResult ?? throw new InvalidDataException("作廢流程缺少處理結果");
                        switch (voidResult.Outcome)
                        {
                            case InvoiceVoidOutcome.Confirmed:
                            case InvoiceVoidOutcome.AlreadyVoided:
                                MessageBox.Show(this, "光貿已確認發票作廢完成。", "作廢完成",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                                refreshAfterFlow = true;
                                break;
                            case InvoiceVoidOutcome.PendingConfirmation:
                                MessageBox.Show(
                                    this,
                                    "作廢請求已送出，但光貿尚未確認最終結果。\n\n目前狀態為「待確認」，系統不會盲目重送。",
                                    "作廢結果待確認",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Warning);
                                refreshAfterFlow = true;
                                break;
                            case InvoiceVoidOutcome.Rejected:
                                MessageBox.Show(this, voidResult.Message, "未送出/作廢未完成",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return;
                            case InvoiceVoidOutcome.RetryReady:
                                MessageBox.Show(
                                    this,
                                    "先前的待確認狀態已解除，本次沒有自動重送。\n系統將重新查詢這張發票的最新狀態。",
                                    "請重新確認",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information);
                                refreshAfterFlow = true;
                                break;
                        }
                    }
                }
                catch (EmployeeVoidAuthenticationDelayException error)
                {
                    MessageBox.Show(this, error.Message, "驗證暫停", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (InvalidOperationException error) when (
                    error.Message == "員工編號或密碼錯誤" ||
                    error.Message == "發票號碼不符，請重新確認")
                {
                    MessageBox.Show(this, error.Message, "驗證失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception error)
                {
                    MessageBox.Show(this, error.Message, "作廢未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                finally
                {
                    if (!IsDisposed) SetVoidBusy(false);
                }

                if (refreshAfterFlow) break;
            }
        }

        if (refreshAfterFlow && !IsDisposed) await RefreshAfterVoidAsync();
    }

    private async Task RefreshAfterVoidAsync()
    {
        SetVoidBusy(true);
        try
        {
            var fresh = await detailRefreshService.RefreshAsync(record, previewCancellation.Token);
            if (previewCancellation.IsCancellationRequested || IsDisposed) return;
            record = fresh;
            latestStatusUnknown = false;
            Text = $"發票詳細資訊－{record.InvoiceNumber}";
            PopulateDetails();
            RefreshPreviewAfterQuery();
        }
        catch (OperationCanceledException) when (previewCancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (IsDisposed) return;
            latestStatusUnknown = true;
            Text = "發票詳細資訊 (最新狀態未確認)";
            PopulateDetails();
            MessageBox.Show(
                this,
                "作廢流程已結束，但目前無法重新向光貿確認這張發票的最新資料。\n\n以下畫面可能不是最新狀態；在重新查詢成功前，作廢與折讓按鈕會停用。\n\n" + error.Message,
                "無法確認最新資料",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed) SetVoidBusy(false);
        }
    }

    private void RefreshPreviewAfterQuery()
    {
        if (paperInvoice)
        {
            var eligibility = service.GetInvoicePdfEligibility(record);
            if (!eligibility.Allowed)
            {
                paperPreview.Image = null;
                activePreview?.Dispose();
                activePreview = null;
                ShowUnavailablePreview(eligibility);
            }
            return;
        }

        var receiptFrame = FindTaggedControl(this, "carrier-receipt") as Panel;
        var receipt = receiptFrame?.Controls.OfType<PictureBox>().FirstOrDefault();
        if (receipt is null) return;
        var replacement = CarrierInvoicePreview.Render(record, repository.Settings.LoadOrCreate(), sellerCompanyName);
        var old = activePreview;
        activePreview = replacement;
        receipt.Image = replacement;
        old?.Dispose();
    }

    private async Task StartAllowanceAsync()
    {
        if (allowanceBusy || voidBusy) return;
        using var request = new AllowanceRequestForm();
        if (request.ShowDialog(this) != DialogResult.OK) return;

        SetAllowanceBusy(true);
        try
        {
            var result = await allowanceWorkflow.SubmitAsync(
                record,
                request.EmployeeNo,
                request.Password,
                request.Reason,
                request.TaxInclusiveAmount);
            MessageBox.Show(
                this,
                result.AlreadyQueued
                    ? result.Message
                    : "折讓申請已建立。\n\n請由管理員至「上傳問題」查看明細，並在光貿網站完成人工折讓後按「已解決」。",
                "折讓人工處理",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
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
            MessageBox.Show(this, error.Message, "折讓申請未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed) SetAllowanceBusy(false);
        }
    }

    private void SetVoidBusy(bool busy)
    {
        voidBusy = busy;
        UpdateActionAvailability();
    }

    private void SetAllowanceBusy(bool busy)
    {
        allowanceBusy = busy;
        UpdateActionAvailability();
    }

    private void UpdateActionAvailability()
    {
        var operationBusy = voidBusy || allowanceBusy;
        close.Enabled = !operationBusy;
        changePrinter.Enabled = !operationBusy;

        var pdfAllowed = !operationBusy && !latestStatusUnknown && service.GetInvoicePdfEligibility(record).Allowed;
        viewPdf.Enabled = pdfAllowed;
        printPdf.Enabled = pdfAllowed;

        var voidManualReview = voidWorkflow.ManualReviewFor(record) is not null;
        var allowanceReview = allowanceWorkflow.ManualReviewFor(record);
        var canOperateInvoice = CanShowVoidAction() && !latestStatusUnknown;

        voidInvoice.Visible = canOperateInvoice || voidManualReview;
        if (voidBusy)
        {
            voidInvoice.Text = "作廢確認中…";
            voidInvoice.Enabled = false;
        }
        else
        {
            voidInvoice.Text = voidManualReview ? "人工確認中" : "作廢";
            voidInvoice.Enabled = canOperateInvoice && !allowanceBusy && !voidManualReview && allowanceReview is null;
        }

        allowanceInvoice.Visible = canOperateInvoice || allowanceReview is not null;
        if (allowanceBusy)
        {
            allowanceInvoice.Text = "折讓申請中…";
            allowanceInvoice.Enabled = false;
        }
        else
        {
            allowanceInvoice.Text = allowanceReview is null
                ? "折讓"
                : allowanceReview.AwaitingConfirmation ? "折讓待確認" : "折讓處理中";
            allowanceInvoice.Enabled = canOperateInvoice && !voidBusy && allowanceReview is null && !voidManualReview;
        }
    }

    private static void CompactDetailAction(Button button)
    {
        button.Width = DetailActionButtonWidth;
        button.Margin = new Padding(3, 2, 3, 2);
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
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, InformationColumnWidth));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
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
            Margin = new Padding(0, 0, 6, 4),
            BackColor = SystemColors.Control,
        };
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        section.Controls.Add(new Label
        {
            Text = "發票資訊",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold),
            BackColor = SystemColors.Control,
            Margin = new Padding(6, 0, 0, 0),
        }, 0, 0);
        var scroller = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = Padding.Empty,
            Padding = new Padding(6, 0, 4, 0),
            BackColor = SystemColors.Control,
            BorderStyle = BorderStyle.FixedSingle,
        };
        scroller.Controls.Add(details);
        section.Controls.Add(scroller, 0, 1);
        return section;
    }

    private Control BuildPaperPreview(InvoicePdfEligibility eligibility)
    {
        previewMessageHost.Controls.Add(previewStatus);
        previewMessageHost.Controls.Add(retryPreview);
        retryPreview.BringToFront();
        a4PreviewFrame.Controls.Add(paperPreview);
        a4PreviewFrame.Controls.Add(previewMessageHost);
        previewMessageHost.BringToFront();
        paperPreviewHost.Controls.Add(a4PreviewFrame);
        if (!eligibility.Allowed) ShowUnavailablePreview(eligibility);
        return paperPreviewHost;
    }

    private void ShowUnavailablePreview(InvoicePdfEligibility eligibility)
    {
        previewStatus.Text = record.InvoiceState switch
        {
            InvoiceStates.Failed => "此發票開立失敗\r\n無法取得官方 PDF",
            InvoiceStates.Voided => "此發票已作廢\r\n無法取得官方 PDF",
            _ => string.IsNullOrWhiteSpace(eligibility.Reason) ? "目前無法取得官方 PDF" : eligibility.Reason,
        };
        retryPreview.Visible = false;
        previewMessageHost.Visible = true;
        LayoutPreviewMessage();
    }

    private void LayoutPreviewMessage()
    {
        if (!retryPreview.Visible) return;
        var x = Math.Max(0, (previewMessageHost.ClientSize.Width - retryPreview.PreferredSize.Width) / 2);
        var y = Math.Max(0, previewMessageHost.ClientSize.Height / 2 + 34);
        retryPreview.Location = new Point(x, y);
    }

    private void LayoutA4Preview()
    {
        const double a4Ratio = 210D / 297D;
        var availableWidth = Math.Max(1, paperPreviewHost.ClientSize.Width - 6);
        var availableHeight = Math.Max(1, paperPreviewHost.ClientSize.Height - 8);
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
        previewMessageHost.BringToFront();
    }

    private Control BuildCarrierContent()
    {
        activePreview = CarrierInvoicePreview.Render(record, repository.Settings.LoadOrCreate(), sellerCompanyName);
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
            Padding = new Padding(6),
            Margin = new Padding(4, 0, 4, 4),
            Tag = "carrier-receipt",
        };
        receiptFrame.Controls.Add(receipt);

        var items = CreateItemsGrid();
        var itemSection = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(4, 0, 4, 4),
            Tag = "carrier-items",
        };
        itemSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        itemSection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        itemSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
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
            Padding = new Padding(0, 3, 2, 0),
        }, 0, 2);

        var rightStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Tag = "carrier-right-stack",
        };
        rightStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rightStack.RowStyles.Add(new RowStyle(SizeType.Absolute, CarrierItemsSectionHeight));
        rightStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightStack.Controls.Add(itemSection, 0, 0);
        rightStack.Controls.Add(receiptFrame, 0, 1);

        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Tag = "carrier-preview",
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, InformationColumnWidth));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.Controls.Add(BuildInformationSection(), 0, 0);
        split.Controls.Add(rightStack, 1, 0);
        return split;
    }

    private DataGridView CreateItemsGrid()
    {
        var items = UiControls.Grid();
        items.ReadOnly = true;
        items.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        items.ScrollBars = ScrollBars.Vertical;
        items.Columns.Add(Column("品名", 180, fill: true));
        items.Columns.Add(Column("數量", 48, right: true));
        items.Columns.Add(Column("單價", 70, right: true));
        items.Columns.Add(Column("金額", 76, right: true));
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
        previewMessageHost.Visible = true;
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
            previewMessageHost.Visible = false;
        }
        catch (OperationCanceledException) when (previewCancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!IsDisposed)
            {
                previewStatus.Text = "預覽尚未取得：\r\n" + error.Message;
                retryPreview.Visible = true;
                previewMessageHost.Visible = true;
                LayoutPreviewMessage();
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
            using var viewer = new InvoicePdfViewerForm(document, Path.Combine(repository.CacheDirectory, "WebView2"));
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
        if (remembered.Length != 0 && InvoicePdfPrinter.IsInstalled(remembered) && InvoicePdfPrinter.CanDuplex(remembered))
            return remembered;
        if (remembered.Length != 0)
        {
            MessageBox.Show(
                this,
                $"先前設定的發票印表機「{remembered}」目前不存在或未回報雙面能力。\n直接列印只能使用支援雙面的印表機，請重新選擇；若要使用其他印表機，請改用「檢視 PDF」手動列印。",
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
        if (!InvoicePdfPrinter.InstalledPrinters().Any(InvoicePdfPrinter.CanDuplex))
        {
            MessageBox.Show(
                this,
                "Windows 目前沒有回報支援雙面的印表機。\n若要使用單面印表機，請先「檢視 PDF」再由 PDF Viewer 手動列印。",
                "沒有可用的雙面印表機",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return null;
        }
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
            printerStatus.Text = "尚未設定";
            printerStatus.ForeColor = Color.DimGray;
            return;
        }
        if (!InvoicePdfPrinter.IsInstalled(name) || !InvoicePdfPrinter.CanDuplex(name))
        {
            printerStatus.Text = name + " (不可直接列印)";
            printerStatus.ForeColor = Color.Firebrick;
            return;
        }
        printerStatus.Text = name;
        printerStatus.ForeColor = SystemColors.ControlText;
    }

    private void SetPdfBusy(bool busy, string action)
    {
        pdfBusy = busy;
        var allowed = !busy && !voidBusy && !allowanceBusy && !latestStatusUnknown && service.GetInvoicePdfEligibility(record).Allowed;
        viewPdf.Enabled = allowed;
        printPdf.Enabled = allowed;
        changePrinter.Enabled = !busy && !voidBusy && !allowanceBusy;
        viewPdf.Text = busy && action == "檢視" ? "取得 PDF 中…" : "檢視 PDF";
        printPdf.Text = busy && action == "列印" ? "列印中…" : "列印發票";
    }

    private void ConfigureDetails()
    {
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, InformationLabelWidth));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    }

    private void PopulateDetails()
    {
        details.SuspendLayout();
        try
        {
            foreach (Control child in details.Controls.Cast<Control>().ToArray())
            {
                details.Controls.Remove(child);
                if (!ReferenceEquals(child, printerStatus)) child.Dispose();
            }
            details.RowStyles.Clear();
            details.RowCount = 0;

            if (latestStatusUnknown)
            {
                var warning = ValueLabel("重新查詢失敗，以下資料可能不是最新狀態");
                warning.ForeColor = Color.Firebrick;
                warning.Font = new Font(Font, FontStyle.Bold);
                AddDetail("最新狀態", warning.Text, warning);
            }
            AddDetail("發票號碼", record.InvoiceNumber);
            AddDetail("開立時間", IssueTime(record), singleLine: true);
            AddDetail("訂單編號", record.OrderId);
            AddDetail("來源", record.Source);
            AddDetail("買受人", record.BuyerName);
            AddDetail("統一編號", record.BuyerIdentifier);
            AddDetail("發票金額", MoneyFormatter.Integer(record.Amount));
            AddDetail("使用環境", record.Environment == Environments.Production ? "正式" : "測試");
            AddDetail("交付方式", record.Delivery);
            AddInvoiceStateDetail();
            AddDetail("上傳狀態", record.UploadStatusText);
            AddDetail("最後確認", record.LastChecked);
            AddOptionalDetail("折讓紀錄", AllowanceSummary(record), SystemColors.ControlText);
            AddOptionalDetail("錯誤訊息", record.ErrorMessage, Color.Firebrick);
            AddOptionalDetail("總備註", record.MainRemark, SystemColors.ControlText);
            if (paperInvoice)
            {
                AddDetail("發票印表機", string.Empty, printerStatus);
                UpdatePrinterStatus();
            }
        }
        finally
        {
            details.ResumeLayout(true);
        }
    }

    private void AddInvoiceStateDetail()
    {
        if (record.InvoiceState != InvoiceStates.OpenedWaitingVoid)
        {
            var value = ValueLabel(record.InvoiceState);
            if (record.InvoiceState == InvoiceStates.Voided) value.ForeColor = Color.Firebrick;
            AddDetail("發票狀態", record.InvoiceState, value);
            return;
        }

        var row = details.RowCount++;
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.Controls.Add(FieldLabel("發票狀態"), 0, row);
        details.Controls.Add(WaitingVoidDetailValue(), 1, row);
        AddDetailSeparator();
    }

    private static Control WaitingVoidDetailValue()
    {
        var line = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = SystemColors.Control,
            Margin = Padding.Empty,
            Padding = new Padding(0, 5, 0, 5),
            MinimumSize = new Size(0, 30),
            Tag = WaitingVoidDetailTag,
        };
        line.Controls.Add(new Label
        {
            Text = InvoiceStates.Opened,
            AutoSize = true,
            ForeColor = SystemColors.ControlText,
            BackColor = SystemColors.Control,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        });
        line.Controls.Add(new Label
        {
            Text = WaitingVoidDetailText,
            AutoSize = true,
            ForeColor = SystemColors.ControlText,
            BackColor = Color.FromArgb(255, 235, 59),
            Margin = Padding.Empty,
            Padding = new Padding(2, 0, 2, 0),
            Tag = WaitingVoidHighlightTag,
        });
        return line;
    }

    private void AddDetail(string label, string value, Label? target = null, bool singleLine = false)
    {
        var row = details.RowCount++;
        details.RowStyles.Add(singleLine ? new RowStyle(SizeType.Absolute, 31) : new RowStyle(SizeType.AutoSize));
        details.Controls.Add(FieldLabel(label), 0, row);
        var text = target ?? ValueLabel(value, singleLine);
        if (target is not null) text.Text = value;
        details.Controls.Add(text, 1, row);
        AddDetailSeparator();
    }

    private void AddOptionalDetail(string label, string value, Color valueColor)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var row = details.RowCount++;
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.Controls.Add(FieldLabel(label), 0, row);
        var text = ValueLabel(value);
        text.ForeColor = valueColor;
        details.Controls.Add(text, 1, row);
        AddDetailSeparator();
    }

    private void AddDetailSeparator()
    {
        var row = details.RowCount++;
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        var line = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 1,
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(205, 205, 205),
        };
        details.Controls.Add(line, 0, row);
        details.SetColumnSpan(line, 2);
    }

    private static string AllowanceSummary(InvoiceRecord record)
    {
        var allowances = InvoiceAllowanceMetadata.ReadOfficial(record);
        if (allowances.Count == 0) return string.Empty;
        return string.Join("\r\n", allowances.Select(item =>
        {
            var amount = "?";
            try
            {
                amount = FixedDecimal.Add(
                    FixedDecimal.Parse(item.TotalAmount),
                    FixedDecimal.Parse(item.TaxAmount)).ToString();
            }
            catch (Exception error) when (error is FormatException or OverflowException)
            {
            }
            return $"{item.AllowanceNumber}｜{item.AllowanceDate}｜{item.InvoiceType}｜狀態 {item.InvoiceStatus}｜含稅 {amount}";
        }));
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
            InvoiceDate = "2026/09/16",
            InvoiceTime = "12:34:56",
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
                InvoiceDate = baseRecord.InvoiceDate,
                InvoiceTime = baseRecord.InvoiceTime,
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
            service,
            "志遠醫療器材行");
        carrier.PerformLayout();
        carrier.VerifyLayout(companyBuyer: false, carrier: true);

        using var waitingVoid = new RecordDetailForm(
            new InvoiceRecord
            {
                Id = "waiting-void-smoke",
                Environment = environment,
                InvoiceNumber = "EE87654321",
                InvoiceState = InvoiceStates.OpenedWaitingVoid,
                Delivery = "會員載具",
                CarrierType = "amego",
                CarrierId1 = "motmp_20260916003",
                Source = InvoiceSources.Mo,
                OrderId = "20260916003",
                InvoiceDate = "2026/09/16",
                InvoiceTime = "12:45:00",
                Amount = 105,
                UploadStatus = UploadStatuses.Complete,
                UploadStatusText = "完成",
                Items = baseRecord.Items,
            },
            repository,
            service,
            "志遠醫療器材行");
        waitingVoid.PerformLayout();
        var waitingState = FindTaggedControl(waitingVoid, WaitingVoidDetailTag) as FlowLayoutPanel;
        var waitingHighlight = FindTaggedControl(waitingVoid, WaitingVoidHighlightTag) as Label;
        if (waitingState is null || waitingHighlight is null ||
            waitingHighlight.Text != WaitingVoidDetailText ||
            waitingHighlight.BackColor != Color.FromArgb(255, 235, 59))
            throw new InvalidOperationException("等待發票作廢的詳細資料狀態未使用指定文字與黃底強調");

        using var voidedCarrier = new RecordDetailForm(
            new InvoiceRecord
            {
                Id = "voided-carrier-smoke",
                Environment = environment,
                InvoiceNumber = "DD87654321",
                InvoiceState = InvoiceStates.Voided,
                Delivery = "會員載具",
                CarrierType = "amego",
                CarrierId1 = "motmp_20260916002",
                Source = InvoiceSources.Mo,
                OrderId = "20260916002",
                InvoiceDate = "2026/09/16",
                InvoiceTime = "12:40:00",
                Amount = 105,
                Items = baseRecord.Items,
            },
            repository,
            service,
            "志遠醫療器材行");
        voidedCarrier.PerformLayout();
        voidedCarrier.VerifyLayout(companyBuyer: false, carrier: true);
        var voidedStateValue = voidedCarrier.details.Controls
            .OfType<Label>()
            .FirstOrDefault(label => label.Text == InvoiceStates.Voided);
        if (voidedStateValue is null || voidedStateValue.ForeColor != Color.Firebrick)
            throw new InvalidOperationException("已作廢發票詳細資訊的發票狀態未使用紅字強調");

        using var failed = new RecordDetailForm(
            new InvoiceRecord
            {
                Id = "failed-preview-smoke",
                Environment = environment,
                InvoiceNumber = "CC12345678",
                InvoiceState = InvoiceStates.Failed,
                Delivery = InvoiceService.DeliveryPaper,
                Source = InvoiceSources.Manual,
                OrderId = "20260917001",
                InvoiceDate = "2026/09/17",
                InvoiceTime = "21:29:34",
                Amount = 105,
                ErrorMessage = "API test failure",
                Items = baseRecord.Items,
            },
            repository,
            service);
        failed.PerformLayout();
        if (!failed.a4PreviewFrame.Controls.Contains(failed.previewMessageHost) ||
            !failed.previewMessageHost.Controls.Contains(failed.previewStatus) ||
            failed.previewMessageHost.BackColor != Color.White ||
            failed.previewStatus.TextAlign != ContentAlignment.MiddleCenter ||
            !failed.previewStatus.Text.Contains("開立失敗", StringComparison.Ordinal))
            throw new InvalidOperationException("開立失敗發票未在 A4 白紙中央顯示不可取得 PDF 狀態");

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

        using var reason = new VoidReasonForm();
        reason.PerformLayout();
        reason.VerifySmokeLayout();
        using var confirmPaper = new VoidConfirmationForm(paperInvoice: true);
        confirmPaper.PerformLayout();
        confirmPaper.VerifySmokeLayout();
        using var confirmCarrier = new VoidConfirmationForm(paperInvoice: false);
        confirmCarrier.PerformLayout();
        confirmCarrier.VerifySmokeLayout();
        using var allowance = new AllowanceRequestForm();
        allowance.PerformLayout();
        allowance.VerifySmokeLayout();
    }

    private void VerifyLayout(bool companyBuyer, bool carrier)
    {
        if (details.ColumnCount != 2 || details.RowCount < 24)
            throw new InvalidOperationException("發票詳細資訊未使用直向資訊與分隔線配置");
        if (ClientSize.Width != DetailWindowWidth)
            throw new InvalidOperationException("紙本與會員載具詳細資訊未使用一致的精簡視窗寬度");
        if (details.BackColor != SystemColors.Control)
            throw new InvalidOperationException("發票資訊區未沿用視窗灰底");
        if (!UiControls.HasLogicalSize(close, 100, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("關閉按鈕未使用核准尺寸");
        if (carrier)
        {
            if (paperInvoice || activePreview is null)
                throw new InvalidOperationException("會員載具未建立模擬發票預覽");
            var carrierRoot = FindTaggedControl(this, "carrier-preview") as TableLayoutPanel;
            var rightStack = FindTaggedControl(this, "carrier-right-stack") as TableLayoutPanel;
            var itemSection = FindTaggedControl(this, "carrier-items") as TableLayoutPanel;
            var receiptFrame = FindTaggedControl(this, "carrier-receipt") as Panel;
            var grid = itemSection?.Controls.OfType<DataGridView>().FirstOrDefault();
            if (carrierRoot is null || carrierRoot.ColumnCount != 2 ||
                rightStack is null || rightStack.RowCount != 2 || rightStack.RowStyles[0].SizeType != SizeType.Absolute ||
                Math.Abs(rightStack.RowStyles[0].Height - CarrierItemsSectionHeight) > 0.1F ||
                itemSection is null || receiptFrame is null || grid is null || grid.ScrollBars != ScrollBars.Vertical)
                throw new InvalidOperationException("會員載具右側未使用交易明細上、模擬發票下的固定版面");
            return;
        }
        if (!paperInvoice || !viewPdf.Enabled || !printPdf.Enabled)
            throw new InvalidOperationException("已開立紙本發票未開放檢視或列印");
        if (!UiControls.HasLogicalSize(viewPdf, DetailActionButtonWidth, UiControls.StandardButtonHeight) ||
            !UiControls.HasLogicalSize(printPdf, DetailActionButtonWidth, UiControls.StandardButtonHeight) ||
            !UiControls.HasLogicalSize(changePrinter, DetailActionButtonWidth, UiControls.StandardButtonHeight))
            throw new InvalidOperationException("紙本發票操作按鈕未使用詳細頁精簡尺寸");
        if (service.GetInvoicePdfEligibility(record).CompanyBuyer != companyBuyer)
            throw new InvalidOperationException("紙本發票買方類型判斷錯誤");
        if (!paperPreviewHost.Controls.Contains(a4PreviewFrame))
            throw new InvalidOperationException("紙本預覽未使用 A4 比例容器");
        if (pdfBusy)
            throw new InvalidOperationException("紙本詳細資訊初始狀態不應處於 PDF 忙碌狀態");
    }

    private static Control? FindTaggedControl(Control root, string tag)
    {
        foreach (Control child in root.Controls)
        {
            if (string.Equals(child.Tag as string, tag, StringComparison.Ordinal)) return child;
            var nested = FindTaggedControl(child, tag);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.TopLeft,
        ForeColor = Color.DimGray,
        BackColor = SystemColors.Control,
        Margin = Padding.Empty,
        Padding = new Padding(0, 5, 4, 5),
        MinimumSize = new Size(0, 30),
        AutoEllipsis = true,
    };

    private static Label ValueLabel(string value, bool singleLine = false) => new()
    {
        Text = value,
        AutoSize = !singleLine,
        Dock = DockStyle.Fill,
        TextAlign = singleLine ? ContentAlignment.MiddleLeft : ContentAlignment.TopLeft,
        ForeColor = SystemColors.ControlText,
        BackColor = SystemColors.Control,
        Margin = Padding.Empty,
        Padding = new Padding(0, 5, 0, 5),
        MinimumSize = new Size(0, 30),
        MaximumSize = singleLine ? Size.Empty : new Size(InformationValueMaxWidth, 0),
        AutoEllipsis = singleLine,
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
        }
        base.Dispose(disposing);
    }
}
