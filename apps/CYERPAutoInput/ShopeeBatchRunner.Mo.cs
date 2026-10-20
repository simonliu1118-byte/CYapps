namespace CYERPAutoInput;

/// <summary>What MO店+ 全自動 brought down, for printing and the two-file import.</summary>
internal sealed class MoBatchResult
{
    public string Folder { get; init; } = string.Empty;
    /// <summary>未出貨 export, before shipping (the invoices are made from it).</summary>
    public string? Before { get; set; }
    /// <summary>配送中 export, after shipping (tracking numbers).</summary>
    public string? After { get; set; }
    public List<(MoShipKind Kind, string Path)> Labels { get; } = [];
    /// <summary>新竹物流 到廠取貨總表.</summary>
    public List<string> Summaries { get; } = [];
    public List<string> Notes { get; } = [];
    public int Shipped { get; set; }
}

/// <summary>
/// MO店+ 全自動, the MO店+ 後台 part (user, 2026-10-14, steps in WORK_HANDOFF): in a Chrome window
/// of its own, 訂單 › E001訂單管理 › 我的訂單 › 未出貨 (全部／全部) › 匯出報表 into the Temp folder;
/// 批次出貨, per 配送方式 with orders (新竹貨運, 7-11店出, 全家店出): select all › 批次出貨 › check the
/// orders, 併箱出貨 › 確認出貨; 批次出貨記錄's newest row must be that method, all 全部成功 › 列印文件
/// › 列印標籤 (新竹 also 列印出貨總表) saved to Temp; then 我的訂單 › 配送中 › 匯出報表. Printing,
/// the import and ERP follow in the main window. Every page is read before a click; whatever
/// does not look as expected stops.
/// </summary>
internal sealed partial class ShopeeBatchRunner
{
    /// <summary>店+管理系統 (user's screenshot, 2026-10-14).</summary>
    public const string MoUrl = "https://3p.momo.com.tw/FrameServlet.do";

    /// <param name="readOrders">The 訂單編號 of an export (it is password protected; the caller reads it).</param>
    public async Task<MoBatchResult> RunMoAsync(string folder, Func<string, IReadOnlyCollection<string>> readOrders, Action<string> status)
    {
        var result = new MoBatchResult { Folder = folder };
        _pageTabs = true;
        Directory.CreateDirectory(folder);
        status("MO店+ 全自動：開啟 Chrome…");
        await OpenWindowAsync(MoUrl);
        var (first, _) = await WaitAsync("MO店+ 店+管理系統", 40, s => MoBatchScreens.SignedIn(s.Boxes) || s.Boxes.Any(b => b.Text.Contains("登入", StringComparison.Ordinal)) ? s : null);
        var signedIn = MoBatchScreens.SignedIn(first.Boxes);
        first.Dispose();
        if (!signedIn) throw new ShopeeBatchStop("MO店+ 後台還沒登入：請在這個 Chrome 視窗登入後再按一次 MO店+ 全自動。");

        // 1–2. 未出貨 export.
        status("MO店+ 全自動：匯出未出貨訂單…");
        await OpenMyOrdersAsync("未出貨");
        using (var orders = await ReadAsync())
        {
            if (MoBatchScreens.Tab(orders.Boxes, "未出貨") is { Count: 0 })
            {
                _log.Info("mo-batch", "no orders to ship");
                result.Notes.Add("未出貨沒有訂單。");
                return result;
            }
        }
        result.Before = await ExportAsync(folder, "未出貨訂單");
        var batchOrders = readOrders(result.Before);
        _log.Info("mo-batch", $"before export orders={batchOrders.Count}");

        // 4–6. 批次出貨, method by method.
        var done = new HashSet<MoShipKind>();
        // Back to 批次出貨 only while a 配送方式 read there is still to do; after the last one
        // straight to 我的訂單 › 配送中 (user, 2026-10-14).
        List<MoShipMethod>? pending = null;
        while (pending is not { Count: 0 })
        {
            status($"MO店+ 全自動：批次出貨（已出貨 {result.Shipped} 筆）…");
            var methods = await OpenBatchShipAsync();
            foreach (var other in methods.Where(m => m.Kind == MoShipKind.Other))
            {
                var note = $"配送方式「{other.Name}」有 {other.Count} 筆，程式不處理，請人工出貨。";
                if (!result.Notes.Contains(note)) result.Notes.Add(note);
            }
            var todo = methods.Where(m => m.Kind != MoShipKind.Other).OrderBy(m => m.Kind).ToList();
            if (todo.Count == 0) break;
            var next = todo[0];
            if (!done.Add(next.Kind))
                throw new ShopeeBatchStop($"「{next.Name}」批次出貨之後還有 {next.Count} 筆未出貨，請人工確認。", true);
            await ShipMethodAsync(next, batchOrders, result);
            pending = todo.Skip(1).ToList();
        }

        // 8. 配送中 export (user, 2026-10-14: from 配送中).
        status("MO店+ 全自動：匯出配送中訂單…");
        await OpenMyOrdersAsync("配送中");
        result.After = await ExportAsync(folder, "配送中訂單");
        _log.Info("mo-batch", $"done shipped={result.Shipped} labels={result.Labels.Count} summaries={result.Summaries.Count} notes={result.Notes.Count}");
        return result;
    }

    /// <summary>訂單 › 訂單管理 › E001訂單管理 › 我的訂單, the tab, 處理提醒 全部, 配送方式 全部.</summary>
    private async Task OpenMyOrdersAsync(string tab)
    {
        await OpenOrderManagementAsync();
        using (var page = await ReadAsync())
            if (MoBatchScreens.Text(page.Boxes, "我的訂單") is { } mine) Click(page, mine, "我的訂單");
        var (list, tabBox) = await WaitAsync($"「{tab}」分頁", 30, s => MoBatchScreens.Tab(s.Boxes, tab)?.Box);
        using (list) Click(list, tabBox, tab);
        await Task.Delay(800, _token);
        foreach (var (label, button) in new[] { ("處理提醒", "全部"), ("配送方式", "全部") })
        {
            using var page = await ReadAsync();
            if (MoBatchScreens.Filter(page.Boxes, label, button) is { } filter) Click(page, filter, $"{label} {button}");
            await Task.Delay(600, _token);
        }
        var (ready, _) = await WaitAsync("訂單列表", 30, s => s.Boxes.Any(b => b.Text.Trim() == "匯出報表") ? s : null);
        ready.Dispose();
    }

    /// <summary>E001訂單管理, from wherever the window is (the top menu's 訂單 › 訂單管理).</summary>
    private async Task OpenOrderManagementAsync()
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var page = await ReadAsync();
            if (MoBatchScreens.IsOrderManagement(page.Boxes)) return;
            if (MoBatchScreens.OrderManagement(page.Boxes) is { } entry) { Click(page, entry, "E001訂單管理"); await Task.Delay(1500, _token); continue; }
            if (MoBatchScreens.Text(page.Boxes, "訂單管理") is { } menu && attempt > 0) { Click(page, menu, "訂單管理"); await Task.Delay(800, _token); continue; }
            var top = page.Boxes.Where(b => b.Text.Trim() == "訂單").OrderBy(b => b.Top).FirstOrDefault();
            if (top is null)
            {
                // The header (店+管理系統, 登出) shows before the menu does (user LOG 2026-10-20: 18
                // lines read 2 seconds after opening, 「訂單」 not there yet): the menu is waited for.
                var (shown, menuTop) = await WaitAsync("MO店+ 後台上方的「訂單」選單", 30,
                    s => s.Boxes.Where(b => b.Text.Trim() == "訂單").OrderBy(b => b.Top).FirstOrDefault());
                using (shown) Click(shown, menuTop, "訂單");
                await Task.Delay(1200, _token);
                continue;
            }
            Click(page, top, "訂單");
            await Task.Delay(1200, _token);
        }
        var (done, _) = await WaitAsync("E001訂單管理", 20, s => MoBatchScreens.IsOrderManagement(s.Boxes) ? s : null);
        done.Dispose();
    }

    /// <summary>匯出報表 → 匯出提醒 (已完成) and Chrome's 另存新檔 into the folder → 關閉. The .xls saved.</summary>
    private async Task<string> ExportAsync(string folder, string what)
    {
        using (var page = await ReadAsync())
        {
            var button = page.Boxes.FirstOrDefault(b => b.Text.Trim() == "匯出報表") ?? throw new ShopeeBatchStop("找不到「匯出報表」。");
            Click(page, button, "匯出報表");
        }
        var (error, files) = await SaveFromDialogAsync(folder, "匯出報表", 60);
        if (error is not null) throw new ShopeeBatchStop($"{what}匯出檔存不下來：{error}。", _shippedHint);
        var file = files.FirstOrDefault(f => f.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            ?? throw new ShopeeBatchStop($"{what}匯出後找不到 .xls 檔。", _shippedHint);
        var (dialog, close) = await WaitOrNullAsync(10, s => MoBatchScreens.ExportClose(s.Boxes));
        if (dialog is not null && close is not null) using (dialog) Click(dialog, close, "匯出提醒 關閉");
        else dialog?.Dispose();
        _log.Info("mo-batch", $"{what} exported");
        return file;
    }

    /// <summary>批次出貨 (left menu), 處理提醒 全部: the 配送方式 with orders.</summary>
    private async Task<List<MoShipMethod>> OpenBatchShipAsync()
    {
        await OpenOrderManagementAsync();
        using (var page = await ReadAsync())
        {
            var menu = page.Boxes.Where(b => b.Text.Trim() == "批次出貨").OrderBy(b => b.Left).FirstOrDefault()
                ?? throw new ShopeeBatchStop("找不到左側的「批次出貨」。", _shippedHint);
            Click(page, menu, "批次出貨（左側選單）");
        }
        // The 配送方式 row is what matters here (user, 2026-10-14: read its counts, then go in).
        static bool IsBatchShip(Screen s) => MoBatchScreens.Text(s.Boxes, "配送方式") is not null && MoBatchScreens.Filter(s.Boxes, "處理提醒", "全部") is not null;
        // From 批次出貨記錄 (the next 配送方式) the left menu may do nothing (LOG 2026-10-14: the
        // record stayed): then another 批次出貨 (a tab) or 返回, the browser's back, a reload.
        for (var step = 0; step < 3; step++)
        {
            var (ready, _) = await WaitOrNullAsync(6, s => IsBatchShip(s) ? s : null);
            if (ready is not null) { ready.Dispose(); break; }
            using var page = await ReadAsync();
            var leftmost = page.Boxes.Where(b => b.Text.Trim() == "批次出貨").OrderBy(b => b.Left).FirstOrDefault();
            _log.Info("mo-batch", $"批次出貨頁 not yet (step {step}); page: {string.Join(" | ", page.Boxes.Select(b => ShopeeScreenshot.Compact(b.Text)).Where(t => t.Length is > 0 and <= 24).Take(60))}");
            var other = page.Boxes.Where(b => b != leftmost && (b.Text.Trim() == "批次出貨" || b.Text.Trim() == "返回")).OrderBy(b => b.Top).FirstOrDefault();
            if (step == 0 && other is not null) Click(page, other, $"{other.Text.Trim()}（頁面上）");
            else if (step <= 1) Keys("Alt+Left（上一頁）", NativeMethods.VK_MENU, NativeMethods.VK_LEFT);
            else Keys("F5", NativeMethods.VK_F5);
            await Task.Delay(1500, _token);
        }
        var (list, _) = await WaitAsync("批次出貨頁", 30, s => IsBatchShip(s) ? s : null, _shippedHint);
        using (list)
            if (MoBatchScreens.Filter(list.Boxes, "處理提醒", "全部") is { } all) Click(list, all, "處理提醒 全部");
        await Task.Delay(800, _token);
        var (page2, methods) = await WaitAsync("批次出貨的配送方式", 20, s => MoBatchScreens.Text(s.Boxes, "配送方式") is not null ? MoBatchScreens.ShipMethods(s.Boxes) : null, _shippedHint);
        page2.Dispose();
        _log.Info("mo-batch", $"ship methods with orders: {string.Join(", ", methods.Select(m => $"{m.Name}({m.Count})"))}");
        return methods;
    }

    /// <summary>One 配送方式: select all, 批次出貨, check, 併箱出貨, 確認出貨, the record, its PDFs.</summary>
    private async Task ShipMethodAsync(MoShipMethod method, IReadOnlyCollection<string> batchOrders, MoBatchResult result)
    {
        using (var page = await ReadAsync())
        {
            var button = MoBatchScreens.ShipMethods(page.Boxes).FirstOrDefault(m => m.Name == method.Name)?.Box
                ?? throw new ShopeeBatchStop($"找不到配送方式「{method.Name}」。", _shippedHint);
            Click(page, button, method.Name);
        }
        await Task.Delay(1200, _token);
        // The select-all box has no text: just left of 「已選 0 個訂單」.
        var (list, selected) = await WaitAsync("「已選 0 個訂單」", 20, s => MoBatchScreens.Selected(s.Boxes) is { Count: 0 } sel ? sel.Box : null, _shippedHint);
        using (list)
        {
            _inputs++;
            Front();
            var point = new Point(list.Area.Left + selected.Left - 22, list.Area.Top + selected.CenterY);
            _log.Info("mo-batch", $"click select-all at {point.X},{point.Y}");
            InputSender.Click(point);
        }
        var (picked, button2) = await WaitAsync($"勾選 {method.Count} 筆", 15, s => MoBatchScreens.Selected(s.Boxes) is { } sel && sel.Count > 0 ? MoBatchScreens.ShipSelectedButton(s.Boxes) : null, _shippedHint);
        using (picked)
        {
            var count = MoBatchScreens.Selected(picked.Boxes)!.Value.Count;
            if (count != method.Count) throw new ShopeeBatchStop($"「{method.Name}」勾選了 {count} 筆，不是 {method.Count} 筆，未出貨。", _shippedHint);
            Click(picked, button2, "批次出貨");
        }

        var (ship, total) = await WaitAsync("批次出貨確認頁", 30, s => MoBatchScreens.ShipPageCount(s.Boxes) is { } n ? (object)n : null, _shippedHint);
        using (ship)
        {
            var orders = MoBatchScreens.ShipPageOrders(ship.Boxes);
            if ((int)total != method.Count || orders.Count != method.Count)
            {
                _log.Info("mo-batch", $"ship page: {string.Join(" | ", ship.Boxes.Select(b => ShopeeScreenshot.Compact(b.Text)).Where(t => t.Length is > 0 and <= 24).Take(60))}");
                throw new ShopeeBatchStop($"批次出貨頁是 {total} 筆（讀到 {orders.Count} 個訂單編號），不是「{method.Name}」的 {method.Count} 筆，未出貨。", _shippedHint);
            }
            var unknown = orders.Where(o => !batchOrders.Contains(o)).ToList();
            if (unknown.Count > 0)
                throw new ShopeeBatchStop($"批次出貨頁的訂單 {string.Join("、", unknown)} 不在剛匯出的未出貨訂單檔裡，未出貨。", _shippedHint);
            if (MoBatchScreens.Text(ship.Boxes, "併箱出貨") is { } merge) Click(ship, merge, "併箱出貨");
        }
        // 確認出貨 is at the page's bottom.
        OcrBox? confirm = null;
        Screen? bottom = null;
        for (var i = 0; i < 6 && confirm is null; i++)
        {
            bottom?.Dispose();
            bottom = await ReadAsync();
            if (MoBatchScreens.Text(bottom.Boxes, "確認出貨") is { } c && Visible(bottom, c)) confirm = c;
            else
            {
                _inputs++;
                Wheel(new Point(bottom.Area.Left + bottom.Area.Width / 2, bottom.Area.Top + bottom.Area.Height / 2), -5);
                await Task.Delay(500, _token);
            }
        }
        if (confirm is null || bottom is null) { bottom?.Dispose(); throw new ShopeeBatchStop("找不到「確認出貨」，未出貨。", _shippedHint); }
        var shippedAt = DateTime.Now;
        using (bottom) Click(bottom, confirm, "確認出貨");
        _shippedHint = true;
        result.Shipped += method.Count;
        _log.Info("mo-batch", $"shipped {method.Kind} count={method.Count}");

        // 批次出貨中 → 查看批次出貨記錄 (or 關閉, then the link).
        var (dialog, close) = await WaitAsync("「批次出貨中」", 30, s => MoBatchScreens.ShippingDialogClose(s.Boxes), true);
        using (dialog)
        {
            if (MoBatchScreens.Text(dialog.Boxes, "查看批次出貨記錄") is { } records) Click(dialog, records, "查看批次出貨記錄");
            else Click(dialog, close, "關閉");
        }
        await Task.Delay(1500, _token);
        using (var page = await ReadAsync())
            if (MoBatchScreens.Text(page.Boxes, "出貨時間") is null && MoBatchScreens.Text(page.Boxes, "批次出貨記錄") is { } link)
                Click(page, link, "批次出貨記錄");

        await SaveRecordDocumentsAsync(method.Kind, method.Name, method.Count, shippedAt, result);
    }

    /// <summary>
    /// 批次出貨記錄: the top row must be the shipment just made — this method, the count, and an
    /// 出貨時間 not before <paramref name="since"/> (user, 2026-10-14: no order numbers here; the
    /// newest is on top, told by its time) — all 全部成功; then 列印文件 › 列印標籤 (新竹 also
    /// 列印出貨總表), each saved. <paramref name="since"/> null takes the top row as it is (測試).
    /// </summary>
    private async Task SaveRecordDocumentsAsync(MoShipKind kind, string name, int count, DateTime? since, MoBatchResult result)
    {
        MoShipRecord? record = null;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var logged = false;
        while (true)
        {
            using var page = await ReadAsync();
            record = MoBatchScreens.LatestRecord(page.Boxes);
            if (record is not null && MoBatchScreens.RecordMatches(record, kind, count, since))
            {
                if (record.Status.Contains("全部成功", StringComparison.Ordinal) && record.PrintButton is not null) break;
                if (record.Status.Contains("失敗", StringComparison.Ordinal) || record.Status.Contains("部分", StringComparison.Ordinal))
                    throw new ShopeeBatchStop($"批次出貨記錄「{record.Method}」{record.Time} 的狀態是「{record.Status}」，請人工處理（未列印、未打單）。", true);
            }
            if (!logged)
            {
                logged = true;
                _log.Info("mo-batch", $"record not yet: {(record is null ? "no row read" : $"{record.Time} {record.Method} {record.Orders} {record.Status} print={record.PrintButton is not null}")}; page: {string.Join(" | ", page.Boxes.Select(b => ShopeeScreenshot.Compact(b.Text)).Where(t => t.Length is > 0 and <= 40).Take(40))}");
            }
            if (watch.Elapsed > TimeSpan.FromSeconds(120))
                throw new ShopeeBatchStop($"批次出貨記錄等不到「{name}」{count} 筆全部成功（最新一筆：{record?.Time} {record?.Method} {record?.Status}）。", true);
            if (MoBatchScreens.Text(page.Boxes, "刷新") is { } refresh && watch.Elapsed > TimeSpan.FromSeconds(3)) Click(page, refresh, "刷新");
            await Task.Delay(3000, _token);
        }
        _log.Info("mo-batch", $"record {record.Time} {record.Method} {record.Status} orders={record.Orders}");

        // 列印文件 → 列印標籤 (新竹 also 列印出貨總表), each saved.
        using (var page = await ReadAsync())
        {
            var print = MoBatchScreens.LatestRecord(page.Boxes)?.PrintButton ?? throw new ShopeeBatchStop("找不到最新一筆的「列印文件」。", true);
            Click(page, print, "列印文件");
        }
        foreach (var doc in kind == MoShipKind.Hct ? new[] { "列印標籤", "列印出貨總表" } : ["列印標籤"])
        {
            var (docs, button) = await WaitAsync($"「{doc}」", 20, s => MoBatchScreens.PrintDocumentButton(s.Boxes, doc), true);
            using (docs) Click(docs, button, doc);
            var (error, files) = await SaveFromDialogAsync(result.Folder, doc, 60);
            var pdf = files.FirstOrDefault(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
            if (error is not null || pdf is null) throw new ShopeeBatchStop($"「{name}」的{doc} PDF 存不下來：{error ?? "找不到 PDF"}。", true);
            if (doc == "列印出貨總表") result.Summaries.Add(pdf);
            else result.Labels.Add((kind, pdf));
            _log.Info("mo-batch", $"{kind} {doc} saved");
        }
        // Close the 注意 dialog.
        _inputs++;
        Front();
        InputSender.Press(NativeMethods.VK_ESCAPE);
        await Task.Delay(600, _token);
        using (var page = await ReadAsync())
            if (MoBatchScreens.PrintDocumentButton(page.Boxes, "列印標籤") is not null && MoBatchScreens.Text(page.Boxes, "注意") is { } title &&
                ShopeeBatchScreens.DialogCloseMark(page.Boxes, title) is { } x)
                Click(page, x, "注意 關閉");
    }
}
