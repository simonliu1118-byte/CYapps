namespace CYInvoiceVisualShell;

// Tab Lab V1: keep the native WinForms TabControl/page behavior and only
// owner-draw the tab headers. No custom page navigation or custom chrome.
internal sealed class ThemeTabControlV1 : TabControl
{
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);
    private int hoverIndex = -1;

    internal ThemeTabControlV1()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Normal;
        Appearance = TabAppearance.Normal;
        Multiline = false;
        Padding = new Point(14, 5);
        Font = VisualTokens.Font(10f);
        BackColor = Color.White;
    }

    internal void ApplyTheme(CyTheme theme)
    {
        palette = VisualTokens.GetPalette(theme);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var next = HitTestTab(e.Location);
        if (next == hoverIndex) return;
        hoverIndex = next;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverIndex < 0) return;
        hoverIndex = -1;
        Invalidate();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= TabPages.Count) return;

        var rect = GetTabRect(e.Index);
        var selected = e.Index == SelectedIndex;
        var hovered = e.Index == hoverIndex && !selected;

        var background = hovered ? palette.Soft : Color.White;
        using (var brush = new SolidBrush(background))
            e.Graphics.FillRectangle(brush, rect);

        using var textFont = selected
            ? VisualTokens.Font(10f, FontStyle.Bold)
            : VisualTokens.Font(10f);

        var textColor = selected ? VisualTokens.TextPrimary : VisualTokens.TextSecondary;
        TextRenderer.DrawText(
            e.Graphics,
            TabPages[e.Index].Text,
            textFont,
            rect,
            textColor,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.EndEllipsis);

        if (selected)
        {
            using var pen = new Pen(palette.Accent, 2f);
            var y = rect.Bottom - 2;
            e.Graphics.DrawLine(pen, rect.Left + 10, y, rect.Right - 10, y);
        }
    }

    private int HitTestTab(Point location)
    {
        for (var i = 0; i < TabCount; i++)
        {
            if (GetTabRect(i).Contains(location)) return i;
        }
        return -1;
    }
}
