using Xunit;
namespace CYERPAutoInput.Tests;
/// <summary>
/// A five-item 任選 group whose 編號 sits in its middle, then a one-item group (user's order,
/// 2026-11-25; codes and numbers replaced with fictitious ones).
/// </summary>
public class ShopeeGroupSplitTests
{
    private static OcrBox B(string t, int x, int y, int w, int h) => new(t, x, y, w, h);
    [Fact]
    public void LongGroupKeepsItsLastItem()
    {
        const string N = "【志遠】§台灣製現貨§ 中醫護貝圖 足部反射區 人體小宇宙 肌肉壓痛點 骨節筋膜疼痛 脊髓";
        var order = new List<OcrBox> { B("訂單編號",50,12,59,17), B("2610000TEST01A",50,39,107,15), B("運送資訊",50,75,59,17),
            B("包裹1:蝦皮店到店 - 隔日到貨# TW200000000001K",50,107,301,17), B("共 6 個商品",99,149,65,15) };
        var items = new List<OcrBox> { B("編號",52,16,13,42), B("商品",83,30,26,15), B("單價",631,30,26,15), B("數量",691,30,27,15), B("小計",762,30,26,15),
            B("Any 2 enjoy 1% off",63,91,90,13),
            B(N,140,120,411,30), B("200",642,140,22,15), B("1",716,140,8,15), B("規格: 甲圖",140,160,104,13), B("商品選項貨號: X00021",140,176,111,13),
            B(N,140,190,411,30), B("200",642,210,22,15), B("1",716,210,8,15), B("規格: 乙圖",140,230,115,13), B("商品選項貨號: X00025",140,246,111,13),
            B(N,140,260,411,30), B("200",642,280,22,15), B("1",716,280,8,15), B("970",765,280,22,15), B("1",55,288,8,15), B("1,000",759,296,28,13),
            B("規格: 丙圖",140,299,126,13), B("商品選項貨號: X00020",140,316,111,12),
            B(N,140,330,411,30), B("200",642,350,22,15), B("1",716,350,8,15), B("規格: 丁圖",140,369,147,13), B("商品選項貨號: X00026",140,385,111,13),
            B(N,140,399,411,31), B("200",642,419,22,15), B("1",716,419,8,15), B("規格: 戊圖",140,439,158,13), B("商品選項貨號: X00022",140,455,111,13),
            B(N,146,488,395,30), B("2",54,516,8,15), B("200",635,516,22,15), B("1",710,516,8,15), B("200",766,516,22,15), B("規格: 己圖",146,528,104,13), B("商品選項貨號: X00023",146,544,111,13) };
        var amounts = new List<OcrBox> { B("買家實付金額",40,12,88,17), B("訂單金額",591,36,52,15), B("NT$1,170",719,36,57,15), B("買家支付運費",566,65,77,15),
            B("NT$0",744,65,32,15), B("優惠券",604,94,39,15), B("NT$0",744,94,32,15), B("賣場優惠券",579,122,64,15), B("-NT$30",733,122,43,15),
            B("NT$1,140",688,151,88,23), B("買家總支付金額",553,155,90,15) };
        var problems = new List<string>();
        var po = ShopeeScreenshot.ParseOrder(ShopeeScreenshot.MergeLines(order), problems);
        var (it, gr) = ShopeeScreenshot.ParseItems(ShopeeScreenshot.MergeLines(items), 828, problems);
        var am = ShopeeScreenshot.ParseAmounts(ShopeeScreenshot.MergeLines(amounts), problems);
        Assert.Equal(5, it.Count(i => i.Group == 1));
        Assert.Equal("X00023", Assert.Single(it, i => i.Group == 2).ItemCode);
        Assert.Contains(gr, g => g is { Index: 1, Shown: 970, Original: 1000 });
        var mapping = new ShopeeShotMapping("T01", "C0001", "蝦皮訂單", "D901", "S100", "P900", new Dictionary<string, string> { ["蝦皮店到店 - 隔日到貨"] = "F7" });
        var r = ShopeeScreenshot.Combine(po, it, gr, am, mapping, problems);
        Assert.Empty(r.Problems);
        Assert.NotNull(r.Order);
    }
}
