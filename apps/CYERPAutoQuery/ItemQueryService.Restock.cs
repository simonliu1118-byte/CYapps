using System.Diagnostics;

namespace CYERPAutoQuery;

/// <summary>
/// 進貨輔助 (CY Web, user 2026-10-20): COPR15 and COPR16 for every customer in the range, summed by
/// 品號 and day, then the 品號's 庫存數量 from INVI02 by the batch (進階設定 OR-ed codes, 資料瀏覽 轉出全部).
/// </summary>
internal sealed partial class ItemQueryService
{
    /// <summary>After this many 庫存 batches failing in a row the rest are not tried (ERP is likely in trouble).</summary>
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

        // 庫存 by the batch (user 2026-10-20: one INVI02 query per 品號 was too slow): every code
        // OR-ed in 進階設定, 資料瀏覽 exported at once. A batch the condition box does not take
        // whole is made smaller; a batch that fails otherwise is marked and the next one tried.
        var stocks = new Dictionary<string, string>(StringComparer.Ordinal);
        var asked = new HashSet<string>(StringComparer.Ordinal);
        var failed = new HashSet<string>(StringComparer.Ordinal);
        var pending = items.Take(Restock.MaxStockLookups).Select(i => i.ItemCode).Where(ItemCodeConditions.IsUsable).ToList();
        var lookups = pending.Count;
        var size = ItemCodeConditions.MaxBatch;
        var failures = 0;
        stage?.Invoke("STOCK", 0, lookups);
        while (pending.Count > 0 && failures < RestockFailuresInRow)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var take = ItemCodeConditions.Batch(pending, ConditionLimit, size);
            var batch = pending.Take(take).ToList();
            progress.Report($"ERP：查庫存 {lookups - pending.Count + 1}～{lookups - pending.Count + take}/{lookups}…");
            try
            {
                foreach (var row in await ListItemsAsync(batch, progress, cancellationToken))
                {
                    var code = ItemSearchResult.Value(row, "品號").Trim();
                    if (Restock.Quantity(ItemSearchResult.Value(row, "庫存數量")) is { } stock) stocks[code] = stock;
                }
                asked.UnionWith(batch);
                failures = 0;
            }
            catch (ConditionNotTakenException ex) when (take > 1)
            {
                size = Math.Max(1, take / 2);
                _log.Warn("restock", $"condition of {take} codes not taken room={ex.Room?.ToString() ?? "?"}; next batch {size}");
                continue;
            }
            catch (InvalidOperationException ex) when (ex is not QueryFailure { Code: ErpAgentErrorCode.ERP_BUSY })
            {
                failures++;
                failed.UnionWith(batch);
                _log.Warn("restock", $"stock batch of {take} failed: {ex.GetType().Name}: {ex.Message}");
            }
            catch (InvalidDataException ex)
            {
                failures++;
                failed.UnionWith(batch);
                _log.Warn("restock", $"stock batch of {take} unreadable: {ex.Message}");
            }
            pending.RemoveRange(0, take);
            stage?.Invoke("STOCK", lookups - pending.Count, lookups);
        }
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            items[i] = stocks.TryGetValue(item.ItemCode, out var stock) ? item with { Stock = stock }
                : asked.Contains(item.ItemCode) ? item with { StockNote = "ERP 查無此品號或庫存數量讀不到" }
                : failed.Contains(item.ItemCode) ? item with { StockNote = "庫存查詢失敗" }
                : !ItemCodeConditions.IsUsable(item.ItemCode) ? item with { StockNote = "品號含特殊字元，未查庫存" }
                : i >= Restock.MaxStockLookups ? item with { StockNote = $"品號超過 {Restock.MaxStockLookups} 個，未查庫存" }
                : item with { StockNote = "前面連續查詢失敗，未查庫存" };
        }
        _log.Info("restock", $"done items={items.Count} stock={items.Count(i => i.Stock is not null)} ms={watch.ElapsedMilliseconds}");
        return new RestockResult(query, items, watch.Elapsed);
    }
}
