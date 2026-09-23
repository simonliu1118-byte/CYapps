namespace CYERPAutoInput;

internal sealed class CoupangButton : Button
{
    private static readonly Color[] BrandColors =
    [
        Color.FromArgb(145, 69, 18),
        Color.FromArgb(238, 126, 0),
        Color.FromArgb(112, 176, 0),
        Color.FromArgb(0, 142, 196)
    ];

    public CoupangButton()
    {
        Text = string.Empty;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderColor = Color.FromArgb(188, 188, 188);
        FlatAppearance.BorderSize = 1;
        BackColor = Color.White;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        base.OnPaint(pevent);
        var chars = new[] { "酷", "澎", "商", "城" };
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var total = ClientRectangle.Width - 18;
        var cell = Math.Max(1, total / chars.Length);
        var left = (ClientRectangle.Width - cell * chars.Length) / 2;
        for (var i = 0; i < chars.Length; i++)
        {
            using var brush = new SolidBrush(BrandColors[i]);
            var rect = new Rectangle(left + i * cell, 1, cell, ClientRectangle.Height - 2);
            pevent.Graphics.DrawString(chars[i], Font, brush, rect, format);
        }
    }
}
