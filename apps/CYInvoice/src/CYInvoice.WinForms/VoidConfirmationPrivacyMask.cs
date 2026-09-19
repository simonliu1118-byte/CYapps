namespace CYInvoice.WinForms;

internal sealed class VoidConfirmationPrivacyMask : IDisposable
{
    private const string MaskedDetailTitle = "發票詳細資訊";
    private const string PreviewMaskTag = "void-confirmation-preview-mask";
    private const string InvoiceMaskTag = "void-confirmation-invoice-mask";

    private readonly Form owner;
    private readonly string originalTitle;
    private readonly List<MaskedLabel> labels = [];
    private readonly List<MaskedPicture> pictures = [];
    private readonly List<MaskedListText> listTexts = [];
    private readonly List<Control> overlays = [];
    private bool disposed;

    private VoidConfirmationPrivacyMask(Form owner, string invoiceNumber)
    {
        this.owner = owner;
        originalTitle = owner.Text;
        owner.Text = MaskedDetailTitle;

        foreach (var label in Descendants(owner).OfType<Label>()
                     .Where(label => string.Equals(label.Text.Trim(), invoiceNumber, StringComparison.OrdinalIgnoreCase)))
        {
            labels.Add(new MaskedLabel(label, label.Visible));
            AddInvoiceMask(label);
            label.Visible = false;
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

    private void AddInvoiceMask(Label label)
    {
        var parent = label.Parent;
        if (parent is null) return;
        var maskWidth = Math.Min(Math.Max(104, label.PreferredWidth + 10), Math.Max(104, label.Width));
        var maskHeight = Math.Min(20, Math.Max(14, label.Height - 8));
        var overlay = new Panel
        {
            BackColor = Color.Black,
            Size = new Size(maskWidth, maskHeight),
            Location = new Point(label.Left, label.Top + Math.Max(0, (label.Height - maskHeight) / 2)),
            Margin = Padding.Empty,
            Tag = InvoiceMaskTag,
        };
        parent.Controls.Add(overlay);
        overlay.BringToFront();
        overlays.Add(overlay);
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
            if (invoiceColumn < 0 || list.Items.Count == 0) continue;

            var firstIndex = list.TopItem?.Index ?? 0;
            for (var index = Math.Max(0, firstIndex); index < list.Items.Count; index++)
            {
                Rectangle bounds;
                try { bounds = list.GetItemRect(index); }
                catch (ArgumentException) { break; }
                if (bounds.Top >= list.ClientSize.Height) break;
                if (bounds.Bottom <= 0) continue;
                var item = list.Items[index];
                if (item.SubItems.Count <= invoiceColumn) continue;
                var subItem = item.SubItems[invoiceColumn];
                listTexts.Add(new MaskedListText(list, subItem, subItem.Text));
                subItem.Text = string.Empty;
            }
            list.Invalidate();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        if (!owner.IsDisposed) owner.Text = originalTitle;
        foreach (var item in labels)
        {
            if (!item.Label.IsDisposed) item.Label.Visible = item.Visible;
        }
        foreach (var item in listTexts)
        {
            if (item.List.IsDisposed) continue;
            item.SubItem.Text = item.Text;
            item.List.Invalidate();
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
        var number = new Label { Text = "AA12345678", AutoSize = false, Size = new Size(150, 30) };
        var previewHost = new Panel();
        previewHost.Controls.Add(new PictureBox { Dock = DockStyle.Fill });
        owner.Controls.Add(number);
        owner.Controls.Add(previewHost);

        var mask = Apply(owner, "AA12345678");
        if (owner.Text != MaskedDetailTitle || number.Visible ||
            FindTaggedControl(owner, InvoiceMaskTag) is not Panel invoiceMask || invoiceMask.BackColor != Color.Black ||
            FindTaggedControl(owner, PreviewMaskTag) is null)
            throw new InvalidOperationException("作廢確認未正確遮蔽詳細資料中的發票號碼與預覽");

        mask.Dispose();
        if (owner.Text != "發票詳細資訊-AA12345678" || !number.Visible ||
            FindTaggedControl(owner, InvoiceMaskTag) is not null || FindTaggedControl(owner, PreviewMaskTag) is not null)
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

    private sealed record MaskedLabel(Label Label, bool Visible);
    private sealed record MaskedPicture(PictureBox Picture, bool Visible);
    private sealed record MaskedListText(ListView List, ListViewItem.ListViewSubItem SubItem, string Text);
}
