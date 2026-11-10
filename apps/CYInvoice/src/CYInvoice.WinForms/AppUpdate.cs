using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace CYInvoice.WinForms;

/// <summary>
/// Downloads a newer Release, checks its SHA-256, unpacks it under Temp and replaces this
/// program's files once it exits (user, 2026-10-17: update by itself, without asking). The
/// files of the package are copied over the program folder; Data (settings, records), Temp
/// and logs are not in the package and stay as they are.
/// </summary>
internal sealed class AppUpdater
{
    private static readonly HttpClient Http = CreateClient();

    private static void Log(string message)
    {
        try
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "log");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "update.log"),
                DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CYInvoice", ApplicationVersion.Read()));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    /// <summary>The program folder of an unpacked newer Release, ready to be put in place; null when there is none.</summary>
    public async Task<(ReleaseOffer Offer, string Folder)?> PrepareAsync(CancellationToken cancellationToken, Action<ReleaseOffer>? found = null)
    {
        var current = Version.Parse(ApplicationVersion.Read());
        var json = await Http.GetStringAsync(UpdateCheck.ReleasesUrl, cancellationToken);
        if (UpdateCheck.Newest(json, current) is not { } offer)
        {
            Log($"no newer release than V{current}");
            return null;
        }
        Log($"newer release V{offer.Version} found; downloading");
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
        var folder = Path.Combine(unpacked, "CYInvoice");
        if (!File.Exists(Path.Combine(folder, "CYInvoice.exe")))
            throw new InvalidDataException("更新檔裡沒有 CYInvoice/CYInvoice.exe");
        if (File.ReadAllText(Path.Combine(folder, "VERSION")).Trim() != offer.Version.ToString(3) ||
            File.ReadAllText(Path.Combine(folder, "BUILD")).Trim() != "0")
            throw new InvalidDataException("ZIP 版號或 BUILD 與正式 Release 不一致");
        // Never copy any path that can overlap installed private runtime information.
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Data", "Cache", "Logs", "log", "Temp", "config", "settings.json",
            "invoices.json", "buyer_names.json", "appsettings.json"
        };
        if (Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(file =>
            forbidden.Contains(Path.GetRelativePath(folder, file).Split(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0])))
            throw new InvalidDataException("更新檔包含禁止覆寫的本機資料路徑");
        Log($"release V{offer.Version} downloaded and verified sha256={actual[..12]}");
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
            Log($"update outcome unreadable: {ex.GetType().Name}: {ex.Message}");
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
        Log($"update V{version.ToString(3)} will be applied after exit restart={restart}");
    }

    // Windows CI uses isolated synthetic files: checks updater actually replaces only executable
    // content and leaves a private Data directory untouched. Never runs against installation data.
    internal static void VerifyInstallerSmoke()
    {
        var root = Path.Combine(Path.GetTempPath(), "cyinvoice-update-smoke-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "installed");
        var work = Path.Combine(root, "work");
        var source = Path.Combine(work, "unpacked", "CYInvoice");
        var outcome = Path.Combine(target, "Temp", "update-result.txt");
        Directory.CreateDirectory(Path.Combine(target, "Data"));
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(work);
        var exe = Path.Combine(target, "CYInvoice.exe");
        var dataFile = Path.Combine(target, "Data", "CYInvoice.db");
        File.WriteAllText(exe, "old binary");
        File.WriteAllText(dataFile, "private synthetic data");
        File.WriteAllText(Path.Combine(source, "CYInvoice.exe"), "new binary");
        File.WriteAllText(Path.Combine(source, "VERSION"), "9.9.9");
        File.WriteAllText(Path.Combine(source, "BUILD"), "0");
        static string Q(string value) => "'" + value.Replace("'", "''") + "'";
        var script = Path.Combine(work, "apply-update-smoke.ps1");
        var body = ApplyScript
            .Replace("@PID@", int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("@SOURCE@", Q(source))
            .Replace("@TARGET@", Q(target))
            .Replace("@EXE@", Q(exe))
            .Replace("@WORK@", Q(work))
            .Replace("@OUTCOME@", Q(outcome))
            .Replace("@PROGRAM@", Q(Program))
            .Replace("@VERSION@", Q("9.9.9"))
            .Replace("@RESTART@", "$false");
        try
        {
            File.WriteAllText(script, body, new UTF8Encoding(true));
            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = work,
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(script);
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Cannot start synthetic update installer");
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                throw new InvalidOperationException("Synthetic update installer timed out");
            }
            if (process.ExitCode != 0 ||
                File.ReadAllText(exe) != "new binary" ||
                File.ReadAllText(dataFile) != "private synthetic data" ||
                !File.Exists(outcome) ||
                UpdateOutcome.Parse(File.ReadAllText(outcome)) is not { Ok: true, Version: "9.9.9" })
                throw new InvalidOperationException("Update installer failed to replace program files or preserve private data");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private const string Program = "CYInvoice";

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
          if ($process -and -not $process.HasExited) { throw "CYInvoice 尚未完全結束，禁止覆寫執行檔" }
          $files = @(Get-ChildItem -LiteralPath $source -Recurse -File)
          $backup = Join-Path $work 'backup'
          $saved = New-Object System.Collections.Generic.List[string]
          $created = New-Object System.Collections.Generic.List[string]
          foreach ($file in $files) {
            $relative = $file.FullName.Substring($source.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar)
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
              $relative = $file.FullName.Substring($source.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar)
              $destination = Join-Path $target $relative
              New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
              if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) { $created.Add($relative) }
              Retry { Copy-Item -LiteralPath $file.FullName -Destination $destination -Force } 30
              $n++; if ($form) { $bar.Value = $n }; Pump
            }
            $ok = $true
          } catch {
            $detail = "複製失敗：$($_.Exception.Message)"
            Say "更新失敗，正在還原原本的版本…"
            try {
              foreach ($relative in $saved) { Retry { Copy-Item -LiteralPath (Join-Path $backup $relative) -Destination (Join-Path $target $relative) -Force } 10 }
              foreach ($relative in $created) { Remove-Item -LiteralPath (Join-Path $target $relative) -Force -ErrorAction SilentlyContinue }
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
