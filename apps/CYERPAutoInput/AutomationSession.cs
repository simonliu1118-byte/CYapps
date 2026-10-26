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
/// <summary>How an automatic run ended, for the notice's last look (user, 2026-10-12).</summary>
internal enum AutomationOutcome
{
    /// <summary>Everything the settings asked for was done: a green check, gone after 0.5 s.</summary>
    Done,
    /// <summary>Entered, but something is listed for a person to check.</summary>
    NeedsAttention,
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

    /// <summary>Shopee 光學辨識 that goes on into ERP (F8 or 開始 with the setting on).</summary>
    public const string FullAutoTitle = "自動擷取輸入中";
    public const string InputTitle = "自動輸入中";
    /// <summary>蝦皮全自動: shown for the whole batch, its scans and ERP inputs too, with a timer.</summary>
    public const string BatchTitle = "全自動運轉中";

    private string? _pinned;
    private DateTime _since;

    /// <summary>
    /// Holds the notice on <paramref name="title"/> with a running timer until <see cref="Unpin"/>
    /// (user, 2026-10-13: 蝦皮全自動 shows 全自動運轉中 throughout, with 分:秒); the steps' own
    /// titles and their passing ends are not shown meanwhile.
    /// </summary>
    public void Pin(string title)
    {
        _pinned = title;
        _since = DateTime.Now;
    }

    public void Unpin() => _pinned = null;

    public AutomationSession(AppLogger log) => _log = log;

    /// <summary>Esc was pressed (raised on the UI thread): everything of the run stops, background work too.</summary>
    public event Action? Escaped;

    public bool Active => _mouseLock is not null && !_paused;

    /// <summary>A pinned run (蝦皮全自動) waiting on the person: mouse free, Esc still stops the run.</summary>
    private bool _paused;

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
        else if (_paused)
        {
            _paused = false;
            _mouseLock.Relock();
        }
        try
        {
            _overlay ??= new AutomationOverlay();
            _overlay.ShowNotice(_pinned ?? title, _log, _pinned is null ? null : _since);
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
        // A pause inside 蝦皮全自動 (user, 2026-10-26: Esc there only freed the mouse and the batch
        // went on after the question): the mouse is freed, the Esc watch stays.
        if (_pinned is not null && outcome == AutomationOutcome.Paused)
        {
            if (!_paused) { _paused = true; _mouseLock.Release(); }
            return;
        }
        _paused = false;
        _mouseLock.Dispose();
        _mouseLock = null;
        // Inside 蝦皮全自動 a step's end is not the batch's: the notice stays, unless stopped.
        if (_pinned is not null && outcome is not (AutomationOutcome.Cancelled or AutomationOutcome.Stopped)) return;
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
        _ui?.Post(_ => { End(AutomationOutcome.Cancelled); Escaped?.Invoke(); }, null);
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
    private string _title = AutomationSession.InputTitle;
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

    /// <summary>分:秒, with 時 only when needed (user, 2026-10-13).</summary>
    private static string Elapsed(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    /// <param name="since">Shows the time since then beside the hint (蝦皮全自動).</param>
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
            // Drawn before it is seen (user, 2026-10-13: it showed the last run's end for a moment).
            Opacity = 0;
            Show();
            Refresh();
            Opacity = 0.9;
        }
        else Invalidate();
        _timer.Start();
    }

    /// <summary>
    /// The run's end (user, 2026-10-12): a green check and 「自動輸入完成」 for 0.5 s, or an
    /// amber warning for 1.5 s when it stopped or left something to check.
    /// </summary>
    public void ShowOutcome(AutomationOutcome outcome)
    {
        _timer.Stop();
        if (!_excluded || !IsHandleCreated || !Visible) { HideNotice(); return; }
        _outcome = outcome;
        (_title, _hint) = outcome switch
        {
            AutomationOutcome.Done => ("自動輸入完成", string.Empty),
            AutomationOutcome.NeedsAttention => ("輸入完成，有項目要確認", "請看提示視窗"),
            AutomationOutcome.Stopped => ("自動輸入已中斷", "請看提示視窗"),
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

    /// <summary>Just wide enough for the text (user, 2026-10-12: the box was too wide), centered.</summary>
    private void Place()
    {
        // A running timer is measured at its widest so the box does not jump.
        var hint = _since is not null && _outcome is null ? $"{RunningHint}　0:00:00" : _hint;
        var textWidth = Math.Max(TextRenderer.MeasureText(_title, TitleFont).Width, TextRenderer.MeasureText(hint, HintFont).Width);
        var size = new Size(86 + textWidth + 36, 92);
        // Growing the box while it is on screen (「自動輸入中」 → a longer outcome) left its right
        // part cut off (CYERPAutoQuery, user's photo 2026-10-16): it is resized only while hidden.
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
