using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace CYERPAutoQuery;

/// <summary>
/// Downloads a newer Release, checks its SHA-256, unpacks it under Temp and replaces this
/// program's files once it exits (user, 2026-10-17: the same automatic update as CYERPAutoInput;
/// copied from CYERPAutoInput AppUpdate.cs). The files of the package are copied over the program
/// folder; config (Web 連線), TEMP and logs are not in the package and stay as they are.
/// </summary>
internal sealed class AppUpdater(AppLogger log)
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CYERPAutoQuery", AppVersionInfo.Version));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    /// <summary>The program folder of an unpacked newer Release, ready to be put in place; null when there is none.</summary>
    public async Task<(ReleaseOffer Offer, string Folder)?> PrepareAsync(CancellationToken cancellationToken)
    {
        var current = Version.Parse(AppVersionInfo.Version);
        var json = await Http.GetStringAsync(UpdateCheck.ReleasesUrl, cancellationToken);
        if (UpdateCheck.Newest(json, current) is not { } offer)
        {
            log.Info("update", $"no newer release than V{current}");
            return null;
        }
        log.Info("update", $"newer release V{offer.Version} found; downloading");

        var work = Path.Combine(AppContext.BaseDirectory, "Temp", $"update-V{offer.Version}");
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        var zip = Path.Combine(work, offer.ZipName);
        var checksum = await Http.GetStringAsync(offer.ChecksumUrl, cancellationToken);
        var expected = UpdateCheck.ExpectedSha256(checksum, offer.ZipName)
            ?? throw new InvalidDataException("Release 的 SHA-256 檔讀不到這個 zip 的雜湊");
        await using (var source = await Http.GetStreamAsync(offer.ZipUrl, cancellationToken))
        await using (var target = File.Create(zip))
            await source.CopyToAsync(target, cancellationToken);
        string actual;
        await using (var stream = File.OpenRead(zip))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        if (actual != expected) throw new InvalidDataException($"下載的更新檔 SHA-256 不符（應為 {expected[..12]}…，實際 {actual[..12]}…）");

        var unpacked = Path.Combine(work, "unpacked");
        ZipFile.ExtractToDirectory(zip, unpacked);
        var folder = Path.Combine(unpacked, "CYERPAutoQuery");
        if (!File.Exists(Path.Combine(folder, "CYERPAutoQuery.exe")))
            throw new InvalidDataException("更新檔裡沒有 CYERPAutoQuery/CYERPAutoQuery.exe");
        log.Info("update", $"release V{offer.Version} downloaded and verified sha256={actual[..12]}");
        return (offer, folder);
    }

    /// <summary>
    /// Starts a hidden PowerShell that waits for this process to end, copies
    /// <paramref name="folder"/> over the program folder (retrying while files are locked) and,
    /// with <paramref name="restart"/>, starts the new version. The caller then closes the program.
    /// </summary>
    public void ApplyAfterExit(string folder, bool restart)
    {
        var target = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var exe = Environment.ProcessPath ?? Path.Combine(target, "CYERPAutoQuery.exe");
        var work = Path.GetDirectoryName(Path.GetDirectoryName(folder)!)!;
        var script = Path.Combine(work, "apply-update.ps1");
        static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
        File.WriteAllText(script, string.Join("\r\n",
            "$ErrorActionPreference = 'Stop'",
            $"$pidToWait = {Environment.ProcessId}",
            $"$source = {Quote(folder)}",
            $"$target = {Quote(target)}",
            $"$exe = {Quote(exe)}",
            $"$work = {Quote(work)}",
            "try { Wait-Process -Id $pidToWait -Timeout 120 -ErrorAction SilentlyContinue } catch { }",
            "$done = $false",
            "for ($i = 0; $i -lt 30 -and -not $done; $i++) {",
            "  try { Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force; $done = $true }",
            "  catch { Start-Sleep -Seconds 1 }",
            "}",
            $"if ($done -and {(restart ? "$true" : "$false")}) {{ Start-Process -FilePath $exe -WorkingDirectory $target }}",
            "if ($done) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }"), new UTF8Encoding(true));
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = work
        });
        log.Info("update", $"update will be applied after exit restart={restart}");
    }
}
