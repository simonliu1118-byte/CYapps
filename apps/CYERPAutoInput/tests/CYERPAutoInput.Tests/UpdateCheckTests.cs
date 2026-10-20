using Xunit;

namespace CYERPAutoInput.Tests;

public class UpdateCheckTests
{
    private const string Base = "https://github.com/simonliu1118-byte/CYapps/releases/download/";

    private static string Release(string tag, string version, bool draft = false, bool pre = false, string? host = null) => $$"""
        {"tag_name":"{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(pre ? "true" : "false")}},"assets":[
          {"name":"CYERPAutoInput_V{{version}}.zip","browser_download_url":"{{host ?? Base}}{{tag}}/CYERPAutoInput_V{{version}}.zip"},
          {"name":"CYERPAutoInput_V{{version}}.zip.sha256","browser_download_url":"{{host ?? Base}}{{tag}}/CYERPAutoInput_V{{version}}.zip.sha256"}]}
        """;

    [Fact]
    public void Picks_the_newest_formal_release_of_this_project_only()
    {
        var json = "[" + string.Join(",",
            Release("cyerp-auto-input-v0.3.1", "0.3.1"),
            Release("cyerp-auto-input-v0.4.0", "0.4.0", draft: true),
            Release("cyerp-auto-input-v0.3.5", "0.3.5", pre: true),
            Release("cyinvoice-v9.0.0", "9.0.0"),
            Release("cyerp-auto-input-v0.3.2", "0.3.2"),
            Release("cyerp-auto-input-v0.3.9", "0.3.9", host: "https://example.com/")) + "]";
        var offer = UpdateCheck.Newest(json, new Version(0, 3, 0));
        Assert.NotNull(offer);
        Assert.Equal(new Version(0, 3, 2), offer.Version);
        Assert.Equal("CYERPAutoInput_V0.3.2.zip", offer.ZipName);
        Assert.EndsWith("/cyerp-auto-input-v0.3.2/CYERPAutoInput_V0.3.2.zip.sha256", offer.ChecksumUrl);
    }

    [Fact]
    public void The_same_version_or_a_build_of_it_is_not_an_update()
    {
        var json = "[" + Release("cyerp-auto-input-v0.3.1", "0.3.1") + "]";
        Assert.Null(UpdateCheck.Newest(json, new Version(0, 3, 1)));
        Assert.Null(UpdateCheck.Newest(json, new Version(0, 4, 0)));
    }

    [Fact]
    public void Reads_the_hash_for_the_zip_from_the_checksum_file()
    {
        var hash = new string('a', 64);
        Assert.Equal(hash, UpdateCheck.ExpectedSha256($"{hash.ToUpperInvariant()}  CYERPAutoInput_V0.3.1.zip\r\n", "CYERPAutoInput_V0.3.1.zip"));
        Assert.Null(UpdateCheck.ExpectedSha256($"{hash}  other.zip", "CYERPAutoInput_V0.3.1.zip"));
        Assert.Null(UpdateCheck.ExpectedSha256("abc  CYERPAutoInput_V0.3.1.zip", "CYERPAutoInput_V0.3.1.zip"));
    }

    [Theory]
    [InlineData("ok\r\n0.3.4\r\n\r\n", true, "0.3.4", "")]
    [InlineData("\uFEFFfailed\n0.3.4\n複製失敗：拒絕存取；已還原原本的版本\n", false, "0.3.4", "複製失敗：拒絕存取；已還原原本的版本")]
    public void Reads_the_outcome_the_update_script_left(string text, bool ok, string version, string detail) =>
        Assert.Equal(new UpdateOutcome(ok, version, detail), UpdateOutcome.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("ok")]
    [InlineData("maybe\n0.3.4")]
    public void Ignores_an_unreadable_outcome(string text) => Assert.Null(UpdateOutcome.Parse(text));
}
