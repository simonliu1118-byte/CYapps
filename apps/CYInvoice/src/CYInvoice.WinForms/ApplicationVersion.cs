namespace CYInvoice.WinForms;

internal static class ApplicationVersion
{
    public static string Read()
    {
        var value = ReadFile("VERSION");
        return Version.TryParse(value, out _) ? value : "未提供";
    }

    public static string ReadDisplay()
    {
        var version = Read();
        return int.TryParse(ReadFile("BUILD"), out var build) && build > 0
            ? $"{version} Build {build}" : version;
    }

    public static string FormatClientVersion(string? value)
    {
        var display = (value ?? string.Empty).Split('+', 2)[0].Trim().TrimStart('V', 'v');
        var parts = display.Split(" Build ", StringSplitOptions.None);
        if (!Version.TryParse(parts[0], out _) || parts.Length > 2) return "未提供";
        if (parts.Length == 1) return $"V{parts[0]}";
        return int.TryParse(parts[1], out var build) && build >= 0
            ? $"V{parts[0]}" + (build > 0 ? $" Build {build}" : string.Empty) : "未提供";
    }

    private static string ReadFile(string name)
    {
        try { return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name)).Trim(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return string.Empty; }
    }

    internal static void VerifySmokeVersion()
    {
        foreach (var (raw, expected) in new[] {
            ("2.6.11+abcdef0123456789", "V2.6.11"), ("V2.6.10 Build 2", "V2.6.10 Build 2"),
            ("2.6.13", "V2.6.13"), ("2.6.13 Build 0", "V2.6.13"), ("", "未提供"),
            ("invalid", "未提供"), ("2.6.13 Build invalid", "未提供") })
            if (FormatClientVersion(raw) != expected) throw new InvalidOperationException("加入時版本格式錯誤");
        if (!Version.TryParse(Read(), out _) || !int.TryParse(ReadFile("BUILD"), out var build) || build < 0)
            throw new InvalidOperationException("測試包缺少有效 VERSION / BUILD");
    }
}
