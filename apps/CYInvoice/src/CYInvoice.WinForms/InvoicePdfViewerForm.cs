using CYInvoice.Core.Invoicing;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CYInvoice.WinForms;

internal sealed class InvoicePdfViewerForm : Form
{
    private readonly InvoicePdfDocument document;
    private readonly string userDataDirectory;
    private readonly WebView2 webView = new() { Dock = DockStyle.Fill };

    public InvoicePdfViewerForm(InvoicePdfDocument document, string userDataDirectory)
    {
        this.document = document;
        this.userDataDirectory = userDataDirectory;
        Text = $"官方發票 PDF－{document.InvoiceNumber}－{document.Style.Name}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1180, 860);
        MinimumSize = new Size(820, 600);
        ShowInTaskbar = false;
        KeyPreview = true;
        Icon = ApplicationIcon.Load();
        Controls.Add(webView);
        Shown += async (_, _) => await InitializeViewerAsync();
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Escape) Close();
        };
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
            webView.ZoomFactor = 0.85D;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            var source = new Uri(document.Path).AbsoluteUri;
            webView.CoreWebView2.NavigationStarting += (_, eventArgs) =>
            {
                if (!string.Equals(eventArgs.Uri, source, StringComparison.OrdinalIgnoreCase)) eventArgs.Cancel = true;
            };
            webView.Source = new Uri(source);
        }
        catch (Exception error)
        {
            MessageBox.Show(
                this,
                $"PDF 內嵌檢視無法啟動。請確認 Windows 已安裝 Microsoft Edge WebView2 Runtime。\n\n{error.Message}",
                "PDF 檢視失敗",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    internal void VerifySmokeLayout()
    {
        if (Controls.Count != 1 || !ReferenceEquals(Controls[0], webView))
            throw new InvalidOperationException("PDF Viewer 仍包含額外自製工具列");
        if (webView.Dock != DockStyle.Fill)
            throw new InvalidOperationException("PDF Viewer 未填滿可視區域");
        if (ClientSize.Height < 800)
            throw new InvalidOperationException("PDF Viewer 高度不足以顯示完整 A4 頁面");
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
