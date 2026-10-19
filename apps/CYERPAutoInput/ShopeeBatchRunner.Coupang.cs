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

    /// <summary>Wing 我的訂單 (user's screenshot, 2026-10-19).</summary>
    public const string CoupangUrl = "https://wing.coupang.com/tenants/sfl-portal/delivery/management";

    /// <summary>
    /// 酷澎 全自動, its first part (user, 2026-10-19 screenshots): in a Chrome window of its own, Wing
    /// 我的訂單 › 訂單確認: tick every listed order › 確認訂單 › 「確認接受訂單」 must name exactly those
    /// orders › 確認 › 「配送處理完成。」 › 確認. The page does not reload by itself, so it is reloaded
    /// and the round repeats while 訂單確認 still counts orders (a list shows ten at a time). Then
    /// 配送準備 is opened and read for the log. Returns the accepted 訂單編號.
    /// </summary>
    public async Task<List<string>> ConfirmCoupangOrdersAsync(Action<string> status)
    {
        _pageTabs = true;
        status("酷澎全自動：開啟 Chrome…");
        await OpenWindowAsync(CoupangUrl);
        var accepted = new List<string>();
        for (var round = 1; ; round++)
        {
            var (page, card) = await WaitAsync("酷澎「我的訂單」的訂單狀態", 40, s => CoupangScreens.StatusCard(s.Boxes, "訂單確認") is { } c ? (object)c : null);
            var (label, count) = ((OcrBox, int))card;
            _log.Info("coupang-batch", $"訂單確認 count={count} round={round}");
            if (count == 0) { page.Dispose(); break; }
            if (round > 10) { page.Dispose(); throw new ShopeeBatchStop($"確認了 {accepted.Count} 筆後「訂單確認」仍有 {count} 筆，請人工確認。"); }
            using (page) Click(page, label, "訂單確認");
            status($"酷澎全自動：確認訂單（已確認 {accepted.Count} 筆）…");

            var (list, header) = await WaitAsync("「待確認訂單清單」", 20, s => CoupangScreens.ListHeader(s.Boxes, "待確認訂單清單") is { } h && CoupangScreens.ListedOrders(s.Boxes, h).Count > 0 ? h : null);
            List<string> listed;
            using (list)
            {
                listed = CoupangScreens.ListedOrders(list.Boxes, header);
                var (_, selected) = CoupangScreens.Counts(list.Boxes);
                _log.Info("coupang-batch", $"待確認 listed={listed.Count} selected={selected}");
                if (selected != listed.Count) Click(list, CoupangScreens.SelectAll(list.Boxes, header), "全選");
            }
            var (ticked, _) = await WaitAsync("全選勾起來", 8, s => CoupangScreens.Counts(s.Boxes).Selected == listed.Count ? s : null);
            using (ticked)
            {
                var button = CoupangScreens.ConfirmOrdersButton(ticked.Boxes) ?? throw new ShopeeBatchStop("找不到「確認訂單」按鈕。");
                Click(ticked, button, "確認訂單");
            }

            var (dialog, accept) = await WaitAsync("「確認接受訂單」視窗", 15, s => CoupangScreens.AcceptDialog(s.Boxes) is { } d ? (object)d : null);
            using (dialog)
            {
                var (named, orders, confirm) = ((int?, List<string>, OcrBox))accept;
                _log.Info("coupang-batch", $"accept dialog count={named} orders={orders.Count}");
                // The dialog must name exactly the orders ticked; otherwise nothing is accepted.
                if (named != listed.Count || (orders.Count > 0 && !orders.ToHashSet().SetEquals(listed)))
                    throw new ShopeeBatchStop($"「確認接受訂單」列出的訂單（{named?.ToString() ?? "?"} 筆）和勾選的 {listed.Count} 筆不同，沒有按確認，請人工處理。");
                Click(dialog, confirm, "確認接受訂單 確認");
            }
            var (done, ok) = await WaitAsync("「配送處理完成」", 30, s => CoupangScreens.DoneDialogConfirm(s.Boxes));
            using (done) Click(done, ok, "配送處理完成 確認");
            accepted.AddRange(listed);
            _log.Info("coupang-batch", $"accepted {listed.Count} order(s)");
            await Task.Delay(800, _token);
            // The page keeps its old list (user, 2026-10-19); reloaded for the counts.
            Keys("F5", NativeMethods.VK_F5);
            await Task.Delay(1500, _token);
        }

        status("酷澎全自動：開啟配送準備…");
        var (ready, prep) = await WaitAsync("酷澎「配送準備」", 30, s => CoupangScreens.StatusCard(s.Boxes, "配送準備") is { } c ? (object)c : null);
        using (ready) Click(ready, (((OcrBox, int))prep).Item1, "配送準備");
        var (arranged, _) = await WaitAsync("「待安排配送清單」", 20, s => CoupangScreens.ListHeader(s.Boxes, "待安排配送清單") is { } h ? h : null);
        using (arranged)
            _log.Info("coupang-batch", $"配送準備 page: {string.Join(" | ", arranged.Boxes.Select(b => ShopeeScreenshot.Compact(b.Text)).Where(t => t.Length is > 0 and <= 30).Take(120))}");
        return accepted;
    }
}
