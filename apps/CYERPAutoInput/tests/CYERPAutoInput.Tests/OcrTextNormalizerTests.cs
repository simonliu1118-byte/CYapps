using Xunit;

namespace CYERPAutoInput.Tests;

public class OcrTextNormalizerTests
{
    [Theory]
    [InlineData("数量", "數量")]
    [InlineData("库别", "庫別")]
    [InlineData("换算单位", "換算單位")]
    [InlineData("发票资料（一）", "發票資料(一)")]
    [InlineData(" 品 號 ", "品號")]
    [InlineData("全形　空白", "全形空白")]
    [InlineData(null, "")]
    public void Normalize_maps_common_ocr_variants(string? input, string expected) =>
        Assert.Equal(expected, OcrTextNormalizer.Normalize(input));
}
