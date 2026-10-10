namespace CYInvoice.WinForms;

internal sealed class ThemedDangerButton : RoundedButton
{
    internal ThemedDangerButton(string text)
    {
        Text = text;
        Width = UiControls.StandardButtonWidth;
        Height = UiControls.StandardButtonHeight;
        Margin = new Padding(6, 2, 6, 2);
        AutoSize = false;
        TabStop = true;
    }

    protected override ButtonColors ResolveColors()
    {
        if (!Enabled) return RoundedButtonSurface.SecondaryColors(false, false, false);
        var fill = Pressed ? Color.FromArgb(143, 38, 38)
            : Hovered ? Color.FromArgb(158, 45, 45) : Color.FromArgb(180, 55, 55);
        return new ButtonColors(fill, Color.FromArgb(143, 38, 38), Color.White);
    }
}
