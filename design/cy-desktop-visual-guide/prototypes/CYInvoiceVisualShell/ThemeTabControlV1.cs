using System.Runtime.InteropServices;

namespace CYInvoiceVisualShell;

// Tab Lab V2: keep native WinForms TabControl/page behavior and only
// owner-draw the tab headers. Hover invalidates only the affected tabs so the
// selected tab does not repaint/flicker while the pointer crosses other tabs.
internal sealed class ThemeTabControlV1 : TabControl
{
    private const int WM_CHANGEUISTATE = 0x0127;
    private const int UIS_SET = 1;
    private const int UISF_HIDEFOCUS = 0x1;

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

        // Keep the native TabControl, but reduce visible repaint artifacts in
        // the small owner-drawn header region.
        DoubleBuffered = true;
    }

    internal void ApplyTheme(CyTheme theme)
    {
        palette = VisualTokens.GetPalette(theme);
        InvalidateHeader();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        HideNativeFocusCue();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        HideNativeFocusCue();
        InvalidateSelectedTab();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateSelectedTab();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        HideNativeFocusCue();
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        var oldHover = hoverIndex;
        hoverIndex = -1;

        base.OnSelectedIndexChanged(e);
        HideNativeFocusCue();

        InvalidateTab(oldHover);
        InvalidateSelectedTab();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var next = HitTestTab(e.Location);
        // The selected tab already has a stronger active treatment; do not
        // apply a second hover state or repaint it just because the pointer
        // enters its rectangle.
        if (next == SelectedIndex)
            next = -1;

        if (next == hoverIndex)
            return;

        var previous = hoverIndex;
        hoverIndex = next;

        // Critical: do NOT Invalidate() the whole TabControl here. Repaint only
        // the two headers whose hover state actually changed.
        InvalidateTab(previous);
        InvalidateTab(next);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverIndex < 0)
            return;

        var previous = hoverIndex;
        hoverIndex = -1;
        InvalidateTab(previous);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= TabPages.Count)
            return;

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

    private void InvalidateSelectedTab()
    {
        InvalidateTab(SelectedIndex);
    }

    private void InvalidateHeader()
    {
        for (var i = 0; i < TabCount; i++)
            InvalidateTab(i);
    }

    private void InvalidateTab(int index)
    {
        if (index < 0 || index >= TabCount || !IsHandleCreated)
            return;

        var rect = GetTabRect(index);
        // Include the underline edge and anti-aliased text fringe without
        // touching unrelated tabs/pages.
        rect.Inflate(2, 2);
        Invalidate(rect, false);
    }

    private int HitTestTab(Point location)
    {
        for (var i = 0; i < TabCount; i++)
        {
            if (GetTabRect(i).Contains(location))
                return i;
        }
        return -1;
    }

    private void HideNativeFocusCue()
    {
        if (!IsHandleCreated)
            return;

        var wParam = (IntPtr)(UIS_SET | (UISF_HIDEFOCUS << 16));
        SendMessage(Handle, WM_CHANGEUISTATE, wParam, IntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
