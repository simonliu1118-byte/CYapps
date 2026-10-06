using Xunit;

namespace CYERPAutoInput.Tests;

public class ProbeRedactionTests
{
    [Theory]
    [InlineData(0x2B)] // push button
    [InlineData(0x25)] // page tab
    [InlineData(0x19)] // column header
    [InlineData(0x0C)] // menu item
    public void Structural_msaa_names_are_kept(int role) =>
        Assert.True(ProbeRedaction.KeepMsaaName(role, diagnostic: false));

    [Theory]
    [InlineData(0x2A)] // editable text
    [InlineData(0x1D)] // cell
    [InlineData(0x1C)] // row
    [InlineData(0x22)] // list item
    [InlineData(0x29)] // static text (may show document data)
    public void Data_bearing_msaa_names_are_redacted_unless_diagnostic(int role)
    {
        Assert.False(ProbeRedaction.KeepMsaaName(role, diagnostic: false));
        Assert.True(ProbeRedaction.KeepMsaaName(role, diagnostic: true));
    }

    [Theory]
    [InlineData("TDBEdit", false)]
    [InlineData("TcxDBImageComboBox", false)]
    [InlineData("TcxCustomInnerTextEdit", false)]
    [InlineData("TcxTabSheet", true)]
    [InlineData("TButton", true)]
    [InlineData("TdxRibbon", true)]
    [InlineData("TcxGridSite", false)]
    public void Window_text_is_kept_only_for_structural_classes(string className, bool keep) =>
        Assert.Equal(keep, ProbeRedaction.KeepWindowText(className, diagnostic: false));

    [Fact]
    public void Show_redacts_to_length_and_quotes_kept_text()
    {
        Assert.Equal("<5 chars>", ProbeRedaction.Show("A1234", keep: false));
        Assert.Equal("\"新增\"", ProbeRedaction.Show("新增", keep: true));
        Assert.Equal("<0 chars>", ProbeRedaction.Show(null, keep: false));
    }
}
