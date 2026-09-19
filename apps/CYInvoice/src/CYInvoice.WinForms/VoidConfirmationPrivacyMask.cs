namespace CYInvoice.WinForms;

internal static class VoidConfirmationPrivacyMask
{
    private const string DetailTitlePrefix = "發票詳細資訊－";
    private const string MaskedDetailTitle = "發票詳細資訊";
    private const string MaskedInvoiceNumberText = "••••••••••";
    private const string PreviewMaskTag = "void-confirmation-preview-mask";
    private const int DelayedRestoreMilliseconds = 5000;
    private static readonly Dictionary<Form, MaskState> States = new();

    internal static void Apply(Form? owner)
    {
        if (owner is null || owner.IsDisposed) return;
        if (States.TryGetValue(owner, out var existing))
        {
            existing.KeepMasked();
            return;
        }

        var title = owner.Text ?? string.Empty;
        if (!title.StartsWith(DetailTitlePrefix, StringComparison.Ordinal)) return;
        var invoiceNumber = title[DetailTitlePrefix.Length..].Trim();
        if (invoiceNumber.Length == 0) return;

        States[owner] = new MaskState(owner, invoiceNumber);
    }

    internal static void KeepMasked(Form? owner)
    {
        if (owner is not null && States.TryGetValue(owner, out var state))
            state.KeepMasked();
    }

    internal static void Restore(Form? owner)
    {
        if (owner is null || !States.Remove(owner, out var state)) return;
        state.Dispose();
    }

    internal static bool IsMasked(Form owner) => States.ContainsKey(owner);

    internal static void VerifySmokeLayout()
    {
        using var owner = new Form { Text = "發票詳細資訊－AA12345678" };
        var number = new Label { Text = "AA12345678" };
        var previewHost = new Panel();
        previewHost.Controls.Add(new PictureBox { Dock = DockStyle.Fill });
        owner.Controls.Add(number);
        owner.Controls.Add(previewHost);

        Apply(owner);
        if (!IsMasked(owner) || owner.Text != MaskedDetailTitle || number.Text != MaskedInvoiceNumberText ||
            FindTaggedControl(owner, PreviewMaskTag) is null)
            throw new InvalidOperationException("作廢確認未正確遮蔽詳細資料中的發票號碼與預覽");

        Restore(owner);
        if (IsMasked(owner) || owner.Text != "發票詳細資訊－AA12345678" || number.Text != "AA12345678" ||
            FindTaggedControl(owner, PreviewMaskTag) is not null)
            throw new InvalidOperationException("作廢確認取消後未正確還原詳細資料");
    }

    private static Control? FindTaggedControl(Control root, string tag)
    {
        foreach (Control child in root.Controls)
        {
            if (Equals(child.Tag, tag)) return child;
            var nested = FindTaggedControl(child, tag);
            if (nested is not null) return nested;
        }
        return null;
    }

    private sealed class MaskState : IDisposable
    {
        private readonly Form owner;
        private readonly string originalTitle;
        private readonly List<MaskedLabel> labels = [];
        private readonly List<MaskedPicture> pictures = [];
        private readonly List<Control> overlays = [];
        private readonly System.Windows.Forms.Timer restoreTimer = new() { Interval = DelayedRestoreMilliseconds };
        private bool disposed;

        internal MaskState(Form owner, string invoiceNumber)
        {
            this.owner = owner;
            originalTitle = owner.Text;
            owner.Text = MaskedDetailTitle;

            foreach (var label in Descendants(owner).OfType<Label>()
                         .Where(label => string.Equals(label.Text.Trim(), invoiceNumber, StringComparison.OrdinalIgnoreCase)))
            {
                labels.Add(new MaskedLabel(label, label.Text, label.BackColor));
                label.Text = MaskedInvoiceNumberText;
                label.BackColor = Color.FromArgb(224, 224, 224);
            }

            var maskedParents = new HashSet<Control>();
            foreach (var picture in Descendants(owner).OfType<PictureBox>())
            {
                pictures.Add(new MaskedPicture(picture, picture.Visible));
                picture.Visible = false;
                var parent = picture.Parent;
                if (parent is null || !maskedParents.Add(parent)) continue;
                var overlay = new Label
                {
                    Text = "作廢確認中\r\n發票預覽暫時遮蔽",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.FromArgb(224, 224, 224),
                    ForeColor = Color.FromArgb(88, 88, 88),
                    Font = new Font("Microsoft JhengHei UI", 11F, FontStyle.Bold),
                    Margin = Padding.Empty,
                    Tag = PreviewMaskTag,
                };
                parent.Controls.Add(overlay);
                overlay.BringToFront();
                overlays.Add(overlay);
            }

            restoreTimer.Tick += RestoreTimerTick;
            owner.Activated += OwnerActivated;
            owner.EnabledChanged += OwnerEnabledChanged;
            owner.FormClosed += OwnerFormClosed;
        }

        internal void KeepMasked()
        {
            if (disposed) return;
            restoreTimer.Stop();
            foreach (var overlay in overlays)
                if (!overlay.IsDisposed) overlay.BringToFront();
        }

        private void OwnerActivated(object? sender, EventArgs eventArgs)
        {
            if (!disposed && owner.Enabled) restoreTimer.Start();
        }

        private void OwnerEnabledChanged(object? sender, EventArgs eventArgs)
        {
            if (disposed) return;
            if (!owner.Enabled)
            {
                restoreTimer.Stop();
                return;
            }
            if (Form.ActiveForm == owner) restoreTimer.Start();
        }

        private void RestoreTimerTick(object? sender, EventArgs eventArgs)
        {
            restoreTimer.Stop();
            Restore(owner);
        }

        private void OwnerFormClosed(object? sender, FormClosedEventArgs eventArgs) => Restore(owner);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            restoreTimer.Stop();
            restoreTimer.Tick -= RestoreTimerTick;
            restoreTimer.Dispose();
            owner.Activated -= OwnerActivated;
            owner.EnabledChanged -= OwnerEnabledChanged;
            owner.FormClosed -= OwnerFormClosed;

            if (!owner.IsDisposed) owner.Text = originalTitle;
            foreach (var item in labels)
            {
                if (item.Label.IsDisposed) continue;
                item.Label.Text = item.Text;
                item.Label.BackColor = item.BackColor;
            }
            foreach (var item in pictures)
            {
                if (!item.Picture.IsDisposed) item.Picture.Visible = item.Visible;
            }
            foreach (var overlay in overlays)
            {
                if (overlay.IsDisposed) continue;
                overlay.Parent?.Controls.Remove(overlay);
                overlay.Dispose();
            }
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }

        private sealed record MaskedLabel(Label Label, string Text, Color BackColor);
        private sealed record MaskedPicture(PictureBox Picture, bool Visible);
    }
}
