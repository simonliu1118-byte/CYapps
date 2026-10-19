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
    /// 配送準備 (<see cref="ArrangeCoupangShippingAsync"/>), and 配送指示 is opened and read for the
    /// log. Returns the accepted 訂單編號.
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

        await ArrangeCoupangShippingAsync(status);
        await LogCoupangTabAsync("配送指示");
        return accepted;
    }

    /// <summary>
    /// 配送準備 (user, 2026-10-19): per 配送通路 among the listed orders, choose it in the bottom
    /// 「請選擇配送方式」 first (the list then shows only those orders), tick all › 批次安排配送; when
    /// 「請確認發票開立需求」 comes, tick 「不用再提醒我」 › 確認. Done once the orders leave the list;
    /// reloaded and repeated while 配送準備 still counts orders.
    /// </summary>
    private async Task ArrangeCoupangShippingAsync(Action<string> status)
    {
        var arranged = 0;
        for (var round = 1; ; round++)
        {
            var (page, card) = await WaitAsync("酷澎「配送準備」", 30, s => CoupangScreens.StatusCard(s.Boxes, "配送準備") is { } c ? (object)c : null);
            var (label, count) = ((OcrBox, int))card;
            _log.Info("coupang-batch", $"配送準備 count={count} round={round}");
            if (count == 0) { page.Dispose(); return; }
            if (round > 10) { page.Dispose(); throw new ShopeeBatchStop($"安排了 {arranged} 筆後「配送準備」仍有 {count} 筆，請人工確認。", true); }
            using (page) Click(page, label, "配送準備");

            var (list, header) = await WaitAsync("「待安排配送清單」", 20, s => CoupangScreens.ListHeader(s.Boxes, "待安排配送清單") is { } h && CoupangScreens.ListedOrders(s.Boxes, h).Count > 0 ? h : null);
            List<string> channels;
            using (list)
            {
                channels = CoupangScreens.ColumnValues(list.Boxes, header, "配送通路").Distinct().ToList();
                _log.Info("coupang-batch", $"待安排 orders={CoupangScreens.ListedOrders(list.Boxes, header).Count} channels={string.Join("、", channels)}");
            }
            if (channels.Count == 0) throw new ShopeeBatchStop("讀不到「配送通路」欄，請人工安排配送。", true);
            foreach (var channel in channels)
            {
                status($"酷澎全自動：安排配送 {channel}（已安排 {arranged} 筆）…");
                await ChooseShipMethodAsync(channel);
                arranged += await ArrangeChannelAsync(channel);
            }
            Keys("F5", NativeMethods.VK_F5);
            await Task.Delay(1500, _token);
        }
    }

    /// <summary>
    /// The bottom 配送方式 drop-down set to <paramref name="channel"/>: opened, first option, then one
    /// option down at a time (keys; a native list) until it shows that channel.
    /// </summary>
    private async Task ChooseShipMethodAsync(string channel)
    {
        string? previous = null;
        for (var step = 0; step <= 8; step++)
        {
            var (bar, found) = await WaitAsync("「請選擇配送方式」與「批次安排配送」", 10, s => CoupangScreens.ShipBar(s.Boxes) is { } b ? (object)b : null);
            var (select, _) = ((OcrBox, OcrBox))found;
            var shown = ShopeeScreenshot.Compact(select.Text);
            _log.Info("coupang-batch", $"配送方式 shows {shown} step={step}");
            using (bar)
            {
                if (shown == channel) return;
                if (step > 1 && shown == previous) break; // the end of the list
                previous = shown;
                Click(bar, select, "請選擇配送方式");
            }
            await Task.Delay(400, _token);
            Keys(step == 0 ? "Home" : "Down", step == 0 ? NativeMethods.VK_HOME : (ushort)0x28 /* VK_DOWN */);
            await Task.Delay(200, _token);
            Keys("Enter", NativeMethods.VK_RETURN);
            await Task.Delay(1200, _token); // the list is filtered again
        }
        throw new ShopeeBatchStop($"「請選擇配送方式」裡找不到「{channel}」，請人工安排配送。", true);
    }

    /// <summary>The listed orders (all of <paramref name="channel"/>) ticked › 批次安排配送 › 發票提醒; how many were arranged.</summary>
    private async Task<int> ArrangeChannelAsync(string channel)
    {
        var (list, header) = await WaitAsync($"「{channel}」的訂單", 15, s =>
            CoupangScreens.ListHeader(s.Boxes, "待安排配送清單") is { } h && CoupangScreens.ListedOrders(s.Boxes, h).Count > 0 &&
            CoupangScreens.ColumnValues(s.Boxes, h, "配送通路").All(c => c == channel) ? h : null);
        List<string> orders;
        using (list)
        {
            orders = CoupangScreens.ListedOrders(list.Boxes, header);
            var (_, selected) = CoupangScreens.Counts(list.Boxes);
            if (selected != orders.Count) Click(list, CoupangScreens.SelectAll(list.Boxes, header), "全選");
        }
        var (ticked, _) = await WaitAsync("全選勾起來", 8, s => CoupangScreens.Counts(s.Boxes).Selected == orders.Count ? s : null);
        using (ticked)
        {
            if (CoupangScreens.ShipBar(ticked.Boxes) is not { } bar || ShopeeScreenshot.Compact(bar.Select.Text) != channel)
                throw new ShopeeBatchStop($"按「批次安排配送」前配送方式不是「{channel}」，已停止。", true);
            Click(ticked, bar.Arrange, $"批次安排配送 {channel}");
        }

        // 「請確認發票開立需求」 may come (not always); then the orders leave the list.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var reminded = false;
        while (true)
        {
            await Task.Delay(700, _token);
            using var screen = await ReadAsync();
            if (!reminded && CoupangScreens.InvoiceReminder(screen.Boxes) is { } reminder)
            {
                reminded = true;
                Click(screen, reminder.DontRemind, "不用再提醒我");
                await Task.Delay(400, _token);
                Click(screen, reminder.Confirm, "請確認發票開立需求 確認");
                continue;
            }
            if (CoupangScreens.DoneDialogConfirm(screen.Boxes) is { } ok) { Click(screen, ok, "配送處理完成 確認"); continue; }
            var left = CoupangScreens.ListHeader(screen.Boxes, "待安排配送清單") is { } h ? CoupangScreens.ListedOrders(screen.Boxes, h) : [];
            if (!left.Intersect(orders).Any() && CoupangScreens.InvoiceReminder(screen.Boxes) is null)
            {
                _log.Info("coupang-batch", $"arranged {orders.Count} order(s) channel={channel} reminder={reminded}");
                return orders.Count;
            }
            if (watch.Elapsed > TimeSpan.FromSeconds(40))
            {
                _log.Info("coupang-batch", $"after 批次安排配送: {string.Join(" | ", screen.Boxes.Select(b => ShopeeScreenshot.Compact(b.Text)).Where(t => t.Length is > 0 and <= 30).Take(120))}");
                throw new ShopeeBatchStop($"按「批次安排配送」（{channel}）40 秒後訂單仍在清單上，請人工確認。", true);
            }
        }
    }

    /// <summary>A 訂單狀態 tab opened and its page written to the log (the steps after it come next).</summary>
    private async Task LogCoupangTabAsync(string name)
    {
        var (page, card) = await WaitAsync($"酷澎「{name}」", 30, s => CoupangScreens.StatusCard(s.Boxes, name) is { } c ? (object)c : null);
        using (page) Click(page, (((OcrBox, int))card).Item1, name);
        await Task.Delay(2500, _token);
        using var shown = await ReadAsync();
        _log.Info("coupang-batch", $"{name} page: {string.Join(" | ", shown.Boxes.Select(b => ShopeeScreenshot.Compact(b.Text)).Where(t => t.Length is > 0 and <= 30).Take(150))}");
    }
}
