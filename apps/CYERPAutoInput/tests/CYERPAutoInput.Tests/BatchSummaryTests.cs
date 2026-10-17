using Xunit;

namespace CYERPAutoInput.Tests;

public class BatchSummaryTests
{
    [Fact]
    public void Sections_become_rows_with_status_order_and_detail()
    {
        var rows = BatchSummary.Rows(
        [
            new("已完成 2 張：", ["90000000000001 → T01-20260101001，發票 AB12345678", "90000000000002 → T01-20260101002"]),
            new("需人工處理 1 張（未輸入 ERP）：", ["90000000000003：物流商「測試物流」沒有設定貨運別"]),
            new("需要人工處理 1 筆：", ["訂單 2601010ABCDEFG：庫存不足"]),
            new("其他事項：", ["配送方式「冷凍」有 1 筆，程式不處理，請人工出貨。"]),
            new("重複略過 0 張：", [])
        ]);
        Assert.Equal(5, rows.Count);
        Assert.Equal(new BatchSummaryRow("已完成", "90000000000001", "T01-20260101001，發票 AB12345678"), rows[0]);
        Assert.False(rows[0].NeedsPerson);
        Assert.Equal(new BatchSummaryRow("需人工處理", "90000000000003", "物流商「測試物流」沒有設定貨運別"), rows[2]);
        Assert.True(rows[2].NeedsPerson);
        Assert.Equal("2601010ABCDEFG", rows[3].OrderSn);
        Assert.Equal("需要人工處理", rows[3].Status);
        Assert.Equal(("其他事項", ""), (rows[4].Status, rows[4].OrderSn));
        Assert.StartsWith("配送方式", rows[4].Detail);
        var chat = Assert.Single(BatchSummary.Rows([new("已用聊聊通知買家 1 筆：", ["訂單 2601010ABCDEFG（發票）"])]));
        Assert.Equal(("2601010ABCDEFG", "（發票）"), (chat.OrderSn, chat.Detail));
    }

    [Fact]
    public void Copy_is_tab_separated_with_the_stop()
    {
        var text = BatchSummary.Tsv("已儲存 1 / 2 張。", [new BatchSummaryRow("已完成", "90000000000001", "T01-1")], "停止於：訂單 90000000000002");
        Assert.Contains("已完成\t90000000000001\tT01-1", text);
        Assert.EndsWith("停止於：訂單 90000000000002", text);
    }
}
