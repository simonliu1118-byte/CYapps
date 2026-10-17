using System.Drawing.Drawing2D;

namespace CYERPAutoInput;

/// <summary>
/// Values as pills (user, 2026-10-14: 品號 and 關鍵字 become pills once Enter is pressed):
/// typed text becomes a pill on Enter, a comma or leaving the box; a click on a pill's × (or
/// Backspace in an empty box) removes it. Pasting "a,b,c" gives three pills. Duplicates
/// (ignoring case) are not added twice.
/// </summary>
internal sealed class PillBox : UserControl
{
    private readonly TextBox _input = new() { BorderStyle = BorderStyle.None, Width = 120 };
    private readonly List<Pill> _pills = [];
    private bool _laying;

    public PillBox(IEnumerable<string> values, string placeholder, int width)
    {
        Width = width;
        BackColor = CyVisualTheme.White;
        BorderStyle = BorderStyle.FixedSingle;
        Padding = new Padding(3);
        _input.PlaceholderText = placeholder;
        _input.BackColor = CyVisualTheme.White;
        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Back && _input.TextLength == 0 && _pills.Count > 0)
            {
                Remove(_pills[^1]);
                e.SuppressKeyPress = true;
            }
        };
        _input.TextChanged += (_, _) =>
        {
            if (_input.Text.IndexOfAny([',', '，', '、']) >= 0) Commit();
        };
        _input.Leave += (_, _) => Commit(focus: false);
        Controls.Add(_input);
        Click += (_, _) => _input.Focus();
        foreach (var value in values) AddPill(value);
        Arrange();
    }

    /// <summary>The pills, then whatever is still typed.</summary>
    public List<string> Values => [.. _pills.Select(p => p.Value), .. Split(_input.Text)];

    /// <summary>Typed text into pills; false when there was none (Enter then moves on).</summary>
    public bool Commit(bool focus = true)
    {
        var parts = Split(_input.Text);
        if (parts.Count == 0)
        {
            if (_input.TextLength > 0) _input.Clear();
            return false;
        }
        _input.Clear();
        foreach (var part in parts) AddPill(part);
        Arrange();
        if (focus) _input.Focus();
        return true;
    }

    public bool Holds(Control control) => control == _input || control == this;

    private static List<string> Split(string text) =>
        text.Split([',', '，', '、'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(t => t.Length > 0).ToList();

    private void AddPill(string value)
    {
        value = value.Trim();
        if (value.Length == 0 || _pills.Any(p => string.Equals(p.Value, value, StringComparison.OrdinalIgnoreCase))) return;
        var pill = new Pill(value, Font);
        pill.Removed += (_, _) => { Remove(pill); _input.Focus(); };
        _pills.Add(pill);
        Controls.Add(pill);
    }

    private void Remove(Pill pill)
    {
        _pills.Remove(pill);
        Controls.Remove(pill);
        pill.Dispose();
        Arrange();
    }

    private void Arrange()
    {
        if (_laying) return;
        _laying = true;
        try
        {
            int x = Padding.Left, y = Padding.Top, line = Math.Max(_input.PreferredHeight, 24);
            var right = ClientSize.Width - Padding.Right;
            foreach (var pill in _pills)
            {
                if (x > Padding.Left && x + pill.Width > right) { x = Padding.Left; y += line + 3; }
                pill.Location = new Point(x, y);
                x += pill.Width + 4;
            }
            var inputWidth = Math.Max(80, right - x);
            if (x > Padding.Left && x + 80 > right) { x = Padding.Left; y += line + 3; inputWidth = right - x; }
            _input.Location = new Point(x, y + (line - _input.PreferredHeight) / 2);
            _input.Width = inputWidth;
            Height = y + line + Padding.Bottom + (Height - ClientSize.Height);
        }
        finally
        {
            _laying = false;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Arrange();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Arrange();
    }

    /// <summary>One value, drawn as a rounded tag with a ×; a click on the × removes it.</summary>
    private sealed class Pill : Control
    {
        public string Value { get; }
        public event EventHandler? Removed;
        private readonly int _textWidth;

        public Pill(string value, Font font)
        {
            Value = value;
            Font = font;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            _textWidth = TextRenderer.MeasureText(value, font).Width;
            Size = new Size(_textWidth + 30, 24);
            new ToolTip().SetToolTip(this, "按 × 移除");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 1, Width - 1, Height - 3);
            using (var path = new GraphicsPath())
            {
                var d = r.Height;
                path.AddArc(r.Left, r.Top, d, d, 90, 180);
                path.AddArc(r.Right - d, r.Top, d, d, 270, 180);
                path.CloseFigure();
                using var fill = new SolidBrush(CyVisualTheme.AccentSoft);
                using var pen = new Pen(CyVisualTheme.AccentFocus);
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Value, Font, new Rectangle(8, 1, _textWidth, Height - 3), CyVisualTheme.TextPrimary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            TextRenderer.DrawText(g, "×", Font, new Rectangle(Width - 20, 1, 14, Height - 3), CyVisualTheme.TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.X >= Width - 22) Removed?.Invoke(this, EventArgs.Empty);
        }
    }
}
