using System.Reflection;

namespace CYERPAutoInput;

internal static class AppVersionInfo
{
    private static readonly Lazy<VersionStamp> Current = new(Load);

    public static string Version => Current.Value.Version;
    public static int Build => Current.Value.Build;
    public static string Display => Build > 0 ? $"V{Version} Build {Build}" : $"V{Version}";

    private static VersionStamp Load()
    {
        var version = ReadResource("CYERPAutoInput.VERSION").Trim();
        var buildText = ReadResource("CYERPAutoInput.BUILD").Trim();

        if (!System.Version.TryParse(version, out var parsedVersion) || parsedVersion is null)
            throw new InvalidOperationException("Embedded VERSION is invalid.");
        if (!int.TryParse(buildText, out var build) || build < 0)
            throw new InvalidOperationException("Embedded BUILD is invalid.");

        return new VersionStamp(version, build);
    }

    private static string ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded version resource: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record VersionStamp(string Version, int Build);
}
