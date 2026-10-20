using CYERPAutoInput;
using Xunit;

namespace CYERPAutoInput.Tests;

public sealed class SlipNoteTests
{
    // About 3.53 mm a Chinese character at 10 pt, half that otherwise.
    private static double Width(string line) => line.Sum(c => c < 0x2E80 ? 1.76 : 3.53);

    [Fact]
    public void Lines_drop_blank_ends_and_trailing_spaces()
    {
        Assert.Empty(SlipNote.Lines(null));
        Assert.Empty(SlipNote.Lines(" \r\n \n"));
        Assert.Equal(["感謝購買", "", "有問題請聊聊  我們"], SlipNote.Lines("\r\n感謝購買  \r\n\r\n有問題請聊聊  我們\n\n"));
    }

    [Fact]
    public void Check_limits_lines_and_width()
    {
        Assert.Null(SlipNote.Check("", Width));
        Assert.Null(SlipNote.Check(new string('好', 27) + "\n" + new string('A', 54) + "\n第三行", Width));
        Assert.Contains("最多 3 行", SlipNote.Check("一\n二\n三\n四", Width));
        Assert.Contains("第 2 行太長", SlipNote.Check("一\n" + new string('好', 28), Width));
    }
}
