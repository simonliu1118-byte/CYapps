using System.Drawing;
using Xunit;

namespace CYERPAutoInput.Tests;

public class SlipImageTests
{
    private static void Fill(byte[] p, int w, int left, int top, int width, int height)
    {
        for (var y = top; y < top + height; y++)
            for (var x = left; x < left + width; x++) p[y * w + x] = 0;
    }

    /// <summary>A slip: a frame, a barcode with a 4-pixel module (bar 4, space 4, bar 4, space 8 …) and a short dotted divider.</summary>
    private static void Slip(byte[] p, int w, int left, int top)
    {
        Fill(p, w, left, top, 340, 2);
        Fill(p, w, left, top + 438, 340, 2);
        Fill(p, w, left, top, 2, 440);
        Fill(p, w, left + 338, top, 2, 440);
        for (var x = left + 10; x + 12 <= left + 330; x += 20)
        {
            Fill(p, w, x, top + 60, 4, 100);
            Fill(p, w, x + 8, top + 60, 4, 100);
        }
        for (var x = left + 10; x < left + 330; x += 8) Fill(p, w, x, top + 240, 4, 2);
    }

    /// <summary>An A4-like page of four cells divided by dashed lines; slips in the top left and bottom right.</summary>
    private static GrayImage FourCells()
    {
        const int w = 800, h = 1100;
        var p = GrayImage.Blank(w, h).Pixels;
        for (var y = 0; y < h; y += 12) Fill(p, w, 399, y, 2, 6);
        for (var x = 0; x < w; x += 12) Fill(p, w, x, 549, 6, 2);
        Slip(p, w, 30, 60);
        Slip(p, w, 430, 600);
        return new GrayImage(w, h, p);
    }

    [Fact]
    public void Cuts_the_slips_out_of_a_four_cell_page()
    {
        var page = FourCells();
        var slips = SlipImage.FindSlips(page);
        Assert.Equal(2, slips.Count);
        var first = slips[0];
        // The frame with a margin, inside its cell (left of 399, above 549).
        Assert.True(first.Left <= 30 && first.Top <= 60 && first.Right >= 370 && first.Bottom >= 500);
        Assert.True(first.Right < 399 && first.Bottom < 549);
        var second = slips[1];
        Assert.True(second.Left <= 430 && second.Left > 401 && second.Top <= 600 && second.Top > 551);
        Assert.Equal(4, SlipImage.BarcodeModule(page, first));
    }

    [Fact]
    public void A_page_without_cut_lines_is_one_slip()
    {
        const int w = 420, h = 520;
        var p = GrayImage.Blank(w, h).Pixels;
        Slip(p, w, 40, 40);
        var slips = SlipImage.FindSlips(new GrayImage(w, h, p));
        Assert.Single(slips);
        Assert.True(slips[0].Left <= 40 && slips[0].Right >= 380);
    }

    [Fact]
    public void Scales_by_whole_numbers_keeping_the_bars_whole()
    {
        var page = FourCells();
        var area = SlipImage.FindSlips(page)[0];

        var down = SlipImage.Plan(page, area, 200, 300);
        Assert.Equal((1, 2, 4), (down.Up, down.Down, down.Module));
        Assert.Equal((area.Width / 2, area.Height / 2), (down.Width, down.Height));
        var small = SlipImage.Render(page, down);
        Assert.All(small.Pixels, v => Assert.True(v is 0 or 255));
        Assert.Equal(2, SlipImage.BarcodeModule(small, new Rectangle(0, 0, small.Width, small.Height)));

        // 3 would fit but splits a 4-pixel bar; 4 divides it.
        Assert.Equal(4, SlipImage.Plan(page, area, 150, 300).Down);

        var up = SlipImage.Plan(page, area, 1000, 1000);
        Assert.Equal((2, 1), (up.Up, up.Down));
        var big = SlipImage.Render(page, up);
        Assert.Equal(8, SlipImage.BarcodeModule(big, new Rectangle(0, 0, big.Width, big.Height)));
    }
}
