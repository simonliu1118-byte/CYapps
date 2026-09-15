using CYInvoice.Core.Invoicing;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CYInvoice.WinForms;

internal sealed class InvoicePdfViewerForm : Form
{
    private const double MinimumZoom = 0.5;
    private const double MaximumZoom = 3.0;
    private readonly InvoicePdfDocument document;
    private readonly string userDataDirectory;
    private readonly WebView2 webView = new() { Dock = DockStyle.Fill };
    private readonly Label status = new()
    {
        Text = "正在載入 PDF…",
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(8, 10, 12, 0),
    };
    private readonly Button zoomOut = UiControls.StandardButton("縮小");
    private readonly Button zoomReset = UiControls.StandardButton("100%");
    private readonly Button zoomIn = UiControls.StandardButton("放大");
    private readonly Button download = UiControls.StandardButton("下載");
    private readonly Button print = UiControls.StandardButton("列印");

    public InvoicePdfViewerForm(InvoicePdfDocument document, string userDataDirectory)
    {
        this.document = document;
        this.userDataDirectory = userDataDirectory;
        Text = $"官方發票 PDF－{document.InvoiceNumber}－{document.Style.Name}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1100, 760);
        MinimumSize = new Size(820, 600);
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        Icon = ApplicationIcon.Load();

        foreach (var button in new[] { zoomOut, zoomReset, zoomIn, print }) button.Enabled = false;
        zoomOut.Click += (_, _) => ChangeZoom(-0.1);
        zoomReset.Click += (_, _) => SetZoom(1.0);
        zoomIn.Click += (_, _) => ChangeZoom(0.1);
        download.Click += (_, _) => SaveCopy();
        print.Click += (_, _) => webView.CoreWebView2?.ShowPrintUI(CoreWebView2PrintDialogKind.System);
        var close = UiControls.StandardButton("關閉");
        close.Click += (_, _) => Close();

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(6, 5, 6, 4),
        };
        toolbar.Controls.AddRange([zoomOut, zoomReset, zoomIn, download, print, close, status]);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(webView, 0, 1);
        Controls.Add(root);
        CancelButton = close;
        Shown += async (_, _) => await InitializeViewerAsync();
    }

    private async Task InitializeViewerAsync()
    {
        try
        {
            Directory.CreateDirectory(userDataDirectory);
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataDirectory);
            await webView.EnsureCoreWebView2Async(environment);
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            var source = new Uri(document.Path).AbsoluteUri;
            webView.CoreWebView2.NavigationStarting += (_, eventArgs) =>
            {
                if (!string.Equals(eventArgs.Uri, source, StringComparison.OrdinalIgnoreCase)) eventArgs.Cancel = true;
            };
            webView.Source = new Uri(source);
            SetZoom(1.0);
            foreach (var button in new[] { zoomOut, zoomReset, zoomIn, print }) button.Enabled = true;
            status.Text = document.FromCache ? "當日快取" : "已下載官方檔案";
        }
        catch (Exception error)
        {
            status.Text = "PDF 內嵌檢視無法啟動";
            MessageBox.Show(
                this,
                $"PDF 內嵌檢視無法啟動。請確認 Windows 已安裝 Microsoft Edge WebView2 Runtime。\n\n{error.Message}\n\n仍可使用「下載」保存 PDF。",
                "PDF 檢視失敗",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ChangeZoom(double delta) => SetZoom(webView.ZoomFactor + delta);

    private void SetZoom(double value)
    {
        value = Math.Clamp(value, MinimumZoom, MaximumZoom);
        webView.ZoomFactor = value;
        zoomReset.Text = $"{value:P0}";
        zoomOut.Enabled = value > MinimumZoom;
        zoomIn.Enabled = value < MaximumZoom;
    }

    private void SaveCopy()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "下載發票 PDF",
            Filter = "PDF 檔案 (*.pdf)|*.pdf",
            DefaultExt = "pdf",
            AddExtension = true,
            FileName = $"{document.InvoiceNumber}_{document.Style.Code}.pdf",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.Copy(document.Path, dialog.FileName, overwrite: true);
            MessageBox.Show(this, "PDF 已下載。", "下載完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "PDF 下載失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal void VerifySmokeLayout()
    {
        foreach (var button in new[] { zoomOut, zoomReset, zoomIn, download, print })
            if (!UiControls.HasLogicalSize(button, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight))
                throw new InvalidOperationException("PDF Viewer 按鈕未使用標準尺寸");
        if (webView.Dock != DockStyle.Fill) throw new InvalidOperationException("PDF Viewer 未填滿可視區域");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            webView.Dispose();
            Icon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
