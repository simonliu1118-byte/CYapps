using CYERPAutoInput;
using Xunit;

namespace CYERPAutoInput.Tests;

public sealed class SlipNoteTests
{
    // A Chinese character one em wide, anything else half.
    private static double Width(string line, float points) => line.Sum(c => c < 0x2E80 ? 0.5 : 1.0) * points * 25.4 / 72;

    [Fact]
    public void Lines_drop_blank_ends_and_trailing_spaces()
    {
        Assert.Empty(SlipNote.Lines(null));
        Assert.Empty(SlipNote.Lines(" \r\n \n"));
        Assert.Equal(["感謝購買", "", "有問題請聊聊  我們"], SlipNote.Lines("\r\n感謝購買  \r\n\r\n有問題請聊聊  我們\n\n"));
    }

    [Fact]
    public void Few_words_are_large_many_wrap_smaller()
    {
        Assert.Null(SlipNote.Fit("", Width));
        var short_ = SlipNote.Fit("感謝您的購買", Width)!.Value;
        Assert.Equal(SlipNote.MaxPoints, short_.Points);
        Assert.Equal(["感謝您的購買"], short_.Lines);

        var two = SlipNote.Fit("感謝您的購買\n有問題請聊聊", Width)!.Value;
        Assert.True(two.Points < SlipNote.MaxPoints);
        Assert.Equal(2, two.Lines.Count);

        // 60 characters do not fit one line: wrapped, smaller, every line within the block.
        var many = SlipNote.Fit(new string('好', 60), Width)!.Value;
        Assert.True(many.Lines.Count >= 3);
        Assert.True(many.Points >= SlipNote.MinPoints);
        Assert.All(many.Lines, l => Assert.True(Width(l, many.Points) <= SlipNote.BlockWidthMm));
        Assert.Equal(60, many.Lines.Sum(l => l.Length));
        Assert.True(many.Lines.Count * SlipNote.LineHeightMm(many.Points) <= SlipNote.BlockHeightMm);
    }

    [Fact]
    public void Latin_words_stay_whole_and_too_much_is_refused()
    {
        var fit = SlipNote.Fit(string.Join(' ', Enumerable.Repeat("thanks", 20)), Width)!.Value;
        Assert.All(fit.Lines, l => Assert.DoesNotContain(l.Split(' '), w => w.Length > 0 && w != "thanks"));
        Assert.Null(SlipNote.Check("感謝", Width));
        Assert.Null(SlipNote.Fit(new string('好', 200), Width));
        Assert.Contains("太多", SlipNote.Check(new string('好', 200), Width));
    }
}
