using System.Text.Json;
using System.Text.RegularExpressions;

namespace CYERPAutoQuery;

/// <summary>A formal CYERPAutoQuery Release on GitHub: its version and the zip／checksum download links.</summary>
internal sealed record ReleaseOffer(Version Version, string Tag, string ZipName, string ZipUrl, string ChecksumUrl);

/// <summary>
/// Which GitHub Release is newer than this program (user, 2026-10-17: the same automatic update
/// as CYERPAutoInput; copied from CYERPAutoInput UpdateCheck.cs). Actions artifacts do not count.
/// Only this project's tags
/// (cyerp-auto-query-vX.Y.Z), not drafts or pre-releases, with both the zip and its .sha256.
/// Pure: no network, unit tested.
/// </summary>
internal static class UpdateCheck
{
    public const string Repository = "simonliu1118-byte/CYapps";
    public const string TagPrefix = "cyerp-auto-query-v";

    private static readonly Regex Tag = new(@"^cyerp-auto-query-v(\d+)\.(\d+)\.(\d+)$", RegexOptions.CultureInvariant);

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
            var zipName = $"CYERPAutoQuery_V{version}.zip";
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
