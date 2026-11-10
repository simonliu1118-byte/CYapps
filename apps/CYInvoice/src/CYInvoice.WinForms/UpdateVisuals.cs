namespace CYInvoice.WinForms;

// Native Windows controls only; the updater never draws or changes invoice data in Paint.
internal enum UpdateBannerKind { Progress, Success, Failure }

internal sealed class UpdateBanner : Panel
{
    private readonly ProgressBar indicator = new()
    {
        Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 26, Size = new Size(96, 17),
    };
    private readonly Label message = new()
    {
        AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
    };
    private readonly Button dismiss = new()
    {
        Text = "×", Size = new Size(28, 25), FlatStyle = FlatStyle.Flat, TabStop = false,
    };
    private readonly System.Windows.Forms.Timer hideTimer = new();

    public UpdateBanner()
    {
        Dock = DockStyle.Top;
        Height = 39;
        Visible = false;
        BackColor = Color.FromArgb(236, 246, 255);
        dismiss.FlatAppearance.BorderSize = 0;
        dismiss.Click += (_, _) => HideBanner();
        hideTimer.Tick += (_, _) => HideBanner();
        Controls.AddRange([indicator, message, dismiss]);
        Layout += (_, _) =>
        {
            indicator.Location = new Point(12, 11);
            dismiss.Location = new Point(Math.Max(8, Width - 36), 7);
            var lead = indicator.Visible ? 119 : 12;
            message.SetBounds(lead, 3, Math.Max(10, dismiss.Left - lead - 8), 33);
        };
    }

    public void Show(UpdateBannerKind kind, string text, TimeSpan? hideAfter = null)
    {
        BackColor = kind == UpdateBannerKind.Success ? Color.FromArgb(237, 250, 239) :
            kind == UpdateBannerKind.Failure ? Color.FromArgb(255, 242, 241) :
            Color.FromArgb(236, 246, 255);
        message.ForeColor = Color.FromArgb(31, 41, 55);
        message.Text = text;
        indicator.Visible = kind == UpdateBannerKind.Progress;
        indicator.MarqueeAnimationSpeed = indicator.Visible ? 26 : 0;
        dismiss.Visible = !indicator.Visible;
        hideTimer.Stop();
        if (hideAfter is { } duration)
        {
            hideTimer.Interval = Math.Clamp((int)duration.TotalMilliseconds, 1, 60_000);
            hideTimer.Start();
        }
        Visible = true;
        PerformLayout();
    }

    public void HideBanner()
    {
        hideTimer.Stop();
        indicator.MarqueeAnimationSpeed = 0;
        Visible = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) hideTimer.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class UpdateCountdownForm : Form
{
    private readonly System.Windows.Forms.Timer tick = new() { Interval = 1000 };
    private readonly Label message = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
    private int secondsLeft;

    public UpdateCountdownForm(string version, int seconds, Func<bool> isBusy)
    {
        secondsLeft = seconds;
        Text = "CYInvoice 自動更新";
        Font = new Font("Microsoft JhengHei UI", 10);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ControlBox = false;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(445, 163);
        BackColor = Color.FromArgb(247, 249, 252);
        message.SetBounds(20, 10, 408, 67);
        var bar = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30,
            Bounds = new Rectangle(23, 84, 400, 16)
        };
        var now = new Button
        {
            Text = "立即更新", DialogResult = DialogResult.OK,
            Bounds = new Rectangle(201, 115, 105, 32)
        };
        var later = new Button
        {
            Text = "稍後再更新", DialogResult = DialogResult.Ignore,
            Bounds = new Rectangle(316, 115, 110, 32)
        };
        Controls.AddRange([message, bar, now, later]);
        AcceptButton = now;
        void Render() => message.Text = $"CYInvoice 新版 V{version} 已下載並驗證。\n{secondsLeft} 秒後將關閉並更新，完成後自動重新開啟。";
        Render();
        tick.Tick += (_, _) =>
        {
            if (isBusy()) { DialogResult = DialogResult.Abort; return; }
            if (--secondsLeft <= 0) DialogResult = DialogResult.OK;
            else Render();
        };
        Shown += (_, _) =>
        {
            if (isBusy()) { DialogResult = DialogResult.Abort; return; }
            tick.Start();
        };
        FormClosed += (_, _) => tick.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tick.Dispose();
        base.Dispose(disposing);
    }
}
