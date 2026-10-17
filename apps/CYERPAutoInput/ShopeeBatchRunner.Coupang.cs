namespace CYERPAutoInput;

/// <summary>
/// 酷澎 (Coupang Wing). For now only the step the user asked to try first (2026-10-15): on the
/// 我的訂單 page the user opened in Chrome (正在配送, user 2026-10-15), every order ticked and 「下載Excel」 pressed
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
        // 正在配送 (user, 2026-10-15): tick select-all, check 已選 = 共, then 下載Excel.
        var (page, download) = await WaitOrNullAsync(8, s => CoupangScreens.DownloadExcel(s.Boxes));
        if (page is null || download is null)
        {
            using var shown = await ReadAsync();
            _log.Info("coupang-batch", $"no 下載Excel; page: {string.Join(" | ", shown.Boxes.Select(b => ShopeeScreenshot.Compact(b.Text)).Where(t => t.Length is > 0 and <= 24).Take(80))}");
            page?.Dispose();
            throw new ShopeeBatchStop("頁面上找不到「下載Excel」：請打開酷澎「我的訂單 › 正在配送」並讓 Chrome 在最前面。");
        }
        using (page)
        {
            var header = CoupangScreens.OrderHeader(page.Boxes, download) ?? throw new ShopeeBatchStop("找不到訂單列表的「訂單編號」欄。");
            var (total, selected) = CoupangScreens.Counts(page.Boxes);
            _log.Info("coupang-batch", $"list total={total} selected={selected}");
            if (total == 0) throw new ShopeeBatchStop("這個清單沒有訂單（共 0 筆）。");
            if (selected != total) Click(page, CoupangScreens.SelectAll(page.Boxes, header), "全選");
        }
        var (ticked, counts) = await WaitOrNullAsync(5, s => CoupangScreens.Counts(s.Boxes) is { Total: > 0 } c && c.Selected == c.Total ? (object)c : null);
        if (ticked is null)
        {
            using var shown = await ReadAsync();
            var (t, sel) = CoupangScreens.Counts(shown.Boxes);
            throw new ShopeeBatchStop($"全選沒有勾起來（共 {t?.ToString() ?? "?"} 筆、已選 {sel?.ToString() ?? "?"} 筆）。");
        }
        using (ticked)
        {
            _log.Info("coupang-batch", $"all ticked {counts}");
            var button = CoupangScreens.DownloadExcel(ticked.Boxes) ?? download;
            Click(ticked, button, "下載Excel");
        }
        var (error, files) = await SaveFromDialogAsync(folder, "下載Excel", 60);
        if (error is not null) throw new ShopeeBatchStop($"酷澎訂單 Excel 存不下來：{error}。");
        var file = files.FirstOrDefault(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            ?? throw new ShopeeBatchStop("酷澎「下載Excel」後找不到 .xlsx 檔。");
        _log.Info("coupang-batch", "excel saved");
        return file;
    }
}
