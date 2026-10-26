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
    public void Lines_are_never_broken_and_size_follows_the_room()
    {
        Assert.Null(SlipNote.Fit("", Width));
        // Few words in a lot of room: the largest size.
        Assert.Equal(SlipNote.MaxPoints, SlipNote.Fit("感謝您的購買", Width, 40)!.Value.Points);
        // Three lines in the least room: not at 14 pt; under a 全家 slip down to 10 pt.
        var three = "收到商品如有問題請先聊聊詢問\n志遠夥伴竭誠為您服務\n發票委託加值中心代開";
        Assert.Null(SlipNote.Fit(three, Width, SlipNote.BlockHeightMm));
        var family = SlipNote.Fit(three, Width, 16, SlipNote.FamilyMinPoints)!.Value;
        Assert.Equal(3, family.Lines.Count);
        Assert.True(family.Points >= SlipNote.FamilyMinPoints);
        Assert.True(SlipNote.Fit(three, Width, 30)!.Value.Points >= SlipNote.MinPoints);
    }

    [Fact]
    public void A_line_longer_than_one_line_at_14_pt_is_not_taken()
    {
        Assert.True(SlipNote.LineFits(new string('好', 19), Width));
        Assert.False(SlipNote.LineFits(new string('好', 20), Width));
        Assert.True(SlipNote.LineFits(new string('A', 38), Width));
    }

    [Fact]
    public void Symbols_get_their_own_font_and_pairs_stay_whole()
    {
        Assert.Equal(["📹請務必錄影開箱"], SlipNote.Lines("📹\uFE0F請務必錄影開箱"));
        Assert.Equal([("⚠", true), ("勿使用", false), ("🔪", true), ("刀片", false)], SlipNote.Runs("⚠勿使用🔪刀片"));
        Assert.False(SlipNote.IsSymbol('★' - 0x100));
    }
}
