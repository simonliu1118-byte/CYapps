using System.Drawing.Drawing2D;

namespace CYERPAutoQuery;

/// <summary>
/// What the person at the computer sees of an automatic update (user, 2026-10-20: an update in
/// progress must show, so they know one came): a banner over the main window while it downloads,
/// waits and after it was put in place, and a countdown before the program restarts. The same file
/// in CYERPAutoQuery and CYERPAutoQuery.
/// </summary>
internal sealed class UpdateSpinner : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 40 };
    private float _angle;

    public UpdateSpinner()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(20, 20);
        _timer.Tick += (_, _) => { _angle = (_angle + 12) % 360; Invalidate(); };
    }

    public Color Color { get; set; } = CyVisualTheme.Accent;

    public bool Spinning
    {
        get => _timer.Enabled;
        set { _timer.Enabled = value; Visible = value; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Math.Min(Width, Height) - 4;
        var box = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
        using var track = new Pen(CyVisualTheme.Blend(Color, CyVisualTheme.White, 0.25), 2.5f);
        using var arc = new Pen(Color, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawEllipse(track, box);
        e.Graphics.DrawArc(arc, box, _angle, 100);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}

internal enum UpdateBannerKind { Progress, Success, Failure }

/// <summary>The strip over the main window: a spinner while busy, ✓／! when done, ✕ to dismiss.</summary>
internal sealed class UpdateBanner : Panel
{
    private readonly UpdateSpinner _spinner = new() { Margin = new Padding(0) };
    private readonly Label _mark = new() { AutoSize = false, Size = new Size(20, 20), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold) };
    private readonly Label _text = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Button _close = new() { Text = "✕", FlatStyle = FlatStyle.Flat, Size = new Size(28, 24), TabStop = false };
    private readonly System.Windows.Forms.Timer _hide = new();

    public UpdateBanner()
    {
        Dock = DockStyle.Top;
        Height = 34;
        Padding = new Padding(12, 0, 8, 0);
        Visible = false;
        _close.FlatAppearance.BorderSize = 0;
        _close.Click += (_, _) => Visible = false;
        _hide.Tick += (_, _) => { _hide.Stop(); Visible = false; };
        Controls.AddRange([_spinner, _mark, _text, _close]);
        Layout += (_, _) =>
        {
            var y = (Height - 20) / 2;
            _spinner.Location = new Point(Padding.Left, y);
            _mark.Location = new Point(Padding.Left, y);
            _close.Location = new Point(Width - Padding.Right - _close.Width, (Height - _close.Height) / 2);
            _text.SetBounds(Padding.Left + 28, 0, Math.Max(10, _close.Left - Padding.Left - 36), Height);
        };
    }

    /// <summary>Shows <paramref name="text"/>; <paramref name="hideAfter"/> hides it by itself, and only a finished state can be dismissed.</summary>
    public void Show(UpdateBannerKind kind, string text, TimeSpan? hideAfter = null)
    {
        var (color, soft) = kind switch
        {
            UpdateBannerKind.Success => (CyVisualTheme.Success, CyVisualTheme.SuccessSoft),
            UpdateBannerKind.Failure => (CyVisualTheme.Danger, CyVisualTheme.DangerSoft),
            _ => (CyVisualTheme.Accent, CyVisualTheme.AccentSoft)
        };
        BackColor = soft;
        _text.BackColor = soft;
        _mark.BackColor = soft;
        _text.ForeColor = CyVisualTheme.TextPrimary;
        _text.Text = text;
        _spinner.Color = color;
        _spinner.Spinning = kind == UpdateBannerKind.Progress;
        _mark.Visible = kind != UpdateBannerKind.Progress;
        _mark.ForeColor = color;
        _mark.Text = kind == UpdateBannerKind.Success ? "✓" : "!";
        _close.Visible = kind != UpdateBannerKind.Progress;
        _close.BackColor = soft;
        _hide.Stop();
        if (hideAfter is { } after) { _hide.Interval = (int)after.TotalMilliseconds; _hide.Start(); }
        Visible = true;
        PerformLayout();
    }

    public void HideBanner()
    {
        _hide.Stop();
        _spinner.Spinning = false;
        Visible = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var line = new Pen(CyVisualTheme.Border);
        e.Graphics.DrawLine(line, 0, Height - 1, Width, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hide.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// The countdown before an update restarts the program (user, 2026-10-20: 10 seconds; 「稍後再更新」
/// waits 30 minutes). OK: update now (time up or 「立即更新」); Ignore: later; Abort: the program
/// got busy (<paramref name="busy"/>), try again when it is idle.
/// </summary>
internal sealed class UpdateCountdownForm : Form
{
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    private readonly Label _text = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
    private int _left;

    public UpdateCountdownForm(string program, string version, int seconds, Func<bool> busy)
    {
        _left = seconds;
        Text = $"{program} 更新";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ControlBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = CyVisualTheme.Window;
        ClientSize = new Size(440, 150);

        var spinner = new UpdateSpinner { Size = new Size(36, 36), Location = new Point(22, 30) };
        spinner.Spinning = true;
        _text.SetBounds(72, 16, 350, 64);
        var now = new Button { Text = "立即更新", Size = new Size(100, 32), Location = new Point(212, 100), DialogResult = DialogResult.OK };
        var later = new Button { Text = "稍後再更新", Size = new Size(110, 32), Location = new Point(318, 100), DialogResult = DialogResult.Ignore };
        Controls.AddRange([spinner, _text, now, later]);
        AcceptButton = now;
        CyVisualTheme.Apply(this);

        void Render() => _text.Text = $"{program} 有新版 V{version}。\n{_left} 秒後自動更新並重新開啟程式。";
        Render();
        _tick.Tick += (_, _) =>
        {
            if (busy()) { DialogResult = DialogResult.Abort; return; }
            if (--_left <= 0) { DialogResult = DialogResult.OK; return; }
            Render();
        };
        Shown += (_, _) => { Activate(); _tick.Start(); };
        FormClosed += (_, _) => _tick.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tick.Dispose();
        base.Dispose(disposing);
    }
}
