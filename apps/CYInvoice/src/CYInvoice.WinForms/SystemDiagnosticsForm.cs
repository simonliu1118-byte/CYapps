using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Text;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;
using Microsoft.Web.WebView2.Core;

namespace CYInvoice.WinForms;

internal sealed class SystemDiagnosticsForm : Form
{
    private const string DailySyncScope = "daily-two-period";
    private readonly LocalRepository repository;
    private readonly InvoiceService invoiceService;
    private readonly AmegoConnectivityProbe connectivityProbe = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly ListView list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        HideSelection = false,
        MultiSelect = false,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
    };
    private readonly Button refreshButton = UiControls.StandardButton("重新檢查");
    private readonly Button copyButton = UiControls.StandardButton("複製診斷資訊");
    private readonly Button closeButton = UiControls.StandardButton("關閉");
    private readonly bool startupSmokeTest;
    private bool refreshing;

    public SystemDiagnosticsForm(LocalRepository repository, bool startupSmokeTest = false)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        invoiceService = new InvoiceService(repository);
        this.startupSmokeTest = startupSmokeTest;

        Text = "系統診斷";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 520);
        MinimumSize = new Size(680, 440);
        Font = new Font("Microsoft JhengHei UI", 10F);
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowIcon = false;
        BackColor = SystemColors.Window;

        BuildLayout();
        FormClosed += (_, _) => lifetime.Cancel();
        if (startupSmokeTest)
        {
            PopulateLocalDiagnostics(includeNetworkPlaceholder: true);
        }
        else
        {
            Shown += async (_, _) => await RefreshDiagnosticsAsync();
        }
    }

    private void BuildLayout()
    {
        list.Columns.Add("項目", 170, HorizontalAlignment.Left);
        list.Columns.Add("狀態", 90, HorizontalAlignment.Left);
        list.Columns.Add("說明", 450, HorizontalAlignment.Left);

        var description = new Label
        {
            Text = "只顯示維護所需狀態，不顯示 App Key、密碼、發票內容或完整本機路徑。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(80, 80, 80),
            Margin = new Padding(3, 0, 3, 4),
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Padding = new Padding(0, 4, 0, 0),
        };
        actions.Controls.Add(refreshButton);
        actions.Controls.Add(copyButton);
        actions.Controls.Add(closeButton);
        actions.SizeChanged += (_, _) => CenterActions(actions);

        refreshButton.Click += async (_, _) => await RefreshDiagnosticsAsync();
        copyButton.Click += (_, _) => CopyDiagnostics();
        closeButton.Click += (_, _) => Close();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.Controls.Add(description, 0, 0);
        root.Controls.Add(list, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
        CenterActions(actions);
    }

    private static void CenterActions(FlowLayoutPanel actions)
    {
        var width = actions.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
        actions.Padding = new Padding(Math.Max(0, (actions.ClientSize.Width - width) / 2), 4, 0, 0);
    }

    private async Task RefreshDiagnosticsAsync()
    {
        if (refreshing) return;
        refreshing = true;
        refreshButton.Enabled = false;
        copyButton.Enabled = false;
        try
        {
            PopulateLocalDiagnostics(includeNetworkPlaceholder: false);
            await CheckAmegoConnectivityAsync(lifetime.Token);
            await CheckAmegoAccountAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            refreshing = false;
            if (!IsDisposed)
            {
                refreshButton.Enabled = true;
                copyButton.Enabled = list.Items.Count != 0;
            }
        }
    }

    private void PopulateLocalDiagnostics(bool includeNetworkPlaceholder)
    {
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            AddRow("程式版本", "資訊", $"CYInvoice V{ApplicationVersion.Read()}");
            AddRow("Windows / .NET", "資訊", $"{RuntimeInformation.OSDescription}; .NET {Environment.Version}; {(Environment.Is64BitProcess ? "x64" : "非 x64")}");

            var settings = repository.Settings.LoadOrCreate();
            var environmentText = settings.Environment == Environments.Production ? "正式環境" : "測試環境";
            AddRow("使用環境", "資訊", environmentText);
            AddCompanyConfiguration(settings);
            AddDatabaseStatus(settings);
            AddSyncStatus(settings);
            AddPendingStatus(settings);
            AddWebView2Status();
            AddExcelStatus();
            AddPrinterStatus(settings);
            AddCacheStatus();

            if (includeNetworkPlaceholder)
            {
                AddRow("光貿服務", "略過", "Startup smoke 不進行外部網路連線");
                AddRow("光貿帳號", "略過", "Startup smoke 不進行帳號 API 驗證");
            }
        }
        finally
        {
            list.EndUpdate();
        }
        copyButton.Enabled = list.Items.Count != 0;
    }

    private void AddCompanyConfiguration(Settings settings)
    {
        if (settings.Environment != Environments.Production)
        {
            AddRow("公司 / API 設定", "正常", $"光貿測試設定 {MaskBan(AmegoDefaults.TestInvoice)}");
            return;
        }

        var invoice = settings.ProductionInvoice.Trim();
        if (invoice.Length != 8 || !invoice.All(char.IsAsciiDigit))
        {
            AddRow("公司 / API 設定", "異常", "正式公司統編尚未正確設定");
            return;
        }
        if (settings.ProductionAppKeyEncrypted.Trim().Length == 0)
        {
            AddRow("公司 / API 設定", "異常", $"{MaskBan(invoice)}; App Key 未設定");
            return;
        }
        try
        {
            var key = repository.Settings.ProductionAppKey(settings);
            AddRow("公司 / API 設定", key.Trim().Length == 0 ? "異常" : "正常",
                key.Trim().Length == 0 ? $"{MaskBan(invoice)}; App Key 為空白" : $"{MaskBan(invoice)}; App Key 已設定且可解密");
        }
        catch (Exception)
        {
            AddRow("公司 / API 設定", "異常", $"{MaskBan(invoice)}; App Key 無法解密");
        }
    }

    private void AddDatabaseStatus(Settings settings)
    {
        try
        {
            var result = SqliteBootstrapper.EnsureMigrated(repository.DataDirectory, settings.ProductionInvoice);
            AddRow("本機資料庫", "正常", $"SQLite quick_check 正常; 發票 {result.InvoiceCount:N0} 筆; 明細 {result.ItemCount:N0} 筆");
        }
        catch (Exception error)
        {
            AddRow("本機資料庫", "異常", ShortError(error));
        }
    }

    private void AddSyncStatus(Settings settings)
    {
        var seller = CurrentSellerInvoice(settings);
        if (seller.Length == 0)
        {
            AddRow("每日完整同步", "注意", "目前公司設定不足，無法讀取同步狀態");
            AddRow("最近官方回查", "注意", "目前公司設定不足");
            return;
        }

        var accountKey = settings.Environment + "|" + seller;
        try
        {
            var last = new InvoiceSyncStateStore(repository.DataDirectory).LastSuccess(accountKey, DailySyncScope);
            AddRow("每日完整同步", last is null ? "注意" : "正常",
                last is null ? "尚無成功紀錄" : $"最後成功 {last.Value.ToLocalTime():yyyy/MM/dd HH:mm:ss}");
        }
        catch (Exception error)
        {
            AddRow("每日完整同步", "異常", ShortError(error));
        }

        try
        {
            var latest = CurrentAccountRecords(settings)
                .Select(record => record.LastChecked.Trim())
                .Where(value => value.Length != 0)
                .OrderByDescending(value => value, StringComparer.Ordinal)
                .FirstOrDefault();
            AddRow("最近官方回查", latest is null ? "注意" : "正常",
                latest is null ? "尚無 invoice_query / 同步回查紀錄" : latest);
        }
        catch (Exception error)
        {
            AddRow("最近官方回查", "異常", ShortError(error));
        }
    }

    private void AddPendingStatus(Settings settings)
    {
        var seller = CurrentSellerInvoice(settings);
        if (seller.Length == 0)
        {
            AddRow("待處理項目", "注意", "目前公司設定不足");
            return;
        }

        try
        {
            var accountKey = settings.Environment + "|" + seller;
            var unresolved = new InvoiceSyncIssueStore(repository.DataDirectory).Unresolved(accountKey).Count;
            var failed = CurrentAccountRecords(settings).Count(record => record.InvoiceState == InvoiceStates.Failed);
            AddRow("待處理項目", unresolved == 0 && failed == 0 ? "正常" : "注意",
                $"未解決待辦 / 同步問題 {unresolved:N0} 筆; 開立失敗 {failed:N0} 筆");
        }
        catch (Exception error)
        {
            AddRow("待處理項目", "異常", ShortError(error));
        }
    }

    private void AddWebView2Status()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            AddRow("WebView2 Runtime", string.IsNullOrWhiteSpace(version) ? "異常" : "正常",
                string.IsNullOrWhiteSpace(version) ? "找不到 WebView2 Runtime" : version.Trim());
        }
        catch (Exception error)
        {
            AddRow("WebView2 Runtime", "異常", ShortError(error));
        }
    }

    private void AddExcelStatus()
    {
        try
        {
            var excelType = Type.GetTypeFromProgID("Excel.Application", throwOnError: false);
            AddRow("Microsoft Excel", excelType is null ? "注意" : "正常",
                excelType is null ? "找不到 Excel COM 註冊; MO 密碼保護 .xls 無法使用 Excel COM" : "Excel COM 已註冊");
        }
        catch (Exception error)
        {
            AddRow("Microsoft Excel", "注意", ShortError(error));
        }
    }

    private void AddPrinterStatus(Settings settings)
    {
        var configured = settings.InvoicePrinterName.Trim();
        if (configured.Length == 0)
        {
            AddRow("發票印表機", "注意", "尚未指定發票印表機");
            return;
        }
        try
        {
            var installed = PrinterSettings.InstalledPrinters.Cast<string>()
                .Any(name => string.Equals(name, configured, StringComparison.OrdinalIgnoreCase));
            AddRow("發票印表機", installed ? "正常" : "注意",
                installed ? "已設定且 Windows 可找到此印表機" : "已設定，但 Windows 目前找不到此印表機");
        }
        catch (Exception error)
        {
            AddRow("發票印表機", "注意", ShortError(error));
        }
    }

    private void AddCacheStatus()
    {
        try
        {
            long bytes = 0;
            if (Directory.Exists(repository.CacheDirectory))
            {
                foreach (var path in Directory.EnumerateFiles(repository.CacheDirectory, "*", SearchOption.AllDirectories))
                    bytes += new FileInfo(path).Length;
            }
            AddRow("本機 Cache", "資訊", $"{bytes / 1024d / 1024d:N1} MB");
        }
        catch (Exception error)
        {
            AddRow("本機 Cache", "注意", ShortError(error));
        }
    }

    private async Task CheckAmegoConnectivityAsync(CancellationToken cancellationToken)
    {
        try
        {
            await connectivityProbe.CheckAsync(cancellationToken);
            AddRow("光貿服務", "正常", "API 時間服務可連線");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            AddRow("光貿服務", "異常", ShortError(error));
        }
    }

    private async Task CheckAmegoAccountAsync(CancellationToken cancellationToken)
    {
        var settings = repository.Settings.LoadOrCreate();
        if (settings.Environment == Environments.Production &&
            (settings.ProductionInvoice.Trim().Length != 8 || settings.ProductionAppKeyEncrypted.Trim().Length == 0))
        {
            AddRow("光貿帳號", "異常", "正式環境公司統編或 App Key 尚未設定完整");
            return;
        }
        try
        {
            _ = await invoiceService.HealthCheckAsync(cancellationToken);
            AddRow("光貿帳號", "正常", "目前環境帳號 API 驗證成功");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            AddRow("光貿帳號", "異常", ShortError(error));
        }
    }

    private IEnumerable<InvoiceRecord> CurrentAccountRecords(Settings settings)
    {
        var seller = CurrentSellerInvoice(settings);
        return repository.Invoices.LoadOrCreate().Where(record =>
            string.Equals(record.Environment, settings.Environment, StringComparison.Ordinal) &&
            (record.SellerInvoice.Trim().Length == 0 || string.Equals(record.SellerInvoice.Trim(), seller, StringComparison.Ordinal)));
    }

    private static string CurrentSellerInvoice(Settings settings) =>
        settings.Environment == Environments.Test ? AmegoDefaults.TestInvoice : settings.ProductionInvoice.Trim();

    private void CopyDiagnostics()
    {
        if (list.Items.Count == 0) return;
        var builder = new StringBuilder();
        builder.AppendLine("CYInvoice 系統診斷");
        builder.AppendLine($"產生時間：{DateTimeOffset.Now:yyyy/MM/dd HH:mm:ss zzz}");
        foreach (ListViewItem item in list.Items)
        {
            var detail = item.SubItems.Count > 2 ? item.SubItems[2].Text : string.Empty;
            builder.Append(item.Text).Append("：").Append(item.SubItems[1].Text);
            if (detail.Length != 0) builder.Append(" - ").Append(detail);
            builder.AppendLine();
        }
        try
        {
            Clipboard.SetText(builder.ToString());
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "無法複製診斷資訊：" + error.Message, "系統診斷", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void AddRow(string item, string status, string detail)
    {
        var row = new ListViewItem(item);
        row.SubItems.Add(status);
        row.SubItems.Add(detail.Trim());
        list.Items.Add(row);
    }

    private static string MaskBan(string value)
    {
        value = value.Trim();
        return value.Length == 8 && value.All(char.IsAsciiDigit) ? value[..4] + "****" : "未設定";
    }

    private static string ShortError(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            var message = current.Message.Trim();
            if (message.Length != 0) return message.Replace(Environment.NewLine, " ", StringComparison.Ordinal);
        }
        return error.GetType().Name;
    }

    internal void VerifySmokeLayout()
    {
        if (Text != "系統診斷" || ShowIcon)
            throw new InvalidOperationException("系統診斷視窗標題或圖示不正確");
        if (list.View != View.Details || list.Columns.Count != 3 || list.Columns[0].Text != "項目" || list.Columns[1].Text != "狀態")
            throw new InvalidOperationException("系統診斷清單結構不正確");
        if (refreshButton.Text != "重新檢查" || copyButton.Text != "複製診斷資訊" || closeButton.Text != "關閉")
            throw new InvalidOperationException("系統診斷操作按鈕不正確");
        if (!startupSmokeTest || list.Items.Count < 8)
            throw new InvalidOperationException("系統診斷 smoke 資料未建立");
        if (list.Items.Cast<ListViewItem>().Any(item => item.SubItems.Cast<ListViewItem.ListViewSubItem>().Any(sub => sub.Text.Contains("App Key:", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("系統診斷不得顯示 App Key 內容");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) lifetime.Dispose();
        base.Dispose(disposing);
    }
}
