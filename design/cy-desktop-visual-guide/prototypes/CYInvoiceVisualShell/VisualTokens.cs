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
    // Restored to the lighter neutral family shown in the approved theme sample.
    internal static readonly Color Window = Color.FromArgb(248, 250, 252); // #F8FAFC
    internal static readonly Color Subtle = Color.FromArgb(248, 250, 252);
    internal static readonly Color ReadOnly = Color.FromArgb(241, 243, 245);
    internal static readonly Color Border = Color.FromArgb(209, 213, 219); // #D1D5DB
    internal static readonly Color Divider = Color.FromArgb(229, 232, 236);
    internal static readonly Color Grid = Color.FromArgb(221, 225, 230);
    internal static readonly Color TextPrimary = Color.FromArgb(31, 41, 55); // #1F2937
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
        // Approved sample direction: clean modern teal, not muted grey-teal.
        CyTheme.Teal => new(
            Color.FromArgb(13, 148, 136),   // #0D9488
            Color.FromArgb(13, 128, 118),
            Color.FromArgb(15, 118, 110),
            Color.FromArgb(209, 242, 235),  // #D1F2EB
            Color.FromArgb(45, 212, 191),
            Color.FromArgb(204, 251, 241)),
        // Coral is intentionally rose-coral/pink so it is visually separate from Apricot and Danger red.
        CyTheme.Coral => new(
            Color.FromArgb(212, 101, 123),
            Color.FromArgb(196, 86, 110),
            Color.FromArgb(173, 72, 94),
            Color.FromArgb(252, 232, 237),
            Color.FromArgb(227, 155, 172),
            Color.FromArgb(247, 220, 227)),
        CyTheme.Apricot => new(
            Color.FromArgb(216, 132, 74),
            Color.FromArgb(195, 115, 63),
            Color.FromArgb(170, 100, 53),
            Color.FromArgb(250, 236, 221),
            Color.FromArgb(224, 161, 120),
            Color.FromArgb(246, 227, 209)),
        // Approved sample direction: #2563EB / #DBEAFE.
        _ => new(
            Color.FromArgb(37, 99, 235),    // #2563EB
            Color.FromArgb(29, 78, 216),
            Color.FromArgb(30, 64, 175),
            Color.FromArgb(219, 234, 254),  // #DBEAFE
            Color.FromArgb(96, 165, 250),
            Color.FromArgb(219, 234, 254)),
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
