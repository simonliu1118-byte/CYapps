using System.Diagnostics;

namespace CYERPAutoQuery;

/// <summary>
/// 進貨輔助 (CY Web, user 2026-10-20): COPR15 and COPR16 for every customer in the range, summed by
/// 品號 and day, then each 品號's 庫存數量 from INVI02, one query each (INVQ01 not opened).
/// </summary>
internal sealed partial class ItemQueryService
{
    /// <summary>After this many 庫存 lookups failing in a row the rest are not tried (ERP is likely in trouble).</summary>
    private const int RestockFailuresInRow = 3;

    /// <param name="stage">Told each step: REPORT with the 明細表 done of 2, then STOCK with the 品號 looked up of those to look up.</param>
    public async Task<RestockResult> QueryRestockAsync(RestockQuery query, IProgress<string> progress, CancellationToken cancellationToken,
        Action<string, int, int>? stage = null)
    {
        var watch = Stopwatch.StartNew();
        var sales = query.Sales;
        var orders = await GuardErpMessagesAsync(async () =>
        {
            var all = new List<SalesOrder>();
            stage?.Invoke("REPORT", 0, SalesReport.All.Count);
            for (var r = 0; r < SalesReport.All.Count; r++)
            {
                all.AddRange(await RunSalesReportAsync(SalesReport.All[r], sales, progress, cancellationToken));
                stage?.Invoke("REPORT", r + 1, SalesReport.All.Count);
            }
            return all;
        });
        var items = Restock.Summarize(orders);
        _log.Info("restock", $"{query.From:yyyy-MM-dd}~{query.To:yyyy-MM-dd} orders={orders.Count} items={items.Count} ms={watch.ElapsedMilliseconds}");

        var options = ItemQueryOptions.Default with { HeaderOnly = true };
        var failures = 0;
        var lookups = Math.Min(items.Count, Restock.MaxStockLookups);
        stage?.Invoke("STOCK", 0, lookups);
        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[i];
            if (i >= Restock.MaxStockLookups) { items[i] = item with { StockNote = $"品號超過 {Restock.MaxStockLookups} 個，未查庫存" }; continue; }
            if (failures >= RestockFailuresInRow) { items[i] = item with { StockNote = "前面連續查詢失敗，未查庫存" }; continue; }
            progress.Report($"ERP：查庫存 {i + 1}/{Math.Min(items.Count, Restock.MaxStockLookups)}（{item.ItemCode}）…");
            try
            {
                var result = await QueryAsync(item.ItemCode, options, progress, cancellationToken);
                items[i] = !result.Found ? item with { StockNote = "ERP 查無此品號" }
                    : Restock.Quantity(result.Value("庫存數量")) is { } stock ? item with { Stock = stock }
                    : item with { StockNote = "庫存數量讀不到" };
                failures = 0;
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException)
            {
                failures++;
                _log.Warn("restock", $"stock item={_log.Value(item.ItemCode)} failed: {ex.GetType().Name}: {ex.Message}");
                items[i] = item with { StockNote = "庫存查詢失敗" };
            }
            stage?.Invoke("STOCK", i + 1, lookups);
        }
        _log.Info("restock", $"done items={items.Count} stock={items.Count(i => i.Stock is not null)} ms={watch.ElapsedMilliseconds}");
        return new RestockResult(query, items, watch.Elapsed);
    }
}
