namespace CYInvoiceVisualShell;

// V3 deliberately keeps native WinForms control behavior.
// The only interactive control with owner-draw is the TabControl header,
// matching the CYInvoice project rule that permits header-only tab styling.
internal sealed class V3TabControl : TabControl
{
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);
    private DensityMetrics metrics = VisualTokens.GetDensity(CyDensity.Standard);

    internal V3TabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        Appearance = TabAppearance.Normal;
        Padding = new Point(16, 6);
        ItemSize = new Size(180, metrics.TabHeight);
        Multiline = false;
    }

    internal void ApplyVisual(ThemePalette newPalette, DensityMetrics density)
    {
        palette = newPalette;
        metrics = density;
        Font = VisualTokens.Font(metrics.BodyPt);
        ItemSize = new Size(180, metrics.TabHeight);
        Invalidate();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var rect = GetTabRect(e.Index);
        var selected = e.Index == SelectedIndex;
        using var background = new SolidBrush(Color.White);
        e.Graphics.FillRectangle(background, rect);

        using var selectedFont = selected
            ? VisualTokens.Font(metrics.BodyPt, FontStyle.Bold)
            : VisualTokens.Font(metrics.BodyPt);
        var textColor = selected ? VisualTokens.TextPrimary : VisualTokens.TextSecondary;
        TextRenderer.DrawText(
            e.Graphics,
            TabPages[e.Index].Text,
            selectedFont,
            rect,
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        if (selected)
        {
            using var pen = new Pen(palette.Accent, 2f);
            e.Graphics.DrawLine(pen, rect.Left + 12, rect.Bottom - 2, rect.Right - 12, rect.Bottom - 2);
        }
    }
}
