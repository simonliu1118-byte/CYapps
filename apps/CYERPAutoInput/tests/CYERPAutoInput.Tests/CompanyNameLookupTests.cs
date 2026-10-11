using Xunit;

namespace CYERPAutoInput.Tests;

/// <summary>Response parsing only; the tests never call the registry API.</summary>
public class CompanyNameLookupTests
{
    [Theory]
    [InlineData("12345678", true)]
    [InlineData("1234567", false)]
    [InlineData("1234567A", false)]
    [InlineData("", false)]
    public void Recognizes_a_ban(string value, bool expected) => Assert.Equal(expected, CompanyNameResponse.IsBan(value));

    [Fact]
    public void Reads_the_name_of_the_first_record()
    {
        const string json = """[{"Business_Accounting_NO":"12345678","Company_Name":" 測試股份有限公司 "}]""";
        Assert.Equal("測試股份有限公司", CompanyNameResponse.ParseName(json, "Company_Name"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("""[{"Other":"x"}]""")]
    [InlineData("""{"Company_Name":"x"}""")]
    public void No_match_gives_an_empty_name(string json) => Assert.Equal(string.Empty, CompanyNameResponse.ParseName(json, "Company_Name"));
}
