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

    public async Task<RestockResult> QueryRestockAsync(RestockQuery query, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var sales = query.Sales;
        var orders = await GuardErpMessagesAsync(async () =>
        {
            var all = new List<SalesOrder>();
            foreach (var report in SalesReport.All)
                all.AddRange(await RunSalesReportAsync(report, sales, progress, cancellationToken));
            return all;
        });
        var items = Restock.Summarize(orders);
        _log.Info("restock", $"{query.From:yyyy-MM-dd}~{query.To:yyyy-MM-dd} orders={orders.Count} items={items.Count} ms={watch.ElapsedMilliseconds}");

        var options = ItemQueryOptions.Default with { HeaderOnly = true };
        var failures = 0;
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
                items[i] = result.Found
                    ? item with { Stock = result.Value("庫存數量").Trim() }
                    : item with { StockNote = "ERP 查無此品號" };
                failures = 0;
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException)
            {
                failures++;
                _log.Warn("restock", $"stock item={_log.Value(item.ItemCode)} failed: {ex.GetType().Name}: {ex.Message}");
                items[i] = item with { StockNote = "庫存查詢失敗" };
            }
        }
        _log.Info("restock", $"done items={items.Count} stock={items.Count(i => i.Stock is not null)} ms={watch.ElapsedMilliseconds}");
        return new RestockResult(query, items, watch.Elapsed);
    }
}
