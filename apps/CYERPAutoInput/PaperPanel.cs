using System.Runtime.InteropServices;

namespace CYERPAutoInput;

/// <summary>
/// 紙本發票待列印 (user, 2026-10-27): while a 全自動 runs, paper invoices that are ready wait in a
/// small box under the 「全自動運轉中」 notice instead of stopping the run; its one button prints
/// them. The box never takes the focus (the run keeps typing), is left out of screen captures
/// (the run's OCR never sees it) and lives on a thread of its own, so a dialog the run opens
/// does not disable it. The locked mouse is let into it only (<see cref="PhysicalMouseLock.Allowed"/>).
/// </summary>
internal sealed class PaperPanel : IDisposable
{
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private PanelForm? _form;

    /// <summary>The button was pressed (raised on the thread that created the panel).</summary>
    public event Action? PrintRequested;

    public PaperPanel()
    {
        _thread = new Thread(() =>
        {
            _form = new PanelForm(() => _ui?.Post(_ => PrintRequested?.Invoke(), null));
            _form.HandleCreated += (_, _) => _ready.Set();
            _ = _form.Handle;
            Application.Run();
        }) { IsBackground = true, Name = "CY paper panel" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(3));
    }

    /// <summary>Shows (or updates) the box with <paramref name="text"/>; its screen bounds.</summary>
    public Rectangle Show(string text) =>
        _form is null ? Rectangle.Empty : (Rectangle)_form.Invoke(() => _form.ShowText(text));

    public void Hide()
    {
        if (_form is { IsHandleCreated: true }) _form.BeginInvoke(() => _form.Hide());
    }

    public void Dispose()
    {
        if (_form is { IsHandleCreated: true }) _form.BeginInvoke(() => { _form.Close(); Application.ExitThread(); });
        _ready.Dispose();
    }

    private sealed class PanelForm : Form
    {
        private const int WsExToolWindow = 0x00000080, WsExNoActivate = 0x08000000, WsExTopmost = 0x00000008;
        private const int WmMouseActivate = 0x0021, MaNoActivate = 3;
        private const uint WdaExcludeFromCapture = 0x11;
        private readonly Label _text = new()
        {
            Dock = DockStyle.Top, Height = 44, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft JhengHei UI", 10.5F)
        };
        private readonly Button _print = new()
        {
            Text = "A4 白紙放好了，開始列印", Dock = DockStyle.Bottom, Height = 40, FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(234, 88, 12), ForeColor = Color.White, Font = new Font("Microsoft JhengHei UI", 11F, FontStyle.Bold)
        };

        public PanelForm(Action pressed)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(32, 32, 36);
            Padding = new Padding(10, 6, 10, 10);
            Size = new Size(360, 104);
            _print.FlatAppearance.BorderSize = 0;
            _print.Click += (_, _) => pressed();
            Controls.Add(_text);
            Controls.Add(_print);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle |= WsExToolWindow | WsExNoActivate | WsExTopmost;
                return p;
            }
        }

        // A click must not take the focus from ERP or Chrome.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmMouseActivate) { m.Result = MaNoActivate; return; }
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetWindowDisplayAffinity(Handle, WdaExcludeFromCapture);
        }

        public Rectangle ShowText(string text)
        {
            _text.Text = text;
            // Under the 「全自動運轉中」 notice, which sits in the middle of the main screen (92 px high).
            var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top + screen.Height / 2 + 46 + 10);
            if (!Visible) Show();
            return Bounds;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);
    }
}
