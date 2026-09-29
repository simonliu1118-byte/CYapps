namespace CYInvoice.WinForms;

internal sealed class VoidConfirmationPrivacyMask : IDisposable
{
    private const string MaskedDetailTitle = "發票詳細資訊";
    private const string PreviewMaskTag = "void-confirmation-preview-mask";

    private readonly Form owner;
    private readonly string originalTitle;
    private readonly List<MaskedLabel> labels = [];
    private readonly List<MaskedPicture> pictures = [];
    private readonly List<Control> overlays = [];
    private readonly List<ListMaskHandler> listMasks = [];
    private bool disposed;

    private VoidConfirmationPrivacyMask(Form owner, string invoiceNumber)
    {
        this.owner = owner;
        originalTitle = owner.Text;
        owner.Text = MaskedDetailTitle;

        foreach (var label in Descendants(owner).OfType<Label>()
                     .Where(label => string.Equals(label.Text.Trim(), invoiceNumber, StringComparison.OrdinalIgnoreCase)))
        {
            labels.Add(new MaskedLabel(label, label.Text));
            label.Text = string.Empty;
        }

        MaskVisibleInvoiceNumbers(owner.Owner);

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
    }

    internal static VoidConfirmationPrivacyMask Apply(Form owner, string invoiceNumber)
    {
        ArgumentNullException.ThrowIfNull(owner);
        invoiceNumber = (invoiceNumber ?? string.Empty).Trim();
        if (owner.IsDisposed) throw new ObjectDisposedException(nameof(owner));
        if (invoiceNumber.Length == 0) throw new ArgumentException("發票號碼不可空白", nameof(invoiceNumber));
        return new VoidConfirmationPrivacyMask(owner, invoiceNumber);
    }

    private void MaskVisibleInvoiceNumbers(Form? backgroundForm)
    {
        if (backgroundForm is null || backgroundForm.IsDisposed) return;
        foreach (var list in Descendants(backgroundForm).OfType<ListView>())
        {
            var invoiceColumn = -1;
            for (var index = 0; index < list.Columns.Count; index++)
            {
                if (string.Equals(list.Columns[index].Text.Trim(), "發票號碼", StringComparison.Ordinal))
                {
                    invoiceColumn = index;
                    break;
                }
            }
            if (invoiceColumn < 0 || !list.OwnerDraw) continue;

            DrawListViewSubItemEventHandler handler = (_, eventArgs) =>
            {
                if (eventArgs.ColumnIndex != invoiceColumn || eventArgs.Item is null || eventArgs.SubItem is null) return;
                var backColor = eventArgs.Item.UseItemStyleForSubItems
                    ? eventArgs.Item.BackColor
                    : eventArgs.SubItem.BackColor;
                if (backColor == Color.Empty) backColor = list.BackColor;
                using var brush = new SolidBrush(backColor);
                eventArgs.Graphics.FillRectangle(brush, eventArgs.Bounds);
                if (list.GridLines)
                {
                    using var gridPen = new Pen(Color.FromArgb(226, 226, 226));
                    eventArgs.Graphics.DrawLine(gridPen, eventArgs.Bounds.Right - 1, eventArgs.Bounds.Top,
                        eventArgs.Bounds.Right - 1, eventArgs.Bounds.Bottom);
                    eventArgs.Graphics.DrawLine(gridPen, eventArgs.Bounds.Left, eventArgs.Bounds.Bottom - 1,
                        eventArgs.Bounds.Right, eventArgs.Bounds.Bottom - 1);
                }
            };
            list.DrawSubItem += handler;
            listMasks.Add(new ListMaskHandler(list, handler));
            list.Invalidate();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        if (!owner.IsDisposed) owner.Text = originalTitle;
        foreach (var item in listMasks)
        {
            if (item.List.IsDisposed) continue;
            item.List.DrawSubItem -= item.Handler;
            item.List.Invalidate();
        }
        foreach (var item in labels)
        {
            if (!item.Label.IsDisposed) item.Label.Text = item.Text;
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

    internal static void VerifySmokeLayout()
    {
        using var owner = new Form { Text = "發票詳細資訊-AA12345678" };
        var numberHost = new Panel { Size = new Size(180, 40) };
        var number = new Label { Text = "AA12345678", AutoSize = false, Location = new Point(10, 5), Size = new Size(150, 30) };
        numberHost.Controls.Add(number);
        var previewHost = new Panel();
        previewHost.Controls.Add(new PictureBox { Dock = DockStyle.Fill });
        owner.Controls.Add(numberHost);
        owner.Controls.Add(previewHost);

        var mask = Apply(owner, "AA12345678");
        if (owner.Text != MaskedDetailTitle || number.Text.Length != 0 ||
            FindTaggedControl(owner, PreviewMaskTag) is null)
            throw new InvalidOperationException("作廢確認未正確遮蔽詳細資料中的發票號碼與預覽");

        mask.Dispose();
        if (owner.Text != "發票詳細資訊-AA12345678" || number.Text != "AA12345678" ||
            FindTaggedControl(owner, PreviewMaskTag) is not null)
            throw new InvalidOperationException("作廢確認結束後未正確還原詳細資料");
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

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private sealed record MaskedLabel(Label Label, string Text);
    private sealed record MaskedPicture(PictureBox Picture, bool Visible);
    private sealed record ListMaskHandler(ListView List, DrawListViewSubItemEventHandler Handler);
}