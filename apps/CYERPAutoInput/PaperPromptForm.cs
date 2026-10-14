using System.Drawing.Drawing2D;

namespace CYERPAutoInput;

/// <summary>
/// 放紙提醒 (user, 2026-10-14): before the A4 新竹物流 documents print, the batch waits on a dark,
/// rounded panel in the middle of the screen like the 「全自動運轉中」 notice — not a dialog —
/// with a very large 「請在印表機放入寄件單用紙」 / 「請在印表機放入取貨總表用紙」 and a large 好了
/// in the middle. A pause, not a stop: Esc (or 停止) stops.
/// </summary>
internal sealed class PaperPromptForm : Form
{
    private static readonly Color Panel = Color.FromArgb(32, 32, 36);
    private static readonly Color Amber = Color.FromArgb(245, 180, 0);
    private readonly string _title;
    private readonly string _hint;
    private readonly Font _titleFont = new("Microsoft JhengHei UI", 30F, FontStyle.Bold);
    private readonly Font _hintFont = new("Microsoft JhengHei UI", 12F);

    public PaperPromptForm(string paper, string printer)
    {
        _title = $"請在印表機放入{paper}";
        _hint = $"印表機：{printer}　　放好之後按「好了」繼續列印";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        BackColor = Panel;
        Opacity = 0.96;
        DoubleBuffered = true;

        var width = Math.Max(TextRenderer.MeasureText(_title, _titleFont).Width + 140, 640);
        Size = new Size(width, 300);
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1040);
        Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top + (screen.Height - Height) / 2);
        using (var path = Rounded(new Rectangle(Point.Empty, Size), 20)) Region = new Region(path);

        var ok = new Button
        {
            Text = "好了",
            Size = new Size(260, 70),
            Location = new Point((Width - 260) / 2, 190),
            FlatStyle = FlatStyle.Flat,
            BackColor = CyVisualTheme.Accent,
            ForeColor = Color.White,
            Font = new Font("Microsoft JhengHei UI", 22F, FontStyle.Bold),
            DialogResult = DialogResult.OK,
            Cursor = Cursors.Hand
        };
        ok.FlatAppearance.BorderSize = 0;
        ok.FlatAppearance.MouseOverBackColor = CyVisualTheme.AccentHover;
        ok.FlatAppearance.MouseDownBackColor = CyVisualTheme.AccentPressed;
        var stop = new LinkLabel
        {
            Text = "停止（Esc）",
            AutoSize = true,
            LinkColor = Color.FromArgb(200, 200, 205),
            ActiveLinkColor = Color.White,
            BackColor = Panel,
            Font = new Font("Microsoft JhengHei UI", 10F),
            LinkBehavior = LinkBehavior.HoverUnderline
        };
        stop.LinkClicked += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(ok);
        Controls.Add(stop);
        stop.Location = new Point(Width - stop.PreferredWidth - 20, Height - stop.PreferredHeight - 14);
        AcceptButton = ok;
        CancelButton = null;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            DialogResult = DialogResult.Cancel;
            Close();
        };
        Shown += (_, _) => { Activate(); ok.Focus(); };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // An amber warning mark, as the notice's 「有項目要確認」.
        var icon = new Rectangle((Width - 48) / 2, 22, 48, 44);
        using (var triangle = new GraphicsPath())
        {
            triangle.AddPolygon([new PointF(icon.Left + 24, icon.Top), new PointF(icon.Right, icon.Bottom), new PointF(icon.Left, icon.Bottom)]);
            using var fill = new SolidBrush(Amber);
            g.FillPath(fill, triangle);
        }
        using (var mark = new Pen(Panel, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(mark, icon.Left + 24, icon.Top + 15, icon.Left + 24, icon.Top + 29);
        using (var dot = new SolidBrush(Panel)) g.FillEllipse(dot, icon.Left + 21, icon.Top + 34, 6, 6);
        TextRenderer.DrawText(g, _title, _titleFont, new Rectangle(0, 76, Width, 60), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, _hint, _hintFont, new Rectangle(0, 140, Width, 30), Color.FromArgb(200, 200, 205), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _hintFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
