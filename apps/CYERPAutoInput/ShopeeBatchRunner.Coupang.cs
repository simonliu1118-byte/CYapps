namespace CYERPAutoInput;

/// <summary>
/// 酷澎 (Coupang Wing). For now only the step the user asked to try first (2026-10-15): on the
/// 我的訂單 page the user opened in Chrome (配送指示 or another list), 「下載Excel」 is pressed
/// and the file saved into the run's Temp folder; the rest (CYInvoice, printing, ERP) is the
/// caller's. The whole 全自動 (確認訂單, 安排配送) waits for real orders to walk through.
/// </summary>
internal sealed partial class ShopeeBatchRunner
{
    /// <summary>The front Chrome window's 「下載Excel」 pressed and the .xlsx saved into <paramref name="folder"/>.</summary>
    public async Task<string> DownloadCoupangExcelAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        _window = ChromeWindows().FirstOrDefault();
        if (_window == 0) throw new ShopeeBatchStop("找不到 Chrome 視窗：請先在 Chrome 打開酷澎「我的訂單」頁面。");
        _pageTabs = true;
        Front();
        await Task.Delay(500, _token);
        var (page, button) = await WaitAsync("酷澎「下載Excel」", 20,
            s => s.Boxes.Where(b => ShopeeScreenshot.Compact(b.Text).Equals("下載Excel", StringComparison.OrdinalIgnoreCase)).OrderBy(b => b.Top).FirstOrDefault());
        using (page) Click(page, button, "下載Excel");
        var (error, files) = await SaveFromDialogAsync(folder, "下載Excel", 60);
        if (error is not null) throw new ShopeeBatchStop($"酷澎訂單 Excel 存不下來：{error}。");
        var file = files.FirstOrDefault(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            ?? throw new ShopeeBatchStop("酷澎「下載Excel」後找不到 .xlsx 檔。");
        _log.Info("coupang-batch", "excel saved");
        return file;
    }
}
