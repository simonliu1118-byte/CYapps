namespace CYERPAutoInput;

/// <summary>What 酷澎 全自動 did in Wing, for CYInvoice, printing and ERP.</summary>
internal sealed class CoupangBatchResult
{
    public List<string> Accepted { get; } = [];
    public List<string> Arranged { get; } = [];
    /// <summary>配送指示's 「下載Excel」 (DeliveryList), null when 配送指示 had no order.</summary>
    public string? Excel { get; set; }
    public List<string> Notes { get; } = [];
}

/// <summary>
/// 酷澎 (Coupang Wing) 全自動 (user's screenshots, 2026-10-19), in a Chrome window of its own on
/// 我的訂單: 訂單確認 › tick all › 確認訂單 › 「確認接受訂單」 naming exactly those orders › 確認 ›
/// 「配送處理完成。」 › 確認; 配送準備, per 配送通路: choose it in 「請選擇配送方式」 › tick all ›
/// 批次安排配送 › (「請確認發票開立需求」 › 不用再提醒我 › 確認) › 「安排出貨」 with the set 取貨地址 ›
/// 提交 › 「取貨詳情」 all 成功 › 列印寄件單 on the thermal printer (宅配 22 %, 便利商店 100 %) ›
/// 關閉; 配送指示 › tick all › 下載Excel into Temp. CYInvoice, paper invoices and ERP follow in
/// the main window. Wing pages do not reload by themselves after a step, so they are reloaded.
/// </summary>
internal sealed partial class ShopeeBatchRunner
{
    /// <summary>Wing 我的訂單 (user's screenshot, 2026-10-19).</summary>
    public const string CoupangUrl = "https://wing.coupang.com/tenants/sfl-portal/delivery/management";

    public async Task<CoupangBatchResult> RunCoupangAsync(string folder, Action<string> status)
    {
        Directory.CreateDirectory(folder);
        if (ShopeeScreenshot.Compact(_print.HctPickupAddress).Length < 6)
            throw new ShopeeBatchStop("請先在「設定 › 平台匯入 › 蝦皮」填新竹物流取件地址：酷澎「安排出貨」的取貨地址要和它相同才提交。");
        _pageTabs = true;
        var result = new CoupangBatchResult();
        status("酷澎全自動：開啟 Chrome…");
        await OpenWindowAsync(CoupangUrl);
        await ConfirmCoupangOrdersAsync(result, status);
        _shippedHint = result.Accepted.Count > 0;
        await ArrangeCoupangShippingAsync(result, status);

        status("酷澎全自動：配送指示下載 Excel…");
        var (page, card) = await WaitAsync("酷澎「配送指示」", 30, s => CoupangScreens.StatusCard(s.Boxes, "配送指示") is { } c ? (object)c : null, shipped: _shippedHint);
        var (label, count) = ((OcrBox, int))card;
        _log.Info("coupang-batch", $"配送指示 count={count}");
        if (count == 0)
        {
            page.Dispose();
            result.Notes.Add("配送指示沒有訂單。");
            return result;
        }
        using (page) Click(page, label, "配送指示");
        await Task.Delay(1500, _token);
        result.Excel = await DownloadListExcelAsync(folder);
        return result;
    }

    /// <summary>On the list shown: tick select-all, check 已選 = 共, then 下載Excel saved into <paramref name="folder"/>.</summary>
    private async Task<string> DownloadListExcelAsync(string folder)
    {
        var (page, download) = await WaitOrNullAsync(8, s => CoupangScreens.DownloadExcel(s.Boxes));
        if (page is null || download is null)
        {
            using var shown = await ReadAsync();
            _log.Info("coupang-batch", $"no 下載Excel; page: {string.Join(" | ", shown.Boxes.Select(b => ShopeeScreenshot.Compact(b.Text)).Where(t => t.Length is > 0 and <= 24).Take(80))}");
            page?.Dispose();
            throw new ShopeeBatchStop("頁面上找不到「下載Excel」：請打開酷澎「我的訂單」的清單並讓 Chrome 在最前面。", _shippedHint);
        }
        using (page)
        {
            var header = CoupangScreens.OrderHeader(page.Boxes, download) ?? throw new ShopeeBatchStop("找不到訂單列表的「訂單編號」欄。", _shippedHint);
            var (total, selected) = CoupangScreens.Counts(page.Boxes);
            _log.Info("coupang-batch", $"list total={total} selected={selected}");
            if (total == 0) throw new ShopeeBatchStop("這個清單沒有訂單（共 0 筆）。", _shippedHint);
            if (selected != total) Click(page, CoupangScreens.SelectAll(page.Boxes, header), "全選");
        }
        var (ticked, counts) = await WaitOrNullAsync(5, s => CoupangScreens.Counts(s.Boxes) is { Total: > 0 } c && c.Selected == c.Total ? (object)c : null);
        if (ticked is null)
        {
            using var shown = await ReadAsync();
            var (t, sel) = CoupangScreens.Counts(shown.Boxes);
            throw new ShopeeBatchStop($"全選沒有勾起來（共 {t?.ToString() ?? "?"} 筆、已選 {sel?.ToString() ?? "?"} 筆）。", _shippedHint);
        }
        using (ticked)
        {
            _log.Info("coupang-batch", $"all ticked {counts}");
            var button = CoupangScreens.DownloadExcel(ticked.Boxes) ?? throw new ShopeeBatchStop("找不到「下載Excel」。", _shippedHint);
            Click(ticked, button, "下載Excel");
        }
        var (error, files) = await SaveFromDialogAsync(folder, "下載Excel", 60);
        if (error is not null) throw new ShopeeBatchStop($"酷澎訂單 Excel 存不下來：{error}。", _shippedHint);
        var file = files.FirstOrDefault(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            ?? throw new ShopeeBatchStop("酷澎「下載Excel」後找不到 .xlsx 檔。", _shippedHint);
        _log.Info("coupang-batch", "excel saved");
        return file;
    }

    /// <summary>
    /// 訂單確認: every listed order accepted, round after round (a list shows ten at a time; the
    /// page is reloaded after each, it keeps its old list otherwise).
    /// </summary>
    private async Task ConfirmCoupangOrdersAsync(CoupangBatchResult result, Action<string> status)
    {
        for (var round = 1; ; round++)
        {
            var (page, card) = await WaitAsync("酷澎「我的訂單」的訂單狀態", 40, s => CoupangScreens.StatusCard(s.Boxes, "訂單確認") is { } c ? (object)c : null);
            var (label, count) = ((OcrBox, int))card;
            _log.Info("coupang-batch", $"訂單確認 count={count} round={round}");
            if (count == 0) { page.Dispose(); return; }
            if (round > 10) { page.Dispose(); throw new ShopeeBatchStop($"確認了 {result.Accepted.Count} 筆後「訂單確認」仍有 {count} 筆，請人工確認。", result.Accepted.Count > 0); }
            using (page) Click(page, label, "訂單確認");
            status($"酷澎全自動：確認訂單（已確認 {result.Accepted.Count} 筆）…");

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
                    throw new ShopeeBatchStop($"「確認接受訂單」列出的訂單（{named?.ToString() ?? "?"} 筆）和勾選的 {listed.Count} 筆不同，沒有按確認，請人工處理。", result.Accepted.Count > 0);
                Click(dialog, confirm, "確認接受訂單 確認");
            }
            var (done, ok) = await WaitAsync("「配送處理完成」", 30, s => CoupangScreens.DoneDialogConfirm(s.Boxes));
            using (done) Click(done, ok, "配送處理完成 確認");
            result.Accepted.AddRange(listed);
            _log.Info("coupang-batch", $"accepted {listed.Count} order(s)");
            await Task.Delay(800, _token);
            Keys("F5", NativeMethods.VK_F5);
            await Task.Delay(1500, _token);
        }
    }

    /// <summary>
    /// 配送準備 (user, 2026-10-19): per 配送通路 among the listed orders, choose it in the bottom
    /// 「請選擇配送方式」 first (the list then shows only those orders), tick all and arrange
    /// (<see cref="ArrangeChannelAsync"/>). Reloaded and repeated while 配送準備 still counts orders.
    /// </summary>
    private async Task ArrangeCoupangShippingAsync(CoupangBatchResult result, Action<string> status)
    {
        for (var round = 1; ; round++)
        {
            var (page, card) = await WaitAsync("酷澎「配送準備」", 30, s => CoupangScreens.StatusCard(s.Boxes, "配送準備") is { } c ? (object)c : null, shipped: _shippedHint);
            var (label, count) = ((OcrBox, int))card;
            _log.Info("coupang-batch", $"配送準備 count={count} round={round}");
            if (count == 0) { page.Dispose(); return; }
            if (round > 10) { page.Dispose(); throw new ShopeeBatchStop($"安排了 {result.Arranged.Count} 筆後「配送準備」仍有 {count} 筆，請人工確認。", true); }
            using (page) Click(page, label, "配送準備");

            var (list, header) = await WaitAsync("「待安排配送清單」", 20, s => CoupangScreens.ListHeader(s.Boxes, "待安排配送清單") is { } h && CoupangScreens.ListedOrders(s.Boxes, h).Count > 0 ? h : null, shipped: _shippedHint);
            List<string> channels;
            using (list)
            {
                channels = CoupangScreens.ColumnValues(list.Boxes, header, "配送通路").Distinct().ToList();
                _log.Info("coupang-batch", $"待安排 orders={CoupangScreens.ListedOrders(list.Boxes, header).Count} channels={string.Join("、", channels)}");
                if (channels.Count == 0)
                    _log.Info("coupang-batch", $"待安排 rows: {string.Join(" | ", list.Boxes.Where(b => b.Top >= header.Top - 10).OrderBy(b => b.Top).ThenBy(b => b.Left).Take(60).Select(b => $"{ShopeeScreenshot.Compact(b.Text)}@{b.Left},{b.Top}"))}");
            }
            if (channels.Count == 0) throw new ShopeeBatchStop("讀不到「配送通路」欄，請人工安排配送。", _shippedHint);
            foreach (var channel in channels)
            {
                status($"酷澎全自動：安排配送 {channel}（已安排 {result.Arranged.Count} 筆）…");
                await ChooseShipMethodAsync(channel);
                result.Arranged.AddRange(await ArrangeChannelAsync(channel));
                _shippedHint = true;
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
            var (bar, found) = await WaitAsync("「請選擇配送方式」與「批次安排配送」", 10, s => CoupangScreens.ShipBar(s.Boxes) is { } b ? (object)b : null, shipped: _shippedHint);
            var (select, _) = ((OcrBox, OcrBox))found;
            var shown = CoupangScreens.Channel(ShopeeScreenshot.Compact(select.Text)) ?? ShopeeScreenshot.Compact(select.Text);
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
        throw new ShopeeBatchStop($"「請選擇配送方式」裡找不到「{channel}」，請人工安排配送。", _shippedHint);
    }

    /// <summary>
    /// The listed orders (all of <paramref name="channel"/>) ticked › 批次安排配送, then whatever Wing
    /// shows until they leave the list: 「請確認發票開立需求」 › 不用再提醒我 › 確認; 「安排出貨」 with
    /// the set 取貨地址 › 提交; 「取貨詳情」, all 成功 › 列印寄件單 › 關閉. The arranged 訂單編號.
    /// </summary>
    private async Task<List<string>> ArrangeChannelAsync(string channel)
    {
        var (list, header) = await WaitAsync($"「{channel}」的訂單", 15, s =>
            CoupangScreens.ListHeader(s.Boxes, "待安排配送清單") is { } h && CoupangScreens.ListedOrders(s.Boxes, h).Count > 0 &&
            CoupangScreens.ColumnValues(s.Boxes, h, "配送通路").All(c => c == channel) ? h : null, shipped: _shippedHint);
        List<string> orders;
        using (list)
        {
            orders = CoupangScreens.ListedOrders(list.Boxes, header);
            var (_, selected) = CoupangScreens.Counts(list.Boxes);
            if (selected != orders.Count) Click(list, CoupangScreens.SelectAll(list.Boxes, header), "全選");
        }
        var (ticked, _) = await WaitAsync("全選勾起來", 8, s => CoupangScreens.Counts(s.Boxes).Selected == orders.Count ? s : null, shipped: _shippedHint);
        using (ticked)
        {
            if (CoupangScreens.ShipBar(ticked.Boxes) is not { } bar || CoupangScreens.Channel(ShopeeScreenshot.Compact(bar.Select.Text)) != channel)
                throw new ShopeeBatchStop($"按「批次安排配送」前配送方式不是「{channel}」，已停止。", _shippedHint);
            Click(ticked, bar.Arrange, $"批次安排配送 {channel}");
        }
        return await FinishArrangeAsync(channel, orders, submitted: false);
    }

    /// <summary>What Wing shows after 批次安排配送, until 取貨詳情 is closed (see <see cref="ArrangeChannelAsync"/>).</summary>
    private async Task<List<string>> FinishArrangeAsync(string channel, List<string> orders, bool submitted)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        System.Diagnostics.Stopwatch? detailsSeen = null;
        bool reminded = false, printed = false;
        while (true)
        {
            await Task.Delay(700, _token);
            using var screen = await ReadAsync();
            if (!reminded && CoupangScreens.InvoiceReminder(screen.Boxes) is { } reminder)
            {
                // Not every time (user, 2026-10-19); once ticked it does not come again.
                reminded = true;
                Click(screen, reminder.DontRemind, "不用再提醒我");
                await Task.Delay(400, _token);
                Click(screen, reminder.Confirm, "請確認發票開立需求 確認");
                continue;
            }
            if (!submitted && CoupangScreens.ArrangeDialog(screen.Boxes, _print.HctPickupAddress) is { } arrange)
            {
                _log.Info("coupang-batch", $"安排出貨 method={arrange.Method} address_matches={arrange.AddressMatches?.ToString() ?? "none"}");
                if (arrange.Method.Length > 0 && (CoupangScreens.Channel(arrange.Method) ?? arrange.Method) != channel)
                    throw new ShopeeBatchStop($"「安排出貨」的配送方式是「{arrange.Method}」，不是「{channel}」，沒有提交。", true);
                // The pickup address must be the one set for 新竹物流 in 設定 (user, 2026-10-19: same as 蝦皮).
                if (arrange.AddressMatches == false)
                    throw new ShopeeBatchStop("「安排出貨」的取貨地址和設定的取件地址不同，沒有提交，請人工確認。", true);
                // 宅配 is picked up at the set address: not submitted before it is read.
                if (arrange.AddressMatches is null && channel.StartsWith("宅配", StringComparison.Ordinal)) continue;
                submitted = true;
                Click(screen, arrange.Submit, "安排出貨 提交");
                continue;
            }
            if (CoupangScreens.PickupDetails(screen.Boxes) is { } details)
            {
                if (!printed)
                {
                    detailsSeen ??= System.Diagnostics.Stopwatch.StartNew();
                    var allDone = details.Orders.Count > 0 && details.Succeeded.Count == details.Orders.Count && orders.All(details.Succeeded.Contains);
                    _log.Info("coupang-batch", $"取貨詳情 orders={details.Orders.Count} succeeded={details.Succeeded.Count} method={details.Method}");
                    if (!allDone)
                    {
                        // 狀態 is still loading right after 提交 (user LOG 2026-10-19): wait for 成功.
                        if (detailsSeen.Elapsed < TimeSpan.FromSeconds(30)) continue;
                        _log.Info("coupang-batch", $"取貨詳情 page: {string.Join(" | ", screen.Boxes.Select(b => $"{ShopeeScreenshot.Compact(b.Text)}@{b.Left},{b.Top}").Take(150))}");
                        throw new ShopeeBatchStop($"「取貨詳情」30 秒內不是全部成功（{details.Succeeded.Count}/{details.Orders.Count}），沒有列印寄件單，請人工確認。", true);
                    }
                    printed = true;
                    Click(screen, details.Print, "列印寄件單");
                    await PrintCoupangSlipsAsync(channel, orders.Count, details.Trackings);
                }
                else
                {
                    // 取貨詳情 already showed every order 成功; the list behind it is not reloaded
                    // by Wing (user, 2026-10-19), so closing it ends the channel.
                    Click(screen, details.Close, "取貨詳情 關閉");
                    await Task.Delay(800, _token);
                    _log.Info("coupang-batch", $"arranged {orders.Count} order(s) channel={channel} reminder={reminded}");
                    return orders;
                }
                continue;
            }
            if (CoupangScreens.DoneDialogConfirm(screen.Boxes) is { } ok) { Click(screen, ok, "配送處理完成 確認"); continue; }
            var left = CoupangScreens.ListHeader(screen.Boxes, "待安排配送清單") is { } h ? CoupangScreens.ListedOrders(screen.Boxes, h) : [];
            if (printed && !left.Intersect(orders).Any())
            {
                _log.Info("coupang-batch", $"arranged {orders.Count} order(s) channel={channel} reminder={reminded}");
                return orders;
            }
            if (watch.Elapsed > TimeSpan.FromSeconds(120))
            {
                _log.Info("coupang-batch", $"after 批次安排配送: {string.Join(" | ", screen.Boxes.Select(b => ShopeeScreenshot.Compact(b.Text)).Where(t => t.Length is > 0 and <= 30).Take(150))}");
                throw new ShopeeBatchStop($"按「批次安排配送」（{channel}）後沒有走完（提交 {submitted}、列印 {printed}），請人工確認。", true);
            }
        }
    }

    /// <summary>
    /// After 列印寄件單: Chrome's print dialog (Ctrl+P when the slips open in a tab of their own
    /// instead), on the thermal printer at 宅配 22 % (設定), 便利商店 100 % (user, 2026-10-19);
    /// a tab opened for it is closed afterwards.
    /// </summary>
    private async Task PrintCoupangSlipsAsync(string channel, int orders, IReadOnlyList<string> trackings)
    {
        if (channel.Contains("全家", StringComparison.Ordinal))
        {
            await PrintCoupangFamilyAsync(trackings);
            return;
        }
        var (dialog, _) = await WaitOrNullAsync(12, s => ShopeeBatchScreens.IsPrintDialog(s.Boxes) ? s : null);
        var ownTab = dialog is null;
        dialog?.Dispose();
        if (ownTab)
        {
            using (var shown = await ReadAsync())
                _log.Info("coupang-batch", $"寄件單 page: {string.Join(" | ", shown.Boxes.Select(b => ShopeeScreenshot.Compact(b.Text)).Where(t => t.Length is > 0 and <= 30).Take(60))}");
            Keys("Ctrl+P (寄件單)", NativeMethods.VK_CONTROL, 0x50 /* P */);
        }
        var scale = channel.StartsWith("便利商店", StringComparison.Ordinal) ? 100 : _print.CoupangHctScale;
        await PrintDialogAsync(scale, pages: null, sheetsAllowed: Math.Max(1, orders));
        _log.Info("coupang-batch", $"slips printed channel={channel} orders={orders} scale={scale} own_tab={ownTab}");
        await Task.Delay(2000, _token);
        if (ownTab)
        {
            Keys("Ctrl+W (寄件單)", NativeMethods.VK_CONTROL, 0x57 /* W */);
            await Task.Delay(800, _token);
        }
    }

    /// <summary>
    /// 酷澎 全家 (user, 2026-10-20: 列印寄件單 opens the same FamilyMart寄取貨單 as 蝦皮; page 2 at
    /// 165 % did not scan, the cut one did): the page is checked for every 寄件編號, saved, each
    /// slip cut and printed dot for dot as for 蝦皮; the tab is closed after. A single slip that
    /// cannot be cut goes through Chrome as for 蝦皮; several stop for a person.
    /// </summary>
    private async Task PrintCoupangFamilyAsync(IReadOnlyList<string> trackings)
    {
        if (trackings.Count == 0) throw new ShopeeBatchStop("「便利商店」讀不到寄件單號，沒有列印寄件單，請人工列印。", true);
        using (var page = await WaitFamilyLabelPageAsync())
            foreach (var tracking in trackings)
                if (!ShopeeBatchScreens.ShowsNumber(page.Boxes, tracking))
                    throw new ShopeeBatchStop($"全家寄取貨單上讀不到寄件編號 {tracking}，未列印。", true);
        var (slips, printer, info) = await CutFamilySlipsAsync(trackings);
        if (slips is not null)
        {
            foreach (var slip in slips) PrintCutSlip(printer, slip, "酷澎");
            _log.Info("coupang-batch", $"family slips printed cut count={slips.Count} {info}");
        }
        else if (trackings.Count == 1)
        {
            _log.Info("coupang-batch", $"family cut: {info}; printing through Chrome");
            Keys("Ctrl+P", NativeMethods.VK_CONTROL, 0x50 /* P */);
            await PrintDialogAsync(_print.FamilyScale, _print.FamilyPages);
        }
        else throw new ShopeeBatchStop($"全家寄取貨單 {trackings.Count} 張切不出來（{info}），未列印，請人工列印。", true);
        await Task.Delay(1500, _token);
        Keys("Ctrl+W (寄取貨單)", NativeMethods.VK_CONTROL, 0x57 /* W */);
        await Task.Delay(800, _token);
    }

    /// <summary>
    /// <see cref="CutFamilySlipAsync"/> for several orders: one save of the page, as many slips
    /// as 寄件編號, each cut slip read (OCR) and every 寄件編號 found once. Null with the reason
    /// when anything is short.
    /// </summary>
    private async Task<(List<GrayImage>? Slips, string Printer, string Info)> CutFamilySlipsAsync(IReadOnlyList<string> trackings)
    {
        if (trackings.Count == 1)
        {
            var one = await CutFamilySlipAsync(trackings[0]);
            return (one.Slip is null ? null : [one.Slip], one.Printer, one.Info);
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "Temp", "slip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            var printer = SlipPrinter.Installed(_printer);
            if (printer is null) return (null, string.Empty, $"找不到名稱符合「{_printer}」的印表機");
            Directory.CreateDirectory(folder);
            Keys("Ctrl+S (save the slip's page)", NativeMethods.VK_CONTROL, 0x53 /* S */);
            if ((await SaveFromDialogAsync(folder, "Ctrl+S")).Error is { } failed) return (null, printer, failed);
            if (SlipPrinter.LargestPicture(folder) is not { } picture) return (null, printer, "存下的網頁裡沒有寄取貨單圖片");
            var page = picture.Image;
            var areas = SlipImage.FindSlips(page);
            var label = SlipPrinter.Label(printer, 100, 150);
            _log.Info("coupang-batch", $"family cut: picture {page.Width}x{page.Height} slips={areas.Count} trackings={trackings.Count} dpi={label.Dpi}");
            if (areas.Count != trackings.Count) return (null, printer, $"圖片裡找到 {areas.Count} 張寄取貨單，寄件編號 {trackings.Count} 個");
            var slips = new List<GrayImage>();
            var left = trackings.ToList();
            foreach (var area in areas)
            {
                var plan = SlipImage.Plan(page, area, label.Width, label.Height);
                if (plan.Module == 0 || plan.Width > label.Width || plan.Height > label.Height || plan.Width * 25.4 / label.Dpi < 50)
                    return (null, printer, $"寄取貨單 {area} 量不到條碼或尺寸不合標籤");
                var slip = SlipImage.Render(page, plan);
                string? shown = null;
                foreach (var t in left) if (await SlipShowsAsync(slip, t)) { shown = t; break; }
                if (shown is null) return (null, printer, $"裁好的寄取貨單讀不到剩下的寄件編號（{string.Join("、", left)}）");
                left.Remove(shown);
                slips.Add(slip);
            }
            return (slips, printer, $"{slips.Count} 張，{label.Dpi} dpi");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ShopeeBatchStop)
        {
            return (null, string.Empty, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// 酷澎 › 列印寄件單 only (user, 2026-10-20): the Wing dialog the user opened in front
    /// (寄件詳情 from 配送指示, or 取貨詳情／便利商店) — its 列印寄件單 pressed and the slips printed as
    /// in 全自動 (全家 cut and printed dot for dot), then 關閉.
    /// </summary>
    public async Task<string> PrintOpenCoupangSlipAsync()
    {
        _window = ChromeWindows().FirstOrDefault();
        if (_window == 0) throw new ShopeeBatchStop("找不到 Chrome 視窗：請先在 Chrome 打開酷澎 Wing 的寄件詳情。");
        _pageTabs = true;
        _shippedHint = true;
        Front();
        await Task.Delay(500, _token);
        var (shown, found) = await WaitOrNullAsync(8, s => CoupangScreens.PickupDetails(s.Boxes) is { } d ? (object)d : null);
        if (shown is null || found is null)
        {
            using (var page = await ReadAsync())
                _log.Info("coupang-batch", $"print only page: {string.Join(" | ", page.Boxes.Where(b => b.Text.Trim().Length is > 0 and <= 30).Take(200).Select(b => $"{ShopeeScreenshot.Compact(b.Text)}@{b.Left},{b.Top},{b.Width}x{b.Height}"))}");
            shown?.Dispose();
            throw new ShopeeBatchStop("畫面上沒有「寄件詳情」（或取貨詳情）視窗：請在 Wing 配送指示按該筆的「列印寄件單」打開後再試。");
        }
        var details = ((List<string> Orders, List<string> Succeeded, string Method, OcrBox Print, OcrBox Close, List<string> Trackings))found;
        var channel = details.Method.Length > 0 ? details.Method : "宅配";
        _log.Info("coupang-batch", $"print only channel={channel} trackings={details.Trackings.Count}");
        using (shown) Click(shown, details.Print, "列印寄件單");
        await PrintCoupangSlipsAsync(channel, Math.Max(1, details.Trackings.Count), details.Trackings);
        await Task.Delay(800, _token);
        using (var after = await ReadAsync())
            if (CoupangScreens.PickupDetails(after.Boxes) is { } open) Click(after, open.Close, "關閉");
        return $"{channel}：寄件單 {string.Join("、", details.Trackings)} 已送印。";
    }
}
