using System.Text.Json;
using System.Text.RegularExpressions;

namespace CYInvoice.WinForms;

/// <summary>A formal CYInvoice Release on GitHub: its version and the zip／checksum download links.</summary>
internal sealed record ReleaseOffer(Version Version, string Tag, string ZipName, string ZipUrl, string ChecksumUrl);

/// <summary>
/// Which GitHub Release is newer than this program (user, 2026-10-17: the program looks for a
/// newer formal Release itself; Actions artifacts do not count). Only this project's tags
/// (cyinvoice-vX.Y.Z), not drafts or pre-releases, with both the zip and its .sha256.
/// Pure: no network, unit tested.
/// </summary>
internal static class UpdateCheck
{
    public const string Repository = "simonliu1118-byte/CYapps";
    public const string TagPrefix = "cyinvoice-v";

    private static readonly Regex Tag = new(@"^cyinvoice-v(\d+)\.(\d+)\.(\d+)$", RegexOptions.CultureInvariant);

    public static string ReleasesUrl => $"https://api.github.com/repos/{Repository}/releases?per_page=50";

    /// <summary>
    /// The newest Release above <paramref name="current"/>, or null. A Build of a version
    /// (V0.3.1 Build 2) comes after that version's Release, so only a higher X.Y.Z is newer.
    /// </summary>
    public static ReleaseOffer? Newest(string releasesJson, Version current)
    {
        using var document = JsonDocument.Parse(releasesJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
        ReleaseOffer? best = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (Flag(release, "draft") || Flag(release, "prerelease")) continue;
            var tag = Text(release, "tag_name");
            var match = Tag.Match(tag);
            if (!match.Success) continue;
            var version = new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
            if (version <= current || (best is not null && version <= best.Version)) continue;
            var zipName = $"CYInvoice_V{version}.zip";
            string zipUrl = string.Empty, checksumUrl = string.Empty;
            if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = Text(asset, "name");
                    var url = Text(asset, "browser_download_url");
                    if (name == zipName) zipUrl = url;
                    else if (name == zipName + ".sha256") checksumUrl = url;
                }
            if (!IsReleaseDownload(zipUrl, tag) || !IsReleaseDownload(checksumUrl, tag)) continue;
            best = new ReleaseOffer(version, tag, zipName, zipUrl, checksumUrl);
        }
        return best;
    }

    internal static void VerifySmokeCases()
    {
        const string releases = """
            [
              {
                "tag_name":"cyinvoice-v3.1.50","draft":false,"prerelease":false,
                "assets":[
                  {"name":"CYInvoice_V3.1.50.zip","browser_download_url":"https://github.com/simonliu1118-byte/CYapps/releases/download/cyinvoice-v3.1.50/CYInvoice_V3.1.50.zip"},
                  {"name":"CYInvoice_V3.1.50.zip.sha256","browser_download_url":"https://github.com/simonliu1118-byte/CYapps/releases/download/cyinvoice-v3.1.50/CYInvoice_V3.1.50.zip.sha256"}
                ]
              },
              {"tag_name":"cyerp-auto-input-v9.9.9","draft":false,"prerelease":false,"assets":[]},
              {"tag_name":"cyinvoice-v3.1.51","draft":false,"prerelease":true,"assets":[]},
              {"tag_name":"cyinvoice-v3.1.52","draft":true,"prerelease":false,"assets":[]},
              {"tag_name":"cyinvoice-v3.1.53","draft":false,"prerelease":false,"assets":[{"name":"CYInvoice_V3.1.53.zip","browser_download_url":"https://example.org/invalid.zip"}]}
            ]
            """;
        var offer = Newest(releases, new Version(3, 1, 49));
        if (offer?.Version != new Version(3, 1, 50) ||
            Newest(releases, new Version(3, 1, 50)) is not null)
            throw new InvalidOperationException("自動更新必須只選擇較新的 CYInvoice 正式 ZIP 與檢查碼");
        const string hash = "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef";
        if (ExpectedSha256(hash + "  CYInvoice_V3.1.50.zip", "CYInvoice_V3.1.50.zip") != hash ||
            ExpectedSha256(hash + "  other.zip", "CYInvoice_V3.1.50.zip") is not null)
            throw new InvalidOperationException("自動更新 SHA-256 格式驗證失敗");
        if (UpdateOutcome.Parse("ok\\n3.1.50\\n") is not { Ok: true, Version: "3.1.50" } ||
            UpdateOutcome.Parse("not-a-status\\n3.1.50") is not null)
            throw new InvalidOperationException("自動更新回復狀態解析失敗");
    }

    /// <summary>Only this repository's own Release downloads for that tag are fetched.</summary>
    private static bool IsReleaseDownload(string url, string tag) =>
        url.StartsWith($"https://github.com/{Repository}/releases/download/{tag}/", StringComparison.Ordinal);

    /// <summary>The hash in a 「hash  name」 checksum file for <paramref name="zipName"/>, or null.</summary>
    public static string? ExpectedSha256(string checksumText, string zipName)
    {
        foreach (var line in checksumText.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*') == zipName && Regex.IsMatch(parts[0], "^[0-9a-fA-F]{64}$"))
                return parts[0].ToLowerInvariant();
        }
        return null;
    }

    private static bool Flag(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    private static string Text(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
}

/// <summary>What the update script left for the next start: put in place or not, and why.</summary>
internal sealed record UpdateOutcome(bool Ok, string Version, string Detail)
{
    /// <summary>Releases page for a manual update.</summary>
    public static string ReleasesPage => $"https://github.com/{UpdateCheck.Repository}/releases";

    /// <summary>Lines 「ok｜failed」, version, detail; null for anything else.</summary>
    public static UpdateOutcome? Parse(string text)
    {
        var lines = text.Replace("\r", string.Empty).Split('\n');
        if (lines.Length < 2 || lines[1].Trim().Length == 0) return null;
        var state = lines[0].Trim().TrimStart('﻿');
        if (state != "ok" && state != "failed") return null;
        return new UpdateOutcome(state == "ok", lines[1].Trim(), string.Join(" ", lines.Skip(2).Select(l => l.Trim()).Where(l => l.Length > 0)));
    }
}
