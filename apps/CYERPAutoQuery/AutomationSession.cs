using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CYERPAutoQuery;

// Copied from CYERPAutoInput AutomationSession.cs (user, 2026-10-16: the query locks the
// mouse and shows 「自動查詢中」 in the middle of the screen, the same as the input).

/// <summary>How an automatic run ended, for the notice's last look (user, 2026-10-12).</summary>
internal enum AutomationOutcome
{
    /// <summary>Everything the settings asked for was done: a green check, gone after 0.5 s.</summary>
    Done,
    /// <summary>The query ran, but ERP has no such 品號.</summary>
    NotFound,
    /// <summary>Stopped by an error.</summary>
    Stopped,
    /// <summary>Stopped by the user's Esc: a red no-entry sign (user, 2026-10-12).</summary>
    Cancelled,
    /// <summary>The program stopped to ask something.</summary>
    Paused
}

internal sealed class AutomationSession : IDisposable
{
    private readonly AppLogger _log;
    private PhysicalMouseLock? _mouseLock;
    private AutomationOverlay? _overlay;
    private SynchronizationContext? _ui;
    private CancellationTokenSource _escape = new();

    /// <summary>Cancelled the moment the user presses Esc during this run.</summary>
    public CancellationToken EscapeToken => _escape.Token;

    public const string QueryTitle = "自動查詢中";

    public AutomationSession(AppLogger log) => _log = log;

    public bool Active => _mouseLock is not null;

    /// <summary>Locks the mouse and shows the notice; when already running, only the title follows.</summary>
    public void Begin(string title)
    {
        if (_mouseLock is null)
        {
            if (_escape.IsCancellationRequested) { _escape.Dispose(); _escape = new CancellationTokenSource(); }
            _ui = SynchronizationContext.Current;
            _mouseLock = PhysicalMouseLock.Engage(_log);
            _mouseLock.EscapePressed += OnEscape;
        }
        try
        {
            _overlay ??= new AutomationOverlay();
            // Every query shows its running time, the same timer as CYERPAutoInput (user, 2026-10-16).
            _overlay.ShowNotice(title, _log, DateTime.Now);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ExternalException)
        {
            _log.Warn("automation", $"notice not shown: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Releases the mouse at once; the notice shows how the run ended for a moment, then goes.
    /// Without an outcome the run paused to ask the user something.
    /// </summary>
    public void End(AutomationOutcome outcome = AutomationOutcome.Paused)
    {
        if (_mouseLock is null) return;
        _mouseLock.Dispose();
        _mouseLock = null;
        try { _overlay?.ShowOutcome(outcome); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ExternalException)
        {
            _log.Warn("automation", $"outcome not shown: {ex.GetType().Name}");
            _overlay?.HideNotice();
        }
    }

    /// <summary>Esc: the run is told to stop and the notice says so at once.</summary>
    private void OnEscape()
    {
        _log.Info("automation", "Esc pressed: stopping");
        try { _escape.Cancel(); } catch (ObjectDisposedException) { }
        _ui?.Post(_ => End(AutomationOutcome.Cancelled), null);
    }

    public void Dispose()
    {
        _escape.Dispose();
        _mouseLock?.Dispose();
        _mouseLock = null;
        _overlay?.HideNotice();
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
    private readonly System.Windows.Forms.Timer _linger = new();
    private AutomationOutcome? _outcome;
    private bool _excluded;
    private float _angle;
    private string _title = AutomationSession.QueryTitle;
    private static readonly Font TitleFont = new("Microsoft JhengHei UI", 15F, FontStyle.Bold);
    private static readonly Font HintFont = new("Microsoft JhengHei UI", 10.5F);
    private string _hint = RunningHint;
    private DateTime? _since;
    private const string RunningHint = "按 Esc 中止";
    private static readonly Color DoneGreen = Color.FromArgb(34, 197, 94);
    private static readonly Color AttentionAmber = Color.FromArgb(245, 180, 0);
    private static readonly Color StopRed = Color.FromArgb(220, 38, 38);

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
        _timer.Tick += (_, _) =>
        {
            _angle = (_angle + 30) % 360;
            if (_since is { } since && _outcome is null) _hint = $"{RunningHint}　{Elapsed(DateTime.Now - since)}";
            Invalidate();
        };
        _linger.Tick += (_, _) => HideNotice();
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

    /// <summary>分:秒, with 時 only when needed (same as CYERPAutoInput).</summary>
    private static string Elapsed(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    /// <param name="since">Shows the time since then beside the hint.</param>
    public void ShowNotice(string title, AppLogger log, DateTime? since = null)
    {
        _linger.Stop();
        _outcome = null;
        _title = title;
        _since = since;
        _hint = since is { } s ? $"{RunningHint}　{Elapsed(DateTime.Now - s)}" : RunningHint;
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
        Place();
        if (!Visible)
        {
            // Drawn before it is seen, so the last run's end never flashes (CYERPAutoInput).
            Opacity = 0;
            Show();
            Refresh();
            Opacity = 0.9;
        }
        else Invalidate();
        _timer.Start();
    }

    /// <summary>
    /// The run's end: a green check and 「自動查詢完成」 for 0.5 s, or an amber warning for
    /// 1.5 s when it stopped or found nothing (same timing as CYERPAutoInput).
    /// </summary>
    public void ShowOutcome(AutomationOutcome outcome)
    {
        _timer.Stop();
        if (!_excluded || !IsHandleCreated || !Visible) { HideNotice(); return; }
        _outcome = outcome;
        (_title, _hint) = outcome switch
        {
            AutomationOutcome.Done => ("自動查詢完成", string.Empty),
            AutomationOutcome.NotFound => ("查無資料", "請確認查詢內容"),
            AutomationOutcome.Stopped => ("自動查詢已中斷", "請看提示視窗"),
            AutomationOutcome.Cancelled => ("使用者已停止", "已按 Esc 緊急停止"),
            _ => ("已暫停", "請看提示視窗")
        };
        Place();
        Invalidate();
        _linger.Interval = outcome == AutomationOutcome.Done ? 500 : 1500;
        _linger.Start();
    }

    public void HideNotice()
    {
        _timer.Stop();
        _linger.Stop();
        if (IsHandleCreated && Visible) Hide();
    }

    /// <summary>
    /// Just wide enough for the text (user, 2026-10-12: the box was too wide), centered.
    /// Growing the box while it is on screen (「自動查詢中」 → a longer outcome) left its right
    /// part cut off (user's photo, 2026-10-16), so it is resized only while hidden — the same
    /// as CYERPAutoInput, and any future longer text still fits.
    /// </summary>
    private void Place()
    {
        // A running timer is measured at its widest so the box does not jump.
        var hint = _since is not null && _outcome is null ? $"{RunningHint}　0:00:00" : _hint;
        var textWidth = Math.Max(TextRenderer.MeasureText(_title, TitleFont).Width, TextRenderer.MeasureText(hint, HintFont).Width);
        var size = new Size(86 + textWidth + 36, 92);
        var shown = Visible;
        if (shown && size != Size) Hide();
        Size = size;
        var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top + (screen.Height - Height) / 2);
        Region?.Dispose();
        using var path = Rounded(new Rectangle(Point.Empty, Size), 16);
        Region = new Region(path);
        if (shown && !Visible) Show();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var icon = new Rectangle(26, (Height - 40) / 2, 40, 40);
        switch (_outcome)
        {
            case null:
                using (var track = new Pen(Color.FromArgb(70, 255, 255, 255), 5f))
                    g.DrawEllipse(track, icon);
                using (var arc = new Pen(CyVisualTheme.Accent, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(arc, icon, _angle, 100);
                break;
            case AutomationOutcome.Done:
                using (var fill = new SolidBrush(DoneGreen)) g.FillEllipse(fill, icon);
                using (var tick = new Pen(Color.White, 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(tick, [new PointF(icon.Left + 10, icon.Top + 21), new PointF(icon.Left + 17, icon.Top + 28), new PointF(icon.Left + 30, icon.Top + 13)]);
                break;
            case AutomationOutcome.Cancelled:
                using (var fill = new SolidBrush(StopRed)) g.FillEllipse(fill, icon);
                using (var bar = new SolidBrush(Color.White)) g.FillRectangle(bar, icon.Left + 9, icon.Top + 17, 22, 6);
                break;
            default:
                using (var triangle = new GraphicsPath())
                {
                    triangle.AddPolygon([new PointF(icon.Left + 20, icon.Top + 2), new PointF(icon.Right, icon.Bottom - 3), new PointF(icon.Left, icon.Bottom - 3)]);
                    using var fill = new SolidBrush(AttentionAmber);
                    g.FillPath(fill, triangle);
                }
                using (var mark = new Pen(Color.FromArgb(32, 32, 36), 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(mark, icon.Left + 20, icon.Top + 14, icon.Left + 20, icon.Top + 25);
                using (var dot = new SolidBrush(Color.FromArgb(32, 32, 36)))
                    g.FillEllipse(dot, icon.Left + 18, icon.Top + 29, 4.5f, 4.5f);
                break;
        }
        if (_hint.Length == 0)
            TextRenderer.DrawText(g, _title, TitleFont, new Rectangle(86, 0, Width - 86, Height), Color.White,
                TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        else
        {
            TextRenderer.DrawText(g, _title, TitleFont, new Point(86, 18), Color.White, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, _hint, HintFont, new Point(88, 52), Color.FromArgb(210, 210, 210), TextFormatFlags.NoPrefix);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _linger.Dispose(); }
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
