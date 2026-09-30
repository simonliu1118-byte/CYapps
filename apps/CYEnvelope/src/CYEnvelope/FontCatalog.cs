using System.IO;
using System.Windows;
using System.Windows.Media;

namespace CYEnvelope;

// ResourceFile: font bundled in the program (downloaded at build time, see tools/fonts.json); null = installed in Windows.
public sealed record FontChoice(string Family, string Display, string? ResourceFile);

public static class FontCatalog
{
    public const string Default = "DFKai-SB";

    public static readonly IReadOnlyList<FontChoice> Choices =
    [
        new("DFKai-SB", "標楷體", null),
        new("PMingLiU", "新細明體", null),
        new("Noto Sans TC", "思源黑體（內建）", "NotoSansTC-Regular.otf"),
        new("Noto Serif TC", "思源宋體（內建）", "NotoSerifTC-Regular.otf"),
        new("LXGW WenKai TC", "霞鶩文楷（內建，楷書風）", "LXGWWenKaiTC-Regular.ttf")
    ];

    // The family chosen in Settings; it is used for every envelope text (recipient, address, phone,
    // postal code, frame text). Null keeps each text's own font name (unit checks, older data).
    public static string? Selected { get; set; }

    // Only built when a bundled font is really used: the pack:// scheme is registered by WPF's Application,
    // which the plain core checks do not have, so touch PackUriHelper first to register it.
    private static Uri BundledFolder()
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        return new Uri("pack://application:,,,/CYEnvelope;component/Fonts/");
    }
    private static readonly Dictionary<string, Typeface> Cache = [];

    // useSelected false: decorative marks (the tick) that must not follow the chosen font.
    public static Typeface Typeface(string family, bool useSelected = true)
    {
        var name = useSelected ? Selected ?? family : family;
        if (Cache.TryGetValue(name, out var cached)) return cached;
        var choice = Choices.FirstOrDefault(c => c.Family == name);
        var fontFamily = choice?.ResourceFile is not null ? new FontFamily(BundledFolder(), "./#" + name) : new FontFamily(name);
        return Cache[name] = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    }

    // Bundled fonts are missing in builds made without tools/fetch-fonts.ps1; system fonts may not be installed.
    public static bool IsAvailable(FontChoice choice)
    {
        try
        {
            if (choice.ResourceFile is not null)
                return Application.GetResourceStream(new Uri(BundledFolder(), choice.ResourceFile)) is not null;
            return Fonts.SystemFontFamilies.Any(f => string.Equals(f.Source, choice.Family, StringComparison.OrdinalIgnoreCase) ||
                f.FamilyNames.Values.Any(n => string.Equals(n, choice.Family, StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception error) when (error is IOException or UriFormatException or InvalidOperationException)
        {
            return false;
        }
    }
}
