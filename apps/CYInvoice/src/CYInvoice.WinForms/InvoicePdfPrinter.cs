using System.Drawing.Printing;
using CYInvoice.Core.Invoicing;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CYInvoice.WinForms;

internal static class InvoicePdfPrinter
{
    private const double A4WidthInches = 8.26772D;
    private const double A4HeightInches = 11.69291D;
    private const double A5WidthInches = 5.82677D;
    private const double A5HeightInches = 8.26772D;

    public static IReadOnlyList<string> InstalledPrinters()
    {
        try
        {
            return PrinterSettings.InstalledPrinters.Cast<string>()
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static bool IsInstalled(string printerName) =>
        printerName.Length != 0 && InstalledPrinters().Any(name =>
            string.Equals(name, printerName, StringComparison.OrdinalIgnoreCase));

    public static bool CanDuplex(string printerName)
    {
        try
        {
            var settings = new PrinterSettings { PrinterName = printerName };
            return settings.IsValid && settings.CanDuplex;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static async Task PrintAsync(
        InvoicePdfDocument document,
        string userDataDirectory,
        string printerName,
        bool consumerPaper,
        CancellationToken cancellationToken)
    {
        if (!IsInstalled(printerName))
            throw new InvalidOperationException("先前設定的發票印表機已不存在，請重新選擇印表機。");
        if (!File.Exists(document.Path))
            throw new FileNotFoundException("找不到要列印的官方發票 PDF。", document.Path);

        Directory.CreateDirectory(userDataDirectory);
        using var host = new Form
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            ClientSize = new Size(16, 16),
            Opacity = 0D,
        };
        using var webView = new WebView2 { Dock = DockStyle.Fill };
        host.Controls.Add(webView);
        host.Show();
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataDirectory);
            await webView.EnsureCoreWebView2Async(environment);
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            await NavigateAsync(webView, new Uri(document.Path).AbsoluteUri, cancellationToken);

            var settings = webView.CoreWebView2.Environment.CreatePrintSettings();
            settings.PrinterName = printerName;
            settings.Orientation = CoreWebView2PrintOrientation.Portrait;
            settings.MediaSize = CoreWebView2PrintMediaSize.Custom;
            if (document.Style.Code == InvoicePdfStyles.A5.Code)
            {
                settings.PageWidth = A5WidthInches;
                settings.PageHeight = A5HeightInches;
            }
            else
            {
                settings.PageWidth = A4WidthInches;
                settings.PageHeight = A4HeightInches;
            }
            settings.MarginTop = 0D;
            settings.MarginBottom = 0D;
            settings.MarginLeft = 0D;
            settings.MarginRight = 0D;
            settings.ScaleFactor = 1D;
            settings.PagesPerSide = 1;
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            settings.Duplex = consumerPaper
                ? (CanDuplex(printerName) ? CoreWebView2PrintDuplex.TwoSidedLongEdge : CoreWebView2PrintDuplex.OneSided)
                : CoreWebView2PrintDuplex.Default;

            var result = await webView.CoreWebView2.PrintAsync(settings);
            if (result == CoreWebView2PrintStatus.Succeeded) return;
            if (result == CoreWebView2PrintStatus.PrinterUnavailable)
                throw new InvalidOperationException($"印表機「{printerName}」目前無法使用，請檢查連線或重新選擇印表機。");
            throw new InvalidOperationException("Windows 無法完成這次發票列印工作。");
        }
        finally
        {
            host.Close();
        }
    }

    private static async Task NavigateAsync(WebView2 webView, string source, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
        {
            if (eventArgs.IsSuccess) completion.TrySetResult(true);
            else completion.TrySetException(new InvalidOperationException($"官方發票 PDF 載入失敗：{eventArgs.WebErrorStatus}"));
        }
        webView.CoreWebView2.NavigationCompleted += Completed;
        try
        {
            using var registration = timeout.Token.Register(() => completion.TrySetCanceled(timeout.Token));
            webView.Source = new Uri(source);
            await completion.Task;
            await Task.Delay(250, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("官方發票 PDF 載入逾時，尚未送出列印工作。");
        }
        finally
        {
            webView.CoreWebView2.NavigationCompleted -= Completed;
        }
    }
}
