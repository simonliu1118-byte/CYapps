using System.Runtime.InteropServices;
using CYInvoice.Core.Invoicing;

namespace CYInvoice.WinForms;

internal sealed class BuyerNameField : TextBox
{
    private const int EmSetMargins = 0x00D3;
    private const int EcRightMargin = 0x0002;
    private const int RetryWidth = 22;
    private const int RetryInset = 3;
    private readonly RetryGlyph retry = new()
    {
        Visible = false,
        TabStop = false,
        Cursor = Cursors.Hand,
        Margin = Padding.Empty,
        Font = new Font("Segoe UI Symbol", 11F, FontStyle.Bold),
    };
    private readonly ToolTip toolTip = new();
    private TextBox? buyerBan;
    private InvoiceService? service;
    private Func<bool>? lookupEnabled;
    private Func<string, NameLookup, string>? resolveName;
    private CancellationToken lifetimeToken;
    private CancellationTokenSource? activeLookup;
    private string apiName = string.Empty;
    private bool locked;
    private bool applyingLookup;
    private int generation;

    public BuyerNameField(int maximumLength = 32767)
    {
        Dock = DockStyle.Fill;
        MaxLength = maximumLength;
        Margin = new Padding(3, 5, 3, 5);
        BorderStyle = BorderStyle.FixedSingle;

        toolTip.SetToolTip(retry, "重新查詢買受人名稱");
        retry.Click += async (_, _) => await LookupAsync(forceApi: true);
        Controls.Add(retry);
        retry.BringToFront();
    }

    public string ApiName => apiName;
    public bool HasApiMismatch => apiName.Length != 0 && !string.Equals(Text.Trim(), apiName, StringComparison.Ordinal);
    public bool IsApplyingLookup => applyingLookup;

    public event Action<string>? BuyerBanChanged;
    public event Action<string>? LookupStarted;
    public event Action<string, NameLookup>? LookupCompleted;
    public event Action<string, Exception>? LookupFailed;

    public void BindLookup(
        TextBox buyerBanField,
        InvoiceService invoiceService,
        Func<bool> enabled,
        Func<string, NameLookup, string>? resolver = null,
        CancellationToken cancellationToken = default)
    {
        if (buyerBan is not null) buyerBan.TextChanged -= BuyerBanTextChanged;
        buyerBan = buyerBanField;
        service = invoiceService;
        lookupEnabled = enabled;
        resolveName = resolver;
        lifetimeToken = cancellationToken;
        buyerBan.TextChanged += BuyerBanTextChanged;
    }

    public void SetLookupState(NameLookup lookup)
    {
        apiName = lookup.ApiName.Trim();
        UpdateState();
    }

    public void ClearLookupState()
    {
        apiName = string.Empty;
        UpdateState();
    }

    public void SetLocked(bool value)
    {
        locked = value;
        Enabled = true;
        ReadOnly = value;
        TabStop = !value;
        BackColor = value ? Color.FromArgb(242, 242, 242) : Color.White;
        UpdateState();
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        LayoutRetryButton();
        ApplyTextMargin();
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        LayoutRetryButton();
    }

    protected override void OnTextChanged(EventArgs eventArgs)
    {
        base.OnTextChanged(eventArgs);
        UpdateState();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (buyerBan is not null) buyerBan.TextChanged -= BuyerBanTextChanged;
            activeLookup?.Cancel();
            activeLookup?.Dispose();
            toolTip.Dispose();
            retry.Font.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuyerBanTextChanged(object? sender, EventArgs eventArgs)
    {
        if (buyerBan is null) return;
        generation++;
        activeLookup?.Cancel();
        activeLookup?.Dispose();
        activeLookup = null;
        if (lookupEnabled?.Invoke() != true) return;

        ClearLookupState();
        ApplyLookupText(string.Empty);
        var ban = buyerBan.Text.Trim();
        BuyerBanChanged?.Invoke(ban);
        if (IsValidBan(ban)) _ = LookupAsync(forceApi: false);
    }

    private async Task LookupAsync(bool forceApi)
    {
        if (buyerBan is null || service is null || lookupEnabled?.Invoke() != true) return;
        var ban = buyerBan.Text.Trim();
        if (!IsValidBan(ban)) return;
        var currentGeneration = generation;
        var preservedName = Text;

        activeLookup?.Cancel();
        activeLookup?.Dispose();
        activeLookup = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        activeLookup.CancelAfter(TimeSpan.FromSeconds(20));
        var token = activeLookup.Token;
        LookupStarted?.Invoke(ban);
        try
        {
            var lookup = forceApi
                ? await service.LookupBuyerNameFromApiAsync(ban, token)
                : await service.LookupBuyerNameAsync(ban, token);
            if (token.IsCancellationRequested || currentGeneration != generation ||
                !string.Equals(buyerBan.Text.Trim(), ban, StringComparison.Ordinal)) return;

            var resolved = forceApi
                ? lookup.ApiName.Trim().Length == 0 ? preservedName : lookup.ApiName.Trim()
                : resolveName?.Invoke(ban, lookup) ?? lookup.Name.Trim();
            ApplyLookupText(resolved);
            SetLookupState(lookup);
            LookupCompleted?.Invoke(ban, lookup);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (currentGeneration != generation || !string.Equals(buyerBan.Text.Trim(), ban, StringComparison.Ordinal)) return;
            ClearLookupState();
            if (forceApi) ApplyLookupText(preservedName);
            LookupFailed?.Invoke(ban, error);
        }
    }

    private void ApplyLookupText(string value)
    {
        applyingLookup = true;
        try { Text = value; }
        finally { applyingLookup = false; }
    }

    private void UpdateState()
    {
        ForeColor = HasApiMismatch ? Color.FromArgb(196, 0, 0) : SystemColors.WindowText;
        var showRetry = !locked && HasApiMismatch && buyerBan is not null && service is not null;
        if (retry.Visible != showRetry)
        {
            retry.Visible = showRetry;
            ApplyTextMargin();
        }
        retry.Enabled = !locked;
        retry.BaseBackColor = locked ? BackColor : Color.FromArgb(248, 248, 248);
        if (showRetry) retry.BringToFront();
    }

    private void LayoutRetryButton()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        var height = Math.Max(16, ClientSize.Height - (RetryInset * 2));
        retry.SetBounds(
            Math.Max(RetryInset, ClientSize.Width - RetryWidth - RetryInset),
            RetryInset,
            RetryWidth,
            height);
    }

    private void ApplyTextMargin()
    {
        if (!IsHandleCreated) return;
        var rightMargin = retry.Visible ? RetryWidth + RetryInset + 5 : 1;
        SendMessage(Handle, EmSetMargins, new IntPtr(EcRightMargin), new IntPtr(rightMargin << 16));
    }

    private static bool IsValidBan(string value) => value.Length == 8 && value.All(char.IsAsciiDigit);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private sealed class RetryGlyph : Control
    {
        private bool hot;
        private bool pressed;
        private Color baseBackColor = Color.FromArgb(248, 248, 248);

        public RetryGlyph()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        public Color BaseBackColor
        {
            get => baseBackColor;
            set
            {
                if (baseBackColor == value) return;
                baseBackColor = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs eventArgs)
        {
            hot = true;
            Invalidate();
            base.OnMouseEnter(eventArgs);
        }

        protected override void OnMouseLeave(EventArgs eventArgs)
        {
            hot = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(eventArgs);
        }

        protected override void OnMouseDown(MouseEventArgs eventArgs)
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                pressed = true;
                Invalidate();
            }
            base.OnMouseDown(eventArgs);
        }

        protected override void OnMouseUp(MouseEventArgs eventArgs)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(eventArgs);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            var background = pressed
                ? Color.FromArgb(214, 234, 249)
                : hot ? Color.FromArgb(232, 243, 252) : baseBackColor;
            using var brush = new SolidBrush(background);
            eventArgs.Graphics.FillRectangle(brush, ClientRectangle);
            using var pen = new Pen(Color.FromArgb(125, 125, 125));
            eventArgs.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));

            var textBounds = new Rectangle(1, 0, Math.Max(0, Width - 2), Height);
            TextRenderer.DrawText(
                eventArgs.Graphics,
                "↻",
                Font,
                textBounds,
                Enabled ? Color.FromArgb(45, 45, 45) : SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
}
