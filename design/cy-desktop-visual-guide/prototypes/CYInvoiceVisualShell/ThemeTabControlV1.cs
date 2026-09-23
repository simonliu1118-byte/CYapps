using System.Runtime.InteropServices;

namespace CYInvoiceVisualShell;

// Native WinForms TabControl/page behavior with header-only owner draw.
// Hover invalidates only affected headers so the selected tab stays stable.
internal sealed class ThemeTabControlV1 : TabControl
{
    private const int WM_CHANGEUISTATE = 0x0127;
    private const int UIS_SET = 1;
    private const int UISF_HIDEFOCUS = 0x1;

    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);
    private int hoverIndex = -1;
    private float headerFontPt = 10f;
    private int underlineInset = 10;

    internal ThemeTabControlV1()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Normal;
        Appearance = TabAppearance.Normal;
        Multiline = false;
        Padding = new Point(14, 5);
        Font = VisualTokens.Font(10f);
        BackColor = Color.White;
        DoubleBuffered = true;
    }

    internal void ConfigureStandard()
    {
        headerFontPt = 10f;
        underlineInset = 10;
        Padding = new Point(14, 5);
        Font = VisualTokens.Font(10f);
        InvalidateHeader();
    }

    internal void ConfigureLarge()
    {
        // Large is an App Choice for higher-level navigation, not a different
        // visual language: same native TabControl, same underline/hover model.
        headerFontPt = 11f;
        underlineInset = 14;
        Padding = new Point(20, 9);
        Font = VisualTokens.Font(11f);
        InvalidateHeader();
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
        if (next == SelectedIndex)
            next = -1;

        if (next == hoverIndex)
            return;

        var previous = hoverIndex;
        hoverIndex = next;

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
            ? VisualTokens.Font(headerFontPt, FontStyle.Bold)
            : VisualTokens.Font(headerFontPt);

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
            e.Graphics.DrawLine(pen, rect.Left + underlineInset, y, rect.Right - underlineInset, y);
        }
    }

    private void InvalidateSelectedTab() => InvalidateTab(SelectedIndex);

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
