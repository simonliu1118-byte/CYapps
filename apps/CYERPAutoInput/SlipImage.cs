using System.Drawing;

namespace CYERPAutoInput;

/// <summary>A grayscale picture as bytes (0 black … 255 white), row by row.</summary>
internal sealed class GrayImage(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels;
    public byte this[int x, int y] => Pixels[y * Width + x];

    public static GrayImage Blank(int width, int height) => new(width, height, Enumerable.Repeat((byte)255, width * height).ToArray());
}

/// <summary>How a slip is printed: its area in the page, the scale (Up／Down, one of them 1) and the size in printer dots.</summary>
internal sealed record SlipPlan(Rectangle Area, int Up, int Down, int Module, int Width, int Height);

/// <summary>
/// Thermal printing of convenience-store slips (user, 2026-10-14: 全家 printed through Chrome at
/// page 2, 165 % looked sharp but often did not scan). A slip is a picture; scaling it by a
/// fraction makes its barcode's bars uneven (a 4-pixel module becomes 6 or 7 dots), which
/// scanners refuse. So the slip is cut from its page (A4 with four cells divided by dashed
/// lines, or a single slip), made pure black and white, and scaled by a whole number up or
/// down chosen so the barcode's narrowest bar stays a whole number of printer dots, then
/// printed at that size. Pure: no Windows drawing, unit tested.
/// </summary>
internal static class SlipImage
{
    private const int Dark = 128;

    /// <summary>The box around everything darker than mid grey; null on a blank page.</summary>
    public static Rectangle? InkBounds(GrayImage page)
    {
        int left = page.Width, top = page.Height, right = -1, bottom = -1;
        for (var y = 0; y < page.Height; y++)
            for (var x = 0; x < page.Width; x++)
            {
                if (page[x, y] >= Dark) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        return right < 0 ? null : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    /// <summary>
    /// The slips on a page: the dashed cut lines (long, broken lines across the page) divide it
    /// into four cells; each cell with ink gives the box around its ink, with a margin for the
    /// barcodes' quiet zones. A page without cut lines both ways is one slip.
    /// </summary>
    public static List<Rectangle> FindSlips(GrayImage page)
    {
        var w = page.Width;
        var h = page.Height;
        var ink = new bool[w * h];
        for (var i = 0; i < ink.Length; i++) ink[i] = page.Pixels[i] < Dark;

        var cutColumns = NearMiddle(DashedLines(ink, w, h, vertical: true), w);
        var cutRows = NearMiddle(DashedLines(ink, w, h, vertical: false), h);
        // Four cells have cut lines both ways; a single slip's own dotted divider (between its
        // frame's sides, as wide as the picture) is one way only.
        if (cutColumns.Count == 0 || cutRows.Count == 0) { cutColumns.Clear(); cutRows.Clear(); }
        foreach (var x in cutColumns)
            for (var dx = -2; dx <= 2; dx++)
                if (x + dx >= 0 && x + dx < w)
                    for (var y = 0; y < h; y++) ink[y * w + x + dx] = false;
        foreach (var y in cutRows)
            for (var dy = -2; dy <= 2; dy++)
                if (y + dy >= 0 && y + dy < h)
                    Array.Fill(ink, false, (y + dy) * w, w);

        int[] xs = cutColumns.Count > 0 ? [0, cutColumns[cutColumns.Count / 2], w] : [0, w];
        int[] ys = cutRows.Count > 0 ? [0, cutRows[cutRows.Count / 2], h] : [0, h];
        var pad = Math.Max(4, w / 80);
        var slips = new List<Rectangle>();
        for (var r = 0; r + 1 < ys.Length; r++)
            for (var c = 0; c + 1 < xs.Length; c++)
            {
                int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1, count = 0;
                for (var y = ys[r]; y < ys[r + 1]; y++)
                    for (var x = xs[c]; x < xs[c + 1]; x++)
                    {
                        if (!ink[y * w + x]) continue;
                        count++;
                        if (x < left) left = x;
                        if (x > right) right = x;
                        if (y < top) top = y;
                        if (y > bottom) bottom = y;
                    }
                var area = (long)(xs[c + 1] - xs[c]) * (ys[r + 1] - ys[r]);
                if (count < area / 500) continue; // a few specks, not a slip
                var cell = Rectangle.FromLTRB(xs[c], ys[r], xs[c + 1], ys[r + 1]);
                var box = Rectangle.FromLTRB(left - pad, top - pad, right + 1 + pad, bottom + 1 + pad);
                box.Intersect(cell);
                if (cutColumns.Count > 0 && c == 0) box.Width = Math.Min(box.Width, cutColumns[cutColumns.Count / 2] - 3 - box.Left);
                if (cutRows.Count > 0 && r == 0) box.Height = Math.Min(box.Height, cutRows[cutRows.Count / 2] - 3 - box.Top);
                slips.Add(box);
            }
        return slips;
    }

    /// <summary>
    /// MO店+ 7-11 交貨便 (user, 2026-10-15: an A4 page with the 10×14 cm slip in a solid frame at
    /// the top left, short dashed cut marks beside it; more slips sit on the same page): each slip
    /// is a large connected frame of ink. The dashes are small pieces and the boxes inside a
    /// frame are inside it, so neither counts. In reading order, top to bottom, left to right.
    /// </summary>
    public static List<Rectangle> FindFramedSlips(GrayImage page)
    {
        var w = page.Width;
        var h = page.Height;
        var seen = new bool[w * h];
        var stack = new Stack<int>();
        var frames = new List<Rectangle>();
        for (var start = 0; start < seen.Length; start++)
        {
            if (seen[start] || page.Pixels[start] >= Dark) continue;
            int left = start % w, right = left, top = start / w, bottom = top;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                int x = i % w, y = i / w;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        var n = ny * w + nx;
                        if (seen[n] || page.Pixels[n] >= Dark) continue;
                        seen[n] = true;
                        stack.Push(n);
                    }
            }
            if (right - left + 1 >= w / 6 && bottom - top + 1 >= h / 8) frames.Add(Rectangle.FromLTRB(left, top, right + 1, bottom + 1));
        }
        // A thin margin only: the frame is the slip's edge (the slip is as wide as the label paper).
        var pad = Math.Max(4, w / 200);
        return frames
            .Where(f => !frames.Any(o => o != f && o.Contains(f)))
            .Select(f => Rectangle.Intersect(Rectangle.Inflate(f, pad, pad), new Rectangle(0, 0, w, h)))
            .OrderBy(f => f.Top / Math.Max(1, h / 10)).ThenBy(f => f.Left)
            .ToList();
    }

    /// <summary>
    /// Positions of dashed lines: across most of the page (from the first ink to the last over
    /// 80 % of it), a quarter or more inked, broken into many dashes, and thin. A slip's own
    /// dotted divider is only as long as the slip and is not taken.
    /// </summary>
    /// <summary>
    /// The page is cut in half each way, so of the dashed lines found only the one nearest the
    /// middle (its few pixel rows) is the cut. Two MO店+ 全家 slips side by side (user,
    /// 2026-10-17) carry their own dotted dividers (寄件專用｜物流專用, inside 取件專用) across
    /// the page; taken for the cut, they printed each slip in pieces.
    /// </summary>
    private static List<int> NearMiddle(List<int> lines, int size)
    {
        if (lines.Count == 0) return lines;
        var middle = lines.MinBy(l => Math.Abs(l - size / 2));
        if (Math.Abs(middle - size / 2) > size / 6) return [];
        return lines.Where(l => Math.Abs(l - middle) <= 8).ToList();
    }

    private static List<int> DashedLines(bool[] ink, int w, int h, bool vertical)
    {
        var lines = new List<int>();
        var length = vertical ? h : w;
        for (var i = 0; i < (vertical ? w : h); i++)
        {
            int count = 0, changes = 0, first = -1, last = -1;
            var previous = false;
            for (var j = 0; j < length; j++)
            {
                var v = vertical ? ink[j * w + i] : ink[i * w + j];
                if (v) { count++; if (first < 0) first = j; last = j; }
                if (j > 0 && v != previous) changes++;
                previous = v;
            }
            if (count > length / 4 && changes > 30 && last - first > length * 8 / 10) lines.Add(i);
        }
        // A cut line is thin; rows through a barcode (with the slip's frame on both sides) can
        // look the same, but there are dozens of them together.
        var thin = new List<int>();
        for (var start = 0; start < lines.Count;)
        {
            var end = start;
            while (end + 1 < lines.Count && lines[end + 1] - lines[end] <= 2) end++;
            if (lines[end] - lines[start] < 8) thin.AddRange(lines.Skip(start).Take(end - start + 1));
            start = end + 1;
        }
        return thin;
    }

    /// <summary>
    /// The width of a barcode's narrowest bar or space in the area: in rows crossed by many
    /// bars, the shortest run that is common (one in ten runs or more). 0 when there is no
    /// barcode.
    /// </summary>
    public static int BarcodeModule(GrayImage page, Rectangle area)
    {
        var counts = new Dictionary<int, int>();
        var total = 0;
        for (var y = area.Top; y < area.Bottom; y += 3)
        {
            var runs = new List<int>();
            var current = page[area.Left, y] < Dark;
            var length = 0;
            for (var x = area.Left; x < area.Right; x++)
            {
                var v = page[x, y] < Dark;
                if (v == current) { length++; continue; }
                runs.Add(length);
                current = v;
                length = 1;
            }
            if (runs.Count < 50) continue; // not across a barcode
            foreach (var run in runs.Skip(1)) { counts[run] = counts.GetValueOrDefault(run) + 1; total++; }
        }
        if (total == 0) return 0;
        return counts.Where(p => p.Value * 10 >= total).Select(p => p.Key).DefaultIfEmpty(0).Min();
    }

    /// <summary>
    /// The scale for the label: the largest whole-number enlargement, or the smallest
    /// whole-number reduction, that fits <paramref name="labelWidth"/> × <paramref name="labelHeight"/>
    /// dots. A reduction is taken only when it divides the barcode's narrowest bar evenly (so
    /// every bar stays whole), unless none that fits does.
    /// </summary>
    public static SlipPlan Plan(GrayImage page, Rectangle area, int labelWidth, int labelHeight)
    {
        var module = BarcodeModule(page, area);
        bool Fits(int up, int down) => area.Width * up / down <= labelWidth && area.Height * up / down <= labelHeight;
        if (Fits(1, 1))
        {
            var up = 1;
            while (up < 8 && Fits(up + 1, 1)) up++;
            return new SlipPlan(area, up, 1, module, area.Width * up, area.Height * up);
        }
        var fitting = Enumerable.Range(2, 30).Where(d => Fits(1, d)).ToList();
        var down = fitting.FirstOrDefault(d => module == 0 || module % d == 0);
        if (down == 0) down = fitting.Count > 0 ? fitting[0] : 32;
        return new SlipPlan(area, 1, down, module, area.Width / down, area.Height / down);
    }

    /// <summary>The slip in pure black and white at the planned size (a reduction averages each block).</summary>
    public static GrayImage Render(GrayImage page, SlipPlan plan)
    {
        var a = plan.Area;
        var outW = plan.Width;
        var outH = plan.Height;
        var result = new byte[outW * outH];
        for (var y = 0; y < outH; y++)
            for (var x = 0; x < outW; x++)
            {
                int value;
                if (plan.Down == 1) value = page[a.Left + x / plan.Up, a.Top + y / plan.Up];
                else
                {
                    var sum = 0;
                    for (var dy = 0; dy < plan.Down; dy++)
                        for (var dx = 0; dx < plan.Down; dx++)
                            sum += page[a.Left + x * plan.Down + dx, a.Top + y * plan.Down + dy];
                    value = sum / (plan.Down * plan.Down);
                }
                result[y * outW + x] = value < Dark ? (byte)0 : (byte)255;
            }
        return new GrayImage(outW, outH, result);
    }
}
