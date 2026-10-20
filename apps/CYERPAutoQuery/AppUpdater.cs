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
    public async Task<(ReleaseOffer Offer, string Folder)?> PrepareAsync(CancellationToken cancellationToken, Action<ReleaseOffer>? found = null)
    {
        var current = Version.Parse(AppVersionInfo.Version);
        var json = await Http.GetStringAsync(UpdateCheck.ReleasesUrl, cancellationToken);
        if (UpdateCheck.Newest(json, current) is not { } offer)
        {
            log.Info("update", $"no newer release than V{current}");
            return null;
        }
        log.Info("update", $"newer release V{offer.Version} found; downloading");
        found?.Invoke(offer);

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

    /// <summary>Where the update script leaves its outcome for the next start (Temp is not in the package).</summary>
    public static string OutcomePath => Path.Combine(AppContext.BaseDirectory, "Temp", "update-result.txt");

    /// <summary>The outcome of the last update, once: the file is removed when read.</summary>
    public UpdateOutcome? TakeOutcome()
    {
        try
        {
            if (!File.Exists(OutcomePath)) return null;
            var outcome = UpdateOutcome.Parse(File.ReadAllText(OutcomePath));
            File.Delete(OutcomePath);
            return outcome;
        }
        catch (Exception ex)
        {
            log.Warn("update", $"update outcome unreadable: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Starts a PowerShell that shows 「正在更新」 (user, 2026-10-20: the person at the computer must
    /// see it), waits for this process to end, backs up the files the package replaces, copies
    /// <paramref name="folder"/> over the program folder (retrying while files are locked) and
    /// leaves the outcome in <see cref="OutcomePath"/>. When a file cannot be copied the backup is
    /// put back, so the old version still runs (user, 2026-10-20: open it anyway and say so). With
    /// <paramref name="restart"/> the program is started again either way. The caller then closes the program.
    /// </summary>
    public void ApplyAfterExit(string folder, Version version, bool restart)
    {
        var target = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var exe = Environment.ProcessPath ?? Path.Combine(target, Program + ".exe");
        var work = Path.GetDirectoryName(Path.GetDirectoryName(folder)!)!;
        var script = Path.Combine(work, "apply-update.ps1");
        static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
        File.WriteAllText(script, ApplyScript
            .Replace("@PID@", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("@SOURCE@", Quote(folder))
            .Replace("@TARGET@", Quote(target))
            .Replace("@EXE@", Quote(exe))
            .Replace("@WORK@", Quote(work))
            .Replace("@OUTCOME@", Quote(OutcomePath))
            .Replace("@PROGRAM@", Quote(Program))
            .Replace("@VERSION@", Quote(version.ToString(3)))
            .Replace("@RESTART@", restart ? "$true" : "$false")
            .Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(true));
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = work
        });
        log.Info("update", $"update V{version.ToString(3)} will be applied after exit restart={restart}");
    }

    private const string Program = "CYERPAutoQuery";

    /// <summary>The script <see cref="ApplyAfterExit"/> runs; @NAME@ are filled in, quoted.</summary>
    private const string ApplyScript = """
        $ErrorActionPreference = 'Stop'
        $pidToWait = @PID@
        $source = @SOURCE@
        $target = @TARGET@
        $exe = @EXE@
        $work = @WORK@
        $outcome = @OUTCOME@
        $program = @PROGRAM@
        $version = @VERSION@
        $restart = @RESTART@
        $form = $null
        function Pump { if ($form) { [System.Windows.Forms.Application]::DoEvents() } }
        function Say([string]$text) { if ($form) { $label.Text = $text; Pump } }
        function WaitMs([int]$ms) { $until = (Get-Date).AddMilliseconds($ms); while ((Get-Date) -lt $until) { Pump; Start-Sleep -Milliseconds 40 } }
        function Retry([scriptblock]$action, [int]$times) {
          for ($i = 1; $i -le $times; $i++) { try { & $action; return } catch { if ($i -eq $times) { throw }; WaitMs 1000 } }
        }
        try {
          Add-Type -AssemblyName System.Windows.Forms, System.Drawing
          [System.Windows.Forms.Application]::EnableVisualStyles()
          $form = New-Object System.Windows.Forms.Form
          $form.Text = "$program 更新"
          $form.FormBorderStyle = 'FixedDialog'; $form.ControlBox = $false; $form.TopMost = $true
          $form.StartPosition = 'CenterScreen'; $form.ShowInTaskbar = $true
          $form.ClientSize = New-Object System.Drawing.Size(440, 130)
          $form.Font = New-Object System.Drawing.Font('Microsoft JhengHei UI', 10)
          $form.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
          $label = New-Object System.Windows.Forms.Label
          $label.SetBounds(22, 18, 396, 50)
          $bar = New-Object System.Windows.Forms.ProgressBar
          $bar.SetBounds(22, 80, 396, 18); $bar.Style = 'Marquee'; $bar.MarqueeAnimationSpeed = 30
          $form.Controls.AddRange(@($label, $bar))
          $label.Text = "正在更新 $program 到 V$version，請稍候…`n請不要關機或開啟本程式。"
          $form.Show(); $form.Activate(); Pump
        } catch { $form = $null }

        $ok = $false; $detail = ''
        try {
          $process = Get-Process -Id $pidToWait -ErrorAction SilentlyContinue
          $deadline = (Get-Date).AddSeconds(120)
          while ($process -and -not $process.HasExited -and (Get-Date) -lt $deadline) { Pump; Start-Sleep -Milliseconds 40 }
          $files = @(Get-ChildItem -LiteralPath $source -Recurse -File)
          $backup = Join-Path $work 'backup'
          $saved = New-Object System.Collections.Generic.List[string]
          foreach ($file in $files) {
            $relative = $file.FullName.Substring($source.Length).TrimStart('')
            $current = Join-Path $target $relative
            if (Test-Path -LiteralPath $current -PathType Leaf) {
              $copy = Join-Path $backup $relative
              New-Item -ItemType Directory -Force -Path (Split-Path $copy) | Out-Null
              Retry { Copy-Item -LiteralPath $current -Destination $copy -Force } 10
              $saved.Add($relative)
            }
            Pump
          }
          if ($form) { $bar.Style = 'Continuous'; $bar.Maximum = [Math]::Max(1, $files.Count); $bar.Value = 0 }
          try {
            $n = 0
            foreach ($file in $files) {
              $relative = $file.FullName.Substring($source.Length).TrimStart('')
              $destination = Join-Path $target $relative
              New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
              Retry { Copy-Item -LiteralPath $file.FullName -Destination $destination -Force } 30
              $n++; if ($form) { $bar.Value = $n }; Pump
            }
            $ok = $true
          } catch {
            $detail = "複製失敗：$($_.Exception.Message)"
            Say "更新失敗，正在還原原本的版本…"
            try {
              foreach ($relative in $saved) { Retry { Copy-Item -LiteralPath (Join-Path $backup $relative) -Destination (Join-Path $target $relative) -Force } 10 }
              $detail += "；已還原原本的版本"
            } catch { $detail += "；還原也失敗：$($_.Exception.Message)" }
          }
        } catch {
          if (-not $detail) { $detail = "更新前準備失敗：$($_.Exception.Message)" }
        }
        try {
          New-Item -ItemType Directory -Force -Path (Split-Path $outcome) | Out-Null
          Set-Content -LiteralPath $outcome -Value @($(if ($ok) { 'ok' } else { 'failed' }), $version, $detail) -Encoding UTF8
        } catch { }
        if ($ok) { Say "更新完成，正在開啟 $program V$version…"; WaitMs 1200 }
        else { Say "更新失敗，正在開啟原本的版本…`n可能需要手動更新。"; WaitMs 3000 }
        if ($restart) { try { Start-Process -FilePath $exe -WorkingDirectory $target } catch { } }
        if ($form) { $form.Close() }
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
        """;
}
