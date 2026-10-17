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
    public void Slips_side_by_side_are_not_cut_at_their_own_dotted_dividers()
    {
        // MO店+ 全家 (user, 2026-10-17): two slips in the top cells; their dotted dividers
        // (寄件專用｜物流專用 …) line up across the page and must not count as the cut.
        const int w = 800, h = 1100;
        var p = GrayImage.Blank(w, h).Pixels;
        for (var y = 0; y < h; y += 12) Fill(p, w, 399, y, 2, 6);
        for (var x = 0; x < w; x += 12) Fill(p, w, x, 549, 6, 2);
        foreach (var left in new[] { 30, 430 })
        {
            Slip(p, w, left, 60);
            foreach (var y in new[] { 180, 420 })
                for (var x = left + 10; x < left + 330; x += 8) Fill(p, w, x, y, 4, 2);
        }
        var slips = SlipImage.FindSlips(new GrayImage(w, h, p));
        Assert.Equal(2, slips.Count);
        Assert.All(slips, s => Assert.True(s.Top <= 60 && s.Bottom >= 500 && s.Bottom < 549));
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

    /// <summary>A 7-11 交貨便 slip: a solid frame with two boxes inside, short dashed cut marks to its right and below.</summary>
    private static void SevenSlip(byte[] p, int w, int left, int top)
    {
        Fill(p, w, left, top, 300, 3);
        Fill(p, w, left, top + 417, 300, 3);
        Fill(p, w, left, top, 3, 420);
        Fill(p, w, left + 297, top, 3, 420);
        Fill(p, w, left + 10, top + 200, 280, 2);
        Fill(p, w, left + 10, top + 300, 280, 2);
        Fill(p, w, left + 10, top + 200, 2, 100);
        Fill(p, w, left + 288, top + 200, 2, 100);
        for (var x = left + 40; x < left + 120; x += 8) Fill(p, w, x, top + 40, 4, 100);
        for (var y = top - 10; y < top + 440; y += 10) Fill(p, w, left + 312, y, 2, 5);
        for (var x = left - 10; x < left + 320; x += 10) Fill(p, w, x, top + 432, 5, 2);
    }

    [Fact]
    public void Seven_slip_is_its_solid_frame_without_the_cut_marks()
    {
        const int w = 900, h = 1270;
        var p = GrayImage.Blank(w, h).Pixels;
        SevenSlip(p, w, 60, 60);
        var slip = Assert.Single(SlipImage.FindFramedSlips(new GrayImage(w, h, p)));
        var pad = Math.Max(4, w / 200);
        Assert.Equal(new Rectangle(60 - pad, 60 - pad, 300 + 2 * pad, 420 + 2 * pad), slip);
    }

    [Fact]
    public void Several_seven_slips_on_a_page_come_in_reading_order()
    {
        const int w = 900, h = 1270;
        var p = GrayImage.Blank(w, h).Pixels;
        SevenSlip(p, w, 60, 60);
        SevenSlip(p, w, 480, 60);
        SevenSlip(p, w, 60, 600);
        var slips = SlipImage.FindFramedSlips(new GrayImage(w, h, p));
        Assert.Equal(3, slips.Count);
        Assert.True(slips[0].Left < 100 && slips[0].Top < 100);
        Assert.True(slips[1].Left > 400 && slips[1].Top < 100);
        Assert.True(slips[2].Left < 100 && slips[2].Top > 500);
    }
}
