namespace CYInvoiceVisualShell;

internal enum CyTheme
{
    Blue,
    Teal,
    Coral,
    Apricot,
}

internal enum CyDensity
{
    Compact,
    Standard,
    Comfortable,
}

internal enum CyButtonRole
{
    Primary,
    Secondary,
    Danger,
}

internal readonly record struct ThemePalette(
    Color Accent,
    Color Hover,
    Color Pressed,
    Color Soft,
    Color Focus,
    Color Selection);

internal readonly record struct DensityMetrics(
    float BodyPt,
    float SecondaryPt,
    float SectionPt,
    float ButtonPt,
    int InputHeight,
    int ButtonHeight,
    int LargeButtonHeight,
    int RowHeight,
    int HeaderHeight,
    int TabHeight,
    int OuterPadding,
    int FieldGap);

internal static class VisualTokens
{
    internal static readonly Color White = Color.White;
    internal static readonly Color Window = Color.FromArgb(245, 247, 250);
    internal static readonly Color Subtle = Color.FromArgb(248, 250, 252);
    internal static readonly Color ReadOnly = Color.FromArgb(241, 243, 245);
    internal static readonly Color Border = Color.FromArgb(217, 222, 229);
    internal static readonly Color Divider = Color.FromArgb(229, 232, 236);
    internal static readonly Color Grid = Color.FromArgb(221, 225, 230);
    internal static readonly Color TextPrimary = Color.FromArgb(31, 41, 55);
    internal static readonly Color TextSecondary = Color.FromArgb(102, 112, 133);
    internal static readonly Color TextDisabled = Color.FromArgb(152, 162, 179);
    internal static readonly Color Danger = Color.FromArgb(180, 55, 55);
    internal static readonly Color DangerHover = Color.FromArgb(164, 47, 47);
    internal static readonly Color DangerSoft = Color.FromArgb(248, 234, 234);
    internal static readonly Color Success = Color.FromArgb(33, 130, 92);
    internal static readonly Color SuccessSoft = Color.FromArgb(234, 245, 240);
    internal static readonly Color Warning = Color.FromArgb(166, 107, 16);
    internal static readonly Color WarningSoft = Color.FromArgb(250, 241, 227);
    internal static readonly Color Info = Color.FromArgb(53, 106, 154);
    internal static readonly Color InfoSoft = Color.FromArgb(234, 241, 247);

    internal static ThemePalette GetPalette(CyTheme theme) => theme switch
    {
        CyTheme.Teal => new(
            Color.FromArgb(47, 111, 115),
            Color.FromArgb(39, 94, 97),
            Color.FromArgb(32, 78, 81),
            Color.FromArgb(229, 241, 240),
            Color.FromArgb(105, 161, 163),
            Color.FromArgb(220, 237, 235)),
        CyTheme.Coral => new(
            Color.FromArgb(201, 117, 75),
            Color.FromArgb(182, 104, 65),
            Color.FromArgb(158, 89, 54),
            Color.FromArgb(248, 233, 224),
            Color.FromArgb(216, 146, 111),
            Color.FromArgb(243, 225, 214)),
        CyTheme.Apricot => new(
            Color.FromArgb(216, 132, 74),
            Color.FromArgb(195, 115, 63),
            Color.FromArgb(170, 100, 53),
            Color.FromArgb(250, 236, 221),
            Color.FromArgb(224, 161, 120),
            Color.FromArgb(246, 227, 209)),
        _ => new(
            Color.FromArgb(46, 74, 113),
            Color.FromArgb(39, 63, 97),
            Color.FromArgb(32, 53, 80),
            Color.FromArgb(232, 238, 245),
            Color.FromArgb(111, 143, 184),
            Color.FromArgb(224, 234, 245)),
    };

    internal static DensityMetrics GetDensity(CyDensity density) => density switch
    {
        CyDensity.Compact => new(9.5f, 8.5f, 11.5f, 9.5f, 30, 31, 42, 28, 44, 34, 14, 8),
        CyDensity.Comfortable => new(11f, 9.5f, 13f, 11f, 38, 42, 48, 38, 52, 40, 24, 12),
        _ => new(10f, 9f, 12f, 10f, 34, 36, 46, 32, 48, 37, 18, 10),
    };

    internal static Font Font(float pt, FontStyle style = FontStyle.Regular) =>
        new("Microsoft JhengHei UI", pt, style, GraphicsUnit.Point);
}
