using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CYERPAutoInput;

/// <summary>
/// While CY works on its own (user, 2026-10-12): the physical mouse is locked and a topmost
/// notice with a spinner sits in the middle of the screen — 「全自動運轉中」 for Shopee full
/// auto from the page scan to the print, 「自動輸入中」 for any other ERP input. Ended whenever
/// CY stops to ask the user something, so a message box is never behind a locked mouse.
/// </summary>
internal sealed class AutomationSession : IDisposable
{
    private readonly AppLogger _log;
    private PhysicalMouseLock? _mouseLock;
    private AutomationOverlay? _overlay;

    public const string FullAutoTitle = "全自動運轉中";
    public const string InputTitle = "自動輸入中";

    public AutomationSession(AppLogger log) => _log = log;

    public bool Active => _mouseLock is not null;

    /// <summary>Locks the mouse and shows the notice; when already running, only the title follows.</summary>
    public void Begin(string title)
    {
        _mouseLock ??= PhysicalMouseLock.Engage(_log);
        try
        {
            _overlay ??= new AutomationOverlay();
            _overlay.ShowNotice(title, _log);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ExternalException)
        {
            _log.Warn("automation", $"notice not shown: {ex.GetType().Name}");
        }
    }

    /// <summary>Releases the mouse and hides the notice.</summary>
    public void End()
    {
        _overlay?.HideNotice();
        _mouseLock?.Dispose();
        _mouseLock = null;
    }

    public void Dispose()
    {
        End();
        _overlay?.Dispose();
        _overlay = null;
    }
}

/// <summary>
/// The automation notice. It never takes focus or clicks (mouse input falls through to the
/// window below), and it is excluded from screen capture, so the page scan and the ERP
/// screen reading never see it. Where Windows cannot exclude it (before Windows 10 2004),
/// it is not shown at all rather than risk covering what CY reads.
/// </summary>
internal sealed class AutomationOverlay : Form
{
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTopmost = 0x00000008;
    private const uint WdaExcludeFromCapture = 0x11;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 50 };
    private bool _excluded;
    private float _angle;
    private string _title = AutomationSession.InputTitle;
    private static readonly Font TitleFont = new("Microsoft JhengHei UI", 15F, FontStyle.Bold);
    private static readonly Font HintFont = new("Microsoft JhengHei UI", 10.5F);
    private const string Hint = "按 Esc 中止";

    public AutomationOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(32, 32, 36);
        Opacity = 0.9;
        Size = new Size(240, 92);
        DoubleBuffered = true;
        _timer.Tick += (_, _) => { _angle = (_angle + 30) % 360; Invalidate(); };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate | WsExTopmost;
            return p;
        }
    }

    public void ShowNotice(string title, AppLogger log)
    {
        _title = title;
        if (!IsHandleCreated) CreateHandle();
        if (!_excluded)
        {
            _excluded = SetWindowDisplayAffinity(Handle, WdaExcludeFromCapture);
            if (!_excluded)
            {
                log.Warn("automation", $"notice not shown: capture exclusion unavailable error={Marshal.GetLastWin32Error()}");
                return;
            }
        }
        // Just wide enough for the text (user, 2026-10-12: the box was too wide).
        var textWidth = Math.Max(TextRenderer.MeasureText(_title, TitleFont).Width, TextRenderer.MeasureText(Hint, HintFont).Width);
        Size = new Size(86 + textWidth + 26, 92);
        var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top + (screen.Height - Height) / 2);
        Region?.Dispose();
        using (var path = Rounded(new Rectangle(Point.Empty, Size), 16)) Region = new Region(path);
        Show();
        _timer.Start();
    }

    public void HideNotice()
    {
        _timer.Stop();
        if (IsHandleCreated && Visible) Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var spinner = new Rectangle(26, (Height - 40) / 2, 40, 40);
        using (var track = new Pen(Color.FromArgb(70, 255, 255, 255), 5f))
            g.DrawEllipse(track, spinner);
        using (var arc = new Pen(CyVisualTheme.Accent, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(arc, spinner, _angle, 100);
        TextRenderer.DrawText(g, _title, TitleFont, new Point(86, 18), Color.White, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Hint, HintFont, new Point(88, 52), Color.FromArgb(210, 210, 210), TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);
}
